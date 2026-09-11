using Styx.Logic.Pathing;

namespace Styx.Logic.Profiles.Quest;

public abstract class ProfileNode
{
    protected ProfileNode(string elementName) => ElementName = elementName;
    public string ElementName { get; }
    public string? GoalText { get; init; }
}

public sealed class GrindToNode : ProfileNode
{
    public GrindToNode() : base("GrindTo") { }
}

public sealed class MoveToNode : ProfileNode
{
    public MoveToNode(WoWPoint destination) : base("MoveTo") => Destination = destination;
    public WoWPoint Destination { get; }
}

public sealed class PickUpNode : ProfileNode
{
    public PickUpNode(uint questId, uint giverId) : base("PickUp")
    {
        QuestId = questId;
        GiverId = giverId;
    }
    public uint QuestId { get; }
    public uint GiverId { get; }
}

public sealed class TurnInNode : ProfileNode
{
    public TurnInNode(uint questId, uint turnInId) : base("TurnIn")
    {
        QuestId = questId;
        TurnInId = turnInId;
    }
    public uint QuestId { get; }
    public uint TurnInId { get; }
}

public sealed class UseItemNode : ProfileNode
{
    public UseItemNode(uint itemId) : base("UseItem") => ItemId = itemId;
    public uint ItemId { get; }
}

public sealed class UnknownProfileNode : ProfileNode
{
    public UnknownProfileNode(string elementName) : base(elementName) { }
}

public sealed class OrderNodeCollection : List<ProfileNode>
{
    public OrderNodeCollection() { }
    public OrderNodeCollection(IEnumerable<ProfileNode> nodes) : base(nodes) { }
}
