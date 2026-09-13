using Bots.Quest;
using Honorbuddy5875.Movement;
using Honorbuddy5875.Navigation;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals;

internal static class QuestRoutingProbe
{
    internal static int Run(string[] args)
    {
        QuestBot? bot = null;
        VanillaNavigationSession? navigation = null;
        UInputPlayerMover? mover = null;
        IPlayerMover previousMover = Navigator.PlayerMover;
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            bool execute = args.Contains("--execute");
            string[] values = args.Skip(1).Where(a => a != "--execute").ToArray();
            if (values.Length != 2)
                throw new ArgumentException("Usage: --quest-routing-test <profile.xml> <mmaps-directory> [--execute]");
            ProfileManager.LoadNew(values[0], false);
            var profile = ProfileManager.CurrentProfile;
            ObjectManager.Initialize5875();
            navigation = new VanillaNavigationSession(values[1]);
            var me = ObjectManager.Me ?? throw new InvalidOperationException("Local player unavailable.");
            if (profile.ContinentId != navigation.MapId || me.Level < profile.MinLevel || me.Level > profile.MaxLevel)
                throw new InvalidOperationException("Player map/level does not match the profile.");
            bot = new QuestBot { MovementExecutionEnabled = false, MaximumMovementDisplacement = 300,
                NoProgressTimeout = TimeSpan.FromSeconds(5), ClientTargetSyncEnabled = false };
            bot.Start(); bot.Root.Start(null); bot.Root.Tick(null);
            Print(bot);
            if (!Moving(bot))
            {
                Console.WriteLine("ROUTING CHECKPOINT: node retained until its quest/interaction/objective requirement is met.");
                return bot.CurrentDecision.Kind == QuestDecisionKind.ProfileComplete ? 0 : 2;
            }
            if (bot.CurrentDecision.Destination is not WoWPoint target || !Navigator.CanNavigateFully(me.Location, target))
                throw new InvalidOperationException("The current profile decision has no complete mesh path.");
            if (!execute)
            {
                Console.WriteLine("QUEST ROUTING PLAN: PASS; current decision has a complete mesh path; no input initialized.");
                return 0;
            }
            if (!me.IsAlive || me.Combat) throw new InvalidOperationException("Player must be alive and out of combat.");
            ulong guid = me.Guid;
            uint health = me.CurrentHealth;
            WoWPoint start = me.Location;
            Console.WriteLine("Routing starts in 5 seconds. Focus WoW with chat closed. Ctrl+C cancels. Deadline 90 seconds, displacement limit 300 yards.");
            if (cancel.Token.WaitHandle.WaitOne(5000)) return 2;
            ObjectManager.Update();
            me = ObjectManager.Me ?? throw new InvalidOperationException("Local player unavailable.");
            if (me.Guid != guid || !me.IsAlive || me.Combat || me.CurrentHealth < health || me.Location.Distance(start) > 1)
                throw new InvalidOperationException("Player state changed before movement.");
            mover = new UInputPlayerMover();
            Navigator.PlayerMover = mover;
            bot.MovementExecutionEnabled = true;
            long deadline = Environment.TickCount64 + 90000, nextLog = 0;
            while (!cancel.IsCancellationRequested && Environment.TickCount64 < deadline)
            {
                ObjectManager.Update();
                bot.Root.Tick(null);
                if (Environment.TickCount64 >= nextLog)
                { Print(bot); nextLog = Environment.TickCount64 + 1000; }
                if (bot.MovementAborted) return 2;
                if (!Moving(bot) && bot.CurrentDecision.Kind != QuestDecisionKind.None)
                {
                    Navigator.Clear(); Print(bot);
                    Console.WriteLine("ROUTING CHECKPOINT: movement stopped. Quest acceptance, rewards, combat and loot were not sent.");
                    return bot.CurrentDecision.Kind is QuestDecisionKind.QuestGiverInRange or QuestDecisionKind.ProfileComplete ? 0 : 2;
                }
                cancel.Token.WaitHandle.WaitOne(50);
            }
            Console.WriteLine("QUEST ROUTING: STOPPED (cancelled or timed out)");
            return 2;
        }
        catch (Exception ex) { Console.Error.WriteLine($"QUEST ROUTING: ERROR - {ex.Message}"); return 1; }
        finally
        {
            Console.CancelKeyPress -= handler;
            try { bot?.Root.Stop(null); bot?.Stop(); }
            finally
            {
                try { Navigator.PlayerMover = previousMover; }
                finally
                {
                    try { mover?.Dispose(); }
                    finally
                    {
                        try { navigation?.Dispose(); }
                        finally { ObjectManager.Shutdown5875(); ProfileManager.LoadEmpty(); }
                    }
                }
            }
        }
    }

    private static bool Moving(QuestBot bot) => bot.CurrentDecision.Kind is
        QuestDecisionKind.MoveToQuestGiver or QuestDecisionKind.MoveToProfileLocation;
    private static void Print(QuestBot bot) => Console.WriteLine(
        $"QuestOrder[{bot.CurrentProfileNodeIndex}]: {bot.CurrentDecision.Kind}; {bot.CurrentDecision.Description}; move={bot.LastMoveResult}; stopped={bot.MovementStopReason}");
}
