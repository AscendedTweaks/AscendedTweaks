# Abnahmeberichte (Abschnitt 15)

Phase 1 steht im Chatverlauf vom 2026-10-17 und ist hier zusammengefasst. Ab Phase 2 wird jede Phase hier festgehalten.

## Phase 1: Engine, Sicherung, Revert, 5 Beispiel-Tweaks

Revert ja, Dry-Run unverändert ja, Beschreibungen geprüft (Host, nur HKCU), keine durchgefallenen Tweaks, Qualitätstor grün (123 Tests). Größte Schwäche (behoben): Neuer Batch trotz offenem unterbrochenem Batch, keine Sperre gegen zweite Instanz.

## Phase 2: Helfer, IPC, Rechtemodell, Protokoll, Wiederherstellungspunkt

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja | `ServiceTests.RevertAll_spans_both_stores`, `Machine_revert_asks_about_conflicts_through_the_helper`, `Failing_user_part_compensates_machine_part` (Snapshot nach Kompensation gleich vorher). |
| 2 | Dry-Run ändert nichts | Ja | `ServiceTests.Preview_and_status_never_start_the_helper`: 0 Schreibzugriffe, kein Helfer gestartet. CLI-Smoke-Test: `--apply PERF-001,PRIV-013 --dry-run` ohne UAC. |
| 3 | Beschreibungen stimmen | Keine neuen Tweaks | - |
| 4 | UI nach Design-System | Keine UI in Phase 2 | - |
| 5 | Durchgefallene Tweaks | Keine | - |
| 6 | Qualitätstor grün | Ja | `build.ps1 -Coverage`: 0 Warnungen, 152/152 Tests (Catalog 25, Engine 104, Ipc 23), Linter sauber, Format sauber. Abdeckung: Engine 91,7 %, Core 97,6 %, Ipc 85,6 %. |
| 7 | Größte Schwäche | Teilweise offen | Der echte UAC-Ende-zu-Ende-Pfad (zwei Prozesse, Helfer mit Admin-Rechten) ist nicht gelaufen, weil dafür ein UAC-Dialog bestätigt werden muss (offener Punkt 9 in DECISIONS). Behoben wurde ein Fehler, den die Tests gefunden haben: Bei abgewiesenem Client warf der Client eine rohe `IOException` statt einer verständlichen Meldung. |
| 8 | Annahmen | Siehe DECISIONS.md, Abschnitt "Phase 2" | `ApplyTweaks` als Batch, `RecoverInterrupted`, Helfer-Log in ProgramData, ein Wiederherstellungspunkt pro Gesamt-Batch, zweiter UAC-Dialog nach Leerlauf, WMI statt PowerShell. |
| 9 | Nächste Phase | Phase 3 | WPF-Rahmen mit Navigation, Design-Tokens, Gestaltungskatalog, Lokalisierung DE/EN, Versionscheck mit Lesemodus, Kontrastwerkzeug. |

Abgewiesene Fremd-Clients (Test): falsche Prozess-ID, Programm außerhalb des Installationsverzeichnisses, fremder Pipe-Server, falsche Protokollversion, wiederholter Zähler, kaputtes JSON, unbekannter Befehl, Rahmen über 1 MB.

## Phase 3: WPF-Rahmen, Design-System, Lokalisierung, Versionscheck

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Unverändert ja | Keine Engine-Änderung; Engine-Tests weiter grün. |
| 2 | Dry-Run ändert nichts | Unverändert ja | wie Phase 2 |
| 3 | Beschreibungen stimmen | Keine neuen Tweaks | - |
| 4 | UI nach Design-System | Ja | `docs/DESIGN.md` vor der UI-Arbeit gelesen. Screenshots unter `docs/screenshots/phase3` (Dunkel und Hell, 100 und 200 Prozent, schmales Fenster mit eingeklappter Navigation, vollständiger Gestaltungskatalog). `tools/contrast-check`: 102 Paare bestanden, inklusive getönter Flächen. Bei der Sichtprüfung gefundene und behobene Abweichungen: eingeklappte Navigation schnitt Symbole ab (Bildlaufleiste reservierte Platz), Symbol "HardDrive24" fehlt im Font (jetzt "Storage24"), Listen-Auswahl im WPF-UI-Standard mit zu wenig Kontrast und im hellen Theme unsichtbar (jetzt getönte Auswahl mit Akzentstrich). |
| 5 | Durchgefallene Tweaks | Keine | - |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 169/169 Tests (App 17, Catalog 25, Ipc 23, Engine 104), Linter, Kontrast und Format sauber. |
| 7 | Größte Schwäche | Behoben | Animationen fehlten. Jetzt: Seitenwechsel 200 ms Ease-Out, nur Opazität und 8 Punkte Versatz; aus, wenn Windows-Animationen aus sind. Offen bleibt (Phase 14): Narrator-Stichprobe und echter Durchlauf bei 200 Prozent Systemskalierung statt Rendering mit 192 dpi. |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 3" | WPF-UI nur für Fenster, Symbole und Standardstile; eigene Navigation; kein Mica; Mindestgröße 800 x 480; Token `OnAccent`; einheitliche Platzhalter; Screenshot-Modus nur in Debug. |
| 9 | Nächste Phase | Phase 4 | Tweak-Karte, Details-Panel, Änderungsleiste, Vorschau-, Fortschritts- und Ergebnisdialog, Expertenwarnung, Wiederherstellungsseite, Onboarding; die 5 Beispiel-Tweaks über die Oberfläche anwend- und rücksetzbar. |

## Phase 4: Karten, Details, Änderungsleiste, Dialoge, Wiederherstellung, Onboarding

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja | `WorkspaceTests.Toggle_off_reverts_and_asks_on_conflict` (mit Konfliktdialog), `Restore_page_lists_and_undoes_everything_then_compares_with_baseline` (Alles rückgängig plus Vergleich mit dem Ausgangszustand: keine Abweichung). |
| 2 | Dry-Run ändert nichts | Ja | `Preview_only_and_declined_preview_change_nothing`: 0 Schreibzugriffe bei reiner Vorschau und bei abgelehnter Vorschau. Die Vorschau ist immer der erste Schritt (`Apply_shows_preview_first_then_applies_and_verifies`). |
| 3 | Beschreibungen stimmen | Unverändert | Keine neuen Tweaks. Quellen-Texte auf Umlaute umgestellt, weil sie im Detailpanel sichtbar sind. |
| 4 | UI nach Design-System | Ja | DESIGN.md vor der Arbeit gelesen. Screenshots `docs/screenshots/phase4` (Karten, Detailpanel, Vorschau, Expertenwarnung Stufe 3, Onboarding Schritt 2, Wiederherstellung, 200 Prozent). Kontrastprüfung grün. Bei der Sichtprüfung behoben: Sperrgrund in der Karte wurde abgeschnitten; Detailpanel überdeckte Inhalt auch bei breitem Fenster; Konverter waren aus Vorlagen nicht erreichbar (Absturz beim ersten Start). |
| 5 | Durchgefallene Tweaks | Keine | - |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 184/184 Tests (App 32, Catalog 25, Ipc 23, Engine 104), Linter, Kontrast, Format. |
| 7 | Größte Schwäche | Behoben | Doppelte Rückfrage bei reinen Benutzer-Tweaks (Vorschau, danach "kein Wiederherstellungspunkt"). Jetzt steht der Hinweis in der Vorschau; nur ein fehlgeschlagener Punkt bei Maschinen-Tweaks fragt gesondert (`Failed_restore_point_for_machine_tweaks_is_asked_explicitly`). Offen: Bedienung von Hand (offener Punkt 10). |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 4" | Dialoge als Ebene, Ablaufreihenfolge, Vorschau als Zustimmung bei Benutzer-Tweaks, Sperre für "von Windows so eingestellt", Ausgangszustand nur lesend im Benutzer-Speicher, Detailpanel-Verhalten. |
| 9 | Nächste Phase | Phase 5 | Modul Datenschutz PRIV-001 bis PRIV-024 mit Prüfung über Microsoft-Dokumentation und Host-Test (nur HKCU). |

