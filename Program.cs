using System.ComponentModel;
using System.Globalization;
using System.Xml.Linq;
using Honorbuddy5875.Combat;
using Honorbuddy5875.Movement;
using Honorbuddy5875.Runtime;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using Styx.WoWInternals.WoWObjects;

internal static class Program
{
    private const double MinSeedDistance = 10.0;
    private const double MaxSeedDistance = 35.0;
    private const double MaximumProfileDistance = 40.0;
    private static readonly TimeSpan CombatTimeout = TimeSpan.FromSeconds(40);

    private static int Main(string[] args)
    {
        if (args.Any(a => string.Equals(a, "--quest-reward-accept-self-test", StringComparison.OrdinalIgnoreCase)))
            return QuestRewardAcceptSelfTest.Run();
        int acceptTest = Array.FindIndex(args, a => string.Equals(a, "--quest-reward-accept-test", StringComparison.OrdinalIgnoreCase));
        if (acceptTest >= 0)
        {
            int layoutArg = Array.FindIndex(args, a => string.Equals(a, "--keyboard-layout", StringComparison.OrdinalIgnoreCase));
            return QuestRewardAcceptProbe.Run(acceptTest + 1 < args.Length ? args[acceptTest + 1] : null,
                args.Any(a => string.Equals(a, "--execute", StringComparison.OrdinalIgnoreCase)),
                layoutArg >= 0 && layoutArg + 1 < args.Length ? args[layoutArg + 1] : null);
        }
        if (args.Any(a => string.Equals(a, "--quest-reward-dialog-self-test", StringComparison.OrdinalIgnoreCase)))
            return QuestRewardDialogSelfTest.Run();
        int rewardDialogTest = Array.FindIndex(args, a => string.Equals(a, "--quest-reward-dialog-test", StringComparison.OrdinalIgnoreCase));
        if (rewardDialogTest >= 0)
        {
            int layoutArg = Array.FindIndex(args, a => string.Equals(a, "--keyboard-layout", StringComparison.OrdinalIgnoreCase));
            return QuestRewardDialogProbe.Run(rewardDialogTest + 1 < args.Length ? args[rewardDialogTest + 1] : null,
                args.Any(a => string.Equals(a, "--execute", StringComparison.OrdinalIgnoreCase)),
                layoutArg >= 0 && layoutArg + 1 < args.Length ? args[layoutArg + 1] : null);
        }

        if (args.Any(a => string.Equals(a, "--gossip-select-self-test", StringComparison.OrdinalIgnoreCase)))
            return GossipSelectionProbe.RunSelfTest();
        int gossipTest = Array.FindIndex(args, a => string.Equals(a, "--gossip-select-test", StringComparison.OrdinalIgnoreCase));
        if (gossipTest >= 0)
        {
            int layoutArg = Array.FindIndex(args, a => string.Equals(a, "--keyboard-layout", StringComparison.OrdinalIgnoreCase));
            return GossipSelectionProbe.Run(gossipTest + 1 < args.Length ? args[gossipTest + 1] : null,
                args.Any(a => string.Equals(a, "--execute", StringComparison.OrdinalIgnoreCase)),
                layoutArg >= 0 && layoutArg + 1 < args.Length ? args[layoutArg + 1] : null);
        }

        if (args.Any(a => string.Equals(a, "--turn-in-interaction-self-test", StringComparison.OrdinalIgnoreCase)))
            return TurnInInteractionProbe.RunSelfTest();
        int interactionTest = Array.FindIndex(args, a => string.Equals(a, "--turn-in-interaction-test", StringComparison.OrdinalIgnoreCase));
        if (interactionTest >= 0)
            return TurnInInteractionProbe.Run(interactionTest + 1 < args.Length ? args[interactionTest + 1] : null,
                args.Any(a => string.Equals(a, "--execute", StringComparison.OrdinalIgnoreCase)));

        if (args.Any(a => string.Equals(a, "--turn-in-self-test", StringComparison.OrdinalIgnoreCase)))
            return TurnInApproachProbe.RunSelfTest();
        int approachTest = Array.FindIndex(args, a => string.Equals(a, "--turn-in-approach-test", StringComparison.OrdinalIgnoreCase));
        if (approachTest >= 0)
            return TurnInApproachProbe.Run(approachTest + 1 < args.Length ? args[approachTest + 1] : null,
                args.Any(a => string.Equals(a, "--execute", StringComparison.OrdinalIgnoreCase)));

        if (args.Any(a => string.Equals(a, "--quest-order-self-test", StringComparison.OrdinalIgnoreCase)))
            return QuestOrderProbe.RunSelfTest();
        int orderTest = Array.FindIndex(args, a => string.Equals(a, "--quest-order-test", StringComparison.OrdinalIgnoreCase));
        if (orderTest >= 0)
            return QuestOrderProbe.RunLive(orderTest + 1 < args.Length ? args[orderTest + 1] : null);

        if (args.Any(a => string.Equals(a, "--quest-cache-metadata-test", StringComparison.OrdinalIgnoreCase)))
            return QuestCacheProbe.RunLive();
        int fileTest = Array.FindIndex(args, a => string.Equals(a, "--quest-cache-file-test", StringComparison.OrdinalIgnoreCase));
        if (fileTest >= 0)
            return QuestCacheProbe.RunFile(fileTest + 1 < args.Length ? args[fileTest + 1] : null);
        int selfTest = Array.FindIndex(args, a => string.Equals(a, "--quest-cache-self-test", StringComparison.OrdinalIgnoreCase));
        if (selfTest >= 0)
            return QuestCacheSelfTest.Run(selfTest + 1 < args.Length ? args[selfTest + 1] : null);

        if (args.Any(a => string.Equals(a, "--quest-cache-test", StringComparison.OrdinalIgnoreCase)))
            return RunQuestCacheTest();

        if (args.Any(a => string.Equals(a, "--quest-log-test", StringComparison.OrdinalIgnoreCase)))
            return RunQuestLogTest();

        bool armed = args.Any(a => string.Equals(a, "--combat-routine-test", StringComparison.OrdinalIgnoreCase));
        UInputPlayerMover? mover = null;
        UInputClientTargeting? targetInput = null;
        UInputCombatActions? combatInput = null;
        MinimalAutoAttackRoutine? routine = null;

        try
        {
            Console.WriteLine("Honorbuddy 5875 Phase 16.1 - minimal CombatRoutine auto-attack state machine");
            Console.WriteLine("------------------------------------------------------------------------");

            ObjectManager.Initialize5875();
            FactionTemplateStore5875.Initialize();
            LocalPlayer me = StyxWoW.Me
                ?? throw new InvalidOperationException("LocalPlayer unavailable after initialization.");

            List<WoWUnit> strictHostiles = ObjectManager.GetObjectsOfType<WoWUnit>(true, false)
                .Where(u => u.IsValid && u.IsStrictHostileCombatCandidate && !u.Combat)
                .OrderBy(u => u.Distance2D)
                .ToList();

            WoWUnit? seed = strictHostiles
                .Where(u => u.Distance2D >= MinSeedDistance && u.Distance2D <= MaxSeedDistance)
                .FirstOrDefault();

            if (seed is null)
            {
                Console.WriteLine($"PID:                 {ObjectManager.WoWProcess?.Id}");
                Console.WriteLine($"Player:              {me.Race} class={me.ClassId} level={me.Level}");
                Console.WriteLine($"Location:            {me.Location}");
                Console.WriteLine($"Strict hostiles:     {strictHostiles.Count}");
                Console.WriteLine();
                Console.WriteLine($"PHASE 16.1 PREFLIGHT: BLOCKED - no strict hostile between {MinSeedDistance:F0}-{MaxSeedDistance:F0} yards.");
                PrintNearest(strictHostiles);
                return 3;
            }

            string profilePath = WriteProfile(seed, strictHostiles, me.Location);
            ProfileManager.LoadNew(profilePath);
            Targeting.Instance.Pulse();
            WoWUnit? preferred = Targeting.Instance.FirstUnit;

            Console.WriteLine($"PID:                 {ObjectManager.WoWProcess?.Id}");
            Console.WriteLine($"WoW module base:     0x{ObjectManager.ModuleBase:X8}");
            Console.WriteLine($"ObjectManager:       0x{ObjectManager.ObjectManagerAddress:X8}");
            Console.WriteLine($"Local GUID:          0x{ObjectManager.LocalGuid:X16}");
            Console.WriteLine($"Player:              {me.Race} class={me.ClassId} level={me.Level}");
            Console.WriteLine($"Player health:       {me.CurrentHealth}/{me.MaxHealth}");
            Console.WriteLine($"Player combat:       {me.Combat}");
            Console.WriteLine($"Client target before:0x{me.CurrentTargetGuid:X16}");
            Console.WriteLine($"Generated profile:   {profilePath}");
            Console.WriteLine();

            Console.WriteLine("Preferred internal strict-hostile target");
            Console.WriteLine("----------------------------------------");
            Console.WriteLine($"GUID:                 0x{preferred?.Guid ?? 0:X16}");
            Console.WriteLine($"Entry:                {preferred?.Entry ?? 0}");
            Console.WriteLine($"Level:                {preferred?.Level ?? 0}");
            Console.WriteLine($"Reaction:             {preferred?.MyReaction.ToString() ?? "n/a"}");
            Console.WriteLine($"Strict hostile:       {preferred?.IsStrictHostileCombatCandidate ?? false}");
            Console.WriteLine($"Distance:             {preferred?.Distance2D ?? double.NaN:F2} yards");
            Console.WriteLine();

            Console.WriteLine("Phase-16 CombatRoutine surface");
            Console.WriteLine("------------------------------");
            Console.WriteLine("Targeting:             adopt valid target or Tab through /dev/uinput");
            Console.WriteLine("Movement:              Navigator -> /dev/uinput");
            Console.WriteLine("CombatRoutine states:  Approach -> StartAttack -> MaintainMelee -> TargetDead/Aborted");
            Console.WriteLine("Attack action:         T = Attack Target toggle");
            Console.WriteLine("Abilities/spells:      none");
            Console.WriteLine("Player-health floor:   50% of health at combat-routine start");
            Console.WriteLine($"Combat timeout:        {CombatTimeout.TotalSeconds:F0}s");
            Console.WriteLine("Memory writes:         none");
            Console.WriteLine("Injection/internal calls:none");
            Console.WriteLine();

            bool preflight =
                !me.Combat &&
                preferred is not null &&
                preferred.MyReaction == WoWUnitReaction.Hostile &&
                preferred.IsStrictHostileCombatCandidate &&
                Bots.Grind.LevelBot.IsProfileTargetCandidate(preferred, ProfileManager.CurrentProfile);

            if (!preflight)
            {
                Console.WriteLine("PHASE 16.1 PREFLIGHT: FAIL - player/hostile/profile invariants did not hold.");
                if (me.Combat)
                    Console.WriteLine("Leave combat before running this probe.");
                return 2;
            }

            Console.WriteLine("PHASE 16.1 PREFLIGHT: PASS");
            Console.WriteLine("IMPORTANT: this probe expects WoW's Attack Target action to be bound to T.");
            if (!armed)
            {
                Console.WriteLine("No input was sent because --combat-routine-test was not specified.");
                Console.WriteLine("Run again with --combat-routine-test for the live minimal CombatRoutine test.");
                return 0;
            }

            mover = new UInputPlayerMover();
            targetInput = new UInputClientTargeting();
            combatInput = new UInputCombatActions();
            Navigator.PlayerMover = mover;

            Console.WriteLine();
            Console.WriteLine("LIVE MINIMAL COMBATROUTINE TEST IS ARMED.");
            Console.WriteLine("Alt-Tab to WoW now. The routine will select one profile-valid strict hostile,");
            Console.WriteLine("approach melee range, start normal auto-attack once, maintain melee range, and stop on death/safety/timeout.");
            Console.WriteLine("No abilities, Lua, memory writes or injection are used.");
            Console.WriteLine("Take manual control immediately if the routine reports Aborted.");
            for (int i = 6; i >= 1; i--)
            {
                Console.WriteLine($"Starting in {i}...");
                Thread.Sleep(1000);
            }

            ObjectManager.Update();
            me = StyxWoW.Me ?? throw new InvalidOperationException("LocalPlayer unavailable before target sync.");
            if (me.Combat)
            {
                Console.WriteLine();
                Console.WriteLine("PHASE 16.1 RESULT: BLOCKED - player entered combat before target synchronization.");
                return 3;
            }

            Targeting.Instance.Pulse();
            preferred = Targeting.Instance.FirstUnit
                ?? throw new InvalidOperationException("No internal profile target is available at live start.");

            ClientTargetSelectionResult selection = ClientTargetSelector5875.SelectProfileHostile(
                targetInput,
                ProfileManager.CurrentProfile,
                preferred.Guid,
                maxTabAttempts: 12,
                settleMilliseconds: 150,
                minimumDistance: 6.0,
                maximumDistance: MaximumProfileDistance);

            Console.WriteLine();
            Console.WriteLine("Client target synchronization");
            Console.WriteLine("-----------------------------");
            foreach (ClientTargetAttempt attempt in selection.Attempts)
            {
                Console.WriteLine(
                    $"#{attempt.Attempt,2}: guid=0x{attempt.ClientGuid:X16} entry={attempt.Entry,-6} " +
                    $"reaction={attempt.Reaction?.ToString() ?? "n/a",-9} strict={attempt.IsStrictHostile,-5} " +
                    $"profile={attempt.IsProfileCandidate,-5} dist={attempt.Distance,6:F2} preferred={attempt.MatchesPreferred}");
            }

            if (!selection.Accepted || selection.FinalGuid == 0)
            {
                Navigator.Clear();
                Console.WriteLine();
                Console.WriteLine($"PHASE 16.1 RESULT: FAIL - client target synchronization failed: {selection.StopReason}");
                return 2;
            }

            ulong selectedGuid = selection.FinalGuid;
            ObjectManager.Update();
            me = StyxWoW.Me ?? throw new InvalidOperationException("LocalPlayer unavailable after target sync.");
            WoWUnit? target = ObjectManager.GetObjectByGuid<WoWUnit>(selectedGuid);
            if (!ValidateTarget(me, target, selectedGuid))
            {
                Console.WriteLine();
                Console.WriteLine("PHASE 16.1 RESULT: FAIL - synchronized client target failed combat-routine validation.");
                return 2;
            }

            uint targetStartHealth = target!.CurrentHealth;
            uint playerStartHealth = me.CurrentHealth;
            WoWPoint previous = me.Location;
            double cumulativeTravel = 0;
            DateTime deadline = DateTime.UtcNow + CombatTimeout;
            DateTime nextPrint = DateTime.MinValue;

            routine = new MinimalAutoAttackRoutine(selectedGuid, combatInput);
            RoutineManager.SetCurrent(routine);
            routine.Initialize();

            Console.WriteLine();
            Console.WriteLine($"Adopted client GUID:  0x{selectedGuid:X16}");
            Console.WriteLine($"Preferred GUID match: {selectedGuid == preferred.Guid}");
            Console.WriteLine($"Starting target HP:    {targetStartHealth}/{target.MaxHealth}");
            Console.WriteLine();
            Console.WriteLine("CombatRoutine observations");
            Console.WriteLine("--------------------------");

            while (DateTime.UtcNow < deadline &&
                   routine.State is not MinimalCombatState.TargetDead and not MinimalCombatState.Aborted)
            {
                routine.Pulse();
                Thread.Sleep(70);

                ObjectManager.Update();
                me = StyxWoW.Me ?? throw new InvalidOperationException("LocalPlayer disappeared during CombatRoutine test.");
                target = ObjectManager.GetObjectByGuid<WoWUnit>(selectedGuid);
                cumulativeTravel += previous.Distance2D(me.Location);
                previous = me.Location;

                if (DateTime.UtcNow >= nextPrint)
                {
                    Console.WriteLine(
                        $"state={routine.State,-13} dist={target?.Distance2D ?? double.NaN,6:F2} " +
                        $"meCombat={me.Combat,-5} meHP={me.CurrentHealth,3}/{me.MaxHealth,-3} " +
                        $"targetHP={target?.CurrentHealth ?? 0,3}/{target?.MaxHealth ?? 0,-3} " +
                        $"client=0x{me.CurrentTargetGuid:X16} damage={routine.DamageObserved}");
                    nextPrint = DateTime.UtcNow + TimeSpan.FromMilliseconds(240);
                }
            }

            bool timedOut = DateTime.UtcNow >= deadline && routine.State != MinimalCombatState.TargetDead;
            if (timedOut)
                routine.StopAttackIfNeeded();

            Navigator.Clear();
            ObjectManager.Update();
            LocalPlayer endPlayer = StyxWoW.Me
                ?? throw new InvalidOperationException("LocalPlayer unavailable at end of CombatRoutine test.");
            WoWUnit? endTarget = ObjectManager.GetObjectByGuid<WoWUnit>(selectedGuid);
            bool targetDead = routine.State == MinimalCombatState.TargetDead || endTarget is null || !endTarget.IsAlive || endTarget.CurrentHealth == 0;
            uint endTargetHealth = endTarget?.CurrentHealth ?? 0;
            bool clientHeldUntilTerminal = targetDead || endPlayer.CurrentTargetGuid == selectedGuid;

            Console.WriteLine();
            Console.WriteLine("Minimal CombatRoutine result");
            Console.WriteLine("----------------------------");
            Console.WriteLine($"Preferred internal GUID:0x{preferred.Guid:X16}");
            Console.WriteLine($"Selected client GUID:   0x{selectedGuid:X16}");
            Console.WriteLine($"Preferred GUID match:   {selectedGuid == preferred.Guid}");
            Console.WriteLine($"Final routine state:    {routine.State}");
            Console.WriteLine($"Target dead:            {targetDead}");
            Console.WriteLine($"Target health:          {targetStartHealth} -> {endTargetHealth}");
            Console.WriteLine($"Damage observed:        {routine.DamageObserved}");
            Console.WriteLine($"Player health:          {playerStartHealth} -> {endPlayer.CurrentHealth}");
            Console.WriteLine($"Lowest player health:   {routine.LowestPlayerHealth}");
            Console.WriteLine($"Closest target distance:{routine.ClosestDistance:F2} yards");
            Console.WriteLine($"Client target terminal: 0x{endPlayer.CurrentTargetGuid:X16}");
            Console.WriteLine($"Target held to terminal:{clientHeldUntilTerminal}");
            Console.WriteLine($"Attack start taps:      {routine.AttackStartTaps}");
            Console.WriteLine($"Attack stop taps:       {routine.AttackStopTaps}");
            Console.WriteLine($"Navigator.MoveTo calls: {routine.MoveCalls}");
            Console.WriteLine($"Cumulative travel:      {cumulativeTravel:F2} yards");
            Console.WriteLine($"Timed out:              {timedOut}");
            Console.WriteLine($"Stop reason:            {(timedOut ? $"{CombatTimeout.TotalSeconds:F0} second combat timeout" : routine.StopReason)}");
            Console.WriteLine("Abilities/spells:       none");
            Console.WriteLine("Memory writes:          none");
            Console.WriteLine("Injection/internal calls:none");

            bool pass =
                targetDead &&
                routine.DamageObserved &&
                routine.AttackStartTaps == 1 &&
                routine.State == MinimalCombatState.TargetDead &&
                !timedOut &&
                routine.LowestPlayerHealth > 0;

            Console.WriteLine();
            if (pass)
            {
                Console.WriteLine("PHASE 16.1 RESULT: PASS - the minimal CombatRoutine acquired a verified hostile client target, maintained melee auto-attack, and observed target death on build 5875.");
                return 0;
            }

            if (timedOut)
            {
                Console.WriteLine("PHASE 16.1 RESULT: BLOCKED - combat timeout reached before target death; attack was stopped and movement released.");
                return 3;
            }

            Console.WriteLine("PHASE 16.1 RESULT: FAIL - minimal CombatRoutine terminal invariants did not hold. Do not add abilities yet.");
            return 2;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode is 13 or 1)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("PHASE 16.1 RESULT: BLOCKED - /dev/uinput permission denied.");
            Console.Error.WriteLine(ex.Message);
            return 4;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("PHASE 16.1 RESULT: ERROR");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            try { routine?.ShutDown(); } catch { }
            try { Navigator.Clear(); } catch { }
            try { mover?.Dispose(); } catch { }
            try { targetInput?.Dispose(); } catch { }
            try { combatInput?.Dispose(); } catch { }
            ObjectManager.Shutdown5875();
        }
    }

    private static int RunQuestCacheTest()
    {
        try
        {
            Console.WriteLine("Honorbuddy Reborn 1.12.1 - original WoWCache quest identity compatibility probe");
            Console.WriteLine("-------------------------------------------------------------------------------");

            ObjectManager.Initialize5875();
            LocalPlayer me = StyxWoW.Me
                ?? throw new InvalidOperationException("LocalPlayer unavailable after initialization.");

            Styx.Logic.Questing.QuestLog questLog = me.QuestLog;
            Cache questCache = StyxWoW.Cache[CacheDb.Quest];
            QuestCacheDiagnostics diagnostics = Vanilla5875QuestCache.GetDiagnostics();

            Console.WriteLine($"PID:                    {ObjectManager.WoWProcess?.Id}");
            Console.WriteLine($"Player:                 {me.Race} class={me.ClassId} level={me.Level}");
            Console.WriteLine($"QuestLog.QuestCount:    {questLog.QuestCount}");
            Console.WriteLine($"Quest cache file:       {diagnostics.Path ?? "<not found>"}");

            if (diagnostics.IsValid5875QuestCache)
            {
                Console.WriteLine($"WDB signature:          {diagnostics.Signature}");
                Console.WriteLine($"WDB build:              {diagnostics.Build}");
                Console.WriteLine($"WDB locale:             {diagnostics.Locale}");
                Console.WriteLine($"WDB records:            {diagnostics.RecordCount}");
            }
            else
            {
                Console.WriteLine($"WDB status:             unavailable ({diagnostics.Error ?? "unknown error"})");
            }

            Console.WriteLine();
            Console.WriteLine("Original Honorbuddy cache chain");
            Console.WriteLine("-------------------------------");
            Console.WriteLine("StyxWoW.Cache -> WoWCache[CacheDb.Quest] -> Cache.GetInfoBlockById -> InfoBlock.Quest");
            Console.WriteLine();

            int active = 0;
            bool allValid = true;
            for (uint index = 0; index < Vanilla5875.QuestLogSlotCount; index++)
            {
                uint id = questLog.GetQuestId(index);
                if (id == 0)
                    continue;

                InfoBlock? block = questCache.GetInfoBlockById(id);
                QuestCacheEntry entry = block?.Quest ?? default;
                Styx.Logic.Questing.PlayerQuest? playerQuest = questLog.GetQuestById(id);
                bool persisted = Vanilla5875QuestCache.ContainsPersistedRecord(id);
                string source = persisted ? "WDB" : "live-identity";

                bool valid =
                    block is not null &&
                    block.Id == id &&
                    entry.Id == id &&
                    playerQuest is not null &&
                    playerQuest.Id == id;

                allValid &= valid;
                Console.WriteLine(
                    $"slot={index,2} id={id,-6} cache={(block is not null),-5} " +
                    $"InfoBlock.Id={block?.Id ?? 0,-6} QuestCacheEntry.Id={entry.Id,-6} " +
                    $"PlayerQuest.Id={playerQuest?.Id ?? 0,-6} source={source,-13} valid={valid}");
                active++;
            }

            Console.WriteLine();
            bool pass =
                active > 0 &&
                active == questLog.QuestCount &&
                allValid;

            Console.WriteLine(pass
                ? "QUEST CACHE STEP 3 RESULT: PASS - original StyxWoW.Cache -> Cache -> InfoBlock -> QuestCacheEntry chain resolves live PlayerQuest IDs on build 5875."
                : "QUEST CACHE STEP 3 RESULT: FAIL - Honorbuddy cache-chain invariants did not hold.");
            Console.WriteLine("Public cache model:     StyxWoW.Cache -> WoWCache -> Cache -> InfoBlock -> QuestCacheEntry");
            Console.WriteLine("Build-specific layer:   Vanilla5875QuestCache (internal)");
            Console.WriteLine("Input:                  none");
            Console.WriteLine("Memory writes:          none");
            Console.WriteLine("Addon/chatlog bridge:   none");

            if (!diagnostics.IsValid5875QuestCache)
            {
                Console.WriteLine();
                Console.WriteLine("Note: the live quest identity fallback preserves original PlayerQuest behavior until");
                Console.WriteLine("the Vanilla client has flushed questcache.wdb. No alternate public API is exposed.");
            }

            return pass ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("QUEST CACHE STEP 3 RESULT: ERROR");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            ObjectManager.Shutdown5875();
        }
    }

    private static int RunQuestLogTest()
    {
        try
        {
            Console.WriteLine("Honorbuddy Reborn 1.12.1 - original QuestLog + PlayerQuest compatibility probe");
            Console.WriteLine("-------------------------------------------------------------------------------");

            ObjectManager.Initialize5875();
            LocalPlayer me = StyxWoW.Me
                ?? throw new InvalidOperationException("LocalPlayer unavailable after initialization.");

            Styx.Logic.Questing.QuestLog questLog = me.QuestLog;
            List<Styx.Logic.Questing.PlayerQuest> allQuests = questLog.GetAllQuests();

            Console.WriteLine($"PID:                    {ObjectManager.WoWProcess?.Id}");
            Console.WriteLine($"Player:                 {me.Race} class={me.ClassId} level={me.Level}");
            Console.WriteLine($"QuestLog.QuestCount:    {questLog.QuestCount}");
            Console.WriteLine($"GetAllQuests().Count:   {allQuests.Count}");
            Console.WriteLine();
            Console.WriteLine("Active PlayerQuest wrappers");
            Console.WriteLine("---------------------------");

            int active = 0;
            bool wrappersValid = true;
            for (uint index = 0; index < Vanilla5875.QuestLogSlotCount; index++)
            {
                uint id = questLog.GetQuestId(index);
                if (id == 0)
                    continue;

                Styx.Logic.Questing.QuestLogEntry info =
                    questLog.GetQuestInfo(checked((int)index));
                Styx.Logic.Questing.PlayerQuest? byIndex = questLog.GetQuest(index);
                Styx.Logic.Questing.PlayerQuest? byId = questLog.GetQuestById(id);

                Styx.Logic.Questing.WoWDescriptorQuest descriptor = default;
                bool gotData = byIndex?.GetData(ref descriptor) == true;
                bool expectedCompleted =
                    (info.State & Styx.Logic.Questing.StateFlag.Completed) != 0;
                bool expectedFailed =
                    (info.State & Styx.Logic.Questing.StateFlag.Failed) != 0;

                bool valid =
                    byIndex is not null &&
                    byId is not null &&
                    byIndex.Id == id &&
                    byId.Id == id &&
                    gotData &&
                    descriptor.Id == id &&
                    byIndex.IsCompleted == expectedCompleted &&
                    byIndex.IsFailed == expectedFailed;

                wrappersValid &= valid;
                Console.WriteLine(
                    $"slot={index,2} id={id,-6} PlayerQuest.Id={byIndex?.Id ?? 0,-6} " +
                    $"completed={byIndex?.IsCompleted ?? false,-5} failed={byIndex?.IsFailed ?? false,-5} " +
                    $"GetData={gotData,-5} flags={descriptor.Flags,-10} valid={valid}");
                active++;
            }

            Console.WriteLine();
            bool pass =
                active == questLog.QuestCount &&
                allQuests.Count == active &&
                wrappersValid;

            Console.WriteLine(pass
                ? "QUESTLOG STEP 2 RESULT: PASS - Honorbuddy GetAllQuests/GetQuest/GetQuestById return live PlayerQuest wrappers on build 5875."
                : "QUESTLOG STEP 2 RESULT: FAIL - PlayerQuest wrapper invariants did not hold.");
            Console.WriteLine("Public model:          Quest -> PlayerQuest -> WoWDescriptorQuest");
            Console.WriteLine("Build-specific layer:  Vanilla5875QuestLogReader (internal)");
            Console.WriteLine("Input:                 none");
            Console.WriteLine("Memory writes:         none");
            Console.WriteLine("Addon/chatlog bridge:  none");
            return pass ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("QUESTLOG STEP 2 RESULT: ERROR");
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            ObjectManager.Shutdown5875();
        }
    }

    private static bool ValidateTarget(LocalPlayer me, WoWUnit? target, ulong selectedGuid)
        => target is not null &&
           target.IsValid &&
           target.IsAlive &&
           me.CurrentTargetGuid == selectedGuid &&
           target.Guid == selectedGuid &&
           target.MyReaction == WoWUnitReaction.Hostile &&
           target.IsStrictHostileCombatCandidate &&
           Bots.Grind.LevelBot.IsProfileTargetCandidate(target, ProfileManager.CurrentProfile);

    private static void PrintNearest(IReadOnlyList<WoWUnit> hostiles)
    {
        Console.WriteLine();
        Console.WriteLine("Nearest strict-hostile distances");
        Console.WriteLine("--------------------------------");
        foreach (WoWUnit unit in hostiles.Take(8))
        {
            Console.WriteLine(
                $"0x{unit.Guid:X16} entry={unit.Entry,-6} lvl={unit.Level,-3} " +
                $"dist={unit.Distance2D,6:F2} combat={unit.Combat,-5} reaction={unit.MyReaction}");
        }
        Console.WriteLine();
        Console.WriteLine($"Move so at least one listed strict hostile is {MinSeedDistance:F0}-{MaxSeedDistance:F0} yards away and run again.");
    }

    private static string WriteProfile(WoWUnit seed, IReadOnlyList<WoWUnit> hostiles, WoWPoint playerLocation)
    {
        List<WoWUnit> envelope = hostiles
            .Where(u => u.IsValid && u.IsStrictHostileCombatCandidate && !u.Combat)
            .Where(u => u.Distance2D >= 6.0 && u.Distance2D <= MaximumProfileDistance)
            .ToList();
        if (envelope.Count == 0)
            envelope.Add(seed);

        int minLevel = envelope.Min(u => u.Level);
        int maxLevel = envelope.Max(u => u.Level);
        string factions = string.Join(" ", envelope.Select(u => u.FactionId).Distinct().OrderBy(v => v));

        static string F(float value) => value.ToString("0.000", CultureInfo.InvariantCulture);

        var doc = new XDocument(
            new XElement("HBProfile",
                new XElement("Name", "Honorbuddy 5875 Phase 16 Minimal CombatRoutine Probe"),
                new XElement("MinLevel", "1"),
                new XElement("MaxLevel", "60"),
                new XElement("GrindArea",
                    new XAttribute("Name", "Phase16MinimalCombatArea"),
                    new XElement("TargetMinLevel", minLevel.ToString(CultureInfo.InvariantCulture)),
                    new XElement("TargetMaxLevel", maxLevel.ToString(CultureInfo.InvariantCulture)),
                    new XElement("MaxDistance", MaximumProfileDistance.ToString("0", CultureInfo.InvariantCulture)),
                    new XElement("RandomizeHotspots", "False"),
                    new XElement("Factions", factions),
                    new XElement("Hotspots",
                        new XElement("Hotspot",
                            new XAttribute("Name", "Start"),
                            new XAttribute("X", F(playerLocation.X)),
                            new XAttribute("Y", F(playerLocation.Y)),
                            new XAttribute("Z", F(playerLocation.Z))))),
                new XElement("QuestOrder",
                    new XElement("GrindTo", new XAttribute("GoalText", "Phase 16 minimal CombatRoutine validation")))));

        string path = Path.Combine(Directory.GetCurrentDirectory(), "phase16-combat-routine-profile.xml");
        doc.Save(path);
        return path;
    }
}
