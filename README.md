# Ascended Tweaks

Open-source Windows tuning that previews every change and can undo everything.

## Download

**[Download AscendedTweaks.zip](https://github.com/AscendedTweaks/AscendedTweaks/releases/latest/download/AscendedTweaks.zip)** for Windows 10 22H2 (build 19045) or Windows 11, 64-bit.
The checksums are on the [latest release page](https://github.com/AscendedTweaks/AscendedTweaks/releases/latest).

1. Download `AscendedTweaks.zip` and `SHA256SUMS.txt`.
2. Unzip it and start `AscendedTweaks.exe`. No administrator rights are needed to start.
3. Choose "Install (recommended)" or "Just try it (portable)". `START HERE.txt` explains install, undo and removal.

Optional check before starting (PowerShell): `Get-FileHash .\AscendedTweaks.zip -Algorithm SHA256`.
The hash must match the line in `SHA256SUMS.txt`.

## What you get

- **Undo everything.** Every change is backed up first and can be reset with one click.
- **Preview first.** You see every value before and after, nothing changes without your confirmation.
- **No telemetry.** The app sends nothing. It only goes online when you start a check yourself.
- **Open source.** Every tweak names its source, so you can check what it does.

## Why does Windows warn me?

The files are not code-signed yet, so SmartScreen may show "Windows protected your PC". Check the hash as shown above,
then click "More info" > "Run anyway". You can also verify that the ZIP was built from this repository:

```
gh attestation verify AscendedTweaks.zip --repo AscendedTweaks/AscendedTweaks
```

Do not turn off Microsoft Defender or SmartScreen for this or any other app.

## What it does not do

Ascended Tweaks never turns off Defender, the firewall, UAC, SmartScreen or Windows Update. Deleted files and removed apps
cannot always be restored; the app says so before you confirm.

## Command line

```
ascendedtweaks-cli --list [--module PRIV] [--json]
ascendedtweaks-cli --apply PRIV-001,PRIV-013 [--dry-run]
ascendedtweaks-cli --revert PRIV-001 | --revert-all
```

## Build from source

Requires the .NET 8 SDK (see `global.json`).

```
dotnet build
powershell -NoProfile -ExecutionPolicy RemoteSigned -File build.ps1
```

`build.ps1` is the quality gate (build without warnings, tests, catalog lint, contrast and format checks, package test).
`build.ps1 -Package` builds the finished folder and the ZIP. Details: `docs/BUILD.md`.

| Folder | Content |
| --- | --- |
| `src/` | The app, the helper for administrator changes, the command line and the tweak catalog |
| `tests/` | Automated tests |
| `tools/` | Build, release and catalog tools |
| `site/` | The project website |
| `docs/` | Design notes, sources and privacy |

## License

MIT, see [LICENSE](LICENSE). Not affiliated with Microsoft or any game publisher.
