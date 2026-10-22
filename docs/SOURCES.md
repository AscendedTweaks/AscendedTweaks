# Quellen und Prüfnachweise

Pro Tweak: Microsoft-Dokumentation (falls vorhanden) und eigene Prüfung mit Datum, Build und Methode. Leistungsmessungen (Messprotokoll aus Abschnitt 6) werden hier mit Rohdaten eingetragen.

## Prüfumgebung Phase 1

- Rechner: Entwicklungsrechner, Windows 11 25H2, Build 26200, Edition Home (EditionID `Core`). Keine VM verfügbar (siehe `docs/DECISIONS.md`, offener Punkt 1).
- Methode: `tests/vm/Invoke-TweakIntegrationTest.ps1`, Schritte 3 bis 7 aus Abschnitt 11.2, Registry-Snapshots mit `tools/registry-snapshot`, App-Zustand in einem temporären Ordner.
- Bericht: `tests/vm/report-host-26200.json` (2026-10-17).

## PRIV-001 Werbe-ID ausschalten

- Microsoft Learn: "Manage connections from Windows operating system components to Microsoft services", Abschnitt 18.1 General. Die Seite beschreibt den Schalter in der Einstellungen-App und als Registry-Alternative `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AdvertisingInfo\Enabled` = 0. Die App setzt den benutzerbezogenen Wert unter HKCU, den der Schalter schreibt.
- Eigene Prüfung 2026-10-17, Build 26200 Home: Auf dem Rechner war der Schalter bereits aus und der HKCU-Wert stand auf 0. Für den Test wurde der Wert kurz auf den Windows-Standard 1 gesetzt und danach wieder auf 0. Ergebnis: Dry-Run ohne Änderung; Anwenden ändert nur `Enabled` (1 auf 0); Erkennung `Applied`; Zurücksetzen stellt 1 wieder her; Erkennung `NotApplied`. Bestanden.

## PRIV-013 Feedback-Abfragen ausschalten

- Microsoft Learn: dieselbe Seite, Abschnitt 18.16 Feedback & diagnostics. Einstellung "Nie": `HKCU\Software\Microsoft\Siuf\Rules\PeriodInNanoSeconds` = 0 und `NumberOfSIUFInPeriod` = 0 (REG_DWORD). "Automatisch" bedeutet: beide Werte gelöscht.
- Eigene Prüfung 2026-10-17, Build 26200 Home: Schlüssel `Siuf` existierte vorher nicht. Anwenden legt `Siuf` und `Siuf\Rules` mit beiden Werten an, sonst nichts. Zurücksetzen entfernt beide Werte und beide Schlüssel; Snapshot danach identisch mit vorher. Bestanden.

## PERF-016 Fax-Dienst deaktivieren

- Keine Leistungsmessung, daher "Umstritten".
- Eigene Prüfung 2026-10-17, Build 26200 Home: Dienst "Fax" nicht installiert. Zustandserkennung meldet "nicht verfügbar: Dienst Fax ist nicht installiert". Apply/Revert nur gegen Fakes getestet.

## PERF-001 Energiesparplan Höchstleistung

- Microsoft Learn: "Powercfg command-line options" (`/setactive`, Alias `SCHEME_MIN` für Höchstleistung, GUID 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c).
- Eigene Prüfung 2026-10-17, Build 26200 Home (nur lesend): Plan "Höchstleistung" ist vorhanden, aktiv ist "Ultimative Leistung" (vom Altskript gesetzt). Erkennung meldet `NotApplied`. Apply/Revert nur gegen Fakes getestet, weil ein Test auf dem Host den vom Altskript gesicherten Energieplan-Zustand berührt hätte.
- Keine Leistungsmessung, daher "Umstritten".

## UI-001 Klassisches Kontextmenü

- Keine Microsoft-Dokumentation.
- Eigene Prüfung 2026-10-17, Build 26200 Home: Anwenden legt `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` mit leerem Standardwert an; Zurücksetzen entfernt beide angelegten Schlüssel; Snapshot danach identisch. Die sichtbare Wirkung nach Explorer-Neustart wurde nicht geprüft (Explorer wurde auf dem Arbeitsrechner nicht neu gestartet). Deshalb vorerst "Umstritten".

## Phase 5: Datenschutz (PRIV-001 bis PRIV-024)

