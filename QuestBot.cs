using TreeSharp;
using Honorbuddy5875.Movement;
using Styx;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.Quest;

public enum QuestDecisionKind
{
    None,
    AcquireTarget,
    MoveToHotspot,
    MoveToProfileLocation,
    QuestInteractionDeferred,
    ItemUseDeferred,
    UnsupportedProfileNode,
    ObjectiveInProgress,
    QuestStateBlocked,
    ProfileComplete,
    TurnInReady,
    QuestGiverUnavailable,
    MoveToQuestGiver,
    QuestGiverInRange
}

public enum QuestFlowState
{
    Idle,
    Patrol,
    AcquireTarget,
    ApproachTarget,
    TargetReleased,
    ResumePatrol,
    Complete,
    Aborted
}

public sealed record QuestDecision(
    QuestDecisionKind Kind,
    string Description,
    ulong TargetGuid = 0,
    uint Entry = 0,
    WoWPoint? Destination = null);

public sealed partial class QuestBot : BotBase
{
    private Composite? _root;
    private int _profileNodeIndex;
    private WoWPoint? _activeHotspot;
    private WoWPoint? _movementStart;
    private uint _movementStartHealth;
    private WoWPoint? _lastProgressLocation;
    private DateTime _lastProgressAtUtc;
    private DateTime _targetAcquisitionEnableAtUtc;
    private ulong _stickyTargetGuid;
    private readonly HashSet<ulong> _releasedTargetGuids = new();
    private bool _clientTargetSynchronized;
    private ulong _preferredInternalTargetGuid;

    public override string Name => "Questing - 5875 Client Target Approach Probe";
    public override PulseFlags PulseFlags => PulseFlags.All;
    public override bool RequirementsMet
        => !ProfileManager.CurrentProfile.IsEmpty &&
           (ProfileManager.CurrentProfile.QuestOrder.Count > 0 ||
            ProfileManager.CurrentProfile.GrindArea?.Hotspots.Count > 0);
    public override Composite Root => _root ??= CreateRoot();

    public bool MovementExecutionEnabled { get; set; }
    public float MaximumMovementDisplacement { get; set; } = 30.0f;
    public TimeSpan NoProgressTimeout { get; set; } = TimeSpan.FromSeconds(3.0);
    public float ProgressResetDistance { get; set; } = 0.20f;
    public float TargetApproachDistance { get; set; } = 3.5f;
    public TimeSpan TargetAcquisitionDelay { get; set; } = TimeSpan.Zero;
    public bool AutoReleaseTargetAfterApproach { get; set; }
    public int RequiredResumeHotspotsToReach { get; set; } = 1;
    public bool ClientTargetSyncEnabled { get; set; }
    public UInputClientTargeting? ClientTargetInput { get; set; }
    public int MaximumClientTabAttempts { get; set; } = 12;

    public int ClientTargetSyncAttempts { get; private set; }
    public int ClientTargetSyncSuccesses { get; private set; }
    public int ClientTargetSyncFailures { get; private set; }
    public ulong PreferredInternalTargetGuid => _preferredInternalTargetGuid;
    public ulong SynchronizedClientTargetGuid { get; private set; }
    public ClientTargetSelectionResult? LastClientTargetSelection { get; private set; }

    public bool MovementGoalReached { get; private set; }
    public bool MovementAborted { get; private set; }
    public string MovementStopReason { get; private set; } = "not started";
    public WoWPoint? MovementDestination { get; private set; }
    public MoveResult? LastMoveResult { get; private set; }
    public long NavigatorMoveCalls { get; private set; }
    public int HotspotsReached { get; private set; }
    public int RouteWraps { get; private set; }
    public List<string> ReachedHotspotNames { get; } = new();

