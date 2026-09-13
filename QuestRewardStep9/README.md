# Steg 9 — Simple Parchment till belöningsdialog

Utgångspunkt: steg 8. Användarens liveprov har verifierat quest 2383 med metadata=True, completed=True, failed=False och valid=True. WDB-posten anger Frang som mottagare och Simple Parchment (item 12635, antal 1) som krav. Den nya profilen har enbart TurnIn för quest 2383 / NPC 3153.

Detta paket innehåller kompletta källfiler och alla tidigare profiler/testverktyg. Det nya testet kan börja med stängd NPC-dialog, öppen gossip-lista eller rätt quests progressdialog. Det återanvänder ett vanligt högerklick och gossip-valet vid behov, anropar sedan originalets publika QuestFrame.ClickContinue() via adaptern för build 5875, och stannar vid belöningsdialogen.

## Installera (fish)

Spara arkivet i Downloads:

```fish
cd ~/Programming/Projects/Honorbuddy-Reborn

tar -czf "$HOME/Downloads/honorbuddy-reborn-before-step9-"(date +%Y%m%d-%H%M%S)".tar.gz" ./*.cs ./*.csproj

tar -xzf "$HOME/Downloads/honorbuddy-reborn-rewarddialog-step9.tar.gz" -C .

env MSBuildEnableWorkloadResolver=false dotnet build HonorbuddyReborn.csproj

env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-reward-dialog-self-test
```

## Förbered i WoW

Behåll Simple Parchment färdig i questloggen och behåll själva föremålet. Gå till Frang utanför Den och ställ dig cirka 3 yards från honom. Låt hans dialog vara stängd inför det första fullständiga provet. Sting of the Scorpid (789) kan vara kvar i loggen; profilen gäller endast 2383.

Den befintliga approach-proben accepterar nu även en ensam TurnIn-nod. Om du vill använda den fungerande rörelsen på en kort, fri väg till Frang kan du köra:

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --turn-in-approach-test QuestRewardStep9/SimpleParchment.xml --execute
```

Samma gränser gäller som i steg 6: högst 25 yards från NPC:n vid start, fri kort väg på samma nivå, nedräkning och begränsad körtid. Använd den här nya profilen för Frang. Steg-7- och steg-8-probernas äldre indataformat är oförändrade; det nya steg-9-testet hanterar öppning och val för den ensamma TurnIn-noden.

## Läs planen utan input

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-reward-dialog-test QuestRewardStep9/SimpleParchment.xml
```

Förväntat: rätt profil, quest 2383, NPC entry 3153, TurnIn index 0, NPC inom räckvidd och `QUEST REWARD STEP 9 PLAN RESULT: READY`.

## Kör hela dialogsteget

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-reward-dialog-test QuestRewardStep9/SimpleParchment.xml --execute --keyboard-layout se
```

Använd `us` om amerikansk tangentbordslayout är aktiv i WoW. Parametern ändrar inte din layout.

Under de fem sekunderna: fokusera WoW, stäng chatten, slå av Caps Lock, släpp tangenterna och håll pekaren över Frangs modell om NPC-dialogen är stängd. Klicka eller skriv inte själv medan testet pågår.

Testet utför vid behov:

1. Ett högerklick via WoWObject.Interact(), bara när mouseover-GUID matchar den förväntade NPC:n.
2. Ett val av quest 2383 via GossipFrame.SelectActiveQuest(), med rätt aktiva listindex.
3. QuestFrame.ClickContinue(), som skriver det fasta kommandot `/script if IsQuestCompletable() then CompleteQuest() end` via vanliga tangenttryckningar.
4. Kontroll av belöningsdialogens NPC-GUID, quest-ID 2383, läge Reward och att questen fortfarande är aktiv/färdig med TurnIn-noden kvar.

Klientens IsQuestCompletable kontrollerar de aktuella kraven, inklusive föremål. De vanliga objektivräknarna används inte för att uppskatta inventarieinnehåll. Inget kommando för belöningsacceptans finns i flödet.

Förväntat resultat efter en progressdialog:

```text
QuestFrame.ClickContinue(): guarded native CompleteQuest command submitted once.
Quest conversation: ... stage=Reward; quest=2383; title=Simple Parchment; requestPending=0
QuestFrame.Instance.CurrentShownQuestId: 2383
Actions: interact=True; select=True; continue=True
TurnIn index=0; quest still active and completed=True; node retained=True
QUEST REWARD STEP 9 CONTINUE RESULT: PASS - progress -> reward offer confirmed for the expected NPC and quest.
STOPPED BEFORE REWARD ACCEPTANCE.
```

Om NPC:n eller gossip-valet öppnar belöningsdialogen direkt blir resultatet `REWARD READY` och continue=False. Om den redan är öppen vid programmets start blir det `ALREADY AT REWARD`, utan input. Dessa resultat bevisar inte en utförd Continue-övergång.

Låt belöningsdialogen vara kvar och klistra in hela terminalresultatet. Klicka inte på Complete Quest/Accept Reward efter testet; nästa steg behöver questen kvar.

## Gränser och felsökning

Varje åtgärd görs högst en gång. Testet väntar högst åtta sekunder på respektive dialogsvar och högst 30 sekunder totalt efter nedräkningen. Ändrad NPC, fel quest, död/strid, tappad hälsa eller borttagen quest stoppar körningen. Avbrott under skrivning försöker avbryta kommandoraden med Escape. Input-enheterna släpper hållna tangenter/knappar vid stängning.

Den vanliga quest-greeting-listan (mode 0) har ännu ingen automatisk väljare i det här testet; den stoppar med ett tydligt meddelande. Gossip-listan stöds. Om resultatet fastnar i Progress, kontrollera att föremålet finns kvar och att kommandot skrevs rätt.

Som i steg 8 krävs fungerande Enter-bindning för chatt, rätt aktiv layout och oförändrat WoW-fokus. Testet läser inte det fokuserade textfältet eller den inskrivna texten. Minnesläsningen kontrollerar klientens konversationstillstånd, inte den renderade Lua-rutans IsVisible. Se EVIDENCE.md och validation.txt för verifieringens omfattning.