Die 5 Beispiel-Tweaks sind über die Oberfläche anwend- und rücksetzbar (Karten, Änderungsleiste, Detailpanel "Zurücksetzen", Wiederherstellungsseite). Geprüft über ViewModel-Tests mit echter Engine auf Fakes; Bedienung von Hand steht aus.

## Phase 5: Modul Datenschutz (PRIV-001 bis PRIV-024)

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja, für alle auf dem Host prüfbaren Tweaks | Host-Integrationstest PRIV-014 bis PRIV-019 (`tests/vm/report-host-26200-phase5.json`): Snapshot nach Revert gleich Ausgangszustand. Richtlinien-Tweaks gegen Fakes: `User_policy_runs_in_helper_against_the_users_hive_and_reverts`, `Failing_user_part_compensates_machine_and_policy_parts`. |
| 2 | Dry-Run ändert nichts | Ja | Schritt 3 des Host-Tests für alle sechs; Vorschau ohne Helferstart (`User_policy_...`: `HelperStarted` bleibt false). |
| 3 | Beschreibungen stimmen | Ja, mit Abweichungen dokumentiert | Jede Quelle gegen Microsoft Learn geprüft. Vier Tweaks auf dokumentierte Richtlinien umgestellt (PRIV-006, 009, 012, 024), PRIV-008 ohne undokumentierten Bing-Wert. Alle Richtlinien-Tweaks nennen jetzt "Von deiner Organisation verwaltet" als Nebenwirkung. |
| 4 | UI nach Design-System | Ja | Berechtigungs-Übersicht als Karte mit aufklappbaren Gruppen, nur Anzeige, Knopf zu den Windows-Einstellungen. Screenshots `docs/screenshots/phase5` (hell, dunkel, 200 Prozent, schmal) mit Beispieldaten statt der echten App-Liste. |
| 5 | Durchgefallene Tweaks | PRIV-004, PRIV-007, BingSearchEnabled aus PRIV-008 | `REJECTED-TWEAKS.md`: Dienste stehen bereits auf "Manuell" (Placebo); Bing-Wert undokumentiert. |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 193/193 Tests (App 34, Catalog 26, Ipc 27, Engine 106), Linter (25 Tweaks), Kontrast, Format. |
| 7 | Größte Schwäche | Offen | 14 der 22 Datenschutz-Tweaks (11 Machine, 3 UserPolicy) brauchen Admin-Rechte und sind nur gegen Fakes getestet, nicht auf einem echten System. Grund: kein UAC-Test ohne den Nutzer, keine VM (offene Punkte 1 und 9). Außerdem sind PRIV-017 bis 019 nur auf Registry-Ebene belegt (offener Punkt 11). |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 5" | Bereich UserPolicy über HKU\SID, ein Bereich pro Tweak und Auftrag, dokumentierte Richtlinie statt interner Werte, Build-Grenzen für Copilot und Recall, Berechtigungs-Übersicht nur lesend. |
| 9 | Nächste Phase | Phase 6 | Modul Leistung PERF-001 bis PERF-026, Autostart-Manager, Dienste-Manager, Aufgaben, Prozessübersicht. |

Wichtiger Befund aus dem Host-Test: `HKCU\Software\Policies` ist auch in der eigenen Benutzer-Hive nur für Administratoren beschreibbar. Die ursprüngliche Einordnung von PRIV-008, 010 und 012 als Benutzer-Tweak wäre auf jedem echten System mit "Zugriff verweigert" gescheitert. Der neue Bereich UserPolicy löst das; der Linter erzwingt ihn.

## Phase 6: Modul Leistung (PERF-001 bis PERF-026, Autostart, Dienste, Aufgaben, Prozesse)

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja | Host-Test PERF-003 bis 007 und PERF-026 (Benutzer-Eintrag): Snapshot nach Revert gleich Ausgangszustand. Fakes: Aufgaben, Systemparameter, Ruhezustand, Rollback bei fehlschlagender Aufgabe (`Phase6ExecutorTests`), Autostart über Workspace und Helfer (`WorkspaceTests`). |
| 2 | Dry-Run ändert nichts | Ja | Schritt 3 des Host-Tests; Vorschau ist auch für Autostart-Einträge der erste Schritt (`Autostart_entry_is_disabled_after_preview_and_enabled_again`). |
| 3 | Beschreibungen stimmen | Ja | Kein Text verspricht Geschwindigkeit ohne Messung; Tweaks mit Geschwindigkeitsversprechen sind "Umstritten" und ausgeblendet. Belegt nur dokumentierte, beobachtbare Änderungen (PERF-005, 006, 008, 009, 015) und PERF-026 (Task-Manager-Mechanismus, offener Punkt 13). |
| 4 | UI nach Design-System | Ja | Leistungsseite mit Bereichsauswahl (Segmente), Autostart mit Bewertung und Begründung, Dienste und Aufgaben aus dem Katalog mit Standard- und Ist-Wert, Prozessübersicht ohne "RAM bereinigen". Screenshots `docs/screenshots/phase6` mit Beispieldaten. |
| 5 | Durchgefallene Tweaks | PERF-019 | `REJECTED-TWEAKS.md`: RemoteRegistry ist bereits deaktiviert (Placebo). PERF-021 und 022 bleiben, melden auf Build 26200 aber "nicht verfügbar". |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 240/240 Tests (App 38, Catalog 28, Ipc 27, Engine 147), Linter (47 Tweaks), Kontrast, Format. |
| 7 | Größte Schwäche | Offen | Von 24 Leistungs-Tweaks sind 19 Machine-Tweaks nur gegen Fakes getestet (UAC, offener Punkt 9), und ohne Messprotokoll sind 19 "Umstritten" (offener Punkt 12). Der Host-Test fand einen echten Fehler (CLI kannte die Autostart-IDs bei `--status`/`--revert` nicht); behoben, Originalwert sofort wiederhergestellt. |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 6" | Nachweisregel für Leistungs-Tweaks, neue Aktionstypen, SPI statt Registry, GetPwrCapabilities, Autostart als erzeugter Tweak, Bewertungsregeln, Katalog-only für Dienste und Aufgaben. |
| 9 | Nächste Phase | Phase 7 | Modul Gaming (GAME-001 bis GAME-021, Spielprofile, Anti-Cheat-Check, Priorität-Starter). |

