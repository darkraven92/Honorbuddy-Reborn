namespace Styx.WoWInternals;

public static class WoWMovement
{
    [Flags]
    public enum MovementDirection
    {
        None = 0,
        Forward = 1 << 0,
        Backwards = 1 << 1,
        TurnLeft = 1 << 2,
        TurnRight = 1 << 3,
        StrafeLeft = 1 << 4,
        StrafeRight = 1 << 5,
        JumpAscend = 1 << 6,
        Descend = 1 << 7
    }

    public static void Move(MovementDirection direction)
        => Styx.Logic.Pathing.Navigator.PlayerMover.Move(direction);

    public static void MoveStop(MovementDirection direction)
    {
        if (Styx.Logic.Pathing.Navigator.PlayerMover is Styx.Logic.Pathing.PlayerMover keyboardMover)
            keyboardMover.MoveStop(direction);
        else
            Styx.Logic.Pathing.Navigator.PlayerMover.MoveStop();
    }

    public static void MoveStop()
        => Styx.Logic.Pathing.Navigator.PlayerMover.MoveStop();
}
