using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;
using Bots.Grind;

namespace Styx.Logic;

public delegate void IncludeTargetsFilterDelegate(
    List<WoWObject> incomingUnits,
    HashSet<WoWObject> outgoingUnits);

public delegate void RemoveTargetsFilterDelegate(List<WoWObject> units);

public sealed class Targeting
{
    private Targeting() { }

    public static Targeting Instance { get; } = new();

    public event IncludeTargetsFilterDelegate? IncludeTargetsFilter;
    public event RemoveTargetsFilterDelegate? RemoveTargetsFilter;

    public float PullDistance { get; set; } = 30f;
    public bool KillBetweenHotspots { get; set; } = true;
    public long PulseCount { get; private set; }
    public List<WoWUnit> TargetList { get; private set; } = new();
    public WoWUnit? FirstUnit => TargetList.FirstOrDefault();

    public void Pulse()
    {
        PulseCount++;
        Profile profile = ProfileManager.CurrentProfile;

        var incoming = ObjectManager.GetObjectsOfType<WoWUnit>(true, false)
            .Where(u => u.IsValid)
            .Cast<WoWObject>()
            .ToList();

        var included = new HashSet<WoWObject>();
        IncludeTargetsFilter?.Invoke(incoming, included);

        // The legacy filters are additive/removal hooks. Our core baseline is the
        // profile-filtered unit set, and hooks can add or remove objects from it.
        var working = incoming
            .OfType<WoWUnit>()
            .Where(u => LevelBot.IsProfileTargetCandidate(u, profile))
            .Cast<WoWObject>()
            .ToList();

        foreach (WoWObject item in included)
        {
            if (!working.Contains(item))
                working.Add(item);
        }

        RemoveTargetsFilter?.Invoke(working);

        TargetList = working
            .OfType<WoWUnit>()
            .Where(u => u.IsValid)
            .OrderBy(u => Score(u, profile))
            .ThenBy(u => u.Guid)
            .ToList();
    }

    public void ResetCounters()
    {
        PulseCount = 0;
        TargetList = new List<WoWUnit>();
    }

    private static double Score(WoWUnit unit, Profile profile)
    {
        int midpoint = profile.TargetMinLevel + Math.Max(0, profile.TargetMaxLevel - profile.TargetMinLevel) / 2;
        double levelPenalty = Math.Abs(unit.Level - midpoint) * 0.15;
        return unit.Distance + levelPenalty;
    }
}
