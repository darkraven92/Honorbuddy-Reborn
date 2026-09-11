namespace Honorbuddy5875.Runtime;

internal static class Vanilla5875
{
    public const uint PreferredImageBase = 0x0040_0000;

    // 1.12.1.5875 client DBC store slots (preferred-image virtual addresses).
    // FactionTemplate.dbc class instance: 0x00C0DD34; records slot +0x08; max-id slot +0x0C.
    public const uint FactionTemplateRecordsPreferredVa = 0x00C0_DD3C;
    public const uint FactionTemplateCountPreferredVa = 0x00C0_DD40;
    public const uint ObjectManagerPointerRva = 0x0074_1414;
    public const uint FirstObjectFromObjectManager = 0xAC;
    public const uint LocalGuidFromObjectManager = 0xC0;

    public const uint DescriptorPointerFromObject = 0x08;
    public const uint ObjectTypeFromObject = 0x14;
    public const uint GuidFromObject = 0x30;
    public const uint NextObjectFromObject = 0x3C;

    public const uint PositionXFromObject = 0x9B8;
    public const uint PositionYFromObject = 0x9BC;
    public const uint PositionZFromObject = 0x9C0;
    public const uint FacingFromObject = 0x9C4;

    public const uint ObjectEntry = 0x0C;
    public const uint UnitTargetGuid = 0x40;
    public const uint UnitHealth = 0x58;
    public const uint UnitPower1 = 0x5C;
    public const uint UnitMaxHealth = 0x70;
    public const uint UnitMaxPower1 = 0x74;
    public const uint UnitLevel = 0x88;
    public const uint UnitFactionTemplate = 0x8C;
    public const uint UnitBytes0 = 0x90;
    public const uint UnitFlags = 0xB8;
    public const uint UnitDynamicFlags = 0x23C;
    public const uint UnitNpcFlags = 0x24C;

    public const uint UnitFlagNonAttackable = 0x0000_0002;
    public const uint UnitFlagPlayerControlled = 0x0000_0008;
    public const uint UnitFlagNotAttackable1 = 0x0000_0080;
    public const uint UnitFlagImmuneToPlayer = 0x0000_0100;
    public const uint UnitFlagNonAttackable2 = 0x0001_0000;
    public const uint UnitFlagInCombat = 0x0008_0000;
    public const uint UnitFlagNotSelectable = 0x0200_0000;

    public const uint UnitFlagBlocksPlayerAttack =
        UnitFlagNonAttackable |
        UnitFlagNotAttackable1 |
        UnitFlagImmuneToPlayer |
        UnitFlagNonAttackable2 |
        UnitFlagNotSelectable;
    public const uint MinimumUserAddress = 0x0001_0000;
    public const int MaximumObjectCount = 4096;
}

internal static class HonorbuddyOffsetIds
{
    public const int FirstObject = 0;
    public const int LocalGuid = 1;
    public const int GetPosition = 5102;
    public const int ObjectEntry = 5184;
    public const int DescriptorPointer = 5187;
    public const int ObjectType = 5188;
    public const int ObjectGuid = 5189;
    public const int NextObject = 5190;
    public const int TargetGuid = 5528;
    public const int Health = 5532;
    public const int Power1 = 5533;
    public const int MaxHealth = 5538;
    public const int MaxPower1 = 5539;
    public const int Level = 5546;
    public const int FactionTemplate = 5547;
    public const int UnitFlags = 5549;
    public const int DynamicFlags = 5568;
    public const int NpcFlags = 5572;
}
