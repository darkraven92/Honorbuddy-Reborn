using Bots.Quest;
using Honorbuddy5875.Navigation;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles;
using Styx.Logic.Profiles.Quest;

internal static class QuestRoutingSelfTest
{
    internal static int Run(string? meshDirectory)
    {
        int checks = 0;
        string directory = Path.Combine(Path.GetTempPath(), "hb-quest-routing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        INavigationProvider previous = Navigator.NavigationProvider;
        QuestBot? bot = null;
        void Check(bool pass, string reason) { checks++; if (!pass) throw new InvalidOperationException(reason); }
        try
        {
            var provider = new RecordingProvider();
            Navigator.NavigationProvider = provider;
            var state = QuestOrderSnapshot.Missing("Quest is not active");
            QuestGiverSnapshot? npc = null;
            bot = new QuestBot(_ => state) { ReadQuestGiver = (_, _) => npc };
            void Load(string nodes)
            {
                bot.Root.Stop(null);
                File.WriteAllText(Path.Combine(directory, "test.xml"), $"<HBProfile><QuestOrder>{nodes}</QuestOrder></HBProfile>");
                ProfileManager.LoadNew(Path.Combine(directory, "test.xml"), false);
                bot.Start(); bot.Root.Start(null); bot.Root.Tick(null);
            }
            void Tick() => bot.Root.Tick(null);
            const string pickup = "<PickUp QuestId=\"4641\" GiverId=\"10176\" X=\"10\" Y=\"20\" Z=\"3\" />";
            const string turn = "<TurnIn QuestId=\"4641\" TurnInId=\"3143\" X=\"30\" Y=\"40\" Z=\"4\" />";
            Load(pickup + turn);
            Check(ProfileManager.CurrentProfile.QuestOrder[0] is PickUpNode { Location: not null }, "pickup coordinates retained");
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.MoveToQuestGiver && bot.CurrentDecision.TargetGuid == 0 &&
                bot.CurrentDecision.Destination?.X == 10, "absent pickup NPC routes to profile coordinate");
            bot.ExecuteQuestGiverApproach();
            Check(provider.LastDestination?.X == 10 && bot.CurrentProfileNodeIndex == 0, "NPC search delegates movement without advancing pickup");
            provider.Result = MoveResult.ReachedDestination;
            bot.ExecuteQuestGiverApproach(); Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverUnavailable && bot.CurrentProfileNodeIndex == 0,
                "arrival without live NPC retains quest node and stops search");
            Tick(); Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverUnavailable, "missing NPC does not loop on reached coordinate");
            npc = new(11, 10176, new(12, 21, 3), 6, 6, 4, true, false, false);
            Tick();
            Check(bot.CurrentDecision.TargetGuid == 11 && bot.CurrentDecision.Destination?.X == 12, "loaded NPC supersedes static profile location");
            npc = npc with { Distance = 3, Distance2D = 3 };
            Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverInRange && bot.CurrentProfileNodeIndex == 0,
                "in-range pickup does not fabricate quest acceptance");
            state = new(true, false, false, [0,0,0,0], [0,0,0,0], [0,0,0,0]);
            Tick();
            Check(bot.CurrentProfileNodeIndex == 1 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked,
                "active pickup advances; incomplete turn-in remains blocked");
            state = state with { Completed = true }; npc = null;
            Tick();
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.MoveToQuestGiver && bot.CurrentDecision.Destination?.X == 30,
                "new NPC node has independent search destination");
            provider.Result = MoveResult.PathGenerationFailed;
            bot.ExecuteQuestGiverApproach();
            Check(bot.MovementAborted && bot.CurrentProfileNodeIndex == 1, "failed search path aborts without consuming turn-in");
            state = state with { Failed = true };
            Load(pickup + turn);
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "failed pickup does not approach or advance");

            state = QuestOrderSnapshot.Missing("Absent");
            Load("<MoveTo X=\"1\" Y=\"2\" Z=\"3\" /><MoveTo X=\"4\" Y=\"5\" Z=\"6\" />" + pickup);
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.MoveToProfileLocation, "first route waypoint selected");
            bot.HandleDestinationReached();
            Check(bot.CurrentProfileNodeIndex == 1 && !bot.MovementGoalReached, "first waypoint advances without stopping QuestOrder");
            bot.HandleDestinationReached();
            Check(bot.CurrentProfileNodeIndex == 1 && !bot.MovementGoalReached, "duplicate arrival is ignored");
            Tick(); Check(bot.CurrentDecision.Destination?.X == 4, "second waypoint selected by root");
            bot.HandleDestinationReached(); Tick();
            Check(bot.CurrentProfileNodeIndex == 2 && bot.CurrentDecision.Kind == QuestDecisionKind.MoveToQuestGiver,
                "route continues into pickup behavior");
            Load("<MoveTo X=\"1\" Y=\"2\" Z=\"3\" />"); bot.HandleDestinationReached();
            Check(bot.CurrentProfileNodeIndex == 1 && bot.MovementGoalReached && bot.CurrentDecision.Kind == QuestDecisionKind.ProfileComplete,
                "last route waypoint completes the profile");

            const string collection = "<Objective QuestId=\"789\" Type=\"CollectItem\" ItemId=\"4862\" CollectCount=\"10\" />";
            const string scorpidTurn = "<TurnIn QuestId=\"789\" TurnInId=\"3143\" />";
            state = new(true, false, false, [0,0,0,0], [0,0,0,0], [63,63,63,63])
                { ItemIds = [4862,0,0,0], ItemRequired = [10,0,0,0] };
            Load(collection + scorpidTurn);
            Check(ProfileManager.CurrentProfile.QuestOrder[0] is ObjectiveNode { ItemId: 4862, CollectCount: 10 }, "collection XML retained");
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.ObjectiveInProgress,
                "normal kill counters cannot complete item objective");
            state = state with { Completed = true }; Tick();
            Check(bot.CurrentProfileNodeIndex == 1 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestGiverUnavailable,
                "single collection quest completes from live flag and matching metadata");
            Tick(); Check(bot.CurrentProfileNodeIndex == 1, "collection advances once; missing turn-in NPC does not consume quest");
            state = state with { ItemIds = [123,0,0,0] }; Load(collection + scorpidTurn);
            Check(bot.CurrentProfileNodeIndex == 0 && bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "wrong WDB item rejected");
            state = state with { ItemIds = [4862,0,0,0], ItemRequired = [20,0,0,0] }; Load(collection + scorpidTurn);
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "wrong WDB item count rejected");
            state = state with { ItemIds = [4862,123,0,0], ItemRequired = [10,1,0,0] }; Load(collection + scorpidTurn);
            Check(bot.CurrentDecision.Kind == QuestDecisionKind.QuestStateBlocked, "multi-item objective needs actual inventory counts");
            foreach (string invalid in new[] {
                "<MoveTo X=\"NaN\" Y=\"2\" Z=\"3\" />", "<MoveTo />",
                "<PickUp QuestId=\"4641\" GiverId=\"10176\" X=\"1\" />",
                "<TurnIn QuestId=\"789\" TurnInId=\"3143\" X=\"1\" Y=\"2\" Z=\"Infinity\" />",
                "<Objective QuestId=\"789\" Type=\"CollectItem\" />",
                "<Objective QuestId=\"789\" Type=\"CollectItem\" ItemId=\"4862\" CollectCount=\"-1\" />" })
            {
                bool rejected = false;
                try { Load(invalid); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "invalid routing XML rejected");
            }

            string profilePath = Path.Combine(AppContext.BaseDirectory, "OrcRoutingStep12", "ValleyOfTrials.xml");
            var profile = Profile.FromFile(profilePath);
            Check(profile.ContinentId == 1 && profile.QuestOrder.Count == 10, "Orc profile map and sequence");
            Check(profile.QuestOrder.OfType<PickUpNode>().Select(p => p.QuestId).SequenceEqual(new uint[] {4641,788,789}), "Orc quest order");
            Check(profile.QuestOrder.OfType<PickUpNode>().First().GiverId == 10176, "Vanilla Kaltunk entry");
            if (meshDirectory is not null)
            {
                using var real = new VanillaMeshPathfinder(meshDirectory, profile.ContinentId);
                WoWPoint[] locations = profile.QuestOrder.Select(node => node switch {
                    PickUpNode p => p.Location, TurnInNode t => t.Location, MoveToNode m => (WoWPoint?)m.Destination, _ => null
                }).Where(p => p.HasValue).Select(p => p!.Value).ToArray();
                for (int i=1;i<locations.Length;i++)
                {
                    WoWPoint[] path = real.FindPath(locations[i-1], locations[i], 4096);
                    Check(path.Length > 0, $"Orc mesh leg {i}: {real.LastError}");
                    Console.WriteLine($"Orc mesh leg {i}: {locations[i-1]} -> {locations[i]}, {path.Length} points");
                }
            }
            Console.WriteLine($"QUEST ROUTING SELF TEST: PASS ({checks} checks; actual QuestBot root decisions and arrival executors; no live input)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"QUEST ROUTING SELF TEST: FAIL after {checks} checks: {ex}"); return 1; }
        finally
        {
            bot?.Root.Stop(null); bot?.Stop();
            Navigator.NavigationProvider = previous;
            ProfileManager.LoadEmpty(); Directory.Delete(directory, true);
        }
    }

    private sealed class RecordingProvider : INavigationProvider
    {
        public MoveResult Result = MoveResult.Moved;
        public WoWPoint? LastDestination;
        public MoveResult MoveTo(WoWPoint location) { LastDestination = location; return Result; }
        public float PathPrecision { get; set; } = 0.35f;
        public bool Clear() => true;
        public WoWPoint[] GeneratePath(WoWPoint from, WoWPoint to) => [];
        public bool CanNavigateFully(WoWPoint from, WoWPoint to, int maxHops) => false;
        public IStuckHandler StuckHandler { get; set; } = new StationaryHandler();
    }
    private sealed class StationaryHandler : IStuckHandler
    {
        public bool IsStuck() => false;
        public void Unstick() { }
        public void Reset() { }
    }
}
