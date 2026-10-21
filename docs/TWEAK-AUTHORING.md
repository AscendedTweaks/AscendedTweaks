# Tweaks schreiben

Tweaks sind Daten. Ein neuer Tweak braucht keine Code-Änderung, solange er einen vorhandenen Aktionstyp nutzt.

## Ablauf

1. Datei `src/TweakPilot.Catalog/tweaks/<modul>/<ID>.json` anlegen. Der Dateiname ist die ID.
2. Texte `tweak.<ID>.name`, `.desc`, `.side` in `tweaks/i18n/de-DE.json` und `en-US.json` eintragen. "Was funktioniert danach nicht mehr?" (`.side`) ist nie leer.
3. `detect` so schreiben, dass jede Aktion in `apply` genau eine passende Bedingung mit demselben Zielwert hat und umgekehrt. Der Linter prüft das.
4. Quelle eintragen: Microsoft-Learn-Seitentitel plus Abschnitt, oder eigene Messung mit Datum und Build.
5. `dotnet run --project tools/catalog-lint` muss "Katalog sauber" melden.
6. Integrationstest nach Abschnitt 11.2 laufen lassen (VM; ersatzweise Host nur für HKCU-Tweaks): `tests/vm/Invoke-TweakIntegrationTest.ps1 -TweakIds <ID>`.
7. Bei Erfolg `verifiedOn` ergänzen. Bei Misserfolg Tweak entfernen und Grund in `docs/REJECTED-TWEAKS.md` eintragen.

## Regeln des Linters

- ID-Format `MODUL-NNN`, eindeutig, Kategorie passt zum Modul.
- Textschlüssel heißen `tweak.<ID>.name|desc|side` und existieren in beiden Sprachen.
- Mindestens eine Quelle.
- "Umstritten" ist immer `hiddenByDefault: true`. "Placebo" wird nie ausgeliefert.
- Ab V2 Pflicht: `impact` (`Noticeable`, `Small`, `Unclear`), `impactNote` (`tweak.ID.impact`, eine Zeile Begründung), `measuredOn` (Liste), `plainTitleKey` (`tweak.ID.plain.title`, höchstens 5 Wörter) und `plainDescKey` (`tweak.ID.plain.desc`, höchstens 90 Zeichen), Texte in beiden Sprachen. `Noticeable` nur mit Messbeleg in `measuredOn`; "Umstritten" ist immer `Unclear`, `Unclear` immer `hiddenByDefault: true`. Die Einstufung steht mit Begründung in `docs/IMPACT-AUDIT.md`.
- HKCU-Aktionen nur mit `scope: User` und ohne Admin; HKLM, Dienste, Energiepläne und Befehle nur mit `scope: Machine` und Admin.
- HKCU-Aktionen unter `Software\Policies` (Benutzer-Richtlinien) nur mit `scope: UserPolicy` und Admin; sie laufen im Helfer. Ein Tweak mischt keine Bereiche.
- `ScheduledTaskSetEnabled` nur für Aufgaben unter `\Microsoft\Windows\`, Bereich Machine; Erkennung `ScheduledTaskEnabledEquals`. Fehlt die Aufgabe, ist der Tweak "nicht verfügbar".
- `SystemParametersInfo` nur mit Parametern der Whitelist und im erlaubten Wertebereich, Bereich User; Erkennung `SystemParameterEquals`.
- Dienst-Aktionen sollen `windowsDefault` angeben (Anzeige im Dienste-Manager).
- Teilen sich mehrere Einstellungen eine Zeichenkette (zum Beispiel `DirectXUserGlobalSettings`), nie `RegistrySetValue` verwenden, sondern `RegistryListEntry` mit `format` und `key`; Erkennung `RegistryListEntryEquals`.
- Vor jedem Host-Test prüfen, dass der Tweak "nicht angewendet" ist; das Integrationsskript bricht sonst den Tweak ohne Änderung ab.
- Leistungs-Tweaks mit Geschwindigkeitsversprechen bleiben ohne Messprotokoll "Umstritten".
- Registry-Pfade ohne führenden oder abschließenden Backslash, ohne `..`, ohne `/`.
- Schlüssel anlegen oder löschen nur unterhalb von `RegistryPaths.KeyAllowlist`.
- Befehle nur aus `CommandWhitelist`, Werte nur aus deren erlaubter Liste. Seit V2-4: `nic.eee` (nur Adapter mit *EEE, sonst "nicht verfügbar") und `defender.scanload` (0 oder 5 bis 100; einzige Defender-Berührung, DEFN-001).
- `PowerSettingSet` (V2-4) setzt eine Unter-Einstellung des aktiven Energieplans für Netzbetrieb (`subgroup`, `setting`, `value`); Erkennung `PowerSettingEquals` mit denselben Werten. Die Sicherung merkt sich den Plan, das Zurücksetzen trifft genau ihn. GUIDs vorher in der Microsoft-Dokumentation oder mit `powercfg /aliases` bestätigen.
- `desktopOnly: true` für Energie-Tweaks, die auf Notebooks Akku kosten: auf Geräten mit Akku "nicht verfügbar", in Presets nur in der Stufe "Stark".
- Module seit V2-4: `BROW` (Browser, Kategorie `Performance.*`), `POWR` (Energie-Feintuning, `Performance.Power`), `DEFN` (Defender-Scan-Last, `Performance.*`).

## Test-Checkliste pro Tweak (Abschnitt 11.3)

| Nr. | Prüfung | Ergebnis |
|---|---|---|
| 1 | Existiert der Wert, Dienst, Task oder das Paket auf allen unterstützten Builds? | |
| 2 | Ist der Standardwert korrekt dokumentiert? | |
| 3 | Erkennt `detect` den Zustand richtig? | |
| 4 | Hat der Tweak den beschriebenen Effekt (Messprotokoll oder Dokumentation)? | |
| 5 | Ändert der Dry-Run nichts? | |
| 6 | Verändert Anwenden nur Deklariertes? | |
| 7 | Stellt Revert den Ausgangszustand her? | |
| 8 | Verhalten bei fehlender Berechtigung? | |
| 9 | Ist die Nebenwirkung in der Beschreibung korrekt und vollständig? | |
| 10 | Stimmen Risikostufe und Wirkungsnachweis mit dem Befund überein? | |

## Beispiel

Siehe `tweaks/privacy/PRIV-013.json` (zwei Werte, `AllOf`-Erkennung) und `tweaks/ui/UI-001.json` (Schlüssel anlegen).