Hauptquelle ist Microsoft Learn "Manage connections from Windows operating system components to Microsoft services" (Abschnittsnummern in den Tweak-Dateien) sowie die jeweiligen Policy-CSP-Seiten. Der genaue Wortlaut mit Abschnitt steht in `sources` jeder Tweak-Datei und im Detailpanel der App.

Eigene Prüfung 2026-10-17, Build 26200 Home, Host-Integrationstest Schritte 3 bis 7 (`tests/vm/report-host-26200-phase5.json`):

| ID | Bereich | Ergebnis |
|---|---|---|
| PRIV-014 | User | Bestanden. Beide Werte von 0 auf 1, zurück auf 0. |
| PRIV-015 | User | Bestanden. Wert neu angelegt und wieder entfernt. |
| PRIV-016 | User | Bestanden. Wert neu angelegt und wieder entfernt. |
| PRIV-017 | User | Bestanden (Registry). Sieben Werte; vorhandener Wert 338389 von 1 auf 0 und zurück, fehlende Werte angelegt und wieder entfernt. Wirkung auf die Oberfläche nicht geprüft, daher "Umstritten". |
| PRIV-018 | User | Bestanden (Registry), "Umstritten". |
| PRIV-019 | User | Bestanden (Registry), "Umstritten". |

Nicht auf dem Host prüfbar (brauchen Admin-Rechte, also UAC): alle Machine-Tweaks (PRIV-002, 003, 005, 006, 009, 011, 020 bis 024) und die Benutzer-Richtlinien PRIV-008, 010, 012. Diese sind gegen Fakes getestet (Engine- und Helfer-Tests inklusive HKU-Umleitung) und stehen im offenen Punkt 9 (`DECISIONS.md`). PRIV-001 und PRIV-013 wurden in Phase 1 geprüft.

Vorher gelesen (nur lesend, Host 26200): `dmwappushservice` und `lfsvc` stehen auf "Manuell"; daraus folgt die Ablehnung von PRIV-004 und PRIV-007.

## Phase 6: Leistung (PERF-001 bis PERF-026)

Quellen pro Tweak stehen in `sources` der Tweak-Dateien (Microsoft Learn: Powercfg command-line options, SystemParametersInfoW, Privacy Policy CSP, fsutil behavior, fsutil 8dot3name, Guidance on disabling system services on Windows, SYSTEM_POWER_CAPABILITIES; MSRC CVE-2021-34527 für PERF-015).

Gelesen auf dem Host (Build 26200 Home, nur lesend): WSearch Automatisch (verzögert), SysMain und Spooler Automatisch, RetailDemo Manuell, MapsBroker Automatisch (verzögert), RemoteRegistry Deaktiviert (daraus Ablehnung PERF-019), Fax nicht installiert; Ruhezustand aktiv (hiberfil.sys 13,5 GB), Schnellstart aus; Aufgaben "Microsoft Compatibility Appraiser" und "ProgramDataUpdater" auf diesem Build nicht vorhanden (Tweaks melden "nicht verfügbar"). Die Zustandserkennung aller PERF-Tweaks lieferte auf dem echten System plausible Werte (`ascendedtweaks-cli --list`).

Eigene Prüfung 2026-10-17, Host-Integrationstest Schritte 3 bis 7 (`tests/vm/report-host-26200-phase6.json`):

| ID | Ergebnis |
|---|---|
| PERF-003 | Bestanden (Registry). Wirkung auf die Effekte nicht geprüft, "Umstritten". |
| PERF-004 | Bestanden. 1 auf 0, zurück auf 1. |
| PERF-005 | Bestanden. Geändert nur `UserPreferencesMask` (ein Bit) und `WindowMetrics\MinAnimate` ("1" auf "0"), beide als Abdruck deklariert; danach exakt wie vorher. |
| PERF-006 | Bestanden. `MenuShowDelay` "400" auf "100" und zurück. |
| PERF-007 | Bestanden. Schlüssel `Serialize` angelegt und wieder entfernt. |
| PERF-026 | Bestanden für den Benutzer-Eintrag eines Programms (Name im Bericht weggelassen): Dry-Run ohne Änderung, Freigabewert 02 auf 03, Erkennung "angewendet", Zurücksetzen exakt auf den alten Wert, Erkennung "nicht angewendet". Beim ersten Lauf fand der Test einen Fehler in der CLI (`--status` und `--revert` kannten die erzeugten IDs nicht); der Wert wurde sofort von Hand wiederhergestellt, die CLI korrigiert und der Test wiederholt. |

