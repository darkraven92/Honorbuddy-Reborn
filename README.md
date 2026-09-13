# Honorbuddy Reborn — WoW 1.12.1 build 5875

Aktuellt arbete: [mesh-navigation, steg 12](NavigationStep12/README.md).
Navigator använder nu en `INavigationProvider` med VMaNGOS/Detour-mesh och
`IPlayerMover` för rörelse. Bygginstruktioner, offlineprov och liveprov finns där.

Questdialog och belöningsflöde dokumenteras i [steg 10](QuestRewardStep10/README.md).
[Orc-routing för 4641, 788 och 789](OrcRoutingStep12/README.md) har nu en profil och
tester av QuestOrder samt verkliga meshvägar. Full questautomation återstår.

## Tidigare CombatRoutine-prov

Phase 16.1 validates the first minimal clean-room `CombatRoutine` state machine against WoW 1.12.1 build 5875.

Flow:

- Honorbuddy-style profile/Targeting chooses a strict hostile candidate.
- The current WoW client target is adopted if valid; otherwise normal Tab input is used.
- `MinimalAutoAttackRoutine` derives from the reconstructed `CombatRoutine` base.
- States: `Approach -> StartAttack -> MaintainMelee -> TargetDead/Aborted`.
- Movement uses Navigator + `/dev/uinput`.
- Attack uses the normal WoW `Attack Target` action bound to `T`.
- No abilities/spells, Lua, memory writes, injection, or anti-cheat interaction.
- Safety: abort on target divergence/invalid target, player HP <= 50% of starting HP, excessive displacement, no damage progress, or 40s timeout.

Run preflight:

```bash
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj
```

Run live test:

```bash
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --combat-routine-test --mesh-directory /path/to/mmaps
```

The live test expects WoW's **Attack Target** binding on `T`.
