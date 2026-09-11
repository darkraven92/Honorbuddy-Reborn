# Phase 16.1 port status

Phase 15B.2 validated one damaging `Attack Target` input while a profile-valid strict hostile client target remained synchronized.

Phase 16.1 moves that behavior into a reconstructed `CombatRoutine`-derived state machine:

`Approach -> StartAttack -> MaintainMelee -> TargetDead/Aborted`

This phase intentionally uses auto-attack only. Spell rotation, rage logic, pull abilities, healing, looting, Lua, memory writes and injected client calls are not part of this phase.
