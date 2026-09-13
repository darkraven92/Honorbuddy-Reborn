using Bots.Quest;
using Honorbuddy5875.Movement;
using Styx.Logic.Profiles;

internal static class QuestObjectiveCombatSelfTest
{
    internal static int Run()
    {
        int checks = 0;

        string directory = Path.Combine(
            Path.GetTempPath(),
            "hb-objective-combat-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        QuestBot? bot = null;

        void Check(bool pass, string reason)
        {
            checks++;

            if (!pass)
                throw new InvalidOperationException(reason);
        }

        try
        {
            Check(
                ClientTargetSelector5875.EntryMatches(3098, 3098),
                "required entry accepts exact match");

            Check(
                !ClientTargetSelector5875.EntryMatches(3099, 3098),
                "required entry rejects another mob");

            Check(
                ClientTargetSelector5875.EntryMatches(3099, 0),
                "zero required entry preserves legacy targeting");

            var candidates = new[]
            {
                new ObjectiveTargetSnapshot(10, 3099, new(-1, 0, 0), 2, true, true),
                new ObjectiveTargetSnapshot(11, 3098, new(10, 0, 0), 10, true, true),
                new ObjectiveTargetSnapshot(12, 3098, new(5, 0, 0), 5, true, true),
                new ObjectiveTargetSnapshot(13, 3098, new(1, 0, 0), 1, false, true),
                new ObjectiveTargetSnapshot(14, 3098, new(1, 0, 0), 1, true, false)
            };

            ObjectiveTargetSnapshot? selected =
                QuestBot.SelectObjectiveTarget(3098, 0, candidates);

            Check(
                selected?.Guid == 12,
                "nearest alive strict-hostile matching objective entry selected");

            selected =
                QuestBot.SelectObjectiveTarget(3098, 11, candidates);

            Check(
                selected?.Guid == 11,
                "sticky objective target is retained while valid");

            Check(
                QuestBot.SelectObjectiveTarget(9999, 0, candidates) is null,
                "missing objective entry returns no target");

            QuestOrderSnapshot state = State(done: 0);
            ObjectiveTargetSnapshot? liveTarget = candidates[2];

            bot = new QuestBot(_ => state)
            {
                ResolveQuestGiverLocations = false,

                ReadObjectiveTarget = (entry, preferred) =>
                    liveTarget is null
                        ? null
                        : QuestBot.SelectObjectiveTarget(
                            entry,
                            preferred,
                            new[] { liveTarget })
            };

            const string objective =
                "<Objective QuestId=\"788\" Type=\"KillMob\" MobId=\"3098\" KillCount=\"10\" />";

            const string turn =
                "<TurnIn QuestId=\"788\" TurnInId=\"3143\" />";

            void Load(string nodes)
            {
                bot.Root.Stop(null);

                string path =
                    Path.Combine(directory, "profile.xml");

                File.WriteAllText(
                    path,
                    $"<HBProfile><Name>Objective Combat Test</Name>" +
                    $"<QuestOrder>{nodes}</QuestOrder></HBProfile>");

                ProfileManager.LoadNew(path, false);

                bot.Start();
                bot.Root.Start(null);
                bot.Root.Tick(null);
            }

            void Tick() => bot.Root.Tick(null);

            Load(objective + turn);

            Check(
                bot.CurrentProfileNodeIndex == 0,
                "incomplete kill objective retains QuestOrder node");

            Check(
                bot.CurrentDecision.Kind == QuestDecisionKind.ObjectiveInProgress,
                "kill objective produces ObjectiveInProgress");

            Check(
                bot.CurrentDecision.Entry == 3098,
                "objective decision carries mob entry");

            Check(
                bot.CurrentDecision.TargetGuid == 12,
                "objective decision carries selected target GUID");

            Check(
                bot.ObjectiveTargetGuid == 12,
                "QuestBot retains selected objective target");

            liveTarget =
                new ObjectiveTargetSnapshot(
                    20,
                    9999,
                    new(1, 1, 1),
                    1,
                    true,
                    true);

            Tick();

            Check(
                bot.CurrentDecision.TargetGuid == 0,
                "wrong-entry candidate is never adopted");

            Check(
                bot.CurrentProfileNodeIndex == 0,
                "missing valid target does not consume objective");

            liveTarget = candidates[2];
            state = State(done: 9);
            Tick();

            Check(
                bot.CurrentProfileNodeIndex == 0 &&
                bot.CurrentDecision.TargetGuid == 12,
                "9/10 retains objective and reacquires matching target");

            bot.MarkObjectiveTargetDeathObserved(
                788,
                3098,
                9);

            Tick();

            Check(
                bot.CurrentProfileNodeIndex == 0,
                "target death alone never advances QuestOrder");

            Check(
                bot.AwaitingObjectiveCredit,
                "target death enters quest-credit settle state");

            Check(
                bot.CurrentDecision.TargetGuid == 0,
                "no replacement target selected before quest credit settles");

            Check(
                bot.CurrentDecision.Description.Contains(
                    "awaiting authoritative quest credit",
                    StringComparison.OrdinalIgnoreCase),
                "status reports authoritative quest-credit wait");

            state = State(
                done: 10,
                completed: true);

            Tick();

            Check(
                !bot.AwaitingObjectiveCredit,
                "authoritative progress clears credit wait");

            Check(
                bot.CurrentProfileNodeIndex == 1,
                "10/10 advances objective exactly once");

            Check(
                bot.CurrentDecision.Kind == QuestDecisionKind.TurnInReady,
                "completed objective exposes turn-in");

            int transitions =
                bot.TransitionLog.Count(
                    line => line.StartsWith(
                        "QuestOrder[",
                        StringComparison.Ordinal));

            Tick();

            Check(
                bot.CurrentProfileNodeIndex == 1 &&
                bot.TransitionLog.Count(
                    line => line.StartsWith(
                        "QuestOrder[",
                        StringComparison.Ordinal)) == transitions,
                "repeated completed tick does not replay objective transition");

            state = State(done: 9);
            liveTarget = candidates[2];

            Load(objective + turn);

            bot.MarkObjectiveTargetDeathObserved(
                788,
                3098,
                9);

            Tick();

            Check(
                bot.CurrentProfileNodeIndex == 0 &&
                state.Done[0] == 9,
                "observed death cannot fabricate descriptor progress");

            const string collect =
                "<Objective QuestId=\"789\" Type=\"CollectItem\" " +
                "ItemId=\"4862\" CollectCount=\"10\" />";

            state = new QuestOrderSnapshot(
                true,
                false,
                false,
                [0,0,0,0],
                [0,0,0,0],
                [0,0,0,0])
            {
                ItemIds = [4862,0,0,0],
                ItemRequired = [10,0,0,0]
            };

            Load(collect);

            Check(
                bot.CurrentDecision.Kind == QuestDecisionKind.ObjectiveInProgress &&
                bot.CurrentDecision.TargetGuid == 0 &&
                bot.CurrentDecision.Entry == 0,
                "CollectItem remains outside KillMob target execution");

            Console.WriteLine(
                $"QUEST OBJECTIVE COMBAT STEP 13 SELF TEST: PASS " +
                $"({checks} checks; no live input)");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"QUEST OBJECTIVE COMBAT STEP 13 SELF TEST: FAIL " +
                $"after {checks} checks: {ex}");

            return 1;
        }
        finally
        {
            try { bot?.Root.Stop(null); } catch { }
            try { bot?.Stop(); } catch { }

            ProfileManager.LoadEmpty();

            Directory.Delete(
                directory,
                recursive: true);
        }
    }

    private static QuestOrderSnapshot State(
        int done,
        bool completed = false)
        => new(
            true,
            completed,
            false,
            [3098,0,0,0],
            [10,0,0,0],
            [(ushort)done,0,0,0]);
}
