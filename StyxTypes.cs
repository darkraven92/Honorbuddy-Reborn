namespace Styx;

public enum WoWObjectType : uint
{
    None = 0,
    Item = 1,
    Container = 2,
    Unit = 3,
    Player = 4,
    GameObject = 5,
    DynamicObject = 6,
    Corpse = 7,
    AiGroup = 8,
    AreaTrigger = 9
}

// Honorbuddy's enum numbering is important: Health is a special -2 value;
// the WoW client raw power selector still uses Mana=0, Rage=1, ...
public enum WoWPowerType : int
{
    Health = -2,
    Mana = 0,
    Rage = 1,
    Focus = 2,
    Energy = 3,
    Happiness = 4,
    Runes = 5,
    RunicPower = 6,
    SoulShards = 7,
    Eclipse = 8,
    HolyPower = 9,
    Unknown = 10
}

public enum WoWUnitReaction : int
{
    Hated = 0,
    Hostile = 1,
    Unfriendly = 2,
    Neutral = 3,
    Friendly = 4,
    Honored = 5,
    Revered = 6,
    Exalted = 7
}

public enum WoWGender : byte
{
    Male = 0,
    Female = 1,
    None = 2
}

public enum WoWRace : byte
{
    None = 0,
    Human = 1,
    Orc = 2,
    Dwarf = 3,
    NightElf = 4,
    Undead = 5,
    Tauren = 6,
    Gnome = 7,
    Troll = 8,
    Goblin = 9,
    BloodElf = 10,
    Draenei = 11
}
