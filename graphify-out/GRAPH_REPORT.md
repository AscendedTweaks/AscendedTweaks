# Graph Report - TweakPilot  (2026-10-17)

## Corpus Check
- Corpus is ~33,981 words - fits in a single context window. You may not need a graph.

## Summary
- 1359 nodes · 3568 edges · 73 communities (64 shown, 9 thin omitted)
- Extraction: 82% EXTRACTED · 18% INFERRED · 0% AMBIGUOUS · INFERRED: 653 edges (avg confidence: 0.84)
- Token cost: 116,539 input · 0 output

## Community Hubs (Navigation)
- Engine-Tests
- Projekt-Namespaces
- Energieplan und P/Invoke
- Projektdateien und Solution
- Zustandserkennung
- Registry-Fakes und HKCU-Sperre
- Registry-Werte und Literale
- Registry-Schlüssel anlegen
- Registry-Werte löschen und wiederherstellen
- Systemumgebung und Edition
- Wiederherstellungspunkte
- Registry-Schlüssel löschen
- JSON-Konverter
- Rücksetzen und Konflikte
- Tweak-Definition
- Dienst-Executor
- CLI-Host
- Befehls-Executor
- Executor-Tests
- Katalog-Tests
- Katalog-Linter
- Echte-Registry-Integrationstests
- Registry-Wert setzen
- Tweak-Service-Koordinator
- Protokollierung
- Helfer-Handler
- Helfer-Client
- Executor-Basis
- Engine-Transaktion
- Katalog-Loader
- Journal- und Ledger-Dokumente
- Enums und Nachweisstufen
- Architektur-Komponenten
- Journal-Batch-Status
- Zustandsspeicher
- Batch-Ergebnis-DTO
- Test-Harness
- Helfer-Start und Identität
- Dienst-Fakes und Starttypen
- Pipe-Server-Schleife
- Abgelehnte Tweaks
- Client-Prüfung
- Katalog-Objekt
- Befehlsausführung
- Fehlerarten
- Batch-Ergebnis
- Zeit und Manifest
- CLI-Argumente
- Sicherungs-Schnittstelle
- Vorprüfungs-Ergebnisse
- Transaktionskonzept (Doku)
- Windows-Dienststeuerung
- Versionsprüfung
- Batch-Kontext
- Design-System
- IPC-Protokoll-Regeln
- Befehls-Whitelist
- Aktionsmodelle
- Batch-Ausgänge
- JSON-Lines-Log
- Entscheidungen und Nachweise
- Vorschau-Zeilen
- Reg-Export
- Journal-Tweak-Status
- Tweak-Autorenregeln
- Changelog und Datenschutz-Tweaks
- UI-Komponenten (Doku)
- Executor-Zuordnung
- IPC-Antwort
- Apply-Anfrage
- IPC-Anfrage

## God Nodes (most connected - your core abstractions)
1. `RegHive` - 78 edges
2. `TweakPilotException` - 59 edges
3. `TweakDefinition` - 56 edges
4. `TweakPilot.Core` - 46 edges
5. `RegistryData` - 41 edges
6. `EngineTests` - 41 edges
7. `TweakEngine` - 35 edges
8. `CatalogTests` - 28 edges
9. `TweakAction` - 27 edges
10. `ActionCapture` - 27 edges

## Surprising Connections (you probably didn't know these)
- `TweakPilot.Engine` --references--> `System.ServiceProcess.ServiceController 8.0.1 (MIT)`  [INFERRED]
  docs/ARCHITECTURE.md → THIRD-PARTY-NOTICES.md
- `FakeRegistry` --implements--> `IRegistryAccess`  [EXTRACTED]
  tests/TweakPilot.Engine.Tests/Fakes.cs → src/TweakPilot.Core/Abstractions.cs
- `FakeServices` --implements--> `IServiceAccess`  [EXTRACTED]
  tests/TweakPilot.Engine.Tests/Fakes.cs → src/TweakPilot.Core/Abstractions.cs
