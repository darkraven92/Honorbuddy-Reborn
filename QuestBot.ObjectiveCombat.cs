using Bots.Grind;
using Honorbuddy5875.Combat;
using Honorbuddy5875.Movement;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.Quest;

internal sealed record ObjectiveTargetSnapshot(
    ulong Guid,
    uint Entry,
    WoWPoint Location,
    double Distance,
    bool Alive,
    bool StrictHostile,
    bool NeutralPotential = false);

public sealed partial class QuestBot
{
    private const int ObjectiveCreditSettleMilliseconds = 3000;
    private const double ObjectiveMaximumTargetDistance = 60.0;

    private ObjectiveTargetSnapshot? _objectiveTarget;
    private ulong _objectiveTargetGuid;
    private uint _objectiveQuestId;
    private uint _objectiveEntry;
    private ushort _objectiveProgressBaseline;
    private bool _awaitingObjectiveCredit;
    private long _objectiveCreditDeadline;
    private MinimalAutoAttackRoutine? _objectiveCombatRoutine;
    private bool _objectiveTargetFilterAttached;

    public bool ObjectiveCombatExecutionEnabled { get; set; }
    public UInputCombatActions? ObjectiveCombatInput { get; set; }
    public int ObjectiveCombatStarts { get; private set; }
    public int ObjectiveTargetsCompleted { get; private set; }
    public ulong ObjectiveTargetGuid => _objectiveTargetGuid;
    public bool AwaitingObjectiveCredit => _awaitingObjectiveCredit;

    internal Func<uint, ulong, ObjectiveTargetSnapshot?> ReadObjectiveTarget { get; set; }
        = ReadLiveObjectiveTarget;

    private static ObjectiveTargetSnapshot? ReadLiveObjectiveTarget(uint requiredEntry, ulong preferredGuid)
    {
        var candidates = new List<ObjectiveTargetSnapshot>();

        foreach (WoWUnit unit in Targeting.Instance.TargetList)
        {
            try
            {
                if (!unit.IsValid)
                    continue;

                candidates.Add(new ObjectiveTargetSnapshot(
                    unit.Guid,
                    unit.Entry,
                    unit.Location,
                    unit.Distance2D,
                    unit.IsAlive,
                    unit.IsStrictHostileCombatCandidate,
                    unit.IsNeutralPotentialCombatCandidate));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
            }
        }

        return SelectObjectiveTarget(requiredEntry, preferredGuid, candidates);
    }