    public ulong AcquiredTargetGuid => _stickyTargetGuid;
    public long TargetApproachCalls { get; private set; }
    public int TargetLostCount { get; private set; }
    public int TargetAcquisitionCount { get; private set; }
    public int TargetApproachCompletedCount { get; private set; }
    public int ReleasedTargetCount { get; private set; }
    public long PatrolMoveCallsBeforeAcquire { get; private set; }
    public long ResumePatrolMoveCalls { get; private set; }
    public int ResumeHotspotsReached { get; private set; }
    public QuestFlowState FlowState { get; private set; } = QuestFlowState.Idle;
    public List<string> TransitionLog { get; } = new();

    public QuestDecision CurrentDecision { get; private set; }
        = new(QuestDecisionKind.None, "No decision has been evaluated yet.");
    public long PulseCalls { get; private set; }
    public long DecisionEvaluations { get; private set; }

    public Composite CreateRoot()
    {
        return new TreeSharp.Action(_ =>
        {
            EvaluateDecision();
            ExecuteCurrentDecision();
            return RunStatus.Running;
        });
    }

    public override void Start()
    {
        ResetObjectiveCombatState(resetCounters: true);
        AttachObjectiveTargetFilter();
        _rewardAttempt = null;
        _profileNodeIndex = 0;
        _questOrderProfile = null;
        _turnInNpcGuid = 0;
        _turnInNpc = null;
        _questGiverSearchDestination = null;
        _questGiverSearchArrived = false;
        _activeHotspot = null;
        _stickyTargetGuid = 0;
        _releasedTargetGuids.Clear();
        MovementGoalReached = false;
        MovementAborted = false;
        MovementStopReason = MovementExecutionEnabled ? "running" : "execution disabled";
        MovementDestination = null;
        LastMoveResult = null;
        NavigatorMoveCalls = 0;
        TargetApproachCalls = 0;
        TargetLostCount = 0;
        TargetAcquisitionCount = 0;
        TargetApproachCompletedCount = 0;
        ReleasedTargetCount = 0;
        PatrolMoveCallsBeforeAcquire = 0;
        ResumePatrolMoveCalls = 0;
        ResumeHotspotsReached = 0;
        HotspotsReached = 0;
        RouteWraps = 0;
        ReachedHotspotNames.Clear();
        TransitionLog.Clear();
        FlowState = QuestFlowState.Idle;
        _clientTargetSynchronized = false;
        _preferredInternalTargetGuid = 0;
        ClientTargetSyncAttempts = 0;
        ClientTargetSyncSuccesses = 0;
        ClientTargetSyncFailures = 0;
        SynchronizedClientTargetGuid = 0;
        LastClientTargetSelection = null;

        LocalPlayer? me = StyxWoW.Me;
        _movementStart = me?.Location;
        _movementStartHealth = me?.CurrentHealth ?? 0;
        _lastProgressLocation = me?.Location;
        _lastProgressAtUtc = DateTime.UtcNow;
        _targetAcquisitionEnableAtUtc = DateTime.UtcNow + TargetAcquisitionDelay;

        TreeRoot.GoalText = ProfileManager.CurrentProfile.Name;
        TreeRoot.StatusText = MovementExecutionEnabled
            ? "Executing patrol -> client target sync -> safe approach on WoW 1.12.1.5875"
            : "Evaluating patrol/client-target profile on WoW 1.12.1.5875";
    }

    public override void Stop()
    {
        DetachObjectiveTargetFilter();
        ResetObjectiveCombatState();
        _rewardAttempt = null;
        Navigator.Clear();
    }
    public override void Pulse() => PulseCalls++;
    public void EvaluateNow() => EvaluateDecision();

