# Sicherheits-Review (Phase 14)

Stand 2026-10-18. Geprüft wurde jede Schutzmaßnahme aus `docs/THREAT-MODEL.md` gegen den Code und gegen einen automatisierten Test. Wo ein Test fehlte, wurde er ergänzt. Ein Penetrationstest mit einem fremden Prozess gegen den echten Helfer (mit UAC) steht aus, weil er Administratorrechte und eine VM braucht (offener Punkt 9).

## A: Schadprogramm mit Benutzerrechten will den Helfer missbrauchen

| Schutzmaßnahme | Code | Beleg (Test) | Ergebnis |
|---|---|---|---|
| Kein Dauerdienst, Ende nach Leerlauf und mit dem startenden Prozess | `HelperServer` | `Server_ends_after_idle_timeout`, `Server_ends_when_parent_process_is_gone` | erfüllt |
| Zufälliger, an den Benutzer gebundener Pipe-Name | `IpcProtocol` | `Pipe_names_are_bound_to_the_user` | erfüllt |
| Pipe-ACL nur Benutzer und SYSTEM | `HelperServer` | `Pipe_acl_allows_only_user_and_system` | erfüllt |
| Client-Prüfung (Eltern-PID, Installationsordner) | `WindowsClientVerifier` | `Client_from_unexpected_process_is_rejected`, `Client_outside_install_directory_is_rejected`, `Directory_check_is_exact` | erfüllt; die Authenticode-Prüfung des Clients wirkt nur in Release-Builds mit Zertifikat (offener Punkt 4) |
| Server-Prüfung im Client (kein Pipe-Squatting) | `HelperClient` | `Client_refuses_a_server_that_is_not_the_launched_helper` | erfüllt |
| Protokoll-Härtung (1 MB, Zähler, unbekannte Felder) | `PipeFraming`, `IpcProtocol` | `Framing_roundtrip_and_limits`, `Oversized_frame_closes_connection`, `Server_rejects_protocol_violations_and_closes` | erfüllt |
| Befehls-Whitelist, Tweak-IDs gegen Katalog | `EngineHelperHandler` | `Handler_refuses_user_tweaks_and_bad_input` (unbekannter Befehl `RunScript`, unbekannte ID, Pfad in Batch-ID, Steuerzeichen im Namen) | erfüllt |
| Erzeugte IDs (Autostart, Spiele, DNS, Updates) streng geprüft | `IpcProtocol.IsValidTweakId` | `Pipe_names_are_bound_to_the_user` (Abschnitt Tweak-IDs), `Generated_update_ids_are_strictly_checked` (Pause in der Zukunft und über 35 Tage abgelehnt), `Custom_dns_is_a_generated_tweak_with_strict_validation` | erfüllt |
| Nur Maschinen-Tweaks und Benutzer-Richtlinien; HKCU nur `Software\Policies` über `HKU\<SID aus der Client-Prüfung>` | `HelperRegistryAccess` | `Helper_registry_maps_only_user_policies`, `Handler_refuses_mixed_scopes_in_one_request` | erfüllt |
| Bereinigung im Helfer ohne Pfade | `EngineHelperHandler`, `CleanupEngine` | `Helper_cleanup_accepts_only_known_machine_categories_without_paths`, `File_swapped_for_a_link_after_the_scan_is_not_deleted`, `Handle_based_delete_refuses_a_junction` | erfüllt |
| Apps entfernen nur mit gültigen Paketnamen, Systemapps gesperrt | `AppRemover` | `Helper_remove_apps_validates_names_and_requires_the_remover`, `Remover_refuses_locked_foreign_microsoft_and_invalid_names` | erfüllt |
| Wartung nur feste Aktionen und ein Laufwerksbuchstabe; Medientyp prüft der Helfer selbst | `EngineHelperHandler`, `Maintenance` | **neu:** `Helper_maintenance_accepts_only_fixed_actions_and_single_drive_letters` (unbekannte Aktion, Zahl statt Name, Kleinbuchstabe, Ziffer, Zusatzfeld, Defragmentieren auf SSD); `Maintenance_blocks_defrag_on_ssd_and_unknown_media` | erfüllt; der Test fehlte bis Phase 14 |
| DNS-Werte geprüft, netsh mit Argumentliste | `AdapterState`, `CommandExecutor` | `Dns_states_are_validated_strictly` | erfüllt |
| Aufgabenplanung nur unter `\Microsoft\Windows\`, nur ENABLE/DISABLE | `WindowsScheduledTaskAccess` | `Task_paths_are_limited_to_windows_tasks`, `Task_access_uses_schtasks_without_shell_and_reports_errors` | erfüllt |
| Zustandsdateien: Benutzer nur lesen, Prüfsummen vor Wiederherstellung | `StateStore` | `Recovery_refuses_tampered_backup`, `Corrupt_ledger_is_reported_not_reset` | erfüllt; die ACL setzt der Helfer beim Start (`StateDirectorySecurity.Apply`), auf einem echten System mit UAC noch nicht beobachtet (offener Punkt 9) |

## B: Manipulierter Katalog oder Profil-Import

| Schutzmaßnahme | Code | Beleg | Ergebnis |
|---|---|---|---|
| Eingebetteter Katalog mit Schema und Linter | `CatalogLoader`, `CatalogLinter` | `CatalogTests`, Linter im Qualitätstor | erfüllt |
| Allowlists für Schlüssel und Befehle | `RegistryPaths`, `CommandWhitelist` | `CatalogTests`, `Task_access_uses_schtasks_without_shell_and_reports_errors` | erfüllt |
| Profil-Import nicht vertrauenswürdig | `ProfileCodec` | `Import_rejects_untrusted_content` (15 Fälle: unbekannte Felder, Pfade, erzeugte IDs, Doppelte, Version, Steuerzeichen, abgeschnittenes JSON), `Import_rejects_files_over_one_megabyte_and_too_many_entries`, `Import_checks_the_file_and_only_previews` | erfüllt |
| Export-Skripte ohne Ausbruch aus Zeichenketten oder Kommentaren | `ScriptExporter` | `Texts_cannot_break_out_of_strings_or_comments`, `Export_reads_original_values_and_writes_matching_reset_steps` (kein `Invoke-Expression`, `-EncodedCommand`, `RunAs`, Netzwerkzugriff) | erfüllt (neu in Phase 13) |

## C: Manipulierter Update-Download

| Schutzmaßnahme | Code | Beleg | Ergebnis |
|---|---|---|---|
| Kein Auto-Update, keine Anfrage ohne Einrichtung | `UpdateChecker`, `UpdateConfig` | `Without_address_key_or_publisher_nothing_is_requested`, `Update_check_is_honest_when_not_configured` | erfüllt |
| Ed25519-Signatur des Manifests | `UpdateChecker.VerifySignature` | `Tampered_manifest_or_foreign_key_is_rejected` | erfüllt; dabei gefunden und behoben: ein ungültiger öffentlicher Schlüssel warf eine Ausnahme statt "ungültig" |
| Strenges Manifest, nur HTTPS | `UpdateChecker` | `Signed_but_invalid_manifests_are_rejected` (HTTP-Installer, Version ohne Patch, Zusatzfeld, Größe) | erfüllt |
| SHA-256, Größe, Authenticode mit erwartetem Herausgeber; ungültige Datei wird gelöscht | `UpdateChecker.DownloadAsync` | `Download_checks_size_hash_and_publisher_and_deletes_bad_files` | erfüllt gegen Fakes; mit echtem Zertifikat offen (offene Punkte 4 und 7) |
| Start erst nach zweiter Bestätigung | `SettingsMaintenanceViewModel` | `Installer_starts_only_after_verified_download_and_second_confirmation` | erfüllt |

## Netzwerk im Leerlauf

`tests/perf/Measure-AppPerformance.ps1` hat in drei Läufen je 30 Sekunden lang alle TCP-Verbindungen und UDP-Endpunkte der App abgefragt (`Get-NetTCPConnection`, `Get-NetUDPEndpoint`, alle 2 Sekunden): keine. Das ist eine Prozess-Abfrage, kein Paketmitschnitt; ein Mitschnitt mit Wireshark in der VM ergänzt das (offener Punkt 17).

## Befunde dieser Prüfung

1. Fehlender Handler-Test für `RunMaintenance`/`ReadDiskHealth`: ergänzt. Der Code war bereits korrekt.
2. `UpdateChecker.VerifySignature` warf bei einem ungültigen Schlüssel eine Ausnahme: behoben (Phase 13).
3. Die Befehlsliste in `THREAT-MODEL.md` war seit Phase 8 veraltet: aktualisiert.
4. Offen und nicht ohne Nutzer prüfbar: echter Helfer mit UAC gegen einen fremden Client (Penetrationstest), Release-Signaturprüfung, ACL des Zustandsordners auf einem echten System.
