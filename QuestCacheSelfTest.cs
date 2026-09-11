using System.Buffers.Binary;
using System.Text;
using Styx;
using Styx.Logic.Questing;
using Styx.WoWInternals.WoWCache;

namespace Honorbuddy5875.Runtime;

// Dependency-free regression tests. Only a temporary directory is written.
internal static class QuestCacheSelfTest
{
    internal static int Run(string? fixturePath)
    {
        int checks = 0;
        void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            checks++;
        }
        void Reject(byte[] bytes, string label)
        {
            try { Vanilla5875QuestCacheReader.Parse(bytes); }
            catch (InvalidDataException) { checks++; return; }
            throw new InvalidOperationException($"Invalid input accepted: {label}");
        }
        try
        {
            byte[] synthetic = MakeSynthetic();
            var parsed = Vanilla5875QuestCacheReader.Parse(synthetic);
            var q = new TestQuest(parsed.Entries[900001]);
            var d = q.InternalInfo.Data!;
            Check(q.Id == 900001 && q.Name == "Å Test Quest", "UTF-8 name and identity");
            Check(q.Level == -1 && d.ZoneOrSort == -81, "signed level and sort");
            Check(q.Description == "Details $B$B$N", "raw WoW tokens preserved");
            Check(d.ObjectiveSummary == "Summary" && d.EndText == "End", "text order");
            Check(q.NextQuestId == 900002 && q.RewardSpellId == 456, "next quest and spell");
            Check(q.RewardMoney == 0 && d.RewardOrRequiredMoney == -123, "negative money is not a reward");
            Check(q.RewardMoneyAtMaxLevel == 789, "max-level money");
            Check(d.Method == 2 && d.Type == 7 && d.SourceItemId == 99 && d.Flags == 8, "numeric header");
            Check(d.ReputationFaction == 72 && d.ReputationValue == 3000 &&
                d.OppositeReputationFaction == 76 && d.OppositeReputationValue == -500, "reputation fields");
            Check(d.RewardItemIds.SequenceEqual(new[] { 100, 101, 102, 103 }) &&
                d.RewardItemCounts.SequenceEqual(new[] { 1, 2, 3, 4 }), "reward pair order");
            Check(d.ChoiceItemIds.SequenceEqual(new[] { 200, 201, 202, 203, 204, 205 }) &&
                d.ChoiceItemCounts.SequenceEqual(new[] { 5, 6, 7, 8, 9, 10 }), "choice pair order");
            Check(d.PointMapId == 1 && d.PointX == 0.25f && d.PointY == 0.75f && d.PointOption == 9, "map fields");
            Check(q.NormalObjectiveIDs.SequenceEqual(new[] { 123, unchecked((int)0x8000002Au), 789, 0 }), "GameObject bit preserved");
            Check(q.NormalObjectiveRequiredCounts.SequenceEqual(new[] { 10, 11, 12, 13 }), "objective counts");
            Check(q.CollectItemIDs.SequenceEqual(new[] { 500, 501, 502, 503 }) &&
                q.CollectItemCounts.SequenceEqual(new[] { 20, 21, 22, 23 }), "collection slots");
            Check(q.Objectives.SequenceEqual(new[] { "slot 0", "slot 1", "slot 2", "slot 3" }), "objective strings");
            q.CollectItemIDs[0] = 0;
            q.Objectives[0] = "mutated";
            Check(q.CollectItemIDs[0] == 500 && q.Objectives[0] == "slot 0", "public arrays do not mutate cache");

            byte[] BadAt(int offset, uint value)
            {
                byte[] b = (byte[])synthetic.Clone();
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset), value);
                return b;
            }
            Reject(BadAt(0, 0), "signature");
            Reject(BadAt(4, 15595), "wrong build");
            Reject(BadAt(16, 99), "unmapped version");
            Reject(BadAt(28, 2), "embedded identity mismatch");
            Reject(BadAt(24, uint.MaxValue), "oversized payload");
            Reject(BadAt(20, 0), "zero record key");
            Reject(synthetic.Concat(new byte[] { 1 }).ToArray(), "trailing file data");
            byte[] invalidUtf8 = (byte[])synthetic.Clone();
            invalidUtf8[184] = 0xFF;
            Reject(invalidUtf8, "invalid UTF-8");
            byte[] duplicate = synthetic[..^8].Concat(synthetic[20..]).ToArray();
            Reject(duplicate, "duplicate record");
            // Every truncated prefix must fail, including missing final string / terminator.
            for (int i = 0; i < synthetic.Length; i++) Reject(synthetic[..i], $"prefix {i}");
            byte[] payloadExtra = synthetic[..^8].Concat(new byte[] { 1 }).Concat(new byte[8]).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(payloadExtra.AsSpan(24),
                BinaryPrimitives.ReadUInt32LittleEndian(synthetic.AsSpan(24)) + 1);
            Reject(payloadExtra, "unconsumed payload");

            string temp = Path.Combine(Path.GetTempPath(), "hb-quest-test-" + Guid.NewGuid().ToString("N"));
            string? previousRoot = Environment.GetEnvironmentVariable("HB_WOW_ROOT");
            try
            {
                Directory.CreateDirectory(Path.Combine(temp, "WDB"));
                Environment.SetEnvironmentVariable("HB_WOW_ROOT", temp);
                var identity = new TestQuest(new QuestCacheEntry { Id = 900001 });
                Check(identity.Id == 900001, "identity remains usable without WDB");
                bool unavailable = false;
                try { _ = identity.Name; }
                catch (InvalidOperationException) { unavailable = true; }
                Check(unavailable, "missing metadata is explicit");
                string cachePath = Path.Combine(temp, "WDB", "questcache.wdb");
                File.WriteAllBytes(cachePath, synthetic);
                Check(identity.Name == "Å Test Quest", "identity wrapper promotes after WDB appears");
                var block = StyxWoW.Cache[CacheDb.Quest].GetInfoBlockById(900001);
                Check(block?.Id == 900001 && block.Quest.Data?.Title == "Å Test Quest", "full public cache chain");
                Check(StyxWoW.Cache[CacheDb.Quest].GetInfoBlockById(111111) is null, "absent ID not fabricated");
                File.WriteAllBytes(cachePath, new byte[] { 1 });
                Check(!Vanilla5875QuestCache.GetDiagnostics().IsValid5875QuestCache &&
                    !Vanilla5875QuestCache.ContainsPersistedRecord(900001), "bad replacement invalidates backend records");
            }
            finally
            {
                Environment.SetEnvironmentVariable("HB_WOW_ROOT", previousRoot);
                if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
            }

            if (fixturePath is not null)
            {
                var actual = Vanilla5875QuestCacheReader.ReadFile(fixturePath);
                Check(actual.Diagnostics.Build == 5875 && actual.Diagnostics.Locale == "enUS", "uploaded fixture header");
                var real = new TestQuest(actual.Entries[4641]);
                Check(real.Name == "Your Place In The World" && real.Level == 1 && real.NextQuestId == 788, "uploaded fixture metadata");
                Check(real.InternalInfo.Data!.ObjectiveSummary.StartsWith("Speak with Gornek.") &&
                    real.Description.StartsWith("Finally, you are of age, $N"), "uploaded fixture text order");
                Check(real.RewardMoney == 0 && real.RewardMoneyAtMaxLevel == 30 && real.RewardSpellId == 0, "uploaded fixture rewards");
                Check(real.CollectItemIDs.All(x => x == 0) && real.CollectItemCounts.All(x => x == 0) &&
                    real.NormalObjectiveIDs.All(x => x == 0) && real.NormalObjectiveRequiredCounts.All(x => x == 0) &&
                    real.Objectives.All(string.IsNullOrEmpty), "uploaded fixture has no item or kill counters");
            }
            Console.WriteLine($"QUEST CACHE STEP 4 SELF TEST: PASS ({checks} assertions; " +
                $"real quest-4641 fixture={(fixturePath is null ? "not supplied" : "verified")})");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"QUEST CACHE STEP 4 SELF TEST: FAIL after {checks} assertions - {ex}");
            return 1;
        }
    }

    private static byte[] MakeSynthetic()
    {
        using var payload = new MemoryStream();
        using (var w = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            // 15 fixed header words, 4 reward pairs, 6 choice pairs, 4 point words.
            uint[] words = { 900001, 2, uint.MaxValue, unchecked((uint)-81), 7, 72, 3000,
                76, unchecked((uint)-500), 900002, unchecked((uint)-123), 789, 456, 99, 8 };
            foreach (uint value in words) w.Write(value);
            for (int i = 0; i < 4; i++) { w.Write(100 + i); w.Write(1 + i); }
            for (int i = 0; i < 6; i++) { w.Write(200 + i); w.Write(5 + i); }
            w.Write(1u); w.Write(0.25f); w.Write(0.75f); w.Write(9u);
            void StringZ(string s) { w.Write(Encoding.UTF8.GetBytes(s)); w.Write((byte)0); }
            StringZ("Å Test Quest"); StringZ("Summary"); StringZ("Details $B$B$N"); StringZ("End");
            uint[] objectiveIds = { 123, 0x8000002A, 789, 0 };
            for (int i = 0; i < 4; i++)
            { w.Write(objectiveIds[i]); w.Write(10 + i); w.Write(500 + i); w.Write(20 + i); }
            for (int i = 0; i < 4; i++) StringZ($"slot {i}");
        }
        using var file = new MemoryStream();
        using (var w = new BinaryWriter(file, Encoding.ASCII, leaveOpen: true))
        {
            w.Write(Encoding.ASCII.GetBytes("TSQW")); w.Write(5875u);
            w.Write(Encoding.ASCII.GetBytes("SUne")); w.Write(6364u); w.Write(3u);
            w.Write(900001u); w.Write((uint)payload.Length); w.Write(payload.ToArray());
            w.Write(0u); w.Write(0u);
        }
        return file.ToArray();
    }

    private sealed class TestQuest(QuestCacheEntry entry) : Quest(entry);
}
