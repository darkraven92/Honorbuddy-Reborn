using Honorbuddy5875.Runtime;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;

namespace Bots.Quest;

public sealed partial class QuestBot
{
    private RewardAcceptance? _rewardAttempt;
    private Profile? _rewardProfile;
    private int _rewardIndex;

    internal void BeginRewardAcceptance(RewardAcceptance attempt)
    {
        var profile = ProfileManager.CurrentProfile;
        if (_rewardAttempt is not null || attempt.Submitted || !ReferenceEquals(profile, _questOrderProfile) ||
            _profileNodeIndex >= profile.QuestOrder.Count || profile.QuestOrder[_profileNodeIndex] is not TurnInNode turn ||
            turn.QuestId != attempt.QuestId || CurrentDecision.Kind != QuestDecisionKind.QuestGiverInRange ||
            CurrentQuestGiver is not { } npc || npc.Entry != turn.TurnInId || npc.Guid != attempt.NpcGuid)
            throw new InvalidOperationException("Acceptance must belong to this bot's current in-range TurnIn node.");
        _rewardAttempt = attempt; _rewardProfile = profile; _rewardIndex = _profileNodeIndex;
    }

    internal bool ConfirmRewardAcceptance(RewardAcceptance attempt, RewardPlayerState after, QuestDialogSnapshot dialog, out ulong xpGain)
    {
        if (!ReferenceEquals(_rewardAttempt, attempt) || !ReferenceEquals(ProfileManager.CurrentProfile, _rewardProfile) ||
            !ReferenceEquals(_questOrderProfile, _rewardProfile) || _profileNodeIndex != _rewardIndex ||
            _rewardProfile.QuestOrder[_rewardIndex] is not TurnInNode turn || turn.QuestId != attempt.QuestId)
            throw new InvalidOperationException("Reward transaction is stale, consumed, or belongs to a different profile/node.");
        if (!attempt.Observe(after, dialog, out xpGain)) return false;
        _rewardAttempt = null;
        AdvanceQuestOrder($"TurnIn quest={attempt.QuestId}: one acceptance submission followed by quest removal, closed dialog and XP +{xpGain}");
        return true;
    }
}
