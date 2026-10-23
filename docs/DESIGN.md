# DESIGN.md – Ascended Tweaks

Format nach dem Aufbau der DESIGN.md-Dateien aus voltagent/awesome-design-md (neun Abschnitte). Übernommen wurden nur die Gliederung und abstrakte Prinzipien, keine Marken, Farben oder Texte fremder Produkte. Vor jeder UI-Aufgabe wird diese Datei erneut gelesen. Weicht die UI ab, wird die UI korrigiert, nicht dieses Dokument.

## 1. Visual Theme & Atmosphere

- Freundliches, lebendiges Windows-11-Fluent-Design. Es gibt nur das dunkle Design (seit 2026-10-23 kein heller Modus mehr); der Windows-Hochkontrast nutzt weiter die Systemfarben.
- Wirkung: sachlich, hochwertig, aufgeräumt, vertrauenswürdig. Ein Werkzeug, das erklärt, nicht eines, das drängt.
- Grau als Hauptfarbe, Schwarz für Schattierung, blaue Akzente und ein weicher blauer Glow an wenigen Stellen (siehe .claude/skills/ascended-design), aber kein Hacker-Look, keine Neonränder, keine Blau-Lila-Verläufe, keine Scareware-Optik, keine Fortschrittsbalken ohne echten Fortschritt.
- Dichte: mittel. Jede Tweak-Karte zeigt nur das Nötige, Details stehen im Seitenpanel.
- Ehrlichkeitshinweis ist Teil der Gestaltung: Dashboard und Kopfzeile jeder Tweak-Seite zeigen dauerhaft `banner.honesty`.

## 2. Color Palette & Roles

Tokens werden ausschließlich über Ressourcen-Schlüssel verwendet, nie als Hex-Wert im XAML.

### Dunkel (einziges Design): Grau mit blauen Akzenten

| Token | Hex | Rolle |
|---|---|---|
| `Color.Sidebar` | #202124 | Rahmen: Titelleiste und Navigation (dunkleres Grau) |
| `Color.Background` | #2B2D31 | Inhaltspanel der Seiten (Hauptfarbe Grau), runde Ecke oben links |
| `Color.Surface` | #313338 | Karten, Kacheln |
| `Color.Elevated` | #383A40 | Dialoge, Seitenpanel |
| `Color.Border` | #1E1F22 | Ränder, Trennlinien (schwarze Schattierung) |
| `Color.AccentFill` | #3D6BE0 | Primärschaltflächen (kräftiges Blau, Schrift weiß) |
| `Color.Accent` | #8AB0FF | Links, Fokus, Schalter an (helles Blau) |
| `Color.AccentTint` | #2A3A5E | Auswahl in Navigation und Segmenten, Symbolkacheln (Marineblau) |
| `Color.Success` | #BEBEBE | Risiko "Sicher", Erfolg |
| `Color.Warning` | #F2F2F2 | Risiko "Vorsicht", Hinweise |
| `Color.Danger` | #F29A9A | Risiko "Experte", Fehler (gedämpftes Rot) |
| `Color.TextPrimary` | #F2F2F2 | Fließtext, Überschriften |
| `Color.TextSecondary` | #BEBEBE | Beschreibungen, Metadaten |

### Kontrast

`tools/contrast-check` prüft im Build alle Text- und Statusfarben auf Hintergrund, Fläche und Erhöht (4,5:1), OnAccent auf Akzent und getönte Flächen als Alpha-Mischung (Auswahl 14 % Akzent, Hover 10 % TextSecondary, gefüllte Badges 15 % Tonfarbe; Text 4,5:1, Symbole 3:1). Seit 2026-10-23 zusätzlich: Weiß auf Knopf-Blau 4,80:1, Text auf Marine-Auswahl 12,78:1.

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
- **Listen und Navigation**: Auswahl mit 14 % Akzent getönt plus Akzentstrich links, Hover mit 10 % TextSecondary.
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
- Schatten nur für Dialoge und Seitenpanel, weich und kurz (Fluent-Standard). Glow nur an Primärschaltflächen, dem aktiven Navigationsstrich und Preset-Symbolen.
- Animationen 200 ms, Ease-Out, nur Opazität und Versatz bis 8 Punkte. Ist in Windows "Animationseffekte" aus, gibt es keine Animation.

## 7. Do's and Don'ts

Do:
- Ehrliche Badges: Wirkung nur so stark angeben, wie sie belegt ist.
- Nebenwirkungen immer zeigen ("Was funktioniert danach nicht mehr?" ist nie leer).
- Ein Klick, wo es sicher geht: Vor dem Klick steht auf Karte oder Kachel, was sich ändert; "Änderungen ansehen" zeigt die volle Vorschau. Löschen (Bereinigung, große Dateien, Apps) immer mit Vorschau und Bestätigung.
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

Kurzreferenz: Hintergrund #2B2D31, Fläche #313338, Erhöht #383A40, Rand #1E1F22, Knopf #3D6BE0 (Schrift weiß), Akzent #8AB0FF, Auswahl #212A3F, Erfolg #BEBEBE, Warnung #F2F2F2, Gefahr #F29A9A, Text #F2F2F2 / #BEBEBE. Nur dunkles Design.

Vorlagen für UI-Aufgaben:
- "Baue die Komponente X nach docs/DESIGN.md Abschnitt 4. Nutze nur Tokens aus Abschnitt 2, Abstände aus Abschnitt 5, alle Zustände aus Abschnitt 4, Texte über Lokalisierungsschlüssel."
- "Prüfe die Seite Y gegen docs/DESIGN.md: Tokens statt Hex, Fokusrahmen, Kontrast, 200 Prozent Skalierung, eingeklappte Navigation unter 1000 Punkten."
- "Erstelle die Tweak-Karte mit Schalter, Name, zwei Zeilen Beschreibung, Risiko- und Wirkungs-Badge (Text plus Symbol), Chips für Neustart und Admin, Link 'Mehr Details'."
