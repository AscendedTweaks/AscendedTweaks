# Bauen und verpacken

## Qualitätstor

```
powershell -NoProfile -ExecutionPolicy RemoteSigned -File build.ps1
```

Build ohne Warnungen, alle Tests, Katalog-Linter, Kontrastprüfung, Formatprüfung und der Pakettest (`tests\package\Test-Package.ps1`, läuft nur in einem Temp-Ordner mit Platzhalter-Programmen).

## Fertiger Ordner auf dem Desktop (V2 Abschnitt 7)

```
powershell -NoProfile -ExecutionPolicy RemoteSigned -File build.ps1 -Package
```

`package.ps1` veröffentlicht App, Helfer und Kommandozeile als einzelne Dateien ohne installiertes .NET (`win-x64`, self-contained, ohne `.pdb`), bettet den SHA-256 des Helfers in App und Kommandozeile ein und schreibt:

```
Desktop\AscendedTweaks\
  START HERE.txt
  AscendedTweaks.exe
  app\AscendedTweaks.Helper.exe
  app\ascendedtweaks-cli.exe
  Docs\Privacy.md, Licenses.md, Changelog.md, Sources.md, Checksums.txt
  .ascendedtweaks-package        (versteckte Markierung)
artifacts\AscendedTweaks.zip   (gleicher Inhalt; immer derselbe Name, damit der Link .../releases/latest/download/AscendedTweaks.zip dauerhaft funktioniert)
```

Der Zielordner wird nur geleert, wenn er die Markierung `.ascendedtweaks-package` trägt. Ein gleichnamiger Ordner ohne Markierung bleibt unberührt; das Paket landet dann in `Ascended Tweaks (2)` (oder der nächsten freien Nummer). `-Destination` wählt einen anderen Zielordner, `-PublishDir` überspringt das Veröffentlichen (Test-Hook).

## Was nicht in den Quellordner gehört

Diese Ausgaben entstehen beim Bauen und werden weder weitergegeben noch versioniert (siehe `.gitignore`):

- `bin\`, `obj\` in jedem Projekt (Standard von .NET; `dotnet clean` entfernt sie)
- `artifacts\` (Veröffentlichungen, ZIP-Dateien, Screenshots der Phasen, Sicherung von V1)
- `TestResults\` (Abdeckung)

Der fertige Ordner liegt außerhalb des Quellordners auf dem Desktop.

## Installer (optional)

`installer\AscendedTweaks.iss` (Inno Setup 6) übernimmt den Paketordner als zweiten Weg mit Administratorrechten. Auf dem Entwicklungsrechner ist Inno Setup nicht installiert; der Installer wird dort nicht gebaut. Der empfohlene Weg ist die Installation pro Benutzer aus der App ("Installieren (empfohlen)").
