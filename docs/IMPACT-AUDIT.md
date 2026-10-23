# Wirkungs-Audit (V2-1)

Stand 2026-10-19. Jeder Tweak hat eine Wirkung für Nutzer: **Spürbar** (nur mit Messbeleg oder Microsoft-Beleg für ein sofort wahrnehmbares Verhalten, Feld `measuredOn`), **Klein** (belegt, aber kaum wahrnehmbar oder nur für Datenschutz und Ruhe) oder **Unklar** (Wirkung nicht belegt; nur im Experten-Modus). Nichts wurde gelöscht oder schöngeredet.

Ergebnis: 4 Spürbar, 43 Klein, 54 Unklar (Katalog plus Basis der erzeugten Tweaks; seit V2-4 mit den neun neuen Einträgen BROW, POWR, DEFN und NET-012).

| ID | Name | Risiko | Nachweis | Wirkung | Begründung |
|---|---|---|---|---|---|
| BROW-001 | Edge-Schnellstart aus | Caution | Proven | Klein | Weniger Edge-Prozesse nach der Anmeldung; Richtlinie, Browser zeigt "verwaltet". |
| BROW-002 | Edge im Hintergrund aus | Caution | Proven | Klein | Wirkt nur, wenn der Hintergrundmodus eingeschaltet war. |
| BROW-003 | Chrome im Hintergrund aus | Caution | Proven | Klein | Wirkt nur mit installiertem Chrome und eingeschaltetem Hintergrundmodus. |
| DEB-027 | Widgets abschalten | Caution | Proven | Klein | Ein Hintergrundprozess weniger, Wirkung auf RAM nicht gemessen. |
| DEFN-001 | Defender-Scans sanfter | Caution | Proven | Klein | Scan-Last 20 statt 50 Prozent; Leerlauf-Scans sind ausgenommen, Schutz bleibt an. |
| DISK-001 | TRIM aktivieren | Safe | Proven | Klein | Nur wirksam, wenn TRIM vorher aus war; Windows-Standard ist an. |
| DISK-004 | Speicheroptimierung an | Safe | Disputed | Unklar | Registry-Wert nicht dokumentiert; Wirkung erst nach Sichtpruefung belegt. |
| DISK-007 | Reservierten Speicher abschalten | Caution | Disputed | Unklar | Gewinn von rund 7 GB nicht gemessen; Updates können bei wenig Platz scheitern. |
| GAME-001 | Spielmodus an | Safe | Disputed | Unklar | Standardmäßig an; Gewinn nicht belegt. |
| GAME-002 | Hardwarebeschleunigte GPU-Planung an | Caution | Disputed | Unklar | Wirkung hängt von Grafikkarte und Spiel ab; nicht gemessen. |
| GAME-003 | Game Bar nicht per Controller öffnen | Safe | Disputed | Unklar | Nur Bedienkomfort; Registry-Werte nicht dokumentiert. |
| GAME-004 | Spielaufnahme und Übertragung aus | Safe | Proven | Klein | Dokumentierte Richtlinie; Aufnahme läuft ohnehin nur bei Bedarf. |
| GAME-005 | Hintergrundaufnahme aus | Safe | Disputed | Unklar | Hintergrundaufnahme kostet etwas Leistung; Werte nicht dokumentiert. |
| GAME-006 | Vollbildoptimierungen global aus | Caution | Disputed | Unklar | Kann je nach Spiel besser oder schlechter sein. |
| GAME-007 | Vollbildoptimierung für ein Spiel aus | erzeugt | - | Unklar | Kann je nach Spiel besser oder schlechter sein. |
| GAME-008 | GPU-Präferenz für ein Spiel | erzeugt | - | Unklar | Nur auf PCs mit zwei Grafikkarten wirksam; nicht gemessen. |
| GAME-009 | Optimierungen für Spiele im Fenstermodus | Safe | Disputed | Unklar | Senkt die Latenz im Fenstermodus laut Microsoft; für diesen Rechner nicht gemessen. |
| GAME-010 | Variable Bildwiederholrate im Fenstermodus | Safe | Disputed | Unklar | Nur mit VRR-Monitor wirksam; nicht gemessen. |
| GAME-011 | Mausbeschleunigung aus | Safe | Proven | Spürbar | Zeigerbewegung folgt der Maus 1:1; die Änderung ist sofort spürbar. |
| GAME-012 | Tastenkürzel der Einrastfunktion aus | Safe | Proven | Klein | Verhindert nur das Fenster nach fünfmal Umschalt. |
| GAME-013 | Tastenkürzel für Anschlag- und Statusanzeige aus | Safe | Proven | Klein | Verhindert nur Fenster bei langem Druck auf Umschalt oder Num. |
| GAME-016 | Dauerhaft hohe Priorität für ein Spiel (Experte) | erzeugt | - | Unklar | Kein belegter Gewinn; bei Anti-Cheat gesperrt. |
| GAME-019 | Speicherintegrität ausschalten (Experte) | Expert | Disputed | Unklar | Schwächt die Sicherheit; Gewinn nicht belegt. |
| GAME-021 | Globale Timer-Auflösung (Experte) | Expert | Disputed | Unklar | Kein belegter Nutzen, mehr Stromverbrauch. |
| GAME-022 | Höchste Bildwiederholrate | Caution | Proven | Spürbar | Deutlich flüssigere Bewegung, wenn der Bildschirm unter seinem Maximum lief. |
| GAME-023 | Zeigergeschwindigkeit auf Standard (1:1) | Caution | Proven | Klein | Ändert nur etwas, wenn die Zeigergeschwindigkeit nicht in der Mitte stand. |
| GAME-024 | Schnellere Tastenwiederholung | Caution | Proven | Klein | Nur gehaltene Tasten außerhalb von Spielen, die Tasten direkt lesen. |
| NET-001 | DNS: Cloudflare | Safe | Proven | Klein | Andere Namensauflösung; Geschwindigkeit hängt vom Anschluss ab. |
| NET-002 | DNS: Google | Safe | Proven | Klein | Andere Namensauflösung; Geschwindigkeit hängt vom Anschluss ab. |
| NET-003 | DNS: Quad9 | Safe | Proven | Klein | Andere Namensauflösung mit Schutz vor Schadseiten. |
| NET-004 | Eigene DNS-Server | erzeugt | - | Klein | Andere Namensauflösung; Geschwindigkeit hängt vom Anschluss ab. |
| NET-009 | LLMNR ausschalten | Caution | Proven | Klein | Sicherheitsgewinn in fremden Netzen, keine Geschwindigkeit. |
| NET-010 | TCP-Autotuning auf Standard | Safe | Proven | Klein | Stellt nur den Windows-Standard wieder her. |
| NET-011 | Energiesparen der Netzwerkkarte aus | Caution | Disputed | Unklar | Wirkung nicht belegt; kostet auf Notebooks Akku. |
| NET-012 | Energy-efficient Ethernet aus | Caution | Proven | Klein | Leitung wechselt nicht mehr in den Stromsparzustand; nur Adapter mit *EEE. |
| PERF-001 | Energiesparplan Höchstleistung | Safe | Disputed | Unklar | Kein messbarer Gewinn in Spielen belegt. |
| PERF-002 | Energiesparplan „Ultimative Leistung“ | Caution | Disputed | Unklar | Kein messbarer Gewinn gegenüber Höchstleistung belegt. |
| PERF-003 | Visuelle Effekte: optimale Leistung | Safe | Disputed | Unklar | Wirkung auf aktuellen PCs nicht gemessen. |
| PERF-004 | Transparenzeffekte aus | Safe | Disputed | Unklar | Wirkung auf aktuellen PCs nicht gemessen. |
| PERF-005 | Fensteranimationen aus | Safe | Proven | Spürbar | Fenster öffnen sofort statt mit Animation; sofort sichtbar. |
| PERF-006 | Menü-Verzögerung 100 ms | Safe | Proven | Spürbar | Untermenüs öffnen 300 ms früher als mit dem Standard von 400 ms. |
| PERF-007 | Autostart-Verzögerung aufheben | Safe | Disputed | Unklar | Registry-Wert nicht dokumentiert; Wirkung nicht gemessen. |
| PERF-008 | Hintergrund-Apps sperren | Caution | Proven | Klein | Weniger Apps im Hintergrund; RAM-Gewinn nicht gemessen. |
| PERF-009 | Ruhezustand ausschalten | Caution | Proven | Spürbar | Gibt die Ruhezustandsdatei frei, meist mehrere GB. |
| PERF-010 | Schnellstart aus | Caution | Disputed | Unklar | Hilft nur bei bestimmten Startproblemen. |
| PERF-011 | Letzte Zugriffszeit nicht mehr speichern | Safe | Disputed | Unklar | Auf SSDs ohne messbaren Nutzen. |
| PERF-012 | Kurze 8.3-Dateinamen nicht mehr anlegen | Expert | Disputed | Unklar | Kein belegter Gewinn; alte Installer können scheitern. |
| PERF-013 | Windows-Suche auf Manuell | Caution | Disputed | Unklar | Suche wird langsamer; Gewinn nicht gemessen. |
| PERF-014 | SysMain auf Manuell | Caution | Disputed | Unklar | Auf SSDs ohne belegten Nutzen. |
| PERF-015 | Druckwarteschlange deaktivieren | Caution | Proven | Klein | Ein Dienst weniger; nur ohne Drucker sinnvoll. |
| PERF-016 | Fax-Dienst deaktivieren | Safe | Disputed | Unklar | Dienst läuft ohnehin nur bei Bedarf. |
| PERF-017 | Einzelhandelsdemo-Dienst deaktivieren | Safe | Disputed | Unklar | Dienst läuft ohnehin nur bei Bedarf. |
| PERF-018 | Karten-Manager deaktivieren | Safe | Disputed | Unklar | Dienst läuft ohnehin nur bei Bedarf. |
| PERF-020 | Prozessor-Zeitscheiben für Vordergrund (Experte) | Expert | Disputed | Unklar | Kein belegter Gewinn auf aktuellen Systemen. |
| PERF-021 | Aufgabe „Microsoft Compatibility Appraiser“ aus | Caution | Disputed | Unklar | Läuft selten; Gewinn nicht gemessen. |
| PERF-022 | Aufgabe „ProgramDataUpdater“ aus | Caution | Disputed | Unklar | Läuft selten; Gewinn nicht gemessen. |
| PERF-023 | Aufgabe CEIP „Consolidator“ aus | Safe | Disputed | Unklar | Läuft selten; Gewinn nicht gemessen. |
| PERF-024 | Aufgabe CEIP „UsbCeip“ aus | Safe | Disputed | Unklar | Läuft selten; Gewinn nicht gemessen. |
| PERF-025 | Aufgabe Fehlerberichts-Warteschlange aus | Safe | Disputed | Unklar | Läuft selten; Gewinn nicht gemessen. |
| PERF-026 | Autostart-Eintrag deaktivieren | erzeugt | - | Klein | Wirkung hängt vom Programm ab; im Autostart-Assistenten gemessen. |
| POWR-001 | USB-Energiesparen aus (nur Desktop) | Caution | Disputed | Unklar | Vorteil für Eingabegeräte nicht belegt. |
| POWR-002 | PCI-Express-Energiesparen aus (nur Desktop) | Caution | Disputed | Unklar | Spürbarer Vorteil nicht belegt. |
| POWR-003 | Prozessor-Mindestleistung 100 % (nur Desktop) | Caution | Proven | Klein | Schnellere Reaktion bei plötzlicher Last, mehr Strom und Wärme. |
| POWR-004 | Festplatte nie ausschalten (nur Desktop) | Caution | Proven | Klein | Nur bei Festplatten, die sich abschalten (meist HDD). |
| PRIV-001 | Werbe-ID ausschalten | Safe | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-002 | Diagnosedaten auf Minimum | Safe | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-003 | Telemetrie-Dienst deaktivieren | Caution | Disputed | Unklar | Kann Updates und Fehlerdiagnose stören; Gewinn nicht belegt. |
| PRIV-005 | Aktivitätsverlauf ausschalten | Safe | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-006 | Standortzugriff für Apps verweigern | Caution | Proven | Klein | Wirkt auf den Datenschutz; Karten und Wetter wissen den Ort nicht. |
| PRIV-008 | Websuche im Startmenü ausschalten | Safe | Disputed | Unklar | Einstellung nicht dokumentiert; Wirkung erst nach Sichtprüfung belegt. |
| PRIV-009 | Suchhighlights ausschalten | Safe | Proven | Klein | Weniger Inhalte im Suchfeld; wirkt auf Ruhe und Datenschutz. |
| PRIV-010 | Copilot in Windows ausschalten | Caution | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-011 | Recall-Momentaufnahmen verhindern | Caution | Proven | Klein | Wirkt auf den Datenschutz; nur auf Copilot+-PCs relevant. |
| PRIV-012 | Maßgeschneiderte Erfahrungen ausschalten | Safe | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-013 | Feedback-Abfragen ausschalten | Safe | Proven | Klein | Weniger Rückfragen; keine Geschwindigkeit. |
| PRIV-014 | Sammeln von Eingabe- und Handschriftdaten ausschalten | Safe | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-015 | Sprachliste für Websites verbergen | Safe | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-016 | App-Starts nicht verfolgen | Safe | Proven | Klein | Wirkt auf den Datenschutz; Start zeigt weniger Vorschläge. |
| PRIV-017 | Vorschläge, Tipps und App-Werbung ausschalten | Safe | Disputed | Unklar | Einstellung nicht dokumentiert; Wirkung erst nach Sichtprüfung belegt. |
| PRIV-018 | Empfehlungen im Startmenü ausschalten | Safe | Disputed | Unklar | Einstellung nicht dokumentiert; Wirkung erst nach Sichtprüfung belegt. |
| PRIV-019 | Kontobenachrichtigungen im Startmenü ausschalten | Safe | Disputed | Unklar | Einstellung nicht dokumentiert; Wirkung erst nach Sichtprüfung belegt. |
| PRIV-020 | Windows-Fehlerberichterstattung ausschalten | Caution | Proven | Klein | Wirkt auf den Datenschutz; Microsoft erhält keine Fehlerberichte. |
| PRIV-021 | Programm zur Verbesserung der Benutzerfreundlichkeit ausschalten | Safe | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-022 | App-Kompatibilitäts-Telemetrie ausschalten | Caution | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-023 | Geräteübergreifende Zwischenablage ausschalten | Safe | Proven | Klein | Wirkt auf den Datenschutz, nicht auf die Geschwindigkeit. |
| PRIV-024 | Lieferoptimierung ohne Peer-to-Peer | Safe | Proven | Klein | Kein Upload an fremde PCs; Downloads laufen normal weiter. |
| UI-001 | Klassisches Kontextmenü (Windows 11) | Safe | Disputed | Unklar | Nur Bedienung; Registry-Schlüssel nicht dokumentiert. |
| UI-002 | Taskleiste linksbündig | Safe | Disputed | Unklar | Nur Darstellung; Wert nicht dokumentiert. |
| UI-003 | Aufgabenansicht-Symbol ausblenden | Safe | Disputed | Unklar | Nur Darstellung; Wert nicht dokumentiert. |
| UI-004 | Chat-Symbol ausblenden | Safe | Disputed | Unklar | Nur Darstellung; Wert nicht dokumentiert. |
| UI-005 | Widgets-Symbol ausblenden | Safe | Disputed | Unklar | Nur Darstellung; Wert nicht dokumentiert. |
| UI-006 | Suche als Symbol | Safe | Disputed | Unklar | Nur Darstellung; Wert nicht dokumentiert. |
| UI-007 | Dateiendungen anzeigen | Safe | Disputed | Unklar | Mehr Übersicht; Wert nicht dokumentiert. |
| UI-008 | Versteckte Dateien anzeigen | Safe | Disputed | Unklar | Nur Darstellung; Wert nicht dokumentiert. |
| UI-009 | Explorer startet in „Dieser PC“ | Safe | Disputed | Unklar | Nur Bedienung; Wert nicht dokumentiert. |
| UI-010 | Mehr angeheftete Apps im Start | Safe | Disputed | Unklar | Nur Darstellung; Wert nicht dokumentiert. |
| UI-011 | Sekunden in der Uhr | Safe | Disputed | Unklar | Nur Darstellung; Wert nicht dokumentiert. |
| UPD-001 | Updates pausieren | erzeugt | - | Unklar | Werte nicht dokumentiert; Sichtprüfung steht aus. |
| UPD-002 | Funktionsupdates 90 Tage verschieben | Caution | Proven | Klein | Weniger Überraschungen durch neue Versionen; keine Geschwindigkeit. |
| UPD-003 | Qualitätsupdates 7 Tage verschieben | Caution | Proven | Klein | Mehr Ruhe, aber Sicherheitsupdates kommen später. |
| UPD-004 | Treiber nicht über Windows Update | Caution | Proven | Klein | Nur sinnvoll, wenn du Treiber selbst pflegst. |
| UPD-005 | Kein automatischer Neustart bei Anmeldung | Caution | Proven | Klein | Kein Neustart mitten in der Arbeit; Updates wirken erst nach Neustart. |
| UPD-006 | Nutzungszeit 8 bis 22 Uhr | Safe | Proven | Klein | Weniger Neustarts tagsüber; keine Geschwindigkeit. |
| UPD-007 | Windows-Version festhalten | erzeugt | - | Unklar | Registry-Wertnamen nur in der ADMX-Datei. |

