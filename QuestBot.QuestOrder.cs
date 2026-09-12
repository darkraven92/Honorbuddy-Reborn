using Styx;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.Logic.Questing;

namespace Bots.Quest;

// One internal snapshot per quest per evaluation, shared across adjacent nodes.
// This is a test seam inside QuestBot, not a new public quest-provider API.
internal sealed record QuestOrderSnapshot(bool Active, bool Completed, bool Failed,
    int[] Ids, int[] Required, ushort[] Done, string? Error = null)
{
    internal static QuestOrderSnapshot Missing(string message) =>
        new(false, false, false, [], [], [], message);
}

public sealed partial class QuestBot
{
    private readonly Func<uint, QuestOrderSnapshot> _readQuestOrderState;
    private Profile? _questOrderProfile;
    internal int CurrentProfileNodeIndex => _profileNodeIndex;

    public QuestBot() : this(ReadLiveQuestOrderState) { }
    internal QuestBot(Func<uint, QuestOrderSnapshot> readQuestOrderState)
        => _readQuestOrderState = readQuestOrderState;

    private static QuestOrderSnapshot ReadLiveQuestOrderState(uint id)
    {
        try
        {
            var log = StyxWoW.Me?.QuestLog;
            if (log is null) return QuestOrderSnapshot.Missing("Local player is unavailable.");
            PlayerQuest? quest = log.GetQuestById(id);
            if (quest is null)
                return QuestOrderSnapshot.Missing($"Quest {id} is not active; rewarded/abandoned history is not available.");
            WoWDescriptorQuest descriptor = default;
            if (!quest.GetData(ref descriptor) || descriptor.Id != id || descriptor.ObjectivesDone is not { Length: 4 })
                return QuestOrderSnapshot.Missing($"Quest {id} descriptor is unavailable or changed during the read.");
            // Flags and counters come from this same descriptor snapshot.
            bool completed = (descriptor.Flags & WoWDescriptorQuestFlags.Completed) != 0;
            bool failed = (descriptor.Flags & WoWDescriptorQuestFlags.Failed) != 0;
            try
            {
                return new(true, completed, failed, quest.NormalObjectiveIDs,
                    quest.NormalObjectiveRequiredCounts, descriptor.ObjectivesDone);
            }
            catch (InvalidOperationException ex)
            {
                return new(true, completed, failed, [], [], descriptor.ObjectivesDone, ex.Message);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        { return QuestOrderSnapshot.Missing(ex.Message); }
    }

    // Returns true when the quest-order layer has made a decision. Existing
    // GrindTo/MoveTo nodes continue through the existing movement code.
    private bool EvaluateQuestOrder(Profile profile)
    {
        if (!ReferenceEquals(_questOrderProfile, profile))
        {
            _questOrderProfile = profile;
            _profileNodeIndex = 0;
            _turnInNpcGuid = 0;
            _turnInNpc = null;
        }
        if (profile.QuestOrder.Count == 0) return false;
        var snapshots = new Dictionary<uint, QuestOrderSnapshot>();
        QuestOrderSnapshot Read(uint id)
        {
            if (!snapshots.TryGetValue(id, out var state))
                snapshots[id] = state = _readQuestOrderState(id);
            return state;
        }

        while (_profileNodeIndex < profile.QuestOrder.Count)
        {
            ProfileNode node = profile.QuestOrder[_profileNodeIndex];
            if (node is ObjectiveNode objective)
            {
                if (objective.Type != "KillMob")
                    return SetQuestOrderDecision(QuestDecisionKind.UnsupportedProfileNode,
                        $"Objective type '{objective.Type}' is not implemented; node retained.");
                var state = Read(objective.QuestId);
                if (!state.Active || state.Failed || state.Error is not null)
                    return BlockQuest(objective.QuestId, state);
                if (state.Ids.Length != 4 || state.Required.Length != 4 || state.Done.Length != 4)
                    return SetQuestOrderDecision(QuestDecisionKind.QuestStateBlocked, "Objective arrays are incomplete.");
                int[] slots = Enumerable.Range(0, 4).Where(i =>
                    state.Ids[i] == objective.MobId && state.Required[i] == objective.KillCount).ToArray();
                if (slots.Length != 1)
                    return SetQuestOrderDecision(QuestDecisionKind.QuestStateBlocked,
                        $"Quest {objective.QuestId}: profile MobId/KillCount does not identify one matching WDB slot.");
                int slot = slots[0];
                if (state.Done[slot] < objective.KillCount)
                    return SetQuestOrderDecision(QuestDecisionKind.ObjectiveInProgress,
                        $"Quest {objective.QuestId} objective[{slot}]: entry={objective.MobId} " +
                        $"progress={state.Done[slot]}/{objective.KillCount}; objective combat execution is not connected yet.",
                        objective.MobId);
                AdvanceQuestOrder($"Objective quest={objective.QuestId} entry={objective.MobId} " +
                    $"progress={state.Done[slot]}/{objective.KillCount}");
                continue;
            }
            if (node is PickUpNode pick)
            {
                var state = Read(pick.QuestId);
                if (pick.QuestId == 0 || pick.GiverId == 0)
                    return SetQuestOrderDecision(QuestDecisionKind.QuestStateBlocked, "PickUp has invalid quest/NPC IDs.");
                if (state.Active && !state.Failed)
                {
                    AdvanceQuestOrder($"PickUp quest={pick.QuestId} already active");
                    continue;
                }
                return SetQuestOrderDecision(QuestDecisionKind.QuestInteractionDeferred,
                    $"PickUp quest={pick.QuestId} giver={pick.GiverId}: acceptance is not implemented; node retained.", pick.GiverId);
            }
            if (node is TurnInNode turn)
            {
                if (turn.QuestId == 0 || turn.TurnInId == 0)
                    return SetQuestOrderDecision(QuestDecisionKind.QuestStateBlocked, "TurnIn has invalid quest/NPC IDs.");
                var state = Read(turn.QuestId);
                if (!state.Active || state.Failed)
                    return BlockQuest(turn.QuestId, state);
                if (!state.Completed)
                    return SetQuestOrderDecision(QuestDecisionKind.QuestStateBlocked,
                        $"TurnIn quest={turn.QuestId}: live IsCompleted is false; node retained.", turn.TurnInId);
                if (ResolveQuestGiverLocations)
                    return EvaluateTurnInLocation(turn);
                return SetQuestOrderDecision(QuestDecisionKind.TurnInReady,
                    $"TurnIn ready: quest={turn.QuestId} npc={turn.TurnInId}; interaction is not implemented; node retained.",
                    turn.TurnInId);
            }
            return false;
        }
        return SetQuestOrderDecision(QuestDecisionKind.ProfileComplete, "All supported QuestOrder nodes were consumed.");
    }

    private bool BlockQuest(uint id, QuestOrderSnapshot state) =>
        SetQuestOrderDecision(QuestDecisionKind.QuestStateBlocked,
            state.Failed ? $"Quest {id} failed; node retained." :
            state.Error ?? $"Quest {id} state is unavailable; node retained.");

    private bool SetQuestOrderDecision(QuestDecisionKind kind, string description, uint entry = 0)
    {
        _turnInNpc = null;
        _turnInNpcGuid = 0;
        CurrentDecision = new(kind, description, Entry: entry);
        TreeRoot.StatusText = description;
        return true;
    }

    private void AdvanceQuestOrder(string reason)
    {
        int previous = _profileNodeIndex++;
        TransitionLog.Add($"QuestOrder[{previous}] -> [{_profileNodeIndex}]: {reason}");
        _stickyTargetGuid = 0;
        _preferredInternalTargetGuid = 0;
        _clientTargetSynchronized = false;
        SynchronizedClientTargetGuid = 0;
        _activeHotspot = null;
        if (MovementExecutionEnabled) Navigator.Clear();
    }
}
