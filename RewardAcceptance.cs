namespace Honorbuddy5875.Runtime;

// A short-lived observation transaction, not a persistent rewarded-quest history or packet receipt.
internal sealed class RewardAcceptance
{
    internal uint QuestId { get; }
    internal ulong NpcGuid { get; }
    internal RewardPlayerState Before { get; }
    internal RewardOffer Offer { get; }
    internal bool Submitted { get; private set; }
    private long _submittedAt;

    internal RewardAcceptance(uint quest, ulong npc, RewardPlayerState before, QuestDialogSnapshot dialog)
    {
        QuestId = quest; NpcGuid = npc;
        Before = before with { ActiveIds = before.ActiveIds.ToArray() };
        Offer = dialog.Reward is { } offer ? offer with { FixedItems = offer.FixedItems.ToArray(), Choices = offer.Choices.ToArray() }
            : throw new InvalidOperationException("Live reward data are unavailable.");
        RequireReady(before, dialog);
    }
    internal void RequireReady(RewardPlayerState state, QuestDialogSnapshot dialog)
    {
        state.RequireValid();
        if (Submitted) throw new InvalidOperationException("Acceptance was already submitted; automatic retries are disabled.");
        if (!Before.SameAs(state) || !state.ActiveIds.Contains(QuestId) || !state.QuestCompleted || state.QuestFailed)
            throw new InvalidOperationException("Player XP or completed active quest changed before acceptance.");
        if (!QuestRewardDialogFlow.IsReward(dialog, NpcGuid, QuestId) || !Offer.SameAs(dialog.Reward))
            throw new InvalidOperationException("The expected reward offer must remain open and unchanged.");
        if (Offer.Choices.Length != 0)
            throw new InvalidOperationException("This phase supports no-choice rewards only; no reward choice was made.");
    }
    internal void MarkSubmitted()
    {
        if (Submitted) throw new InvalidOperationException("Acceptance cannot be submitted twice.");
        Submitted = true; _submittedAt = Environment.TickCount64;
    }
    internal bool Observe(RewardPlayerState after, QuestDialogSnapshot dialog, out ulong xpGain)
    {
        xpGain = 0;
        if (!Submitted) throw new InvalidOperationException("No acceptance command was submitted in this transaction.");
        if (Environment.TickCount64 - _submittedAt > 12000) throw new TimeoutException("Reward observation window expired.");
        after.RequireValid();
        if (after.Pid != Before.Pid || after.Guid != Before.Guid || after.QuestFailed)
            throw new InvalidOperationException("Player session changed or quest failed.");
        var expectedOthers = Before.ActiveIds.Where(id => id != QuestId);
        if (!expectedOthers.SequenceEqual(after.ActiveIds.Where(id => id != QuestId)))
            throw new InvalidOperationException("Another active quest changed during acceptance.");
        // A level-up can reset XP. One level-up is supported; more cannot be measured from these two snapshots.
        if (after.Level == Before.Level && after.NextXp == Before.NextXp && after.Xp >= Before.Xp)
            xpGain = after.Xp - Before.Xp;
        else if (after.Level == Before.Level + 1)
            xpGain = (ulong)Before.NextXp - Before.Xp + after.Xp;
        else
            throw new InvalidOperationException("XP/level transition cannot be measured consistently.");
        if (dialog.NpcGuid != 0 && (dialog.NpcGuid != NpcGuid || dialog.QuestId != QuestId || dialog.RawStage != 3))
            throw new InvalidOperationException("A different quest dialog opened during acceptance.");
        if (dialog.GossipNpcGuid != 0 && dialog.GossipNpcGuid != NpcGuid)
            throw new InvalidOperationException("A different gossip conversation opened during acceptance.");
        return xpGain > 0 && !after.ActiveIds.Contains(QuestId) && dialog.Closed;
    }
}
