using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx.Logic.Pathing;

public enum MoveResult
{
    Moved,
    ReachedDestination,
    Failed
}

public abstract class PlayerMover : IDisposable
{
    public abstract void Move(WoWMovement.MovementDirection direction);
    public abstract void MoveStop(WoWMovement.MovementDirection direction);
    public abstract void MoveStop();
    public virtual void MoveTowards(WoWPoint location) => Navigator.MoveDirectTowards(location);
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
    private static PlayerMover _playerMover = new NullPlayerMover();

    public static PlayerMover PlayerMover
    {
        get => _playerMover;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_playerMover, value))
                return;
            _playerMover.MoveStop();
            _playerMover = value;
        }
    }

    public static float PathPrecision { get; set; } = 0.75f;
    public static WoWPoint? CurrentDestination { get; private set; }
    public static long MoveToCalls { get; private set; }

    public static MoveResult MoveTo(WoWPoint destination)
    {
        MoveToCalls++;
        CurrentDestination = destination;
        LocalPlayer? me = Styx.StyxWoW.Me;
        if (me is null || !me.IsValid)
            return MoveResult.Failed;

        if (me.Location.Distance2D(destination) <= PathPrecision)
        {
            PlayerMover.MoveStop();
            return MoveResult.ReachedDestination;
        }

        MoveDirectTowards(destination);
        return MoveResult.Moved;
    }

    internal static void MoveDirectTowards(WoWPoint destination)
    {
        LocalPlayer? me = Styx.StyxWoW.Me;
        if (me is null || !me.IsValid)
        {
            PlayerMover.MoveStop();
            return;
        }

        double desired = Math.Atan2(destination.Y - me.Y, destination.X - me.X);
        double delta = NormalizeRadians(desired - me.Rotation);
        const double turnThreshold = 0.18;

        if (delta > turnThreshold)
        {
            PlayerMover.MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.TurnRight);
            PlayerMover.Move(WoWMovement.MovementDirection.TurnLeft);
        }
        else if (delta < -turnThreshold)
        {
            PlayerMover.MoveStop(WoWMovement.MovementDirection.Forward | WoWMovement.MovementDirection.TurnLeft);
            PlayerMover.Move(WoWMovement.MovementDirection.TurnRight);
        }
        else
        {
            PlayerMover.MoveStop(WoWMovement.MovementDirection.TurnLeft | WoWMovement.MovementDirection.TurnRight);
            PlayerMover.Move(WoWMovement.MovementDirection.Forward);
        }
    }

    public static void Clear()
    {
        CurrentDestination = null;
        PlayerMover.MoveStop();
    }

    public static List<WoWPoint> GeneratePath(WoWPoint destination)
    {
        LocalPlayer? me = Styx.StyxWoW.Me;
        return me is null ? new List<WoWPoint>() : new List<WoWPoint> { me.Location, destination };
    }

    internal static double NormalizeRadians(double value)
    {
        while (value > Math.PI) value -= Math.PI * 2.0;
        while (value < -Math.PI) value += Math.PI * 2.0;
        return value;
    }
}
