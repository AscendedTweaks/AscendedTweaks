# Messprotokoll für Leistungsaussagen

Gilt für alle Leistungs- und Gaming-Tweaks, die "Belegt" oder "Gering" tragen sollen (Auftrag Abschnitt 6). Ohne vollständige Messung bleibt ein Tweak mit Geschwindigkeitsversprechen "Umstritten" und ist standardmäßig ausgeblendet.

## Regeln

- 5 Läufe vorher, 5 Läufe nachher, jeweils der Median.
- Gleiche VM oder gleicher Rechner, Neustart zwischen den Läufen, gleiche Hintergrundlast (keine anderen Programme, Netzwerk wie im Alltag, Windows Update pausiert).
- Messwerkzeuge sind nur Entwicklungswerkzeuge und werden nicht ausgeliefert.
- Ergebnis mit Rohdaten in `docs/SOURCES.md`, Messdateien unter `tests/perf/<Tweak-ID>/`.

## Einstufung

| Ergebnis (Median nachher gegen vorher) | Nachweis |
|---|---|
| Verbesserung ab 2 Prozent | Belegt |
| Verbesserung über 0 und unter 2 Prozent | Gering |
| Keine Verbesserung oder Verschlechterung | Umstritten (oder Placebo und Ablehnung, wenn wiederholt kein Effekt) |
| Weniger als 5 Läufe je Seite | Umstritten |

`tools/perf-measure compare` schlägt die Stufe nach dieser Tabelle vor.

## Ablauf Bildrate (FPS)

1. Spiel und Szene festlegen, die reproduzierbar ist (eingebauter Benchmark oder feste Wiederholung, 60 Sekunden).
2. Aufzeichnung mit PresentMon (Entwicklungswerkzeug, nicht im Lieferumfang), CSV je Lauf.
3. `perf-measure fps vorher lauf1.csv … lauf5.csv > vorher.json`
4. Tweak über TweakPilot anwenden, Neustart, Schritte 2 und 3 mit `nachher` wiederholen.
5. `perf-measure compare vorher.json nachher.json`
6. Tweak zurücksetzen und prüfen, dass der Ausgangszustand wieder erreicht ist (Integrationstest Schritt 6).

Ausgewertet werden Durchschnitts-FPS (1000 / mittlere Bildzeit) und 1-%-Tief (1000 / mittlere Bildzeit der langsamsten 1 Prozent). Unterstützte Spalten: `MsBetweenPresents` (PresentMon 1.x) und `FrameTime` (2.x).

## Ablauf Startdauer

1. Startdauer aus dem Ereignisprotokoll `Microsoft-Windows-Diagnostics-Performance/Operational`, Ereignis 100 (BootDuration in Millisekunden), je Neustart.
2. `perf-measure values vorher ms <wert1> … <wert5> > vorher.json`, nachher entsprechend, dann `compare` (kleiner ist besser).

## Stand

Noch keine Messung durchgeführt: Der Entwicklungsrechner darf nicht für Messreihen neu gestartet werden, und es gibt keine VM (`DECISIONS.md`, offene Punkte 1 und 12). Werkzeug und Auswertung sind getestet (`MeasurementTests`).
