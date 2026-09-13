using Honorbuddy5875.Navigation;

namespace Styx.Logic.Pathing;

internal readonly record struct NavigationPosition(WoWPoint Location, int MapId);

public sealed class MeshNavigator : INavigationProvider
{
    private readonly IMeshPathfinder? _mesh;
    private readonly Func<NavigationPosition?> _position;
    private readonly Func<IPlayerMover> _mover;
    private WoWPoint[] _path = [];
    private WoWPoint? _destination;
    private int _next;
    private int _recoveryAttempts;
    private float _precision = 0.35f;
    private IStuckHandler _stuckHandler;

    // Until a map identity and mesh are configured, navigation fails closed.
    public MeshNavigator() : this(null, () => null, () => Navigator.PlayerMover) { }
    internal MeshNavigator(IMeshPathfinder? mesh, Func<NavigationPosition?> position,
        Func<IPlayerMover> mover, Func<DateTime>? clock = null)
    {
        _mesh = mesh;
        _position = position;
        _mover = mover;
        _stuckHandler = new ProgressStuckHandler(position, mover, clock ?? (() => DateTime.UtcNow));
    }

    public float PathPrecision
    {
        get => _precision;
        set
        {
            if (!float.IsFinite(value) || value <= 0 || value > 1) throw new ArgumentOutOfRangeException(nameof(value));
            _precision = value;
        }
    }
    public IStuckHandler StuckHandler
    {
        get => _stuckHandler;
        set { ArgumentNullException.ThrowIfNull(value); Clear(); _stuckHandler = value; }
    }

    internal static bool Finite(WoWPoint p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    private bool Arrived(WoWPoint a, WoWPoint b) => a.Distance2D(b) <= PathPrecision && Math.Abs(a.Z - b.Z) <= 2;
    public WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to) => FindPath(from, to, 4096);
    public bool CanNavigateFully(WoWPoint from, WoWPoint to, int maxHops) => FindPath(from, to, maxHops).Length > 0;
    private WoWPoint[] FindPath(WoWPoint from, WoWPoint to, int maxHops)
    {
        if (_mesh is null || !Finite(from) || !Finite(to) || maxHops is < 1 or > 65535) return [];
        WoWPoint[] path = _mesh.FindPath(from, to, maxHops);
        // Do not reinterpret a partial backend result as a complete route.
        if (path.Length == 0 || path.Any(p => !Finite(p)) ||
            path[0].Distance2D(from) > 0.75 || Math.Abs(path[0].Z - from.Z) > 2 ||
            path[^1].Distance2D(to) > 0.75 || Math.Abs(path[^1].Z - to.Z) > 2) return [];
        return path;
    }

    public MoveResult MoveTo(WoWPoint location)
    {
        try
        {
            NavigationPosition? snapshot = _position();
            if (_mesh is null || snapshot is not NavigationPosition current || current.MapId != _mesh.MapId ||
                !Finite(current.Location) || !Finite(location)) return Fail(MoveResult.Failed);
            WoWPoint here = current.Location;
            bool changed = _destination is not WoWPoint old || old.Distance(location) > 0.25;
            if (changed) { _path = []; _destination = location; _recoveryAttempts = 0; StuckHandler.Reset(); }
            if (_path.Length == 0 || DistanceToCurrentSegment(here) > 2.0)
            {
                _mover().MoveStop();
                _path = GeneratePath(here, location);
                _next = 0;
                if (_path.Length == 0) return Fail(MoveResult.PathGenerationFailed);
            }
            // Arrival is checked only after a full mesh corridor has been established.
            while (_next < _path.Length && Arrived(here, _path[_next])) _next++;
            if (_next == _path.Length)
            {
                Clear();
                return MoveResult.ReachedDestination;
            }
            if (StuckHandler.IsStuck())
            {
                _mover().MoveStop();
                if (_recoveryAttempts++ > 0) return Fail(MoveResult.Failed);
                StuckHandler.Unstick();
                _path = [];
                return MoveResult.UnstuckAttempt;
            }
            _mover().MoveTowards(_path[_next]);
            return MoveResult.Moved;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return Fail(MoveResult.Failed);
        }
    }

    private double DistanceToCurrentSegment(WoWPoint p)
    {
        if (_next >= _path.Length) return double.PositiveInfinity;
        WoWPoint a = _path[Math.Max(0, _next - 1)], b = _path[_next];
        double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
        double length = dx * dx + dy * dy + dz * dz;
        double t = length == 0 ? 0 : Math.Clamp(((p.X-a.X)*dx+(p.Y-a.Y)*dy+(p.Z-a.Z)*dz)/length, 0, 1);
        return p.Distance(new((float)(a.X+t*dx), (float)(a.Y+t*dy), (float)(a.Z+t*dz)));
    }
    private MoveResult Fail(MoveResult result) { Clear(); return result; }
    public bool Clear()
    {
        _path = [];
        _destination = null;
        _next = 0;
        _recoveryAttempts = 0;
        _mover().MoveStop();
        StuckHandler.Reset();
        return true;
    }

    private sealed class ProgressStuckHandler(Func<NavigationPosition?> position, Func<IPlayerMover> mover,
        Func<DateTime> clock) : IStuckHandler
    {
        private WoWPoint? _anchor;
        private DateTime _since;
        public bool IsStuck()
        {
            if (position() is not NavigationPosition current) return true;
            if (_anchor is not WoWPoint anchor || anchor.Distance(current.Location) >= 0.5)
            { _anchor = current.Location; _since = clock(); }
            return clock() - _since >= TimeSpan.FromSeconds(4);
        }
        public void Unstick() { mover().MoveStop(); Reset(); }
        public void Reset() { _anchor = null; _since = clock(); }
    }
}
