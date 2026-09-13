using Honorbuddy5875.Movement;
using Styx;
using Styx.Combat.CombatRoutine;
using Styx.Logic.Pathing;
using Styx.WoWInternals;
using Styx.WoWInternals.WoWObjects;

namespace Honorbuddy5875.Combat;

public enum MinimalCombatState
{
    Idle,
    Approach,
    StartAttack,
    MaintainMelee,
    TargetDead,
    Aborted
}

/// <summary>
/// Phase-16 clean-room combat routine for build 5875.
/// It uses only verified client targeting plus normal keyboard/movement input.
/// No Lua, memory writes, injected calls, or anti-cheat interaction.
/// </summary>
public sealed class MinimalAutoAttackRoutine : CombatRoutine
{
    private const double AttackStartDistance = 5.25;
    private const double ReapproachDistance = 5.75;
    private const double MaxDisplacement = 50.0;
    private const double MinimumHealthFraction = 0.50;

    private readonly ulong _targetGuid;
    private readonly UInputCombatActions _combatInput;
    private readonly bool _allowNeutralPotential;
    private readonly WoWPoint _startLocation;
    private readonly uint _startHealth;

    private bool _attackToggleStarted;
    private uint _lastTargetHealth;
    private DateTime _lastDamageAt;

    public MinimalAutoAttackRoutine(
        ulong targetGuid,
        UInputCombatActions combatInput,
        bool allowNeutralPotential = false)
    {
        _targetGuid = targetGuid;
        _combatInput = combatInput ?? throw new ArgumentNullException(nameof(combatInput));
        _allowNeutralPotential = allowNeutralPotential;

        LocalPlayer me = Styx.StyxWoW.Me
            ?? throw new InvalidOperationException("LocalPlayer unavailable while creating combat routine.");
        _startLocation = me.Location;
        _startHealth = me.CurrentHealth;
    }

    public override string Name => "5875 Minimal Auto-Attack";
    public override WoWClass Class => WoWClass.Warrior;

    public MinimalCombatState State { get; private set; } = MinimalCombatState.Idle;
    public string StopReason { get; private set; } = string.Empty;
    public bool DamageObserved { get; private set; }
    public bool TargetDied => State == MinimalCombatState.TargetDead;
    public bool AttackToggleStarted => _attackToggleStarted;
    public int AttackStartTaps { get; private set; }
    public int AttackStopTaps { get; private set; }
    public int MoveCalls { get; private set; }
    public uint LowestPlayerHealth { get; private set; } = uint.MaxValue;
    public uint LowestTargetHealth { get; private set; } = uint.MaxValue;
    public double ClosestDistance { get; private set; } = double.PositiveInfinity;
    public DateTime StartedAt { get; private set; }

    public override void Initialize()
    {
        ObjectManager.Update();
        LocalPlayer me = Styx.StyxWoW.Me
            ?? throw new InvalidOperationException("LocalPlayer unavailable while initializing combat routine.");
        WoWUnit? target = ObjectManager.GetObjectByGuid<WoWUnit>(_targetGuid);

        if (!ValidateTarget(me, target))
        {
            Abort("target failed initial combat-routine validation");
            return;
        }

        StartedAt = DateTime.UtcNow;
        _lastTargetHealth = target!.CurrentHealth;
        LowestPlayerHealth = me.CurrentHealth;
        LowestTargetHealth = target.CurrentHealth;
        ClosestDistance = target.Distance2D;
        _lastDamageAt = StartedAt;
        State = MinimalCombatState.Approach;
    }

