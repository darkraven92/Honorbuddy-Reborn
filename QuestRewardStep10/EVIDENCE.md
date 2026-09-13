# Static evidence and implementation limits

All VAs below refer to the supplied 32-bit WoW executable at preferred image base 0x400000.
Runtime quest dialog reads require its exact SHA-256 plus existing live instruction anchors.

WoW.exe SHA-256: `b4756d38ef207c02ed651f4952bd89a70b4857b73a33413339e1b285b28d2dc7`.

## Reward offer

The offer is decoded from the same twice-read block as its NPC GUID, quest ID, mode and pending flag.
It is exposed internally only while the NPC GUID is nonzero and mode is Reward (3).

| Native implementation | Evidence | Used data |
|---|---|---|
| 0x5016A0 | Native fixed reward count scans IDs until first zero, maximum 6, stride 0x24 | ID 0xBDF020 |
| 0x5016C0 | Native choice count scans IDs until first zero, maximum 6, stride 0x24 | ID 0xBDF02C |
| 0x50174E | GetQuestItemInfo fixed branch loads ID and count | 0xBDF020 / 0xBDF024 |
| 0x50177C | GetQuestItemInfo choice branch loads ID and count | 0xBDF02C / 0xBDF030 |
| 0x501DD0 | GetRewardMoney | 0xBE082C |
| Existing reward-spell reader evidence | GetRewardSpell | 0xBE0830 |

## Input and original API

Native Lua GetQuestReward at 0x501D80 accepts an optional choice argument. Its core at 0x5015B0
requires mode 3 and no pending request. When choices exist, it validates a choice index; when none
exist, no argument is needed. It submits the current quest/NPC request and sets 0xBE0844 to 1.
The restricted transport additionally uses `GetNumQuestChoices()==0` in the submitted native command.
The Swedish equals key is Linux code 11 with Shift; US equals is code 13 without Shift, checked against
local XKB `se(basic)` -> `latin(type2)` and `us(basic)` definitions.

Original Honorbuddy.exe SHA-256:
`87ff9ef92f02e89c1d060c0071581730dcb446aae597b6f0d97006f46e118f3a`.
The original public `Styx.Logic.Inventory.Frames.Quest.QuestFrame.CompleteQuest()` exists at method
RVA 0x17D7F4. The step 9 resource verifier recovers its native Continue and repeated reward-button
click script. This port implements a **limited reward-stage-only backend** behind that original
method name: one explicitly armed native reward acceptance, no item choice, no repeated clicks.
It is not a claim of complete behavioral equivalence with the original method. `ClickContinue()`
remains the progress-to-reward entry point from step 9.

## Player XP and confirmation

CORRECTION: the original step 10 incorrectly treated player-relative offsets as full-descriptor offsets.
Native UnitXP at 0x5173C3 loads `[player+0xE68]` then reads +0x840; UnitXPMax at
0x517483 uses the same pointer then +0x844. Player setup at 0x5DD2A0 calls base setup,
adds 0x2F0 to the descriptor pointer (instruction 0x5DD2B5), then stores it at player+0xE68.
Therefore the correct offsets from `[player+8]` are 0xB30 and 0xB34. The read buffer must
reach 0xB38. The descriptor table indexes 0x210/0x211 are relative to PLAYER's field block.
The hotfix checks the live pointer relation before and after reading. Quest log and level offsets
remain based on the full descriptor. The regression fixture deliberately puts invalid XP values
at 0x840/0x844 and valid XP at 0xB30/0xB34. Earlier passing tests used the same wrong base as
the implementation and consequently did not detect this error.

Dispatch for opcode 0x191 reaches 0x5DC400. Inspection shows quest/XP/money values parsed into stack
locals, notifications and conversation cleanup. We did not establish a durable quest-specific
acknowledgement record suitable for passive polling. This implementation does not hook that handler
or claim to capture a server acknowledgement. It uses the documented bounded before/after criteria
instead. Quest absence alone never advances a TurnIn. A failed observation may leave an already
rewarded quest unconfirmed; no persistent history or automatic retry is fabricated.

## Reproduce the static check

```sh
python3 QuestRewardStep10/verify_client.py /path/to/WoW.exe
```

The standard-library verifier checks the SHA-256 and 32 instruction/data anchors. It verifies that
the supplied binary matches the recorded evidence; it is not a runtime test. The earlier original
API verifier remains at `QuestRewardStep9/verify_honorbuddy_api.py` and requires its documented
analysis dependencies. Neither executable is included in this archive.