## Phase 7: Modul Gaming (GAME-001 bis GAME-021, Spielprofile, Anti-Cheat-Prüfung, Priorität-Starter)

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja | Host-Test GAME-003, 006, 009, 010, 012, 013 (`tests/vm/report-host-26200-phase7.json`). Fakes: Listen-Einträge setzen und unabhängig zurücksetzen (`Two_tweaks_sharing_one_value_revert_independently`, `Existing_foreign_entries_survive_apply_and_revert`), Pro-Spiel-Tweaks, Maus und Eingabehilfen (`Phase7Tests`), Profil übernehmen, abwählen und entfernen bis zum Ausgangszustand (`Game_profile_applies_and_reverts_per_game_settings_through_preview`). |
| 2 | Dry-Run ändert nichts | Ja | Schritt 3 des Host-Tests; Profile wenden nur nach Vorschau an. Anti-Cheat-Prüfung schreibt nichts (`Anti_cheat_view_shows_states_without_changing_anything`). |
| 3 | Beschreibungen stimmen | Ja, mit Korrekturen | GAME-003 schaltet nicht Win+G ab (Text korrigiert); GAME-004 auf dokumentierte Richtlinie umgestellt (Home ausgenommen); GAME-008 auf "Umstritten", weil nicht dokumentiert. Kein Text verspricht FPS ohne Messung. |
| 4 | UI nach Design-System | Ja | Pflicht-Hinweisbox Anti-Cheat im Wortlaut des Auftrags oben auf der Gaming-Seite, Bereiche Einstellungen, Spielprofile, Anti-Cheat-Prüfung. Screenshots `docs/screenshots/phase7` mit Beispieldaten. |
| 5 | Durchgefallene Tweaks | GAME-014 | Xbox-Dienste stehen bereits auf "Manuell" (Placebo). GAME-017 und 018 folgen mit Phase 8 und 10. |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 279/279 Tests (App 42, Catalog 28, Ipc 27, Engine 182), Linter (60 Tweaks), Kontrast, Format. |
| 7 | Größte Schwäche | Teilweise behoben | Der erste Host-Test von GAME-012/013 lief gegen einen bereits angewendeten Live-Zustand, und meine Notfall-Rücksetzung hat die Kürzel kurz wieder eingeschaltet. Behoben: Live-Zustand exakt wiederhergestellt, Vorbedingungsprüfung im Integrationsskript. Offen bleibt, dass keine Leistungsaussage gemessen ist (offener Punkt 12) und Spielstart mit Priorität nicht von Hand beobachtet wurde (offener Punkt 14). |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 7" | Aktionstyp RegistryListEntry, erzeugte Pro-Spiel-Tweaks, SPI für Maus und Eingabehilfen, dokumentierte Richtlinie für GAME-004, Launcher-Erkennung, Fortnite-Spielprozess, Anti-Cheat-Prüfung ohne Admin, Messprotokoll. |
| 9 | Nächste Phase | Phase 8 | Modul Bereinigung (CLEAN-001 bis CLEAN-014) mit Scan, Auswahl, Papierkorb-Modus und Pfad-Sicherheitstests. |

## Phase 8: Modul Bereinigung (CLEAN-001 bis CLEAN-014)

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Nicht anwendbar, offen kommuniziert | Löschen ist nicht über die Engine umkehrbar (Abschnitt 6.5); Papierkorb-Modus für Benutzerordner, Hinweis in Seite, Rückfrage und Ergebnis. Was rückgängig gemacht werden muss, wird es: Update-Dienste exakt wiederhergestellt (`Windows_update_services_are_restored_exactly`), StateFlags der Datenträgerbereinigung exakt wiederhergestellt (`Previous_installations_runner_restores_all_state_flags`). |
| 2 | Dry-Run ändert nichts | Ja | Scan ohne Schreibzugriffe (`Scan_counts_old_files_and_never_follows_junctions`), Bereinigen erst nach Scan und Rückfrage (`Nothing_can_be_cleaned_before_a_scan`, `Declined_confirmation_deletes_nothing`). Host-Scan aller Kategorien nur lesend. |
| 3 | Beschreibungen stimmen | Ja | Texte nennen, was gelöscht wird und was nie (Cookies, Passwörter, Verlauf), und die Folgen; Shader-Cache ausdrücklich ohne FPS-Versprechen. |
| 4 | UI nach Design-System | Ja | Warnbanner, Scan, Auswahl, Modus, Admin- und Modus-Badges je Kategorie, gefährliche Aktion als Danger-Schaltfläche. Screenshots `docs/screenshots/phase8`. |
| 5 | Durchgefallene Tweaks | Keine | - |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 317/317 Tests (App 48, Catalog 28, Ipc 29, Engine 212), Linter, Kontrast, Format. |
| 7 | Größte Schwäche | Behoben bzw. offen benannt | Behoben: Papierkorb wäre vorausgewählt gewesen (jetzt nie), Papierkorb-Größe falsch gelesen (Strukturausrichtung). Offen: Das Löschen in Systemordnern über den Helfer und die Sonderabläufe (cleanmgr, DISM, Update-Dienste) sind nur gegen Fakes getestet (UAC, offener Punkt 9); der Papierkorb-Modus nur über die Engine-Logik, nicht mit echtem `IFileOperation` (sonst landen Testdateien im Papierkorb des Nutzers). |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 8" | Kein Tweak, Auswahl pro Kategorie ohne Pfade über IPC, Systemordner nur endgültig, Schätzung ohne UAC, handle-basiertes Löschen, Dienst- und StateFlags-Wiederherstellung, Vorauswahl. |
| 9 | Nächste Phase | Phase 9 | Modul Debloat (Appx, OneDrive-Schutz, Widgets) und Oberfläche (UI-001 bis UI-011) mit getesteter Wiederherstellung entfernter Apps. |

