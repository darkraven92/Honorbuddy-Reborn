using Bots.Quest;
using Honorbuddy5875.Movement;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;

namespace Honorbuddy5875.Runtime;

internal static class TurnInApproachProbe
{
    internal static int Run(string? path, bool execute)
    {
        QuestBot? bot = null;
        UInputPlayerMover? mover = null;
        PlayerMover previousMover = Navigator.PlayerMover;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Usage: --turn-in-approach-test QuestOrderStep5/CuttingTeeth.xml [--execute]");
            ProfileManager.LoadNew(path, false);
            var order = ProfileManager.CurrentProfile.QuestOrder;
            TurnInProbeProfile.Validate(order);
            ObjectManager.Initialize5875();
            bot = new QuestBot { MovementExecutionEnabled = false, ClientTargetSyncEnabled = false,
                MaximumMovementDisplacement = 30, NoProgressTimeout = TimeSpan.FromSeconds(4) };
            bot.Start();
            bot.EvaluateNow();
            Print(bot);
            if (!execute)
            {
                bool found = bot.CurrentDecision.Kind is QuestDecisionKind.MoveToQuestGiver or QuestDecisionKind.QuestGiverInRange;
                Console.WriteLine(found ? "TURN-IN STEP 6 PLAN RESULT: PASS" : "TURN-IN STEP 6 PLAN RESULT: BLOCKED");
                Console.WriteLine("Scope: live NPC discovery and approach decision only. No input was initialized.");
                return found ? 0 : 2;
            }
            if (bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverInRange)
            {
                Console.WriteLine("TURN-IN STEP 6 APPROACH RESULT: ALREADY IN RANGE - no movement performed.");
                return 0;
            }
            if (!CanStart(bot)) return 2;
            Console.WriteLine("Movement starts in 5 seconds. Focus WoW with chat closed. W/A/D use the existing movement bindings.");
            Console.WriteLine("Use a clear, short path on the same level. Ctrl+C in this terminal cancels; timeout is 25 seconds.");
            for (int i = 5; i > 0 && !cancellation.IsCancellationRequested; i--)
            { Console.WriteLine(i); Thread.Sleep(1000); }
            if (cancellation.IsCancellationRequested) return 2;
            ObjectManager.Update();
            bot.EvaluateNow();
            if (!CanStart(bot)) return 2;

            mover = new UInputPlayerMover();
            Navigator.PlayerMover = mover;
            bot.MovementExecutionEnabled = true;
            bot.Start();
            bot.Root.Start(null);
            long deadline = Environment.TickCount64 + 25000;
            long nextLog = 0;
            while (!cancellation.IsCancellationRequested && Environment.TickCount64 < deadline)
            {
                ObjectManager.Update();
                bot.Root.Tick(null);
                if (Environment.TickCount64 >= nextLog)
                { Print(bot); nextLog = Environment.TickCount64 + 1000; }
                if (bot.MovementAborted)
                {
                    Console.WriteLine($"TURN-IN STEP 6 APPROACH RESULT: STOPPED - {bot.MovementStopReason}");
                    return 2;
                }
                if (bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverInRange)
                {
                    Print(bot);
                    Console.WriteLine($"TURN-IN STEP 6 APPROACH RESULT: PASS - Navigator calls={bot.NavigatorMoveCalls}, TurnIn node retained.");
                    Console.WriteLine("No NPC interaction or reward acceptance was performed.");
                    return 0;
                }
                if (bot.CurrentDecision.Kind != QuestDecisionKind.MoveToQuestGiver)
                {
                    Print(bot);
                    Console.WriteLine("TURN-IN STEP 6 APPROACH RESULT: STOPPED - quest/NPC state no longer permits approach.");
                    return 2;
                }
                Thread.Sleep(100);
            }
            Console.WriteLine("TURN-IN STEP 6 APPROACH RESULT: STOPPED - cancelled or timed out.");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TURN-IN STEP 6 RESULT: ERROR - {ex.Message}");
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
            try { bot?.Root.Stop(null); bot?.Stop(); }
            finally
            {
                try { Navigator.PlayerMover = previousMover; }
                finally
                {
                    mover?.Dispose();
                    ObjectManager.Shutdown5875();
                    ProfileManager.LoadEmpty();
                }
            }
        }
    }

    private static bool CanStart(QuestBot bot)
    {
        if (bot.CurrentDecision.Kind != QuestDecisionKind.MoveToQuestGiver || bot.CurrentQuestGiver is not { Distance: <= 25 })
        {
            Console.WriteLine("Approach blocked: no movement decision or NPC is more than 25 yards away.");
            return false;
        }
        var me = Styx.StyxWoW.Me;
        if (me is null || !me.IsAlive || me.Combat)
        {
            Console.WriteLine("Approach blocked: player is unavailable, dead, or in combat.");
            return false;
        }
        return true;
    }

    private static void Print(QuestBot bot)
    {
        Console.WriteLine($"Node index={bot.CurrentProfileNodeIndex}; decision={bot.CurrentDecision.Kind}");
        if (bot.CurrentQuestGiver is { } npc)
            Console.WriteLine($"NPC entry={npc.Entry} guid=0x{npc.Guid:X16} position={npc.Location} " +
                $"distance3D={npc.Distance:F2} distance2D={npc.Distance2D:F2} modeledInteractRange={npc.InteractRange:F2}");
        Console.WriteLine(bot.CurrentDecision.Description);
    }

    internal static int RunSelfTest()
    {
        int checks = 0;
        string directory = Path.Combine(Path.GetTempPath(), "hb-turnin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            void Check(bool value, string label)
            { if (!value) throw new InvalidOperationException(label); checks++; }
            QuestGiverSnapshot Npc(ulong guid, double distance) =>
                new(guid, 3143, new(100, 200, 20), distance, distance, 4, true, false, false);
            var near = Npc(11, 8); var far = Npc(22, 15);
            Check(QuestBot.SelectQuestGiver(3143, 0, [far, near])?.Guid == 11, "nearest matching NPC");
            Check(QuestBot.SelectQuestGiver(3143, 22, [far, near])?.Guid == 22, "stable selected GUID");
            Check(QuestBot.SelectQuestGiver(3143, 22, [near])?.Guid == 11, "recover when prior NPC disappears");
            foreach (var bad in new[] { near with { Guid = 0 }, near with { Entry = 3098 }, near with { Alive = false },
                near with { PlayerControlled = true }, near with { InCombat = true }, near with { Distance = double.NaN },
                near with { Location = new(float.PositiveInfinity, 0, 0) }, near with { InteractRange = 0 } })
                Check(QuestBot.SelectQuestGiver(3143, 0, [bad]) is null, "reject unusable NPC");
            string path = Path.Combine(directory, "profile.xml");
            File.WriteAllText(path, "<HBProfile><QuestOrder><Objective QuestId=\"788\" Type=\"KillMob\" MobId=\"3098\" KillCount=\"10\" />" +
                "<TurnIn QuestId=\"788\" TurnInId=\"3143\" /></QuestOrder></HBProfile>");
            ProfileManager.LoadNew(path, false);
            var state = new QuestOrderSnapshot(true, true, false, [3098, 0, 0, 0], [10, 0, 0, 0], [10, 0, 0, 0]);
            QuestGiverSnapshot? npc = near;
            var bot = new QuestBot(_ => state) { ReadQuestGiver = (_, _) => npc };
            bot.Start(); bot.Root.Start(null);
            void Tick() => bot.Root.Tick(null);
            Tick();
            Check(bot.CurrentProfileNodeIndex == 1 && bot.CurrentDecision.Kind == QuestDecisionKind.MoveToQuestGiver &&
                bot.CurrentDecision.TargetGuid == 11 && bot.CurrentDecision.Destination?.X == 100, "resolved TurnIn destination");
            Check(bot.NavigatorMoveCalls == 0, "read-only mode sends no movement");
            npc = near with { Location = new(110, 210, 20), Distance = 6, Distance2D = 6 }; Tick();
            Check(bot.CurrentDecision.Destination?.X == 110, "refresh moving NPC coordinates");
            npc = near with { Distance = 3.5, Distance2D = 3.5 }; Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverInRange && bot.CurrentProfileNodeIndex == 1, "arrive without consuming TurnIn");
            Tick(); Check(bot.CurrentProfileNodeIndex == 1 && bot.TransitionLog.Count == 1, "retained across ticks");
            npc = near with { Distance = 3.6, Distance2D = 3.6 }; Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.MoveToQuestGiver, "half-yard margin");
            npc = near with { Distance = 10, Distance2D = 0 }; Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "vertical separation is not arrival");
            npc = near with { Distance = 3.8, Distance2D = 0 }; Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverInRange,
                "in-range vertical offset does not loop on Navigator's 2D precision");
            npc = null; Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverUnavailable && bot.CurrentQuestGiver is null &&
                bot.CurrentDecision.Destination is null, "lost NPC clears stale destination");
            var oldMover = Navigator.PlayerMover;
            var recording = new RecordingMover();
            try
            {
                Navigator.PlayerMover = recording;
                bot.MovementExecutionEnabled = true;
                npc = near with { Distance = 3, Distance2D = 3 }; Tick();
                Check(recording.Stops > 0 && recording.Moves == 0 && !bot.MovementAborted,
                    "arrival releases movement through the real root execution path");
                int stops = recording.Stops;
                npc = null; Tick();
                Check(recording.Stops > stops && recording.Moves == 0 && !bot.MovementAborted,
                    "NPC disappearance releases movement through the real root execution path");
            }
            finally { bot.MovementExecutionEnabled = false; Navigator.PlayerMover = oldMover; }
            npc = near; state = state with { Completed = false }; Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "incomplete quest cannot approach turn-in");
            state = state with { Completed = true, Failed = true }; Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "failed quest cannot approach");
            bot.Root.Stop(null); bot.Stop();
            Console.WriteLine($"TURN-IN STEP 6 SELF TEST: PASS ({checks} checks; NPC selection and QuestBot root decisions)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TURN-IN STEP 6 SELF TEST: FAIL after {checks} checks - {ex}");
            return 1;
        }
        finally { ProfileManager.LoadEmpty(); Directory.Delete(directory, true); }
    }

    private sealed class RecordingMover : PlayerMover
    {
        internal int Moves;
        internal int Stops;
        public override void Move(WoWMovement.MovementDirection direction) => Moves++;
        public override void MoveStop(WoWMovement.MovementDirection direction) => Stops++;
        public override void MoveStop() => Stops++;
    }
}
