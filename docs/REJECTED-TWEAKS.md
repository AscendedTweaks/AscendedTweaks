# Abgelehnte Tweaks

Hier steht jeder Tweak, der bewusst nicht enthalten ist oder bei der Prüfung durchgefallen ist, mit Grund. IDs werden nie wiederverwendet.

## Bei der Prüfung durchgefallen

Phase 1: keiner. Hinweis: PERF-016 ist auf dem Entwicklungsrechner (Build 26200 Home) nicht verfügbar, weil der Dienst "Fax" dort nicht installiert ist. Das ist kein Durchfallen; der Tweak meldet "nicht verfügbar" und wird nicht angewendet (Regel S12).

Phase 5:

| ID | Tweak | Grund |
|---|---|---|
| PRIV-004 | Dienst `dmwappushservice` auf Manuell | Kein Effekt: Der Dienst steht unter Windows 10 22H2 und 11 bereits standardmäßig auf "Manuell" (Host gelesen, Build 26200; Microsoft Learn "Windows system services" nennt keinen anderen Standard). Ein Tweak, der den Standard setzt, wäre Placebo. |
| PRIV-007 | Dienst `lfsvc` (Geolocation) auf Manuell | Kein Effekt aus demselben Grund: Standard ist bereits "Manuell" (Trigger-Start). Wer Standortzugriff sperren will, nutzt PRIV-006 (dokumentierte Richtlinie). |
| (Teil von PRIV-008) | `HKCU\Software\Microsoft\Windows\CurrentVersion\Search\BingSearchEnabled` = 0 | Nicht dokumentiert, auf aktuellen Builds laut Berichten wirkungslos. Außerdem liegt der Wert außerhalb von `Software\Policies` und kann nicht im selben Bereich wie die Richtlinie geschrieben werden. Entfernt; PRIV-008 setzt nur noch die Richtlinie und bleibt "Umstritten". |

Phase 6:

| ID | Tweak | Grund |
|---|---|---|
| PERF-019 | Remote-Registrierung deaktivieren | Kein Effekt: Der Dienst `RemoteRegistry` steht auf Windows 10 und 11 (Client) bereits standardmäßig auf "Deaktiviert" (Host gelesen, Build 26200). Ein Tweak, der den Standard setzt, wäre Placebo. |

Phase 7:

| ID | Tweak | Grund |
|---|---|---|
| GAME-014 | Xbox-Hintergrunddienste auf Manuell | Kein Effekt: `XblAuthManager`, `XblGameSave`, `XboxNetApiSvc` und `XboxGipSvc` stehen bereits standardmäßig auf "Manuell" (Host gelesen, Build 26200). Placebo. |

Phase 9: keiner abgelehnt. Einschränkung: UI-005 (Widgets-Symbol) ist auf Build 26200 nicht umsetzbar, weil Windows den Schreibzugriff blockiert; der Tweak gilt nur bis Build 22631, darüber hilft DEB-027.

Abweichungen vom Mechanismus der Kandidatenliste (Tweak bleibt, aber mit dokumentiertem Weg):