    private void EvaluateDecision()
    {
        DecisionEvaluations++;
        if (MovementGoalReached || MovementAborted)
            return;
        Profile profile = ProfileManager.CurrentProfile;
        if (EvaluateQuestOrder(profile))
            return;
        ProfileNode? node = profile.QuestOrder.Count == 0
            ? null
            : profile.QuestOrder[_profileNodeIndex];

        if (node is GrindToNode || node is null)
        {
            WoWUnit? target = null;
            if (_stickyTargetGuid != 0)
            {
                target = ObjectManager.GetObjectByGuid<WoWUnit>(_stickyTargetGuid);
                if (target is null || !target.IsValid || !target.IsAlive || !target.IsStrictHostileCombatCandidate)
                {
                    ulong lostGuid = _stickyTargetGuid;
                    bool invalidHostileState = target is not null && target.IsValid && target.IsAlive && !target.IsStrictHostileCombatCandidate;
                    _stickyTargetGuid = 0;
                    target = null;
                    TargetLostCount++;
                    _activeHotspot = null;
                    ResetProgressTracking();
                    SetFlowState(
                        QuestFlowState.TargetReleased,
                        invalidHostileState
                            ? $"Target 0x{lostGuid:X16} is no longer a strict hostile combat candidate; resume patrol."
                            : $"Target 0x{lostGuid:X16} disappeared; resume patrol.");
                }
            }

            if (target is null && DateTime.UtcNow >= _targetAcquisitionEnableAtUtc)
            {
                WoWUnit? preferred = Targeting.Instance.TargetList
                    .FirstOrDefault(unit => !_releasedTargetGuids.Contains(unit.Guid));

                if (preferred is not null && _stickyTargetGuid == 0)
                {
                    _preferredInternalTargetGuid = preferred.Guid;

                    if (ClientTargetSyncEnabled)
                    {
                        if (ClientTargetInput is null)
                        {
                            AbortMovement("client target sync enabled but no input backend was supplied");
                            return;
                        }

                        Navigator.Clear();
                        ResetProgressTracking();
                        ClientTargetSyncAttempts++;
                        ClientTargetSelectionResult selection = ClientTargetSelector5875.SelectProfileHostile(
                            ClientTargetInput,
                            profile,
                            preferred.Guid,
                            MaximumClientTabAttempts,
                            150,
                            TargetApproachDistance + 1.0,
                            profile.GrindArea?.MaxDistance ?? double.PositiveInfinity);
                        LastClientTargetSelection = selection;

                        if (!selection.Accepted || selection.FinalGuid == 0)
                        {
                            ClientTargetSyncFailures++;
                            AbortMovement($"client target synchronization failed: {selection.StopReason}");
                            return;
                        }

                        WoWUnit? adopted = ObjectManager.GetObjectByGuid<WoWUnit>(selection.FinalGuid);
                        if (adopted is null || !Bots.Grind.LevelBot.IsProfileTargetCandidate(adopted, profile))
                        {
                            ClientTargetSyncFailures++;
                            AbortMovement("client selected target no longer passes profile/hostile validation");
                            return;
                        }

                        target = adopted;
                        _clientTargetSynchronized = true;
                        SynchronizedClientTargetGuid = adopted.Guid;
                        ClientTargetSyncSuccesses++;
                        ResetProgressTracking();
                    }
                    else
                    {
                        target = preferred;
                    }
                }
            }

            if (target is not null)
            {
                if (_stickyTargetGuid == 0)
                {
                    _stickyTargetGuid = target.Guid;
                    TargetAcquisitionCount++;
                    string source = _clientTargetSynchronized
                        ? $"client-synchronized (preferred 0x{_preferredInternalTargetGuid:X16})"
                        : "internal";
                    SetFlowState(
                        QuestFlowState.AcquireTarget,
                        $"Acquire 0x{target.Guid:X16} entry={target.Entry} distance={target.Distance2D:F2} source={source}");
                }

                CurrentDecision = new QuestDecision(
                    QuestDecisionKind.AcquireTarget,
                    $"Profile candidate locked: entry {target.Entry}, level {target.Level}, distance {target.Distance2D:F1}",
                    target.Guid,
                    target.Entry,
                    target.Location);
                TreeRoot.StatusText = CurrentDecision.Description;
                return;
            }

            if (profile.GrindArea?.HotspotManager is { Hotspots.Count: > 0 } hotspots && StyxWoW.Me is not null)
            {
                _activeHotspot ??= hotspots.GetNearestHotspot(StyxWoW.Me.Location);
                WoWPoint point = _activeHotspot.Value;
                CurrentDecision = new QuestDecision(
                    QuestDecisionKind.MoveToHotspot,
                    MovementExecutionEnabled
                        ? $"No target candidate; navigating to profile hotspot {hotspots.CurrentName} {point}"
                        : $"No target candidate; next action would navigate to hotspot {hotspots.CurrentName} {point}",
                    Destination: point);
                TreeRoot.StatusText = CurrentDecision.Description;
                return;
            }
        }

        CurrentDecision = node switch
        {
            MoveToNode move => new QuestDecision(
                QuestDecisionKind.MoveToProfileLocation,
                MovementExecutionEnabled
                    ? $"Navigating to profile location {move.Destination}"
                    : $"Next action would navigate to {move.Destination}",
                Destination: move.Destination),
            PickUpNode pick => new QuestDecision(
                QuestDecisionKind.QuestInteractionDeferred,
                $"Quest pickup {pick.QuestId} from entry {pick.GiverId} requires Interact/Lua layer.",
                Entry: pick.GiverId),
            TurnInNode turn => new QuestDecision(
                QuestDecisionKind.QuestInteractionDeferred,
                $"Quest turn-in {turn.QuestId} to entry {turn.TurnInId} requires Interact/Lua layer.",
                Entry: turn.TurnInId),
            UseItemNode use => new QuestDecision(
                QuestDecisionKind.ItemUseDeferred,
                $"UseItem {use.ItemId} requires item/Lua layer.",
                Entry: use.ItemId),
            null => new QuestDecision(QuestDecisionKind.None, "Profile contains no executable phase-14 node."),
            _ => new QuestDecision(
                QuestDecisionKind.UnsupportedProfileNode,
                $"Profile node <{node.ElementName}> is parsed but not yet executable.")
        };
        TreeRoot.StatusText = CurrentDecision.Description;
    }

