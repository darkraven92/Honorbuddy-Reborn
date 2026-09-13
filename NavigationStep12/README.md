# Mesh-navigation, steg 12

`Navigator` delegerar till `INavigationProvider`. `MeshNavigator` beräknar och följer
vägen; `IPlayerMover` utför rörelsen. `QuestBot`, profilnoder och behavior tree
väljer fortfarande mål och bestämmer när ett queststeg kan avslutas.

Den interna adaptern läser VMaNGOS mmap v6 / Detour v7 med 64-bitars polygonreferenser.
Den ersätter den gamla implementationen som returnerade `[start, mål]` och sedan
styrde direkt mot målet. En saknad eller ofullständig meshväg stoppar nu rörelsen.

## Bygg

Kräver .NET 10, CMake och en C++17-kompilator samt VMaNGOS Detour-källa.
Verifierad källrevision i `vmangos/core`:
`448df9ba06d2b2b1678fcfd782a77e1e3ebf26b9`.
Byggskriptet kontrollerar varje Detour-källfils SHA-256 innan kompilering.
Detour-källan, spelklienten och extraherade kartfiler distribueras inte här.

```bash
bash NavigationStep12/build.sh /home/ludvig/Programming/Projects/vmangos-core/dep/recastnavigation/Detour
```

Skriptet bygger och testar native-adaptern och bygger C#-projektet. Biblioteket
kopieras till samma katalog som `HonorbuddyReborn.dll`. Vanlig `dotnet build`
bygger C# och kopierar ett redan byggt native-bibliotek; kör skriptet efter ändrad C++-kod.

## Test utan spelinmatning

```bash
dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --navigation-self-test
dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --navigation-self-test /home/ludvig/Games/WoW-NavData/mmaps
dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --mesh-path-test /home/ludvig/Games/WoW-NavData/mmaps 1 -618.667 -4245.333 38.914 -699.2 -4202.667 36.914
```

Native-testet genererar själv en L-formad korridor, en frånkopplad yta och en
separat våning. Det kontrollerar faktiskt Detour-sökning runt ett hinder, inte
bara en mockad waypointlista. C#-testerna kontrollerar providerdelegation,
vägföljning, byte av mål/provider/mover, omplanering, utebliven rörelse och stopp.
Valfria verkliga tester använder nio Kalimdor-tiles, en väg genom Valley of Trials
och en väg över gränsen mellan `0013339` och `0013340`.

`valley-path.json` är resultatet från den första verkliga vägen: 137 punkter.
Punkterna är testpunkter på meshen, inte verifierade NPC-positioner eller en questprofil.

## Liveprov

Läs först klientens aktuella karta, position, relevanta NPC-positioner och kontrollera
att spelaren ligger på gångbar mesh:

```bash
dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --navigation-live-test /home/ludvig/Games/WoW-NavData/mmaps
```

Ett explicit mål kan läggas till som tre världskoordinater, exempelvis
`--navigation-live-test <mmaps> <x> <y> <z>`. Samma kommando med `--execute`
följer vägen med Navigator. Första rörelseprovet är begränsat till 25 yards från
start, 25 sekunder, levande spelare utan strid och bibehållen hälsa. Det börjar efter
fem sekunder; WoW behöver fokus och stängd chatt. Ctrl+C frigör rörelsetangenterna.

Befintligt QuestBot-prov för approach till en färdig quests NPC använder också mesh:

```bash
dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --turn-in-approach-test QuestOrderStep5/CuttingTeeth.xml --mesh-directory /home/ludvig/Games/WoW-NavData/mmaps --execute
```

Även `--combat-routine-test` behöver nu `--mesh-directory <mmaps>` för rörelse.
En okonfigurerad provider kan inte förflytta spelaren.

Liveförsöket i denna leverans nådde WoW-processen men stoppades av Linux
`ptrace_scope=1` vid minnesläsning. `sudo -n` krävde lösenord. Ingen spelinmatning
skickades. Den läsande proben kan köras av användaren med `sudo dotnet ...` om
systemets policy kräver det; agenten har inte ändrat systemets ptrace-policy.

## Referenser och gränser

Honorbuddy-referensen är den lokala binären med SHA-256
`87ff9ef92f02e89c1d060c0071581730dcb446aae597b6f0d97006f46e118f3a`,
med tillhörande `Honorbuddy.xml`. `api-metadata.txt` innehåller resultat från
statisk metadataavläsning utan att exekvera binären. Den verifierar bland annat
`IPlayerMover`, `INavigationProvider`, `IStuckHandler`, `Navigator` och enumvärdena
för `MoveResult`. Implementationen är en rekonstruktion för Vanilla, inte en kopia
av hela originalets navigationsimplementation.

Klientreferensens SHA-256 är
`b4756d38ef207c02ed651f4952bd89a70b4857b73a33413339e1b285b28d2dc7`.
Lua-registret vid `0x83E520` kopplar `IsInInstance` till `0x48A750`.
Funktionen läser kartindex från `0xB4E378`, slår upp Map.dbc via `0xC0DAA8`
och läser instanstyp på rad+8. Kartbytesfunktionen skriver indexet vid `0x495D45`.
Runtimeadaptern verifierar filhash, tre kodankare och kartans DBC-rad samt stoppar
vid byte av karta, klient eller karaktär. Denna avläsning är statiskt verifierad;
livebekräftelse återstår.

WoW XYZ översätts internt till Detour YZX. Sökningen tillåter gångbar mark och
avvisar vatten, magma, slime, branta sluttningar och off-mesh-förbindelser.
Markhöjden samplas med högst en yards avstånd i sidled. Ändpunkter får projiceras
högst 0,75 yards i sidled och 2 yards i höjd till meshen. Vägföljaren använder
0,35 yards precision i sidled och 2 yards höjdtolerans. Meshen, inklusive dess
agentmått och kollisionsunderlag, kommer från den lokala extraktionen.

Stuck-hanteringen stoppar och gör ett omplaneringsförsök; den hoppar inte över
hinder. Profilerna kan inte ge någon garanti om gångbarhet. Quest 4641/788/789
är ännu inte ett självgående end-to-end-flöde: PickUp, automatisk NPC-interaktion
och kopplingen mellan Objective, strid, loot och inventarium återstår. Den lokala
5875-WDB:n bekräftar 788 som 3098 × 10 och 789 som föremål 4862 × 10;
insamlingsmålet får inte räknas som dödade mobs.

De uppgivna `/workspace/scratch/ec40fe0bdd87/...`-referenserna var inte tillgängliga
i denna lokala miljö. Den tidigare `Vanilla5875NavMesh.cs` har därför inte kunnat
jämföras; ovanstående lokala binär, dokumentation och VMaNGOS-källa användes.