- `ListCatalog` --implements--> `ITweakCatalog`  [EXTRACTED]
  tests/TweakPilot.Engine.Tests/Fakes.cs → src/TweakPilot.Core/Abstractions.cs
- `FakeRegistry` --references--> `RegistryData`  [EXTRACTED]
  tests/TweakPilot.Engine.Tests/Fakes.cs → src/TweakPilot.Core/RegistryData.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **ApplyBatch transactional safety (backup, journal, ledger, rollback, recovery)** — docs_architecture_applybatch, docs_architecture_backup_set, docs_architecture_journal, docs_architecture_ledger, docs_architecture_rollback, docs_architecture_recoverinterrupted [EXTRACTED 1.00]
- **Phase 1 sample tweaks** — docs_sources_priv_001, docs_sources_priv_013, docs_sources_perf_016, docs_sources_ui_001, docs_sources_perf_001 [EXTRACTED 1.00]
- **Honest impact reporting principle** — docs_decisions_umstritten_without_measurement, docs_design_risk_impact_badges, docs_design_honesty_banner, docs_design_dos_and_donts, docs_tweak_authoring_linter_rules [INFERRED 0.85]

## Communities (73 total, 9 thin omitted)

### Community 0 - "Engine-Tests"
Cohesion: 0.07
Nodes (21): ConsoleProgress, RegistryKeyExistsCondition, Hive, Path, RiskLevel, Caution, Expert, Safe (+13 more)

### Community 1 - "Projekt-Namespaces"
Cohesion: 0.06
Nodes (14): TweakPilot.Engine.Logging, TweakPilot.Catalog, TweakPilot.Engine.State, TweakPilot.Catalog.Tests, TweakPilot.Engine.Tests, TweakPilot.Cli, TweakPilot.Engine, TweakPilot.Engine.Windows (+6 more)

### Community 2 - "Energieplan und P/Invoke"
Cohesion: 0.07
Nodes (17): IPowerPlanAccess, PowerPlanCapture, BeforeActive, CreatedScheme, Target, PowerPlanExecutor, NativeMethods, ServiceDelayedAutoStartInfo (+9 more)

### Community 3 - "Projektdateien und Solution"
Cohesion: 0.06
Nodes (32): JsonSchema.Net, System.Management, System.ServiceProcess.ServiceController, net8.0, Microsoft.NET.Sdk, net8.0-windows, Microsoft.NET.Sdk, net8.0 (+24 more)

### Community 4 - "Zustandserkennung"
Cohesion: 0.07
Nodes (33): AllOfCondition, Conditions, CommandQueryEqualsCondition, Command, Value, DetectCondition, DetectTypeNames, PowerPlanActiveCondition (+25 more)

### Community 5 - "Registry-Fakes und HKCU-Sperre"
Cohesion: 0.14
Nodes (9): RegHive, HKCU, HKLM, MachineOnlyRegistryAccess, FakeRegistry, FailWrite, IgnoreWrite, WriteCount (+1 more)

### Community 6 - "Registry-Werte und Literale"
Cohesion: 0.13
Nodes (9): RegistryData, Kind, Lines, Number, Text, RegLiteral, Lines, Number (+1 more)

### Community 7 - "Registry-Schlüssel anlegen"
Cohesion: 0.13
Nodes (10): IRegistryAccess, RegistryPaths, RegistryKeyCapture, DefaultBefore, DefaultWritten, Hive, MissingKeys, Path (+2 more)

### Community 8 - "Registry-Werte löschen und wiederherstellen"
Cohesion: 0.11
Nodes (16): RegistryDeleteValueAction, Hive, Name, Path, RegistryValueCapture, Before, Hive, MissingKeys (+8 more)

### Community 9 - "Systemumgebung und Edition"
Cohesion: 0.07
Nodes (28): ISystemEnvironment, Build, Edition, IsElevated, SystemDirectory, Ubr, WindowsEdition, Education (+20 more)

