# Steg 7 — öppna NPC-konversation och läsa quest-ID

Utgångspunkt: det levererade steg-6-paketet. Arkivet innehåller kompletta källfiler, projektfil, tidigare testprofiler och dokumentationen här. Originalbinärerna och bin/obj ingår inte.

Detta steg kopplar `WoWObject.Interact()` till ett vanligt högerklick med kontroll av NPC:n under muspekaren. `QuestFrame.Instance.CurrentShownQuestId`, `ActiveQuests` och `AvailableQuests` läser klientens questkonversation. Gossip-listan läses internt när klienten öppnar den i stället.

Du behöver själv placera muspekaren över Gornek. Programmet flyttar inte pekaren och kontrollerar inte vilket fönster som har fokus. Det utför ett klick och väntar högst åtta sekunder på rätt NPC och quest. TurnIn behålls; questen lämnas inte in i detta steg.

## Installera (fish)

Spara arkivet i Downloads. Kör i projektmappen:

```fish
cd ~/Programming/Projects/Honorbuddy-Reborn

tar -czf "$HOME/Downloads/honorbuddy-reborn-before-step7-"(date +%Y%m%d-%H%M%S)".tar.gz" ./*.cs ./*.csproj

tar -xzf "$HOME/Downloads/honorbuddy-reborn-interaction-step7.tar.gz" -C .

env MSBuildEnableWorkloadResolver=false dotnet build HonorbuddyReborn.csproj

env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --turn-in-interaction-self-test
```

## Testa i spelet

Behåll Cutting Teeth (788) i questloggen, färdig 10/10. Stå inom cirka 3,5 yards från Gornek (3143), som efter steg 6. Om du har flyttat dig kan du köra steg-6-approach igen. Stäng NPC-dialogen innan execute-testet.

Läs först läget utan input:

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --turn-in-interaction-test QuestOrderStep5/CuttingTeeth.xml
```

`PLAN RESULT: READY` betyder att förutsättningarna är lästa och NPC:n är nära. Det verifierar inte någon interaktion. Om rätt konversation redan är öppen kan lästestet skriva `OBSERVE RESULT: PASS`.

Kör sedan klicktestet:

```fish
env MSBuildEnableWorkloadResolver=false dotnet run --project HonorbuddyReborn.csproj -- --turn-in-interaction-test QuestOrderStep5/CuttingTeeth.xml --execute
```

Under de fem sekunderna: fokusera WoW, ha chatten stängd och placera pekaren över Gorneks modell, så att hans tooltip syns. Klicka inte själv. Håll pekaren stilla tills testet har avslutats. Input använder samma /dev/uinput-behörighet som den fungerande rörelsen, men en separat virtuell mus.

Förväntad framgång:

```text
WoWObject.Interact(): right click sent=True; awaiting client conversation (8 seconds).
...
TURN-IN STEP 7 INTERACTION RESULT: PASS - expected NPC and quest confirmed in live conversation data.
TurnIn node retained. No quest selection, CompleteQuest, reward choice, or reward acceptance was performed.
```

Om en lista öppnas är `CurrentShownQuestId: 0` korrekt; quest 788 måste då finnas i den aktiva listan. Om själva questen öppnas ska dess ID vara 788. Klistra in hela resultatet inför nästa steg och behåll questen utan att lämna in den.

Vid fel muspekare skickas inget klick. Vid timeout eller annan NPC skickas inget nytt klick. Stäng dialogen och kör om när förutsättningarna är rätt. `--execute` krävs varje gång; att öppna en dialog manuellt och köra lästestet är endast en observation.

## Verifiering

Se validation.txt. Bygget och syntetiska tester körs här, men WoW/Wine och det riktiga musinputet kan endast verifieras i din miljö. Se REVERSE-ENGINEERING.md för original-API, adresser, kontroll av klientversion och gränser för vad PASS betyder.
