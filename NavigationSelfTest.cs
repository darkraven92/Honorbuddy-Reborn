using Honorbuddy5875.Navigation;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

internal static class NavigationSelfTest
{
    internal static int Run(string? meshDirectory)
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
        var oldProvider = Navigator.NavigationProvider;
        var oldMover = Navigator.PlayerMover;
        try
        {
            var mover = new RecordingMover();
            NavigationPosition? position = new(new(0, 0, 0), 1);
            DateTime now = DateTime.UnixEpoch;
            var mesh = new FakeMesh();
            var nav = new MeshNavigator(mesh, () => position, () => mover, () => now);
            Navigator.PlayerMover = mover;
            Navigator.NavigationProvider = nav;
            WoWPoint goal = new(10, 10, 0);
            mesh.Path = [new(0, 0, 0), new(0, 10, 0), goal];
            Check(Navigator.MoveTo(goal) == MoveResult.Moved && mover.Target?.Y == 10 && mover.Target?.X == 0, "follow first corridor corner, not goal");
            Check(Navigator.MoveTo(goal) == MoveResult.Moved && mesh.Queries == 1, "reuse corridor");
            position = new(new(0, 10, 0), 1);
            Check(Navigator.MoveTo(goal) == MoveResult.Moved && mover.Target?.X == 10, "advance corner in order");
            position = new(goal, 1);
            Check(Navigator.MoveTo(goal) == MoveResult.ReachedDestination && mover.Target is null, "stop on arrival");
            position = new(new(0, 0, 0), 1);
            Navigator.MoveTo(goal); int queries = mesh.Queries;
            mesh.Path = [new(0, 0, 0), new(4, 0, 0)];
            Check(Navigator.MoveTo(new(4, 0, 0)) == MoveResult.Moved && mesh.Queries == queries + 1, "replan changed goal");
            mesh.Path = [];
            Check(Navigator.MoveTo(new(99, 0, 0)) == MoveResult.PathGenerationFailed && mover.Target is null, "missing path stops; no fallback");
            mesh.Path = [new(0, 0, 0), new(1, 1, 0)];
            Check(!nav.CanNavigateFully(new(0, 0, 0), goal, 20), "partial path rejected");
            Check(!nav.CanNavigateFully(new(float.NaN, 0, 0), goal, 20), "NaN rejected");
            Check(!nav.CanNavigateFully(new(0, 0, 0), goal, 0), "invalid maxHops rejected");
            mesh.Path = [new(0, 0, 0), new(0, 10, 0), goal];
            position = new(new(0, 0, 0), 0);
            Check(nav.MoveTo(goal) == MoveResult.Failed && mover.Target is null, "wrong map stops");
            position = null;
            Check(nav.MoveTo(goal) == MoveResult.Failed, "missing player stops");
            position = new(new(0, 0, 0), 1);
            nav.MoveTo(goal); queries = mesh.Queries;
            position = new(new(4, 0, 0), 1);
            mesh.Path = [new(4, 0, 0), new(4, 10, 0), goal];
            Check(nav.MoveTo(goal) == MoveResult.Moved && mesh.Queries == queries + 1, "displacement replans from current position");
            nav.Clear(); position = new(new(0, 0, 10), 1);
            mesh.Path = [];
            Check(nav.MoveTo(new(0, 0, 0)) == MoveResult.PathGenerationFailed, "same XY different floor is not arrival");
            nav.Clear(); position = new(new(0, 0, 0), 1); mesh.Path = [new(0, 0, 0), goal];
            nav.MoveTo(goal); now = now.AddSeconds(5);
            Check(nav.MoveTo(goal) == MoveResult.UnstuckAttempt && mover.Target is null, "no progress stops before recovery");
            Check(nav.MoveTo(goal) == MoveResult.Moved, "recovery replans");
            now = now.AddSeconds(5);
            Check(nav.MoveTo(goal) == MoveResult.Failed && mover.Target is null, "recovery is bounded");
            nav.MoveTo(goal);
            Navigator.NavigationProvider = new MeshNavigator();
            Check(mover.Target is null && Navigator.CurrentDestination is null, "provider replacement stops and clears");
            Check(Navigator.MoveTo(goal) == MoveResult.Failed, "unconfigured mesh cannot drive player");
            Navigator.NavigationProvider = nav;
            nav.MoveTo(goal); Navigator.PlayerMover = new RecordingMover();
            Check(mover.Target is null, "mover replacement releases old movement");
            bool rejected = false;
            try { nav.PathPrecision = float.NaN; } catch (ArgumentOutOfRangeException) { rejected = true; }
            Check(rejected, "invalid precision rejected");
            if (meshDirectory is not null)
            {
                using var real = new VanillaMeshPathfinder(meshDirectory, 1);
                WoWPoint a = new(-618.667f, -4245.333f, 38.914f), b = new(-699.2f, -4202.667f, 36.914f);
                var path = real.FindPath(a, b, 4096);
                Check(path.Length > 2, $"real Valley of Trials corridor: {real.LastError}");
                Check(path[0].Distance(a) < 1 && path[^1].Distance(b) < 1, "real XYZ round trip and complete endpoint");
                Check(path.All(MeshNavigator.Finite), "real finite waypoints");
                Check(path.Zip(path.Skip(1)).All(p => p.First.Distance2D(p.Second) <= 1.01), "real ground segments bounded");
                Check(real.FindPath(a, b, 1).Length == 0, "real truncated corridor rejected");
                Check(real.FindPath(a, new(b.X, b.Y, b.Z + 100), 4096).Length == 0, "real wrong elevation rejected");
                Check(real.FindPath(a, new(10000, 10000, 0), 4096).Length == 0, "real missing tiles rejected");
                WoWPoint acrossTile = new(-597.333f, -4309.333f, 37.728f);
                Check(real.FindPath(a, acrossTile, 4096).Length > 2, "real route crosses the 0013339/0013340 tile boundary");
                string directory = Path.Combine(Path.GetTempPath(), "hb-mesh-validation-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                try
                {
                    void RejectMesh(string reason)
                    {
                        bool failed = false;
                        try { using var invalid = new VanillaMeshPathfinder(directory, 1); }
                        catch (InvalidDataException) { failed = true; }
                        Check(failed, reason);
                    }
                    File.WriteAllBytes(Path.Combine(directory, "001.mmap"), [0]);
                    RejectMesh("invalid parameter file length rejected");
                    File.Copy(Path.Combine(meshDirectory, "001.mmap"), Path.Combine(directory, "001.mmap"), true);
                    RejectMesh("missing tile set rejected");
                    byte[] tile = File.ReadAllBytes(Path.Combine(meshDirectory, "0013339.mmtile"));
                    tile[8] = 99;
                    File.WriteAllBytes(Path.Combine(directory, "0013339.mmtile"), tile);
                    RejectMesh("incompatible mmap version rejected");
                    tile[8] = 6;
                    File.WriteAllBytes(Path.Combine(directory, "0013339.mmtile"), tile[..^1]);
                    RejectMesh("truncated tile payload rejected");
                }
                finally { Directory.Delete(directory, true); }
                Console.WriteLine($"Real mesh: map=1 tiles={real.TileCount}, Valley of Trials path={path.Length} points.");
            }
            Console.WriteLine($"NAVIGATION STEP 12 SELF TEST: PASS ({checks} checks; real mesh={(meshDirectory is null ? "not supplied" : "verified")}; no live input)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"NAVIGATION SELF TEST: FAIL after {checks} checks: {ex}"); return 1; }
        finally { Navigator.NavigationProvider = oldProvider; Navigator.PlayerMover = oldMover; }
    }
    private sealed class RecordingMover : IPlayerMover
    {
        internal WoWPoint? Target;
        public void Move(WoWMovement.MovementDirection direction) { }
        public void MoveTowards(WoWPoint location) => Target = location;
        public void MoveStop() => Target = null;
    }
    private sealed class FakeMesh : IMeshPathfinder
    {
        public int MapId => 1;
        internal WoWPoint[] Path = [];
        internal int Queries;
        public WoWPoint[] FindPath(WoWPoint from, WoWPoint to, int maxHops) { Queries++; return Path; }
    }
}
