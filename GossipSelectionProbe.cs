using Bots.Quest;
using Honorbuddy5875.Movement;
using Styx;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;

namespace Honorbuddy5875.Runtime;

internal static class GossipSelectionProbe
{
    internal static int Run(string? path, bool execute, string? keyboardLayout)
    {
        QuestBot? bot = null;
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Usage: --gossip-select-test QuestOrderStep5/CuttingTeeth.xml [--execute --keyboard-layout se|us]");
            // No implicit layout guess: slash/parentheses differ between Swedish and US.
            var layout = execute ? GossipCommandInput.ParseLayout(keyboardLayout) : GossipKeyboardLayout.Swedish;
            ProfileManager.LoadNew(path, false);
            var order = ProfileManager.CurrentProfile.QuestOrder;
            if (order.Count != 2 || order[0] is not ObjectiveNode objective || order[1] is not TurnInNode turn ||
                objective.QuestId != turn.QuestId)
                throw new InvalidDataException("This probe requires Objective -> TurnIn for the same quest.");
            ObjectManager.Initialize5875();
            using var dialog = new Vanilla5875QuestDialog();
            bot = new QuestBot { MovementExecutionEnabled = false, ClientTargetSyncEnabled = false };
            bot.Start();
            bot.EvaluateNow();
            Console.WriteLine($"PID: {ObjectManager.WoWProcess?.Id}; node={bot.CurrentProfileNodeIndex}; decision={bot.CurrentDecision.Kind}");
            Console.WriteLine(bot.CurrentDecision.Description);
            var npc = RequireReady(bot);
            uint initialHealth = StyxWoW.Me!.CurrentHealth;
            var before = dialog.Read();
            Print(before);
            var observed = new GossipSelectionPlan(npc.Guid, turn.QuestId, 0, []);
            if (observed.HasSelectedQuest(before))
            {
                Console.WriteLine($"QuestFrame.Instance.CurrentShownQuestId: {QuestFrame.Instance.CurrentShownQuestId}");
                Console.WriteLine("GOSSIP STEP 8 RESULT: ALREADY SELECTED - no input sent; this run did not verify selection.");
                return 0;
            }
            var plan = GossipSelectionPlan.Create(before, npc.Guid, turn.QuestId);
            Console.WriteLine($"Expected quest={plan.QuestId}; NPC=0x{plan.NpcGuid:X16}; Honorbuddy active index={plan.ActiveIndex}; Lua active index={plan.LuaIndex}");
            Console.WriteLine($"Command: {plan.Command}");
            if (!execute)
            {
                Console.WriteLine("GOSSIP STEP 8 PLAN RESULT: READY - expected quest uniquely resolved in the active gossip list.");
                Console.WriteLine("Read-only; no input initialized. TurnIn retained.");
                return 0;
            }
            Console.WriteLine($"Selection in 5 seconds; keyboard layout={keyboardLayout}. Focus WoW; keep the gossip list open, chat closed and Caps Lock off.");
            Console.WriteLine("Release all keys. The program opens chat, types the displayed /script command, and submits once. Do not type or click during the test.");
            using var keyboard = new UInputKeyboard(extraKeys: GossipCommandInput.KeyCodes);
            void Validate()
            {
                cancellation.Token.ThrowIfCancellationRequested();
                ObjectManager.Update();
                bot.EvaluateNow();
                var fresh = RequireReady(bot);
                if (fresh.Guid != npc.Guid || StyxWoW.Me!.CurrentHealth < initialHealth)
                    throw new InvalidOperationException("NPC changed or player lost health.");
                plan.ValidateUnchanged(dialog.Read());
            }
            void Delay(int ms)
            {
                if (cancellation.Token.WaitHandle.WaitOne(ms)) throw new OperationCanceledException();
            }
            for (int i = 5; i > 0; i--) { Console.WriteLine(i); Delay(1000); }
            Validate();
            GossipFrame.ArmedSelection = index =>
            {
                if (index != plan.ActiveIndex) throw new InvalidOperationException("Selection index differs from validated plan.");
                GossipCommandInput.Submit(index, layout, Validate, key => keyboard.Tap(key, 25),
                    down => keyboard.SetKey(42, down), Delay, cancellation.Token);
            };
            GossipFrame.Instance.SelectActiveQuest(plan.ActiveIndex);
            Console.WriteLine("GossipFrame.SelectActiveQuest(): command submitted once; awaiting quest conversation (8 seconds).");
            long deadline = Environment.TickCount64 + 8000;
            while (Environment.TickCount64 < deadline)
            {
                Delay(100);
                ObjectManager.Update();
                bot.EvaluateNow();
                var fresh = RequireReady(bot);
                if (fresh.Guid != plan.NpcGuid || StyxWoW.Me!.CurrentHealth < initialHealth)
                    throw new InvalidOperationException("NPC changed or player lost health while awaiting quest dialog.");
                var state = dialog.Read();
                if ((state.NpcGuid != 0 && state.NpcGuid != plan.NpcGuid) ||
                    (state.GossipNpcGuid != 0 && state.GossipNpcGuid != plan.NpcGuid))
                { Print(state); throw new InvalidOperationException("Another NPC conversation appeared."); }
                if (state.NpcGuid != 0 && state.RawStage is >= 1 and <= 3 && state.QuestId != 0 && state.QuestId != plan.QuestId)
                { Print(state); throw new InvalidOperationException("Another quest was selected."); }
                if (!plan.HasSelectedQuest(state)) continue;
                Delay(150);
                state = dialog.Read();
                if (!plan.HasSelectedQuest(state)) continue;
                uint shownId = QuestFrame.Instance.CurrentShownQuestId;
                if (shownId != plan.QuestId) throw new InvalidOperationException("Public QuestFrame ID changed during verification.");
                ObjectManager.Update();
                bot.EvaluateNow();
                fresh = RequireReady(bot);
                if (fresh.Guid != plan.NpcGuid || StyxWoW.Me!.CurrentHealth < initialHealth)
                    throw new InvalidOperationException("Final NPC/player verification failed.");
                Print(state);
                Console.WriteLine($"QuestFrame.Instance.CurrentShownQuestId: {shownId}");
                Console.WriteLine($"TurnIn node retained: {bot.CurrentProfileNodeIndex == 1}");
                Console.WriteLine("GOSSIP STEP 8 SELECTION RESULT: PASS - expected NPC and quest ID confirmed in progress/reward conversation.");
                Console.WriteLine("No CompleteQuest, Continue click, reward selection or reward acceptance was performed.");
                return 0;
            }
            Print(dialog.Read());
            Console.WriteLine("GOSSIP STEP 8 SELECTION RESULT: BLOCKED - expected quest dialog did not appear before timeout; no retry sent.");
            Console.WriteLine("Check WoW focus, Enter binding, keyboard layout and the typed command. Return to the gossip list before another attempt.");
            return 2;
        }
        catch (OperationCanceledException)
        { Console.WriteLine("GOSSIP STEP 8 RESULT: CANCELLED"); return 2; }
        catch (Exception ex)
        { Console.Error.WriteLine($"GOSSIP STEP 8 RESULT: BLOCKED - {ex.Message}"); return 2; }
        finally
        {
            GossipFrame.ArmedSelection = null;
            Console.CancelKeyPress -= cancel;
            bot?.Stop();
            ObjectManager.Shutdown5875();
            ProfileManager.LoadEmpty();
        }
    }
    private static QuestGiverSnapshot RequireReady(QuestBot bot)
    {
        var me = StyxWoW.Me;
        if (me is null || !me.IsAlive || me.Combat || bot.CurrentProfileNodeIndex != 1 ||
            bot.CurrentDecision.Kind != QuestDecisionKind.QuestGiverInRange || bot.CurrentQuestGiver is not { } npc)
            throw new InvalidOperationException("A completed active quest and living NPC in range are required; TurnIn must remain current.");
        return npc;
    }
    private static void Print(QuestDialogSnapshot s)
    {
        Console.WriteLine($"Quest conversation: npc=0x{s.NpcGuid:X16} stage={(s.NpcGuid == 0 ? "Closed" : ((QuestDialogStage)s.RawStage).ToString())} quest={s.QuestId} title={s.Title} requestPending={s.RequestPending}");
        Console.WriteLine($"Gossip conversation: npc=0x{s.GossipNpcGuid:X16}");
        int activeIndex = 0;
        foreach (var q in s.GossipQuests)
        {
            string index = q.Status is 3 or 4 ? (activeIndex++).ToString() : "available";
            Console.WriteLine($"Gossip quest: id={q.Id} status={q.Status} activeIndex={index} title={q.Title}");
        }
    }
    internal static int RunSelfTest() => GossipSelectionSelfTest.Run();
}
