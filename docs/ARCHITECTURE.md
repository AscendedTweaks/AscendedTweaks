# Architektur (Stand Phase 2)

## Projekte und Abhängigkeiten

```
TweakPilot.Cli ─────┐
TweakPilot.Helper ──┼──► TweakPilot.Ipc ──► TweakPilot.Engine ──► TweakPilot.Core
                    └──► TweakPilot.Catalog ─────────────────────────► (Core)
```

- **Core** (`net8.0`, keine Windows-APIs): Tweak-Modell, Aktionen, Erkennungsbedingungen, `RegistryData`, Schnittstellen (`IRegistryAccess`, `IServiceAccess`, `IPowerPlanAccess`, `ICommandRunner`, `ISystemEnvironment`, `ITweakCatalog`), Fehlertypen, Versionsprüfung, Befehls-Whitelist, Registry-Allowlist, JSON-Einstellungen.
- **Catalog** (`net8.0`): eingebettete Tweak-JSONs, `tweak.schema.json`, Sprachdateien, Loader mit Schema-Prüfung (JsonSchema.Net) und Linter.
- **Engine** (`net8.0-windows`): Executors je Aktionstyp, Zustandserkennung, Transaktionssteuerung, Backup-Sets, Journal, Ledger, Windows-Implementierungen der Schnittstellen.
- **Ipc** (`net8.0-windows`): Protokoll (Rahmen, DTOs), Named-Pipe-Server und -Client, Client-Prüfung, `EngineHelperHandler` und `TweakService`. `TweakService` ist der einzige Einstieg für App und CLI: Benutzer-Tweaks laufen lokal, Maschinen-Tweaks im Helfer; Vorschau und Status brauchen nie Admin.
- **Helper** (`TweakPilot.Helper.exe`, `requireAdministrator`): startet nur auf Anforderung, setzt die ACL von `%ProgramData%\TweakPilot`, bedient die Pipe und endet nach 120 s Leerlauf oder mit dem startenden Prozess.
- **Cli** (`tweakpilot-cli.exe`, `asInvoker`): Kommandozeile über `TweakService`.
- **tools/catalog-lint**, **tools/registry-snapshot**: Entwicklungswerkzeuge, nicht ausgeliefert.

Die Engine kennt den Katalog nur über `ITweakCatalog`. Die Oberfläche (ab Phase 3) und der Helfer (ab Phase 2) nutzen dieselbe Engine; es gibt keinen zweiten Codepfad.

## Ablauf `ApplyBatch`

1. **Vorprüfung** (`Plan`): Build-Freigabe (S11), Build-Bereich des Tweaks, Edition, Admin-Bedarf, Zustandserkennung (nicht verfügbar oder bereits angewendet), Konflikte (zwei Tweaks auf derselben Stelle, auch gegen bereits angewendete Tweaks im Ledger).
2. **Vorschau**: `Describe` jeder Aktion liest nur. Identisch für Dry-Run und echtes Anwenden.
3. **Wiederherstellungspunkt** (best effort, ab Phase 2). Ohne Punkt nur nach ausdrücklicher Bestätigung.
4. **Sicherung**: `Capture` aller Aktionen, Backup-Set mit Prüfsummen und `.reg`-Export, Journal-Eintrag `Pending`. Schlägt das fehl, wird nichts geändert.
5. **Pro Tweak**: Journal `Applying`, jede Aktion wird vor der Ausführung für den Rollback vorgemerkt, `Apply`, danach `Verify` jeder Aktion und Zustandserkennung. Ledger-Eintrag, Journal `Applied`.
6. **Fehler**: Rollback aller begonnenen Aktionen in umgekehrter Reihenfolge aus den gesicherten Originalwerten, Prüfung auf Originalzustand, Journal `RolledBack` oder `RollbackFailed` (mit Pfad zum Backup-Set).
7. **Abschluss**: Journal `Committed`.
8. **Wiederaufnahme**: Beim Start meldet `GetInterruptedBatches` offene Batches; `RecoverInterrupted` stellt sie aus dem Backup-Set wieder her (Prüfsummen werden vorher geprüft).

## Zustandsdateien

```
<Root>\journal.json
<Root>\ledger.json
<Root>\Backups\<yyyyMMdd-HHmmss>-<guid8>\
    manifest.json  values.json  services.json  power.json  commands.json
    export\<TweakId>.reg
    checksums.json
```

Root: `%LOCALAPPDATA%\TweakPilot\State` (Benutzer-Tweaks) bzw. `%ProgramData%\TweakPilot\State` (Maschinen-Tweaks). Schreiben immer atomar (temporäre Datei, dann Ersetzen). Beschädigte Dateien werden gemeldet, nie still zurückgesetzt.

## Prozesse (Phase 2)

1. App oder CLI (Standardrechte) liest Zustand und Vorschau selbst.
2. Beim ersten Maschinen-Schritt erzeugt der Client einen Pipe-Namen `TweakPilot.<SID>.<GUID>` und startet den Helfer per `runas` (UAC).
3. Der Helfer prüft Argumente, setzt die ACL, erstellt die Pipe (nur Benutzer-SID und SYSTEM) und prüft den verbindenden Client (PID, Pfad, Benutzer, in Release die Signatur).
4. Der Client prüft, dass die Pipe vom gestarteten Helfer kommt (Server-PID).
5. Anfragen: Rahmen mit Länge plus JSON, Version, steigender Zähler.

Siehe `docs/THREAT-MODEL.md`.

## Sicherheit in Phase 1

- Registry-Pfade werden im Linter und vor jedem Zugriff geprüft (kein `..`, kein `/`, keine Steuerzeichen).
- Schlüssel anlegen und löschen nur unterhalb der Allowlist, im Linter und zur Laufzeit geprüft.
- Befehle nur aus der Whitelist: Programm immer aus dem Systemverzeichnis, Argumente als Liste, einziger Platzhalter wird nur durch erlaubte Werte ersetzt, kein Shell-Aufruf.
- ACLs für `%ProgramData%\TweakPilot\State`, Helfer-Prozess und Named Pipe folgen in Phase 2.