### Community 10 - "Wiederherstellungspunkte"
Cohesion: 0.11
Nodes (18): EngineFactory, EngineText, IRestorePointService, NoRestorePointService, PresetRestorePointService, RestorePointResult, IsAcceptable, RestorePointStatus (+10 more)

### Community 11 - "Registry-Schlüssel löschen"
Cohesion: 0.12
Nodes (15): RegistryDeleteKeyAction, Hive, Path, RegistryTreeCapture, Hive, Path, Tree, RegistryTreeNode (+7 more)

### Community 12 - "JSON-Konverter"
Cohesion: 0.15
Nodes (9): CoreJson, Options, DiscriminatedConverter, RegLiteralConverter, RevertSpecConverter, ActionTypeNames, RevertSpec, Actions (+1 more)

### Community 13 - "Rücksetzen und Konflikte"
Cohesion: 0.10
Nodes (19): ConflictDecision, Cancel, KeepCurrent, RestoreOriginal, RevertConflict, RevertOutcome, Cancelled, Failed (+11 more)

### Community 14 - "Tweak-Definition"
Cohesion: 0.09
Nodes (23): TweakDefinition, Apply, Builds, Category, DescriptionKey, Detect, Editions, Evidence (+15 more)

### Community 15 - "Dienst-Executor"
Cohesion: 0.16
Nodes (9): IServiceAccess, ServiceCapture, BeforeDelayed, BeforeRunning, BeforeStartType, Service, WrittenDelayed, WrittenStartType (+1 more)

### Community 16 - "CLI-Host"
Cohesion: 0.16
Nodes (3): Host, Culture, Program

### Community 17 - "Befehls-Executor"
Cohesion: 0.19
Nodes (8): CommandAction, Command, Value, CommandCapture, BeforeValue, Command, WrittenValue, CommandExecutor

### Community 18 - "Executor-Tests"
Cohesion: 0.18
Nodes (7): TweakPilotException, Kind, TweakId, PowerPlanSetAction, DuplicateFrom, SchemeGuid, ExecutorTests

### Community 20 - "Katalog-Linter"
Cohesion: 0.23
Nodes (4): CatalogLinter, TextBundle, Culture, Texts

### Community 22 - "Registry-Wert setzen"
Cohesion: 0.14
Nodes (14): RegValueKind, Binary, DWord, ExpandString, MultiString, QWord, String, RegistrySetValueAction (+6 more)

### Community 23 - "Tweak-Service-Koordinator"
Cohesion: 0.19
Nodes (10): ITweakCatalog, Tweaks, BatchPlan, Excluded, Items, Ready, RequiresReboot, TweakService (+2 more)

### Community 24 - "Protokollierung"
Cohesion: 0.12
Nodes (17): ILogSink, LogLevel, Debug, Error, Information, Warning, LogRecord, DurationMs (+9 more)

### Community 25 - "Helfer-Handler"
Cohesion: 0.15
Nodes (12): EngineHelperHandler, IHelperHandler, IpcProtocolException, PingResponse, Elevated, ProcessId, RecoverRequest, BatchId (+4 more)

### Community 26 - "Helfer-Client"
Cohesion: 0.20
Nodes (4): TweakStatus, HelperClient, Launches, IHelperChannel

### Community 27 - "Executor-Basis"
Cohesion: 0.22
Nodes (7): TweakAction, ActionExecutor, ActionType, CaptureType, IActionExecutor, ActionType, CaptureType

### Community 28 - "Engine-Transaktion"
Cohesion: 0.26
Nodes (3): TweakEngine, AppVersion, Store

### Community 30 - "Journal- und Ledger-Dokumente"
Cohesion: 0.16
Nodes (12): JournalDocument, Batches, Version, LedgerDocument, Entries, Version, LedgerEntry, AppliedUtc (+4 more)

