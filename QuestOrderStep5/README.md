# Honorbuddy Reborn – QuestOrder steg 5

Den verifierade questinformationen är nu kopplad till QuestBots profilindex och beslut. Det nya provet kör den riktiga QuestBot-roten två gånger och kontrollerar att ett slutfört Objective följs av TurnIn, utan att TurnIn felaktigt förbrukas.

## Installera och kör

Spara paketet i Downloads. Använd din befintliga projektrot. Paketet innehåller kompletta källfiler, inklusive den senaste QuestCacheProbe.cs med framstegsutskrift.

```fish
cd ~/Programming/Projects/Honorbuddy-Reborn

tar -czf "$HOME/Downloads/honorbuddy-reborn-before-step5-"(date +%Y%m%d-%H%M%S)".tar.gz" ./*.cs ./*.csproj

tar -xzf "$HOME/Downloads/honorbuddy-reborn-questorder-step5.tar.gz" -C .

env MSBuildEnableWorkloadResolver=false dotnet build HonorbuddyReborn.csproj

env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-order-self-test
```

Starta WoW och logga in på karaktären med quest 788 kvar i questloggen på 10/10. Kör:

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-order-test QuestOrderStep5/CuttingTeeth.xml
```

Förväntade nyckelrader:

```text
QuestOrder[0] -> [1]: Objective quest=788 entry=3098 progress=10/10
Current node index: 1
Decision: TurnInReady
TurnIn ready: quest=788 npc=3143; interaction is not implemented; node retained.
Node retained across second tick: True
QUEST ORDER STEP 5 RESULT: PASS
```

Detta är en verifiering av profilflödet. Testet initierar ingen inputbackend och utför ingen förflyttning, strid, NPC-interaktion eller belöningsacceptans. Testet använder uttryckligen den nya flaggan; programmets tidigare ordinarie startflöde finns kvar.

## Implementerat

- Profilparsern läser `Objective` med `QuestId`, `Type="KillMob"`, `MobId` och `KillCount`.
- QuestBot läser genom `StyxWoW.Me.QuestLog.GetQuestById`, `PlayerQuest.GetData` och Quest-egenskaperna. Klientoffsets ligger kvar i det interna Vanilla-lagret.
- Objective matchar exakt en WDB-plats med rätt mob-ID och antal. Den aktuella räknaren avgör när denna nod kan förbrukas.
- Vid 0/10 eller 1/10 blir beslutet `ObjectiveInProgress`. Vid 10/10 går profilindex vidare.
- TurnIn kräver dessutom spelarens Completed-flagga och ett aktivt, icke-misslyckat uppdrag. Enbart ett uppnått delmål räcker inte för att förklara hela uppdraget klart.
- `TurnInReady` behåller noden. Försvunnen quest betraktas inte som bevis för inlämning, eftersom den även kan ha övergivits.
- PickUp kan förbrukas om uppdraget redan är aktivt. Acceptans för ett saknat uppdrag är fortfarande uppskjuten.
- Ett förbrukat order-slut upprepar inte sista noden. En ny profil återställer profilindex.
- Okända kontrollnoder och Objective-typer blockeras. Väntan på questdata/interaktion räknas inte som att förflyttningen fastnat.

## Verifiering

Byggt som net10.0 med SDK 10.0.401: 0 varningar, 0 fel.

21 kontroller kör den riktiga QuestBot-roten med interna testsnapshots, inklusive 0/10 → 1/10 → 10/10, stabil TurnIn-nod, räknare utan Completed-flagga, failed, saknad metadata, fel ID/antal, tvetydiga platser, okänd nod, orderslut, ny profil och befintligt MoveTo-beslut. Detta är en intern testanslutning, ingen ny publik quest-provider.

Cache-regressionen från steg 4 passerar fortfarande 363 kontroller mot den medföljande verkliga quest-4641-filen och syntetiska fall.

Din tidigare terminalutskrift verifierar quest 788:s mål 3098, krav 10 och liveförändringen 0 → 1 → 10 med Completed vid 10. Den nya profilövergången har ännu inte live-testats mot din dator.

## Återstående arbete och kompatibilitetsgränser

Det här steget kopplar beslutsflödet. Att automatiskt bekämpa kvarvarande Objective-mål, navigera till questgivaren, öppna questdialogen, välja rätt uppdrag och acceptera belöningen återstår. `TurnInReady` betyder redo för den implementationen, inte inlämnat.

Den implementerade Objective-delmängden kräver explicit KillCount som motsvarar WDB-kravet och en entydig matchning. CollectItem, använd föremål, andra Objective-typer, villkor/loopar och historik för belönade uppdrag är inte implementerade. Befintliga GrindTo/MoveTo-beteenden är inte här ombyggda till en fullständig generell QuestOrder-motor.

De nya klasstyperna följer projektets befintliga ProfileNode-konvention. Originalets fullständiga Objective-klass/getters ingick inte i det uppladdade underlaget. Detta är alltså inte ett påstående om binär eller fullständig API-identitet med Honorbuddy 2.0.0.5999. Nästa utbyggnad ska fortsätta använda originalets referens där den finns.

Provprofilen använder quest/mob/antal från din avläsning. Gornek har entry 3143 enligt [Wowheads Classic-post](https://www.wowhead.com/classic/npc=3143/gornek). Profilen anger mottagaren; WDB-filen har inte gett oss NPC:ns position eller verifierat serverns NPC-relation. Ingen sökning eller interaktion med NPC:n utförs i detta steg.

## Filändringar jämfört med senaste steg 4

Ändrade: Program.cs, ProfileQuestNodes.cs, Profiles.cs, QuestBot.cs.

Nya: QuestBot.QuestOrder.cs, QuestOrderProbe.cs, denna dokumentation och CuttingTeeth.xml.

Övriga medföljande källfiler är oförändrade från den senaste leveransen. Alla källfiler ligger direkt i projektroten för att undvika en extra underkatalog med dubbla C#-definitioner.
