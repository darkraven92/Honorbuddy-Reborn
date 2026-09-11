using Honorbuddy5875.Runtime;
using Styx.Logic.Pathing;

namespace Styx.WoWInternals.WoWObjects;

public delegate void ObjectInvalidateDelegate();

public class WoWObject
{
    private readonly ulong _guid;

    public event ObjectInvalidateDelegate? OnInvalidate;

    public WoWObject(uint baseAddress) : this(baseAddress, ReadGuid(baseAddress)) { }

    internal WoWObject(uint baseAddress, ulong guid)
    {
        BaseAddressFromManager = baseAddress;
        _guid = guid;
    }

    public uint BaseAddress => BaseAddressFromManager;
    internal uint BaseAddressFromManager { get; set; }
    public bool IsValid => BaseAddress != 0;
    public virtual uint Entry => ReadDescriptor<uint>(Vanilla5875.ObjectEntry);
    public virtual WoWObjectType Type => IsValid
        ? (WoWObjectType)Styx.WoWInternals.ObjectManager.Read<uint>((ulong)BaseAddress + Vanilla5875.ObjectTypeFromObject)
        : WoWObjectType.None;
    public virtual ulong Guid => _guid;

    public virtual float X => Location.X;
    public virtual float Y => Location.Y;
    public virtual float Z => Location.Z;
    public virtual float Rotation => ReadObject<float>(Vanilla5875.FacingFromObject);
    public virtual float RotationDegrees => Rotation * 180.0f / MathF.PI;

    public virtual WoWPoint Location => new(
        ReadObject<float>(Vanilla5875.PositionXFromObject),
        ReadObject<float>(Vanilla5875.PositionYFromObject),
        ReadObject<float>(Vanilla5875.PositionZFromObject));

    public virtual double Distance => Styx.StyxWoW.Me is null
        ? double.PositiveInfinity
        : Location.Distance(Styx.StyxWoW.Me.Location);
    public virtual double DistanceSqr => Styx.StyxWoW.Me is null
        ? double.PositiveInfinity
        : Location.DistanceSqr(Styx.StyxWoW.Me.Location);
    public virtual double Distance2D => Styx.StyxWoW.Me is null
        ? double.PositiveInfinity
        : Location.Distance2D(Styx.StyxWoW.Me.Location);
    public virtual double Distance2DSqr => Styx.StyxWoW.Me is null
        ? double.PositiveInfinity
        : Location.Distance2DSqr(Styx.StyxWoW.Me.Location);

    public virtual float InteractRange => 4.0f;
    public float InteractRangeSqr => InteractRange * InteractRange;
    public bool WithinInteractRange => DistanceSqr < InteractRangeSqr;

    protected T ReadObject<T>(uint byteOffset) where T : unmanaged
    {
        EnsureValid();
        return Styx.WoWInternals.ObjectManager.Read<T>((ulong)BaseAddress + byteOffset);
    }

    protected uint DescriptorAddress
    {
        get
        {
            EnsureValid();
            uint descriptor = Styx.WoWInternals.ObjectManager.ReadPointer32(
                (ulong)BaseAddress + Vanilla5875.DescriptorPointerFromObject);
            if (!Styx.WoWInternals.ObjectManager.IsPlausiblePointer(descriptor))
                throw new InvalidDataException(
                    $"Invalid descriptor pointer 0x{descriptor:X8} for object 0x{BaseAddress:X8}.");
            return descriptor;
        }
    }

    protected T ReadDescriptor<T>(uint byteOffset) where T : unmanaged
        => Styx.WoWInternals.ObjectManager.Read<T>((ulong)DescriptorAddress + byteOffset);

    internal void InvalidateFromManager()
    {
        if (BaseAddressFromManager == 0)
        {
            OnInvalidate?.Invoke();
            return;
        }
        BaseAddressFromManager = 0;
        OnInvalidate?.Invoke();
    }

