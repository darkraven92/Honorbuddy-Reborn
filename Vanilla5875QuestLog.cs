using Styx.Logic.Questing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Honorbuddy5875.Runtime;

/// <summary>
/// Build-specific implementation hidden under Honorbuddy's public QuestLog API.
/// WoW 1.12.1 uses 20 slots of three DWORDs each:
/// +0 quest id, +4 four 6-bit counters plus state byte, +8 timer.
/// </summary>
internal static class Vanilla5875QuestLogReader
{
    public static uint ReadQuestId(int index)
        => ObjectManager.Read<uint>(GetSlotAddress(index));

    public static QuestLogEntry ReadQuestInfo(int index)
    {
        ulong slot = GetSlotAddress(index);
        uint id = ObjectManager.Read<uint>(slot);
        uint countState = ObjectManager.Read<uint>(slot + Vanilla5875.QuestLogCountStateOffset);
        uint time = ObjectManager.Read<uint>(slot + Vanilla5875.QuestLogTimeOffset);

        var counts = new short[4];
        uint packedCounts = countState & Vanilla5875.QuestLogObjectiveCountMask;
        for (int objective = 0; objective < counts.Length; objective++)
        {
            int shift = objective * Vanilla5875.QuestLogObjectiveCounterBits;
            counts[objective] = checked((short)(
                (packedCounts >> shift) & Vanilla5875.QuestLogObjectiveCounterMask));
        }

        return new QuestLogEntry
        {
            Id = checked((int)id),
            State = (StateFlag)(byte)(countState >> Vanilla5875.QuestLogStateShift),
            ObjectiveRequiredCounts = counts,
            Time = unchecked((int)time)
        };
    }

    private static ulong GetSlotAddress(int index)
    {
        if (index < 0 || index >= Vanilla5875.QuestLogSlotCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        LocalPlayer me = Styx.StyxWoW.Me
            ?? throw new InvalidOperationException("LocalPlayer is unavailable.");

        uint descriptor = ObjectManager.ReadPointer32(
            (ulong)me.BaseAddress + Vanilla5875.DescriptorPointerFromObject);

        if (!ObjectManager.IsPlausiblePointer(descriptor))
            throw new InvalidDataException(
                $"Invalid LocalPlayer descriptor pointer 0x{descriptor:X8}.");

        return (ulong)descriptor +
               Vanilla5875.PlayerQuestLog1 +
               checked((uint)index * Vanilla5875.QuestLogSlotStride);
    }
}