### Community 31 - "Enums und Nachweisstufen"
Cohesion: 0.13
Nodes (13): EvidenceLevel, Disputed, Minor, Placebo, Proven, ServiceStartSetting, Automatic, AutomaticDelayed (+5 more)

### Community 32 - "Architektur-Komponenten"
Cohesion: 0.20
Nodes (11): ITweakCatalog, tweak.schema.json, TweakPilot.Catalog, TweakPilot.Cli (tweakpilot-cli.exe), TweakPilot.Core, TweakPilot.Engine, Third-Party Notices (Drittanbieter-Lizenzen), FluentAssertions 6.12.2 (Apache-2.0, test only) (+3 more)

### Community 33 - "Journal-Batch-Status"
Cohesion: 0.14
Nodes (14): BatchStatus, Applying, Committed, Pending, RollbackFailed, RolledBack, JournalBatch, BackupSetId (+6 more)

### Community 34 - "Zustandsspeicher"
Cohesion: 0.24
Nodes (5): StateStore, BackupsDirectory, DefaultMachineRoot, DefaultUserRoot, Root

### Community 35 - "Batch-Ergebnis-DTO"
Cohesion: 0.14
Nodes (14): BatchResultDto, AppliedTweaks, BackupSetPath, BatchId, Error, ErrorKind, Excluded, FailedTweakId (+6 more)

### Community 36 - "Test-Harness"
Cohesion: 0.14
Nodes (11): Harness, Commands, Environment, Executors, Power, Registry, RestorePoints, Services (+3 more)

### Community 37 - "Helfer-Start und Identität"
Cohesion: 0.18
Nodes (9): ClientVerifierOptions, AllowedDirectory, ExpectedProcessId, ExpectedUser, HelperImagePath, RequireSignature, ElevatedHelperLauncher, HelperPath (+1 more)

### Community 38 - "Dienst-Fakes und Starttypen"
Cohesion: 0.18
Nodes (11): ServiceConfig, ServiceStartType, Automatic, Boot, Disabled, Manual, System, FakeServices (+3 more)

### Community 40 - "Abgelehnte Tweaks"
Cohesion: 0.18
Nodes (11): Legacy script FortniteOptimizer.ps1, Rejected Tweaks, Mass service disabling from forum lists (rejected), MMCSS values as 'FPS boost' (rejected), Nagle tweaks TcpAckFrequency/TCPNoDelay (rejected), Registry cleaner (rejected), Disabling Defender/Firewall/UAC/SmartScreen/Update (rejected, rule S5), Manual WinSxS deletion (rejected, use DISM CLEAN-014) (+3 more)

### Community 41 - "Client-Prüfung"
Cohesion: 0.21
Nodes (8): IClientVerifier, WindowsClientVerifier, HelperServerOptions, ClientUser, IdleTimeout, ParentProcessId, PipeName, Verifier

### Community 42 - "Katalog-Objekt"
Cohesion: 0.20
Nodes (9): CatalogLoadResult, Catalog, Issues, IsValid, LintIssue, TweakCatalog, AllIncludingPlacebo, Texts (+1 more)

### Community 43 - "Befehlsausführung"
Cohesion: 0.24
Nodes (8): CommandResult, ICommandRunner, ProcessCommandRunner, FakeCommands, BrokenOutput, Calls, LastAccessValue, WriteCount

### Community 44 - "Fehlerarten"
Cohesion: 0.17
Nodes (11): ErrorKind, BackupFailed, Cancelled, CatalogInvalid, General, NotSupportedOnBuild, PermissionDenied, RollbackFailed (+3 more)

### Community 45 - "Batch-Ergebnis"
Cohesion: 0.17
Nodes (11): BatchResult, AppliedTweaks, BackupSetPath, BatchId, Error, FailedTweakId, Outcome, Plan (+3 more)

### Community 46 - "Zeit und Manifest"
Cohesion: 0.20
Nodes (8): BackupManifest, AppVersion, BatchId, CreatedUtc, TweakIds, WindowsBuild, WindowsUbr, FakeTime

