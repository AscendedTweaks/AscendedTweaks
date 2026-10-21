# Testmatrix (Abschnitt 11.2)

## VMs

| Build | Version | Home | Pro |
|---|---|---|---|
| 19045 | Windows 10 22H2 | offen | offen |
| 22631 | Windows 11 23H2 | offen | offen |
| 26100 | Windows 11 24H2 | offen | offen |
| höchster verfügbarer Build | Windows 11 | offen | offen |
| 26200 (Host, keine VM) | Windows 11 25H2 | teilweise: Benutzer-Tweaks, siehe `tests/vm/report-host-26200-*.json` | nicht vorhanden |

Hyper-V ist auf dem Entwicklungsrechner (Windows 11 Home) nicht verfügbar (offener Punkt 1). Die Matrix braucht einen Rechner mit Windows Pro oder eine andere Virtualisierung.

## Vorbereitung je VM

1. VM mit 4 vCPU und 8 GB Arbeitsspeicher anlegen (Referenz für die Leistungsmessung), Windows installieren, Updates einspielen.
2. .NET 8 SDK installieren und das Repository kopieren, `dotnet build` ausführen.
3. Prüfpunkt "sauber" anlegen (Hyper-V: `Checkpoint-VM -Name <VM> -SnapshotName sauber`).

## Ablauf je VM (PowerShell als Administrator)

```
.\tests\vm\Invoke-TestMatrix.ps1 -Phase Single        # Schritte 2 bis 7 für jeden verfügbaren Tweak
# Prüfpunkt "sauber" wiederherstellen
.\tests\vm\Invoke-TestMatrix.ps1 -Phase Module        # Schritt 8: pro Modul alles anwenden, alles zurücksetzen, Registry-Vergleich
# Prüfpunkt "sauber" wiederherstellen
.\tests\vm\Invoke-TestMatrix.ps1 -Phase RebootApply   # Schritt 9, Teil 1; danach von Hand neu starten
.\tests\vm\Invoke-TestMatrix.ps1 -Phase RebootVerify  # Schritt 9, Teil 2
.\tests\perf\Measure-AppPerformance.ps1 -AppPath <Release-Build>\TweakPilot.App.exe -Out perf.json
```

Jeder Lauf schreibt `tests/vm/report-<Build>-<Edition>-<Phase>.json`. Bestandene Einträge kommen in das Feld `verifiedOn` des Tweaks; nicht bestandene Tweaks werden korrigiert oder in `docs/REJECTED-TWEAKS.md` dokumentiert.

## Grenzen der Skripte

- Die Snapshots erfassen die Registry. Bei Befehls-Tweaks (netsh, fsutil, powercfg, DISM) und Aufgaben prüfen die Schritte 5 und 7 den Zustand über die Zustandserkennung der App; für einen unabhängigen Vergleich zusätzlich `powercfg /list`, `fsutil behavior query`, `schtasks /query` vor und nach dem Test sichern.
- Das Skript legt keine geplanten Aufgaben an und startet nicht selbst neu.
- Der Export (Abschnitt 10.4) wird in der VM zusätzlich so geprüft: `tweakpilot-cli --export ps1 --profile <name> --out x.ps1`, Skript ausführen, Snapshot mit dem Ergebnis der App vergleichen, Rücksetz-Skript ausführen, Snapshot gleich Ausgangszustand.

## Manuelle Prüfungen in der VM

- Tastatur-Durchlauf jeder Seite (Tab-Reihenfolge, Pfeiltasten in Listen, Leertaste schaltet, Enter öffnet Details, Fokus nach Dialogen), Narrator-Stichprobe, Windows-Hochkontrast, 1920 x 1080 bei 200 Prozent.
- Netzwerk-Mitschnitt (Wireshark) 5 Minuten im Leerlauf: keine Pakete der App.
- UAC-Ende-zu-Ende (offener Punkt 9) und Sichtprüfungen (offener Punkt 11).
