namespace Honorbuddy5875.Runtime;

internal sealed record QuestRewardDialogResult(bool Interacted, bool Selected, bool Continued);

// The same bounded flow is exercised by the live probe and synthetic dialog transitions.
// It has no action for GetQuestReward/reward acceptance and never consumes a profile node.
internal static class QuestRewardDialogFlow
{
    internal static void CheckIdentity(QuestDialogSnapshot s, ulong npc, uint quest)
    {
        if (npc == 0 || quest == 0) throw new InvalidOperationException("Expected NPC and quest IDs must be nonzero.");
        if ((s.NpcGuid != 0 && s.NpcGuid != npc) || (s.GossipNpcGuid != 0 && s.GossipNpcGuid != npc))
            throw new InvalidOperationException("A different NPC conversation is open.");
        if (s.NpcGuid != 0 && s.RawStage != 0 && s.QuestId != quest)
            throw new InvalidOperationException("A different quest is shown.");
        if (s.NpcGuid != 0 && s.RawStage is not (0 or 2 or 3))
            throw new InvalidOperationException("Quest dialog is not a turn-in progress/reward conversation.");
    }
    internal static bool IsReward(QuestDialogSnapshot s, ulong npc, uint quest) =>
        npc != 0 && quest != 0 && s.NpcGuid == npc && s.QuestId == quest && s.RawStage == 3 &&
        s.RequestPending == 0 && (s.GossipNpcGuid == 0 || s.GossipNpcGuid == npc);

    internal static void RequireProgress(QuestDialogSnapshot s, ulong npc, uint quest)
    {
        CheckIdentity(s, npc, quest);
        if (s.NpcGuid != npc || s.QuestId != quest || s.RawStage != 2 || s.RequestPending != 0)
            throw new InvalidOperationException("Expected progress dialog must be open with no request pending.");
    }

    internal static QuestRewardDialogResult Execute(ulong npc, uint quest,
        Func<QuestDialogSnapshot> read, Action validatePlayerQuestNpc,
        Action interact, Action<GossipSelectionPlan> select, Action continueQuest,
        Func<Func<QuestDialogSnapshot, bool>, QuestDialogSnapshot> waitFor)
    {
        bool interacted = false, selected = false, continued = false;
        QuestDialogSnapshot Checked(QuestDialogSnapshot snapshot)
        { CheckIdentity(snapshot, npc, quest); return snapshot; }
        QuestDialogSnapshot Wait(Func<QuestDialogSnapshot, bool> predicate) =>
            Checked(waitFor(s => { CheckIdentity(s, npc, quest); return predicate(s); }));
        validatePlayerQuestNpc();
        var state = Checked(read());
        if (state.Closed)
        {
            validatePlayerQuestNpc();
            interact(); interacted = true;
            state = Wait(s => !s.Closed);
        }
        if (state.NpcGuid == 0 && state.GossipNpcGuid == npc)
        {
            var plan = GossipSelectionPlan.Create(state, npc, quest);
            validatePlayerQuestNpc();
            select(plan); selected = true;
            state = Wait(s => s.NpcGuid == npc && s.QuestId == quest && s.RawStage is 2 or 3);
        }
        if (state.NpcGuid == npc && state.RawStage == 2)
        {
            RequireProgress(state, npc, quest);
            validatePlayerQuestNpc();
            continueQuest(); continued = true;
            state = Wait(s => IsReward(s, npc, quest));
        }
        if (!IsReward(state, npc, quest))
            throw new InvalidOperationException("Expected reward offer did not open; a quest greeting list is not handled by this probe.");
        validatePlayerQuestNpc();
        if (!IsReward(Checked(read()), npc, quest))
            throw new InvalidOperationException("Reward conversation changed during final validation.");
        return new(interacted, selected, continued);
    }
}