## Phase 9: Modul Debloat und Oberfläche (DEB-001 bis DEB-028, UI-001 bis UI-011)

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja für Tweaks, eingeschränkt für Apps (offen kommuniziert) | UI-Tweaks: Host-Test, Ausgangszustand nach Revert. Apps: Wiederherstellungsweg Neuregistrierung echt geprüft (`AppxIntegrationTests`), Rückfall Manifest und Store-Seite gegen Fakes (`Restorer_registers_then_falls_back_to_manifest_then_store`), Wiederherstellungsseite (`Restore_page_lists_removed_apps_and_restores`). Nicht jede Wiederherstellung ist garantiert; das steht in Seite, Rückfrage und Wiederherstellungsseite. |
| 2 | Dry-Run ändert nichts | Ja | Schritt 3 des Host-Tests; Apps und OneDrive nur nach ausdrücklicher Rückfrage, nichts vorausgewählt. Host-Prüfungen von Apps und OneDrive nur lesend. |
| 3 | Beschreibungen stimmen | Ja, mit Korrektur | UI-005 auf Build 26200 blockiert: auf Builds bis 22631 begrenzt, Text verweist auf DEB-027. App-Texte nennen die Folgen (zum Beispiel Xbox-App: Game Pass). |
| 4 | UI nach Design-System | Ja | Bereiche Apps, Oberfläche und Widgets, OneDrive; Gruppen nach Kategorien, Schloss und Begründung für Systemapps, Warnhinweis für Treiber- und Sicherheits-Apps. Screenshots `docs/screenshots/phase9`. |
| 5 | Durchgefallene Tweaks | Keine abgelehnt | UI-005 eingeschränkt (siehe REJECTED-TWEAKS.md). |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 344/344 Tests (App 53, Catalog 28, Ipc 30, Engine 233), Linter (71 Tweaks), Kontrast, Format. |
| 7 | Größte Schwäche | Offen benannt | Das Entfernen für alle Benutzer und die Wiederherstellung nach echter Entfernung sind nicht auf einem echten System gelaufen: Es braucht UAC, und eine Entfernung ist auf dem Rechner des Nutzers nicht garantiert umkehrbar. Das gehört in die VM (offene Punkte 1 und 9). Behoben in dieser Phase: Integrationsskript brach bei Fehlerausgaben ab. |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 9" | Eigener Ablauf statt AppxRemove-Aktion, Helfer-Befehl RemoveApps, Wiederherstellungsreihenfolge, WinRT-Projekt, Sperrliste, keine Vorauswahl, OneDrive-Sperrgründe, UI-Tweaks "Umstritten" bis zur Sichtprüfung. |
| 9 | Nächste Phase | Phase 10 | Module Netzwerk und Datenträger (NET-001 bis NET-011, DISK-001 bis DISK-007). |

## Phase 10: Module Netzwerk und Datenträger (NET-001 bis NET-011, DISK-001 bis DISK-007)

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja (gegen Fakes), auf dem Host nicht ausgeführt | Alle NET- und DISK-Tweaks außer DISK-004 sind Maschinen-Tweaks und brauchen UAC. Rücksetzen gemischter DNS-Zustände, DHCP, Energiesparen pro Adapter, Autotuning, TRIM und reservierter Speicher sind gegen Fakes getestet (`NetworkDiskTests`). Wartungsaktionen (Reset, ReTrim, Defragmentieren) sind bewusst keine Tweaks und werden als nicht umkehrbar angekündigt. |
| 2 | Dry-Run ändert nichts | Ja | Vorschau für Tweaks; Erkennung auf dem Host nur lesend. Werkzeuge ändern nur nach Knopfdruck, Reset, ReTrim und Defragmentieren nur nach Rückfrage. |
| 3 | Beschreibungen stimmen | Ja, mit Ergänzung | Hinweis ergänzt, dass VPN-Programme DNS-Einstellungen überschreiben können (Host-Befund). Defragmentieren ist bei unbekanntem Medium gesperrt, der Text sagt das. |
| 4 | UI nach Design-System | Ja | Bereiche Einstellungen, Eigener DNS und Werkzeuge bzw. Einstellungen, Laufwerke und Große Dateien; Gefahren-Knopf für den Netzwerk-Reset; Screenshots `docs/screenshots/phase10` (hell, dunkel, 100 und 200 Prozent, schmal). |
| 5 | Durchgefallene Tweaks | Keine | NET-011, DISK-004 und DISK-007 "Umstritten". |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 385/385 Tests (App 67, Catalog 28, Ipc 30, Engine 260), Linter (80 Tweaks), Kontrast, Format. |
| 7 | Größte Schwäche | Offen benannt | Kein NET- oder DISK-Maschinen-Tweak und keine Wartungsaktion ist auf einem echten System gelaufen: Es braucht UAC, und ein Netzwerk-Reset würde die VPN-Einrichtung des Entwicklungsrechners stören. Das gehört in die VM (offene Punkte 1 und 9). |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 10" | netsh statt PowerShell, AdapterState, NET-004 als erzeugter Tweak, DoH nur geführt, Wartung als feste Helfer-Aktionen, Defragmentieren bei unbekanntem Medium gesperrt, SMART über den Helfer, große Dateien nur Anzeige, "Admin nötig" bei der Erkennung. |
| 9 | Nächste Phase | Phase 11 | Module Updates (UPD-001 bis UPD-008) und Profile. |

## Phase 11: Module Updates (UPD-001 bis UPD-008) und Profile

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja (gegen Fakes), auf dem Host nicht ausgeführt | Pause anwenden und zurücksetzen mit Erhalt fremder Werte (`Pause_applies_and_reverts_to_the_previous_values`); Profil anwenden und mit Zielzustand "nicht angewendet" wieder zurücksetzen (`Applying_a_profile_shows_preview_and_a_before_after_comparison_then_reverts_own_changes`). Alle UPD-Tweaks sind Maschinen-Tweaks (UAC) und auf dem Home-Rechner bis auf UPD-001 nicht verfügbar. |
| 2 | Dry-Run ändert nichts | Ja | Profil-Vorschau auf der Seite plus Vorschau-Dialog; Import wendet nichts an (Test). Host-Prüfung nur lesend. |
| 3 | Beschreibungen stimmen | Ja | Infotext "Windows Update wird nie dauerhaft abgeschaltet"; UPD-003 und UPD-005 nennen die Sicherheitsfolgen, UPD-007 das Supportende. |
| 4 | UI nach Design-System | Ja | Updates mit Bereichen Übersicht und Richtlinien; Profile mit Auswahl, Vorschauliste mit Nebenwirkungen, Vorher/Nachher-Tabelle und Messwerten. Screenshots `docs/screenshots/phase11`. |
| 5 | Durchgefallene Tweaks | Keine abgelehnt | Abweichungen in REJECTED-TWEAKS.md: UPD-006 als Richtlinie, UPD-005 "Vorsicht", UPD-001 und UPD-007 "Umstritten". |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 436/436 Tests (App 73, Catalog 53, Ipc 30, Engine 280), Linter (85 Tweaks), Kontrast, Format. |
| 7 | Größte Schwäche | Offen benannt | Auf dem Entwicklungsrechner (Home) ist von den Update-Tweaks nur die Pause verfügbar, und auch die braucht UAC. Ob Windows die Werte so umsetzt wie beschrieben, zeigt erst die VM mit Pro und Home (offene Punkte 9 und 15). |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 11" | Editionen, UPD-006 als Richtlinie, UPD-005 mit AUOptions, Pause und Zielversion als erzeugte Tweaks, Profile nur mit Tweaks, Profil-Zusammensetzung, Überspringen versteckter und fremder Tweaks, Messwerte als Momentaufnahme, Speicherort, erlaubte IDs im Import. |
| 9 | Nächste Phase | Phase 12 | Dashboard. |

