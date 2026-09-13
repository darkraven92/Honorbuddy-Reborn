using Styx.Logic.Profiles.Quest;

namespace Honorbuddy5875.Runtime;

internal static class TurnInProbeProfile
{
    internal static (TurnInNode Turn, int Index) Validate(OrderNodeCollection order)
    {
        if (order.Count == 1 && order[0] is TurnInNode single && single.QuestId != 0 && single.TurnInId != 0)
            return (single, 0);
        if (order.Count == 2 && order[0] is ObjectiveNode objective && order[1] is TurnInNode turn &&
            objective.QuestId == turn.QuestId && turn.QuestId != 0 && turn.TurnInId != 0)
            return (turn, 1);
        throw new InvalidDataException("This probe requires TurnIn alone, or Objective -> TurnIn for the same quest.");
    }
}
