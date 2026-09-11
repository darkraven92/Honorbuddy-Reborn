# Honorbuddy 5875 Phase 16.1

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
env MSBuildEnableWorkloadResolver=false dotnet run --project Honorbuddy5875Phase16.csproj
```

Run live test:

```bash
env MSBuildEnableWorkloadResolver=false dotnet run --project Honorbuddy5875Phase16.csproj -- --combat-routine-test
```

The live test expects WoW's **Attack Target** binding on `T`.
