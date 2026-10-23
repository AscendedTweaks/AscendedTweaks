# DESIGN.md – Ascended Tweaks

Format nach dem Aufbau der DESIGN.md-Dateien aus voltagent/awesome-design-md (neun Abschnitte). Übernommen wurden nur die Gliederung und abstrakte Prinzipien, keine Marken, Farben oder Texte fremder Produkte. Vor jeder UI-Aufgabe wird diese Datei erneut gelesen. Weicht die UI ab, wird die UI korrigiert, nicht dieses Dokument.

## 1. Visual Theme & Atmosphere

- Ruhiges Windows-11-Fluent-Design. Dunkel ist Standard, Hell ist optional, "System" folgt der Windows-Einstellung.
- Wirkung: sachlich, hochwertig, aufgeräumt, vertrauenswürdig. Ein Werkzeug, das erklärt, nicht eines, das drängt.
- Nicht bunt, kein Hacker-Look, keine Neonfarben, keine Scareware-Optik, keine Fortschrittsbalken ohne echten Fortschritt.
- Dichte: mittel. Jede Tweak-Karte zeigt nur das Nötige, Details stehen im Seitenpanel.
- Ehrlichkeitshinweis ist Teil der Gestaltung: Dashboard und Kopfzeile jeder Tweak-Seite zeigen dauerhaft `banner.honesty`.

## 2. Color Palette & Roles

Tokens werden ausschließlich über Ressourcen-Schlüssel verwendet, nie als Hex-Wert im XAML.

### Dunkel (Standard)

| Token | Hex | Rolle |
|---|---|---|
| `Color.Background` | #131211 | Fensterhintergrund |
| `Color.Surface` | #1B1A18 | Karten, Navigation |
| `Color.Elevated` | #242220 | Dialoge, Seitenpanel, Hover-Fläche |
| `Color.Border` | #36332F | Ränder, Trennlinien |
| `Color.Accent` | #F0883E | Primäraktion, Fokusrahmen, Links |
| `Color.Success` | #6FC48A | Risiko "Sicher", Erfolg |
| `Color.Warning` | #E8C547 | Risiko "Vorsicht", Hinweise |
| `Color.Danger` | #F2666B | Risiko "Experte", Fehler |
| `Color.TextPrimary` | #EFECE7 | Fließtext, Überschriften |
| `Color.TextSecondary` | #A8A29A | Beschreibungen, Metadaten |
| `Color.OnAccent` | #131211 | Schrift und Symbole auf Akzentflächen |

### Hell

| Token | Hex | Rolle |
|---|---|---|
| `Color.Background` | #F5F3EF | Fensterhintergrund |
| `Color.Surface` | #FFFFFF | Karten, Navigation |
| `Color.Elevated` | #FFFFFF mit Rand `Color.Border` | Dialoge, Seitenpanel |
| `Color.Border` | #DCD7CE | Ränder |
| `Color.Accent` | #B4500F | Primäraktion, Fokus |
| `Color.Success` | #2C7A44 | Risiko "Sicher" |
| `Color.Warning` | #8A6300 | Risiko "Vorsicht" |
| `Color.Danger` | #C0323B | Risiko "Experte" |
| `Color.TextPrimary` | #1D1B18 | Fließtext |
| `Color.TextSecondary` | #5C564E | Beschreibungen |
| `Color.OnAccent` | #FFFFFF | Schrift und Symbole auf Akzentflächen |

### Kontrast

Farbschema seit 2026-10-23: warmes Graphit mit Signal-Orange (bewusst nicht der verbreitete Look aus Marineblau und elektrischem Blau). `tools/contrast-check` prüft im Build 102 Paare: alle Text- und Statusfarben auf Hintergrund, Fläche und Erhöht (4,5:1), OnAccent auf Akzent, und getönte Flächen als Alpha-Mischung (Auswahl 14 % Akzent, Hover 10 % TextSecondary, gefüllte Badges 15 % Tonfarbe; Text 4,5:1, Symbole 3:1). Niedrigste Werte: Akzent als Text im hellen Theme 4,63:1, Gefahr auf Erhöht im dunklen Theme 5,20:1.

### Risiko- und Wirkungs-Badges

- Risiko: Sicher = `Color.Success`, Vorsicht = `Color.Warning`, Experte = `Color.Danger`. Badge enthält immer Text und Symbol, Farbe ist nie das einzige Merkmal.
- Wirkung (neutral, nur Umriss): Belegt = Akzent-Umriss, Gering = Grau-Umriss (`Color.TextSecondary`), Umstritten = Warnung-Umriss mit Fragezeichen-Symbol.

## 3. Typography Rules

Schrift ausschließlich "Segoe UI Variable", Fallback "Segoe UI". WPF kennt die Variable-Schrift nur über ihre benannten Ausprägungen: Fließtext nutzt "Segoe UI Variable Text", Subtitle und Title "Segoe UI Variable Display" (Ressourcen `Font.Text` und `Font.Display`).

