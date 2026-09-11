using Honorbuddy5875.Runtime;
using Styx.WoWInternals.WoWCache;

/// <summary>
/// Original Honorbuddy global QuestCacheEntry type.
/// Step 3 restores the cache identity used by Quest/PlayerQuest; remaining
/// cache-backed text/reward fields are restored after the 5875 cache record
/// payload is mapped.
/// </summary>
public struct QuestCacheEntry
{
    public uint Id;

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

        public uint Id => InternalInfo.Id;

        public QuestCacheEntry InternalInfo
        {
            get => _internalInfo;
            protected set => _internalInfo = value;
        }

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