## Phase 12: Dashboard

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja | Das Dashboard ändert selbst nichts; "Empfohlene Optimierung" nutzt den normalen Ablauf mit Sicherung und Rücksetzen (`Recommend_always_opens_the_preview_and_changes_nothing_when_declined`). |
| 2 | Dry-Run ändert nichts | Ja | Vorschau abgelehnt: kein Wert geändert (Test). Systeminfos auf dem Host nur gelesen. |
| 3 | Beschreibungen stimmen | Ja | Jede Anzeige mit Erklärung, kein Gesamtwert; Anti-Cheat-Satz in der allgemeinen Form aus Abschnitt 6.1/6.3; Startdauer ehrlich als "nur mit Administratorrechten lesbar". |
| 4 | UI nach Design-System | Ja | Karten Empfehlung, Zustand, System; Screenshots `docs/screenshots/phase12` (Beispieldaten statt echter Hardware). |
| 5 | Durchgefallene Tweaks | Keine | Keine neuen Tweaks. |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 444/444 Tests (App 81, Catalog 53, Ipc 30, Engine 280), Linter (85 Tweaks), Kontrast, Format. |
| 7 | Größte Schwäche | Offen benannt | Die Startdauer ist ohne Administratorrechte nicht lesbar. Die WMI-Abfragen sind nur auf einem Rechner (AMD, NVMe, Windows 11 Home) geprüft; Intel- und NVIDIA-Systeme, Notebooks und Windows 10 gehören in die Testmatrix (offener Punkt 1). |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 12" | Quellen ohne PowerShell, Startdauer über wevtutil, Neustart-Hinweise, Defender und Virenschutz-Namen, Spielmodus-Standard, Datenträger-Zuordnung, DNS aus der Empfehlung ausgenommen. |
| 9 | Nächste Phase | Phase 13 | Kommandozeile, Export, Einstellungen, Software-Update. |

## Phase 13: Kommandozeile, Export, Einstellungen, Software-Update

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja | Export: Rücksetz-Skript und Rücksetz-`.reg` stellen den Stand beim Export her; Host-Rundlauf PRIV-013, 014, 016 mit Vorher/Nachher-Vergleich per `reg query`: identisch (`ExportTests`, Abschnitt "Phase 13" in SOURCES.md). CLI `--profile` setzt nur eigene Änderungen zurück. |
| 2 | Dry-Run ändert nichts | Ja | `--profile <name> --dry-run` (Host, ohne UAC); Export liest nur (`_h.TotalWrites == 0` im Test). |
| 3 | Beschreibungen stimmen | Ja | Update-Prüfung sagt ehrlich "noch nicht eingerichtet"; Skripte nennen nicht exportierbare Tweaks mit Grund; Hinweise zu Editionen und Benutzer-Richtlinien im Skript. |
| 4 | UI nach Design-System | Ja | Einstellungen mit Software-Update, Protokollen, Kommandozeile, Zurücksetzen; Profile mit "Als Skript". Screenshots `docs/screenshots/phase13`. |
| 5 | Durchgefallene Tweaks | Keine | Keine neuen Tweaks. |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 467/467 Tests (App 86, Catalog 53, Ipc 30, Engine 298), Linter (85 Tweaks), Kontrast, Format. |
| 7 | Größte Schwäche | Offen benannt | Die Update-Kette (echtes Manifest, echter Installer, Authenticode mit echtem Zertifikat) ist nur gegen Fakes getestet, weil Adresse, Schlüssel und Zertifikat fehlen (offene Punkte 4 und 7). Der Skript-Export für Maschinen-Tweaks ist nur syntaktisch geprüft (PowerShell-Parser), nicht ausgeführt (offener Punkt 16). |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 13" | Export-Umfang und Grenzen, Admin-Prüfung statt Rechteanforderung, leere Schlüssel, CLI-Profile, Ed25519 über BouncyCastle, Update nicht eingerichtet, Zurücksetzen der App, Protokolle löschen. |
| 9 | Nächste Phase | Phase 14 | Testmatrix, Barrierefreiheit, Leistung der App, Sicherheitsprüfung. |

## Phase 14: Testmatrix, Barrierefreiheit, Leistung, Sicherheit

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja (Tests), Matrix offen | Rücksetz-Pfade der Engine zu 100 Prozent abgedeckt (Coverlet, alle Testprojekte). VM-Matrix nicht ausgeführt (keine VM); Skript und Anleitung liegen bereit. |
| 2 | Dry-Run ändert nichts | Ja | Unverändert durch alle bisherigen Tests; Leistungsmessung bedient nichts in der App. |
| 3 | Beschreibungen stimmen | Ja | Keine neuen Tweaks; Ping-Ziele jetzt wie im Auftrag nur die drei festen Server. |
| 4 | UI nach Design-System | Ja, mit Korrekturen | Barrierefreiheits-Prüfung: 146 Befunde behoben, danach 0 bei 3437 Elementen; Hochkontrast über Systemfarben. Screenshots `docs/screenshots/phase14`. Manuelle Tastatur- und Narrator-Prüfung offen (offener Punkt 10). |
| 5 | Durchgefallene Tweaks | Keine | |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 474/474 Tests (App 86, Catalog 53, Ipc 31, Engine 304), Linter (85 Tweaks), Kontrast, Format. Abdeckung Core 98,0, Engine 89,6 Prozent. |
| 7 | Größte Schwäche | Offen benannt | Die Testmatrix in VMs (Abschnitt 11.2) ist nicht gelaufen, und der Arbeitssatz der App liegt mit rund 200 MB über der Grenze von 150 MB (privater Speicher 105 bis 114 MB). Beides ist gemessen und dokumentiert, nicht schöngerechnet. |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 14" | Automatische Barrierefreiheits-Prüfung, Hochkontrast über Systemfarben, Leistungsbefund mit Vorschlägen. |
| 9 | Nächste Phase | Phase 15 | Installer, portable Version, Signierung, Antivirus-Test, Dokumentation, Rechtliches, Release-Kandidat. |

