using System.Globalization;
using System.Text.Json;
using Honorbuddy5875.Navigation;
using Styx.Logic.Pathing;

internal static class NavigationProbe
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length != 9) throw new ArgumentException("Usage: --mesh-path-test <mmaps-directory> <map-id> <from-x> <from-y> <from-z> <to-x> <to-y> <to-z>");
            int map = int.Parse(args[2], CultureInfo.InvariantCulture);
            float F(int i) => float.Parse(args[i], CultureInfo.InvariantCulture);
            WoWPoint from = new(F(3), F(4), F(5)), to = new(F(6), F(7), F(8));
            using var mesh = new VanillaMeshPathfinder(args[1], map);
            var provider = new MeshNavigator(mesh, () => null, () => new NullPlayerMover());
            var path = provider.GeneratePath(from, to);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                map, tiles = mesh.TileCount, complete = path.Length > 0, error = mesh.LastError,
                points = path.Select(p => new { p.X, p.Y, p.Z })
            }, new JsonSerializerOptions { WriteIndented = true }));
            return path.Length > 0 ? 0 : 1;
        }
        catch (Exception ex) { Console.Error.WriteLine($"Mesh path test failed: {ex.Message}"); return 1; }
    }
}
