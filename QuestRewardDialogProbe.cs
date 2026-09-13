using Bots.Quest;
using Honorbuddy5875.Movement;
using Styx;
using Styx.Logic.Inventory.Frames.Gossip;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Profiles;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Honorbuddy5875.Runtime;

internal static class QuestRewardDialogProbe
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
                throw new ArgumentException("Usage: --quest-reward-dialog-test QuestRewardStep9/SimpleParchment.xml [--execute --keyboard-layout se|us]");
            var layout = execute ? GossipCommandInput.ParseLayout(keyboardLayout) : GossipKeyboardLayout.Swedish;
            ProfileManager.LoadNew(path, false);
            var (turn, turnIndex) = TurnInProbeProfile.Validate(ProfileManager.CurrentProfile.QuestOrder);
            ObjectManager.Initialize5875();
            using var dialog = new Vanilla5875QuestDialog();
            bot = new QuestBot { MovementExecutionEnabled = false, ClientTargetSyncEnabled = false };
            bot.Start(); bot.EvaluateNow();
            Console.WriteLine($"Profile: {ProfileManager.CurrentProfile.Name}; PID={ObjectManager.WoWProcess?.Id}; TurnIn index={turnIndex}");
            Console.WriteLine(bot.CurrentDecision.Description);
            var npc = RequireReady(bot, turn.QuestId, turnIndex);
            uint health = StyxWoW.Me!.CurrentHealth;
            Console.WriteLine($"Quest {turn.QuestId}: {StyxWoW.Me.QuestLog.GetQuestById(turn.QuestId)!.Name}; NPC entry={npc.Entry}; GUID=0x{npc.Guid:X16}; distance={npc.Distance:F2}");
            var initial = dialog.Read();
            Print(initial);
            QuestRewardDialogFlow.CheckIdentity(initial, npc.Guid, turn.QuestId);
            if (QuestRewardDialogFlow.IsReward(initial, npc.Guid, turn.QuestId))
            {
                Console.WriteLine($"QuestFrame.Instance.CurrentShownQuestId: {QuestFrame.Instance.CurrentShownQuestId}");
                Console.WriteLine("QUEST REWARD STEP 9 RESULT: ALREADY AT REWARD - no input sent; continuation was not exercised.");
                return 0;
            }
            if (initial.NpcGuid == 0 && initial.GossipNpcGuid != 0)
            {
                var plan = GossipSelectionPlan.Create(initial, npc.Guid, turn.QuestId);
                Console.WriteLine($"Gossip active index={plan.ActiveIndex}; command={plan.Command}");
            }
            if (initial.NpcGuid != 0)
                QuestRewardDialogFlow.RequireProgress(initial, npc.Guid, turn.QuestId);
            Console.WriteLine($"Continue command: {GossipCommandInput.ContinueCommand}");
            if (!execute)
            {
                Console.WriteLine("QUEST REWARD STEP 9 PLAN RESULT: READY - completed active quest and expected NPC in range.");
                Console.WriteLine("Read-only; no input initialized. Item possession is checked by native IsQuestCompletable when continuing.");
                return 0;
            }
            Console.WriteLine($"Starts in 5 seconds; keyboard layout={keyboardLayout}. Focus WoW, chat closed, Caps Lock off, all keys released.");
            Console.WriteLine("If the NPC conversation is closed, hold the pointer over the expected NPC's model. Do not click through any dialogs.");
            Console.WriteLine("At most one NPC click, one gossip selection and one Continue command. Stops at the reward offer; total action limit 30 seconds.");
            using var mouse = new UInputKeyboard(mouse: true);
            using var keyboard = new UInputKeyboard(extraKeys: GossipCommandInput.KeyCodes);
            void Delay(int ms)
            { if (cancellation.Token.WaitHandle.WaitOne(ms)) throw new OperationCanceledException(); }
            for (int i = 5; i > 0; i--) { Console.WriteLine(i); Delay(1000); }
            long deadline = Environment.TickCount64 + 30000;
            void Validate()
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (Environment.TickCount64 >= deadline) throw new TimeoutException("Total action time limit reached.");
                ObjectManager.Update(); bot.EvaluateNow();
                var current = RequireReady(bot, turn.QuestId, turnIndex);
                if (current.Guid != npc.Guid || StyxWoW.Me!.CurrentHealth < health)
                    throw new InvalidOperationException("NPC identity changed or player lost health.");
            }
            QuestDialogSnapshot WaitFor(Func<QuestDialogSnapshot, bool> predicate)
            {
                long responseDeadline = Math.Min(deadline, Environment.TickCount64 + 8000);
                while (Environment.TickCount64 < responseDeadline)
                {
                    Delay(100); Validate();
                    var s = dialog.Read();
                    if (!predicate(s)) continue;
                    Delay(150); Validate();
                    s = dialog.Read();
                    if (predicate(s)) { Print(s); return s; }
                }
                Print(dialog.Read());
                throw new TimeoutException("Expected conversation did not appear within 8 seconds; no retry was sent. Check the dialog and typed command.");
            }
            void Interact()
            {
                Validate();
                var target = ObjectManager.GetObjectsOfType<WoWUnit>(false, false).SingleOrDefault(u => u.Guid == npc.Guid)
                    ?? throw new InvalidOperationException("NPC disappeared.");
                Vanilla5875Interaction.ArmedClick = obj =>
                {
                    Vanilla5875Interaction.ArmedClick = null;
                    Validate();
                    Vanilla5875Interaction.ClickHovered(obj, npc.Guid, turn.TurnInId, dialog, mouse);
                };
                target.Interact();
                Console.WriteLine("WoWObject.Interact(): one right click sent.");
            }
            void Select(GossipSelectionPlan plan)
            {
                GossipFrame.ArmedSelection = index =>
                {
                    if (index != plan.ActiveIndex) throw new InvalidOperationException("Unexpected gossip index.");
                    void Guard() { Validate(); plan.ValidateUnchanged(dialog.Read()); }
                    GossipCommandInput.Submit(index, layout, Guard, key => keyboard.Tap(key, 25),
                        down => keyboard.SetKey(42, down), Delay, cancellation.Token);
                };
                GossipFrame.Instance.SelectActiveQuest(plan.ActiveIndex);
                Console.WriteLine($"GossipFrame.SelectActiveQuest({plan.ActiveIndex}): command submitted once.");
            }
            void Continue()
            {
                QuestFrame.ArmedContinue = () =>
                {
                    void Guard() { Validate(); QuestRewardDialogFlow.RequireProgress(dialog.Read(), npc.Guid, turn.QuestId); }
                    GossipCommandInput.SubmitContinue(layout, Guard, key => keyboard.Tap(key, 25),
                        down => keyboard.SetKey(42, down), Delay, cancellation.Token);
                };
                QuestFrame.Instance.ClickContinue();
                Console.WriteLine("QuestFrame.ClickContinue(): guarded native CompleteQuest command submitted once.");
            }
            var result = QuestRewardDialogFlow.Execute(npc.Guid, turn.QuestId, dialog.Read, Validate, Interact, Select, Continue, WaitFor);
            Validate();
            var final = dialog.Read();
            if (!QuestRewardDialogFlow.IsReward(final, npc.Guid, turn.QuestId) || QuestFrame.Instance.CurrentShownQuestId != turn.QuestId)
                throw new InvalidOperationException("Final QuestFrame ID/reward state changed.");
            Print(final);
            Console.WriteLine($"QuestFrame.Instance.CurrentShownQuestId: {turn.QuestId}");
            Console.WriteLine($"Actions: interact={result.Interacted}; select={result.Selected}; continue={result.Continued}");
            Console.WriteLine($"TurnIn index={bot.CurrentProfileNodeIndex}; quest still active and completed=True; node retained=True");
            Console.WriteLine(result.Continued
                ? "QUEST REWARD STEP 9 CONTINUE RESULT: PASS - progress -> reward offer confirmed for the expected NPC and quest."
                : "QUEST REWARD STEP 9 RESULT: REWARD READY - client opened reward offer directly; Continue was not exercised.");
            Console.WriteLine("STOPPED BEFORE REWARD ACCEPTANCE. No GetQuestReward, reward choice or final reward-button click was performed.");
            return 0;
        }
        catch (OperationCanceledException)
        { Console.WriteLine("QUEST REWARD STEP 9 RESULT: CANCELLED"); return 2; }
        catch (Exception ex)
        { Console.Error.WriteLine($"QUEST REWARD STEP 9 RESULT: BLOCKED - {ex.Message}"); return 2; }
        finally
        {
            Vanilla5875Interaction.ArmedClick = null;
            GossipFrame.ArmedSelection = null;
            QuestFrame.ArmedContinue = null;
            Console.CancelKeyPress -= cancel;
            bot?.Stop();
            ObjectManager.Shutdown5875();
            ProfileManager.LoadEmpty();
        }
    }

    private static QuestGiverSnapshot RequireReady(QuestBot bot, uint questId, int turnIndex)
    {
        var me = StyxWoW.Me;
        if (me is null || !me.IsAlive || me.Combat || bot.CurrentProfileNodeIndex != turnIndex ||
            bot.CurrentDecision.Kind != QuestDecisionKind.QuestGiverInRange || bot.CurrentQuestGiver is not { } npc)
            throw new InvalidOperationException("Completed active quest and expected NPC in range are required. Use the approach test with this profile first.");
        var quest = me.QuestLog.GetQuestById(questId) ?? throw new InvalidOperationException("Expected quest is no longer in the quest log.");
        _ = quest.Name; // Requires real metadata; identity fallback alone is insufficient for this probe.
        return npc;
    }
    private static void Print(QuestDialogSnapshot s)
    {
        Console.WriteLine($"Quest conversation: npc=0x{s.NpcGuid:X16}; stage={(s.NpcGuid == 0 ? "Closed" : ((QuestDialogStage)s.RawStage).ToString())}; quest={s.QuestId}; title={s.Title}; requestPending={s.RequestPending}");
        Console.WriteLine($"Gossip conversation: npc=0x{s.GossipNpcGuid:X16}");
        foreach (var q in s.GossipQuests) Console.WriteLine($"Gossip quest: id={q.Id}; status={q.Status}; title={q.Title}");
    }
}