### Community 47 - "CLI-Argumente"
Cohesion: 0.18
Nodes (10): Arguments, AllowExpert, Command, DryRun, Ids, Json, Language, NoRestorePoint (+2 more)

### Community 48 - "Sicherungs-Schnittstelle"
Cohesion: 0.27
Nodes (3): ActionCapture, Applied, CaptureView

### Community 49 - "Vorprüfungs-Ergebnisse"
Cohesion: 0.18
Nodes (11): PreflightOutcome, AdminRequired, AlreadyApplied, Conflict, EditionNotSupported, ExpertLockedOnUntestedBuild, NotSupportedOnBuild, ReadOnlyMode (+3 more)

### Community 50 - "Transaktionskonzept (Doku)"
Cohesion: 0.27
Nodes (6): Architecture (Phase 1), Backup-Set (checksums + .reg export), Write-Ahead Journal (journal.json), Ledger (ledger.json), RecoverInterrupted / GetInterruptedBatches, DeferredRestorePointService

### Community 52 - "Versionsprüfung"
Cohesion: 0.22
Nodes (6): BuildSupport, Supported, Unsupported, Untested, SupportPolicy, PreflightEntry

### Community 53 - "Batch-Kontext"
Cohesion: 0.20
Nodes (9): BackupEntry, ActionIndex, Capture, TweakId, BatchContext, Batch, Entries, Journal (+1 more)

### Community 54 - "Design-System"
Cohesion: 0.22
Nodes (7): DESIGN.md - TweakPilot, voltagent/awesome-design-md format, Change bar (Discard/Preview/Apply), Color Palette & Tokens (Dark/Light), tools/contrast-check, Three-step expert warning dialog, Responsive behavior (nav collapse under 1000, 200% scaling)

### Community 56 - "Befehls-Whitelist"
Cohesion: 0.25
Nodes (8): CommandDefinition, AllowedValues, Executable, Id, QueryArguments, QueryPattern, SetArguments, CommandWhitelist

### Community 57 - "Aktionsmodelle"
Cohesion: 0.22
Nodes (8): RegistryCreateKeyAction, DefaultValue, Hive, Path, ServiceSetStartTypeAction, Service, StartType, StopNow

### Community 58 - "Batch-Ausgänge"
Cohesion: 0.22
Nodes (9): BatchOutcome, Aborted, BackupFailed, BlockedByInterruptedBatch, Committed, DryRun, NothingToDo, RollbackFailed (+1 more)

### Community 59 - "JSON-Lines-Log"
Cohesion: 0.31
Nodes (4): JsonLinesLog, CurrentFile, DefaultMachineDirectory, DefaultUserDirectory

### Community 60 - "Entscheidungen und Nachweise"
Cohesion: 0.36
Nodes (8): Decisions and Assumptions, SupportPolicy.HighestTestedBuild = 26200, Open point: VM test matrix, Sources and Verification Records, Phase 1 test environment (Win 11 25H2 Build 26200 Home), UI-001 Classic Context Menu, Tweak Authoring Guide, Per-tweak test checklist (section 11.3)

### Community 61 - "Vorschau-Zeilen"
Cohesion: 0.25
Nodes (8): PlanItem, ActionType, Current, New, Target, TweakId, ValueKind, ValueName

### Community 63 - "Journal-Tweak-Status"
Cohesion: 0.25
Nodes (8): JournalTweak, Id, Status, TweakJournalStatus, Applied, Applying, Pending, RolledBack

### Community 64 - "Tweak-Autorenregeln"
Cohesion: 0.38
Nodes (3): tools/catalog-lint, Command Whitelist, Registry Key Allowlist (RegistryPaths.KeyAllowlist)

### Community 65 - "Changelog und Datenschutz-Tweaks"
Cohesion: 0.33
Nodes (6): tools/registry-snapshot, Changelog, Unreleased: Phase 1 additions, Microsoft Learn: Manage connections from Windows OS components to Microsoft services, PRIV-001 Disable Advertising ID, PRIV-013 Disable Feedback Prompts