    private void ExecuteCurrentDecision()
    {
        if (MovementGoalReached || MovementAborted)
            return;

        // KillMob execution remains part of QuestOrder, while targeting,
        // movement and combat stay delegated to their existing Honorbuddy
        // layers. Combat is separately armed so routing diagnostics cannot fight.
        if (CurrentDecision.Kind == QuestDecisionKind.ObjectiveInProgress)
        {
            if (ObjectiveCombatExecutionEnabled)
                ExecuteObjectiveCombatDecision();
            else
            {
                Navigator.Clear();
                ResetProgressTracking();
            }
            return;
        }

        if (!MovementExecutionEnabled)
            return;

        // Waiting for quest data or interaction is not a navigation stall.
        if (CurrentDecision.Kind is not (QuestDecisionKind.AcquireTarget or
            QuestDecisionKind.MoveToHotspot or QuestDecisionKind.MoveToProfileLocation or
            QuestDecisionKind.MoveToQuestGiver))
        {
            Navigator.Clear();
            ResetProgressTracking();
            return;
        }

        LocalPlayer? me = StyxWoW.Me;
        if (me is null || !me.IsValid)
        {
            AbortMovement("LocalPlayer unavailable");
            return;
        }

        if (me.Combat)
        {
            AbortMovement("combat detected");
            return;
        }

        if (_movementStartHealth != 0 && me.CurrentHealth < _movementStartHealth)
        {
            AbortMovement("health decreased");
            return;
        }

        if (_movementStart is WoWPoint movementStart &&
            movementStart.Distance2D(me.Location) > MaximumMovementDisplacement)
        {
            AbortMovement("maximum displacement guard");
            return;
        }

        TrackProgress(me.Location);
        if (DateTime.UtcNow - _lastProgressAtUtc > NoProgressTimeout)
        {
            AbortMovement("stuck/no progress timeout");
            return;
        }

        if (CurrentDecision.Kind == QuestDecisionKind.AcquireTarget)
        {
            ExecuteTargetApproach(me);
            return;
        }

        if (CurrentDecision.Kind == QuestDecisionKind.MoveToQuestGiver)
        {
            ExecuteQuestGiverApproach();
            return;
        }

        if (CurrentDecision.Kind is not (QuestDecisionKind.MoveToHotspot or QuestDecisionKind.MoveToProfileLocation) ||
            CurrentDecision.Destination is not WoWPoint destination)
        {
            Navigator.Clear();
            return;
        }

        bool resuming = ReleasedTargetCount > 0 || TargetLostCount > 0;
        SetFlowState(
            resuming ? QuestFlowState.ResumePatrol : QuestFlowState.Patrol,
            resuming ? "Resuming profile patrol after target release." : "Patrolling profile hotspots before target acquisition.");

        MovementDestination = destination;
        LastMoveResult = Navigator.MoveTo(destination);
        NavigatorMoveCalls++;
        if (resuming)
            ResumePatrolMoveCalls++;
        else if (TargetAcquisitionCount == 0)
            PatrolMoveCallsBeforeAcquire++;

        if (LastMoveResult == MoveResult.ReachedDestination)
            HandleDestinationReached();
        else if (LastMoveResult is MoveResult.Failed or MoveResult.PathGenerationFailed)
            AbortMovement("Navigator could not generate or follow a complete path");
    }

