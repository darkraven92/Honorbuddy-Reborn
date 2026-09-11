using Honorbuddy5875.Runtime;
using Styx.WoWInternals.WoWCache;

/// <summary>
/// Original Honorbuddy global QuestCacheEntry type.
/// Managed compatibility value, not a binary overlay of the original structure.
/// The build-specific payload remains internal.
/// </summary>
public struct QuestCacheEntry
{
    public uint Id;
    internal Vanilla5875QuestData? Data;

    public override readonly string ToString()
        => $"QuestCacheEntry(Id={Id})";
}

namespace Styx.Logic.Questing
{
    [Flags]
    public enum WoWDescriptorQuestFlags
    {
        None = 0,
        Completed = 0x01,
        Failed = 0x02
    }

    /// <summary>
    /// Descriptor-backed quest state shape recovered from Honorbuddy 2.0.0.5999.
    /// </summary>
    public struct WoWDescriptorQuest
    {
        public uint Id;
        public WoWDescriptorQuestFlags Flags;
        public ushort[] ObjectivesDone;
        public uint SecondsBeforeFailed;

        public override readonly string ToString()
            => $"WoWDescriptorQuest(Id={Id}, Flags={Flags}, Objectives=[{string.Join(", ", ObjectivesDone ?? Array.Empty<ushort>())}], SecondsBeforeFailed={SecondsBeforeFailed})";
    }

    /// <summary>
    /// Original Honorbuddy Quest base type. Its constructor receives the same
    /// QuestCacheEntry returned by StyxWoW.Cache[CacheDb.Quest].
    /// </summary>
    public class Quest
    {
        private QuestCacheEntry _internalInfo;

        protected Quest(QuestCacheEntry entry)
        {
            _internalInfo = entry;
        }

        public uint Id => _internalInfo.Id;

        public QuestCacheEntry InternalInfo
        {
            get
            {
                // Promote an identity-only wrapper after the client flushes its WDB.
                // Already populated wrappers are snapshots, like their cache value.
                if (_internalInfo.Data is null &&
                    Vanilla5875QuestCache.TryGetQuestEntry(_internalInfo.Id, out var latest) &&
                    latest.Data is not null)
                    _internalInfo = latest;
                return _internalInfo;
            }
            protected set => _internalInfo = value;
        }

        private Vanilla5875QuestData Data => InternalInfo.Data ??
            throw new InvalidOperationException(
                $"Quest {_internalInfo.Id} has live identity only; its build-5875 WDB metadata is unavailable. " +
                "Close WoW normally to flush the cache, then reopen WoW and retry.");

        public string Name => Data.Title;
        public string Description => Data.Details;
        public string[] Objectives => (string[])Data.ObjectiveTexts.Clone();
        public int Level => Data.Level;
        public uint NextQuestId => Data.NextQuestId;
        public int[] CollectItemIDs => (int[])Data.ItemIds.Clone();
        public int[] CollectItemCounts => (int[])Data.ItemCounts.Clone();
        // Preserve the client field's bits. GameObject IDs have bit 31 set;
        // consumers must not interpret such a value as a creature entry.
        public int[] NormalObjectiveIDs => (int[])Data.ObjectiveIds.Clone();
        public int[] NormalObjectiveRequiredCounts => (int[])Data.ObjectiveCounts.Clone();
        // The wire field is signed: a negative amount is a cost, not a reward.
        public uint RewardMoney => (uint)Math.Max(0, Data.RewardOrRequiredMoney);
        public uint RewardMoneyAtMaxLevel => Data.RewardMoneyAtMaxLevel;
        public int RewardSpellId => Data.RewardSpellId;

        public override string ToString() => $"Quest({Id})";
    }

    /// <summary>
    /// Original Honorbuddy live-player quest wrapper. The factory follows the
    /// original cache chain: StyxWoW.Cache[Quest] -> GetInfoBlockById -> InfoBlock.Quest.
    /// Live completion/failure state remains descriptor-backed.
    /// </summary>
    public class PlayerQuest : Quest
    {
        protected PlayerQuest(QuestCacheEntry entry) : base(entry) { }

        public bool IsCompleted
        {
            get
            {
                WoWDescriptorQuest data = default;
                return GetData(ref data) &&
                       (data.Flags & WoWDescriptorQuestFlags.Completed) != 0;
            }
        }

        public bool IsFailed
        {
            get
            {
                WoWDescriptorQuest data = default;
                return GetData(ref data) &&
                       (data.Flags & WoWDescriptorQuestFlags.Failed) != 0;
            }
        }

        public bool GetData(ref WoWDescriptorQuest data)
            => Vanilla5875QuestLogReader.TryReadDescriptorQuest(Id, out data);

        internal static PlayerQuest? FromId(uint questId)
        {
            if (questId == 0)
                return null;

            InfoBlock? info = Styx.StyxWoW.Cache[CacheDb.Quest].GetInfoBlockById(questId);
            if (info is null)
                return null;

            QuestCacheEntry entry = info.Quest;
            return entry.Id == 0 ? null : new PlayerQuest(entry);
        }
    }
}