### Community 66 - "UI-Komponenten (Doku)"
Cohesion: 0.33
Nodes (5): 'Mehr Details' side panel, Risk and Impact Badges, Tweak Card component, Typography (Segoe UI Variable), Language files de-DE.json / en-US.json

### Community 68 - "Executor-Zuordnung"
Cohesion: 0.33
Nodes (5): ExecutorSet, Commands, Power, Registry, Services

### Community 69 - "IPC-Antwort"
Cohesion: 0.33
Nodes (6): IpcResponse, Error, ErrorKind, Ok, Payload, Sequence

### Community 70 - "Apply-Anfrage"
Cohesion: 0.40
Nodes (5): ApplyTweaksRequest, DryRun, Ids, ProceedWithoutRestorePoint, RestorePointHandled

### Community 71 - "IPC-Anfrage"
Cohesion: 0.40
Nodes (5): IpcRequest, Command, Payload, Sequence, Version

## Knowledge Gaps
- **449 isolated node(s):** `Culture`, `Texts`, `Tweaks`, `AllIncludingPlacebo`, `Texts` (+444 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 503 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **9 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `TweakPilot.Core` connect `Projekt-Namespaces` to `Zustandserkennung`, `Katalog-Objekt`, `Befehlsausführung`, `JSON-Konverter`, `Fehlerarten`, `Wiederherstellungspunkte`, `Versionsprüfung`, `Befehls-Whitelist`, `Protokollierung`, `Aktionsmodelle`, `Enums und Nachweisstufen`?**
  _High betweenness centrality (0.111) - this node is a cross-community bridge._
- **Are the 39 inferred relationships involving `TweakPilotException` (e.g. with `.LoadEmbedded()` and `.ReadResource()`) actually correct?**
  _`TweakPilotException` has 39 INFERRED edges - model-reasoned connections that need verification._
- **What connects `Culture`, `Texts`, `Tweaks` to the rest of the system?**
  _449 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Engine-Tests` be split into smaller, more focused modules?**
  _Cohesion score 0.07265917602996255 - nodes in this community are weakly interconnected._
- **Why does `TweakPilotException` connect `Executor-Tests` to `Engine-Tests`, `Energieplan und P/Invoke`, `Registry-Fakes und HKCU-Sperre`, `Registry-Werte und Literale`, `Registry-Schlüssel anlegen`, `Rücksetzen und Konflikte`, `Dienst-Executor`, `Befehls-Executor`, `Echte-Registry-Integrationstests`, `Registry-Wert setzen`, `Tweak-Service-Koordinator`, `Helfer-Handler`, `Helfer-Client`, `Executor-Basis`, `Engine-Transaktion`, `Katalog-Loader`, `Zustandsspeicher`, `Helfer-Start und Identität`, `Befehlsausführung`, `Fehlerarten`, `Batch-Ergebnis`, `Windows-Dienststeuerung`, `Executor-Zuordnung`?**
  _High betweenness centrality (0.081) - this node is a cross-community bridge._
- **Should `Projekt-Namespaces` be split into smaller, more focused modules?**
  _Cohesion score 0.05760905760905761 - nodes in this community are weakly interconnected._
- **Why does `TweakDefinition` connect `Tweak-Definition` to `Engine-Tests`, `Zustandserkennung`, `Registry-Schlüssel anlegen`, `Systemumgebung und Edition`, `Katalog-Objekt`, `JSON-Konverter`, `CLI-Host`, `Katalog-Linter`, `Versionsprüfung`, `Tweak-Service-Koordinator`, `Helfer-Handler`, `Executor-Basis`, `Engine-Transaktion`, `Katalog-Loader`, `Enums und Nachweisstufen`?**
  _High betweenness centrality (0.064) - this node is a cross-community bridge._