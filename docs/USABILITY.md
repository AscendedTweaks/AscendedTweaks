# Usability-Test (V2, Abschnitt 3)

Ziel: Ein Mensch ohne Vorwissen schafft jede Aufgabe in höchstens 3 Klicks. Gezählt wird jeder Klick in Ascended Tweaks ab dem geöffneten Fenster im Einfach-Modus. Eine Windows-Rückfrage nach Administratorrechten (UAC) zählt nicht als Klick in der App, wird aber notiert.

## Aufgaben und Klickpfade

| Nr. | Aufgabe | Klickpfad | Klicks | Stand |
|---|---|---|---|---|
| 1 | Gaming-Preset anwenden | Start (Startseite) -> Karte "Gaming": "Ansehen und anwenden" (1) -> Vorschau: "Anwenden" (2) -> Ergebnis: "Fertig" (3) | 3 | erfüllt; enthält das Preset Maschinen-Tweaks, kommt zusätzlich die UAC-Abfrage |
| 2 | Einen Tweak einzeln zurücknehmen | "Zurück" (1) -> Eintrag: "Rückgängig" (2) -> Ergebnis: "Fertig" (3) | 3 | erfüllt; ohne Rückfrage, weil die Sicherung den alten Wert wiederherstellt |
| 3 | Müll aufräumen | "Aufräumen" (1, Scan startet selbst, sichere Bereiche sind angehakt) -> "Aufräumen" (2) -> Rückfrage: "Bereinigen" (3) | 3 | erfüllt (seit V2-6 mit der schlanken Einfach-Ansicht); das Ergebnisfenster schließt der vierte Klick ("Fertig"), die Aufgabe ist aber nach 3 Klicks erledigt |

Erneut geprüft am 2026-10-20 (V2-8): Pfade unverändert gültig; Pfad 3 nutzt die neue Einfach-Ansicht von "Aufräumen". Erstmals selbst durchgeführt am 2026-10-19 mit dem Snapshot-Modus und den ViewModel-Tests (`SimpleModeTests`, `CleanupPageTests`): Pfad 1 und 2 laufen ohne Administratorrechte mit Benutzer-Tweaks durch, Pfad 3 bis zur Rückfrage (nichts gelöscht). Zeiten mit echten Testpersonen fehlen (siehe unten).

## Ablauf mit Testpersonen

1. Testperson ohne Vorwissen, App frisch installiert, Einfach-Modus, Bildschirm wird aufgezeichnet (nur mit Einwilligung).
2. Aufgabe vorlesen, nicht zeigen. Zeit ab dem Vorlesen bis zur erledigten Aufgabe messen, Klicks zählen.
3. Lautes Denken erlaubt, keine Hilfe geben. Abbruch nach 3 Minuten.
4. Nach jeder Aufgabe fragen:
   - Was hat die App gerade geändert?
   - Wie würdest du das zurücknehmen?
   - War ein Wort unklar? Welches?
5. Am Ende: "Was bedeutet Spürbar, Klein und Unklar?" und "Wofür ist der Schalter Einfach / Experte?"

## Messblatt

| Person | Aufgabe | Zeit (s) | Klicks | Erledigt (ja/nein) | Unklare Wörter | Bemerkung |
|---|---|---|---|---|---|---|
| | 1 | | | | | |
| | 2 | | | | | |
| | 3 | | | | | |

Erfolgskriterium: Jede Aufgabe von mindestens 4 von 5 Personen in höchstens 3 Klicks und unter 60 Sekunden erledigt; jedes genannte unklare Wort wird im Text ersetzt.
