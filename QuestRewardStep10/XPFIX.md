# XP-basrättning

Den första steg 10-leveransen använde relativa player-offseter från fel bas.
Korrekt full-descriptor XP är 0xB30 och nästa nivå 0xB34. Klientens player-fältpekare
(object+0xE68) pekar på descriptor+0x2F0. Den relationen kontrolleras nu live.
Felmeddelanden visar exakt vilket värde som inte är giltigt, inklusive nivå och XP.

Tidigare testfixtur delade det felaktiga antagandet. Den nya fixturen innehåller medvetet
ogiltiga värden på de gamla adresserna och korrekta värden på full-descriptor-adresserna.
Bygget lyckas utan varningar/fel; 68 acceptanskontroller och 46 dialogkontroller passerar.
Live-rättningen återstår att kontrollera med --quest-reward-accept-test utan --execute.

Detta kompletta arkiv ersätter steg 10-koden och korrigerar dokumentationen. Det innehåller
ingen ny pathfindingimplementation. Tidigare byggfiler och klientbinärer ingår inte.

# Nästa huvudmilstolpe: självgående profil i Valley of Trials

1. Navigation enligt originalets Navigator.NavigationProvider / INavigationProvider.
   Anslut en Vanilla-kompatibel mesh-backend, validera kartdata/koordinater, följ beräknade
   vägsegment och hantera ofullständiga vägar, höjdskillnader, stopp och omplanering.
   Befintlig GeneratePath i denna leverans returnerar fortfarande bara start och mål.
2. Automatisk NPC-interaktion utan manuell hover, med rätt NPC/quest verifierad.
   Koppla dialogerna till QuestBot, inklusive PickUp och TurnIn.
3. Koppla profilens Objective till strid, loot och inventariebaserade insamlingsräknare.
4. Kör en hel profil: PickUp -> väg till mål -> Objective -> väg tillbaka -> TurnIn.
   Godkänt kräver en sammanhängande livekörning utan separata testkommandon mellan noderna.

Originalets högnivå-API och beteendeträd är referensen. Kartformatet och klientåtkomsten
anpassas för 1.12.1. Tidigare Detour-klientkod finns som möjligt internt underlag, men dess
integration och kartdata är ännu inte verifierade i detta projekt. Full Honorbuddy-likvärdighet
kräver fler funktioner än denna första avgränsade questprofil.