| ID | Kandidat | Umgesetzt | Grund |
|---|---|---|---|
| PRIV-006 | `ConsentStore\location\Value` = "Deny" (HKLM) | Richtlinie `LetAppsAccessLocation` = 2 | Der ConsentStore-Wert ist interner Zustand der Einstellungen-App; die Richtlinie ist im Privacy Policy CSP dokumentiert und wird von Windows durchgesetzt. |
| PRIV-009 | `HKCU\...\SearchSettings\IsDynamicSearchBoxEnabled` = 0 | Richtlinie `EnableDynamicContentInWSB` = 0 (HKLM) | Nur die Richtlinie ist dokumentiert (Search Policy CSP, AllowSearchHighlights). |
| PRIV-012 | `HKCU\...\Privacy\TailoredExperiencesWithDiagnosticDataEnabled` = 0 | Benutzer-Richtlinie `CloudContent\DisableTailoredExperiencesWithDiagnosticData` = 1 | Dokumentiert in "Manage connections ...", Abschnitt 18.16. |
| PERF-006 | `HKCU\Control Panel\Desktop\MenuShowDelay` = "100" | SPI_SETMENUSHOWDELAY = 100 | Dokumentierte Funktion, wirkt sofort, schreibt denselben Wert. |
| PERF-009 | Ist-Zustand über `powercfg` | `GetPwrCapabilities` | Befehlsausgabe ist sprachabhängig; die Struktur ist dokumentiert. |
| PERF-012 | `fsutil behavior set disable8dot3 1` | `NtfsDisable8dot3NameCreation` = 1 | Gleicher Wert, den fsutil laut Dokumentation setzt; Erkennung ohne Ausgabe-Parsing. |
| UPD-005 | `NoAutoRebootWithLoggedOnUsers` = 1, Risiko "Sicher" | zusätzlich `AUOptions` = 4, Risiko "Vorsicht" | Microsoft verlangt beide Werte und warnt, dass Neustarts nach Qualitätsupdates ganz ausbleiben können. |
| UPD-006 | `UX\Settings` `ActiveHoursStart`/`ActiveHoursEnd`, alle Editionen | Richtlinie `SetActiveHours`, `ActiveHoursStart`, `ActiveHoursEnd`, Pro und höher | Nur die Richtlinie ist dokumentiert ("Manage device restarts after updates"). |
| UPD-001 | Nachweis "Belegt" | "Umstritten", nur im Expertenmodus direkt | Die Pausenwerte unter `UX\Settings` sind nicht dokumentiert; dokumentiert ist die Pause per Richtlinie (nur Pro) und in den Einstellungen. |
| UPD-007 | Nachweis "Belegt" | "Umstritten" | Die Registry-Wertnamen stehen nicht auf Microsoft Learn, nur in der ADMX-Datei. |
| GAME-004 | `AppCaptureEnabled` und `GameDVR_Enabled` (HKCU) | Richtlinie `AllowGameDVR` = 0 (HKLM) | Dokumentiert im ApplicationManagement Policy CSP; laut Dokumentation nur Pro, Enterprise, Education. |
| GAME-009/010 | Wert überschreiben | Eintrag in der gemeinsamen Zeichenkette setzen (`RegistryListEntry`) | Beide Tweaks teilen sich einen Wert; Zurücksetzen darf den anderen nicht löschen. |
| GAME-011 | `MouseSpeed`, `MouseThreshold1/2` = "0" | SPI_SETMOUSE (0, 0, 0) | Dokumentiert, wirkt sofort, Originalwerte exakt wiederherstellbar. |
| GAME-012/013 | `Flags` = "506", "122", "58" | Nur das Bit SKF/FKF/TKF_HOTKEYACTIVE über SystemParametersInfo | Absolute Werte würden andere Einstellungen der Eingabehilfen überschreiben. |
| PRIV-024 | `HKLM\...\DeliveryOptimization\Config\DODownloadMode` = 0 | Richtlinie `SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization\DODownloadMode` = 0 | `Config` ist der interne Zustand; die Richtlinie ist in der Delivery-Optimization-Referenz dokumentiert. |

## V2-4: neue Kandidaten aus Abschnitt 5.2

| Kandidat | Ergebnis | Grund |
|---|---|---|
| Teams-Cache des neuen Teams (`%LOCALAPPDATA%\Packages\MSTeams_8wekyb3d8bbwe\LocalCache\Microsoft\MSTeams`) | Nicht aufgenommen | Microsoft beschreibt nur das Löschen des ganzen Ordners; darin liegen auch Einstellungen. Ein reiner Cache-Unterordner ist nicht dokumentiert. CLEAN-019 nimmt nur die Cache-Ordner des klassischen Teams. |
| Spotify `%LOCALAPPDATA%\Spotify\Data` | Nicht aufgenommen | Auf dem Entwicklungsrechner liegt dort der große Zwischenspeicher (477 MB), aber was Spotify dort ablegt (auch heruntergeladene Titel?), ist nicht dokumentiert. CLEAN-016 nimmt nur `Storage` nach Abschnitt 5.2 und steht auf "Vorsicht". |
| Ganze Launcher-Ordner (Discord, Epic `webcache_*`, Steam `htmlcache`) | Eingeschränkt | Auf dem Entwicklungsrechner liegen dort auch `Cookies` und `Local Storage` (Anmeldung). Aufgenommen sind nur die Unterordner `Cache`, `Code Cache` und `GPUCache`. |
| Edge und Chrome ohne Richtlinie (pro Benutzer) | Nicht möglich | Die Browser-Einstellungen liegen in den Profildateien des Browsers und sind nicht als Schnittstelle dokumentiert; `HKCU\Software\Policies` ist ebenfalls eine Richtlinie. BROW-001 bis BROW-003 bleiben Richtlinien unter HKLM mit Pflichthinweis "Wird von deiner Organisation verwaltet". |
| Defender über `Set-MpPreference` (PowerShell) | Anderer Weg | Die App startet nie PowerShell mit umgangener Ausführungsrichtlinie. DEFN-001 nutzt dieselbe dokumentierte WMI-Klasse `MSFT_MpPreference` (Methode `Set`) direkt. |
| POWR-001, POWR-002 | Aufgenommen, aber "Umstritten" | Die Einstellungen sind dokumentiert, ein Vorteil für Spiele oder Eingabe nicht. Nur im Experten-Modus. |

