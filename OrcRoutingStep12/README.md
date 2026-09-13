# Orc-routing för 4641, 788 och 789

`ValleyOfTrials.xml` beskriver ordningen mellan Kaltunk, Gornek, boar-området och
scorpid-området. Alla sju förflyttningsrelationer i profilen kan beräknas på den
lokala Vanilla-meshen. NPC-relationer och referenskoordinater är verifierade mot
ClassicDB; se [EVIDENCE.md](EVIDENCE.md).

Detta steg kopplar ihop profilrouting, inte hela questautomationen:

- `MoveTo` förbrukas först när Navigator har rapporterat ankomst. QuestOrder går
  sedan vidare till nästa nod i samma profil.
- PickUp och TurnIn använder en laddad NPC:s aktuella position. Om NPC:n saknas
  används profilens valfria X/Y/Z som sökposition. Ankomst utan NPC behåller noden
  och stoppar sökningen. När NPC:n senare blir tillgänglig används dess live-GUID.
- PickUp förbrukas endast när questen finns aktiv och inte är misslyckad i loggen.
  Att närma sig NPC:n eller komma inom räckhåll innebär ingen questacceptans.
- `CollectItem ItemId="4862" CollectCount="10"` behandlas som ett insamlingsmål.
  För en quest med ett enda insamlingskrav och inga andra mål kan en matchande
  WDB-post plus klientens färdigmarkering föra Objective vidare. Delräknaren är
  okänd; andra mål med flera insamlingskrav blockeras tills inventarieläsning finns.

Automatisk questacceptans, full dialogkedja från QuestBot, strid och loot återstår.
TurnIn kräver fortfarande färdig aktiv quest och verifierad belöningsacceptans.
Profilen är avsedd att börja med 4641; saknade äldre quests tolkas inte som redan
belönade. Den nya proben stannar vid första nod som behöver interaktion eller
arbete med ett questmål. Inga tangentkommandon för acceptans, belöningar eller
strid skickas av routingproben.

## Bygg och testa

Bygg native-biblioteket enligt [navigationsstegets instruktioner](../NavigationStep12/README.md).
Därefter:

```bash
env MSBuildEnableWorkloadResolver=false dotnet build --no-restore
dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --quest-routing-self-test /home/ludvig/Games/WoW-NavData/mmaps
```

Testet använder verkliga QuestBot-rootbeslut och arrival/approach-exekvering med
syntetisk klientstatus. Med meshkatalogen testar det dessutom alla profilens
förflyttningsrelationer genom verklig Detour-sökning. Utan katalogargument körs
endast testerna som inte behöver kartdata.

## Liveplan och rörelse

```bash
dotnet bin/Debug/net10.0/HonorbuddyReborn.dll --quest-routing-test OrcRoutingStep12/ValleyOfTrials.xml /home/ludvig/Games/WoW-NavData/mmaps
```

Planläget läser aktuellt QuestOrder-beslut och kontrollerar dess meshväg utan att
initialisera inmatning. Lägg till `--execute` för att låta QuestBot följa rutten
till nästa checkpoint. Spelaren måste matcha profilens karta och nivåintervall.
Provet börjar efter fem sekunder, kräver WoW i fokus med stängd chatt och avbryts
vid strid, minskad hälsa, utebliven rörelse, 300 yards förskjutning eller 90 sekunder.
Ctrl+C frigör rörelsetangenterna.

Liveprovet har ännu inte kunnat verifieras: systemet nekar `process_vm_readv`
med errno 1. Kör vid behov den läsande proben med `sudo` i din terminal och dela
resultatet innan rörelseprovet. Agenten har inte ändrat systemets ptrace-policy.
