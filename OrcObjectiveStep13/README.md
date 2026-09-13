# Orc objective combat - step 13

Step 13 connects an incomplete Honorbuddy `KillMob` QuestOrder objective to the
existing targeting and combat layers without changing their responsibility.

For quest 788 (`Cutting Teeth`), QuestBot keeps the objective cursor and the
authoritative descriptor count. `Targeting` still builds the profile-valid unit
set. QuestBot selects only entry `3098` from that set. Normal client Tab
targeting is constrained to the same entry before `MinimalAutoAttackRoutine`
can be started.

Target death is not quest completion. After a target dies, the bot waits briefly
for the live quest descriptor to increase. Only descriptor progress may advance
the QuestOrder node.

Objective combat is separately armed by `ObjectiveCombatExecutionEnabled`.
Existing routing and self-test commands therefore cannot start combat merely
because movement is enabled.

Regression commands:

    env MSBuildEnableWorkloadResolver=false dotnet build HonorbuddyReborn.csproj
    dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --quest-objective-combat-self-test
    dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --quest-routing-self-test /home/ludvig/Games/WoW-NavData/mmaps
    dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --quest-order-self-test
    dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --navigation-self-test /home/ludvig/Games/WoW-NavData/mmaps

The Step 13 self-test uses injected target snapshots and never opens `/dev/uinput`.