    protected void EnsureValid()
    {
        if (!IsValid)
            throw new InvalidOperationException($"WoWObject 0x{Guid:X16} is no longer valid.");
    }

    private static ulong ReadGuid(uint baseAddress)
    {
        if (baseAddress == 0)
            return 0;
        return Styx.WoWInternals.ObjectManager.Read<ulong>((ulong)baseAddress + Vanilla5875.GuidFromObject);
    }

    public override bool Equals(object? obj) => obj is WoWObject other && this == other;
    public override int GetHashCode() => Guid.GetHashCode();

    public static bool operator ==(WoWObject? left, WoWObject? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null)
            return false;
        return left.Guid == right.Guid;
    }

    public static bool operator !=(WoWObject? left, WoWObject? right) => !(left == right);

    public override string ToString()
        => $"{GetType().Name}(Guid=0x{Guid:X16}, Base=0x{BaseAddress:X8}, Type={Type}, Entry={Entry})";
}

public class WoWUnit : WoWObject
{
    public WoWUnit(uint baseAddress) : base(baseAddress) { }
    internal WoWUnit(uint baseAddress, ulong guid) : base(baseAddress, guid) { }

    public virtual WoWUnit? CurrentTarget
        => Styx.WoWInternals.ObjectManager.GetObjectByGuid<WoWUnit>(CurrentTargetGuid);
    public virtual ulong CurrentTargetGuid => ReadDescriptor<ulong>(Vanilla5875.UnitTargetGuid);
    public virtual uint CurrentHealth => ReadDescriptor<uint>(Vanilla5875.UnitHealth);
    public virtual uint MaxHealth => ReadDescriptor<uint>(Vanilla5875.UnitMaxHealth);
    public virtual float HealthPercent => MaxHealth == 0 ? 0f : CurrentHealth * 100f / MaxHealth;
    public virtual int Level => checked((int)ReadDescriptor<uint>(Vanilla5875.UnitLevel));
    public virtual uint FactionId => ReadDescriptor<uint>(Vanilla5875.UnitFactionTemplate);
    public virtual uint Flags => ReadDescriptor<uint>(Vanilla5875.UnitFlags);
    public virtual uint DynamicFlags => ReadDescriptor<uint>(Vanilla5875.UnitDynamicFlags);
    public virtual uint NpcFlags => ReadDescriptor<uint>(Vanilla5875.UnitNpcFlags);

    private uint Bytes0 => ReadDescriptor<uint>(Vanilla5875.UnitBytes0);
    public virtual WoWRace Race => (WoWRace)(byte)Bytes0;
    public virtual WoWGender Gender => (WoWGender)(byte)(Bytes0 >> 16);
    public byte ClassId => (byte)(Bytes0 >> 8);

    public virtual WoWPowerType PowerType
    {
        get
        {
            byte raw = (byte)(Bytes0 >> 24);
            return raw <= 4 ? (WoWPowerType)raw : WoWPowerType.Unknown;
        }
    }

    public virtual uint CurrentPower => GetCurrentPower(PowerType);
    public virtual uint MaxPower => GetMaxPower(PowerType);
    public virtual float PowerPercent => GetPowerPercent(PowerType);
    public virtual bool Dead => CurrentHealth == 0;
    public virtual bool IsAlive => CurrentHealth > 0;
    public virtual bool Combat => (Flags & Vanilla5875.UnitFlagInCombat) != 0;
    public bool GotTarget => CurrentTargetGuid != 0;

    // Phase 11: read-only Vanilla faction/attackability compatibility.
    public virtual WoWUnitReaction MyReaction
    {
        get
        {
            LocalPlayer? me = Styx.StyxWoW.Me;
            if (me is null)
                return WoWUnitReaction.Neutral;
            if (Guid == me.Guid)
                return WoWUnitReaction.Friendly;
            return FactionTemplateStore5875.GetBaseReaction(me.FactionId, FactionId);
        }
    }

