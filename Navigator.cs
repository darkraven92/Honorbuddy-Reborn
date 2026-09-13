using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Pathing;

public enum MoveResult
{
    Failed = 0,
    ReachedDestination = 1,
    PathGenerationFailed = 2,
    PathGenerated = 3,
    UnstuckAttempt = 4,
    Moved = 5
}

// Compatibility base for the existing keyboard mover, implementing the original interface.
public abstract class PlayerMover : IPlayerMover, IDisposable
{
    public abstract void Move(WoWMovement.MovementDirection direction);
    public abstract void MoveStop(WoWMovement.MovementDirection direction);
    public abstract void MoveStop();
    public virtual void MoveTowards(WoWPoint location)
    {
        LocalPlayer? me = Styx.StyxWoW.Me;
        if (me is null || !me.IsValid || !MeshNavigator.Finite(location)) { MoveStop(); return; }
        double delta = Math.IEEERemainder(Math.Atan2(location.Y - me.Y, location.X - me.X) - me.Rotation, Math.PI * 2);
        if (!double.IsFinite(delta)) { MoveStop(); return; }
        const double threshold = 0.18;
        if (delta > threshold)
        {
            MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.TurnRight);
            Move(WoWMovement.MovementDirection.TurnLeft);
        }
        else if (delta < -threshold)
        {
            MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.TurnLeft);
            Move(WoWMovement.MovementDirection.TurnRight);
        }
        else
        {
            MoveStop(WoWMovement.MovementDirection.TurnLeft | WoWMovement.MovementDirection.TurnRight);
            Move(WoWMovement.MovementDirection.Forward);
        }
    }
    public virtual void Dispose() => MoveStop();
}

public sealed class NullPlayerMover : PlayerMover
{
    public override void Move(WoWMovement.MovementDirection direction) { }
    public override void MoveStop(WoWMovement.MovementDirection direction) { }
    public override void MoveStop() { }
}

public static class Navigator
{
    private static IPlayerMover _playerMover = new NullPlayerMover();
    private static INavigationProvider _navigationProvider = new MeshNavigator();

    public static IPlayerMover PlayerMover
    {
        get => _playerMover;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_playerMover, value)) return;
            Clear();
            _playerMover = value;
        }
    }

    public static INavigationProvider NavigationProvider
    {
        get => _navigationProvider;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_navigationProvider, value)) return;
            Clear();
            _navigationProvider = value;
        }
    }

    public static float PathPrecision { get => NavigationProvider.PathPrecision; set => NavigationProvider.PathPrecision = value; }
    public static WoWPoint? CurrentDestination { get; private set; }
    public static long MoveToCalls { get; private set; }
    public static T? GetNavigationProviderAs<T>() where T : class, INavigationProvider => NavigationProvider as T;

    public static MoveResult MoveTo(WoWPoint destination)
    {
        MoveToCalls++;
        CurrentDestination = destination;
        return NavigationProvider.MoveTo(destination);
    }

    public static bool Clear()
    {
        CurrentDestination = null;
        try { return NavigationProvider.Clear(); }
        finally { PlayerMover.MoveStop(); }
    }

    public static WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to) => NavigationProvider.GeneratePath(from, to);
    public static List<WoWPoint> GeneratePath(WoWPoint destination)
    {
        LocalPlayer? me = Styx.StyxWoW.Me;
        return me is null || !me.IsValid ? [] : [.. GeneratePath(me.Location, destination)];
    }
    public static bool CanNavigateFully(WoWPoint from, WoWPoint to) => CanNavigateFully(from, to, 4096);
    public static bool CanNavigateFully(WoWPoint from, WoWPoint to, int maxHops) => NavigationProvider.CanNavigateFully(from, to, maxHops);
}