| Stil | Größe | Gewicht | Verwendung |
|---|---|---|---|
| Caption | 12 | Regular | Badges, Metadaten, Chips |
| Body | 14 | Regular | Fließtext, Kartenbeschreibung |
| BodyStrong | 14 | SemiBold | Kartentitel, Tabellenkopf |
| Subtitle | 20 | SemiBold | Abschnittsüberschriften |
| Title | 28 | SemiBold | Seitentitel |

Kartenbeschreibung höchstens zwei Zeilen, danach Auslassung; der volle Text steht im Seitenpanel.

## 4. Component Stylings

- **Tweak-Karte**: Mindesthöhe 96, Innenabstand 16, Radius 12, Fläche `Color.Surface`, Rand `Color.Border`. Inhalt: Schalter, Name, Kurzbeschreibung, Risiko-Badge, Wirkungs-Badge, Link "Mehr Details", Chips "Neustart nötig" bzw. "Admin nötig" (Schild-Symbol).
- **Schalter**: Zustände aus, an, Hover, Fokus, Deaktiviert, Teilweise (Mixed, Strich-Symbol), Nicht verfügbar (deaktiviert, Tooltip `unavailable.tooltip` mit Begründung).
- **Listen und Navigation**: Auswahl mit 14 % Akzent getönt plus Akzentstrich links, Hover mit 10 % TextSecondary. Grund: Im hellen Theme sind Fläche und Erhöht beide weiß, eine Auswahl über "Erhöht" wäre unsichtbar.
- **Buttons**: Primär (Akzentfläche), Sekundär (Umriss), Text, Gefahr (Umriss `Color.Danger`). Zustände Normal, Hover, Gedrückt, Fokus, Deaktiviert, Laufend (Spinner, Text bleibt sichtbar). Radius 8.
- **Eingaben**: Radius 8, Rand `Color.Border`, Fokus mit Akzentrahmen, Fehlertext unter dem Feld in `Color.Danger` mit Symbol.
- **Seitenpanel "Mehr Details"**: 420 breit, rechts, `Color.Elevated`. Inhalt: Beschreibung, "Was funktioniert danach nicht mehr", technische Details (Pfad, aktueller und neuer Wert), Quellen, Neustart-/Admin-Hinweis, Schaltfläche "Zurücksetzen".
- **Änderungsleiste**: unten, 56 hoch, erscheint nur bei ausstehenden Änderungen. Links `changebar.pending`, rechts "Verwerfen" (Text), "Vorschau" (sekundär), "Anwenden" (primär).
- **Dialoge**: Radius 12, `Color.Elevated`. Vorschau als Tabelle (Tweak, Stelle, Aktuell, Neu). Fortschritt zeigt echten Fortschritt pro Tweak. Experten-Warnung dreistufig (Erklärung, Kontrollkästchen, Eingabe "VERSTANDEN"/"UNDERSTOOD").
- **Fokusrahmen**: 2 Punkte Akzent, 2 Punkte Abstand, bei Tastaturbedienung immer sichtbar.

## 5. Layout Principles

- Raster 8 Punkte. Abstände 8, 16, 24, 32, 48.
- Radien: 8 für Buttons, Eingaben, Badges; 12 für Karten und Dialoge.
- Fenster Standard 1280 x 800, Minimum 800 x 480. Abweichung von 800 x 560: Bei 1920 x 1080 und 200 Prozent stehen nur rund 960 x 516 Punkte zur Verfügung; ein Minimum von 560 wäre dort größer als der Bildschirm. Inhalte scrollen deshalb vertikal.
- Navigation links 240 breit. Seiten: Dashboard, Leistung, Gaming, Datenschutz, Bereinigung, Debloat, Netzwerk, Datenträger, Updates, Profile, Wiederherstellung, Einstellungen.
- Hauptbereich: scrollbare Kartenliste, Kartenabstand 16.
- Weißraum vor Dekoration: keine Hintergrundbilder, keine Verläufe.

## 6. Depth & Elevation

- Drei Ebenen: Hintergrund, Fläche, Erhöht. Tiefe entsteht vor allem über Flächenfarbe und Rand, nicht über starke Schatten.
- Schatten nur für Dialoge und Seitenpanel, weich und kurz (Fluent-Standard), im hellen Theme zusätzlich Rand.
- Animationen 200 ms, Ease-Out, nur Opazität und Versatz bis 8 Punkte. Ist in Windows "Animationseffekte" aus, gibt es keine Animation.

## 7. Do's and Don'ts

