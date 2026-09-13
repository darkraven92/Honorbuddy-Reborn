# Steg 8 — välj aktiv quest i gossip-listan

Utgångspunkt: steg 7, verifierat live med Gornek (3143), gossip-quest 788 / Cutting Teeth / status 4. Detta paket innehåller kompletta källfiler och tidigare testprofiler. Det kräver ingen separat makro- eller addoninstallation.

## Beteende

`GossipFrame.Instance.SelectActiveQuest(index)` använder originalets nollbaserade index bland aktiva quests. Den interna adaptern hittar quest-ID 788 i den minneslästa listan, räknar om indexet till WoW:s ettbaserade index och skriver exempelvis `/script SelectGossipActiveQuest(1)` via vanliga tangenttryckningar. Den öppnar chatten med Enter, skriver kommandot, kontrollerar NPC/quest/lista igen och skickar med Enter en gång.

Resultatet läses från klientminnet genom den redan implementerade dialogläsaren och `QuestFrame.Instance.CurrentShownQuestId`. Ingen chatlogg används. Testet stannar när rätt NPC:s progress- eller belöningsdialog visar rätt quest-ID. TurnIn-noden behålls. Att en belöningsdialog öppnas betyder inte att någon belöning har accepterats.

## Installera i fish

Spara arkivet i Downloads:

```fish
cd ~/Programming/Projects/Honorbuddy-Reborn

tar -czf "$HOME/Downloads/honorbuddy-reborn-before-step8-"(date +%Y%m%d-%H%M%S)".tar.gz" ./*.cs ./*.csproj

tar -xzf "$HOME/Downloads/honorbuddy-reborn-gossip-step8.tar.gz" -C .

env MSBuildEnableWorkloadResolver=false dotnet build HonorbuddyReborn.csproj

env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --gossip-select-self-test
```

## Liveprov

1. Behåll Cutting Teeth färdig 10/10 i questloggen. Stå nära Gornek, inom samma avstånd som efter steg 7.
2. Låt Gorneks gossip-lista vara öppen. Välj ännu inte Cutting Teeth. Om listan är stängd kan du öppna Gornek igen med det verifierade steg-7-testet eller högerklicka honom manuellt.
3. Läs först utan input:

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --gossip-select-test QuestOrderStep5/CuttingTeeth.xml
```

För den senast verifierade listan ska detta visa:

```text
Expected quest=788; NPC=0xF130000C47000D73; Honorbuddy active index=0; Lua active index=1
Command: /script SelectGossipActiveQuest(1)
GOSSIP STEP 8 PLAN RESULT: READY
```

GUID kan ändras mellan spelsessioner. Testet använder den levande NPC:n från ObjectManager, inte ett hårdkodat GUID.

4. Kör med den tangentbordslayout som är aktiv i WoW. För vanlig svensk layout:

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --gossip-select-test QuestOrderStep5/CuttingTeeth.xml --execute --keyboard-layout se
```

Använd `--keyboard-layout us` för vanlig amerikansk layout. Parametern är obligatorisk vid execute eftersom snedstreck och parenteser ligger på olika tangenter. Den byter inte din layout och detekterar inte Wine:s layout.

5. Under nedräkningen: fokusera WoW, behåll gossip-listan öppen, ha chatten stängd och Caps Lock av. Släpp tangenterna. Programmet förutsätter att Enter öppnar vanlig chatt. Muspekaren behöver inte ligga över Gornek. Skriv och klicka inte själv medan testet arbetar.

Förväntat slutresultat:

```text
GossipFrame.SelectActiveQuest(): command submitted once; awaiting quest conversation (8 seconds).
Quest conversation: ... stage=Progress ... quest=788 ...
QuestFrame.Instance.CurrentShownQuestId: 788
TurnIn node retained: True
GOSSIP STEP 8 SELECTION RESULT: PASS - expected NPC and quest ID confirmed in progress/reward conversation.
```

`stage=Reward` är också ett godkänt urvalsresultat. Testet trycker inte på Continue/Complete Quest och väljer eller accepterar inte någon belöning. Låt den öppnade dialogen vara kvar och klistra in hela terminalresultatet inför nästa steg.

## Om testet stoppar

- `ALREADY SELECTED`: questdialogen var redan öppen när körningen började. Inget input skickades; körningen verifierade inte själva urvalet.
- Gossip saknas: öppna Gorneks lista igen.
- Fel eller ändrad lista/NPC: kommandot skickas inte. Om en kommandorad har börjat skrivas försöker testet avbryta den med Escape.
- Timeout: inget nytt kommando skickas. Kontrollera aktiv layout, Caps Lock, Enter-bindningen och WoW-fokus. Om kommandot blev felskrivet, klistra in texten/felmeddelandet tillsammans med terminalresultatet.

Programmet kontrollerar inte vilket fönster eller textfält som har fokus, Caps Lock eller hur texten faktiskt blev i chatten. Normal input och oförändrad fokus/layout är därför förutsättningar för detta steg. Live-PASS kräver ändå rätt NPC-GUID och quest-ID i klientens dialogdata. De syntetiska testerna bevisar inte Wine/input-beteendet.
