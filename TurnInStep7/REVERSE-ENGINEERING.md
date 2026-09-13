# Step 7 — evidence and limits

Reference inputs were the user's uploaded binaries:

- WoW.exe SHA-256: `b4756d38ef207c02ed651f4952bd89a70b4857b73a33413339e1b285b28d2dc7`
- Honorbuddy.exe SHA-256: `87ff9ef92f02e89c1d060c0071581730dcb446aae597b6f0d97006f46e118f3a`

These executables are not included in this package. `client-anchors.json` preserves the small instruction sequences used to ground the implementation. Run `python3 TurnInStep7/verify_client.py /path/to/WoW.exe` to check them. Static evidence validates the file, not behavior in a running Wine client.

## Original public API

.NET metadata inspection of Honorbuddy.exe established these public methods:

| Type | Member | Method body RVA | Signature |
| --- | --- | --- | --- |
| Styx.WoWInternals.WoWObjects.WoWObject | Interact() | 0xAC078 | void, no arguments |
| Same | Interact(bool) | 0xAC0B0 | void, bool argument |
| Styx.Logic.Inventory.Frames.Quest.QuestFrame | CurrentShownQuestId getter | 0x17D9AC | uint |
| Same | ActiveQuests getter | 0x17DBA0 | List<uint> |
| Same | AvailableQuests getter | 0x17DB38 | List<uint> |

QuestFrame has a public Instance field. This step implements only Interact() and the listed QuestFrame getters/Instance. It does not reproduce Interact(bool), the original remote execution implementation, the complete Frame hierarchy, or quest action methods. Instance methods read through a validated session initialized by this diagnostic. The normal bot does not automatically arm interaction.

## Client 5875 globals

Addresses below are preferred virtual addresses (image base 0x400000). The reader relocates them using ObjectManager.ModuleBase, verifies the mapped executable's SHA-256 and three live code anchors before reading the globals, and checks the active process/module session on each read.

| Address | Meaning | Evidence |
| --- | --- | --- |
| 0xB4E2C8 | uint64 mouseover GUID | Lua unit resolver compares `mouseover` at 0x515BA5 then loads this GUID; string at 0x850CE0 |
| 0xBE0810 | uint64 quest conversation NPC GUID | Session setter at 0x500D79; CloseQuest path 0x501201 clears both halves |
| 0xBE0818 | uint32 quest conversation mode | Setter 0x500D68; AcceptQuest requires 1, CompleteQuest 2, GetQuestReward 3 |
| 0xBE081C | uint32 selected quest ID | Setter 0x500D6E; quest request/reward send paths use this ID |
| 0xBE0844 | uint32 request-pending flag | Setter clears it; request paths set it |
| 0xBDF900 | selected quest title, 64-byte bounded string | GetTitleText at 0x501A20; copying routines pass limit 0x40 |
| 0xBE0838 | active quest greeting count | GetNumActiveQuests and GetActiveTitle |
| 0xBDE690 | active quest greeting array | SelectActiveQuest; GetActiveTitle adds title offset +8 |
| 0xBE0834 | available quest greeting count | GetNumAvailableQuests |
| 0xBDFE60 | available quest greeting array | SelectAvailableQuest |
| 0xBC3F58 | uint64 gossip conversation NPC GUID | Setter 0x4E1FA0; zero path clears both halves and emits close event |
| 0xBB74C0 | gossip quest array | GetGossipActiveQuests helpers at 0x4E2580/0x4E25B0 |

Quest greeting arrays have capacity 32 and stride 0x4C: ID +0, level +4, title +8 (64 bytes), auxiliary flag +0x48. Mode 0 is the greeting list; 1 details, 2 progress/request-items, 3 reward offer. A reward offer is not evidence of reward acceptance.

Gossip quest arrays have capacity 32 and stride 0x20C: ID +0, level +4, status +8, title +12 (512 bytes). The client considers statuses 3 and 4 active. Enumeration ends at ID zero or the capacity. Available gossip entries do not satisfy this turn-in diagnostic.

## Snapshot and success semantics

The reader compares two complete copies of the quest and gossip buffers plus the gossip NPC GUID. It retries changing copies up to three times, then reports blocked. This reduces mixed reads but is not a transactional snapshot of the game thread. It validates counts, modes and string termination. Closed NPC GUIDs suppress stale selected IDs, titles and arrays; greeting mode suppresses stale selected quest IDs.

The execute probe requires a closed conversation before its one click. It verifies a completed active quest, the expected living NPC in modeled range, unchanged NPC GUID and player health, and mouseover equality immediately before the click. The click uses the original public WoWObject.Interact() entry point with an explicitly armed internal uinput backend. It waits for a conversation from that exact NPC containing either the selected quest ID, an active quest greeting entry or an active gossip quest entry. Conflicting simultaneous conversation sessions cannot pass. It leaves the TurnIn node intact.

Client conversation buffers are not the Lua frames' rendered IsVisible state. No IsVisible property is fabricated. The observation-only result cannot establish that the program clicked anything. Synthetic tests cannot establish live behavior.

## Input limits

No InteractUnit Lua function or INTERACTTARGET binding was found in this exact executable. The implementation does not assume either exists. The user must focus WoW and position the pointer over the NPC model. There is no foreground-window verification, automatic cursor positioning, line-of-sight test or guarantee against a pointer/focus change between validation and OS event handling. Only a live response with matching NPC/quest can produce an interaction PASS.

The mouse backend enables EV_REL X/Y for normal mouse classification and BTN_LEFT/BTN_RIGHT, but emits only one BTN_RIGHT press/release. It does not emit cursor movement, choose a quest, click Continue, select a reward or accept it. Failure/timeout does not trigger another click. Input disposal releases held buttons. Existing keyboard event bookkeeping now records a held key before SYN, and Tap releases in finally, so an intermediate write failure can still be cleaned up.
