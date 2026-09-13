using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Styx.WoWInternals;

namespace Honorbuddy5875.Runtime;

internal enum QuestDialogStage : uint { Greeting = 0, Details = 1, Progress = 2, Reward = 3 }
internal sealed record DialogQuest(uint Id, uint Level, uint Status, string Title);
internal sealed record QuestDialogSnapshot(ulong NpcGuid, uint RawStage, uint QuestId, string Title,
    uint RequestPending, DialogQuest[] Active, DialogQuest[] Available, ulong GossipNpcGuid, DialogQuest[] GossipQuests)
{
    internal RewardOffer? Reward { get; init; }
    internal bool Closed => NpcGuid == 0 && GossipNpcGuid == 0;
    internal bool Matches(uint questId, ulong npcGuid) => npcGuid != 0 && questId != 0 &&
        ((NpcGuid == npcGuid && GossipNpcGuid == 0 &&
            ((RawStage is >= 1 and <= 3 && QuestId == questId) ||
             (RawStage == 0 && Active.Any(q => q.Id == questId)))) ||
         (GossipNpcGuid == npcGuid && NpcGuid == 0 && GossipQuests.Any(q => q.Id == questId && q.Status is 3 or 4)));
}

// Addresses are derived from the supplied, hashed 5875 executable; see TurnInStep7/REVERSE-ENGINEERING.md.
// These are client conversation data, not a claim about the rendered Lua frame's IsVisible state.
internal sealed class Vanilla5875QuestDialog : IDisposable
{
    internal const uint QuestStart = 0xBDE068, QuestEnd = 0xBE0848;
    internal const uint GossipStart = 0xBB74C0, GossipEnd = GossipStart + 32 * 0x20C;
    private const uint GossipGuid = 0xBC3F58, MouseoverGuid = 0xB4E2C8;
    private const string SupportedSha256 = "B4756D38EF207C02ED651F4952BD89A70B4857B73A33413339E1B285B28D2DC7";
    private readonly LinuxProcessMemory _memory;
    private readonly ulong _module;
    private readonly int _pid;
    internal static Vanilla5875QuestDialog? Current { get; private set; }

    internal Vanilla5875QuestDialog()
    {
        _pid = ObjectManager.WoWProcess?.Id ?? throw new InvalidOperationException("No WoW process.");
        _module = ObjectManager.ModuleBase;
        _memory = new LinuxProcessMemory(_pid);
        // File hash plus live code anchors: do not use these globals on an unrecognized client.
        string? executable = File.ReadLines($"/proc/{_pid}/maps")
            .Select(line => line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 6)
            .Select(parts => System.Text.RegularExpressions.Regex.Replace(parts[5], @"\\([0-7]{3})",
                match => ((char)Convert.ToInt32(match.Groups[1].Value, 8)).ToString()))
            .FirstOrDefault(path => Path.GetFileName(path).Equals("WoW.exe", StringComparison.OrdinalIgnoreCase));
        if (executable is null) throw new InvalidOperationException("Cannot locate mapped WoW.exe for build verification.");
        using var stream = File.OpenRead(executable);
        if (Convert.ToHexString(SHA256.HashData(stream)) != SupportedSha256)
            throw new InvalidOperationException("Quest dialog offsets require the supplied WoW.exe SHA-256; executable differs.");
        VerifyPointerInstruction(0x501A20, 0xBA, 0xBDF900); // GetTitleText: mov edx, title
        VerifyPointerInstruction(0x515BDB, 0x3D8B, MouseoverGuid); // mouseover unit resolution: mov edi,[guid]
        VerifyPointerInstruction(0x4E1FA0, 0x158B, GossipGuid); // gossip session setter: mov edx,[guid]
        if (Current is not null) throw new InvalidOperationException("A quest dialog reader is already active.");
        Current = this;
    }

