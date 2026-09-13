using Bots.Quest;
using Honorbuddy5875.Movement;
using Styx;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Honorbuddy5875.Runtime;

internal static class QuestRewardAcceptProbe
{
    internal static int Run(string? path, bool execute, string? keyboardLayout)
    {
        QuestBot? bot = null; bool actionAttempted = false;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Usage: --quest-reward-accept-test QuestRewardStep9/SimpleParchment.xml [--execute --keyboard-layout se|us]");
            var layout = execute ? GossipCommandInput.ParseLayout(keyboardLayout) : GossipKeyboardLayout.Swedish;
            ProfileManager.LoadNew(path, false);
            var (turn, index) = TurnInProbeProfile.Validate(ProfileManager.CurrentProfile.QuestOrder);
            ObjectManager.Initialize5875();
            using var dialog = new Vanilla5875QuestDialog();
            using var playerReader = new Vanilla5875RewardState();
            bot = new QuestBot { MovementExecutionEnabled = false, ClientTargetSyncEnabled = false };
            bot.Start(); bot.EvaluateNow();
            Console.WriteLine($"Profile: {ProfileManager.CurrentProfile.Name}; PID={ObjectManager.WoWProcess?.Id}; TurnIn index={index}");
            Console.WriteLine(bot.CurrentDecision.Description);
            if (bot.CurrentProfileNodeIndex != index || bot.CurrentDecision.Kind != QuestDecisionKind.QuestGiverInRange || bot.CurrentQuestGiver is not { } npc)
                throw new InvalidOperationException("Completed active quest and expected NPC in range are required.");
            var quest = StyxWoW.Me!.QuestLog.GetQuestById(turn.QuestId) ?? throw new InvalidOperationException("Quest is not active.");
            Console.WriteLine($"Quest {turn.QuestId}: {quest.Name}; NPC entry={npc.Entry}; GUID=0x{npc.Guid:X16}");
            var initialDialog = dialog.Read();
            var initialPlayer = playerReader.Read(turn.QuestId);
            Print(initialPlayer, initialDialog);
            var attempt = new RewardAcceptance(turn.QuestId, npc.Guid, initialPlayer, initialDialog);
            bot.BeginRewardAcceptance(attempt);
            uint health = StyxWoW.Me.CurrentHealth;
            var position = StyxWoW.Me.Location;
            void ValidatePlayerNpc()
            {
                cancellation.Token.ThrowIfCancellationRequested();
                ObjectManager.Update();
                var me = StyxWoW.Me;
                var unit = ObjectManager.GetObjectByGuid<WoWUnit>(npc.Guid);
                if (me is null || !me.IsAlive || me.Combat || me.Guid != initialPlayer.Guid ||
                    me.CurrentHealth < health || !double.IsFinite(me.Location.Distance(position)) || me.Location.Distance(position) > 0.5 ||
                    unit is null || !unit.IsAlive || unit.Combat || unit.IsPlayerControlled || unit.Entry != turn.TurnInId ||
                    !double.IsFinite(unit.Location.Distance(me.Location)) || unit.Location.Distance(me.Location) >= npc.InteractRange)
                    throw new InvalidOperationException("Player/NPC identity, health, position or interaction range changed.");
            }
            ValidatePlayerNpc();
            Console.WriteLine($"Accept command: {GossipCommandInput.AcceptCommand}");
            Console.WriteLine("Confirmation requires quest removal, closed conversation, positive measured XP gain, and unchanged other quest IDs.");
            if (!execute)
            {
                Console.WriteLine("QUEST REWARD STEP 10 PLAN RESULT: READY - matching reward offer has no item choices.");
                Console.WriteLine("Read-only; no input initialized. Keep the reward offer open for the execute test.");
                return 0;
            }
            Console.WriteLine($"Acceptance in 5 seconds; keyboard layout={keyboardLayout}. Focus WoW, keep reward offer open, chat closed, Caps Lock off, all keys released.");
            Console.WriteLine("This WILL accept the reward. Do not click, move, type or perform other actions. One command; observation timeout 10 seconds; Ctrl+C cancels.");
            void Delay(int ms)
            { if (cancellation.Token.WaitHandle.WaitOne(ms)) throw new OperationCanceledException(); }
            for (int i = 5; i > 0; i--) { Console.WriteLine(i); Delay(1000); }
            ValidatePlayerNpc(); attempt.RequireReady(playerReader.Read(turn.QuestId), dialog.Read());
            using var keyboard = new UInputKeyboard(extraKeys: GossipCommandInput.KeyCodes);
            QuestFrame.ArmedCompletion = () =>
            {
                void Guard()
                { ValidatePlayerNpc(); attempt.RequireReady(playerReader.Read(turn.QuestId), dialog.Read()); }
                GossipCommandInput.SubmitAccept(layout, Guard, key => keyboard.Tap(key, 25),
                    down => keyboard.SetKey(42, down), Delay, cancellation.Token);
            };
            actionAttempted = true;
            QuestFrame.Instance.CompleteQuest();
            attempt.MarkSubmitted();
            Console.WriteLine("QuestFrame.CompleteQuest(): guarded GetQuestReward command submitted once; observing response.");
            long deadline = Environment.TickCount64 + 10000;
            RewardPlayerState after = initialPlayer; QuestDialogSnapshot final = initialDialog;
            while (Environment.TickCount64 < deadline)
            {
                Delay(100); ValidatePlayerNpc();
                after = playerReader.Read(turn.QuestId); final = dialog.Read();
                if (!attempt.Observe(after, final, out _)) continue;
                Delay(150); ValidatePlayerNpc();
                after = playerReader.Read(turn.QuestId); final = dialog.Read();
                if (!bot.ConfirmRewardAcceptance(attempt, after, final, out ulong gain)) continue;
                Print(after, final);
                foreach (string transition in bot.TransitionLog) Console.WriteLine(transition);
                bot.EvaluateNow(); bot.EvaluateNow();
                Console.WriteLine($"Measured XP gain: {gain}; TurnIn index: {index} -> {bot.CurrentProfileNodeIndex}; decision={bot.CurrentDecision.Kind}");
                Console.WriteLine("QUEST REWARD STEP 10 ACCEPT RESULT: PASS - acceptance submission followed by quest removal, closed dialog and XP gain; TurnIn advanced once.");
                Console.WriteLine("Scope: correlated live before/after evidence, not a captured server acknowledgement or persistent rewarded-quest history.");
                return 0;
            }
            Print(after, final);
            throw new TimeoutException("Confirmation criteria were not all observed within 10 seconds. No retry was sent.");
        }
        catch (Exception ex)
        {
            string result = actionAttempted ? "UNCONFIRMED" : ex is OperationCanceledException ? "CANCELLED" : "BLOCKED";
            Console.Error.WriteLine($"QUEST REWARD STEP 10 RESULT: {result} - {ex.Message}");
            Console.WriteLine($"Current node index: {bot?.CurrentProfileNodeIndex}. No automatic retry.");
            if (actionAttempted) Console.WriteLine("Input may have reached the client. Inspect the quest log and dialog before deciding the next action.");
            return 2;
        }
        finally
        {
            QuestFrame.ArmedCompletion = null;
            Console.CancelKeyPress -= cancel;
            bot?.Stop(); ObjectManager.Shutdown5875(); ProfileManager.LoadEmpty();
        }
    }

    private static void Print(RewardPlayerState p, QuestDialogSnapshot d)
    {
        Console.WriteLine($"Player GUID=0x{p.Guid:X16}; level={p.Level}; XP={p.Xp}/{p.NextXp}; active quest IDs=[{string.Join(',', p.ActiveIds)}]");
        Console.WriteLine($"Quest conversation: NPC=0x{d.NpcGuid:X16}; stage={(d.NpcGuid == 0 ? "Closed" : ((QuestDialogStage)d.RawStage).ToString())}; quest={d.QuestId}; requestPending={d.RequestPending}; gossipNPC=0x{d.GossipNpcGuid:X16}");
        if (d.Reward is not { } offer) return;
        Console.WriteLine($"Reward offer: fixedItems={offer.FixedItems.Length}; choices={offer.Choices.Length}; money={offer.Money}; spell={offer.Spell}");
        foreach (var item in offer.FixedItems) Console.WriteLine($"Fixed reward item: id={item.Id} count={item.Count}");
        foreach (var item in offer.Choices) Console.WriteLine($"Choice reward item: id={item.Id} count={item.Count}");
    }
}