## Phase 15: Installer, portable Version, Signierung, Dokumentation, Rechtliches, Release-Kandidat

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Revert funktioniert | Ja (unverändert) | Keine neue Änderungslogik; der Installer erklärt vor der Deinstallation, wie man zurücksetzt, und löscht Sicherungen nur auf Wunsch. |
| 2 | Dry-Run ändert nichts | Ja | Die veröffentlichte Kommandozeile liest nur (`--version`, `--list --module DISK` aus `artifacts\publish`). |
| 3 | Beschreibungen stimmen | Ja | Datenschutztext entspricht dem Verhalten: keine Verbindungen beim Start und im Leerlauf (Messung). |
| 4 | UI nach Design-System | Ja | Bereich "Über" in den Einstellungen; Screenshots `docs/screenshots/phase15`. |
| 5 | Durchgefallene Tweaks | Keine | |
| 6 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 477/477 Tests (App 86, Catalog 53, Ipc 31, Engine 307), Linter (85 Tweaks), Kontrast, Format. Release-Build: `artifacts\TweakPilot-1.0.0-rc.1-portable-win-x64.zip` (93,8 MB, SHA-256 in `artifacts\SHA256SUMS.txt`). |
| 7 | Größte Schwäche | Offen benannt | Der Release-Kandidat ist weder signiert noch als Installer gebaut noch auf Viren geprüft, und die VM-Testmatrix fehlt. Das braucht Entscheidungen und Handgriffe des Auftraggebers (offene Punkte 1, 2 bis 7, 9, 18). |
| 8 | Annahmen | DECISIONS.md, Abschnitt "Phase 15" | Ein Ordner für alle Programme, Speicherorte der portablen Version, Inno Setup mit Rückfrage zu Sicherungen, Signierwege, automatische Drittlizenzen. |
| 9 | Nächste Phase | Keine | Alle 15 Phasen sind umgesetzt; offen sind die Schritte, die der Auftraggeber erledigen oder entscheiden muss. |

## V2-1: Audit und Schema

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 483/483 Tests (App 86, Catalog 59, Ipc 31, Engine 307), Linter (85 Tweaks), Kontrast, Format. |
| 2 | Revert für Neues getestet | Nicht nötig | Keine neue Änderungslogik; nur Metadaten und Texte. Bestehende Revert-Tests unverändert grün. |
| 3 | Dry-Run unverändert | Ja | Engine unverändert. |
| 4 | Beschreibungen stimmen | Ja | Jede Einstufung mit einer Zeile Begründung in `docs/IMPACT-AUDIT.md`; "Spürbar" nur mit Beleg (`measuredOn`), keine FPS-Versprechen. |
| 5 | UI nach Regeln 2.3 | Noch nicht betroffen | Klartexte liegen vor (Titel höchstens 5 Wörter, Satz höchstens 90 Zeichen, vom Linter geprüft); die Oberfläche folgt in V2-2. |
| 6 | Durchgefallene Tweaks | Keine | Nichts gelöscht; 52 Tweaks "Unklar" und nur im Experten-Modus. |
| 7 | Änderungen am echten Rechner | Keine in V2-1 | Vor V2 hat der Nutzer ausdrücklich GAME-009, GAME-010, GAME-012 und GAME-013 (HKCU) anwenden lassen; sie sind aktiv und über die Wiederherstellung zurücksetzbar. |
| 8 | Größte Schwäche | Offen benannt | Nur 4 Tweaks sind "Spürbar"; mehr echte Wirkung bringen erst die neuen Tweaks in V2-4 (Autostart-Assistent, Energie, Browser). |
| 9 | Annahmen | DECISIONS.md, Abschnitt "V2-1" | Messbeleg-Regel, Einstufung von Datenschutz-Tweaks als "Klein". |
| 10 | Nächste Phase | V2-2 | Einfach-Layout mit Modus-Schalter, 5 Seiten, vereinfachten Karten, Suche, Start- und Zurück-Seite, Onboarding in 3 Schritten. |

## V2-2: Einfach-Layout

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 489/489 Tests (App 92, Catalog 59, Ipc 31, Engine 307), Linter, Kontrast, Format. |
| 2 | Revert für Neues getestet | Ja | `Back_page_lists_own_changes_and_undoes_one` (einzeln und alles), Engine unverändert. |
| 3 | Dry-Run unverändert | Ja | Presets und Karten laufen über den bestehenden Vorschau-Dialog. |
| 4 | Beschreibungen stimmen | Ja | Karten zeigen die geprüften Klartexte aus V2-1; Hilfe-Texte höchstens 3 Sätze; kein FPS-Versprechen. |
| 5 | UI nach 2.3 und DESIGN.md | Teilweise | Start, Schneller, Gaming, Privatsphäre, Zurück: ja (eine Hauptaktion, Wirkungswort, ein Risiko-Symbol, 7 Karten plus "Mehr anzeigen", Suche, Schrift 15, Klickflächen 40). Aufräumen: nein, noch Experten-Ansicht (Umbau mit V2-6). Kontrast grün, 200 Prozent und schmal in `artifacts/screens-v2` (44 Bilder), Barrierefreiheits-Prüfung 0 Befunde bei 5010 Elementen. |
| 6 | Durchgefallene Tweaks | Keine | |
| 7 | Änderungen am echten Rechner | Keine | Screenshots im Snapshot-Modus mit Beispieldaten; nichts angewendet, nichts gelöscht. |
| 8 | Größte Schwäche | Als offener Punkt | Die Seite "Aufräumen" ist im Einfach-Modus noch nicht vereinfacht; sie wird in V2-6 zusammen mit den großen Dateien umgebaut. |
| 9 | Annahmen | DECISIONS.md, Abschnitt "V2-2" | Ein Modus-Schalter für alles, Seiteninhalte, Presets vorläufig aus den Profilen, Einstellungen aus Windows, Klartext-Leiste. |
| 10 | Nächste Phase | V2-3 | Sieben Presets mit Stufen Sanft/Standard/Stark, Konfliktregeln und "Preset rückgängig". |

Klickzahlen (`docs/USABILITY.md`): Gaming-Preset 3, einen Tweak zurücknehmen 3, Müll aufräumen 3.

