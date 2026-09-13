using System.Globalization;
using Honorbuddy5875.Movement;
using Honorbuddy5875.Navigation;
using Styx.Logic.Pathing;
using Styx.WoWInternals;

internal static class NavigationLiveProbe
{
    internal static int Run(string[] args)
    {
        VanillaNavigationSession? navigation = null;
        UInputPlayerMover? mover = null;
        IPlayerMover previousMover = Navigator.PlayerMover;
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += onCancel;
        try
        {
            bool execute = args.Contains("--execute");
            string[] values = args.Skip(1).Where(a => a != "--execute").ToArray();
            if (values.Length is not (1 or 4) || (execute && values.Length != 4))
                throw new ArgumentException("Usage: --navigation-live-test <mmaps-directory> [<x> <y> <z> [--execute]]");
            ObjectManager.Initialize5875();
            navigation = new VanillaNavigationSession(values[0]);
            var me = ObjectManager.Me ?? throw new InvalidOperationException("No local player.");
            WoWPoint start = me.Location;
            WoWPoint destination = values.Length == 1 ? start : new(
                float.Parse(values[1], CultureInfo.InvariantCulture),
                float.Parse(values[2], CultureInfo.InvariantCulture),
                float.Parse(values[3], CultureInfo.InvariantCulture));
            WoWPoint[] path = Navigator.GeneratePath(start, destination);
            Console.WriteLine($"PID={ObjectManager.WoWProcess!.Id}; map={navigation.MapId}; player={start}; health={me.CurrentHealth}/{me.MaxHealth}");
            Console.WriteLine($"Destination={destination}; complete={path.Length > 0}; points={path.Length}");
            foreach (var unit in ObjectManager.GetObjectsOfType<Styx.WoWInternals.WoWObjects.WoWUnit>(false, false)
                .Where(u => u.IsValid && u.Entry is 3143 or 3144).OrderBy(u => u.Entry))
                Console.WriteLine($"NPC entry={unit.Entry}; position={unit.Location}; distance={unit.Distance2D:F2}");
            if (path.Length == 0) return 2;
            if (!execute)
            {
                Console.WriteLine("NAVIGATION LIVE PLAN: PASS; no input initialized.");
                return 0;
            }
            if (start.Distance(destination) > 25 || !me.IsAlive || me.Combat)
                throw new InvalidOperationException("Movement test requires a living player out of combat and a destination within 25 yards.");
            uint health = me.CurrentHealth;
            ulong guid = me.Guid;
            Console.WriteLine("Movement starts in 5 seconds. Focus WoW with chat closed. Ctrl+C cancels; deadline 25 seconds.");
            if (cancel.Token.WaitHandle.WaitOne(5000)) return 2;
            ObjectManager.Update();
            me = ObjectManager.Me ?? throw new InvalidOperationException("Local player disappeared.");
            if (me.Guid != guid || !me.IsAlive || me.Combat || me.CurrentHealth < health || me.Location.Distance(start) > 1)
                throw new InvalidOperationException("Player state changed before movement.");
            mover = new UInputPlayerMover();
            Navigator.PlayerMover = mover;
            long deadline = Environment.TickCount64 + 25000, nextLog = 0;
            while (!cancel.IsCancellationRequested && Environment.TickCount64 < deadline)
            {
                ObjectManager.Update();
                me = ObjectManager.Me ?? throw new InvalidOperationException("Local player disappeared.");
                if (me.Guid != guid || !me.IsAlive || me.Combat || me.CurrentHealth < health || me.Location.Distance(start) > 30)
                    throw new InvalidOperationException("Player state no longer permits movement.");
                MoveResult result = Navigator.MoveTo(destination);
                if (Environment.TickCount64 >= nextLog)
                {
                    Console.WriteLine($"Position={me.Location}; remaining={me.Location.Distance(destination):F2}; result={result}");
                    nextLog = Environment.TickCount64 + 1000;
                }
                if (result == MoveResult.ReachedDestination)
                {
                    Console.WriteLine("NAVIGATION LIVE MOVEMENT: PASS");
                    return 0;
                }
                if (result is MoveResult.Failed or MoveResult.PathGenerationFailed) return 2;
                cancel.Token.WaitHandle.WaitOne(50);
            }
            Console.WriteLine("NAVIGATION LIVE MOVEMENT: STOPPED (cancelled or timed out)");
            return 2;
        }
        catch (Exception ex) { Console.Error.WriteLine($"NAVIGATION LIVE: ERROR - {ex.Message}"); return 1; }
        finally
        {
            Console.CancelKeyPress -= onCancel;
            try { Navigator.PlayerMover = previousMover; }
            finally
            {
                try { mover?.Dispose(); }
                finally
                {
                    try { navigation?.Dispose(); }
                    finally { ObjectManager.Shutdown5875(); }
                }
            }
        }
    }
}
