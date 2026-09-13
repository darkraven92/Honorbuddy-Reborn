using System.Buffers.Binary;
using Bots.Quest;
using Honorbuddy5875.Movement;
using Styx;
using Styx.Logic.Inventory.Frames.Quest;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Honorbuddy5875.Runtime;

internal static class TurnInInteractionProbe
{
    internal static int Run(string? path, bool execute)
    {
        QuestBot? bot = null;
        using var cancel = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancel.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Usage: --turn-in-interaction-test QuestOrderStep5/CuttingTeeth.xml [--execute]");
            ProfileManager.LoadNew(path, false);
            var order = ProfileManager.CurrentProfile.QuestOrder;
            if (order.Count != 2 || order[0] is not ObjectiveNode objective || order[1] is not TurnInNode turn ||
                objective.QuestId != turn.QuestId)
                throw new InvalidDataException("This probe requires Objective -> TurnIn for one quest.");
            ObjectManager.Initialize5875();
            using var dialog = new Vanilla5875QuestDialog();
            bot = new QuestBot { MovementExecutionEnabled = false, ClientTargetSyncEnabled = false };
            bot.Start();
            bot.EvaluateNow();
            Console.WriteLine($"PID: {ObjectManager.WoWProcess?.Id}; node={bot.CurrentProfileNodeIndex}; decision={bot.CurrentDecision.Kind}");
            Console.WriteLine(bot.CurrentDecision.Description);
            var before = dialog.Read();
            Print(before);
            Console.WriteLine($"Mouseover GUID: 0x{dialog.ReadMouseover():X16}");
            var npc = RequireReady(bot);
            if (!execute)
            {
                Console.WriteLine(before.Matches(turn.QuestId, npc.Guid)
                    ? "TURN-IN STEP 7 OBSERVE RESULT: PASS - matching NPC conversation contains the expected active quest."
                    : "TURN-IN STEP 7 PLAN RESULT: READY - NPC in range; interaction has not been verified.");
                Console.WriteLine("Read-only. Client conversation data are not a rendered-frame visibility test. TurnIn retained.");
                return 0;
            }
            if (!before.Closed) throw new InvalidOperationException("Close all NPC dialogs, then run --execute again.");
            Console.WriteLine("One right click in 5 seconds. Focus WoW, close chat, and hold the mouse pointer over the NPC's model.");
            Console.WriteLine("Do not click or move the pointer until the result. Mouseover GUID must match; no cursor positioning or focus detection.");
            using var mouse = new UInputKeyboard(mouse: true);
            uint health = StyxWoW.Me!.CurrentHealth;
            for (int i = 5; i > 0; i--)
            {
                Console.WriteLine(i);
                if (cancel.Token.WaitHandle.WaitOne(1000)) throw new OperationCanceledException();
            }
            ObjectManager.Update();
            bot.EvaluateNow();
            var freshNpc = RequireReady(bot);
            if (freshNpc.Guid != npc.Guid || StyxWoW.Me!.CurrentHealth < health)
                throw new InvalidOperationException("NPC changed or player lost health during countdown.");
            var target = ObjectManager.GetObjectsOfType<WoWUnit>(false, false).SingleOrDefault(u => u.Guid == npc.Guid)
                ?? throw new InvalidOperationException("NPC disappeared.");
            bool clicked = false;
            Vanilla5875Interaction.ArmedClick = obj =>
            {
                Vanilla5875Interaction.ArmedClick = null; // Consume permission before attempting input.
                cancel.Token.ThrowIfCancellationRequested();
                Vanilla5875Interaction.ClickHovered(obj, npc.Guid, turn.TurnInId, dialog, mouse);
                clicked = true;
            };
            target.Interact();
            Console.WriteLine($"WoWObject.Interact(): right click sent={clicked}; awaiting client conversation (8 seconds).");
            long deadline = Environment.TickCount64 + 8000;
            while (Environment.TickCount64 < deadline)
            {
                cancel.Token.ThrowIfCancellationRequested();
                ObjectManager.Update();
                bot.EvaluateNow();
                var currentNpc = RequireReady(bot);
                if (currentNpc.Guid != npc.Guid || StyxWoW.Me!.CurrentHealth < health)
                    throw new InvalidOperationException("NPC changed or player lost health while awaiting dialog.");
                var state = dialog.Read();
                if (!state.Closed)
                {
                    // Wait through a packet/UI transition before judging the resulting dialog.
                    if (cancel.Token.WaitHandle.WaitOne(150)) throw new OperationCanceledException();
                    state = dialog.Read();
                    if (state.Closed) continue;
                    if (state.Matches(turn.QuestId, npc.Guid))
                    {
                        Print(state);
                        uint shown = QuestFrame.Instance.CurrentShownQuestId;
                        Console.WriteLine($"QuestFrame.Instance.CurrentShownQuestId: {shown}");
                        if (shown != state.QuestId)
                            throw new InvalidOperationException("Quest dialog changed during public API verification.");
                        bot.EvaluateNow();
                        RequireReady(bot);
                        if (bot.CurrentProfileNodeIndex != 1) throw new InvalidOperationException("TurnIn node changed unexpectedly.");
                        Console.WriteLine("TURN-IN STEP 7 INTERACTION RESULT: PASS - expected NPC and quest confirmed in live conversation data.");
                        Console.WriteLine("TurnIn node retained. No quest selection, CompleteQuest, reward choice, or reward acceptance was performed.");
                        return 0;
                    }
                    // Any other NPC is a hard mismatch; an incomplete list for our NPC may still be arriving.
                    if ((state.NpcGuid != 0 && state.NpcGuid != npc.Guid) ||
                        (state.GossipNpcGuid != 0 && state.GossipNpcGuid != npc.Guid))
                    { Print(state); throw new InvalidOperationException("A different NPC conversation opened."); }
                }
                if (cancel.Token.WaitHandle.WaitOne(100)) throw new OperationCanceledException();
            }
            Print(dialog.Read());
            Console.WriteLine("TURN-IN STEP 7 INTERACTION RESULT: BLOCKED - no matching dialog before timeout; no retry sent.");
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("TURN-IN STEP 7 RESULT: CANCELLED");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TURN-IN STEP 7 RESULT: BLOCKED - {ex.Message}");
            return 2;
        }
        finally
        {
            Vanilla5875Interaction.ArmedClick = null;
            Console.CancelKeyPress -= handler;
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
            throw new InvalidOperationException("Completed active quest and living NPC in range are required; use the step-6 approach first.");
        return npc;
    }
    private static void Print(QuestDialogSnapshot s)
    {
        Console.WriteLine($"Quest conversation: npc=0x{s.NpcGuid:X16} stage={(s.NpcGuid == 0 ? "Closed" : ((QuestDialogStage)s.RawStage).ToString())} " +
            $"quest={s.QuestId} title={s.Title} requestPending={s.RequestPending}");
        foreach (var q in s.Active) Console.WriteLine($"Quest greeting active: id={q.Id} level={q.Level} title={q.Title}");
        foreach (var q in s.Available) Console.WriteLine($"Quest greeting available: id={q.Id} level={q.Level} title={q.Title}");
        Console.WriteLine($"Gossip conversation: npc=0x{s.GossipNpcGuid:X16}");
        foreach (var q in s.GossipQuests)
            Console.WriteLine($"Gossip quest: id={q.Id} status={q.Status} level={q.Level} title={q.Title}");
    }
    internal static int RunSelfTest()
    {
        int checks = 0;
        try
        {
            var quest = new byte[Vanilla5875QuestDialog.QuestEnd - Vanilla5875QuestDialog.QuestStart];
            var gossip = new byte[Vanilla5875QuestDialog.GossipEnd - Vanilla5875QuestDialog.GossipStart];
            void U(uint address, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(
                quest.AsSpan((int)(address - Vanilla5875QuestDialog.QuestStart)), value);
            void Guid(ulong value) => BinaryPrimitives.WriteUInt64LittleEndian(
                quest.AsSpan((int)(0xBE0810 - Vanilla5875QuestDialog.QuestStart)), value);
            void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks++; }
            void Reject(Action action, string message)
            {
                try { action(); } catch (InvalidDataException) { checks++; return; }
                throw new InvalidOperationException(message);
            }
            QuestDialogSnapshot Read(ulong gossipNpc = 0) => Vanilla5875QuestDialog.Decode(quest, gossipNpc, gossip);
            U(0xBE0818, 3); U(0xBE081C, 788); U(0xBE0838, 999);
            var s = Read();
            Check(s.Closed && s.QuestId == 0 && s.Active.Length == 0, "closed GUID suppresses stale buffers");
            Check(!s.Matches(788, 11), "closed session never passes");
            Guid(11); U(0xBE0838, 0);
            "Cutting Teeth"u8.CopyTo(quest.AsSpan((int)(0xBDF900 - Vanilla5875QuestDialog.QuestStart)));
            foreach (uint mode in new uint[] { 1, 2, 3 })
            {
                U(0xBE0818, mode); s = Read();
                Check(s.Matches(788, 11) && s.Title == "Cutting Teeth", "selected quest identity per dialog stage");
            }
            Check(!s.Matches(788, 22), "wrong NPC rejected");
            Check(!s.Matches(789, 11), "wrong quest rejected");
            Check(!s.Matches(0, 0), "zero identities rejected");
            U(0xBE0818, 4); Reject(() => Read(), "unknown stage accepted");
            U(0xBE0818, 0); U(0xBE0838, 1); U(0xBDE690, 788); U(0xBDE694, 2);
            s = Read(); Check(s.QuestId == 0 && s.Matches(788, 11), "greeting uses active list, not stale selected ID");
            U(0xBE0838, 0); U(0xBE0834, 1); U(0xBDFE60, 788);
            Check(!Read().Matches(788, 11), "available quest is not an active turn-in");
            U(0xBE0838, 33); Reject(() => Read(), "oversized count accepted");
            U(0xBE0838, 1); U(0xBDE690, 0); Reject(() => Read(), "empty active entry accepted");
            U(0xBE0838, 0); U(0xBE0834, 0); U(0xBE0818, 2);
            quest.AsSpan((int)(0xBDF900 - Vanilla5875QuestDialog.QuestStart), 64).Fill(65);
            Reject(() => Read(), "unterminated title accepted");
            Guid(0);
            BinaryPrimitives.WriteUInt32LittleEndian(gossip.AsSpan(), 788);
            BinaryPrimitives.WriteUInt32LittleEndian(gossip.AsSpan(4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(gossip.AsSpan(8), 3);
            "Cutting Teeth"u8.CopyTo(gossip.AsSpan(12));
            s = Read(11); Check(s.Matches(788, 11) && s.GossipQuests[0].Title == "Cutting Teeth", "gossip active entry");
            BinaryPrimitives.WriteUInt32LittleEndian(gossip.AsSpan(8), 4);
            Check(Read(11).Matches(788, 11), "second active gossip status");
            BinaryPrimitives.WriteUInt32LittleEndian(gossip.AsSpan(8), 2);
            Check(!Read(11).Matches(788, 11), "available gossip entry cannot pass turn-in test");
            Check(Read().Closed && Read().GossipQuests.Length == 0, "closed gossip ignores stale list");
            BinaryPrimitives.WriteUInt32LittleEndian(gossip.AsSpan(8), 3);
            Guid(22); U(0xBE0818, 0);
            Check(!Read(11).Matches(788, 11), "overlapping conversation sessions rejected");
            Reject(() => Vanilla5875QuestDialog.Decode([], 0, gossip), "truncated memory accepted");
            Console.WriteLine($"TURN-IN STEP 7 SELF TEST: PASS ({checks} checks; synthetic conversation buffers, no live client/input)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"TURN-IN STEP 7 SELF TEST: FAIL after {checks} checks - {ex}");
            return 1;
        }
    }
}
