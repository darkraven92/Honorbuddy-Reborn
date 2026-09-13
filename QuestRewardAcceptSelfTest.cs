using System.Buffers.Binary;
using Bots.Quest;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Profiles;

namespace Honorbuddy5875.Runtime;

internal static class QuestRewardAcceptSelfTest
{
    private static QuestDialogSnapshot Closed() => new(0, 0, 0, "", 0, [], [], 0, []);
    private static QuestDialogSnapshot Reward() => Closed() with
    { NpcGuid = 11, RawStage = 3, QuestId = 2383, Title = "Simple Parchment", Reward = new([], [], 0, 0) };
    private static RewardPlayerState Before() => new(123, 22, 2, 100, 900, [789, 2383], true, false);
    private static RewardPlayerState After() => Before() with { Xp = 140, ActiveIds = [789], QuestCompleted = false };
    private static RewardAcceptance Attempt(bool submitted = true)
    {
        var a = new RewardAcceptance(2383, 11, Before(), Reward());
        if (submitted) a.MarkSubmitted();
        return a;
    }
    internal static int Run()
    {
        int checks = 0;
        string folder = Path.Combine(Path.GetTempPath(), "hb-accept-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); checks++; }
            void Reject<T>(Action action, string message) where T : Exception
            { try { action(); } catch (T) { checks++; return; } throw new InvalidOperationException(message); }
            var a = Attempt();
            Check(a.Observe(After(), Closed(), out ulong gain) && gain == 40, "correlated success with measured XP");
            Check(!a.Observe(Before(), Reward(), out _), "pending unchanged dialog cannot pass");
            Check(!a.Observe(After() with { Xp = 100 }, Closed(), out _), "quest absence alone cannot pass");
            Check(!a.Observe(After(), Reward(), out _), "open reward cannot pass");
            Check(!a.Observe(Before() with { Xp = 140 }, Closed(), out _), "XP plus closure with active quest cannot pass");
            Check(!a.Observe(After(), Closed() with { GossipNpcGuid = 11 }, out _), "gossip is not a closed conversation");
            Check(a.Observe(After() with { Level = 3, Xp = 20, NextXp = 1400 }, Closed(), out gain) && gain == 820,
                "one level-up includes XP before reset");
            foreach (var bad in new[] { After() with { Pid = 124 }, After() with { Guid = 23 },
                After() with { ActiveIds = [] }, After() with { ActiveIds = [789, 4641] },
                After() with { ActiveIds = [789, 789] }, After() with { QuestFailed = true },
                After() with { Level = 4 }, After() with { Xp = 99 }, After() with { NextXp = 0 },
                After() with { Xp = 900 }, After() with { NextXp = 1000 }, After() with { Level = 60 } })
                Reject<InvalidOperationException>(() => a.Observe(bad, Closed(), out _), "bad post-state accepted");
            foreach (var bad in new[] { Reward() with { NpcGuid = 12 }, Reward() with { QuestId = 789 },
                Reward() with { RawStage = 2 }, Closed() with { GossipNpcGuid = 12 } })
                Reject<InvalidOperationException>(() => a.Observe(After(), bad, out _), "wrong post dialog accepted");
            Reject<InvalidOperationException>(() => Attempt(false).Observe(After(), Closed(), out _), "absence before submission accepted");
            Reject<InvalidOperationException>(a.MarkSubmitted, "second submission accepted");
            Reject<InvalidOperationException>(() => a.RequireReady(Before(), Reward()), "resubmission guard accepted");
            foreach (var bad in new[] { Reward() with { Reward = null }, Reward() with { RequestPending = 1 },
                Reward() with { QuestId = 789 }, Reward() with { NpcGuid = 12 }, Reward() with { RawStage = 2 },
                Reward() with { Reward = new([], [new(10, 1)], 0, 0) } })
                Reject<InvalidOperationException>(() => new RewardAcceptance(2383, 11, Before(), bad), "bad initial offer accepted");
            foreach (var bad in new[] { Before() with { QuestCompleted = false }, Before() with { QuestFailed = true },
                Before() with { ActiveIds = [789] } })
                Reject<InvalidOperationException>(() => new RewardAcceptance(2383, 11, bad, Reward()), "bad initial player accepted");
            a = Attempt(false);
            Reject<InvalidOperationException>(() => a.RequireReady(Before() with { Xp = 101 }, Reward()), "XP changed while typing");
            Reject<InvalidOperationException>(() => a.RequireReady(Before(), Reward() with { Reward = new([], [], 1, 0) }), "reward changed while typing");

            // Raw data fixtures exercise the same decoders as the live reader, including stale closed data.
            byte[] block = new byte[Vanilla5875QuestDialog.QuestEnd - Vanilla5875QuestDialog.QuestStart];
            void Put(uint va, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan((int)(va - Vanilla5875QuestDialog.QuestStart)), value);
            Put(0xBE0810, 11); Put(0xBE0818, 3); Put(0xBE081C, 2383);
            Put(0xBDF020, 100); Put(0xBDF024, 2); Put(0xBDF02C, 200); Put(0xBDF030, 3);
            Put(0xBE082C, 30); Put(0xBE0830, 55);
            var decoded = Vanilla5875QuestDialog.Decode(block, 0, new byte[Vanilla5875QuestDialog.GossipEnd - Vanilla5875QuestDialog.GossipStart]);
            Check(decoded.Reward is { } r && r.FixedItems.SequenceEqual(new[] { new RewardItem(100, 2) }) &&
                r.Choices.SequenceEqual(new[] { new RewardItem(200, 3) }) && r.Money == 30 && r.Spell == 55, "interleaved reward decoder");
            Put(0xBE0810, 0);
            Check(Vanilla5875QuestDialog.Decode(block, 0, new byte[Vanilla5875QuestDialog.GossipEnd - Vanilla5875QuestDialog.GossipStart]).Reward is null,
                "closed dialog suppresses stale reward data");
            Put(0xBDF024, 0);
            Reject<InvalidDataException>(() => RewardOffer.Decode(block), "zero-count item accepted");
            byte[] descriptors = new byte[0xB38];
            void D(int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(descriptors.AsSpan(offset), value);
            D(0x88, 2); D(0x840, 123456); D(0x844, 0); D(0xB30, 100); D(0xB34, 900); D(0x318, 2383); D(0x31C, 1u << 24); D(0x324, 789);
            Check(Vanilla5875RewardState.Decode(descriptors, 123, 22, 2383).SameAs(Before()), "full-descriptor XP offsets; old relative offsets contain deliberately invalid values");
            D(0x31C, 2u << 24);
            Check(Vanilla5875RewardState.Decode(descriptors, 123, 22, 2383).QuestFailed, "failed flag is independent of XP");

            string profile = Path.Combine(folder, "profile.xml");
            File.WriteAllText(profile, "<HBProfile><QuestOrder><TurnIn QuestId=\"2383\" TurnInId=\"3153\" /></QuestOrder></HBProfile>");
            ProfileManager.LoadNew(profile, false);
            var live = new QuestOrderSnapshot(true, true, false, [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]);
            var bot = new QuestBot(_ => live) { ReadQuestGiver = (_, _) => new(11, 3153, new(1, 2, 3), 2, 2, 4, true, false, false) };
            bot.Start(); bot.EvaluateNow(); a = Attempt(false); bot.BeginRewardAcceptance(a);
            Reject<InvalidOperationException>(() => bot.BeginRewardAcceptance(Attempt(false)), "overlapping transaction accepted");
            a.MarkSubmitted();
            Check(!bot.ConfirmRewardAcceptance(a, After() with { Xp = 100 }, Closed(), out _) && bot.CurrentProfileNodeIndex == 0,
                "no XP retains actual bot node");
            live = QuestOrderSnapshot.Missing("removed"); bot.EvaluateNow();
            Check(bot.CurrentProfileNodeIndex == 0, "normal bot evaluation does not equate absence with turn-in");
            Check(bot.ConfirmRewardAcceptance(a, After(), Closed(), out gain) && bot.CurrentProfileNodeIndex == 1 && gain == 40,
                "bound transaction advances actual bot once");
            bot.EvaluateNow(); bot.EvaluateNow();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.ProfileComplete && bot.TransitionLog.Count == 1, "repeated ticks preserve one advancement");
            Reject<InvalidOperationException>(() => bot.ConfirmRewardAcceptance(a, After(), Closed(), out _), "receipt replay accepted");
            live = new(true, true, false, [0, 0, 0, 0], [0, 0, 0, 0], [0, 0, 0, 0]);
            bot.Start(); bot.EvaluateNow(); a = Attempt(false); bot.BeginRewardAcceptance(a); a.MarkSubmitted();
            bot.Stop();
            Reject<InvalidOperationException>(() => bot.ConfirmRewardAcceptance(a, After(), Closed(), out _), "stopped bot accepted transaction");
            bot.Start(); bot.EvaluateNow(); a = Attempt(false); bot.BeginRewardAcceptance(a); a.MarkSubmitted();
            ProfileManager.LoadNew(profile, false);
            Reject<InvalidOperationException>(() => bot.ConfirmRewardAcceptance(a, After(), Closed(), out _), "reloaded profile accepted old transaction");
            bot.Stop();

            foreach (var layout in Enum.GetValues<GossipKeyboardLayout>())
            {
                int enters = 0, escapes = 0, validations = 0; bool shift = false;
                void Tap(ushort key) { if (key == 28) enters++; if (key == 1) escapes++; }
                GossipCommandInput.SubmitAccept(layout, () => validations++, Tap, value => shift = value, _ => { }, CancellationToken.None);
                Check(enters == 2 && escapes == 0 && validations == 2 && !shift, "accept transport submits exactly once");
                Check(GossipCommandInput.EncodeAccept(layout).All(k => GossipCommandInput.KeyCodes.Contains(k.Code)), "all acceptance keys enabled");
                int equalIndex = GossipCommandInput.AcceptCommand.IndexOf('=');
                Check(GossipCommandInput.EncodeAccept(layout)[equalIndex] == (layout == GossipKeyboardLayout.Swedish ? new GossipKeyStroke(11, true) : new GossipKeyStroke(13, false)),
                    "equals key matches SE/US layouts");
                enters = escapes = validations = 0;
                Reject<InvalidOperationException>(() => GossipCommandInput.SubmitAccept(layout,
                    () => { if (++validations == 2) throw new InvalidOperationException("dialog changed"); }, Tap, value => shift = value, _ => { }, CancellationToken.None),
                    "changed dialog command submitted");
                Check(enters == 1 && escapes == 1 && !shift, "changed dialog cancels typed command");
                enters = escapes = 0; using var cancel = new CancellationTokenSource(); int delays = 0;
                Reject<OperationCanceledException>(() => GossipCommandInput.SubmitAccept(layout, () => { }, Tap, value => shift = value,
                    _ => { if (++delays == 5) cancel.Cancel(); }, cancel.Token), "cancel ignored");
                Check(enters == 1 && escapes == 1 && !shift, "cancel releases shift and does not submit");
            }
            int calls = 0; QuestFrame.ArmedCompletion = () => calls++;
            QuestFrame.Instance.CompleteQuest();
            Reject<InvalidOperationException>(() => QuestFrame.Instance.CompleteQuest(), "unarmed completion executed");
            Check(calls == 1, "original CompleteQuest API consumes one-shot arm");
            QuestFrame.ArmedCompletion = () => throw new IOException("input failed");
            Reject<IOException>(() => QuestFrame.Instance.CompleteQuest(), "transport error swallowed");
            Check(QuestFrame.ArmedCompletion is null, "failed transport cannot reuse arm");
            Console.WriteLine($"QUEST REWARD STEP 10 SELF TEST: PASS ({checks} checks; reward decoding, correlated state, actual TurnIn advancement, input guards and cancellation; no live input)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"QUEST REWARD STEP 10 SELF TEST: FAIL after {checks} checks - {ex}"); return 1; }
        finally { QuestFrame.ArmedCompletion = null; ProfileManager.LoadEmpty(); Directory.Delete(folder, true); }
    }
}
