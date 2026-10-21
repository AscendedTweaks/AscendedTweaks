# Bedrohungsmodell

Stand Phase 2. Grundlage: Abschnitt 9.5 des Auftrags. Für jeden Angreifer gibt es Schutzmaßnahmen und das verbleibende Restrisiko.

## Schützenswerte Güter

- Administratorrechte des Helfer-Prozesses `TweakPilot.Helper.exe`.
- Integrität der Windows-Konfiguration (Registry, Dienste, Energiepläne).
- Sicherungen, Journal und Ledger unter `%ProgramData%\TweakPilot\State`, weil sie beim Zurücksetzen geschrieben werden.
- Vertrauen in die Anzeige: Vorschau und Protokoll müssen zeigen, was wirklich passiert.

## A: Schadprogramm mit Benutzerrechten will den Helfer missbrauchen

Ziel des Angreifers: Admin-Aktionen über den laufenden Helfer auslösen, ohne selbst eine UAC-Bestätigung zu bekommen.

| Schutzmaßnahme | Umsetzung |
|---|---|
| Kein Dauerdienst, keine Persistenz | Der Helfer läuft nur auf Anforderung (UAC) und endet nach 120 s Leerlauf, nach `Shutdown` oder wenn der startende Prozess endet. Es gibt keinen Autostart, keinen Dienst und keine geplante Aufgabe. |
| Zufälliger Pipe-Name | `TweakPilot.<SID>.<GUID>`. Die GUID erzeugt der Client erst beim Start. |
| Pipe-ACL | Geschützte DACL: nur die SID des startenden Benutzers (Lesen/Schreiben) und SYSTEM. Keine geerbten Einträge. |
| Client-Prüfung im Helfer | Die Prozess-ID des Clients muss der beim Start übergebenen Eltern-PID entsprechen. Das Client-Programm muss im Installationsverzeichnis des Helfers liegen. Die Benutzer-SID wird per Impersonation (Identifikationsstufe) bestätigt. In Release-Builds wird zusätzlich eine gültige Authenticode-Signatur vom selben Herausgeber wie der Helfer verlangt. |
| Server-Prüfung im Client | Der Client verbindet nur mit dem Prozess, den er selbst gestartet hat (`GetNamedPipeServerProcessId`). Ein Squatter auf dem Pipe-Namen wird abgewiesen. |
| Befehls-Whitelist | Nur `Ping`, `ApplyTweaks`, `RevertTweak`, `Capture`, `CreateRestorePoint`, `RecoverInterrupted`, `ScanCleanup`, `RunCleanup`, `RemoveApps`, `RunMaintenance`, `ReadDiskHealth` und `Shutdown` (Stand Phase 14). Es gibt keine freien Befehle, Pfade, Werte oder Skripte. Tweak-IDs werden gegen Format und eingebetteten Katalog geprüft. |
| Nur Maschinen-Tweaks und Benutzer-Richtlinien | Der Helfer lehnt Benutzer-Tweaks ab. `HelperRegistryAccess` sperrt jeden HKCU-Zugriff; einzige Ausnahme sind Benutzer-Richtlinien (`HKCU\Software\Policies\...`), die nach `HKEY_USERS\<SID>` umgeleitet werden. Die SID stammt aus der Client-Prüfung (Impersonation), nicht aus der Nachricht; ein Client kann so nur seine eigene Hive ändern. Direkte `HKU`-Pfade im Katalog sind nicht erlaubt, und ein Auftrag darf keine Bereiche mischen. |
| Erzeugte Autostart-Tweaks | IDs der Form `PERF-026/<Ort>/<Name>` werden nur für fünf feste Orte angenommen; der Name darf keine Steuerzeichen enthalten und höchstens 260 Zeichen lang sein. Der Tweak schreibt ausschließlich einen 12-Byte-Freigabewert unter `Explorer\StartupApproved`; er kann weder Programme noch Befehlszeilen setzen. |
| Pro-Spiel-Tweaks | `GAME-016/<EXE-Name>` schreibt im Helfer ausschließlich `Image File Execution Options\<EXE>\PerfOptions\CpuPriorityClass` = 3; der Name ist auf `[A-Za-z0-9 ._()-]{1,100}.exe` beschränkt, damit weder `Debugger` noch andere IFEO-Werte erreichbar sind. Bei Spielen mit Anti-Cheat ist die Option in der Oberfläche gesperrt. GAME-007/008 laufen nur im Benutzerbereich. |
| Spielstart | Die App startet Spiele ohne erhöhte Rechte und setzt die Priorität nur über die normale Prozess-Schnittstelle; verweigert Windows oder ein Anti-Cheat den Zugriff, wird nicht umgangen. |
| Wartung im Helfer (Phase 10) | Befehl `RunMaintenance` nimmt nur eine Aktion aus einer festen Liste (NetworkReset, ReTrim, Defrag) und bei Laufwerksaktionen einen einzelnen Buchstaben A bis Z; keine Pfade oder Argumente. Der Helfer liest den Medientyp selbst: ReTrim nur auf SSD, Defragmentieren nur auf HDD, unbekannt gesperrt. `ReadDiskHealth` liest nur. |
| DNS-Werte im Helfer (Phase 10) | Der Zustand für `dns.servers` und `nic.powersaving` wird vor dem Ausführen von netsh geprüft (Adapter-GUIDs, `IPAddress.TryParse` in Normalform, gesperrte Adressbereiche). netsh bekommt Argumente als Liste, nie als zusammengesetzte Befehlszeile. |
| Bereinigung im Helfer | Befehle `ScanCleanup` und `RunCleanup` nehmen nur eine Kategorie-ID aus der eingebetteten Liste und einen Modus; unbekannte JSON-Felder (zum Beispiel Pfade) werden abgelehnt. Benutzer-Kategorien lehnt der Helfer ab. Der Helfer kennt nur die Basisordner Windows, ProgramData und Systemlaufwerk, scannt selbst neu, folgt keinen Reparse Points (Prüfung am geöffneten Handle) und löscht nie in geschützten Ordnern. |
| Apps entfernen im Helfer | Befehl `RemoveApps` nimmt nur Paketnamen (Format geprüft, höchstens 60). Gesperrte Systemapps werden abgelehnt; Pakete außerhalb des Katalogs nur, wenn sie weder von Microsoft stammen noch systemsigniert sind. Der Helfer ermittelt die vollständigen Paketnamen selbst. |
| Aufgabenplanung | `schtasks.exe` nur aus System32, ohne Shell, Argumente einzeln; Aufgabenpfade nur unter `\Microsoft\Windows\` mit erlaubten Zeichen, keine relativen Teile; geschrieben werden nur `/ENABLE` und `/DISABLE`. |
| Protokoll-Härtung | Rahmen höchstens 1 MB, Protokollversion, streng steigender Zähler gegen Wiederholung, unbekannte JSON-Felder werden abgelehnt. Nach einem Protokollverstoß wird die Verbindung getrennt. |
| Zustandsdateien | `%ProgramData%\TweakPilot`: Administratoren und SYSTEM Vollzugriff, Benutzer nur Lesen. Die Vererbung ist abgeschaltet. Backup-Sets haben SHA-256-Prüfsummen, die vor jeder Wiederherstellung geprüft werden. |

Restrisiko:
- Schadcode, der bereits im Prozess der App oder CLI läuft (DLL-Injection mit Benutzerrechten), kann den Helfer über die legitime Verbindung steuern. Er bleibt aber auf die Whitelist und den Katalog beschränkt; die Wirkung entspricht dem, was der Nutzer in der App selbst auslösen kann.
- In Debug-Builds gibt es keine Signaturprüfung. Debug-Builds werden nicht ausgeliefert.
- Ein Angreifer mit Benutzerrechten kann `%LOCALAPPDATA%\TweakPilot` (Benutzer-Tweaks) verändern. Er hätte diese Rechte an der Registry ohnehin.

## B: Manipulierter Katalog oder Profil-Import

| Schutzmaßnahme | Umsetzung |
|---|---|
| Eingebetteter Katalog | Tweaks sind in die Assembly eingebettet und werden beim Laden gegen das JSON-Schema und den Linter geprüft. Ein ungültiger Katalog wird nicht geladen. Ab Phase 15 sind alle Assemblies signiert. |
| Allowlists | Schlüssel anlegen und löschen nur unterhalb von `RegistryPaths.KeyAllowlist`, geprüft im Linter und zur Laufzeit. Befehle nur aus `CommandWhitelist` mit erlaubten Werten, Programme nur aus `System32`, Argumente als Liste ohne Shell. |
| Pfadprüfung | Registry-Pfade ohne `..`, `/`, Steuerzeichen, leere Segmente. |
| Profil-Import (Phase 11) | Umgesetzt in `ProfileCodec`: höchstens 1 MB (die App liest nicht mehr als 1 MB plus ein Byte), strenges JSON ohne unbekannte Felder, Schemaversion 1, Name 1 bis 60 Zeichen ohne Steuerzeichen, 1 bis 500 Einträge ohne Doppelte, nur Katalog-IDs und geprüfte NET-004-Adressen (keine erzeugten IDs mit Pfaden oder Programmnamen), nur die Zielzustände "angewendet" und "nicht angewendet". Danach nur Vorschau; angewendet wird über den normalen Ablauf mit Vorschau-Dialog. Eigene gespeicherte Profile werden beim Lesen genauso geprüft. |

Restrisiko: Ein fehlerhafter, aber gültiger Tweak im Katalog (falscher Wert) wird angewendet. Gegenmaßnahmen sind die Prüfung jedes Tweaks nach Abschnitt 11.2/11.3, das Rücksetzen aus der Sicherung und die Verifikation nach dem Schreiben.

## C: Manipulierter Update-Download

| Schutzmaßnahme | Umsetzung (Phase 13, `UpdateChecker`) |
|---|---|
| Kein Auto-Update | Nur Prüfung auf Knopfdruck. |
| Signiertes Manifest | Ed25519-Signatur mit eingebettetem öffentlichen Schlüssel. Der private Schlüssel liegt nie im Repository. |
| Doppelte Prüfung des Installers | SHA-256 aus dem Manifest und Authenticode mit erwartetem Herausgeber, erst danach Start nach Bestätigung. |
| Datenschutz | Keine Nutzerdaten und keine eindeutige Kennung in der Anfrage. |
| Transport und Format | Nur HTTPS (auch nach Weiterleitungen), Größengrenzen 64 KB und 512 MB, Manifest mit festen Feldern (unbekannte abgelehnt), Version nur als 1.2.3. Ungültige öffentliche Schlüssel gelten als falsche Signatur. Bei jedem Fehler wird die heruntergeladene Datei gelöscht. |
| Nicht eingerichtet | Solange Adresse, Schlüssel oder Herausgeber fehlen, sendet die App keine Anfrage. |

Restrisiko: Kompromittierung des Signaturschlüssels. Gegenmaßnahme: Schlüssel offline aufbewahren, Rotation über ein neues App-Release.

## Offene Punkte

- Die Signaturprüfung des Clients ist erst mit dem Signierzertifikat (Phase 15) wirksam.
- Ein Penetrationstest des Helfers in der VM (Fremd-Client, manipulierte Nachrichten) folgt in Phase 14. Die Unit-Tests decken diese Fälle bereits ab, aber nur im eigenen Prozess.