## Messbelege für "Spürbar"

- GAME-011: Microsoft Learn: SystemParametersInfo SPI_SETMOUSE (Beschleunigungsschwellen und Faktor 0 = Zeiger folgt der Maus ohne Beschleunigung); Host-Test 26200: Werte vorher/nachher geprüft
- GAME-022: Microsoft Support: "Change the refresh rate on your monitor in Windows" (höhere Rate = flüssigere Bewegung). Wirkt nur, wenn der Bildschirm unter seiner höchsten Rate läuft; der Gaming-Check "Bildwiederholrate" zeigt beide Werte. Kein Host-Test (kein HKCU-Tweak), nur Engine-Tests mit Fake-Bildschirm.
- PERF-005: Microsoft Learn: SystemParametersInfo SPI_SETCLIENTAREAANIMATION und SPI_SETANIMATION (Fensteranimationen aus); Host-Test 26200
- PERF-006: Microsoft Learn: SystemParametersInfo SPI_SETMENUSHOWDELAY (Verzögerung in Millisekunden); Host-Test 26200: Windows-Standard 400 ms, nachher 100 ms
- PERF-009: Microsoft Learn: "Hibernation" (Größe der Ruhezustandsdatei standardmäßig rund 40 Prozent des Arbeitsspeichers; powercfg /hibernate off entfernt sie)

Wirkung wird in Ressourcen beschrieben (Speicher, Wartezeit, Verhalten), nie als FPS-Versprechen. Neue Tweaks bekommen ihre Einstufung beim Anlegen; der Katalog-Linter lehnt "Spürbar" ohne Messbeleg und "Unklar" ohne Experten-Modus ab.
