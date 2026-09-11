using Styx.Logic.Pathing;

namespace Styx.Logic.AreaManagement;

public sealed class Hotspot
{
    public Hotspot(WoWPoint location, string? name = null)
    {
        Location = location;
        Name = string.IsNullOrWhiteSpace(name) ? location.ToString() : name;
    }

    public string Name { get; }
    public WoWPoint Location { get; }

    public static implicit operator WoWPoint(Hotspot hotspot) => hotspot.Location;
    public override string ToString() => $"{Name} {Location}";
}

public sealed class HotspotManager
{
    private readonly List<Hotspot> _hotspots;
    private int _index = -1;

    public HotspotManager(IEnumerable<Hotspot>? hotspots = null)
    {
        _hotspots = hotspots?.ToList() ?? new List<Hotspot>();
    }

    public List<Hotspot> Hotspots => _hotspots;
    public int CurrentIndex => _index;
    public WoWPoint LastHotSpot { get; private set; }

    public WoWPoint GetNextHotspot()
    {
        if (_hotspots.Count == 0)
            throw new InvalidOperationException("No hotspots have been defined in the current profile.");

        _index = (_index + 1) % _hotspots.Count;
        LastHotSpot = _hotspots[_index].Location;
        return LastHotSpot;
    }

    public WoWPoint GetNearestHotspot(WoWPoint from)
    {
        if (_hotspots.Count == 0)
            throw new InvalidOperationException("No hotspots have been defined in the current profile.");

        int nearestIndex = 0;
        double nearestDistance = double.MaxValue;
        for (int i = 0; i < _hotspots.Count; i++)
        {
            double distance = _hotspots[i].Location.DistanceSqr(from);
            if (distance >= nearestDistance)
                continue;
            nearestDistance = distance;
            nearestIndex = i;
        }

        _index = nearestIndex;
        LastHotSpot = _hotspots[_index].Location;
        return LastHotSpot;
    }

    public string CurrentName => _index >= 0 && _index < _hotspots.Count
        ? _hotspots[_index].Name
        : "none";
}

public sealed class GrindArea
{
    public string Name { get; set; } = "GrindArea";
    public double? LootRadius { get; set; }
    public bool RandomizeHotspots { get; set; }
    public double? MaxDistance { get; set; }
    public int? MaximumHotspotTime { get; set; }
    public List<int> Factions { get; set; } = new();
    public int TargetMinLevel { get; set; }
    public int TargetMaxLevel { get; set; } = int.MaxValue;
    public HotspotManager HotspotManager { get; set; } = new();
    public List<Hotspot> Hotspots => HotspotManager.Hotspots;
}