Nicht auf dem Host prüfbar (Admin-Rechte): alle Machine-Tweaks der Leistung. Gegen Fakes getestet, inklusive Aufgabenplanung (XML-Auswertung, Fehlerpfad, Rollback), Ruhezustand über die Energieverwaltung und maschinenweitem Autostart über den Helfer.

## Phase 7: Gaming (GAME-001 bis GAME-021)

Quellen pro Tweak in `sources` der Tweak-Dateien (Microsoft Learn: ApplicationManagement Policy CSP / AllowGameDVR, SystemParametersInfoW, STICKYKEYS, FILTERKEYS, TOGGLEKEYS, Enable memory integrity; Microsoft-Support: Options to optimize gaming performance in Windows 11). Für GAME-008 (UserGpuPreferences) fand sich keine Microsoft-Learn-Seite, nur Community-Quellen; daher "Umstritten".

Gelesen auf dem Host (nur lesend): Spielmodus an (Standard), Xbox-Dienste alle "Manuell" (daraus Ablehnung GAME-014), HAGS = 2, Spielaufnahme aus, Speicherintegrität an, Secure Boot an, TPM 2.0, Testsignierung und Kernel-Debugger aus. Spielerkennung: Fortnite (Epic, EasyAntiCheat erkannt, Spielprozess-EXE vorhanden), ein weiteres Epic-Spiel, ein Steam-Spiel (Steamworks-Pakete ausgelassen).

Eigene Prüfung 2026-10-18, Host-Integrationstest (`tests/vm/report-host-26200-phase7.json`):

| ID | Ergebnis |
|---|---|
| GAME-003 | Bestanden (Registry), "Umstritten". |
| GAME-006 | Bestanden (Registry), "Umstritten". |
| GAME-009 | Bestanden. Wert neu angelegt mit genau einem Eintrag, danach wieder entfernt. |
| GAME-010 | Bestanden, wie GAME-009. |
| GAME-012 | Bestanden im zweiten Lauf. Der erste Lauf war ungültig: Der Live-Zustand war bereits "Kürzel aus", die Registry zeigte noch den Standard. Der Live-Zustand des Nutzers wurde danach exakt wiederhergestellt; das Skript prüft seitdem eine Vorbedingung. |
| GAME-013 | Wie GAME-012. |

Nicht auf dem Host geprüft: GAME-001, 005 und 011 (bereits im Zielzustand, nicht durch die App; ein Test hätte die Einstellung des Nutzers ändern müssen), alle Machine-Tweaks (UAC), die Pro-Spiel-Tweaks GAME-007, 008 und 016 nur gegen Fakes.

## Phase 8: Bereinigung (CLEAN-001 bis CLEAN-014)

Die Bereinigung braucht keine Microsoft-Dokumentation für eine Wirkung: Sie löscht Zwischenspeicher. Belegt wird das Verhalten (was gelöscht wird, was nie). Verwendete dokumentierte Schnittstellen: `IFileOperation` (Papierkorb), `SHQueryRecycleBin`/`SHEmptyRecycleBin`, `CreateFileW` mit `FILE_FLAG_OPEN_REPARSE_POINT`, `SetFileInformationByHandle` (FileDispositionInfo), Datenträgerbereinigung `cleanmgr /sageset|/sagerun`, `DISM /StartComponentCleanup`.

Host-Prüfung 2026-10-18, nur lesend (`ascendedtweaks-cli --clean-scan`): Alle 14 Kategorien liefern plausible Werte (zum Beispiel Benutzer-Temp 14 MB, Miniaturansichten 166 MB, Shader-Caches 627 MB, Papierkorb 11,7 GB; Systemordner ohne Administratorrechte geschätzt). Beim ersten Lauf meldete der Papierkorb "unbekannt": falsche Struktur-Ausrichtung von `SHQUERYRBINFO` unter 64 Bit, behoben.

Löschen auf dem echten Dateisystem nur in einem eigenen Sandbox-Ordner (`CleanupIntegrationTests`): alte Dateien gelöscht, schreibgeschützte entfernt, geöffnete übersprungen, junge Dateien behalten, echte Junction (`mklink /J`) nicht verfolgt, Ziel hinter der Junction unverändert, Handle-basiertes Löschen einer Junction verweigert. Auf den echten Daten des Nutzers wurde nichts gelöscht.

