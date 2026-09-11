using Styx;
using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;

namespace Honorbuddy5875.Runtime;

internal static class QuestCacheProbe
{
    internal static int RunFile(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Usage: --quest-cache-file-test /path/to/questcache.wdb");
            var file = Vanilla5875QuestCacheReader.ReadFile(path);
            PrintHeader(file.Diagnostics);
            foreach (var entry in file.Entries.Values.OrderBy(e => e.Id))
                PrintQuest(new CachedQuest(entry));
            bool pass = file.Entries.Count > 0;
            Console.WriteLine(pass ? "QUEST CACHE STEP 4 FILE RESULT: PASS" :
                "QUEST CACHE STEP 4 FILE RESULT: BLOCKED - no records to verify.");
            Console.WriteLine("Scope: offline WDB decoding and Quest properties; no live quest-state validation.");
            return pass ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QUEST CACHE STEP 4 FILE RESULT: ERROR - {ex.Message}");
            return 1;
        }
    }

    internal static int RunLive()
    {
        try
        {
            ObjectManager.Initialize5875();
            var me = StyxWoW.Me ?? throw new InvalidOperationException("Local player is unavailable.");
            var diagnostics = Vanilla5875QuestCache.GetDiagnostics();
            PrintHeader(diagnostics);
            Console.WriteLine($"PID: {ObjectManager.WoWProcess?.Id}; player level: {me.Level}");
            Console.WriteLine("StyxWoW.Cache -> Cache -> InfoBlock -> QuestCacheEntry -> PlayerQuest");
            var quests = me.QuestLog.GetAllQuests();
            bool pass = diagnostics.IsValid5875QuestCache && quests.Count > 0 &&
                quests.Count == me.QuestLog.QuestCount;
            foreach (var quest in quests)
            {
                var block = StyxWoW.Cache[CacheDb.Quest].GetInfoBlockById(quest.Id);
                WoWDescriptorQuest descriptor = default;
                bool descriptorRead = quest.GetData(ref descriptor);
                bool valid = block?.Id == quest.Id && block?.Quest.Id == quest.Id &&
                    block?.Quest.Data is not null && quest.InternalInfo.Data is not null &&
                    descriptorRead && descriptor.Id == quest.Id &&
                    descriptor.ObjectivesDone is { Length: 4 };
                pass &= valid;
                Console.WriteLine($"Quest {quest.Id}: metadata={quest.InternalInfo.Data is not null}, " +
                    $"completed={quest.IsCompleted}, failed={quest.IsFailed}, valid={valid}");
                if (quest.InternalInfo.Data is not null)
                {
                    PrintQuest(quest);
                    if (descriptorRead && descriptor.Id == quest.Id &&
                        descriptor.ObjectivesDone is { Length: 4 })
                        PrintProgress(quest, descriptor);
                    else Console.WriteLine("Live objective counters: unavailable or inconsistent.");
                }
                else Console.WriteLine("Metadata unavailable: live-identity fallback does not pass step 4.");
            }
            Console.WriteLine(pass ? "QUEST CACHE STEP 4 LIVE RESULT: PASS" :
                "QUEST CACHE STEP 4 LIVE RESULT: BLOCKED - missing metadata, no active quests, or inconsistent live state.");
            Console.WriteLine("Input: none. Memory writes: none. Completion comes from live descriptors.");
            return pass ? 0 : 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QUEST CACHE STEP 4 LIVE RESULT: ERROR - {ex.Message}");
            return 1;
        }
        finally { ObjectManager.Shutdown5875(); }
    }

    private static void PrintHeader(QuestCacheDiagnostics d)
    {
        Console.WriteLine($"Quest cache file: {d.Path}");
        Console.WriteLine($"WDB: {d.Signature}, build={d.Build}, locale={d.Locale}, version={d.RecordVersion}, records={d.RecordCount}");
        if (!d.IsValid5875QuestCache) Console.WriteLine($"WDB error: {d.Error}");
    }

    private static void PrintProgress(Quest quest, WoWDescriptorQuest descriptor)
    {
        Console.WriteLine($"Live ObjectivesDone: [{string.Join(",", descriptor.ObjectivesDone)}]");
        int[] ids = quest.NormalObjectiveIDs;
        int[] required = quest.NormalObjectiveRequiredCounts;
        bool hasNormalObjective = false;
        for (int i = 0; i < 4; i++)
        {
            if (ids[i] == 0 || required[i] == 0) continue;
            hasNormalObjective = true;
            uint rawId = unchecked((uint)ids[i]);
            string type = (rawId & 0x80000000u) != 0 ? "GameObject" : "Creature";
            uint entry = rawId & 0x7FFFFFFFu;
            Console.WriteLine($"Quest {quest.Id} objective[{i}]: type={type} entry={entry} " +
                $"progress={descriptor.ObjectivesDone[i]}/{required[i]}");
        }
        if (!hasNormalObjective)
            Console.WriteLine("No normal objective counters are defined in this quest's WDB record.");
        if (quest.CollectItemIDs.Any(id => id != 0))
            Console.WriteLine("Item collection progress: not measured by these normal-objective counters.");
        Console.WriteLine("Progress is a single live snapshot; PASS does not verify a counter change or finish the quest.");
        Console.WriteLine();
    }

    private static void PrintQuest(Quest quest)
    {
        var data = quest.InternalInfo.Data!;
        Console.WriteLine($"Quest.Id: {quest.Id}");
        Console.WriteLine($"Quest.Name: {quest.Name}");
        Console.WriteLine($"Quest.Level: {quest.Level}");
        Console.WriteLine($"Quest.NextQuestId: {quest.NextQuestId}");
        // These raw strings are decoded, but the exact HB SubDescription /
        // ObjectiveText / CompletionText getters need the original reference.
        Console.WriteLine($"WDB objective summary: {data.ObjectiveSummary}");
        Console.WriteLine($"Quest.Description: {quest.Description}");
        Console.WriteLine($"WDB end text: {data.EndText}");
        Console.WriteLine($"Quest.CollectItemIDs: [{string.Join(",", quest.CollectItemIDs)}]");
        Console.WriteLine($"Quest.CollectItemCounts: [{string.Join(",", quest.CollectItemCounts)}]");
        Console.WriteLine($"Quest.NormalObjectiveIDs: [{string.Join(",", quest.NormalObjectiveIDs)}]");
        Console.WriteLine($"Quest.NormalObjectiveRequiredCounts: [{string.Join(",", quest.NormalObjectiveRequiredCounts)}]");
        for (int i = 0; i < 4; i++)
            Console.WriteLine($"Quest.Objectives[{i}]: {quest.Objectives[i]}");
        Console.WriteLine($"Quest.RewardMoney: {quest.RewardMoney}; Quest.RewardMoneyAtMaxLevel: {quest.RewardMoneyAtMaxLevel}");
        Console.WriteLine($"Quest.RewardSpellId: {quest.RewardSpellId}");
        Console.WriteLine("RequiredLevel / RewardXp: unavailable from this WDB payload; not inferred.");
        Console.WriteLine();
    }

    private sealed class CachedQuest(QuestCacheEntry entry) : Quest(entry);
}
