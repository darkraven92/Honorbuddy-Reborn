using TreeSharp;

namespace Styx.Combat.CombatRoutine;

public enum WoWClass : byte
{
    None = 0,
    Warrior = 1,
    Paladin = 2,
    Hunter = 3,
    Rogue = 4,
    Priest = 5,
    DeathKnight = 6,
    Shaman = 7,
    Mage = 8,
    Warlock = 9,
    Druid = 11
}

/// <summary>
/// Recovered legacy CombatRoutine base shape. These wrappers are host-side
/// behavior-tree glue and do not perform spell injection or Lua execution.
/// </summary>
public abstract class CombatRoutine : MarshalByRefObject, IDisposable
{
    public abstract string Name { get; }
    public abstract WoWClass Class { get; }
    public virtual double? PullDistance => null;

    public virtual bool NeedRest => false;
    public virtual void Rest() { }
    public virtual bool NeedPreCombatBuffs => false;
    public virtual void PreCombatBuff() { }
    public virtual bool NeedPullBuffs => false;
    public virtual void PullBuff() { }
    public virtual void Pull() { }
    public virtual bool NeedCombatBuffs => false;
    public virtual void CombatBuff() { }
    public virtual void Combat() { }
    public virtual bool NeedHeal => false;
    public virtual void Heal() { }
    public virtual void Initialize() { }
    public virtual void OnButtonPress() { }
    public virtual bool WantButton => false;
    public virtual string ButtonText => string.Empty;
    public virtual void Pulse() { }
    public virtual Composite? MoveToTargetBehavior => null;
    public virtual void ShutDown() { }

    public virtual Composite RestBehavior =>
        new Decorator(_ => NeedRest, new TreeSharp.Action((ActionSucceedDelegate)(_ => Rest())));

    public virtual Composite PreCombatBuffBehavior =>
        new Decorator(_ => NeedPreCombatBuffs,
            new TreeSharp.Action((ActionSucceedDelegate)(_ => PreCombatBuff())));

    public virtual Composite PullBuffBehavior =>
        new Decorator(_ => NeedPullBuffs,
            new TreeSharp.Action((ActionSucceedDelegate)(_ => PullBuff())));

    public virtual Composite PullBehavior =>
        new TreeSharp.Action((ActionSucceedDelegate)(_ => Pull()));

    public virtual Composite CombatBuffBehavior =>
        new Decorator(_ => NeedCombatBuffs,
            new TreeSharp.Action((ActionSucceedDelegate)(_ => CombatBuff())));

    public virtual Composite CombatBehavior =>
        new TreeSharp.Action((ActionSucceedDelegate)(_ => Combat()));

    public virtual Composite HealBehavior =>
        new Decorator(_ => NeedHeal, new TreeSharp.Action((ActionSucceedDelegate)(_ => Heal())));

    public void Dispose()
    {
        ShutDown();
        GC.SuppressFinalize(this);
    }

    public override string ToString() => $"{Class}: {Name}";
}

public static class RoutineManager
{
    public static CombatRoutine? Current { get; private set; }
    public static void SetCurrent(CombatRoutine routine)
        => Current = routine ?? throw new ArgumentNullException(nameof(routine));
}
