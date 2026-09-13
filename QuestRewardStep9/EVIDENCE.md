# Step 9 evidence and implementation scope

## User-observed state

The user's live metadata probe reported quest 2383 (Simple Parchment) active, completed, not failed, with valid metadata. Its WDB payload defines item 12635 x1 and no normal creature/gameobject objectives. The new XML therefore contains TurnIn alone; it does not fabricate a KillMob objective or infer item counts from ObjectivesDone.

NPC mapping was checked against the primary [CMaNGOS Classic-DB data file](https://github.com/cmangos/classic-db/blob/master/Full_DB/ClassicDB_1_12_1_z2815.sql.gz): creature_template entry 3153 is Frang, Warrior Trainer, and creature_involvedrelation contains (3153,2383). Only these identifiers are used in our independently written profile; the SQL database is not included. The running client's NPC is still resolved through ObjectManager and its exact GUID checked against the live conversation.

## Original Honorbuddy public API

Reference SHA-256 remains `87ff9ef92f02e89c1d060c0071581730dcb446aae597b6f0d97006f46e118f3a`.

Static metadata inspection and decoding of the original string resource established:

- QuestFrame.ClickContinue() at RVA 0x17D838 has an instance void/no-argument signature. The decoded command is `QuestFrameCompleteButton:Click()`.
- QuestFrame.CompleteQuest() at RVA 0x17D7F4 starts with native CompleteQuest() and also clicks QuestFrameCompleteQuestButton and QuestFrameCompleteButton repeatedly. It is broader than the continuation-only action required in this step.

The public method added here is ClickContinue(). QuestFrame.CompleteQuest() is deliberately not implemented by this patch. The native client function CompleteQuest() and the original Honorbuddy method with the same name must not be conflated.

The resource was decoded statically without loading/executing Honorbuddy.exe: resource {ce30cf31-4fc4-42b6-8557-99c45f582bae}, built-in DES-CBC resource key/IV fields 0x040012D0/0x040012D1, SmartAssembly chunked deflate, then the original string offset and base64 representation. The optional verify_honorbuddy_api.py script reproduces only the two relevant strings from the user's binary. It requires dnfile and OpenSSL with the legacy DES provider; it is an analysis helper, not a runtime dependency. No license/authentication code is modified or bypassed.

## Exact 5875 client evidence

Reference SHA-256 remains `b4756d38ef207c02ed651f4952bd89a70b4857b73a33413339e1b285b28d2dc7`.

| Client address | Evidence |
| --- | --- |
| 0x501D70 | Native Lua CompleteQuest entry calls 0x501560 |
| 0x501560 | Requires quest mode 2 and no request pending |
| 0x501591–0x50159F | Loads selected quest ID and quest NPC GUID, then calls 0x5EAD30 |
| 0x5015A4 | Marks the reward-offer request pending |
| 0x501D40 | Native IsQuestCompletable calls the current-requirements check at 0x501230; returns a truthy value or nil |
| 0x5015B0 | Separate reward-acceptance routine requires mode 3; the new flow never invokes GetQuestReward |

The new fixed command is `/script if IsQuestCompletable() then CompleteQuest() end`. The script is entered using the previously tested normal-input transport. The command's condition is evaluated by the client immediately before requesting the reward offer. There is no invented public inventory API or guessed collection progress. The original public ClickContinue member is adapted to this guarded native request for build 5875; the full original UI automation backend is not copied.

This folder's client-anchors.json and verify_client.py validate the exact file hash and 17 instruction anchors. The live reader retains the existing executable hash and three live-anchor validation before using the fixed globals. No executable is included in the deliverable.

## Flow and limits

QuestRewardDialogFlow is used by the live probe and synthetic transition tests. It can open the NPC, select the active gossip quest and continue from Progress to Reward, each at most once. Already-open reward offers and offers opened directly by the client are reported separately from a verified Continue transition. Wrong NPC/quest, offer/details mode, pending continuation requests and missing live quest state block progress.

The probe refreshes ObjectManager/QuestBot and real metadata before input, before submitting each typed command, while waiting, and at completion. It requires the quest to remain active/completed and the same living NPC in range; it retains the original profile node index (0 for the new single-node profile, 1 for the prior Objective/TurnIn form). Reward visibility never advances TurnIn. Quest absence never counts as successful reward acceptance.

There is no GetQuestReward template, reward-choice action or final reward-button click in the flow. The shared input transport gained only the fixed guarded continuation command; its cancellation and modifier cleanup behavior is unchanged. The step-6 approach probe's input validation was extended to allow a single TurnIn node. The old step-7/8 probe formats remain unchanged.

OS input and game-thread reads are not atomic, and foreground focus/text contents are not inspected. Rendered frame visibility is not fabricated. The locally run tests validate the implementation and static evidence; the next user run must validate the live continuation on this private server/Wine client.
