# Honorbuddy Reborn – TurnIn steg 6

TurnIn kan nu hitta profilens NPC via ObjectManager, välja NPC:ns aktuella position och använda den befintliga Navigator.MoveTo för en kort förflyttning. TurnIn-noden behålls även när spelaren har kommit fram.

## Installera

Alla källfiler är kompletta. Packa upp i projektroten:

```fish
cd ~/Programming/Projects/Honorbuddy-Reborn

tar -czf "$HOME/Downloads/honorbuddy-reborn-before-step6-"(date +%Y%m%d-%H%M%S)".tar.gz" ./*.cs ./*.csproj

tar -xzf "$HOME/Downloads/honorbuddy-reborn-turnin-step6.tar.gz" -C .

env MSBuildEnableWorkloadResolver=false dotnet build HonorbuddyReborn.csproj
```

## Upptäck NPC och visa förflyttningsbeslut

Ha quest 788 kvar slutförd i questloggen. Med WoW igång och karaktären inloggad:

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --turn-in-approach-test QuestOrderStep5/CuttingTeeth.xml
```

Det här läget initierar ingen inputbackend. Det visar NPC:ns entry, GUID, position, 3D-avstånd, 2D-avstånd och modellens interaktionsavstånd. Ett PLAN PASS verifierar upptäckt och beslut, inte utförd förflyttning.

Möjliga beslut:

| Beslut | Betydelse |
| --- | --- |
| MoveToQuestGiver | En användbar NPC har hittats och är utanför stoppavståndet. |
| QuestGiverInRange | NPC:n är redan inom modellens interaktionsavstånd. |
| QuestGiverUnavailable | Ingen användbar NPC med profilens entry finns i ObjectManager. |
| QuestStateBlocked | Queststatus eller höjdskillnad blockerar steget. |

## Kör den korta förflyttningen

Placera karaktären cirka 5–15 yards från Gornek, med fri väg på samma nivå. Den nuvarande Navigatorn styr direkt mot målet och kan inte planera en väg runt byggnader eller genom dörröppningar. Den bör därför inte användas för en lång väg in till honom i detta test.

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --turn-in-approach-test QuestOrderStep5/CuttingTeeth.xml --execute
```

Du får fem sekunder att ge WoW fokus. Stäng chatten. Den befintliga UInputPlayerMover använder W/A/D för framåt/vänstersväng/högersväng; kontrollera att spelets bindningar motsvarar det. Input skickas till det fokuserade fönstret och fokus kontrolleras inte automatiskt.

Testet använder samma /dev/uinput-backend som projektets tidigare rörelse. Det börjar bara med en giltig MoveToQuestGiver, en levande spelare utanför strid och NPC:n högst 25 yards bort. Förflyttningen begränsas till 30 yards från start, 25 sekunder totalt och fyra sekunder utan positionsframsteg. Befintliga kontroller stoppar vid strid eller minskad hälsa under förflyttningen. Ctrl+C i terminalen avbryter. Tangenter släpps i avslutningskoden.

Efter ankomst väntas:

```text
Node index=1; decision=QuestGiverInRange
TURN-IN STEP 6 APPROACH RESULT: PASS - Navigator calls=..., TurnIn node retained.
```

Om spelaren redan står inom avståndet rapporteras ALREADY IN RANGE; det räknas inte som ett genomfört rörelsetest. Rör dig inte bort för att testa om läget redan ger den verifiering du behöver.

## Implementationen

Normal QuestBot utvärderar nu TurnIn mot ObjectManager efter godkänd queststatus. Endast levande WoWUnit-objekt med exakt rätt entry, utan player-control och utanför strid används. Bland kandidater behålls tidigare vald GUID om den finns kvar, annars väljs närmaste. Positioner och avstånd måste vara ändliga.

Varje objektuppdatering ger nya positioner. Försvinner NPC:n eller blockeras questen försvinner förflyttningsbeslutet; tidigare rörelse stoppas. När NPC:n finns inom räckvidden stoppas rörelsen och TurnIn-noden ligger kvar för nästa implementationsdel.

Profilens NPC anges genom TurnInId. NPC-relation, belöningsdialog och aktuell questmarkör har inte verifierats; namn eller positioner hämtas inte från en ny extern questdatabas.

Den befintliga WoWObject.InteractRange är en fast modell på 4 yards. Normalt stannar koden 0,5 yards innanför. Om Navigatorns 2D-precision redan är nådd används det strikt mindre 3D-interaktionsavståndet. Att stå nära är inte bevis för fri sikt eller att spelet accepterar interaktionen. En NPC långt ovanför/under spelaren får inte räknas som nådd bara för att 2D-avståndet är litet.

## Verifiering

- Hela projektet byggt för net10.0 med SDK 10.0.401: 0 varningar, 0 fel.
- 24 nya lokala kontroller: urval av NPC, stabil GUID, fel entry, försvunnen/död/player-controlled NPC, ogiltiga koordinater, ändrad position, ankomst, avståndsgräns, höjdskillnader, bibehållen TurnIn-nod och släppt rörelse via den verkliga QuestBot-roten med en registrerande testmover.
- Steg-5-regressionen passerar 21 kontroller. Den äldre --quest-order-test behåller avsiktligt sitt läge för enbart Objective -> TurnIn-val.
- Varken det nya ObjectManager-urvalet mot din process eller faktisk uinput-förflyttning har kunnat köras här. Live-testet sker på din dator.

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --turn-in-self-test
```

## Återstående arbete

NPC-interaktion, questdialog, val av belöning och bekräftad inlämning är inte implementerade i steg 6. Det finns ännu ingen navmesh, kollisionskontroll eller automatisk fokuskontroll. Implementationen fortsätter använda projektets befintliga API och interna kompatibilitetskod; den nya beteendelogiken är inte verifierad som exakt dekompilerad Honorbuddy-originalkod.

Ändrade filer från steg 5: Program.cs, QuestBot.cs, QuestBot.QuestOrder.cs och QuestOrderProbe.cs. Nya filer: QuestBot.TurnIn.cs, TurnInApproachProbe.cs och denna dokumentation. Övriga källfiler är oförändrade.
