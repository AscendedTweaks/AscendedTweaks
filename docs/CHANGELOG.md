# Changelog

Format nach Keep a Changelog, Versionen nach Semantic Versioning.

## [Unreleased]

### Hinzugefügt
- GAME-022 "Höchste Bildwiederholrate": stellt den Hauptbildschirm mit einem Klick auf die höchste Rate der aktuellen Auflösung (neuer Aktionstyp `DisplayRefreshRate`, Erkennung `DisplayRefreshIsHighest`); "Jetzt beheben" im Gaming-Check "Bildwiederholrate" nutzt ihn. In den Presets Gaming und Wettkampf.
- Schlagwörter: Jeder Tweak trägt ein bis drei Schlagwörter aus einer festen Liste (zum Beispiel "FPS", "Verzögerung", "Ping"), als Chips auf den Karten. Sie nennen den Bereich, nie einen gemessenen Gewinn; der Linter prüft sie.
- GAME-025 "Spiel bleibt bei Anrufen laut" (Experte), GAME-026 "Tastenkürzel für Maustasten aus", GAME-027 "Game-Bar-Aufnahmen aus, auch bei Home" (Experte), NET-013 "Hintergrund-Updates höchstens 25 % Bandbreite".
- Kurzes Ladefenster beim Start; Arbeitsspeicher in ganzen GB; Autostart-Kachel mit Erklärung und Link zur Liste.
- GAME-023 "Zeigergeschwindigkeit auf Standard (1:1)" und GAME-024 "Schnellere Tastenwiederholung" (SystemParametersInfo `MouseSpeed`, `KeyboardDelay`, `KeyboardSpeed`).

### Behoben
- DNS-Tweaks meldeten auf Adaptern ohne IPv6 "Windows oder ein anderes Programm blockiert die Änderung" und setzten alles zurück; erwartet werden jetzt nur die Adressen, die der Adapter aufnehmen kann.

## [1.1.0] - noch nicht veröffentlicht

Erste öffentliche Version (V3), nicht signiert. Lizenz MIT.

### Hinzugefügt
- V3-2: Eigenes Symbol (`assets/icon`, `tools/make-icon.ps1`) für EXE-Dateien, Fenster, Titelleiste und Installer.
- V3-3: Englische `README.md`, `LICENSE` (MIT), GitHub-Workflows für Build und Tests, Release mit Prüfsummen und Build-Attestierung sowie GitHub Pages.
- V3-4: Website in `site/` (statisch, ohne Cookies und ohne Anfragen an fremde Server).

### Geändert
- V3-1: Die App startet immer auf Englisch; die Sprache wird in "First steps", im Installationsdialog oder in den Einstellungen gewählt. Fehlermeldungen von Engine und Helfer sind englisch.

### Behoben
- Die App beendete sich, wenn der Helfer beim Anwenden einen Fehler meldete oder die Administratorabfrage abgelehnt wurde (zum Beispiel beim Ändern des DNS-Servers); der Fehler steht jetzt im Ergebnisfenster.

## [1.1.0-rc.1] - 2026-10-20

Release-Kandidat V2, nicht signiert. Weiterhin offen: Name, Lizenz, Zertifikat, Update-Adresse, Rechtstexte, VM-Testmatrix (siehe `docs/DECISIONS.md`).

