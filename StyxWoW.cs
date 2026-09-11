using Styx.WoWInternals;
using Styx.WoWInternals.WoWCache;
using Styx.WoWInternals.WoWObjects;

namespace Styx;

public class StyxWoW
{
    private static readonly WoWCache CacheInstance = new();

    private StyxWoW() { }

    // Matches Honorbuddy's public forwarding properties.
    public static WoWCache Cache => CacheInstance;
    public static LocalPlayer? Me => ObjectManager.Me;
    public static bool IsInGame => ObjectManager.IsInGame;
}
