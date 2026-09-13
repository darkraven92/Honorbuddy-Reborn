using System.Buffers.Binary;
using Styx;
using Styx.WoWInternals;

namespace Honorbuddy5875.Runtime;

internal sealed record RewardItem(uint Id, uint Count);
internal sealed record RewardOffer(RewardItem[] FixedItems, RewardItem[] Choices, uint Money, uint Spell)
{
    internal bool SameAs(RewardOffer? other) => other is not null && Money == other.Money && Spell == other.Spell &&
        FixedItems.SequenceEqual(other.FixedItems) && Choices.SequenceEqual(other.Choices);

    internal static RewardOffer Decode(byte[] block)
    {
        if (block.Length != Vanilla5875QuestDialog.QuestEnd - Vanilla5875QuestDialog.QuestStart)
            throw new InvalidDataException("Invalid reward buffer length.");
        uint U(uint va) => BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(checked((int)(va - Vanilla5875QuestDialog.QuestStart))));
        RewardItem[] Items(uint start)
        {
            var result = new List<RewardItem>();
            // Native GetNumQuestRewards/GetNumQuestChoices stop at the first zero ID.
            for (uint i = 0; i < 6; i++)
            {
                uint id = U(start + i * 0x24);
                if (id == 0) break;
                uint count = U(start + i * 0x24 + 4);
                if (count == 0) throw new InvalidDataException("Reward item has zero count.");
                result.Add(new(id, count));
            }
            return result.ToArray();
        }
        return new(Items(0xBDF020), Items(0xBDF02C), U(0xBE082C), U(0xBE0830));
    }
}

internal sealed record RewardPlayerState(int Pid, ulong Guid, uint Level, uint Xp, uint NextXp,
    uint[] ActiveIds, bool QuestCompleted, bool QuestFailed)
{
    internal bool SameAs(RewardPlayerState b) => Pid == b.Pid && Guid == b.Guid && Level == b.Level && Xp == b.Xp &&
        NextXp == b.NextXp && ActiveIds.SequenceEqual(b.ActiveIds) && QuestCompleted == b.QuestCompleted && QuestFailed == b.QuestFailed;

    internal void RequireValid()
    {
        string? reason = Pid <= 0 || Guid == 0 ? "invalid process/player identity" :
            Level is 0 or >= 60 ? "level must be between 1 and 59 for this XP-confirmation probe" :
            NextXp == 0 ? "next-level XP is zero" : Xp >= NextXp ? "current XP is not below next-level XP" :
            ActiveIds.Length > 20 || ActiveIds.Any(id => id == 0) || ActiveIds.Distinct().Count() != ActiveIds.Length
                ? "invalid or duplicated quest IDs" : null;
        if (reason is not null)
            throw new InvalidOperationException($"Invalid reward snapshot: {reason}; PID={Pid}; GUID=0x{Guid:X16}; " +
                $"level={Level}; XP={Xp}/{NextXp}; quests=[{string.Join(',', ActiveIds)}].");
    }
}

// Native UnitXP/UnitXPMax use player + 0xE68, which points to descriptor + 0x2F0.
// The function-local offsets 0x840/0x844 therefore mean full-descriptor 0xB30/0xB34.
// The dialog reader has already checked the exact client hash and live code anchors.
internal sealed class Vanilla5875RewardState : IDisposable
{
    internal const int PlayerFieldsOffset = 0x2F0;
    internal const int XpOffset = PlayerFieldsOffset + 0x840;
    internal const int NextXpOffset = PlayerFieldsOffset + 0x844;
    internal const int SnapshotLength = NextXpOffset + 4;
    private readonly int _pid;
    private readonly ulong _guid;
    private readonly LinuxProcessMemory _memory;
    internal Vanilla5875RewardState()
    {
        if (Vanilla5875QuestDialog.Current is null) throw new InvalidOperationException("Validated quest dialog reader required.");
        _pid = ObjectManager.WoWProcess?.Id ?? throw new InvalidOperationException("No process.");
        _guid = ObjectManager.LocalGuid;
        _memory = new(_pid);
    }
    internal RewardPlayerState Read(uint quest)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!ObjectManager.IsInGame || ObjectManager.WoWProcess?.Id != _pid || ObjectManager.LocalGuid != _guid)
                throw new InvalidOperationException("Player session changed.");
            uint player = StyxWoW.Me!.BaseAddress;
            uint descriptor = ObjectManager.ReadPointer32((ulong)player + 8);
            if (!ObjectManager.IsPlausiblePointer(descriptor)) throw new InvalidDataException("Invalid player descriptor.");
            uint nativePlayerFields = ObjectManager.ReadPointer32((ulong)player + 0xE68);
            if (nativePlayerFields != checked(descriptor + PlayerFieldsOffset))
                throw new InvalidDataException($"Player field pointer mismatch: descriptor=0x{descriptor:X8}; playerFields=0x{nativePlayerFields:X8}; expected descriptor+0x2F0.");
            byte[] a = new byte[SnapshotLength], b = new byte[SnapshotLength];
            _memory.ReadBytes(descriptor, a); _memory.ReadBytes(descriptor, b);
            if (ObjectManager.ReadPointer32((ulong)player + 8) != descriptor ||
                ObjectManager.ReadPointer32((ulong)player + 0xE68) != nativePlayerFields) continue;
            var first = Decode(a, _pid, _guid, quest); var second = Decode(b, _pid, _guid, quest);
            if (first.SameAs(second) && BinaryPrimitives.ReadUInt64LittleEndian(b) == _guid)
            { second.RequireValid(); return second; }
        }
        throw new InvalidOperationException("Player XP/quest log changed during the read.");
    }
    internal static RewardPlayerState Decode(byte[] bytes, int pid, ulong guid, uint quest)
    {
        if (bytes.Length < SnapshotLength) throw new InvalidDataException("Short player descriptor block.");
        uint U(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));
        var ids = new List<uint>(); uint flags = 0;
        for (int i = 0; i < 20; i++)
        {
            int offset = 0x318 + i * 12; uint id = U(offset);
            if (id == 0) continue;
            ids.Add(id);
            if (id == quest) flags = U(offset + 4) >> 24;
        }
        return new(pid, guid, U(0x88), U(XpOffset), U(NextXpOffset), ids.Order().ToArray(), (flags & 1) != 0, (flags & 2) != 0);
    }
    public void Dispose() => _memory.Dispose();
}