## Phase 9: Debloat und Oberfläche

Quellen: Microsoft Learn `PackageManager` (Windows.Management.Deployment: FindPackagesForUser, FindPackages, RemovePackageAsync mit RemoveForAllUsers, DeprovisionPackageForAllUsersAsync, RegisterPackageByFamilyNameAsync, RegisterPackageAsync), NewsAndInterests Policy CSP (`AllowNewsAndInterests`, DEB-027), Dateiattribute `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` und `FILE_ATTRIBUTE_RECALL_ON_OPEN` (Cloud-Dateien) für die OneDrive-Prüfung.

Host-Prüfung 2026-10-18:

- Paketliste gelesen (`ascendedtweaks-cli --apps`): 20 der 26 Katalog-Apps installiert, 9 Apps anderer Hersteller (darunter Treiber- und Sicherheitssoftware, mit Warnhinweis), 19 gesperrte Systemapps.
- Wiederherstellungsweg (a) mit echter Paketverwaltung: Neuregistrierung per Paketfamilie für eine installierte Katalog-App gelingt (`AppxIntegrationTests`, ändert nichts). Entfernen wurde auf dem Host nicht ausgeführt (UAC und nicht garantiert umkehrbar).
- OneDrive (`--onedrive-check`): nicht installiert; nur das Installationsprogramm liegt in System32. Die Sperrgründe sind gegen Fakes getestet.
- UI-Tweaks, Host-Integrationstest (`tests/vm/report-host-26200-phase9.json`): UI-002, 003, 006, 008, 009, 010, 011 bestanden (Registry). UI-005: Schreibzugriff von Windows verweigert, sauber zurückgerollt. UI-004 auf diesem Build nicht verfügbar, UI-007 bereits gesetzt (nicht durch die App).

## Phase 10: Netzwerk und Datenträger

Quellen: Microsoft Learn `netsh interface ip` (set dnsservers, add dnsservers), `netsh winsock reset` und `netsh interface ip reset`, `netsh interface tcp set global autotuninglevel`, DNSClient Policy (`EnableMulticast`, NET-009), `MSPower_DeviceEnable` (root\wmi, NET-011), `fsutil behavior query/set DisableDeleteNotify` (DISK-001), `defrag` (Parameter /L und /D, DISK-002 und DISK-006), `MSFT_PhysicalDisk` und `MSFT_StorageReliabilityCounter` (Storage Management API, DISK-003), `MSStorageDriver_FailurePredictStatus`, DISM `/Get-ReservedStorageState` und `/Set-ReservedStorageState` (DISK-007), DNS over HTTPS in Windows (`DohWellKnownServers`, NET-005), `ipconfig /flushdns` (NET-006), `System.Net.NetworkInformation.Ping` (NET-007).

Host-Prüfung 2026-10-18, nur lesend:

- Erkennung aller NET- und DISK-Tweaks plausibel (`ascendedtweaks-cli --status`); NET-011 (Energiesparen der Netzwerkkarte, WMI root\wmi) und DISK-007 (reservierter Speicher) ohne Administratorrechte als "Admin nötig"; NET-010 und DISK-001 stehen bereits auf dem Windows-Standard.
- Ping und Jitter (`--ping 1.1.1.1`): 50 Pakete, plausible Werte.
- Laufwerke (`--volumes`): Systemlaufwerk als SSD erkannt, Modell gelesen.
- DoH: Windows kennt DoH-Vorlagen; der aktuelle DNS-Server (von einem VPN-Programm gesetzt) ist keiner davon.
- Nicht ausgeführt: Schreiben von DNS, Autotuning, Energiesparen, TRIM, reserviertem Speicher, Wartungsaktionen und SMART (alle UAC; offener Punkt 9).

## Phase 11: Updates und Profile

Quellen: Microsoft Learn "Configure Windows Update client policies" (Tabelle der GPO-Werte unter `HKLM\Software\Policies\Microsoft\Windows\WindowsUpdate`: DeferFeatureUpdates, DeferFeatureUpdatesPeriodInDays 0 bis 365, DeferQualityUpdates, DeferQualityUpdatesPeriodInDays, ExcludeWUDriversInQualityUpdate; Pause höchstens 35 Tage mit automatischer Wiederaufnahme), "Manage device restarts after updates" (SetActiveHours, ActiveHoursStart, ActiveHoursEnd; AUOptions = 4 mit NoAutoRebootWithLoggedOnUsers = 1 und der Warnung, dass Neustarts ganz ausbleiben können), Update Policy CSP (TargetReleaseVersion und ProductVersion; Editionen Pro, Enterprise, Education).

