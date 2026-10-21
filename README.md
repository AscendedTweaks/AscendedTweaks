# TweakPilot

Windows-Optimierung mit ehrlichen Angaben: Jede Änderung hat eine Quelle, eine Vorschau, eine Sicherung und lässt sich zurücksetzen. Viele Tweaks bringen wenig, manche nichts; die App sagt, was belegt ist.

Stand: Release-Kandidat 1.1.0-rc.1 (V2: Einfach-Modus, Presets mit Stufen, Wirkungsangaben, Gaming-Checks, Messung, neue Suche nach großen Dateien, fertiger Ordner auf dem Desktop). Name, Lizenz, Signierzertifikat, Update-Adresse und Rechtstexte sind noch offen; was nur du erledigen kannst, steht in `docs/DECISIONS.md`, Abschnitt "Offene Punkte".

## Benutzen

Den Ordner `TweakPilot` (vom Desktop oder aus `TweakPilot-1.1.0-rc.1-win-x64.zip`) öffnen und `TweakPilot.exe` starten; `START HERE.txt` erklärt Installieren, Zurücksetzen und Entfernen. Beim ersten Start wählst du "Installieren (empfohlen)" (pro Benutzer, ohne Administratorrechte) oder "Nur ausprobieren (portabel)". Die App startet im Einfach-Modus mit fünf Seiten (Start, Schneller, Gaming, Aufräumen, Privatsphäre); der Schalter "Experte" zeigt alles.

## Voraussetzungen

- Windows 10 22H2 (Build 19045) oder Windows 11, 64 Bit
- Zum Bauen: .NET 8 SDK (`global.json`)

## Bauen und prüfen

```
dotnet build
powershell -NoProfile -ExecutionPolicy RemoteSigned -File build.ps1
```

`build.ps1` ist das Qualitätstor: Build ohne Warnungen, alle Tests, Katalog-Linter, Kontrastprüfung, Formatprüfung, Pakettest in einem Temp-Ordner. `build.ps1 -Package` baut danach den fertigen Ordner `Desktop\TweakPilot` und die ZIP-Datei (Einzelheiten in `docs/BUILD.md`).

## Release

Empfohlen: `build.ps1 -Package` (Ordner und ZIP, siehe oben). Der ältere Weg mit Installer:

```
powershell -NoProfile -ExecutionPolicy RemoteSigned -File tools\release\Build-Release.ps1 [-CertificateThumbprint <Fingerabdruck>]
```

Erzeugt unter `artifacts\` die portable Version (ZIP, self-contained), den Installer (wenn Inno Setup 6 installiert ist) und `SHA256SUMS.txt`. Signiert wird nur mit angegebenem Zertifikat (`tools\release\Sign-Release.ps1`). `tools\release\New-ThirdPartyNotices.ps1` aktualisiert `THIRD-PARTY-NOTICES.md`.

## Aufbau

| Projekt | Aufgabe |
|---|---|
| `src/TweakPilot.Core` | Datenmodell, Aktionen, Whitelists, Profile |
| `src/TweakPilot.Catalog` | Eingebetteter Tweak-Katalog (JSON), Schema, Linter, vorgefertigte Profile |
| `src/TweakPilot.Engine` | Vorschau, Sicherung, Journal, Anwenden, Prüfen, Rollback, Zurücksetzen; Bereinigung, Debloat, Werkzeuge, Export, Update-Prüfung |
| `src/TweakPilot.Ipc` | Benannte Pipe zum Helfer, Befehls-Whitelist, `TweakService` |
| `src/TweakPilot.Helper` | Helfer mit Administratorrechten, nur auf Anforderung (UAC) |
| `src/TweakPilot.App` | WPF-Oberfläche |
| `src/TweakPilot.Cli` | `tweakpilot-cli.exe` mit derselben Engine |
| `tools/` | Katalog-Linter, Registry-Snapshot, Kontrastprüfung, Messwerkzeug, Update-Signierung, Release-Skripte |
| `tests/` | Unit-Tests, VM- und Host-Integrationstests, Leistungsmessung |

## Kommandozeile

```
tweakpilot-cli --list [--module PRIV] [--json]
tweakpilot-cli --status PRIV-001
tweakpilot-cli --apply PRIV-001,PRIV-013 [--dry-run] [--silent] [--allow-expert]
tweakpilot-cli --profile safe --dry-run
tweakpilot-cli --export ps1 --profile safe --out C:\Export\safe.ps1
tweakpilot-cli --revert PRIV-001 | --revert-all | --recover
tweakpilot-cli --large-scan        (große Dateien klassifiziert, nur lesend)
```

Exit-Codes: 0 Erfolg, 1 Fehler, 2 Administratorrechte fehlen, 3 Build nicht unterstützt, 4 Verifikation fehlgeschlagen (Rollback erfolgt), 5 ungültige Argumente, 6 Sicherung fehlgeschlagen.

## Dokumentation

| Datei | Inhalt |
|---|---|
| `docs/DECISIONS.md` | Annahmen je Phase und offene Punkte |
| `docs/ACCEPTANCE.md` | Abnahmeberichte der Phasen 1 bis 15 und V2-1 bis V2-8 |
| `docs/IMPACT-AUDIT.md` | Wirkung je Tweak (Spürbar, Klein, Unklar) mit Begründung |
| `docs/USABILITY.md` | Klickpfade und Usability-Test |
| `docs/BUILD.md` | Bauen, Paket, Bauausgaben |
| `docs/SOURCES.md` | Quellen und Host-Prüfungen je Tweak |
| `docs/REJECTED-TWEAKS.md` | Abgelehnte Tweaks und Abweichungen vom Auftrag |
| `docs/THREAT-MODEL.md`, `docs/SECURITY-REVIEW.md` | Bedrohungsmodell und Prüfung |
| `docs/TEST-MATRIX.md` | VM-Testmatrix |
| `docs/MEASUREMENT.md` | Messprotokoll für Leistungsaussagen |
| `docs/DESIGN.md`, `docs/ARCHITECTURE.md`, `docs/TWEAK-AUTHORING.md` | Gestaltung, Aufbau, neue Tweaks |
| `docs/PRIVACY.md` | Datenschutz |
| `CHANGELOG.md` (`docs/CHANGELOG.md`) | Änderungen |

Windows ist eine Marke der Microsoft Corporation. Diese Software steht in keiner Verbindung zu Microsoft, Epic Games oder anderen genannten Unternehmen.
