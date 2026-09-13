using Bots.Grind;
using Styx;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Honorbuddy5875.Movement;

/// <summary>
/// Client targeting through normal keyboard input only. No memory writes or injected calls.
/// Linux input-event key codes: ESC=1, TAB=15.
/// </summary>
public sealed class UInputClientTargeting : IDisposable
{
    private const ushort KeyEsc = 1;
    private const ushort KeyTab = 15;
    private readonly UInputKeyboard _keyboard = new();
    private bool _disposed;

    public void ClearTarget()
    {
        ThrowIfDisposed();
        _keyboard.Tap(KeyEsc);
    }

    public void TargetNearestEnemy()
    {
        ThrowIfDisposed();
        _keyboard.Tap(KeyTab);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _keyboard.Dispose();
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(UInputClientTargeting));
    }
}

public sealed record ClientTargetAttempt(
    int Attempt,
    ulong ClientGuid,
    uint Entry,
    WoWUnitReaction? Reaction,
    bool IsStrictHostile,
    bool IsProfileCandidate,
    double Distance,
    bool MatchesPreferred);

public sealed record ClientTargetSelectionResult(
    bool Accepted,
    ulong PreferredGuid,
    ulong InitialGuid,
    ulong FinalGuid,
    bool ClearedBeforeCycling,
    IReadOnlyList<ClientTargetAttempt> Attempts,
    string StopReason);

public static class ClientTargetSelector5875
{
    /// <summary>
    /// Cycles the normal WoW Tab target until the client selects a target that is also
    /// accepted by the active Honorbuddy-style profile filter. The internal scorer's
    /// preferred GUID is diagnostic only; WoW's own Tab target order can legitimately
    /// choose another valid hostile first because of facing/LOS/client targeting rules.
    /// </summary>
    public static ClientTargetSelectionResult SelectProfileHostile(
        UInputClientTargeting input,
        Profile profile,
        ulong preferredGuid,
        int maxTabAttempts = 12,
        int settleMilliseconds = 150,
        double minimumDistance = 0.0,
        double maximumDistance = double.PositiveInfinity,
        uint requiredEntry = 0)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(profile);
        if (maxTabAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxTabAttempts));

        ObjectManager.Update();
        LocalPlayer me = Styx.StyxWoW.Me
            ?? throw new InvalidOperationException("LocalPlayer unavailable before client targeting.");
        ulong initial = me.CurrentTargetGuid;

        var attempts = new List<ClientTargetAttempt>();

        // Phase 15B.2: if the client already has a valid hostile selected, adopt it.
        // Do not clear a correct target just to force an Esc/Tab round-trip.
        if (TryBuildAcceptedAttempt(
                profile,
                preferredGuid,
                attemptNumber: 0,
                initial,
                minimumDistance,
                maximumDistance,
                requiredEntry,
                out ClientTargetAttempt? initialAttempt))
        {
            attempts.Add(initialAttempt!);
            string reason = initial == preferredGuid
                ? "existing client target is already the preferred profile-valid strict hostile"
                : "existing client target is already a profile-valid strict hostile";

            return new ClientTargetSelectionResult(
                true, preferredGuid, initial, initial, false, attempts, reason);
        }

        // If the current target is not usable, cycle WoW's normal Tab target selection.
        // We intentionally do NOT require Escape first. /dev/uinput is foreground-window
        // input, and clearing a target is unnecessary for cycling to another enemy.
        for (int attempt = 1; attempt <= maxTabAttempts; attempt++)
        {
            input.TargetNearestEnemy();
            Thread.Sleep(settleMilliseconds);
            ObjectManager.Update();

            LocalPlayer currentMe = Styx.StyxWoW.Me
                ?? throw new InvalidOperationException("LocalPlayer unavailable while cycling client targets.");
            ulong guid = currentMe.CurrentTargetGuid;

            WoWUnit? unit = guid == 0 ? null : ObjectManager.GetObjectByGuid<WoWUnit>(guid);
            bool strict = unit?.IsStrictHostileCombatCandidate ?? false;
            bool profileCandidate = unit is not null && LevelBot.IsProfileTargetCandidate(unit, profile);
            double distance = unit?.Distance2D ?? double.NaN;
            bool distanceAccepted = unit is not null &&
                                    double.IsFinite(distance) &&
                                    distance >= minimumDistance &&
                                    distance <= maximumDistance;

            var item = new ClientTargetAttempt(
                attempt,
                guid,
                unit?.Entry ?? 0,
                unit?.MyReaction,
                strict,
                profileCandidate,
                distance,
                guid != 0 && guid == preferredGuid);
            attempts.Add(item);

            if (unit is not null &&
                unit.MyReaction == WoWUnitReaction.Hostile &&
                strict &&
                profileCandidate &&
                EntryMatches(unit.Entry, requiredEntry) &&
                distanceAccepted)
            {
                string reason = guid == preferredGuid
                    ? "preferred internal strict-hostile GUID selected by client"
                    : "client selected a different strict-hostile unit that passes the same profile filter";

                return new ClientTargetSelectionResult(
                    true, preferredGuid, initial, guid, false, attempts, reason);
            }
        }

        ObjectManager.Update();
        ulong finalGuid = Styx.StyxWoW.Me?.CurrentTargetGuid ?? 0;
        return new ClientTargetSelectionResult(
            false, preferredGuid, initial, finalGuid, false, attempts,
            $"no profile-valid strict hostile in the approach distance window selected after {maxTabAttempts} Tab attempts");
    }

    private static bool TryBuildAcceptedAttempt(
        Profile profile,
        ulong preferredGuid,
        int attemptNumber,
        ulong guid,
        double minimumDistance,
        double maximumDistance,
        uint requiredEntry,
        out ClientTargetAttempt? attempt)
    {
        WoWUnit? unit = guid == 0 ? null : ObjectManager.GetObjectByGuid<WoWUnit>(guid);
        bool strict = unit?.IsStrictHostileCombatCandidate ?? false;
        bool profileCandidate = unit is not null && LevelBot.IsProfileTargetCandidate(unit, profile);
        double distance = unit?.Distance2D ?? double.NaN;
        bool distanceAccepted = unit is not null &&
                                double.IsFinite(distance) &&
                                distance >= minimumDistance &&
                                distance <= maximumDistance;

        attempt = new ClientTargetAttempt(
            attemptNumber,
            guid,
            unit?.Entry ?? 0,
            unit?.MyReaction,
            strict,
            profileCandidate,
            distance,
            guid != 0 && guid == preferredGuid);

        return unit is not null &&
               unit.MyReaction == WoWUnitReaction.Hostile &&
               strict &&
               profileCandidate &&
               EntryMatches(unit.Entry, requiredEntry) &&
               distanceAccepted;
    }

    internal static bool EntryMatches(uint actualEntry, uint requiredEntry)
        => requiredEntry == 0 || actualEntry == requiredEntry;

    public static bool EnsureCleared(UInputClientTargeting input, int settleMilliseconds = 130)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            ObjectManager.Update();
            LocalPlayer? me = Styx.StyxWoW.Me;
            if (me is not null && me.CurrentTargetGuid == 0)
                return true;

            input.ClearTarget();
            Thread.Sleep(settleMilliseconds);
        }

        ObjectManager.Update();
        return Styx.StyxWoW.Me?.CurrentTargetGuid == 0;
    }
}