    public override void Pulse()
    {
        if (State is MinimalCombatState.TargetDead or MinimalCombatState.Aborted)
            return;

        ObjectManager.Update();
        LocalPlayer me = Styx.StyxWoW.Me
            ?? throw new InvalidOperationException("LocalPlayer disappeared during combat routine.");
        WoWUnit? target = ObjectManager.GetObjectByGuid<WoWUnit>(_targetGuid);

        LowestPlayerHealth = Math.Min(LowestPlayerHealth, me.CurrentHealth);
        if (target is not null)
        {
            LowestTargetHealth = Math.Min(LowestTargetHealth, target.CurrentHealth);
            ClosestDistance = Math.Min(ClosestDistance, target.Distance2D);
        }

        // Death wins over client-target divergence: some clients may clear or
        // transition target state on the same pulse in which the unit dies.
        if (target is not null && target.IsValid && (!target.IsAlive || target.CurrentHealth == 0))
        {
            Navigator.Clear();
            State = MinimalCombatState.TargetDead;
            StopReason = "selected target died";
            return;
        }

        if (me.CurrentTargetGuid != _targetGuid)
        {
            Abort($"client target diverged to 0x{me.CurrentTargetGuid:X16}");
            return;
        }

        if (target is null || !target.IsValid)
        {
            Abort("selected target disappeared from ObjectManager");
            return;
        }

        if (!IsAllowedCombatCandidate(target))
        {
            Abort("selected target ceased to be an allowed combat candidate");
            return;
        }

        uint minimumHealth = (uint)Math.Ceiling(Math.Max(1.0, _startHealth * MinimumHealthFraction));
        if (me.CurrentHealth <= minimumHealth)
        {
            Abort($"player health safety threshold reached ({me.CurrentHealth}/{me.MaxHealth})");
            return;
        }

        if (_startLocation.Distance2D(me.Location) > MaxDisplacement)
        {
            Abort("maximum combat displacement exceeded");
            return;
        }

        if (target.CurrentHealth < _lastTargetHealth)
        {
            DamageObserved = true;
            _lastDamageAt = DateTime.UtcNow;
            _lastTargetHealth = target.CurrentHealth;
        }

        switch (State)
        {
            case MinimalCombatState.Approach:
                if (target.Distance2D <= AttackStartDistance)
                {
                    Navigator.Clear();
                    State = MinimalCombatState.StartAttack;
                }
                else
                {
                    if (Navigator.MoveTo(target.Location) is MoveResult.Failed or MoveResult.PathGenerationFailed)
                        Abort("Navigator could not approach the target");
                    MoveCalls++;
                }
                break;

            case MinimalCombatState.StartAttack:
                Navigator.Clear();
                _combatInput.ToggleAttackTarget();
                _attackToggleStarted = true;
                AttackStartTaps++;
                _lastDamageAt = DateTime.UtcNow;
                State = MinimalCombatState.MaintainMelee;
                break;

            case MinimalCombatState.MaintainMelee:
                if (target.Distance2D > ReapproachDistance)
                {
                    if (Navigator.MoveTo(target.Location) is MoveResult.Failed or MoveResult.PathGenerationFailed)
                        Abort("Navigator could not maintain melee range");
                    MoveCalls++;
                }
                else
                {
                    Navigator.Clear();
                }

                // A long no-damage window after attack initiation usually means the
                // Attack Target binding/focus/range state is not working as expected.
                if (!DamageObserved && DateTime.UtcNow - _lastDamageAt > TimeSpan.FromSeconds(6.0))
                    Abort("no target-health decrease within 6 seconds of Attack Target input");
                else if (DamageObserved && DateTime.UtcNow - _lastDamageAt > TimeSpan.FromSeconds(8.0))
                    Abort("no further target-health progress for 8 seconds");
                break;
        }
    }

    public void StopAttackIfNeeded()
    {
        Navigator.Clear();
        if (!_attackToggleStarted || State == MinimalCombatState.TargetDead)
            return;

        _combatInput.ToggleAttackTarget();
        _attackToggleStarted = false;
        AttackStopTaps++;
    }

    public override void ShutDown()
    {
        try { StopAttackIfNeeded(); } catch { }
        try { Navigator.Clear(); } catch { }
    }

    private bool ValidateTarget(LocalPlayer me, WoWUnit? target)
        => target is not null &&
           target.IsValid &&
           target.IsAlive &&
           me.CurrentTargetGuid == _targetGuid &&
           target.Guid == _targetGuid &&
           IsAllowedCombatCandidate(target);

    private bool IsAllowedCombatCandidate(WoWUnit target)
        => (target.MyReaction == WoWUnitReaction.Hostile &&
            target.IsStrictHostileCombatCandidate) ||
           (_allowNeutralPotential &&
            target.IsNeutralPotentialCombatCandidate);

    private void Abort(string reason)
    {
        StopReason = reason;
        State = MinimalCombatState.Aborted;
        try { StopAttackIfNeeded(); } catch { }
        try { Navigator.Clear(); } catch { }
    }
}
