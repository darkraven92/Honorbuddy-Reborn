using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals.WoWObjects;

namespace Bots.Grind;

[Flags]
public enum BehaviorFlags
{
    None = 0,
    Death = 1 << 0,
    Combat = 1 << 1,
    Loot = 1 << 2,
    Vendor = 1 << 3,
    Roam = 1 << 4,
    Pull = 1 << 5,
    All = Death | Combat | Loot | Vendor | Roam | Pull
}

public static class LevelBot
{
    public static BehaviorFlags BehaviorFlags { get; set; } = BehaviorFlags.All;

    public static bool IsProfileTargetCandidate(WoWUnit unit, Profile profile)
    {
        if (!unit.IsValid || !unit.IsStrictHostileCombatCandidate)
            return false;

        return PassesProfileTargetFilters(unit, profile);
    }

    public static bool IsQuestObjectiveTargetCandidate(
        WoWUnit unit,
        Profile profile,
        uint requiredEntry)
    {
        if (!unit.IsValid ||
            requiredEntry == 0 ||
            unit.Entry != requiredEntry)
            return false;

        if (!unit.IsStrictHostileCombatCandidate &&
            !unit.IsNeutralPotentialCombatCandidate)
            return false;

        return PassesProfileTargetFilters(unit, profile);
    }

    private static bool PassesProfileTargetFilters(WoWUnit unit, Profile profile)
    {
        if (unit.Level < profile.TargetMinLevel || unit.Level > profile.TargetMaxLevel)
            return false;
        if (profile.AvoidMobs.Contains(unit.Entry))
            return false;
        if (IsTooNearBlackspot(profile.Blackspots, unit.Location))
            return false;

        var area = profile.GrindArea;
        if (area is not null)
        {
            if (area.MaxDistance is double maxDistance && unit.Distance > maxDistance)
                return false;
            if (area.Factions.Count > 0 &&
                !area.Factions.Contains(checked((int)unit.FactionId)))
                return false;
        }
        else if (profile.Factions.Count > 0 &&
                 !profile.Factions.Contains(unit.FactionId))
        {
            return false;
        }

        return true;
    }

    public static bool IsTooNearBlackspot(IEnumerable<Blackspot> blackspots, WoWPoint location)
        => blackspots.Any(blackspot => blackspot.Contains(location));
}
