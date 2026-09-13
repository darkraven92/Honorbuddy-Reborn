namespace Styx.Logic.Pathing;

// Signatures verified against Honorbuddy 2.0.0.5999 metadata; backend is Vanilla-specific.
public interface IPlayerMover
{
    void Move(Styx.WoWInternals.WoWMovement.MovementDirection direction);
    void MoveTowards(WoWPoint location);
    void MoveStop();
}

public interface IStuckHandler
{
    bool IsStuck();
    void Unstick();
    void Reset();
}

public interface INavigationProvider
{
    MoveResult MoveTo(WoWPoint location);
    float PathPrecision { get; set; }
    bool Clear();
    WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to);
    bool CanNavigateFully(WoWPoint from, WoWPoint to, int maxHops);
    IStuckHandler StuckHandler { get; set; }
}
