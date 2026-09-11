using Honorbuddy5875.Runtime;

/// <summary>
/// Quest cache record type used by Honorbuddy's Quest/PlayerQuest hierarchy.
/// Step 2 restores the identity field required by the live player quest surface.
/// The remaining cache-backed fields are restored when the original quest cache is ported.
/// </summary>
public struct QuestCacheEntry
{
    public uint Id;
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
    /// Original Honorbuddy Quest base type. Step 2 restores the cache identity
    /// required by PlayerQuest; text/reward metadata remains a quest-cache milestone.
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
    /// Original Honorbuddy live-player quest wrapper. Completion/failure/data are
    /// resolved from the build-specific 5875 descriptor reader hidden below this API.
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

            return new PlayerQuest(new QuestCacheEntry { Id = questId });
        }
    }
}
