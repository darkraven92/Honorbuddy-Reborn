# Vanilla Orc profile references

World-data source: [CMaNGOS ClassicDB 1.12.1 z2815](https://github.com/cmangos/classic-db/blob/master/Full_DB/ClassicDB_1_12_1_z2815.sql.gz).
Downloaded archive SHA-256:
`4f92db520868ab4e566726f68b5b2e380ae781209beaf22237b4f7f04600d0c0`.
The database was inspected as text; it was not imported into a running server.

Verified `creature_questrelation` pairs are `(10176,4641)`, `(3143,788)` and
`(3143,789)`. Verified `creature_involvedrelation` pairs are `(3143,4641)`,
`(3143,788)` and `(3143,789)`.

| Entry | Name | Reference spawn | World XYZ |
| --- | --- | --- | --- |
| 10176 | Kaltunk | 3442 | -607.434, -4251.33, 39.0393 |
| 3143 | Gornek | 3443 | -600.132, -4186.19, 41.2663 |
| 3098 | Mottled Boar | 12960 | -516.59, -4286.81, 40.0634 |
| 3124 | Scorpid Worker | 8552 | -680.309, -4145.69, 36.0694 |

All four spawns belong to map 1. The two mob spawns provide objective-area
destinations, not a claim that a mob remains exactly there; the source marks them
as roaming. Reference spawn IDs are not used as live object GUIDs. Live NPC
identity and coordinates always come from ObjectManager when available.

Entry 3144 is Eitrigg, not Kaltunk. The navigation diagnostic's NPC filter was
corrected from 3144 to 10176 in this step.

The local client's `WDB/questcache.wdb` was independently decoded as build 5875,
record version 3, enUS. It contains 4641, 788, 789 and 2383. Relevant metadata:

- 4641: Your Place In The World; next quest 788; no item/kill requirements.
- 788: Cutting Teeth; normal objective 3098 × 10; no item requirements.
- 789: Sting of the Scorpid; item 4862 × 10; no normal kill requirements.

The client WDB is not evidence of current quest acceptance, completion or rewards.
Those decisions continue to require live quest-log state. The reference database
is not proof that the user's private server uses identical spawns.

## Honorbuddy profile syntax

The supplied Honorbuddy reference package contains:

- `Bots/ProfessionBuddy/Profiles/Horde/Starters/Undead.xml`, lines 162–163:
  PickUp and TurnIn nodes with X/Y/Z attributes.
- `Default Profiles/Alliance NuokDrhawks685  1-60/Night Elf Start Area.xml`,
  lines 83 and 93: CollectItem objectives with ItemId and CollectCount.

These references establish syntax. Their expansion-specific quest IDs, NPC
positions and completion behavior were not copied into the Vanilla profile.
Existing reconstructed `ProfileNode` types were extended, keeping parsing in
Profile, progression in QuestBot/QuestOrder, and routing in Navigator.

## Reproduce reference verification

```bash
python3 OrcRoutingStep12/verify_reference.py /path/to/ClassicDB_1_12_1_z2815.sql.gz
```

The script validates the archive hash, template identities, quest/NPC relations
and the four selected reference spawns. The archive is not included in this repo.
