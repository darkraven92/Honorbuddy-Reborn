# Honorbuddy Reborn – QuestCache steg 4

Paketet innehåller kompletta källfiler från ditt uppladdade projekt, med cacheavkodning för WoW 1.12.1 build 5875. Filernas placering är platt så att de kan packas upp direkt i projektroten utan en extra undermapp med duplicerade C#-filer.

## Installation och test på CachyOS / fish

Kör i projektroten. Säkerhetskopian innehåller dina befintliga källfiler före uppackningen.

```fish
cd ~/Programming/Projects/Honorbuddy-Reborn

tar -czf "$HOME/Downloads/honorbuddy-reborn-before-step4-"(date +%Y%m%d-%H%M%S)".tar.gz" ./*.cs ./*.csproj

tar -xzf "$HOME/Downloads/honorbuddy-reborn-questcache-step4.tar.gz" -C .

env MSBuildEnableWorkloadResolver=false dotnet build HonorbuddyReborn.csproj

env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-cache-self-test QuestCacheStep4/testdata/questcache-4641.wdb

env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-cache-file-test "$HOME/Games/WoW Vanilla/WDB/questcache.wdb"
```

Starta sedan WoW och logga in på karaktären med questen i questloggen:

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-cache-metadata-test
```

Förväntat slutresultat: `QUEST CACHE STEP 4 LIVE RESULT: PASS`.

Det tidigare `--quest-cache-test` finns kvar som steg-3-identitetstest. `--quest-log-test` finns också kvar. Kör det nya `--quest-cache-metadata-test` för steg 4.

Alla dessa explicit valda tester läser data. Kör inte programmet utan testflagga om du avser att testa cachelagret; det ordinarie startläget finns kvar i det befintliga projektet.

## Verifierat i arbetsmiljön

- Hela det uppladdade C#-projektet byggt för net10.0 med SDK 10.0.401: 0 varningar, 0 fel.
- C#-parsern och Quest-egenskaperna körda direkt mot din uppladdade WDB-fil: PASS.
- Regressionstest: 363 kontroller, inklusive den verkliga posten, syntetiska icke-nollfält, UTF-8, felformat, varje avklippt prefix av testfilen, fel build/version, dubbla ID:n, ID-konflikt, arrayisolering, cachekedjan och övergång från identitet till metadata.
- Live-testet mot ditt WoW har inte körts här. Det kräver din lokala process och aktiva questlogg.

| Avläst från den uppladdade posten | Värde |
| --- | --- |
| ID | 4641 |
| Namn | Your Place In The World |
| Level | 1 |
| NextQuestId | 788 |
| RewardMoney | 0 |
| RewardMoneyAtMaxLevel | 30 |
| RewardSpellId | 0 |
| Föremåls- och normalmålsräknare | Fyra nollor i respektive fält |

Räknarnas nollor är inte ett bevis på slutförande. `PlayerQuest.IsCompleted` och `IsFailed` fortsätter att läsa spelarens live-deskriptorer.

## Arkitektur och levererade egenskaper

Kedjan är fortsatt `StyxWoW.Cache → WoWCache[CacheDb.Quest] → Cache → InfoBlock → QuestCacheEntry → Quest / PlayerQuest`.

`Vanilla5875QuestCache` hanterar filupptäckt och cache. Den nya interna `Vanilla5875QuestCacheReader` avkodar bytes. Den interna `Vanilla5875QuestData` håller klientens fält. Ingen separat publik provider eller extern questdatabas har tillkommit.

Följande Quest-egenskaper hämtar nu WDB-data: `Name`, `Description`, `Objectives`, `Level`, `NextQuestId`, `CollectItemIDs`, `CollectItemCounts`, `NormalObjectiveIDs`, `NormalObjectiveRequiredCounts`, `RewardMoney`, `RewardMoneyAtMaxLevel`, `RewardSpellId`.

Strängarna behåller klientens `$B`, `$N` och liknande markörer. Arrayegenskaperna returnerar kopior med fyra platser; platserna filtreras inte bort. GameObject-markören i bit 31 bevaras i `NormalObjectiveIDs`. Negativt belopp i det interna pengafältet betyder kostnad; det exponeras inte som en stor osignerad belöning.

Enbart `live-identity` räcker fortfarande för ID och live-status, men en ny metadataegenskap kastar `InvalidOperationException` när WDB-information saknas. Det är ett explicit kompatibilitetsbeteende för denna filbaserade backend, inte ett verifierat original-Honorbuddy-undantagskontrakt. En sådan Quest-instans kan få metadata när filen senare dyker upp. Redan avkodade Quest-instanser är snapshots; skapa en ny via QuestLog för ny metadata efter en filändring.

## Avgränsningar som återstår

Detta är fungerande cacheavkodning och en utökad del av Quest-API:t, inte fullständig rekonstruktion av alla originalets getters eller strukturlayout.

- `RequiredLevel` och direkt `RewardXp` finns inte i den avkodade posten. De har inte lagts till med gissade eller nollställda värden.
- Målöversikten och sluttexten avkodas internt och visas som `WDB objective summary` respektive `WDB end text`. Exakt koppling till originalets `SubDescription`, `ObjectiveText` och `CompletionText` behöver originalets getterimplementationer; de namnen har inte fyllts med antagna alias.
- `CollectIntermediateItemIDs/Counts`, `RewardSpell` som WoWSpell-objekt och övriga senare expansionsfält återstår. Startföremålet är inte automatiskt ett mellanliggande insamlingsmål.
- `QuestCacheEntry` är fortfarande ett hanterat kompatibilitetsvärde, inte originalets verifierade binärlayout.
- Filbaserad metadata kan vara fördröjd tills klienten sparat sin cache. Nya quests som saknas på disk ger BLOCKED i steg-4-live-testet. Det motsvarar inte originalets omedelbara minnescacheåtkomst.
- Första verkliga provet har inga item-/kill-räknare. Icke-nollfält och GameObject-markören är testade syntetiskt; fler verkliga questposter behövs för bredare klientverifiering.
- Läser bara TSQW, build 5875, record version 3. Saknad terminator, oväntade bytes, trasiga strängar eller poster gör hela cachefilen ogiltig. Gränser: 64 MiB fil, 1 MiB post. De gränserna är implementationsgränser.

## Ändrade filer

- `Quest.cs`: interna metadata samt stödda Quest-egenskaper.
- `Vanilla5875QuestCache.cs`: indexerar avkodade poster i stället för bara ID/offset.
- `Program.cs`: tre nya diagnostikflaggor före ordinarie startflöde.
- Nya `Vanilla5875QuestCacheReader.cs`, `QuestCacheProbe.cs`, `QuestCacheSelfTest.cs`.
- Övriga medföljande C#-filer och projektfilen är byte-för-byte oförändrade jämfört med uppladdningen.

## Underlag

Den uppladdade 927-bytefilen ger en 20-byteheader, en post med 891-bytepayload och en 8-byteterminator. Payloaden börjar med en upprepad questidentitet. Titelns offset är 156 byte in i payloaden. Därefter följer de variabla text- och målfälten. Hela posten konsumeras exakt.

Fältordningen jämfördes med [VMaNGOS WDBReader, Quest.h på vanilla-grenen](https://github.com/vmangos/WDBReader/blob/vanilla/WDBReader/Quest.h), särskilt `ReadEntry`. Detta är protokollunderlag; det verifierar inte Honorbuddys getters. C#-avkodaren är skriven för projektets befintliga modell. Originalets EXE och dekompilerade getters ingick inte i denna uppladdning.
