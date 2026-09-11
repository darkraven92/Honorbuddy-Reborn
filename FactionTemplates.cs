using Styx;
using Styx.WoWInternals;

namespace Honorbuddy5875.Runtime;

/// <summary>
/// Read-only view of the FactionTemplate.dbc store already loaded by WoW 1.12.1.5875.
/// The 1.12 client exposes a 1-based pointer array: records[id] -> FactionTemplate row.
/// </summary>
internal static class FactionTemplateStore5875
{
    private static bool _initialized;
    private static uint _recordsArray;
    private static int _maxId;

    public static uint RecordsArray => _recordsArray;
    public static int MaxId => _maxId;
    public static bool IsInitialized => _initialized;

    public static void Initialize()
    {
        ulong recordsSlot = RuntimeAddress(Vanilla5875.FactionTemplateRecordsPreferredVa);
        ulong countSlot = RuntimeAddress(Vanilla5875.FactionTemplateCountPreferredVa);

        _recordsArray = ObjectManager.ReadPointer32(recordsSlot);
        _maxId = ObjectManager.Read<int>(countSlot);

        if (!ObjectManager.IsPlausiblePointer(_recordsArray))
            throw new InvalidDataException($"FactionTemplate.dbc records pointer is invalid: 0x{_recordsArray:X8}.");
        if (_maxId is <= 0 or > 100_000)
            throw new InvalidDataException($"FactionTemplate.dbc max ID is implausible: {_maxId}.");

        _initialized = true;
    }

    public static bool TryGet(uint id, out FactionTemplate5875 entry)
    {
        EnsureInitialized();
        entry = default;
        if (id == 0 || id > (uint)_maxId)
            return false;

        uint record = ObjectManager.ReadPointer32((ulong)_recordsArray + id * 4UL);
        if (!ObjectManager.IsPlausiblePointer(record))
            return false;

        uint storedId = ObjectManager.Read<uint>(record + 0x00UL);
        if (storedId != id)
            return false;

        entry = new FactionTemplate5875(
            Id: storedId,
            Faction: ObjectManager.Read<uint>(record + 0x04UL),
            Flags: ObjectManager.Read<uint>(record + 0x08UL),
            FactionGroupMask: ObjectManager.Read<uint>(record + 0x0CUL),
            FriendGroupMask: ObjectManager.Read<uint>(record + 0x10UL),
            EnemyGroupMask: ObjectManager.Read<uint>(record + 0x14UL),
            Enemy0: ObjectManager.Read<uint>(record + 0x18UL),
            Enemy1: ObjectManager.Read<uint>(record + 0x1CUL),
            Enemy2: ObjectManager.Read<uint>(record + 0x20UL),
            Enemy3: ObjectManager.Read<uint>(record + 0x24UL),
            Friend0: ObjectManager.Read<uint>(record + 0x28UL),
            Friend1: ObjectManager.Read<uint>(record + 0x2CUL),
            Friend2: ObjectManager.Read<uint>(record + 0x30UL),
            Friend3: ObjectManager.Read<uint>(record + 0x34UL));
        return true;
    }

    public static FactionTemplate5875 Get(uint id)
        => TryGet(id, out FactionTemplate5875 entry)
            ? entry
            : throw new KeyNotFoundException($"FactionTemplate.dbc does not contain ID {id}.");

    /// <summary>
    /// Vanilla faction-template primitive. This intentionally does not claim player reputation/At-War
    /// overrides; phase 11 uses it for NPC-vs-player baseline classification.
    /// </summary>
    public static WoWUnitReaction GetBaseReaction(uint observerTemplateId, uint otherTemplateId)
    {
        if (observerTemplateId == otherTemplateId && observerTemplateId != 0)
            return WoWUnitReaction.Friendly;

        if (!TryGet(observerTemplateId, out FactionTemplate5875 observer) ||
            !TryGet(otherTemplateId, out FactionTemplate5875 other))
            return WoWUnitReaction.Neutral;

        if ((other.FactionGroupMask & observer.EnemyGroupMask) != 0 ||
            Contains(observer.Enemies, other.Faction))
            return WoWUnitReaction.Hostile;

        if ((other.FactionGroupMask & observer.FriendGroupMask) != 0 ||
            Contains(observer.Friends, other.Faction) ||
            (observer.FactionGroupMask & other.FriendGroupMask) != 0 ||
            Contains(other.Friends, observer.Faction))
            return WoWUnitReaction.Friendly;

        return WoWUnitReaction.Neutral;
    }

    private static bool Contains(ReadOnlySpan<uint> values, uint value)
    {
        if (value == 0)
            return false;
        foreach (uint item in values)
        {
            if (item == value)
                return true;
        }
        return false;
    }

    private static ulong RuntimeAddress(uint preferredVa)
    {
        if (preferredVa < Vanilla5875.PreferredImageBase)
            throw new ArgumentOutOfRangeException(nameof(preferredVa));
        return ObjectManager.ModuleBase + (preferredVa - Vanilla5875.PreferredImageBase);
    }

    private static void EnsureInitialized()
    {
        if (!_initialized)
            Initialize();
    }
}

internal readonly record struct FactionTemplate5875(
    uint Id,
    uint Faction,
    uint Flags,
    uint FactionGroupMask,
    uint FriendGroupMask,
    uint EnemyGroupMask,
    uint Enemy0,
    uint Enemy1,
    uint Enemy2,
    uint Enemy3,
    uint Friend0,
    uint Friend1,
    uint Friend2,
    uint Friend3)
{
    public ReadOnlySpan<uint> Enemies => new[] { Enemy0, Enemy1, Enemy2, Enemy3 };
    public ReadOnlySpan<uint> Friends => new[] { Friend0, Friend1, Friend2, Friend3 };
}
