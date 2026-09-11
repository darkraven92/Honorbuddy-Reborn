using System.Globalization;
using System.Xml.Linq;
using Styx.Logic.AreaManagement;
using Styx.Logic.Pathing;
using Styx.Logic.Profiles.Quest;

namespace Styx.Logic.Profiles;

public readonly record struct Blackspot(WoWPoint Location, float Radius, float Height = 0f)
{
    public bool Contains(WoWPoint point)
    {
        if (Location.Distance2D(point) > Radius)
            return false;
        return Height <= 0f || MathF.Abs(point.Z - Location.Z) <= Height;
    }
}

public sealed class NewProfileLoadedEventArgs : EventArgs
{
    public NewProfileLoadedEventArgs(Profile newProfile, string? path)
    {
        NewProfile = newProfile;
        Path = path;
    }

    public Profile NewProfile { get; }
    public string? Path { get; }
}

public delegate void NewProfileLoadedDelegate(object? sender, NewProfileLoadedEventArgs e);

public sealed class Profile
{
    public string Name { get; internal set; } = "Empty Profile";
    public int ContinentId { get; internal set; } = -1;
    public int MinLevel { get; internal set; } = 1;
    public int MaxLevel { get; internal set; } = int.MaxValue;
    public int TargetMinLevel => GrindArea?.TargetMinLevel ?? MinLevel;
    public int TargetMaxLevel => GrindArea?.TargetMaxLevel ?? MaxLevel;
    public GrindArea? GrindArea { get; internal set; }
    public HotspotManager? HotspotManager => GrindArea?.HotspotManager;
    public HashSet<uint> Factions { get; } = new();
    public HashSet<uint> AvoidMobs { get; } = new();
    public List<Blackspot> Blackspots { get; } = new();
    public OrderNodeCollection QuestOrder { get; } = new();
    public string? SourcePath { get; internal set; }
    public XElement? XmlElement { get; internal set; }
    public bool IsEmpty { get; internal set; }

    internal static Profile Empty() => new() { IsEmpty = true };

    public static Profile FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("Unable to load new profile; file not found.", path);

        XDocument document = XDocument.Load(path, LoadOptions.SetLineInfo);
        XElement root = document.Root
            ?? throw new InvalidDataException("The profile XML does not contain a root element.");
        if (!root.Name.LocalName.Equals("HBProfile", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Expected <HBProfile>, found <{root.Name.LocalName}>.");

        var profile = new Profile
        {
            SourcePath = Path.GetFullPath(path),
            XmlElement = root,
            Name = ElementValue(root, "Name") ?? Path.GetFileNameWithoutExtension(path),
            ContinentId = ParseInt(ElementValue(root, "ContinentId"), -1),
            MinLevel = ParseInt(ElementValue(root, "MinLevel"), 1),
            MaxLevel = ParseInt(ElementValue(root, "MaxLevel"), int.MaxValue)
        };

        ParseUIntList(ElementValue(root, "Factions"), profile.Factions);
        ParseAvoidMobs(root, profile.AvoidMobs);
        ParseBlackspots(root, profile.Blackspots);
        profile.GrindArea = ParseGrindArea(root, profile);
        ParseQuestOrder(root, profile.QuestOrder);
        return profile;
    }

    private static GrindArea? ParseGrindArea(XElement root, Profile profile)
    {
        XElement? element = root.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("GrindArea", StringComparison.OrdinalIgnoreCase));
        if (element is null)
            return null;

        var area = new GrindArea
        {
            Name = Attr(element, "Name") ?? "GrindArea",
            TargetMinLevel = ParseInt(ElementValue(element, "TargetMinLevel"), profile.MinLevel),
            TargetMaxLevel = ParseInt(ElementValue(element, "TargetMaxLevel"), profile.MaxLevel),
            MaxDistance = ParseNullableDouble(ElementValue(element, "MaxDistance")),
            LootRadius = ParseNullableDouble(ElementValue(element, "LootRadius")),
            MaximumHotspotTime = ParseNullableInt(ElementValue(element, "MaximumHotspotTime")),
            RandomizeHotspots = ParseBool(ElementValue(element, "RandomizeHotspots"), false)
        };

        string? factionText = ElementValue(element, "Factions");
        foreach (uint faction in ParseUIntSequence(factionText))
            area.Factions.Add(checked((int)faction));
        if (area.Factions.Count == 0)
            area.Factions.AddRange(profile.Factions.Select(v => checked((int)v)));

        var hotspots = new List<Hotspot>();
        XElement? hotspotContainer = element.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("Hotspots", StringComparison.OrdinalIgnoreCase));
        IEnumerable<XElement> hotspotElements = hotspotContainer is null
            ? element.Elements().Where(e => e.Name.LocalName.Equals("Hotspot", StringComparison.OrdinalIgnoreCase))
            : hotspotContainer.Elements().Where(e => e.Name.LocalName.Equals("Hotspot", StringComparison.OrdinalIgnoreCase));

