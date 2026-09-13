# Step 8 — API, client evidence and scope

Reference executables and hashes are unchanged from TurnInStep7/REVERSE-ENGINEERING.md. This step uses the same full executable SHA-256 check and live code anchors in Vanilla5875QuestDialog.

## Original Honorbuddy API

Inspection of the supplied Honorbuddy.exe confirms `Styx.Logic.Inventory.Frames.Gossip.GossipFrame.SelectActiveQuest(int)` at method body RVA `0x16E5CC`, signature `20 01 01 08` (instance method, one int32 parameter, void return). Its IL loads argument 1, adds constant 1, formats a string, and dispatches through the original action path. The supplied method therefore accepts a zero-based index. The frame has a public Instance field.

This implementation restores that public selector and Instance. It does not claim to implement the original entire Frame hierarchy, GossipActiveQuest wrappers, or generic Lua execution/return-value APIs. An internal one-call callback enables the diagnostic's input path; a normal unarmed call fails explicitly.

## Exact client 5875 selection behavior

| Location | Observation |
| --- | --- |
| 0x4E2AE0 | Lua SelectGossipActiveQuest entry |
| 0x4E2B14–0x4E2B17 | Converts numeric Lua argument to ECX, decrements it, calls 0x4E2600 |
| 0x4E2622 onward | Iterates gossip entries at 0xBB74C0 |
| 0x4E262C–0x4E263A | Counts only entries with status 3 or 4 |
| 0x4E263C | Compares active-entry index against requested index |
| 0x4E2660–0x4E267F | Uses the gossip NPC GUID at 0xBC3F58 and the selected record's quest ID for the request |

The request counts active entries, not every visible row and not available quests. There are at most 32 entries, stride 0x20C. The existing step-7 evidence covers the entry layout and session GUID.

`client-anchors.json` in this folder adds the relevant selection instructions to the previous file anchors. The standard-library-only verifier accepts a path to WoW.exe. Executables are not included.

## Native chat command and input

The source mirror [ChatFrame.lua](https://github.com/satan666/WOW-UI-SOURCE/blob/master/FrameXML/ChatFrame.lua) routes the SCRIPT slash command to RunScript; [GlobalStrings.lua](https://github.com/satan666/WOW-UI-SOURCE/blob/master/FrameXML/GlobalStrings.lua) defines /script. This repository identifies itself as an older 1.12-era UI source mirror; it is supplementary evidence, not a byte-identical validation of this user's installed FrameXML. The selector itself was checked in the user's exact 5875 executable.

The transport writes only a fixed command template with a bounded integer 1–32. It cannot be supplied arbitrary script text through this probe. No addon, macro installation, clipboard, chatlog return bridge, process-memory write or code injection is introduced. The client's own UI executes its native slash command after receiving ordinary key events.

Linux event codes use the system input-event-codes.h definitions. Swedish and US character mappings were checked against the installed XKB `se`, `latin(type2)` and `us` definitions. Enter must open chat, Caps Lock must be off, and the chosen layout must match the layout used by Wine. The code does not inspect foreground focus or the chat edit box, so emitted keystrokes are not themselves proof that the native command ran.

## Validation and transition limits

The selection plan requires exactly one record with the requested quest ID, an active status (3/4), the expected gossip NPC GUID, and no existing quest conversation. It copies the list. Before opening chat and immediately before submitting, it refreshes ObjectManager and QuestBot, requires the quest to remain completed/active and the NPC to remain alive/in range, checks player health, and compares the full gossip record sequence against the plan. Cancellation, list changes or typing errors cancel the partially typed command with Escape when possible. The input backend releases held keys on disposal. There is one submission attempt and no automatic retry.

The execute probe starts from the open gossip list. After submission it requires a stable selected quest conversation from the same NPC with the requested quest ID and mode 2 (progress) or 3 (reward offer). A same-NPC gossip session may remain populated during the transition; a different NPC is rejected. It checks the public QuestFrame ID and retains the completed quest's TurnIn node. Mode 1 (offer/details) does not pass this turn-in selection test. Previously selected quests are reported separately and do not count as a selection performed by this run.

The double memory reads and pre-submit check reduce races but cannot make OS input and game processing atomic. The probe does not assert rendered frame visibility or reward acceptance. Test logs distinguish local synthetic checks from the live test still required on the user's machine.