## V3: Kandidaten aus Open-Source-Tools (2026-10-23)

Gelesen als Quelltext, nichts heruntergeladen oder ausgeführt: Chris Titus Tech WinUtil (`config/tweaks.json`, 68 Einträge). Was dort für Spiele relevant ist und hier fehlte:

| Kandidat | Ergebnis | Grund |
|---|---|---|
| Virtual Machine Platform aus | Aufgenommen als GAME-028 ("Umstritten", Experte) | Microsoft Support "Options to optimize gaming performance in Windows 11" nennt VMP als mögliche Bremse in manchen Konfigurationen. Keine eigene Messung. |
| Multiplane Overlay aus (`Dwm\OverlayTestMode` = 5, `GraphicsDrivers\DisableOverlays`) | Nicht aufgenommen | Keine offizielle Anleitung von Microsoft oder NVIDIA gefunden; Berichte, dass der Wert ab 24H2 nicht mehr wirkt. Hilft höchstens gegen Flackern, kein Leistungsgewinn. |
| Energiedrosselung aus (`Power\PowerThrottling\PowerThrottlingOff`) | Nicht aufgenommen | Registry-Wert nicht dokumentiert. Das Spiel im Vordergrund wird nicht gedrosselt; der Wert lässt nur Hintergrundprogramme mehr Strom nutzen. |
| svchost-Aufteilung aus (`SvcHostSplitThresholdInKB`) | Nicht aufgenommen | Spart nur etwas Arbeitsspeicher, kann laut WinUtil Xbox-Dienste stören; kein Spielevorteil. |
| IPv4 bevorzugen, Teredo aus (`Tcpip6\DisabledComponents`) | Nicht aufgenommen | IPv6 abschalten ist nach Abschnitt 6.12 ausgeschlossen; Teredo aus kann Xbox-Partychat und Mehrspieler stören. |
| Dienste auf Manuell (CscService, StorSvc, SharedAccess) | Nicht aufgenommen | Massen-Änderung von Diensten ist ausgeschlossen; kein belegter Spielevorteil. |

## Bewusst nicht enthalten (Abschnitt 6.12)

| Tweak | Grund |
|---|---|
| Registry-Cleaner | Kein messbarer Leistungsgewinn, Risiko für die Stabilität. |
| "RAM bereinigen", Standby-Liste automatisch leeren | Kein dauerhafter Nutzen; Windows verwaltet den Standby-Speicher selbst. |
| Prefetch löschen oder deaktivieren | Verschlechtert Programmstarts, kein Gewinn. |
| Auslagerungsdatei abschalten | Risiko von Abstürzen bei Speichermangel, keine Absturzabbilder mehr. |
| IPv6 deaktivieren | Von Microsoft nicht empfohlen, kann Windows-Funktionen stören. |
| Nagle-Tweaks (`TcpAckFrequency`, `TCPNoDelay`) für Spiele | Spiele setzen ihre Socket-Optionen selbst; kein belegter Nutzen. |
| MMCSS-Werte (`NetworkThrottlingIndex`, `SystemResponsiveness`, Games-Task-Werte) als "FPS-Boost" | Keine belegte Wirkung. Das vorhandene Altskript `FortniteOptimizer.ps1` setzt einige dieser Werte; sie werden nicht übernommen. |
| Core-Parking-Registry-Tweaks | Keine belegte Wirkung auf aktuellen Systemen, Energieverwaltung übernimmt Windows. |
| Massen-Deaktivierung von Diensten nach Forenlisten | Unvorhersehbare Nebenwirkungen; Dienste nur einzeln aus dem geprüften Katalog. |
| Spectre/Meltdown-Schutz abschalten (`FeatureSettingsOverride`) | Sicherheitsverlust. |
| Hosts-Datei-Blocklisten für Microsoft-Domains | Kann Updates, Store und Aktivierung stören; intransparent. |
| Defender, Firewall, UAC, SmartScreen, Windows Update deaktivieren | Sicherheitsverlust (Regel S5). |
| WinSxS von Hand löschen | Beschädigt das System; nur über DISM (CLEAN-014). |
| Treiber-"Updater" | Fremdquellen, Risiko für Stabilität und Sicherheit. |
| Auto-Overclocking-Versprechen | Hardware-Risiko, nicht belegbar. |