    private ulong Address(uint va) => checked(_module + va - 0x400000);
    private void VerifyPointerInstruction(uint instruction, uint opcode, uint pointer)
    {
        int prefix = opcode <= 255 ? 1 : 2;
        var expected = new byte[prefix + 4];
        expected[0] = (byte)opcode;
        if (prefix == 2) expected[1] = (byte)(opcode >> 8);
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(prefix), checked((uint)Address(pointer)));
        if (!ReadBlock(instruction, expected.Length).AsSpan().SequenceEqual(expected))
            throw new InvalidOperationException($"Live client code differs at 0x{instruction:X8}.");
    }
    private void RequireSession()
    {
        if (Current != this || ObjectManager.WoWProcess?.Id != _pid || ObjectManager.ModuleBase != _module || !ObjectManager.IsInGame)
            throw new InvalidOperationException("The validated client session is no longer active.");
    }
    private byte[] ReadBlock(uint address, int length)
    {
        var bytes = new byte[length];
        _memory.ReadBytes(Address(address), bytes);
        return bytes;
    }
    internal ulong ReadMouseover()
    {
        RequireSession();
        ulong first = _memory.Read<ulong>(Address(MouseoverGuid));
        if (first != _memory.Read<ulong>(Address(MouseoverGuid)))
            throw new InvalidOperationException("Mouseover changed while reading.");
        return first;
    }
    internal QuestDialogSnapshot Read()
    {
        RequireSession();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ulong gossip = _memory.Read<ulong>(Address(GossipGuid));
            byte[] quest = ReadBlock(QuestStart, checked((int)(QuestEnd - QuestStart)));
            byte[] list = ReadBlock(GossipStart, checked((int)(GossipEnd - GossipStart)));
            byte[] questAgain = ReadBlock(QuestStart, quest.Length);
            byte[] listAgain = ReadBlock(GossipStart, list.Length);
            if (gossip == _memory.Read<ulong>(Address(GossipGuid)) &&
                quest.AsSpan().SequenceEqual(questAgain) && list.AsSpan().SequenceEqual(listAgain))
                return Decode(quest, gossip, list);
        }
        throw new InvalidOperationException("Quest dialog changed during the read; retry after the UI settles.");
    }
    internal static QuestDialogSnapshot Decode(byte[] quest, ulong gossip, byte[] list)
    {
        if (quest.Length != QuestEnd - QuestStart || list.Length != GossipEnd - GossipStart)
            throw new InvalidDataException("Invalid quest dialog buffer lengths.");
        uint U(uint va) => BinaryPrimitives.ReadUInt32LittleEndian(quest.AsSpan(checked((int)(va - QuestStart))));
        ulong npc = BinaryPrimitives.ReadUInt64LittleEndian(quest.AsSpan(checked((int)(0xBE0810 - QuestStart))));
        uint stage = npc == 0 ? 0 : U(0xBE0818);
        if (npc != 0 && stage > 3) throw new InvalidDataException($"Unknown quest dialog mode {stage}.");
        DialogQuest[] Entries(uint address, uint count)
        {
            if (count > 32) throw new InvalidDataException("Quest greeting count exceeds 32.");
            var result = new List<DialogQuest>();
            for (uint i = 0; i < count; i++)
            {
                uint va = address + i * 0x4C;
                uint id = U(va);
                if (id == 0) throw new InvalidDataException("Empty quest in a populated greeting list.");
                result.Add(new(id, U(va + 4), U(va + 0x48), Text(quest, checked((int)(va + 8 - QuestStart)), 64)));
            }
            return result.ToArray();
        }
        var gossipEntries = new List<DialogQuest>();
        if (gossip != 0)
            for (int i = 0; i < 32; i++)
            {
                int offset = i * 0x20C;
                uint id = BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(offset));
                if (id == 0) break;
                gossipEntries.Add(new(id, BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(offset + 4)),
                    BinaryPrimitives.ReadUInt32LittleEndian(list.AsSpan(offset + 8)), Text(list, offset + 12, 512)));
            }
        return new(npc, stage, npc != 0 && stage != 0 ? U(0xBE081C) : 0,
            npc != 0 && stage != 0 ? Text(quest, checked((int)(0xBDF900 - QuestStart)), 64) : "",
            npc == 0 ? 0 : U(0xBE0844),
            npc != 0 && stage == 0 ? Entries(0xBDE690, U(0xBE0838)) : [],
            npc != 0 && stage == 0 ? Entries(0xBDFE60, U(0xBE0834)) : [], gossip, gossipEntries.ToArray())
        { Reward = npc != 0 && stage == 3 ? RewardOffer.Decode(quest) : null };
    }
    private static string Text(byte[] buffer, int offset, int capacity)
    {
        var bytes = buffer.AsSpan(offset, capacity);
        int end = bytes.IndexOf((byte)0);
        if (end < 0) throw new InvalidDataException("Unterminated quest dialog string.");
        return Encoding.UTF8.GetString(bytes[..end]);
    }
    public void Dispose()
    {
        if (Current == this) Current = null;
        _memory.Dispose();
    }
}
