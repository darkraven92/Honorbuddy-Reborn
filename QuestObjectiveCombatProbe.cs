using System.ComponentModel;
using Bots.Quest;
using Honorbuddy5875.Movement;
using Honorbuddy5875.Navigation;
using Honorbuddy5875.Runtime;
using Styx;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

internal static class QuestObjectiveCombatProbe
{
    private const uint QuestId = 788;
    private const uint MobEntry = 3098;
    private const int RequiredKills = 10;
    private static readonly TimeSpan LiveDeadline = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CreditDeadline = TimeSpan.FromSeconds(6);

    private readonly record struct Progress(
        int Slot,
        ushort Done,
        int Required,
        bool Completed,
        bool Failed);

    internal static int Run(string[] args)
    {
        bool execute = args.Any(
            value => string.Equals(
                value,
                "--execute",
                StringComparison.OrdinalIgnoreCase));

        string[] values = args
            .Skip(1)
            .Where(value => !string.Equals(
                value,
                "--execute",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (values.Length != 1)
        {
            Console.Error.WriteLine(
                "Usage: --quest-objective-combat-test <mmaps-directory> [--execute]");
            return 1;
        }

        string meshDirectory = values[0];
        string profilePath = Path.Combine(
            Path.GetTempPath(),
            "hb-quest788-single-" + Guid.NewGuid().ToString("N") + ".xml");

        QuestBot? bot = null;
        VanillaNavigationSession? navigation = null;
        UInputPlayerMover? mover = null;
        UInputClientTargeting? targetInput = null;
        UInputCombatActions? combatInput = null;
        IPlayerMover previousMover = Navigator.PlayerMover;

        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            WriteProbeProfile(profilePath);
            ProfileManager.LoadNew(profilePath, false);

            ObjectManager.Initialize5875();
            FactionTemplateStore5875.Initialize();

            navigation = new VanillaNavigationSession(meshDirectory);
            if (navigation.MapId != 1)
                throw new InvalidOperationException(
                    $"Quest {QuestId} probe requires Kalimdor map 1; current map is {navigation.MapId}.");

            LocalPlayer me = StyxWoW.Me
                ?? throw new InvalidOperationException(
                    "LocalPlayer unavailable after initialization.");

            if (!me.IsAlive)
                throw new InvalidOperationException("Player must be alive.");
            if (me.Combat)
                throw new InvalidOperationException(
                    "Player must be out of combat before the probe.");

            Progress initial = ReadProgress(me);

            if (initial.Failed)
                throw new InvalidOperationException(
                    $"Quest {QuestId} is failed.");
            if (initial.Completed || initial.Done >= RequiredKills)
                throw new InvalidOperationException(
                    $"Quest {QuestId} is already complete ({initial.Done}/{RequiredKills}).");

            bot = new QuestBot
            {
                MovementExecutionEnabled = false,
                ObjectiveCombatExecutionEnabled = false,
                ClientTargetSyncEnabled = false,
                ResolveQuestGiverLocations = false,
                MaximumMovementDisplacement = 80,
                NoProgressTimeout = TimeSpan.FromSeconds(5)
            };

            bot.Start();
            bot.Root.Start(null);

            PulseEvaluation(bot);
            PulseEvaluation(bot);

            if (bot.CurrentDecision.Kind != QuestDecisionKind.ObjectiveInProgress ||
                bot.CurrentDecision.Entry != MobEntry ||
                bot.CurrentDecision.TargetGuid == 0 ||
                bot.CurrentDecision.Destination is not WoWPoint destination)
            {
                PrintPreflight(me, initial, bot, null, false);
                Console.WriteLine();
                Console.WriteLine(
                    "SINGLE-KILL PREFLIGHT: BLOCKED - no exact entry 3098 objective target is available.");
                Console.WriteLine(
                    "Move within ObjectManager/Tab range of a Mottled Boar and retry.");
                return 3;
            }

            WoWUnit? target =
                ObjectManager.GetObjectByGuid<WoWUnit>(
                    bot.CurrentDecision.TargetGuid);

            if (target is null ||
                !Bots.Grind.LevelBot.IsQuestObjectiveTargetCandidate(
                    target,
                    ProfileManager.CurrentProfile,
                    MobEntry))
            {
                throw new InvalidOperationException(
                    "QuestBot selected target no longer passes the exact objective candidate gate.");
            }

            bool fullPath =
                Navigator.CanNavigateFully(
                    me.Location,
                    destination);

            PrintPreflight(
                me,
                initial,
                bot,
                target,
                fullPath);

            if (!fullPath)
            {
                Console.WriteLine();
                Console.WriteLine(
                    "SINGLE-KILL PREFLIGHT: BLOCKED - no complete mesh path to the selected Mottled Boar.");
                return 3;
            }

            Console.WriteLine();
            Console.WriteLine(
                "SINGLE-KILL PREFLIGHT: PASS - quest 788, exact entry 3098, attackability and mesh path are verified.");

            if (!execute)
            {
                Console.WriteLine(
                    "No input was sent. Add --execute to kill exactly one objective target.");
                return 0;
            }

            Console.WriteLine();
            Console.WriteLine("LIVE SINGLE-KILL TEST IS ARMED.");
            Console.WriteLine(
                "Requirements: WoW focused, chat closed, Attack Target bound to T.");
            Console.WriteLine(
                "The probe stops objective combat immediately after the first observed target death");
            Console.WriteLine(
                "and passes only if the live quest descriptor increases by exactly one.");
            Console.WriteLine("Ctrl+C cancels.");

            for (int i = 5; i >= 1; i--)
            {
                Console.WriteLine($"Starting in {i}...");
                if (cancel.Token.WaitHandle.WaitOne(1000))
                    return 2;
            }

            ObjectManager.Update();
            me = StyxWoW.Me
                ?? throw new InvalidOperationException(
                    "LocalPlayer unavailable before live start.");

            Progress armed = ReadProgress(me);
            if (!me.IsAlive ||
                me.Combat ||
                armed.Done != initial.Done ||
                armed.Completed ||
                armed.Failed)
            {
                throw new InvalidOperationException(
                    "Player or quest state changed during the countdown.");
            }

            PulseEvaluation(bot);

            if (bot.CurrentDecision.Entry != MobEntry ||
                bot.CurrentDecision.TargetGuid == 0 ||
                bot.CurrentDecision.Destination is not WoWPoint armedDestination)
            {
                throw new InvalidOperationException(
                    "Exact entry 3098 target disappeared before live start.");
            }

            if (!Navigator.CanNavigateFully(
                    me.Location,
                    armedDestination))
            {
                throw new InvalidOperationException(
                    "Complete mesh path disappeared before live start.");
            }

            mover = new UInputPlayerMover();
            targetInput = new UInputClientTargeting();
            combatInput = new UInputCombatActions();

            Navigator.PlayerMover = mover;

            bot.ClientTargetInput = targetInput;
            bot.ObjectiveCombatInput = combatInput;
            bot.MovementExecutionEnabled = true;
            bot.ObjectiveCombatExecutionEnabled = true;

            long deadline =
                checked(Environment.TickCount64 +
                        (long)LiveDeadline.TotalMilliseconds);

            long nextLog = 0;
            bool deathObserved = false;
            long deathObservedAt = 0;

            while (!cancel.IsCancellationRequested &&
                   Environment.TickCount64 < deadline)
            {
                WoWPulsator.Pulse(bot.PulseFlags);
                bot.Pulse();
                bot.Root.Tick(null);

                if (bot.ObjectiveTargetsCompleted > 1)
                {
                    bot.ObjectiveCombatExecutionEnabled = false;
                    Navigator.Clear();

                    Console.WriteLine();
                    Console.WriteLine(
                        "SINGLE-KILL RESULT: FAIL - more than one target death was observed.");
                    return 2;
                }

                if (!deathObserved &&
                    bot.ObjectiveTargetsCompleted == 1)
                {
                    deathObserved = true;
                    deathObservedAt = Environment.TickCount64;

                    bot.ObjectiveCombatExecutionEnabled = false;
                    Navigator.Clear();

                    Console.WriteLine();
                    Console.WriteLine(
                        "First objective target death observed. Combat execution disabled; waiting only for quest credit.");
                }

                ObjectManager.Update();
                me = StyxWoW.Me
                    ?? throw new InvalidOperationException(
                        "LocalPlayer disappeared during live probe.");

                Progress current = ReadProgress(me);

                if (current.Done > initial.Done)
                {
                    bot.ObjectiveCombatExecutionEnabled = false;
                    Navigator.Clear();

                    int delta = current.Done - initial.Done;

                    Console.WriteLine();
                    Console.WriteLine(
                        $"Quest {QuestId} descriptor progress: {initial.Done}/{RequiredKills} -> {current.Done}/{RequiredKills}");
                    Console.WriteLine(
                        $"Objective target deaths observed: {bot.ObjectiveTargetsCompleted}");
                    Console.WriteLine(
                        $"Client target sync: attempts={bot.ClientTargetSyncAttempts} successes={bot.ClientTargetSyncSuccesses} failures={bot.ClientTargetSyncFailures}");

                    if (deathObserved &&
                        bot.ObjectiveTargetsCompleted == 1 &&
                        delta == 1)
                    {
                        Console.WriteLine(
                            "SINGLE-KILL RESULT: PASS - exactly one entry 3098 target died and the authoritative quest descriptor increased by exactly one.");
                        return 0;
                    }

                    Console.WriteLine(
                        "SINGLE-KILL RESULT: FAIL - quest progress changed without the exact single-kill invariant.");
                    return 2;
                }

                if (bot.MovementAborted)
                {
                    bot.ObjectiveCombatExecutionEnabled = false;
                    Navigator.Clear();

                    Console.WriteLine();
                    Console.WriteLine(
                        $"SINGLE-KILL RESULT: FAIL - {bot.MovementStopReason}");
                    return 2;
                }

                if (deathObserved &&
                    Environment.TickCount64 - deathObservedAt >
                    (long)CreditDeadline.TotalMilliseconds)
                {
                    bot.ObjectiveCombatExecutionEnabled = false;
                    Navigator.Clear();

                    Console.WriteLine();
                    Console.WriteLine(
                        $"SINGLE-KILL RESULT: BLOCKED - target died but quest {QuestId} remained {current.Done}/{RequiredKills} for {CreditDeadline.TotalSeconds:F0} seconds.");
                    return 3;
                }

                if (Environment.TickCount64 >= nextLog)
                {
                    WoWUnit? currentTarget =
                        bot.ObjectiveTargetGuid == 0
                            ? null
                            : ObjectManager.GetObjectByGuid<WoWUnit>(
                                bot.ObjectiveTargetGuid);

                    Console.WriteLine(
                        $"decision={bot.CurrentDecision.Kind,-19} " +
                        $"progress={current.Done,2}/{RequiredKills} " +
                        $"target=0x{bot.ObjectiveTargetGuid:X16} " +
                        $"entry={currentTarget?.Entry ?? 0,-5} " +
                        $"reaction={currentTarget?.MyReaction.ToString() ?? "n/a",-10} " +
                        $"dist={currentTarget?.Distance2D ?? double.NaN,6:F2} " +
                        $"meCombat={me.Combat,-5} " +
                        $"meHP={me.CurrentHealth}/{me.MaxHealth} " +
                        $"deaths={bot.ObjectiveTargetsCompleted}");

                    nextLog =
                        Environment.TickCount64 + 500;
                }

                cancel.Token.WaitHandle.WaitOne(70);
            }

            bot.ObjectiveCombatExecutionEnabled = false;
            Navigator.Clear();

            Console.WriteLine();
            Console.WriteLine(
                cancel.IsCancellationRequested
                    ? "SINGLE-KILL RESULT: STOPPED - cancelled by user."
                    : $"SINGLE-KILL RESULT: BLOCKED - {LiveDeadline.TotalSeconds:F0} second live deadline reached.");

            return 3;
        }
        catch (Win32Exception ex)
            when (ex.NativeErrorCode is 13 or 1)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "SINGLE-KILL RESULT: BLOCKED - /dev/uinput permission denied.");
            Console.Error.WriteLine(ex.Message);
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("SINGLE-KILL RESULT: ERROR");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;

            try
            {
                if (bot is not null)
                {
                    bot.ObjectiveCombatExecutionEnabled = false;
                    bot.Root.Stop(null);
                    bot.Stop();
                }
            }
            catch
            {
            }

            try
            {
                Navigator.PlayerMover = previousMover;
            }
            catch
            {
            }

            try { mover?.Dispose(); } catch { }
            try { targetInput?.Dispose(); } catch { }
            try { combatInput?.Dispose(); } catch { }
            try { navigation?.Dispose(); } catch { }

            try { ObjectManager.Shutdown5875(); } catch { }
            ProfileManager.LoadEmpty();

            try
            {
                if (File.Exists(profilePath))
                    File.Delete(profilePath);
            }
            catch
            {
            }
        }
    }

    private static void PulseEvaluation(QuestBot bot)
    {
        ObjectManager.Update();
        Targeting.Instance.Pulse();
        bot.Pulse();
        bot.Root.Tick(null);
    }

    private static Progress ReadProgress(LocalPlayer me)
    {
        PlayerQuest quest = me.QuestLog.GetQuestById(QuestId)
            ?? throw new InvalidOperationException(
                $"Quest {QuestId} is not active in the live quest log.");

        WoWDescriptorQuest descriptor = default;
        if (!quest.GetData(ref descriptor) ||
            descriptor.Id != QuestId ||
            descriptor.ObjectivesDone is not { Length: 4 })
        {
            throw new InvalidOperationException(
                $"Quest {QuestId} descriptor is unavailable or inconsistent.");
        }

        int[] ids = quest.NormalObjectiveIDs;
        int[] required = quest.NormalObjectiveRequiredCounts;

        if (ids.Length != 4 ||
            required.Length != 4)
        {
            throw new InvalidOperationException(
                $"Quest {QuestId} WDB objective arrays are incomplete.");
        }

        int[] slots = Enumerable.Range(0, 4)
            .Where(index =>
                ids[index] == MobEntry &&
                required[index] == RequiredKills)
            .ToArray();

        if (slots.Length != 1)
        {
            throw new InvalidOperationException(
                $"Quest {QuestId} WDB metadata does not contain exactly one entry {MobEntry} x{RequiredKills} objective.");
        }

        int slot = slots[0];

        return new Progress(
            slot,
            descriptor.ObjectivesDone[slot],
            required[slot],
            (descriptor.Flags & WoWDescriptorQuestFlags.Completed) != 0,
            (descriptor.Flags & WoWDescriptorQuestFlags.Failed) != 0);
    }

    private static void PrintPreflight(
        LocalPlayer me,
        Progress progress,
        QuestBot bot,
        WoWUnit? target,
        bool fullPath)
    {
        Console.WriteLine(
            "Honorbuddy Reborn - quest 788 single-target objective combat probe");
        Console.WriteLine(
            "---------------------------------------------------------------");
        Console.WriteLine(
            $"PID:                    {ObjectManager.WoWProcess?.Id}");
        Console.WriteLine(
            $"Player:                 {me.Race} class={me.ClassId} level={me.Level}");
        Console.WriteLine(
            $"Player location:        {me.Location}");
        Console.WriteLine(
            $"Player health:          {me.CurrentHealth}/{me.MaxHealth}");
        Console.WriteLine(
            $"Player combat:          {me.Combat}");
        Console.WriteLine(
            $"Quest 788 progress:     {progress.Done}/{progress.Required}");
        Console.WriteLine(
            $"Quest completed:        {progress.Completed}");
        Console.WriteLine(
            $"Quest failed:           {progress.Failed}");
        Console.WriteLine(
            $"QuestBot decision:      {bot.CurrentDecision.Kind}");
        Console.WriteLine(
            $"Required entry:         {MobEntry}");
        Console.WriteLine(
            $"Selected GUID:          0x{bot.CurrentDecision.TargetGuid:X16}");
        Console.WriteLine(
            $"Selected entry:         {target?.Entry ?? 0}");
        Console.WriteLine(
            $"Selected reaction:      {target?.MyReaction.ToString() ?? "n/a"}");
        Console.WriteLine(
            $"Strict hostile:         {target?.IsStrictHostileCombatCandidate ?? false}");
        Console.WriteLine(
            $"Neutral potential:      {target?.IsNeutralPotentialCombatCandidate ?? false}");
        Console.WriteLine(
            $"Distance:               {target?.Distance2D ?? double.NaN:F2}");
        Console.WriteLine(
            $"Complete mesh path:     {fullPath}");
        Console.WriteLine(
            "Input initialized:       false");
        Console.WriteLine(
            "Loot:                    disabled/not implemented");
        Console.WriteLine(
            "Maximum target deaths:  1");
    }

    private static void WriteProbeProfile(string path)
    {
        string xml =
            "<HBProfile>\n" +
            "  <Name>Quest 788 single-kill objective probe</Name>\n" +
            "  <ContinentId>1</ContinentId>\n" +
            "  <MinLevel>1</MinLevel>\n" +
            "  <MaxLevel>5</MaxLevel>\n" +
            "  <QuestOrder>\n" +
            "    <Objective QuestName=\"Cutting Teeth\" QuestId=\"788\" Type=\"KillMob\" MobId=\"3098\" KillCount=\"10\" />\n" +
            "  </QuestOrder>\n" +
            "</HBProfile>\n";

        File.WriteAllText(path, xml);
    }
}
