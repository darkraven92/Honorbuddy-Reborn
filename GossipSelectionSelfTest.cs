using Styx.Logic.Inventory.Frames.Gossip;

namespace Honorbuddy5875.Runtime;

internal static class GossipSelectionSelfTest
{
    internal static int Run()
    {
        int checks = 0;
        try
        {
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
            void Reject<T>(Action action, string message) where T : Exception
            {
                try { action(); } catch (T) { checks++; return; }
                throw new InvalidOperationException(message);
            }
            DialogQuest Q(uint id, uint status) => new(id, 2, status, id == 788 ? "Cutting Teeth" : "Other");
            QuestDialogSnapshot List(params DialogQuest[] entries) => new(0, 0, 0, "", 0, [], [], 11, entries);
            var initial = List(Q(999, 2), Q(456, 3), Q(788, 4));
            var plan = GossipSelectionPlan.Create(initial, 11, 788);
            Check(plan.ActiveIndex == 1 && plan.LuaIndex == 2 && plan.Command == "/script SelectGossipActiveQuest(2)",
                "index counts only active quests and converts zero-based API to one-based Lua");
            Check(GossipSelectionPlan.Create(List(Q(788, 4)), 11, 788).ActiveIndex == 0, "observed user single active quest");
            plan.ValidateUnchanged(initial with { GossipQuests = initial.GossipQuests.ToArray() });
            Check(true, "equivalent reread accepted");
            Reject<InvalidOperationException>(() => plan.ValidateUnchanged(List(Q(788, 4), Q(456, 3))), "reordered list accepted");
            Reject<InvalidOperationException>(() => plan.ValidateUnchanged(List(Q(999, 2), Q(456, 3), Q(788, 3))), "status change accepted");
            foreach (var bad in new[] { initial with { GossipNpcGuid = 12 }, initial with { GossipNpcGuid = 0 },
                initial with { NpcGuid = 11 }, List(Q(999, 3)), List(Q(788, 2)), List(Q(788, 3), Q(788, 4)),
                List(Q(788, 4), Q(788, 2)), List(Q(0, 4), Q(788, 4)) })
                Reject<InvalidOperationException>(() => GossipSelectionPlan.Create(bad, 11, 788), "invalid list accepted");
            Reject<InvalidOperationException>(() => GossipSelectionPlan.Create(initial, 0, 788), "zero NPC accepted");
            Reject<InvalidOperationException>(() => GossipSelectionPlan.Create(initial, 11, 0), "zero quest accepted");
            var full = Enumerable.Range(1, 32).Select(i => Q((uint)i, 4)).ToArray();
            Check(GossipSelectionPlan.Create(List(full), 11, 32).Command == "/script SelectGossipActiveQuest(32)", "last active slot");
            Reject<InvalidOperationException>(() => GossipSelectionPlan.Create(List(full.Append(Q(33, 4)).ToArray()), 11, 32), "oversized list accepted");
            var selected = initial with { NpcGuid = 11, QuestId = 788, RawStage = 2, GossipNpcGuid = 0 };
            Check(plan.HasSelectedQuest(selected), "progress dialog accepted");
            Check(plan.HasSelectedQuest(selected with { RawStage = 3 }), "reward offer accepted without reward acceptance");
            Check(plan.HasSelectedQuest(selected with { GossipNpcGuid = 11 }), "same NPC retained gossip session allowed");
            foreach (var bad in new[] { selected with { NpcGuid = 12 }, selected with { QuestId = 789 },
                selected with { RawStage = 1 }, selected with { RawStage = 0 }, selected with { GossipNpcGuid = 12 } })
                Check(!plan.HasSelectedQuest(bad), "wrong selected state rejected");

            Check(GossipCommandInput.ParseLayout("se") == GossipKeyboardLayout.Swedish &&
                GossipCommandInput.ParseLayout("US") == GossipKeyboardLayout.Us, "explicit layout parser");
            Reject<ArgumentException>(() => GossipCommandInput.ParseLayout(null), "missing layout accepted");
            Reject<ArgumentException>(() => GossipCommandInput.ParseLayout("de"), "unsupported layout accepted");
            var se = GossipCommandInput.Encode(0, GossipKeyboardLayout.Swedish);
            var us = GossipCommandInput.Encode(0, GossipKeyboardLayout.Us);
            Check(se[0] == new GossipKeyStroke(8, true) && se[^3] == new GossipKeyStroke(9, true) && se[^1] == new GossipKeyStroke(10, true),
                "Swedish slash and parentheses keys");
            Check(us[0] == new GossipKeyStroke(53, false) && us[^3] == new GossipKeyStroke(10, true) && us[^1] == new GossipKeyStroke(11, true),
                "US slash and parentheses keys");
            Check(se[8] == new GossipKeyStroke(31, true), "uppercase S in Select");

            foreach (var layout in Enum.GetValues<GossipKeyboardLayout>())
            {
                var recorder = new Keys(); int validations = 0;
                GossipCommandInput.Submit(0, layout, () => validations++, recorder.Tap, recorder.Shift, _ => { }, CancellationToken.None);
                Check(validations == 2 && recorder.EnterCount == 2 && recorder.EscapeCount == 0 && !recorder.HeldShift,
                    "successful typing validates before opening and before submission, then releases modifiers");
                Check(recorder.Printable.Count == GossipCommandInput.CommandFor(0).Length, "one key stroke per command character");
            }
            {
                var keys = new Keys(); using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                Reject<OperationCanceledException>(() => GossipCommandInput.Submit(0, GossipKeyboardLayout.Swedish,
                    () => { }, keys.Tap, keys.Shift, _ => { }, cancelled.Token), "pre-cancellation ignored");
                Check(keys.EnterCount == 0 && keys.EscapeCount == 0 && keys.Printable.Count == 0, "pre-cancellation emits no input");
            }
            {
                var keys = new Keys(); using var cancelled = new CancellationTokenSource(); int delays = 0;
                Reject<OperationCanceledException>(() => GossipCommandInput.Submit(0, GossipKeyboardLayout.Swedish,
                    () => { }, keys.Tap, keys.Shift, _ => { if (++delays == 4) cancelled.Cancel(); }, cancelled.Token), "typing cancellation ignored");
                Check(keys.EnterCount == 1 && keys.EscapeCount == 1 && !keys.HeldShift, "cancelled partial command is escaped, never submitted");
            }
            {
                var keys = new Keys(); int validations = 0;
                Reject<InvalidOperationException>(() => GossipCommandInput.Submit(1, GossipKeyboardLayout.Us,
                    () => { if (++validations == 2) plan.ValidateUnchanged(List(Q(788, 4), Q(456, 3))); },
                    keys.Tap, keys.Shift, _ => { }, CancellationToken.None), "changed list still submitted");
                Check(keys.EnterCount == 1 && keys.EscapeCount == 1 && !keys.HeldShift, "changed list cancels prepared command");
            }
            {
                var keys = new Keys();
                Reject<IOException>(() => GossipCommandInput.Submit(0, GossipKeyboardLayout.Swedish, () => { },
                    key => { if (key == 8) throw new IOException("simulated write failure"); keys.Tap(key); },
                    keys.Shift, _ => { }, CancellationToken.None), "write failure swallowed");
                Check(keys.EnterCount == 1 && keys.EscapeCount == 1 && !keys.HeldShift, "failed shifted key releases modifier and cancels");
            }
            {
                var keys = new Keys();
                foreach (int bad in new[] { -1, 32 })
                    Reject<ArgumentOutOfRangeException>(() => GossipCommandInput.Submit(bad, GossipKeyboardLayout.Swedish,
                        () => { }, keys.Tap, keys.Shift, _ => { }, CancellationToken.None), "invalid index accepted by transport");
                Check(keys.EnterCount == 0, "invalid index sends no keys");
            }
            int calls = 0;
            GossipFrame.ArmedSelection = i => { Check(i == 1, "public API retains zero-based index"); calls++; };
            GossipFrame.Instance.SelectActiveQuest(1);
            Reject<InvalidOperationException>(() => GossipFrame.Instance.SelectActiveQuest(1), "public API reused consumed arm");
            Check(calls == 1, "public API submits once");
            GossipFrame.ArmedSelection = _ => throw new IOException("simulated failure");
            Reject<IOException>(() => GossipFrame.Instance.SelectActiveQuest(0), "failed selection swallowed");
            Check(GossipFrame.ArmedSelection is null, "failed API call also consumes arm");
            Console.WriteLine($"GOSSIP STEP 8 SELF TEST: PASS ({checks} checks; list identity/index, public API, keyboard sequencing and cancellation; no live input)");
            return 0;
        }
        catch (Exception ex)
        { Console.Error.WriteLine($"GOSSIP STEP 8 SELF TEST: FAIL after {checks} checks - {ex}"); return 1; }
        finally { GossipFrame.ArmedSelection = null; }
    }
    private sealed class Keys
    {
        internal int EnterCount, EscapeCount;
        internal bool HeldShift;
        internal List<GossipKeyStroke> Printable = new();
        internal void Shift(bool down) => HeldShift = down;
        internal void Tap(ushort key)
        {
            if (key == 28) EnterCount++;
            else if (key == 1) EscapeCount++;
            else Printable.Add(new(key, HeldShift));
        }
    }
}
