using Bots.Quest;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;

namespace Honorbuddy5875.Runtime;

internal static class QuestRewardDialogSelfTest
{
    internal static int Run()
    {
        int checks = 0;
        string folder = Path.Combine(Path.GetTempPath(), "hb-reward-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
            void Reject<T>(Action action, string message) where T : Exception
            { try { action(); } catch (T) { checks++; return; } throw new InvalidOperationException(message); }
            var h = new Harness(Closed());
            var result = h.Run();
            Check(result == new QuestRewardDialogResult(true, true, true) && h.Actions.SequenceEqual(new[] { "interact", "select:0", "continue" }),
                "closed -> gossip -> progress -> reward through the real flow");
            Check(h.Active && h.Current.QuestId == 2383 && h.Current.RawStage == 3, "reward offer leaves quest active");
            h = new Harness(Gossip()); result = h.Run();
            Check(!result.Interacted && result.Selected && result.Continued, "reuse an open gossip list");
            h = new Harness(Progress()); result = h.Run();
            Check(h.Actions.SequenceEqual(new[] { "continue" }) && result.Continued, "progress-only entry point");
            h = new Harness(Reward()); result = h.Run();
            Check(h.Actions.Count == 0 && !result.Continued, "existing reward does not execute or claim Continue");
            h = new Harness(Closed()) { AfterInteract = Reward() }; result = h.Run();
            Check(result.Interacted && !result.Selected && !result.Continued, "direct reward response is distinct from Continue");
            h = new Harness(Gossip()) { AfterSelect = Reward() }; result = h.Run();
            Check(result.Selected && !result.Continued, "selection can directly show a reward");
            h = new Harness(Closed()) { AfterInteract = Gossip() with { GossipNpcGuid = 22 } };
            Reject<InvalidOperationException>(() => h.Run(), "wrong NPC response accepted");
            Check(h.Actions.SequenceEqual(new[] { "interact" }), "wrong NPC causes no selection");
            h = new Harness(Gossip()) { AfterSelect = Progress() with { QuestId = 789 } };
            Reject<InvalidOperationException>(() => h.Run(), "wrong selected quest accepted");
            Check(!h.Actions.Contains("continue"), "wrong quest is never continued");
            h = new Harness(Progress() with { RawStage = 1 });
            Reject<InvalidOperationException>(() => h.Run(), "quest offer treated as turn-in progress");
            Check(h.Actions.Count == 0, "details stage causes no input");
            h = new Harness(Progress() with { RequestPending = 1 });
            Reject<InvalidOperationException>(() => h.Run(), "pending progress request submitted again");
            Check(h.Actions.Count == 0, "pending request causes no input");
            h = new Harness(Progress()) { AfterContinue = Progress() };
            Reject<TimeoutException>(() => h.Run(), "unchanged progress incorrectly passes");
            Check(h.Actions.Count(a => a == "continue") == 1, "timeout never retries Continue");
            h = new Harness(Gossip()) { LoseQuestAfterSelect = true };
            Reject<InvalidOperationException>(() => h.Run(), "missing quest advanced to reward");
            Check(!h.Actions.Contains("continue"), "lost quest prevents continuation");
            h = new Harness(Gossip() with { GossipQuests = [new(2383, 1, 4, "Simple Parchment"), new(2383, 1, 4, "Simple Parchment")] });
            Reject<InvalidOperationException>(() => h.Run(), "duplicate quest list accepted");
            Check(h.Actions.Count == 0, "ambiguous list sends no selection");
            foreach (var bad in new[] { Reward() with { NpcGuid = 22 }, Reward() with { QuestId = 789 },
                Reward() with { RequestPending = 1 }, Reward() with { GossipNpcGuid = 22 }, Progress() })
                Check(!QuestRewardDialogFlow.IsReward(bad, 11, 2383), "invalid reward state rejected");

            var single = new OrderNodeCollection { new TurnInNode(2383, 3153) };
            Check(TurnInProbeProfile.Validate(single).Index == 0, "single TurnIn has index zero");
            var pair = new OrderNodeCollection { new ObjectiveNode(788, "KillMob", 3098, 10), new TurnInNode(788, 3143) };
            Check(TurnInProbeProfile.Validate(pair).Index == 1, "existing Cutting Teeth profile stays compatible");
            Reject<InvalidDataException>(() => TurnInProbeProfile.Validate(new() { new TurnInNode(0, 3153) }), "zero quest allowed");
            Reject<InvalidDataException>(() => TurnInProbeProfile.Validate(new() { new ObjectiveNode(788, "KillMob", 3098, 10), new TurnInNode(2383, 3153) }), "different quest nodes allowed");
            string profile = Path.Combine(folder, "SimpleParchment.xml");
            File.WriteAllText(profile, "<HBProfile><QuestOrder><TurnIn QuestId=\"2383\" TurnInId=\"3153\" /></QuestOrder></HBProfile>");
            ProfileManager.LoadNew(profile, false);
            var live = new QuestOrderSnapshot(true, true, false, [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]);
            var bot = new QuestBot(_ => live)
            { ReadQuestGiver = (_, _) => new(11, 3153, new(1, 2, 3), 3, 3, 4, true, false, false) };
            bot.Start(); bot.Root.Start(null); bot.Root.Tick(null); bot.Root.Tick(null);
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverInRange && bot.TransitionLog.Count == 0,
                "actual QuestBot root handles completed item quest with TurnIn only; no fake objective");
            live = live with { Completed = false }; bot.Root.Tick(null);
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "incomplete quest retains TurnIn");
            live = QuestOrderSnapshot.Missing("removed"); bot.Root.Tick(null);
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "absence is not reward acceptance");
            bot.Root.Stop(null); bot.Stop();

            foreach (var layout in Enum.GetValues<GossipKeyboardLayout>())
            {
                var keys = new Keys(); int validations = 0;
                GossipCommandInput.SubmitContinue(layout, () => validations++, keys.Tap, keys.Shift, _ => { }, CancellationToken.None);
                Check(keys.Enters == 2 && keys.Escapes == 0 && !keys.ShiftHeld && validations == 2,
                    "Continue transport validates twice and submits exactly once");
                Check(keys.Characters == GossipCommandInput.ContinueCommand.Length && GossipCommandInput.EncodeContinue(layout).All(k => GossipCommandInput.KeyCodes.Contains(k.Code)),
                    "every Continue command key is enabled in uinput");
            }
            Check(GossipCommandInput.ContinueCommand == "/script if IsQuestCompletable() then CompleteQuest() end",
                "only guarded native request, no reward acceptance command");
            {
                var keys = new Keys(); int validations = 0;
                Reject<InvalidOperationException>(() => GossipCommandInput.SubmitContinue(GossipKeyboardLayout.Swedish,
                    () => { if (++validations == 2) QuestRewardDialogFlow.RequireProgress(Reward(), 11, 2383); },
                    keys.Tap, keys.Shift, _ => { }, CancellationToken.None), "changed page still submitted");
                Check(keys.Enters == 1 && keys.Escapes == 1 && !keys.ShiftHeld, "changed page cancels pending command");
            }
            {
                var keys = new Keys(); using var cancelled = new CancellationTokenSource(); int delays = 0;
                Reject<OperationCanceledException>(() => GossipCommandInput.SubmitContinue(GossipKeyboardLayout.Us,
                    () => { }, keys.Tap, keys.Shift, _ => { if (++delays == 5) cancelled.Cancel(); }, cancelled.Token), "cancellation ignored");
                Check(keys.Enters == 1 && keys.Escapes == 1 && !keys.ShiftHeld, "cancelled Continue is not submitted");
            }
            int calls = 0;
            QuestFrame.ArmedContinue = () => calls++;
            QuestFrame.Instance.ClickContinue();
            Reject<InvalidOperationException>(() => QuestFrame.Instance.ClickContinue(), "unarmed Continue executed");
            Check(calls == 1, "public API consumes the arm once");
            QuestFrame.ArmedContinue = () => throw new IOException("failure");
            Reject<IOException>(() => QuestFrame.Instance.ClickContinue(), "input failure swallowed");
            Check(QuestFrame.ArmedContinue is null, "failure also consumes the arm");
            Console.WriteLine($"QUEST REWARD STEP 9 SELF TEST: PASS ({checks} checks; bounded dialog flow, TurnIn-only root, guarded input and cancellation; no live input)");
            return 0;
        }
        catch (Exception ex)
        { Console.Error.WriteLine($"QUEST REWARD STEP 9 SELF TEST: FAIL after {checks} checks - {ex}"); return 1; }
        finally { QuestFrame.ArmedContinue = null; ProfileManager.LoadEmpty(); Directory.Delete(folder, true); }
    }
    private static QuestDialogSnapshot Closed() => new(0, 0, 0, "", 0, [], [], 0, []);
    private static QuestDialogSnapshot Gossip() => Closed() with { GossipNpcGuid = 11, GossipQuests = [new(2383, 1, 4, "Simple Parchment")] };
    private static QuestDialogSnapshot Progress() => Closed() with { NpcGuid = 11, RawStage = 2, QuestId = 2383, Title = "Simple Parchment" };
    private static QuestDialogSnapshot Reward() => Progress() with { RawStage = 3 };
    private sealed class Harness(QuestDialogSnapshot initial)
    {
        internal QuestDialogSnapshot Current = initial;
        internal QuestDialogSnapshot AfterInteract = Gossip(), AfterSelect = Progress(), AfterContinue = Reward();
        internal bool Active = true, LoseQuestAfterSelect;
        internal List<string> Actions = new();
        internal QuestRewardDialogResult Run() => QuestRewardDialogFlow.Execute(11, 2383, () => Current,
            () => { if (!Active) throw new InvalidOperationException("quest is missing"); },
            () => { Actions.Add("interact"); Current = AfterInteract; },
            plan => { Actions.Add("select:" + plan.ActiveIndex); Current = AfterSelect; if (LoseQuestAfterSelect) Active = false; },
            () => { Actions.Add("continue"); Current = AfterContinue; },
            predicate => predicate(Current) ? Current : throw new TimeoutException("simulated timeout"));
    }
    private sealed class Keys
    {
        internal int Enters, Escapes, Characters;
        internal bool ShiftHeld;
        internal void Tap(ushort code) { if (code == 28) Enters++; else if (code == 1) Escapes++; else Characters++; }
        internal void Shift(bool down) => ShiftHeld = down;
    }
}
