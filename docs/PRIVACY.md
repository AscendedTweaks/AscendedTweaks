# Datenschutz

Wir sammeln nichts. Keine Konten, keine Telemetrie, keine Werbung. Protokolle liegen nur lokal auf deinem PC. Die einzigen Netzwerkzugriffe sind Funktionen, die du selbst startest: Latenztest, DNS-Test, Update-Suche.

## Was auf deinem PC gespeichert wird

| Ort | Inhalt |
|---|---|
| `%LOCALAPPDATA%\TweakPilot\settings.json` | Sprache, Design, Expertenmodus |
| `%LOCALAPPDATA%\TweakPilot\State` | Sicherungen, Journal und Ledger der Benutzer-Tweaks |
| `%LOCALAPPDATA%\TweakPilot\profiles`, `games.json` | Eigene Profile und Spielprofile |
| `%LOCALAPPDATA%\TweakPilot\Logs` | Protokolle (JSON-Zeilen, höchstens 10 Dateien zu je 5 MB). Benutzername und Profilpfad sind durch `%USERNAME%` und `%USERPROFILE%` ersetzt. |
| `%ProgramData%\TweakPilot` | Sicherungen und Protokolle der maschinenweiten Änderungen (nur Administratoren dürfen schreiben) |

Nichts davon wird gesendet. Protokolle lassen sich in den Einstellungen löschen.

## Netzwerkzugriffe

| Funktion | Ziel | Wann |
|---|---|---|
| Ping- und Jitter-Test | gewählter DNS-Server (1.1.1.1, 8.8.8.8 oder 9.9.9.9), ICMP | nur auf Knopfdruck |
| DNS-Cache leeren, DNS-Server ändern | keine Verbindung durch die App; Windows nutzt danach die neuen Server | nur auf Knopfdruck |
| Nach Update suchen | feste HTTPS-Adresse des Projekts, ohne Kennung und ohne Nutzerdaten | nur auf Knopfdruck; in dieser Version noch nicht eingerichtet |

Beim Start und im Leerlauf baut die App keine Verbindung auf (Messung in `tests/perf/report-host-26200.json`; Paketmitschnitt in der VM steht aus).