## V2-3: Presets

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 520/520 Tests (App 97, Catalog 66, Ipc 31, Engine 326), Linter, Kontrast, Format. |
| 2 | Revert für Neues getestet | Ja | `Undoing_preset_a_keeps_what_preset_b_set_and_preview_names_the_earlier_preset` (A, B, A rückgängig: B bleibt), `Result_dialog_undo_takes_back_exactly_this_run`, `Undoing_preset_a_keeps_everything_preset_b_set_or_wants`, `Later_preset_wins_and_reverting_drops_earlier_claims`. |
| 3 | Dry-Run unverändert | Ja | Jedes Preset läuft über die Vorschau "Das wird geändert" (gruppiert in Klartext, technische Tabelle aufklappbar); `Declined_preview_changes_nothing`. Aufräumen scannt zuerst und zeigt Größen. |
| 4 | Beschreibungen stimmen | Ja | Preset-Texte nennen keine FPS-Zahlen; Wettkampf sagt "nie Sicherheitsfunktionen"; Notebook Akku sagt, dass der Energieplan beim Standard bleibt. |
| 5 | UI nach 2.3 und DESIGN.md | Ja, mit Annahme | Raster mit 2 Spalten, Symbol, Name, ein Satz, Anzahl und Neustart, Stufen Sanft/Standard/Stark mit Anzahl, Standard vorausgewählt, eine Hauptaktion. Karte etwa 230 statt 160 Punkte hoch (siehe DECISIONS). Screenshots in `artifacts/screens-v23` (hell, dunkel, 100 und 200 Prozent, schmal), Barrierefreiheits-Prüfung 0 Befunde bei 5163 Elementen. |
| 6 | Durchgefallene Tweaks | Keine | Keine neuen Tweaks in dieser Phase. |
| 7 | Änderungen am echten Rechner | Keine | Screenshots und `preset-plans.txt` lesen nur den Zustand; nichts angewendet, nichts gelöscht. Preset-Zuordnung im Snapshot in einer temporären Datei. |
| 8 | Größte Schwäche | Als Annahme | "Preset rückgängig" stützt sich auf eine eigene Datei der App statt auf den Ledger der Engine (Engine bleibt unverändert). Wer Tweaks im Experten-Modus einzeln zurücksetzt, behält dort einen veralteten Anspruch; er wird beim nächsten "Preset rückgängig" übersprungen, weil der Tweak nicht mehr im Ledger steht. Wettkampf-Extras aus 4.1 fehlen noch bis V2-4. |
| 9 | Annahmen | DECISIONS.md, Abschnitt "V2-3" | Stufen-Regel, Kandidaten-Listen, Zuordnung in eigener Datei, späteres Preset gewinnt, Aufräumen-Stufen, eigene Presets ohne Stufen, Kartenhöhe, Text bei 0 Änderungen. |
| 10 | Nächste Phase | V2-4 | Neue wirksame Tweaks (Autostart-Assistent, Browser im Hintergrund, Energie-Feintuning, weitere aus 5.2) mit Prüfung und Einsortierung über die Stufen-Regel. |

Stufen auf dem echten Katalog (alle Ziele noch offen, ohne Akku): Sicher & Schnell 3/8/8, Gaming 2/5/6, Gaming Wettkampf 3/6/7, Privatsphäre 0/12/17, Büro ruhig 2/6/8 (`Levels_pick_a_growing_number_of_entries_from_the_real_catalog`).

## V2-4: Neue wirksame Tweaks

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 545/545 Tests (App 99, Catalog 66, Ipc 31, Engine 349), Linter "Katalog sauber: 94 Tweaks", Kontrast, Format. |
| 2 | Revert für Neues getestet | Ja (Fakes) | `Power_setting_is_written_to_the_active_plan_and_restored_in_that_plan_after_a_plan_switch`, `Energy_efficient_ethernet_only_counts_adapters_whose_driver_offers_it`, `Defender_scan_load_changes_only_that_value_and_is_unavailable_when_a_policy_sets_it`; BROW über den bestehenden Registry-Executor. Echte Ausführung in VM zu prüfen (Admin). |
| 3 | Dry-Run unverändert | Ja | Vorschau ändert nichts (`WriteCount` 0 im Energie-Test); Bereinigung scannt zuerst. |
| 4 | Beschreibungen stimmen | Ja | Jede Behauptung mit Quelle (Microsoft Learn: Edge-Richtlinien, MSFT_MpPreference, Set-MpPreference, INF-Schlüsselwörter *EEE, PowerWriteACValueIndex, Disk settings, ASPM; Chromium-Richtliniendefinition; Google-Hilfe 9131254). Pflichthinweise "Wird von deiner Organisation verwaltet", "Schutz bleibt an", "Nur auf PCs ohne Akku". Nicht belegte Aussagen gestrichen (Spotify-Downloads). |
| 5 | UI nach 2.3 und DESIGN.md | Ja | Neue Karten nutzen die vorhandenen Vorlagen; Autostart zeigt Klartext und "Braucht gerade ... MB", Prozesse Klartext und Sprung. Screenshots in `artifacts/screens-v24` (170 Bilder), Barrierefreiheits-Prüfung 0 Befunde bei 5381 Elementen. |
| 6 | Durchgefallene Tweaks | Siehe `REJECTED-TWEAKS.md` | Neues Teams, Spotify `Data`, ganze Launcher-Ordner, Browser ohne Richtlinie; POWR-001/002 nur "Umstritten". |
| 7 | Änderungen am echten Rechner | Keine | Nur lesend: `powercfg /aliases` und `/query`, CIM-Abfrage `*EEE`, Run-Schlüssel, Ordnerlisten der Caches, `tweakpilot-cli --status` und `--clean-scan`. |
| 8 | Größte Schwäche | Als offener Punkt 19 | Keiner der neun neuen Tweaks ist echt angewendet worden (alle brauchen Administratorrechte, UAC-Tests sind auf dem Host verboten). Die Herausgeber-Erkennung ist für fünf der zehn Programme auf dem Host nicht beobachtbar. |
| 9 | Annahmen | DECISIONS.md, Abschnitt "V2-4" | Autostart als Erkennungstabelle, RAM nur bei laufendem Programm, PowerSettingSet und desktopOnly, WMI statt PowerShell, *EEE nur wo vorhanden, Cache-Unterordner und Muster-Segment, keine Entwickler-Caches, Preset-Zuordnung. |
| 10 | Nächste Phase | V2-5 | Die zehn Gaming-Checks mit Fake-Daten und die Karte "Messung" mit Vorher/Nachher. |

## V2-5: Gaming-Checks und Messung

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 552/552 Tests (App 103, Catalog 66, Ipc 31, Engine 352), Linter, Kontrast, Format. |
| 2 | Revert für Neues getestet | Entfällt | Checks und Messung ändern nichts am System; DEFN-001-Nachbesserung über den bestehenden Test (Richtlinie oder anderer Virenschutz: nicht verfügbar). |
| 3 | Dry-Run unverändert | Ja | Keine neuen Aktionen. |
| 4 | Beschreibungen stimmen | Ja | Quellen: Microsoft Learn "Launch Windows Settings" (alle ms-settings-Adressen), Win32_PhysicalMemory, EnumDisplaySettings; RAM-Hinweis ausdrücklich "je nach Board verschieden"; Dual-Channel ohne Kaufdruck; kein FPS-Versprechen. |
| 5 | UI nach 2.3 und DESIGN.md | Ja | Ein Satz pro Check, Zustand als Badge, "Beheben:", Link oder "Hingehen". Messkarte mit "Einzelne Messungen schwanken.". Screenshots in `artifacts/screens-v25` (172 Bilder), Barrierefreiheit 0 Befunde bei 5493 Elementen. |
| 6 | Durchgefallene Tweaks | Keine | Keine neuen Tweaks. |
| 7 | Änderungen am echten Rechner | Keine | Nur lesend: echte Messung und alle zehn Checks (Werte in DECISIONS "V2-5"), `MSFT_MpComputerStatus`, `Win32_PhysicalMemory`. |
| 8 | Größte Schwäche | Annahme | Der Vergleich "Nachher" ist ein Einzelwert nach einem Neustart; Hintergrundlast schwankt stark (die Karte sagt das). Die Startdauer fehlt ohne Administratorrechte. Nur der Hauptbildschirm wird geprüft. |
| 9 | Annahmen | DECISIONS.md, Abschnitt "V2-5" | Links statt Schalter bei versteckten Tweaks, RAM-Einheiten, Sprungziele je Modus, automatisches "Vorher" vor einem Preset, Neustart über die Laufzeit erkannt. |
| 10 | Nächste Phase | V2-6 | Große Dateien neu: Klassifikation, Spielübersicht, KeepList, Löschen nur in den Papierkorb; Lauf auf dem echten Rechner nur lesend. Dabei auch die Seite "Aufräumen" im Einfach-Modus vereinfachen. |

