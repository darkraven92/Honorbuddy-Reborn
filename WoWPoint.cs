namespace Styx.Logic.Pathing;

public struct WoWPoint
{
    public float X;
    public float Y;
    public float Z;

    public WoWPoint(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double Distance(WoWPoint other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        double dz = other.Z - Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public double DistanceSqr(WoWPoint other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        double dz = other.Z - Z;
        return dx * dx + dy * dy + dz * dz;
    }

    public double Distance2D(WoWPoint other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public double Distance2DSqr(WoWPoint other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        return dx * dx + dy * dy;
    }

    public override readonly string ToString() => $"<{X:F3}, {Y:F3}, {Z:F3}>";
}