    private void ExecuteTargetApproach(LocalPlayer me)
    {
        ulong guid = CurrentDecision.TargetGuid != 0 ? CurrentDecision.TargetGuid : _stickyTargetGuid;
        WoWUnit? target = guid == 0 ? null : ObjectManager.GetObjectByGuid<WoWUnit>(guid);
        if (target is null || !target.IsValid || !target.IsAlive || !target.IsStrictHostileCombatCandidate)
        {
            Navigator.Clear();
            bool invalidHostileState = target is not null && target.IsValid && target.IsAlive && !target.IsStrictHostileCombatCandidate;
            if (guid != 0)
                TargetLostCount++;
            _stickyTargetGuid = 0;
            _activeHotspot = null;
            ResetProgressTracking();
            SetFlowState(
                QuestFlowState.TargetReleased,
                invalidHostileState
                    ? $"Target 0x{guid:X16} is no longer a strict hostile combat candidate; resume patrol."
                    : $"Target 0x{guid:X16} disappeared; resume patrol.");
            TreeRoot.StatusText = invalidHostileState
                ? "Target no longer passes hostile/attackability gates; returning to patrol."
                : "Target disappeared; returning to patrol on next pulse.";
            return;
        }

        if (ClientTargetSyncEnabled)
        {
            ObjectManager.Update();
            LocalPlayer? currentMe = StyxWoW.Me;
            if (currentMe is null || currentMe.CurrentTargetGuid != target.Guid)
            {
                AbortMovement($"client target GUID diverged from sticky target 0x{target.Guid:X16}");
                return;
            }
        }

        _stickyTargetGuid = target.Guid;
        TargetApproachCalls++;
        MovementDestination = target.Location;
        SetFlowState(QuestFlowState.ApproachTarget, $"Approaching 0x{target.Guid:X16} entry={target.Entry}.");

        double distance = me.Location.Distance2D(target.Location);
        if (distance <= TargetApproachDistance)
        {
            Navigator.Clear();
            TargetApproachCompletedCount++;

            if (AutoReleaseTargetAfterApproach)
            {
                ulong releasedGuid = target.Guid;
                _releasedTargetGuids.Add(releasedGuid);
                ReleasedTargetCount++;
                _stickyTargetGuid = 0;
                _activeHotspot = null;
                ResetProgressTracking();
                SetFlowState(
                    QuestFlowState.TargetReleased,
                    $"Target 0x{releasedGuid:X16} reached approach distance and was locally rejected; resume patrol.");
                TreeRoot.StatusText = "Approach complete; target locally rejected for phase-14 client-target approach test.";
                return;
            }

            MovementGoalReached = true;
            MovementStopReason = "target approach distance reached";
            SetFlowState(QuestFlowState.Complete, MovementStopReason);
            TreeRoot.StatusText = MovementStopReason;
            return;
        }

        LastMoveResult = Navigator.MoveTo(target.Location);
        NavigatorMoveCalls++;
        if (LastMoveResult is MoveResult.Failed or MoveResult.PathGenerationFailed)
            AbortMovement("Navigator could not approach the target");
    }