    internal static ObjectiveTargetSnapshot? SelectObjectiveTarget(
        uint requiredEntry,
        ulong preferredGuid,
        IEnumerable<ObjectiveTargetSnapshot> candidates)
    {
        return candidates
            .Where(candidate => UsableObjectiveTarget(requiredEntry, candidate))
            .OrderByDescending(candidate => preferredGuid != 0 && candidate.Guid == preferredGuid)
            .ThenBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Guid)
            .FirstOrDefault();
    }

    private static bool UsableObjectiveTarget(uint requiredEntry, ObjectiveTargetSnapshot target)
        => requiredEntry != 0 &&
           target.Guid != 0 &&
           target.Entry == requiredEntry &&
           target.Alive &&
           (target.StrictHostile || target.NeutralPotential) &&
           double.IsFinite(target.Distance) &&
           target.Distance >= 0 &&
           float.IsFinite(target.Location.X) &&
           float.IsFinite(target.Location.Y) &&
           float.IsFinite(target.Location.Z);

    private bool EvaluateKillObjective(ObjectiveNode objective, ushort progress)
    {
        if (RetainWhileAwaitingObjectiveCredit(objective, progress))
            return true;

        ObjectiveTargetSnapshot? target = ReadObjectiveTarget(objective.MobId, _objectiveTargetGuid);

        _objectiveQuestId = objective.QuestId;
        _objectiveEntry = objective.MobId;
        _objectiveProgressBaseline = progress;

        if (target is null || !UsableObjectiveTarget(objective.MobId, target))
        {
            _objectiveTarget = null;
            _objectiveTargetGuid = 0;

            return SetObjectiveDecision(
                $"Quest {objective.QuestId}: entry={objective.MobId} " +
                $"progress={progress}/{objective.KillCount}; " +
                "no eligible objective target is currently present in Targeting.");
        }

        _objectiveTarget = target;
        _objectiveTargetGuid = target.Guid;

        return SetObjectiveDecision(
            $"Quest {objective.QuestId}: entry={objective.MobId} " +
            $"progress={progress}/{objective.KillCount}; " +
            $"objective target=0x{target.Guid:X16} distance={target.Distance:F2}.",
            target.Guid,
            target.Location);
    }

    private bool RetainWhileAwaitingObjectiveCredit(ObjectiveNode objective, ushort progress)
    {
        if (!_awaitingObjectiveCredit)
            return false;

        if (_objectiveQuestId != objective.QuestId || _objectiveEntry != objective.MobId)
        {
            ClearObjectiveCreditWait();
            return false;
        }

        if (progress > _objectiveProgressBaseline)
        {
            ClearObjectiveCreditWait();
            return false;
        }

        if (Environment.TickCount64 >= _objectiveCreditDeadline)
        {
            ClearObjectiveCreditWait();
            return false;
        }

        return SetObjectiveDecision(
            $"Quest {objective.QuestId}: entry={objective.MobId} remains " +
            $"{progress}/{objective.KillCount}; awaiting authoritative quest credit after target death.");
    }

    private bool SetObjectiveDecision(
        string description,
        ulong targetGuid = 0,
        WoWPoint? destination = null)
    {
        _turnInNpc = null;
        _turnInNpcGuid = 0;

        CurrentDecision = new QuestDecision(
            QuestDecisionKind.ObjectiveInProgress,
            description,
            targetGuid,
            _objectiveEntry,
            destination);

        TreeRoot.StatusText = description;
        return true;
    }

    private void ExecuteObjectiveCombatDecision()
    {
        if (!MovementExecutionEnabled)
        {
            Navigator.Clear();
            return;
        }

        if (CurrentDecision.TargetGuid == 0 || CurrentDecision.Entry == 0)
        {
            Navigator.Clear();
            return;
        }

        if (ClientTargetInput is null)
        {
            AbortMovement("objective combat is armed but ClientTargetInput is unavailable");
            return;
        }

        if (ObjectiveCombatInput is null)
        {
            AbortMovement("objective combat is armed but ObjectiveCombatInput is unavailable");
            return;
        }

        if (_objectiveCombatRoutine is null)
        {
            Profile profile = ProfileManager.CurrentProfile;

            ClientTargetSelectionResult selection = ClientTargetSelector5875.SelectProfileHostile(
                ClientTargetInput,
                profile,
                CurrentDecision.TargetGuid,
                MaximumClientTabAttempts,
                150,
                0.0,
                ObjectiveMaximumTargetDistance,
                CurrentDecision.Entry);

            LastClientTargetSelection = selection;
            ClientTargetSyncAttempts++;

            if (!selection.Accepted || selection.FinalGuid == 0)
            {
                ClientTargetSyncFailures++;
                AbortMovement(
                    $"objective client-target synchronization failed for entry " +
                    $"{CurrentDecision.Entry}: {selection.StopReason}");
                return;
            }

            ObjectManager.Update();

            LocalPlayer? me = StyxWoW.Me;
            WoWUnit? selected = ObjectManager.GetObjectByGuid<WoWUnit>(selection.FinalGuid);

            if (me is null ||
                selected is null ||
                !selected.IsValid ||
                !selected.IsAlive ||
                selected.Entry != CurrentDecision.Entry ||
                !LevelBot.IsQuestObjectiveTargetCandidate(
                    selected,
                    profile,
                    CurrentDecision.Entry))
            {
                ClientTargetSyncFailures++;
                AbortMovement("objective client target failed entry/profile/hostile validation");
                return;
            }

            _objectiveTargetGuid = selected.Guid;
            _objectiveTarget = new ObjectiveTargetSnapshot(
                selected.Guid,
                selected.Entry,
                selected.Location,
                selected.Distance2D,
                selected.IsAlive,
                selected.IsStrictHostileCombatCandidate,
                selected.IsNeutralPotentialCombatCandidate);

            _clientTargetSynchronized = true;
            SynchronizedClientTargetGuid = selected.Guid;
            ClientTargetSyncSuccesses++;

            _objectiveCombatRoutine =
                new MinimalAutoAttackRoutine(
                    selected.Guid,
                    ObjectiveCombatInput,
                    allowNeutralPotential: true);

            RoutineManager.SetCurrent(_objectiveCombatRoutine);
            _objectiveCombatRoutine.Initialize();
            ObjectiveCombatStarts++;
        }

        _objectiveCombatRoutine.Pulse();

        if (_objectiveCombatRoutine.State == MinimalCombatState.Aborted)
        {
            string reason = _objectiveCombatRoutine.StopReason;
            DisposeObjectiveRoutine();
            AbortMovement($"objective combat aborted: {reason}");
            return;
        }

        if (_objectiveCombatRoutine.State == MinimalCombatState.TargetDead)
        {
            ObjectiveTargetsCompleted++;

            MarkObjectiveTargetDeathObserved(
                _objectiveQuestId,
                _objectiveEntry,
                _objectiveProgressBaseline);

            TreeRoot.StatusText =
                $"Objective target died; waiting for quest {_objectiveQuestId} " +
                $"progress beyond {_objectiveProgressBaseline}.";
        }
    }

    internal void MarkObjectiveTargetDeathObserved(
        uint questId,
        uint entry,
        ushort progressBaseline)
    {
        if (questId == 0)
            throw new ArgumentOutOfRangeException(nameof(questId));

        if (entry == 0)
            throw new ArgumentOutOfRangeException(nameof(entry));

        DisposeObjectiveRoutine();
        Navigator.Clear();

        _objectiveQuestId = questId;
        _objectiveEntry = entry;
        _objectiveProgressBaseline = progressBaseline;
        _objectiveTarget = null;
        _objectiveTargetGuid = 0;
        _awaitingObjectiveCredit = true;
        _objectiveCreditDeadline =
            checked(Environment.TickCount64 + ObjectiveCreditSettleMilliseconds);
    }

    private void ClearObjectiveCreditWait()
    {
        _awaitingObjectiveCredit = false;
        _objectiveCreditDeadline = 0;
    }

    private void DisposeObjectiveRoutine()
    {
        MinimalAutoAttackRoutine? routine = _objectiveCombatRoutine;
        _objectiveCombatRoutine = null;

        if (routine is null)
            return;

        try { routine.ShutDown(); } catch { }
        try { routine.Dispose(); } catch { }
    }

    private void AttachObjectiveTargetFilter()
    {
        if (_objectiveTargetFilterAttached)
            return;

        Targeting.Instance.IncludeTargetsFilter += IncludeQuestObjectiveTargets;
        _objectiveTargetFilterAttached = true;
    }

    private void DetachObjectiveTargetFilter()
    {
        if (!_objectiveTargetFilterAttached)
            return;

        Targeting.Instance.IncludeTargetsFilter -= IncludeQuestObjectiveTargets;
        _objectiveTargetFilterAttached = false;
    }

    private void IncludeQuestObjectiveTargets(
        List<WoWObject> incomingUnits,
        HashSet<WoWObject> outgoingUnits)
    {
        uint requiredEntry = _objectiveEntry;
        if (requiredEntry == 0)
            return;

        Profile profile = ProfileManager.CurrentProfile;

        foreach (WoWUnit unit in incomingUnits.OfType<WoWUnit>())
        {
            if (LevelBot.IsQuestObjectiveTargetCandidate(
                    unit,
                    profile,
                    requiredEntry))
                outgoingUnits.Add(unit);
        }
    }

    private void ResetObjectiveCombatState(bool resetCounters = false)
    {
        DisposeObjectiveRoutine();
        ClearObjectiveCreditWait();

        _objectiveTarget = null;
        _objectiveTargetGuid = 0;
        _objectiveQuestId = 0;
        _objectiveEntry = 0;
        _objectiveProgressBaseline = 0;

        if (resetCounters)
        {
            ObjectiveCombatStarts = 0;
            ObjectiveTargetsCompleted = 0;
        }
    }
}
