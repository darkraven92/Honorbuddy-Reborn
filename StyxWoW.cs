using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Styx;

public class StyxWoW
{
    private StyxWoW() { }

    // Matches Honorbuddy's public forwarding properties.
    public static LocalPlayer? Me => ObjectManager.Me;
    public static bool IsInGame => ObjectManager.IsInGame;
}
