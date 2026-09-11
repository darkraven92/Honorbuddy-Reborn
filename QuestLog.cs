using Honorbuddy5875.Runtime;

namespace Styx.Logic.Questing;

[Flags]
public enum StateFlag : byte
{
    None = 0x00,
    Completed = 0x01,
    Failed = 0x02
}

/// <summary>
/// Quest-log record shape recovered from Honorbuddy 2.0.0.5999.
/// </summary>
public struct QuestLogEntry
{
    public int Id;
    public StateFlag State;
    public short[] ObjectiveRequiredCounts;
    public int Time;

    public override readonly string ToString()
        => $"QuestLogEntry(Id={Id}, State={State}, Objectives=[{string.Join(", ", ObjectiveRequiredCounts ?? Array.Empty<short>())}], Time={Time})";
}

/// <summary>
/// Honorbuddy-compatible quest-log surface exposed by LocalPlayer.QuestLog.
/// Build-specific 5875 descriptor access remains hidden in Vanilla5875QuestLogReader.
/// </summary>
public class QuestLog
{
    public QuestLog() { }

    public uint QuestCount
    {
        get
        {
            uint count = 0;
            for (uint index = 0; index < Vanilla5875.QuestLogSlotCount; index++)
            {
                if (GetQuestId(index) != 0)
                    count++;
            }
            return count;
        }
    }

    public int GetIndexForQuest(uint questId)
    {
        if (questId == 0)
            return -1;

        for (uint index = 0; index < Vanilla5875.QuestLogSlotCount; index++)
        {
            if (GetQuestId(index) == questId)
                return checked((int)index);
        }

        return -1;
    }

    /// <summary>
    /// Matches Honorbuddy: returns all active PlayerQuest wrappers from the live log.
    /// Original build 15595 iterated 25 slots; build 5875 has 20, which is the only
    /// client-version-specific difference here.
    /// </summary>
    public List<PlayerQuest> GetAllQuests()
    {
        var quests = new List<PlayerQuest>();
        for (uint index = 0; index < Vanilla5875.QuestLogSlotCount; index++)
        {
            PlayerQuest? quest = GetQuest(index);
            if (quest is not null)
                quests.Add(quest);
        }
        return quests;
    }

    /// <summary>
    /// Matches Honorbuddy's GetQuest(uint index): index is a quest-log slot.
    /// </summary>
    public PlayerQuest? GetQuest(uint index)
    {
        ValidateIndex(index);
        uint questId = GetQuestId(index);
        return questId == 0 ? null : PlayerQuest.FromId(questId);
    }

    public uint GetQuestId(uint index)
    {
        ValidateIndex(index);
        return Vanilla5875QuestLogReader.ReadQuestId(checked((int)index));
    }

    public bool ContainsQuest(uint questId)
        => questId != 0 && GetIndexForQuest(questId) >= 0;

    public PlayerQuest? GetQuestById(uint questID)
        => ContainsQuest(questID) ? PlayerQuest.FromId(questID) : null;

    public QuestLogEntry GetQuestInfo(int index)
    {
        ValidateIndex(index);
        return Vanilla5875QuestLogReader.ReadQuestInfo(index);
    }

    private static void ValidateIndex(uint index)
    {
        if (index >= Vanilla5875.QuestLogSlotCount)
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"Quest-log index must be between 0 and {Vanilla5875.QuestLogSlotCount - 1} for WoW 1.12.1.");
    }

    private static void ValidateIndex(int index)
    {
        if (index < 0 || index >= Vanilla5875.QuestLogSlotCount)
            throw new ArgumentOutOfRangeException(
                nameof(index),
                index,
                $"Quest-log index must be between 0 and {Vanilla5875.QuestLogSlotCount - 1} for WoW 1.12.1.");
    }
}