### Hinzugefügt
- V2-7: Fertiger Ordner auf dem Desktop (`build.ps1 -Package`, `package.ps1`): `AscendedTweaks.exe`, Helfer und Kommandozeile als einzelne Dateien ohne installiertes .NET, Dokumente, `START HIER.txt`, Prüfsummen, ZIP. Beim ersten Start "Installieren (empfohlen)" pro Benutzer ohne Administratorrechte (Startmenü, Desktop, Eintrag in "Apps") oder "Nur ausprobieren (portabel)"; Deinstallation über "Apps" oder `--uninstall`, Sicherungen standardmäßig behalten. Der Helfer akzeptiert den Paketordner, App und Kommandozeile prüfen vorher den Hash des Helfers. `docs/BUILD.md`.
- V2-6: Große Dateien neu: Klassen "Kann meist weg", "Bitte prüfen", "Wahrscheinlich behalten" und "Spiele und Systembereiche" mit Klartext "Warum sehe ich das?", nichts vorausgewählt, Löschen nur in den Papierkorb mit erneuter Prüfung jeder Datei, Cloud-Platzhalter werden nie geladen, Abzweigungen nie verfolgt, Duplikate per SHA-256 als Gruppe, "Speicher pro Spiel" ohne Löschen, "Immer behalten"-Liste mit Ansicht in den Einstellungen, Zeitlimit 90 Sekunden mit Fortschritt, Schwelle im Experten-Modus einstellbar. Einfach-Seite "Aufräumen" mit schlanker Ansicht. Kommandozeile `--large-scan`.
- V2-5: Zehn Gaming-Checks (Bildwiederholrate, Energiesparmodus, Hybrid-Notebook, Arbeitsspeicher-Takt und -Riegel, Platz auf dem Systemlaufwerk, Spielmodus, Grafiktreiber-Alter, Hintergrund, Secure Boot/TPM/Speicherintegrität) nur als Anzeige mit "Beheben"-Hinweis, Link zu den Windows-Einstellungen oder zur Herstellerseite und Sprung in die App. Karte "Messung" auf der Start-Seite mit Vorher/Nachher nach dem nächsten Neustart. DEFN-001 ist nicht verfügbar, wenn Defender nicht der aktive Virenschutz ist.
- V2-4: Autostart-Assistent erkennt zehn bekannte Programme (Steam, Epic, Discord, Battle.net, EA app, Ubisoft Connect, Spotify, Teams, OneDrive, Adobe-Updater) per Datei und Signatur, mit Klartext und RAM-Angabe, wenn das Programm läuft. Browser im Hintergrund (BROW-001 bis BROW-003), Energie-Feintuning nur für Desktop-PCs (POWR-001 bis POWR-004, neuer Aktionstyp `PowerSettingSet`, Feld `desktopOnly`), Defender-Scan-Last (DEFN-001, Schutz bleibt an), Energy-efficient Ethernet aus (NET-012), Launcher-Caches CLEAN-015 bis CLEAN-020, Hintergrund-Bericht mit 15 Prozessen, Klartext und Sprung zum Autostart.
- V2-3: Sieben Presets auf der Start-Seite (Sicher & Schnell, Gaming, Gaming Wettkampf, Notebook Akku, Privatsphäre, Aufräumen, Büro ruhig) mit Stufen Sanft/Standard/Stark nach fester Regel, Vorschau in Klartext-Gruppen, "Alles wieder rückgängig" im Ergebnis, "Preset rückgängig" ohne Verlust anderer Presets, Konflikt-Hinweis "vorher aus Preset ...", Preset "Aufräumen" nur mit Bereinigung, "Eigenes Preset" mit Name und Haken.

### Geändert
- V2-3: "Empfohlene Optimierung" im Dashboard entfernt (jetzt Preset "Sicher & Schnell"). Profilformat um `symbol` und `cleanup` erweitert. Zurücksetzen versteckter Tweaks über ein Profil braucht keinen Expertenmodus mehr.

- V2-2: Einfach-Modus (Standard) mit Umschalter "Einfach / Experte", fünf Seiten (Start, Schneller, Gaming, Aufräumen, Privatsphäre) und "Zurück", vereinfachte Karten mit Wirkungswort und Risiko-Symbol, Suche über Deutsch und Englisch, höchstens 7 Karten mit "Mehr anzeigen", Hilfe "Was bedeutet das?", Glossar, Klartext-Änderungsleiste, Onboarding in 3 Schritten, Sprache und Design beim ersten Start aus Windows; `docs/USABILITY.md`.
- V2-1: Wirkungs-Audit aller Tweaks (`docs/IMPACT-AUDIT.md`), neue Pflichtfelder `impact`, `impactNote`, `measuredOn`, `plainTitleKey`, `plainDescKey` mit Linter-Regeln, Klartext-Titel und -Sätze für alle Tweaks in Deutsch und Englisch.

## [1.0.0-rc.1] - 2026-10-18

Release-Kandidat, nicht signiert. Offen vor 1.0.0: Name, Lizenz, Zertifikat, Update-Adresse, Rechtstexte, VM-Testmatrix (siehe `docs/DECISIONS.md`).

