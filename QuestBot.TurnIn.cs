using Styx;
using Styx.Logic.BehaviorTree;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Bots.Quest;

internal sealed record QuestGiverSnapshot(ulong Guid, uint Entry, WoWPoint Location,
    double Distance, double Distance2D, float InteractRange, bool Alive, bool PlayerControlled, bool InCombat);

public sealed partial class QuestBot
{
    private ulong _turnInNpcGuid;
    private QuestGiverSnapshot? _turnInNpc;
    // Internal switches/injection are for diagnostics; normal QuestBot resolves NPCs.
    internal bool ResolveQuestGiverLocations { get; set; } = true;
    internal Func<uint, ulong, QuestGiverSnapshot?> ReadQuestGiver { get; set; } = ReadLiveQuestGiver;
    internal QuestGiverSnapshot? CurrentQuestGiver => _turnInNpc;

    private static QuestGiverSnapshot? ReadLiveQuestGiver(uint entry, ulong preferredGuid)
    {
        var me = StyxWoW.Me;
        if (me is null || !me.IsValid) return null;
        WoWPoint player = me.Location;
        if (!Finite(player)) return null;
        var candidates = new List<QuestGiverSnapshot>();
        foreach (var unit in ObjectManager.GetObjectsOfType<WoWUnit>(false, false))
        {
            try
            {
                if (!unit.IsValid || unit.Entry != entry) continue;
                var position = unit.Location;
                candidates.Add(new(unit.Guid, unit.Entry, position, position.Distance(player),
                    position.Distance2D(player), unit.InteractRange, unit.IsAlive, unit.IsPlayerControlled, unit.Combat));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                // An object may disappear between enumeration and field reads.
            }
        }
        return SelectQuestGiver(entry, preferredGuid, candidates);
    }

    internal static QuestGiverSnapshot? SelectQuestGiver(uint entry, ulong preferredGuid,
        IEnumerable<QuestGiverSnapshot> candidates) => candidates
        .Where(n => UsableQuestGiver(entry, n))
        .OrderByDescending(n => preferredGuid != 0 && n.Guid == preferredGuid)
        .ThenBy(n => n.Distance).ThenBy(n => n.Guid).FirstOrDefault();

    private static bool Finite(WoWPoint p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private static bool UsableQuestGiver(uint entry, QuestGiverSnapshot n) =>
        n.Guid != 0 && n.Entry == entry && n.Alive && !n.PlayerControlled && !n.InCombat &&
        Finite(n.Location) && double.IsFinite(n.Distance) && n.Distance >= 0 &&
        double.IsFinite(n.Distance2D) && n.Distance2D >= 0 &&
        float.IsFinite(n.InteractRange) && n.InteractRange > 0;

    private bool EvaluateTurnInLocation(TurnInNode turn)
    {
        QuestGiverSnapshot? npc = ReadQuestGiver(turn.TurnInId, _turnInNpcGuid);
        if (npc is null || !UsableQuestGiver(turn.TurnInId, npc))
        {
            _turnInNpcGuid = 0;
            _turnInNpc = null;
            return SetQuestOrderDecision(QuestDecisionKind.QuestGiverUnavailable,
                $"TurnIn quest={turn.QuestId}: no usable live NPC entry={turn.TurnInId} in ObjectManager; node retained.",
                turn.TurnInId);
        }
        _turnInNpc = npc;
        _turnInNpcGuid = npc.Guid;
        // Stop half a yard inside the model's interaction radius to avoid edge oscillation.
        double stopDistance = Math.Max(0.1, npc.InteractRange - 0.5);
        QuestDecisionKind kind;
        string description;
        if (npc.Distance < npc.InteractRange &&
            (npc.Distance <= stopDistance || npc.Distance2D <= Navigator.PathPrecision))
        {
            kind = QuestDecisionKind.QuestGiverInRange;
            description = $"TurnIn quest={turn.QuestId}: NPC {npc.Entry} within modeled interaction range " +
                $"({npc.Distance:F2}/{npc.InteractRange:F2}); interaction pending; node retained.";
        }
        else
        {
            kind = QuestDecisionKind.MoveToQuestGiver;
            description = $"Approach turn-in NPC {npc.Entry}: guid=0x{npc.Guid:X16} " +
                $"distance={npc.Distance:F2}, destination={npc.Location}.";
        }
        CurrentDecision = new(kind, description, npc.Guid, npc.Entry, npc.Location);
        TreeRoot.StatusText = description;
        return true;
    }

    private void ExecuteQuestGiverApproach()
    {
        var npc = _turnInNpc;
        if (npc is null || npc.Guid != CurrentDecision.TargetGuid ||
            CurrentDecision.Destination is not WoWPoint destination)
        {
            AbortMovement("quest giver snapshot is unavailable");
            return;
        }
        MovementDestination = destination;
        LastMoveResult = Navigator.MoveTo(destination);
        NavigatorMoveCalls++;
        if (LastMoveResult is MoveResult.Failed or MoveResult.PathGenerationFailed)
            AbortMovement("Navigator could not approach the quest giver");
        // Reaching this point never consumes TurnIn or marks the quest rewarded.
        // The next object pulse refreshes the distance and stops movement in range.
    }
}
