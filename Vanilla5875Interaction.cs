using Honorbuddy5875.Movement;
using Styx;
using Styx.WoWInternals.WoWObjects;

namespace Honorbuddy5875.Runtime;

// A normal-input backend. It does not move the cursor or invoke code in WoW.
// The diagnostic arms exactly one click after validating the live quest and NPC.
internal static class Vanilla5875Interaction
{
    internal static Action<WoWObject>? ArmedClick { get; set; }
    internal static void Interact(WoWObject target) => (ArmedClick ??
        throw new InvalidOperationException("NPC interaction input has not been armed."))(target);

    internal static void ClickHovered(WoWObject target, ulong expectedGuid, uint expectedEntry,
        Vanilla5875QuestDialog dialog, UInputKeyboard mouse)
    {
        var me = StyxWoW.Me;
        if (me is null || !me.IsAlive || me.Combat || target is not WoWUnit unit ||
            !unit.IsValid || !unit.IsAlive || unit.Combat || unit.IsPlayerControlled)
            throw new InvalidOperationException("Player or NPC no longer permits interaction.");
        double distance = unit.Distance;
        if (!double.IsFinite(distance) || distance < 0 || distance >= unit.InteractRange ||
            unit.Guid != expectedGuid || unit.Entry != expectedEntry || expectedGuid == 0)
            throw new InvalidOperationException("NPC identity or interaction range changed.");
        if (!dialog.Read().Closed) throw new InvalidOperationException("Close the existing NPC dialog before executing.");
        if (dialog.ReadMouseover() != expectedGuid)
            throw new InvalidOperationException("Mouse pointer is not over the expected NPC; no click sent.");
        mouse.Tap(0x111, 60); // BTN_RIGHT. One click, never an automatic retry.
    }
}
