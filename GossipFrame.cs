namespace Styx.Logic.Inventory.Frames.Gossip;

// Verified original signature: SelectActiveQuest(int), with a zero-based active-list index.
// This subset does not yet implement GossipActiveQuest wrappers or frame visibility.
public sealed class GossipFrame
{
    public static readonly GossipFrame Instance = new();
    private GossipFrame() { }
    internal static Action<int>? ArmedSelection { get; set; }

    public void SelectActiveQuest(int index)
    {
        if (index is < 0 or >= 32) throw new ArgumentOutOfRangeException(nameof(index));
        var select = ArmedSelection ?? throw new InvalidOperationException("Gossip selection input has not been armed.");
        ArmedSelection = null; // One invocation per explicitly armed operation, including failure.
        select(index);
    }
}