### Hinzugefügt
- Phase 15: Bereich "Über" mit Haftungshinweis, Datenschutz, Markenhinweis, Quellen und Drittlizenzen; `docs/PRIVACY.md`; `README.md`; Release-Skripte (`tools/release/Build-Release.ps1`, `Sign-Release.ps1`, `New-ThirdPartyNotices.ps1`), Inno-Setup-Skript `installer/AscendedTweaks.iss`, portable Version.
- Phase 1: Projektstruktur, Tweak-Datenmodell mit JSON-Schema und Linter, Engine mit Dry-Run, Sicherung (Backup-Set mit Prüfsummen und `.reg`-Export), Write-Ahead-Journal, Ledger, Verifikation, Rollback, Rücksetzen einzelner Tweaks mit Konfliktprüfung, Wiederaufnahme unterbrochener Änderungen.
- Executors für Registry-Werte und -Schlüssel, Dienste, Energiepläne und Whitelist-Befehle.
- Fünf Beispiel-Tweaks: PRIV-001, PRIV-013, PERF-016, UI-001, PERF-001.
- Test-Konsole `ascendedtweaks-cli.exe`, Werkzeuge `catalog-lint` und `registry-snapshot`, Integrationstest-Skript.
- Phase 14: Barrierefreiheits-Prüfung im Snapshot-Modus (Namen für Screenreader) mit behobenen Befunden, Hochkontrast über Windows-Systemfarben, Leistungsmessung `tests/perf/Measure-AppPerformance.ps1`, Testmatrix-Skript `tests/vm/Invoke-TestMatrix.ps1` mit Anleitung `docs/TEST-MATRIX.md`, Sicherheits-Review `docs/SECURITY-REVIEW.md`, zusätzliche Tests für Rücksetz-Zweige, Spielebibliothek und Wartung im Helfer.
- Phase 13: Kommandozeile mit `--profile`, `--export ps1|reg` und `--list --module`; Export von Profilen als PowerShell-Skript und `.reg`-Datei mit Rücksetz-Gegenstück; Einstellungen mit Software-Update auf Knopfdruck (Ed25519-signiertes Manifest, SHA-256, Authenticode; noch nicht eingerichtet), Protokolle öffnen und löschen, Hinweise zur Kommandozeile und Zurücksetzen der App-Einstellungen; Werkzeug `tools/update-sign`.
- Phase 12: Dashboard mit Systeminfos (Prozessor, Grafik mit Treiber und Videospeicher, Arbeitsspeicher, Datenträger, Windows, letzter Start), Zustandsanzeigen mit Erklärung ohne Gesamtwert und "Empfohlene Optimierung" mit Vorschau; CLI `--sysinfo`.
- Phase 11: Modul Updates mit Infotext, UPD-002 bis UPD-006 als dokumentierte Richtlinien, Pause (UPD-001, höchstens 35 Tage) und Zielversion (UPD-007) als erzeugte Tweaks im Expertenmodus, Link zu Windows Update (UPD-008); Profile mit fünf vorgefertigten Profilen, eigenen Profilen (speichern, umbenennen, löschen), Export und geprüftem Import (`.tpprofile`), Vorschau mit Nebenwirkungen und Vorher/Nachher-Vergleich mit Messwerten.
- Phase 10: Module Netzwerk und Datenträger mit NET-001 bis NET-003, NET-009 bis NET-011, eigenem DNS (NET-004), DISK-001, DISK-004 und DISK-007; Werkzeuge Ping- und Jitter-Test, DNS-Cache leeren, DoH-Prüfung, Netzwerk-Reset, ReTrim, Defragmentieren nur für HDD, SMART-Anzeige und Suche nach großen Dateien; Helfer-Befehle RunMaintenance und ReadDiskHealth; CLI `--volumes` und `--ping`.
- Phase 9: Debloat mit 26 Katalog-Apps, "Weitere Apps", gesperrten Systemapps, Entfernen für alle Benutzer im Helfer, optional Bereitstellungspaket, Wiederherstellung (Neuregistrierung oder Store-Seite); OneDrive-Deinstallation mit harten Vorbedingungen; Oberflächen-Tweaks UI-002 bis UI-011 und Widgets-Richtlinie DEB-027; Projekt AscendedTweaks.Appx (WinRT); CLI `--apps` und `--onedrive-check`.
- Phase 8: Modul Bereinigung mit 14 Kategorien, Scan vor dem Löschen, Auswahl, Papierkorb- oder Endgültig-Modus, handle-basiertes Löschen ohne Folgen von Abzweigungen, Sonderabläufe für Windows Update, vorherige Installation, Papierkorb und Komponentenspeicher; Helfer-Befehle ScanCleanup und RunCleanup; CLI `--clean-scan`.
- Phase 7: Modul Gaming mit 13 Katalog-Tweaks und den Pro-Spiel-Tweaks GAME-007, 008, 016; Aktionstyp RegistryListEntry; Spielprofile mit Epic-/Steam-Erkennung, Anti-Cheat-Erkennung und Start mit Priorität; Anti-Cheat-Prüfung (nur Anzeige); Messwerkzeug `tools/perf-measure` und Messprotokoll; CLI-Befehle `--games` und `--anticheat`.
- Phase 6: Modul Leistung mit 24 Tweaks (PERF-001 bis PERF-025 ohne 019) plus PERF-026 für einzelne Autostart-Einträge; Aktionstypen ScheduledTaskSetEnabled und SystemParametersInfo; Leistungsseite mit Bereichen Einstellungen, Autostart (mit Signaturprüfung und Bewertung), Dienste, Aufgaben und Prozesse; CLI-Befehl `--autostart`.
- Phase 5: Modul Datenschutz mit 22 Tweaks (PRIV-001 bis PRIV-024 ohne 004 und 007), Bereich `UserPolicy` für Benutzer-Richtlinien über den Helfer, Berechtigungs-Übersicht für Kamera, Mikrofon und Standort (nur Anzeige).
