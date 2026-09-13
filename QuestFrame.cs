using Honorbuddy5875.Runtime;

namespace Styx.Logic.Inventory.Frames.Quest;

// Verified subset of the original Honorbuddy QuestFrame API.
// No IsVisible approximation: client conversation data and rendered frame visibility differ.
public sealed class QuestFrame
{
    public static readonly QuestFrame Instance = new();
    private QuestFrame() { }
    private static QuestDialogSnapshot Read() => (Vanilla5875QuestDialog.Current ??
        throw new InvalidOperationException("Quest dialog reader has not been initialized for this client session.")).Read();
    internal static Action? ArmedContinue { get; set; }
    public void ClickContinue()
    {
        var action = ArmedContinue ?? throw new InvalidOperationException("Quest continuation input has not been armed.");
        ArmedContinue = null;
        action();
    }

    internal static Action? ArmedCompletion { get; set; }
    public void CompleteQuest()
    {
        var action = ArmedCompletion ?? throw new InvalidOperationException("Reward acceptance input has not been armed.");
        ArmedCompletion = null;
        action();
    }

    public uint CurrentShownQuestId => Read().QuestId;
    public List<uint> ActiveQuests => Read().Active.Select(q => q.Id).ToList();
    public List<uint> AvailableQuests => Read().Available.Select(q => q.Id).ToList();
}