        foreach (XElement hotspot in hotspotElements)
        {
            if (TryParsePoint(hotspot, out WoWPoint point))
                hotspots.Add(new Hotspot(point, Attr(hotspot, "Name")));
        }
        area.HotspotManager = new HotspotManager(hotspots);
        return area;
    }

    private static void ParseQuestOrder(XElement root, OrderNodeCollection output)
    {
        XElement? order = root.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("QuestOrder", StringComparison.OrdinalIgnoreCase));
        if (order is null)
            return;

        foreach (XElement element in order.Elements())
        {
            string name = element.Name.LocalName;
            string? goalText = Attr(element, "GoalText");
            ProfileNode node = name switch
            {
                "GrindTo" => new GrindToNode { GoalText = goalText },
                "MoveTo" when TryParsePoint(element, out WoWPoint p)
                    => new MoveToNode(p) { GoalText = goalText },
                "PickUp" => new PickUpNode(
                    ParseUInt(Attr(element, "QuestId")),
                    ParseUInt(Attr(element, "GiverId") ?? Attr(element, "QuestGiverId"))) { GoalText = goalText },
                "TurnIn" => new TurnInNode(
                    ParseUInt(Attr(element, "QuestId")),
                    ParseUInt(Attr(element, "TurnInId") ?? Attr(element, "QuestGiverId"))) { GoalText = goalText },
                "UseItem" => new UseItemNode(
                    ParseUInt(Attr(element, "ItemId") ?? Attr(element, "Id"))) { GoalText = goalText },
                _ => new UnknownProfileNode(name) { GoalText = goalText }
            };
            output.Add(node);
        }
    }

    private static void ParseAvoidMobs(XElement root, HashSet<uint> output)
    {
        XElement? container = root.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("AvoidMobs", StringComparison.OrdinalIgnoreCase));
        if (container is null)
            return;
        foreach (XElement mob in container.Elements())
        {
            string? raw = Attr(mob, "Entry") ?? Attr(mob, "Id");
            if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value))
                output.Add(value);
        }
    }

    private static void ParseBlackspots(XElement root, List<Blackspot> output)
    {
        XElement? container = root.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("Blackspots", StringComparison.OrdinalIgnoreCase));
        if (container is null)
            return;
        foreach (XElement element in container.Elements())
        {
            if (!TryParsePoint(element, out WoWPoint point))
                continue;
            float radius = ParseFloat(Attr(element, "Radius"), 5f);
            float height = ParseFloat(Attr(element, "Height"), 0f);
            output.Add(new Blackspot(point, radius, height));
        }
    }

    private static string? ElementValue(XElement parent, string localName)
        => parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();

    private static string? Attr(XElement element, string localName)
        => element.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase))?.Value?.Trim();

    private static bool TryParsePoint(XElement element, out WoWPoint point)
    {
        bool okX = float.TryParse(Attr(element, "X"), NumberStyles.Float, CultureInfo.InvariantCulture, out float x);
        bool okY = float.TryParse(Attr(element, "Y"), NumberStyles.Float, CultureInfo.InvariantCulture, out float y);
        bool okZ = float.TryParse(Attr(element, "Z"), NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
        point = new WoWPoint(x, y, z);
        return okX && okY && okZ;
    }

    private static void ParseUIntList(string? text, HashSet<uint> output)
    {
        foreach (uint value in ParseUIntSequence(text))
            output.Add(value);
    }

    private static IEnumerable<uint> ParseUIntSequence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;
        foreach (string part in text.Split(new[] { ' ', '\t', '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (uint.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value))
                yield return value;
        }
    }

    private static int ParseInt(string? value, int fallback)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : fallback;
    private static int? ParseNullableInt(string? value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : null;
    private static uint ParseUInt(string? value)
        => uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint result) ? result : 0u;
    private static float ParseFloat(string? value, float fallback)
        => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : fallback;
    private static double? ParseNullableDouble(string? value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : null;
    private static bool ParseBool(string? value, bool fallback)
        => bool.TryParse(value, out bool result) ? result : fallback;
}

public static class ProfileManager
{
    private static Profile _current = Profile.Empty();

    public static event NewProfileLoadedDelegate? OnNewProfileLoaded;
    public static Profile CurrentProfile => _current;
    public static Profile CurrentOuterProfile => _current;
    public static string? CurrentProfilePath => _current.SourcePath;

    public static void LoadNew(string path) => LoadNew(path, true);

    public static void LoadNew(string path, bool rememberMe)
    {
        Profile profile = Profile.FromFile(path);
        _current = profile;
        OnNewProfileLoaded?.Invoke(null, new NewProfileLoadedEventArgs(profile, rememberMe ? profile.SourcePath : null));
    }

    public static void LoadEmpty()
    {
        _current = Profile.Empty();
        OnNewProfileLoaded?.Invoke(null, new NewProfileLoadedEventArgs(_current, null));
    }
}