## V2-6: Große Dateien

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Qualitätstor grün | Ja | `build.ps1`: 0 Warnungen, 561/561 Tests (App 104, Catalog 66, Ipc 31, Engine 360), Linter, Kontrast, Format. |
| 2 | Revert für Neues getestet | Ja | Löschen nur in den Papierkorb (rückgängig über den Explorer); `Recycler_rechecks_everything_and_never_receives_a_class_d_path` (Klasse D auch bei gefälschter Klasse nie im Papierkorb, geänderte, fehlende und geöffnete Dateien übersprungen). |
| 3 | Dry-Run unverändert | Ja | Die Suche liest nur; vor dem Löschen ein Dialog mit Anzahl, Gesamtgröße und den drei größten Dateien. |
| 4 | Beschreibungen stimmen | Ja | Jede Zeile hat "Warum sehe ich das?" mit Ort und Alter; Spiele "TweakPilot löscht nie"; Cloud "wird weder geladen noch gelöscht". |
| 5 | UI nach 2.3 und DESIGN.md | Ja | Gruppen A und B sichtbar, C und D nur auf Klick und nur "Im Explorer zeigen", nichts vorausgewählt, eine Hauptaktion. Einfach-Seite "Aufräumen" vereinfacht. Screenshots in `artifacts/screens-v26` (174 Bilder, mit `disk-large-scanned`), Barrierefreiheit 0 Befunde bei 5724 Elementen. |
| 6 | Durchgefallene Tweaks | Keine | Keine neuen Tweaks. |
| 7 | Änderungen am echten Rechner | Keine | Nur lesend: `tweakpilot-cli --large-scan`. Ergebnis: A 4 Dateien (Installer in Downloads), B 0, C 5, D 60; **0 Spieldateien fälschlich in A oder B**. |
| 8 | Größte Schwäche | Annahme | Die Spielerkennung ist eine Heuristik: Ein Spiel ohne Launcher-Marker und ohne typische Archive wäre nur über "älter als 30 Tage und unbekannt = C" geschützt, nicht als D. Der echte Lauf hat zwei Fehler gezeigt, die die Fake-Tests nicht fanden (C:\Users als "Spiel", Electron-Apps als Spiele); beide sind behoben und als Test festgehalten. |
| 9 | Annahmen | DECISIONS.md, Abschnitt "V2-6" | Klassenreihenfolge, Windows-Reste bleiben in der Bereinigung, Grenzen der Spielerkennung, schwache Archiv-Endungen, Deinstallieren über Windows-Apps, Duplikat-Original, Prüfungen vor dem Löschen, Downloads über Known Folder, Ergebnisgrenzen, Einfach-Ansicht "Aufräumen". |
| 10 | Nächste Phase | V2-7 | Verpackung und Installation: `package.ps1`, Ordner `Desktop\TweakPilot`, Installieren/Portabel/Deinstallieren, `START HIER.txt`, Prüfsummen; Skriptlogik in Temp getestet, App nur mit `--version`/`--list` gestartet. |

## V2-7: Verpackung und Installation

| Nr. | Prüfpunkt | Ergebnis | Beleg |
|---|---|---|---|
| 1 | Qualitätstor grün | Ja | `build.ps1 -Package`: 0 Warnungen, 566/566 Tests (App 109, Catalog 66, Ipc 31, Engine 360), Linter, Kontrast, Format, Pakettest; danach Paket gebaut. |
| 2 | Revert für Neues getestet | Ja | Deinstallation entfernt nur den eigenen markierten Ordner, Verknüpfungen und den Eintrag, Daten standardmäßig behalten (`InstallerTests`, Test-Hook mit Temp-Ordnern und gefälschter Registry). |
| 3 | Dry-Run unverändert | Ja | Keine neuen Tweaks; Installation nur nach Klick, nie automatisch. |
| 4 | Beschreibungen stimmen | Ja | `START HIER.txt` (DE und EN): Start, Installieren/portabel, Zurück, Entfernen, SmartScreen mit Prüfsummen-Anleitung, "nie Defender abschalten". |
| 5 | UI nach 2.3 und DESIGN.md | Ja | Ein kleines Fenster mit zwei Knöpfen ("Installieren (empfohlen)" als Hauptaktion), Texte DE und EN; nicht im Snapshot, weil es vor dem Hauptfenster steht. |
| 6 | Durchgefallene Tweaks | Keine | Keine neuen Tweaks. |
| 7 | Änderungen am echten Rechner | Ja, erlaubt | `C:\Users\tobia\Desktop\TweakPilot` neu angelegt (Ziel der Phase), ZIP in `artifacts`. Aus dem Paket nur `app\tweakpilot-cli.exe --version` und `--list` gestartet. Keine Installation, kein UAC, keine Verknüpfung, kein Registry-Eintrag. |
| 8 | Größte Schwäche | Als offener Punkt 20 | Der Ablauf Installieren und Deinstallieren ist nur mit dem Test-Hook geprüft, nicht mit echtem Doppelklick, echten Verknüpfungen und "Apps". Die Programme sind groß (212, 103, 113 MB), weil jede Datei ihr eigenes .NET enthält. Ohne Signatur zeigt Windows SmartScreen. |
| 9 | Annahmen | DECISIONS.md, Abschnitt "V2-7" | Umbenennen statt Assembly-Name, Einzeldateien, Helfer-Pfadcheck mit zwei Ordnern plus Hash, Installationsmarkierung, einmalige Frage, Deinstallation über Temp-Kopie, deutsche Dokumentnamen, Bauausgaben, Inno als zweiter Weg. |
| 10 | Nächste Phase | V2-8 | Gesamtabnahme: Qualitätstor, README, CHANGELOG, ACCEPTANCE, offene Punkte neu nummeriert (was nur der Nutzer erledigen kann), Version 1.1.0-rc.1. |

Prüfungen am echten Paket: Prüfsummen in `Dokumente\Pruefsummen.txt` stimmen mit den Dateien überein; der SHA-256 des Helfers (`08DFA75B…AB23`) steckt in `TweakPilot.exe` und `app\tweakpilot-cli.exe`.