    private void TrackProgress(WoWPoint current)
    {
        if (_lastProgressLocation is null)
        {
            _lastProgressLocation = current;
            _lastProgressAtUtc = DateTime.UtcNow;
            return;
        }

        if (_lastProgressLocation.Value.Distance2D(current) < ProgressResetDistance)
            return;

        _lastProgressLocation = current;
        _lastProgressAtUtc = DateTime.UtcNow;
    }

    private void ResetProgressTracking()
    {
        _lastProgressLocation = StyxWoW.Me?.Location;
        _lastProgressAtUtc = DateTime.UtcNow;
    }

    internal void HandleDestinationReached()
    {
        if (CurrentDecision.Kind is not (QuestDecisionKind.MoveToProfileLocation or QuestDecisionKind.MoveToHotspot))
            return;
        Navigator.Clear();

        if (CurrentDecision.Kind == QuestDecisionKind.MoveToProfileLocation)
        {
            if (!ReferenceEquals(_questOrderProfile, ProfileManager.CurrentProfile) ||
                _profileNodeIndex >= _questOrderProfile.QuestOrder.Count ||
                _questOrderProfile.QuestOrder[_profileNodeIndex] is not MoveToNode move ||
                CurrentDecision.Destination is not WoWPoint destination || move.Destination.Distance(destination) >= 0.001)
            {
                AbortMovement("MoveTo result belongs to a stale profile decision");
                return;
            }
            AdvanceQuestOrder($"MoveTo reached {destination} through Navigator");
            ResetProgressTracking();
            if (_profileNodeIndex < _questOrderProfile.QuestOrder.Count)
            {
                CurrentDecision = new(QuestDecisionKind.None, "MoveTo reached; next QuestOrder node pending.");
                return;
            }
            SetQuestOrderDecision(QuestDecisionKind.ProfileComplete, "All QuestOrder nodes were consumed.");
        }

        if (CurrentDecision.Kind == QuestDecisionKind.MoveToHotspot &&
            ProfileManager.CurrentProfile.GrindArea?.HotspotManager is { Hotspots.Count: > 0 } hotspots)
        {
            HotspotsReached++;
            ReachedHotspotNames.Add(hotspots.CurrentName);

            bool resumedAfterTarget = ReleasedTargetCount > 0 || TargetLostCount > 0;
            if (resumedAfterTarget)
            {
                ResumeHotspotsReached++;
                if (ResumeHotspotsReached >= Math.Max(1, RequiredResumeHotspotsToReach))
                {
                    MovementGoalReached = true;
                    MovementStopReason = "patrol resumed after target release";
                    SetFlowState(QuestFlowState.Complete, MovementStopReason);
                    TreeRoot.StatusText = MovementStopReason;
                    return;
                }
            }

            int previousIndex = hotspots.CurrentIndex;
            _activeHotspot = hotspots.GetNextHotspot();
            if (hotspots.CurrentIndex <= previousIndex)
                RouteWraps++;

            MovementDestination = _activeHotspot;
            ResetProgressTracking();
            TreeRoot.StatusText = $"Hotspot reached; advancing to {hotspots.CurrentName} {_activeHotspot.Value}";
            return;
        }

        MovementGoalReached = true;
        MovementStopReason = "profile destination reached";
        SetFlowState(QuestFlowState.Complete, MovementStopReason);
        TreeRoot.StatusText = MovementStopReason;
    }

    private void SetFlowState(QuestFlowState state, string reason)
    {
        if (FlowState == state)
            return;

        FlowState = state;
        TransitionLog.Add($"{state}: {reason}");
    }

    private void AbortMovement(string reason)
    {
        Navigator.Clear();
        MovementAborted = true;
        MovementStopReason = reason;
        SetFlowState(QuestFlowState.Aborted, reason);
        TreeRoot.StatusText = $"Movement aborted: {reason}";
    }
}
