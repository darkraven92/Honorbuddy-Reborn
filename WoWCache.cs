using Honorbuddy5875.Runtime;

namespace Styx.WoWInternals.WoWCache
{
    /// <summary>
    /// Original Honorbuddy cache database selector. Numeric ordering matches
    /// Honorbuddy 2.0.0.5999 exactly.
    /// </summary>
    public enum CacheDb
    {
        Creature = 0,
        GameObject = 1,
        Npc = 2,
        Name = 3,
        Guild = 4,
        Quest = 5,
        PageText = 6,
        PetName = 7,
        Petition = 8,
        ItemText = 9,
        WoW = 10,
        ArenaTeam = 11,
        Dance = 12,
        Item = 13,
        ItemClass = 14
    }

    /// <summary>
    /// Honorbuddy-compatible cache root exposed as StyxWoW.Cache.
    /// The build-specific backend is intentionally hidden behind Cache/InfoBlock.
    /// </summary>
    public class WoWCache
    {
        private readonly List<Cache> _caches = new(15);

        public WoWCache()
        {
            for (int i = 0; i < 15; i++)
                _caches.Add(new Cache((CacheDb)i));
        }

        public Cache this[CacheDb index]
        {
            get
            {
                int value = (int)index;
                if (value < 0 || value >= _caches.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _caches[value];
            }
        }
    }
}

/// <summary>
/// Original Honorbuddy global Cache type. The original constructor accepted two
/// client addresses; the 5875 compatibility backend keeps that signature but
/// resolves records through the build-specific adapter instead of BlueMagic.
/// </summary>
public class Cache
{
    public uint Address;

    private readonly uint _resolverAddress;
    private readonly Styx.WoWInternals.WoWCache.CacheDb? _database;

    internal Cache(uint address, uint resolverAddress)
    {
        Address = address;
        _resolverAddress = resolverAddress;
    }

    internal Cache(Styx.WoWInternals.WoWCache.CacheDb database)
    {
        _database = database;
        Address = 0;
        _resolverAddress = 0;
    }

    public InfoBlock? GetInfoBlockById(uint id)
    {
        if (id == 0)
            return null;

        if (_database == Styx.WoWInternals.WoWCache.CacheDb.Quest &&
            Vanilla5875QuestCache.TryGetQuestEntry(id, out _))
        {
            return new InfoBlock(0, id);
        }

        return null;
    }
}

/// <summary>
/// Original Honorbuddy global InfoBlock type. Address is zero for the safe
/// build-5875 adapter because records are not exposed as writable client pointers.
/// </summary>
public class InfoBlock
{
    private uint _id;
    private uint _address;

    internal InfoBlock(uint address, uint id)
    {
        _address = address;
        _id = id;
    }

    public QuestCacheEntry Quest
    {
        get
        {
            return Vanilla5875QuestCache.TryGetQuestEntry(Id, out QuestCacheEntry entry)
                ? entry
                : default;
        }
    }

    public uint Id => _id;
    public uint Address => _address;
}
