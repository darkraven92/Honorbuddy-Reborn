# Steg 10 – acceptera belöningen och flytta fram TurnIn

Detta steg använder den öppna belöningsdialogen för Simple Parchment (2383) hos Frang (3153).
Profilen från steg 9 används oförändrad: `QuestRewardStep9/SimpleParchment.xml`.

## Installera (fish)

```fish
cd ~/Programming/Projects/Honorbuddy-Reborn
tar -czf "$HOME/Downloads/honorbuddy-reborn-before-step10-"(date +%Y%m%d-%H%M%S)".tar.gz" ./*.cs ./*.csproj
tar -xzf "$HOME/Downloads/honorbuddy-reborn-rewardaccept-step10-xpfix.tar.gz" -C .
env MSBuildEnableWorkloadResolver=false dotnet build HonorbuddyReborn.csproj
```

Arkivet innehåller hela den tidigare leveransen plus steg 10. Det innehåller inga klientbinärer,
byggfiler eller någon questcache.wdb som skriver över spelets cache. Säkerhetskopieringskommandot ovan
sparar projektets C#- och projektfiler; profiler och dokumentation ingår i leveransen.

## Läsande prov

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-reward-accept-test QuestRewardStep9/SimpleParchment.xml
```

Förväntat: quest 2383 fortfarande aktiv och completed, Frang inom räckhåll, dialogläge Reward,
inga väntande förfrågningar och `choices=0`. Provet skriver ut spelarens XP och fasta belöningar.
Ingen inmatningsenhet initieras. Om dialogen är stängd, kör steg 9 igen för att öppna belöningserbjudandet:

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-reward-dialog-test QuestRewardStep9/SimpleParchment.xml --execute --keyboard-layout se
```

Om questen redan har lämnats in blockeras provet. Det markerar inte en saknad quest som belönad.
Om `choices` är större än noll blockeras acceptansen; denna fas väljer inga föremål.

## Acceptera en gång

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-reward-accept-test QuestRewardStep9/SimpleParchment.xml --execute --keyboard-layout se
```

Under nedräkningen: fokusera WoW, behåll belöningsdialogen, stäng chatten, stäng av Caps Lock och
släpp tangenterna. Ingen muspekare över NPC:n behövs i detta steg. Flytta, klicka eller skriv inte
under provet. Kommandot **accepterar belöningen**; det stannar inte vid erbjudandet.

`QuestFrame.Instance.CompleteQuest()` skickar exakt en gång:

```text
/script if GetNumQuestChoices()==0 then GetQuestReward() end
```

Tillståndet kontrolleras både före inmatning och omedelbart före sista Enter. Vid ändrat tillstånd
avbryts den delvis skrivna raden med Escape. Ctrl+C avbryter provet. Redan inskickad acceptans kan
inte återtas. Ingen automatisk omsändning sker.

## Vad PASS betyder

Efter inskickad acceptans krävs två observationer, minst 150 ms isär, inom ett observationsfönster
på cirka 10 sekunder:

- Samma process och spelare; spelaren och NPC:n är kvar inom räckhåll, utan strid eller hälsoförlust.
- Quest 2383 har försvunnit och övriga aktiva quest-ID:n är oförändrade.
- Både quest- och gossipkonversationen är stängda.
- Spelarens uppmätta XP har ökat. En nivåökning stöds, med XP före nollställningen medräknad.

Först då flyttas just den bundna TurnIn-noden från index 0 till 1 och profilen blir ProfileComplete.
Vanlig QuestBot-utvärdering hoppar fortfarande inte över en saknad quest. Start, Stop, profilbyte,
nodbyte och redan förbrukade transaktioner förhindrar återanvändning av bekräftelsen.

Detta är **korrelerade före/efter-observationer**, inte ett avlyssnat serverkvitto. Inga samtidiga
XP-givande händelser får inträffa under provet: exempelvis övergivning tillsammans med orelaterad XP
skulle annars kunna efterlikna delar av förloppet. Ingen permanent historik över belönade quester
skapas. Provet stöder inte belöningar utan positiv XP, spelare på nivå 60 eller flera nivåökningar.
Mängden quest-XP hämtas inte från WDB eller gissas; endast spelarens faktiska förändring mäts.

Vid `UNCONFIRMED` kan klienten redan ha accepterat belöningen, men noden flyttas inte fram utan alla
observationer. Kontrollera spelets questlogg och dialog och dela utskriften. Kör inte en automatisk
omgång för att försöka få PASS. Vid en ny process börjar profilen på nytt; ingen checkpoint återställs.

Steget utför ingen förflyttning, NPC-interaktion, gossipselektion eller ny questacceptans.
Sting of the Scorpid (789) får fortsätta vara aktiv.

## Lokala verifieringar

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --quest-reward-accept-self-test
```

XP-rättningen är byggd med .NET SDK 10.0.401: 0 varningar, 0 fel. 68 kontroller för acceptans
och 46 dialogkontroller passerade. Den gamla XP-offseten har rättats; se EVIDENCE.md. Ingen WoW-klient eller uinput användes i dessa kontroller. Live-resultatet för steg 10
återstår på användarens dator. Se `EVIDENCE.md` och `validation.txt`.