Do:
- Ehrliche Badges: Wirkung nur so stark angeben, wie sie belegt ist.
- Nebenwirkungen immer zeigen ("Was funktioniert danach nicht mehr?" ist nie leer).
- Vor jeder Änderung die Vorschau anbieten.
- Alle Texte aus den Sprachdateien laden, nie hartkodieren.

Don't:
- Keine Angstformulierungen ("Dein PC ist in Gefahr", "Kritische Fehler gefunden").
- Keine Prozentversprechen, keine Superlative ohne Beleg, kein Gesamt-"Gesundheitswert".
- Keine Emojis.
- Keine Farbe als einziges Unterscheidungsmerkmal.
- Keine erfundenen Fortschrittsbalken, keine künstlichen Wartezeiten.

## 8. Responsive Behavior

- Unter 1000 Punkten Fensterbreite klappt die Navigation auf 56 Punkte ein (nur Symbole mit Tooltip).
- Seitenpanel überlagert unter 1000 Punkten den Inhalt, statt ihn zu verdrängen.
- Skalierung bis 200 Prozent ohne abgeschnittene Inhalte (Test 1920 x 1080 bei 200 Prozent).
- Hochkontrast-Modus: Systemfarben statt Tokens, Ränder bleiben sichtbar.
- Mindestgröße klickbarer Elemente 32 x 32 Punkte, Schalter 40 x 20.

## 10. Einfach-Modus (V2)

Standard beim ersten Start. Umschalter "Einfach / Experte" oben rechts neben dem Seitentitel; der aktive Modus hat einen Akzent-Rahmen, keine Akzent-Füllung (die Füllung gehört der Hauptaktion).

| Element | Maß |
|---|---|
| Navigation | 240 Punkte, 5 Einträge (Start, Schneller, Gaming, Aufräumen, Privatsphäre), unten fest: Zurück, Einstellungen |
| Fließtext | 15 (`Text.Simple.Body`), Titel der Karte 17 SemiBold (`Text.Simple.Title`), Hinweise 13 (`Text.Simple.Caption`) |
| Klickflächen | mindestens 40 Punkte hoch (Suchfeld, Knöpfe, Schalter-Karte) |
| Seitenabstand | 24 Punkte unter jeder Seite; Karten mit 20 x 16 Innenabstand |
| Karte | ein Schalter, Titel höchstens 5 Wörter, ein Satz höchstens 90 Zeichen, Wirkungswort (Spürbar, Klein, Unklar), ein Risiko-Symbol mit Text (Haken "Sicher", Ausrufezeichen "Mit Vorsicht", Schild "Nur für Experten"), "Technische Details" aufklappbar |
| Liste | nach Wirkung sortiert (Spürbar zuerst), höchstens 7 Karten, darunter "Mehr anzeigen (n)"; Suchfeld "Was möchtest du verbessern?" filtert Deutsch und Englisch |
| Hilfe | "Was bedeutet das?" mit höchstens 3 Sätzen je Seite; Glossar in den Einstellungen |
| Änderungsleiste | "{n} Dinge werden geändert. Neustart nötig: ja/nein." mit "Ansehen" und "Anwenden" |
| Farben | eine Akzentfarbe für die Hauptaktion je Bildschirm; sonst nur Risiko-Symbol sowie Erfolg und Fehler |

Kontrast: Der Einfach-Modus verwendet nur vorhandene Token-Paare (Text und Text-Sekundär auf Hintergrund, Fläche und Erhöht), die `tools/contrast-check` bereits mit mindestens 4,5:1 prüft. Barrierefreiheit: Die automatische Prüfung im Snapshot-Modus deckt die neuen Seiten ab (0 Befunde).

## 9. Agent Prompt Guide

Kurzreferenz: Hintergrund #131211, Fläche #1B1A18, Erhöht #242220, Rand #36332F, Akzent #F0883E (Schrift darauf #131211), Erfolg #6FC48A, Warnung #E8C547, Gefahr #F2666B, Text #EFECE7 / #A8A29A. Hell: #F5F3EF, #FFFFFF, #DCD7CE, Akzent #B4500F, Erfolg #2C7A44, Warnung #8A6300, Gefahr #C0323B, Text #1D1B18 / #5C564E.

Vorlagen für UI-Aufgaben:
- "Baue die Komponente X nach docs/DESIGN.md Abschnitt 4. Nutze nur Tokens aus Abschnitt 2, Abstände aus Abschnitt 5, alle Zustände aus Abschnitt 4, Texte über Lokalisierungsschlüssel."
- "Prüfe die Seite Y gegen docs/DESIGN.md: Tokens statt Hex, Fokusrahmen, Kontrast, 200 Prozent Skalierung, eingeklappte Navigation unter 1000 Punkten."
- "Erstelle die Tweak-Karte mit Schalter, Name, zwei Zeilen Beschreibung, Risiko- und Wirkungs-Badge (Text plus Symbol), Chips für Neustart und Admin, Link 'Mehr Details'."