Host-Prüfung 2026-10-18, nur lesend:

- `ascendedtweaks-cli --status`: UPD-002 bis UPD-006 und UPD-007/11/25H2 melden auf Windows 11 Home "Auf dieser Windows-Edition nicht wirksam"; UPD-001 "nicht angewendet" (keine Pause aktiv).
- Installierte Version laut `DisplayVersion`: 25H2; die Seite zeigt sie an.
- Profil-Vorschau gegen den echten Zustand (Screenshot `phase11/*-profiles-plan.png`): plausibel, zum Beispiel GAME-004 "Auf dieser Windows-Edition nicht wirksam", bereits gesetzte Eingabehilfen-Tastenkürzel "Schon aktiv".
- Nicht ausgeführt: Anwenden von Update-Tweaks und Profilen auf dem Host (UAC, und Profile ändern viele Einstellungen auf einmal). Der Ablauf ist mit Benutzer-Tweaks gegen Fakes getestet.

## Phase 12: Dashboard

Quellen: Microsoft Learn `Win32_Processor`, `Win32_VideoController`, `Win32_PnPSignedDriver`, `Win32_PhysicalMemory` (SMBIOSMemoryType), `Win32_OperatingSystem` (Caption, LastBootUpTime), `MSFT_PhysicalDisk` (MediaType, BusType), `MSFT_Partition`, `GlobalMemoryStatusEx`, `PowerGetActiveScheme` und `PowerReadFriendlyName`, `Tbsi_GetDeviceInfo`, `Win32_DeviceGuard`, `MSFT_MpComputerStatus`, `wevtutil qe`.

Host-Prüfung 2026-10-18, nur lesend (`ascendedtweaks-cli --sysinfo`): Prozessor mit Kernen und Threads, Grafikkarte mit Hersteller, Treiberversion und -datum sowie Videospeicher über 4 GB (`qwMemorySize`), Arbeitsspeicher mit Typ DDR5, NVMe-Laufwerk mit freiem Platz, Windows 11 Home 25H2 mit Build und UBR, Installationsdatum, letzter Start; Startdauer ohne Administratorrechte nicht lesbar (wie erwartet). Zustandsanzeigen: kein Neustart ausstehend, Energieplan, Spielmodus, Defender-Echtzeitschutz aus mit zwei registrierten Virenschutzprogrammen. Ein Kartenleser mit Größe 0 wurde danach ausgeblendet.

## Phase 13: Export und Update

Quellen: Microsoft Learn `New-ItemProperty`, `Remove-ItemProperty`, `Set-Service`, `sc.exe config` (start= delayed-auto), `Enable-ScheduledTask`/`Disable-ScheduledTask`, `powercfg /setactive` und `/duplicatescheme`, Format von `.reg`-Dateien (Windows Registry Editor Version 5.00, `dword:`, `hex(2):`, `hex(7):`, `hex(b):`, `"Name"=-`, `[-Schlüssel]`), `about_Requires`. Ed25519: RFC 8032; Umsetzung BouncyCastle.Cryptography 2.4.0.

Host-Prüfung 2026-10-18:

- `--profile gaming --dry-run`: Plan ohne Änderung, versteckte Tweaks ohne `--allow-expert` übersprungen, GAME-004 auf Home nicht verfügbar.
- `--export ps1 --profile safe` und `--export reg --profile privacy`: Dateien erzeugt; beide `.ps1` laut PowerShell-Parser ohne Syntaxfehler (nicht ausgeführt, enthalten Maschinen-Tweaks).
- Rundlauf mit eigenem Profil aus PRIV-013, PRIV-014 und PRIV-016 (nur HKCU): exportiertes Skript angewendet, Status "Applied"; Rücksetz-Skript ausgeführt, Status "NotApplied", Registry identisch mit dem Stand davor. Dasselbe mit `reg import` der `.reg`-Dateien: Werte identisch, nur der vorher fehlende, leere Schlüssel blieb (dokumentiert). Testreste entfernt.