    public virtual bool IsPlayerControlled
        => (Flags & Vanilla5875.UnitFlagPlayerControlled) != 0;

    public virtual bool UnitFlagsPermitPlayerAttack
        => (Flags & Vanilla5875.UnitFlagBlocksPlayerAttack) == 0;

    public virtual bool IsStrictHostileCombatCandidate
        => Type == WoWObjectType.Unit &&
           Entry != 0 &&
           IsAlive &&
           !IsPlayerControlled &&
           UnitFlagsPermitPlayerAttack &&
           MyReaction < WoWUnitReaction.Neutral;

    public virtual bool IsNeutralPotentialCombatCandidate
        => Type == WoWObjectType.Unit &&
           Entry != 0 &&
           IsAlive &&
           !IsPlayerControlled &&
           UnitFlagsPermitPlayerAttack &&
           MyReaction == WoWUnitReaction.Neutral;

    public uint GetCurrentPower(WoWPowerType type)
    {
        if (type == WoWPowerType.Health)
            return CurrentHealth;
        int index = ClientPowerIndex(type);
        if (index < 0)
            return 0;
        uint value = ReadDescriptor<uint>(Vanilla5875.UnitPower1 + checked((uint)index * 4));
        return type == WoWPowerType.Rage ? value / 10 : value;
    }

    public uint GetMaxPower(WoWPowerType type)
    {
        if (type == WoWPowerType.Health)
            return MaxHealth;
        int index = ClientPowerIndex(type);
        if (index < 0)
            return 0;
        uint value = ReadDescriptor<uint>(Vanilla5875.UnitMaxPower1 + checked((uint)index * 4));
        return type == WoWPowerType.Rage ? value / 10 : value;
    }

    public float GetPowerPercent(WoWPowerType type)
    {
        uint max = GetMaxPower(type);
        return max == 0 ? 0f : GetCurrentPower(type) * 100f / max;
    }

    private static int ClientPowerIndex(WoWPowerType type) => type switch
    {
        WoWPowerType.Mana => 0,
        WoWPowerType.Rage => 1,
        WoWPowerType.Focus => 2,
        WoWPowerType.Energy => 3,
        WoWPowerType.Happiness => 4,
        _ => -1
    };
}

public class WoWPlayer : WoWUnit
{
    public WoWPlayer(uint baseAddress) : base(baseAddress) { }
    internal WoWPlayer(uint baseAddress, ulong guid) : base(baseAddress, guid) { }
}

public class LocalPlayer : WoWPlayer
{
    public LocalPlayer(uint baseAddress) : base(baseAddress) { }
    internal LocalPlayer(uint baseAddress, ulong guid) : base(baseAddress, guid) { }
}

public class WoWItem : WoWObject
{
    public WoWItem(uint baseAddress) : base(baseAddress) { }
    internal WoWItem(uint baseAddress, ulong guid) : base(baseAddress, guid) { }
}

public class WoWContainer : WoWObject
{
    public WoWContainer(uint baseAddress) : base(baseAddress) { }
    internal WoWContainer(uint baseAddress, ulong guid) : base(baseAddress, guid) { }
}

public class WoWGameObject : WoWObject
{
    public WoWGameObject(uint baseAddress) : base(baseAddress) { }
    internal WoWGameObject(uint baseAddress, ulong guid) : base(baseAddress, guid) { }
}

public class WoWDynamicObject : WoWObject
{
    public WoWDynamicObject(uint baseAddress) : base(baseAddress) { }
    internal WoWDynamicObject(uint baseAddress, ulong guid) : base(baseAddress, guid) { }
}

public class WoWCorpse : WoWObject
{
    public WoWCorpse(uint baseAddress) : base(baseAddress) { }
    internal WoWCorpse(uint baseAddress, ulong guid) : base(baseAddress, guid) { }
}
