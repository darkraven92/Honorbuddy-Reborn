using Bots.Quest;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;
using Styx.WoWInternals;

namespace Honorbuddy5875.Runtime;

internal static class QuestOrderProbe
{
    internal static int RunLive(string? path)
    {
        QuestBot? bot = null;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Usage: --quest-order-test QuestOrderStep5/CuttingTeeth.xml");
            ProfileManager.LoadNew(path, false);
            var profile = ProfileManager.CurrentProfile;
            if (profile.QuestOrder.Count != 2 || profile.QuestOrder[0] is not ObjectiveNode objective ||
                profile.QuestOrder[1] is not TurnInNode turn || objective.QuestId != turn.QuestId)
                throw new InvalidDataException("This probe expects an Objective followed by TurnIn for the same quest.");
            ObjectManager.Initialize5875();
            bot = new QuestBot { MovementExecutionEnabled = false, ClientTargetSyncEnabled = false,
                ResolveQuestGiverLocations = false }; // Preserve the step-5 selection-only probe.
            bot.Start();
            bot.Root.Start(null);
            bot.Root.Tick(null);
            int firstIndex = bot.CurrentProfileNodeIndex;
            var firstDecision = bot.CurrentDecision;
            bot.Root.Tick(null);
            bool pass = firstIndex == 1 && bot.CurrentProfileNodeIndex == 1 &&
                firstDecision.Kind == QuestDecisionKind.TurnInReady &&
                bot.CurrentDecision.Kind == QuestDecisionKind.TurnInReady &&
                bot.TransitionLog.Count(x => x.StartsWith("QuestOrder[")) == 1;
            Console.WriteLine($"Profile: {profile.Name}; PID: {ObjectManager.WoWProcess?.Id}");
            foreach (string transition in bot.TransitionLog) Console.WriteLine(transition);
            Console.WriteLine($"Current node index: {bot.CurrentProfileNodeIndex}");
            Console.WriteLine($"Decision: {bot.CurrentDecision.Kind}");
            Console.WriteLine(bot.CurrentDecision.Description);
            Console.WriteLine($"Node retained across second tick: {bot.CurrentProfileNodeIndex == firstIndex}");
            Console.WriteLine(pass ? "QUEST ORDER STEP 5 RESULT: PASS" :
                "QUEST ORDER STEP 5 RESULT: BLOCKED - see current decision above.");
            Console.WriteLine("Scope: Objective -> TurnIn selection. No movement, targeting, NPC interaction, or reward acceptance.");
            return pass ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QUEST ORDER STEP 5 RESULT: ERROR - {ex.Message}");
            return 1;
        }
        finally
        {
            bot?.Root.Stop(null);
            bot?.Stop();
            ObjectManager.Shutdown5875();
            ProfileManager.LoadEmpty();
        }
    }

    internal static int RunSelfTest()
    {
        int checks = 0;
        string temp = Path.Combine(Path.GetTempPath(), "hb-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            void Check(bool ok, string name)
            {
                if (!ok) throw new InvalidOperationException(name);
                checks++;
            }
            string objective = "<Objective QuestId=\"788\" Type=\"KillMob\" MobId=\"3098\" KillCount=\"10\" />";
            string turn = "<TurnIn QuestId=\"788\" TurnInId=\"3143\" />";
            void Profile(string order)
            {
                string path = Path.Combine(temp, "profile.xml");
                File.WriteAllText(path, $"<HBProfile><Name>Order Test</Name><QuestOrder>{order}</QuestOrder></HBProfile>");
                ProfileManager.LoadNew(path, false);
            }
            QuestOrderSnapshot State(int done, bool completed = false, bool failed = false) =>
                new(true, completed, failed, [3098, 0, 0, 0], [10, 0, 0, 0], [(ushort)done, 0, 0, 0]);
            QuestOrderSnapshot state = State(0);
            int reads = 0;
            var bot = new QuestBot(_ => { reads++; return state; }) { ResolveQuestGiverLocations = false };
            void Restart(string order)
            {
                Profile(order);
                bot.Start();
                bot.Root.Start(null);
            }
            void Tick() => bot.Root.Tick(null);

            Restart(objective + turn);
            Check(ProfileManager.CurrentProfile.QuestOrder[0] is ObjectiveNode { QuestId: 788, MobId: 3098, KillCount: 10 }, "parse objective");
            Tick();
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.ObjectiveInProgress, "0/10 stays objective");
            state = State(1); Tick();
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Description.Contains("1/10"), "1/10 stays objective");
            state = State(10, completed: true);
            int before = reads; Tick();
            Check(reads == before + 1, "one snapshot shared by adjacent nodes");
            Check(bot.CurrentProfileNodeIndex == 1 && bot.CurrentDecision.Kind == QuestDecisionKind.TurnInReady &&
                bot.CurrentDecision.Entry == 3143, "10/10 advances to turn-in");
            Tick();
            Check(bot.CurrentProfileNodeIndex == 1 && bot.TransitionLog.Count == 1, "turn-in is retained on repeated tick");
            state = QuestOrderSnapshot.Missing("Quest is absent"); Tick();
            Check(bot.CurrentProfileNodeIndex == 1 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked,
                "disappearance is not rewarded-history proof");

            state = State(10); Restart(objective + turn); Tick();
            Check(bot.CurrentProfileNodeIndex == 1 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked,
                "objective count alone does not authorize turn-in");
            state = State(10, true, true); Restart(objective + turn); Tick();
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "failed quest is not skipped");
            state = State(10, true) with { Error = "Metadata unavailable" }; Restart(objective + turn); Tick();
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "metadata unavailable");
            state = State(10, true) with { Ids = [123, 0, 0, 0] }; Restart(objective + turn); Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked && bot.CurrentProfileNodeIndex == 0, "wrong WDB mob");
            state = State(10, true) with { Required = [20, 0, 0, 0] }; Restart(objective + turn); Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "wrong WDB count");
            state = State(10, true) with { Ids = [3098, 3098, 0, 0], Required = [10, 10, 0, 0] }; Restart(objective + turn); Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "ambiguous matching slots");
            state = State(10, true); Restart("<Objective QuestId=\"788\" Type=\"CollectItem\" />" + turn); Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.UnsupportedProfileNode && bot.CurrentProfileNodeIndex == 0, "unsupported objective is not skipped");
            Restart("<If Condition=\"true\">" + objective + "</If>" + turn); Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.UnsupportedProfileNode && bot.CurrentProfileNodeIndex == 0, "unknown control node is not skipped");
            Restart(objective); Tick(); Tick();
            Check(bot.CurrentProfileNodeIndex == 1 && bot.CurrentDecision.Kind == QuestDecisionKind.ProfileComplete &&
                bot.TransitionLog.Count == 1, "end of order does not replay last node");
            Profile(objective + turn); state = State(0); Tick();
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.ObjectiveInProgress, "new profile resets cursor");
            Restart("<PickUp QuestId=\"788\" GiverId=\"3143\" />" + objective + turn); Tick();
            Check(bot.CurrentProfileNodeIndex == 1 && bot.CurrentDecision.Kind == QuestDecisionKind.ObjectiveInProgress, "active pickup skipped");
            Restart("<MoveTo X=\"1\" Y=\"2\" Z=\"3\" />"); Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.MoveToProfileLocation, "existing MoveTo decision preserved");
            foreach (string invalid in new[] {
                "<Objective Type=\"KillMob\" MobId=\"3098\" KillCount=\"10\" />",
                "<Objective QuestId=\"788\" Type=\"KillMob\" MobId=\"3098\" KillCount=\"-1\" />" })
            {
                bool rejected = false;
                try { Profile(invalid); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "invalid objective XML rejected");
            }
            bot.Root.Stop(null);
            bot.Stop();
            Console.WriteLine($"QUEST ORDER STEP 5 SELF TEST: PASS ({checks} checks; actual QuestBot root ticks with internal test snapshots)");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QUEST ORDER STEP 5 SELF TEST: FAIL after {checks} checks - {ex}");
            return 1;
        }
        finally
        {
            ProfileManager.LoadEmpty();
            Directory.Delete(temp, recursive: true);
        }
    }
}
