using System.Buffers.Binary;
using System.Text;

namespace Honorbuddy5875.Runtime;

// Internal wire data, deliberately independent of the public Honorbuddy model.
// Field order reference: vmangos/WDBReader, vanilla/WDBReader/Quest.h, ReadEntry.
// Confirmed against the supplied 5875, record-version-3 questcache.wdb.
internal sealed record Vanilla5875QuestData(
    uint Id, uint Method, int Level, int ZoneOrSort, uint Type,
    uint ReputationFaction, int ReputationValue,
    uint OppositeReputationFaction, int OppositeReputationValue,
    uint NextQuestId, int RewardOrRequiredMoney, uint RewardMoneyAtMaxLevel,
    int RewardSpellId, uint SourceItemId, uint Flags,
    int[] RewardItemIds, int[] RewardItemCounts,
    int[] ChoiceItemIds, int[] ChoiceItemCounts,
    uint PointMapId, float PointX, float PointY, uint PointOption,
    string Title, string ObjectiveSummary, string Details, string EndText,
    int[] ObjectiveIds, int[] ObjectiveCounts, int[] ItemIds, int[] ItemCounts,
    string[] ObjectiveTexts);

internal sealed record Vanilla5875QuestCacheFile(
    Dictionary<uint, QuestCacheEntry> Entries, QuestCacheDiagnostics Diagnostics);

internal static class Vanilla5875QuestCacheReader
{
    private const int MaxFileBytes = 64 * 1024 * 1024;
    private const int MaxRecordBytes = 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static Vanilla5875QuestCacheFile ReadFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length < 20 || stream.Length > MaxFileBytes)
            throw new InvalidDataException("WDB file size is outside the supported 20-byte to 64-MiB range.");
        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        if (stream.Length != bytes.Length)
            throw new InvalidDataException("WDB size changed while reading; retry after the client finishes writing.");
        return Parse(bytes, path);
    }

    internal static Vanilla5875QuestCacheFile Parse(ReadOnlySpan<byte> bytes, string? path = null)
    {
        if (bytes.Length < 20 || bytes.Length > MaxFileBytes)
            throw new InvalidDataException("Invalid WDB file size.");
        var r = new Cursor(bytes);
        string signature = Encoding.ASCII.GetString(r.Take(4));
        uint build = r.U32();
        byte[] localeBytes = r.Take(4).ToArray();
        Array.Reverse(localeBytes);
        string locale = Encoding.ASCII.GetString(localeBytes).TrimEnd('\0');
        uint internalSize = r.U32();
        uint version = r.U32();
        if (signature != "TSQW" || build != 5875)
            throw new InvalidDataException($"Expected TSQW / build 5875; found {signature} / {build}.");
        if (version != 3)
            throw new InvalidDataException($"Unsupported WDB record version {version}; only version 3 is mapped.");

        var entries = new Dictionary<uint, QuestCacheEntry>();
        bool terminated = false;
        while (r.Remaining > 0)
        {
            uint id = r.U32();
            uint size = r.U32();
            if (id == 0 && size == 0)
            {
                terminated = true;
                if (r.Remaining != 0)
                    throw new InvalidDataException("Unexpected bytes after WDB terminator.");
                break;
            }
            if (id == 0 || size == 0 || size > MaxRecordBytes)
                throw new InvalidDataException($"Quest {id}: invalid record size {size}.");
            Vanilla5875QuestData data;
            try { data = ReadRecord(id, r.Take(checked((int)size))); }
            catch (Exception ex) when (ex is InvalidDataException or DecoderFallbackException)
            { throw new InvalidDataException($"Quest {id}: {ex.Message}", ex); }
            if (!entries.TryAdd(id, new QuestCacheEntry { Id = id, Data = data }))
                throw new InvalidDataException($"Duplicate quest ID {id} in WDB.");
        }
        if (!terminated)
            throw new InvalidDataException("WDB terminator is missing; the file may be incomplete.");
        return new(entries, new QuestCacheDiagnostics(path, signature, build, locale,
            internalSize, version, entries.Count, true, null));
    }

    private static Vanilla5875QuestData ReadRecord(uint id, ReadOnlySpan<byte> bytes)
    {
        var r = new Cursor(bytes);
        if (r.U32() != id)
            throw new InvalidDataException("Record key does not match its embedded quest ID.");
        uint method = r.U32();
        int level = r.I32();
        int zone = r.I32();
        uint type = r.U32();
        uint repFaction = r.U32();
        int repValue = r.I32();
        uint oppositeFaction = r.U32();
        int oppositeValue = r.I32();
        uint next = r.U32();
        int money = r.I32();
        uint maxMoney = r.U32();
        int spell = r.I32();
        uint sourceItem = r.U32();
        uint flags = r.U32();
        var rewards = ReadPairs(ref r, 4);
        var choices = ReadPairs(ref r, 6);
        uint map = r.U32();
        float x = r.F32();
        float y = r.F32();
        uint pointOption = r.U32();
        string title = r.StringZ();
        string summary = r.StringZ();
        string details = r.StringZ();
        string endText = r.StringZ();
        int[] objectiveIds = new int[4], objectiveCounts = new int[4];
        int[] itemIds = new int[4], itemCounts = new int[4];
        for (int i = 0; i < 4; i++)
        {
            objectiveIds[i] = r.I32(); // Preserve bit 31 (GameObject marker).
            objectiveCounts[i] = r.NonNegativeInt();
            itemIds[i] = r.NonNegativeInt();
            itemCounts[i] = r.NonNegativeInt();
        }
        string[] texts = new string[4];
        for (int i = 0; i < 4; i++) texts[i] = r.StringZ();
        if (r.Remaining != 0)
            throw new InvalidDataException($"{r.Remaining} unexpected payload bytes remain.");
        return new(id, method, level, zone, type, repFaction, repValue,
            oppositeFaction, oppositeValue, next, money, maxMoney, spell, sourceItem, flags,
            rewards.Ids, rewards.Counts, choices.Ids, choices.Counts,
            map, x, y, pointOption, title, summary, details, endText,
            objectiveIds, objectiveCounts, itemIds, itemCounts, texts);
    }

    private static (int[] Ids, int[] Counts) ReadPairs(ref Cursor r, int count)
    {
        int[] ids = new int[count], counts = new int[count];
        for (int i = 0; i < count; i++)
        {
            ids[i] = r.NonNegativeInt();
            counts[i] = r.NonNegativeInt();
        }
        return (ids, counts);
    }

    private ref struct Cursor
    {
        private ReadOnlySpan<byte> _remaining;
        internal Cursor(ReadOnlySpan<byte> bytes) => _remaining = bytes;
        internal int Remaining => _remaining.Length;
        internal ReadOnlySpan<byte> Take(int count)
        {
            if (count < 0 || count > _remaining.Length)
                throw new InvalidDataException("Truncated WDB field or record.");
            var value = _remaining[..count];
            _remaining = _remaining[count..];
            return value;
        }
        internal uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
        internal int I32() => unchecked((int)U32());
        internal int NonNegativeInt()
        {
            uint value = U32();
            if (value > int.MaxValue)
                throw new InvalidDataException("Unsigned field cannot fit the public Int32 API.");
            return (int)value;
        }
        internal float F32() => BitConverter.Int32BitsToSingle(I32());
        internal string StringZ()
        {
            int end = _remaining.IndexOf((byte)0);
            if (end < 0) throw new InvalidDataException("Missing string terminator.");
            string value = StrictUtf8.GetString(Take(end));
            Take(1);
            return value;
        }
    }
}
