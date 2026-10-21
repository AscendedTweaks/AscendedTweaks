# Drittanbieter-Lizenzen

Erzeugt mit `tools\release\New-ThirdPartyNotices.ps1` am 2026-10-18 aus `dotnet list package --include-transitive` und den Lizenzangaben der Pakete. Keines der ausgelieferten Pakete sendet Telemetrie.

Die .NET-Laufzeit (Microsoft, MIT) ist in der portablen Version und im Installer enthalten (self-contained).

## Mit der Anwendung ausgeliefert

| Paket | Version | Lizenz |
|---|---|---|
| BouncyCastle.Cryptography | 2.4.0 | MIT |
| CommunityToolkit.Mvvm | 8.2.2 | MIT |
| Humanizer.Core | 2.14.1 | MIT |
| Json.More.Net | 2.1.1 | MIT |
| JsonPointer.Net | 5.3.1 | MIT |
| JsonSchema.Net | 7.3.4 | MIT |
| System.CodeDom | 8.0.0 | MIT |
| System.Diagnostics.EventLog | 8.0.1 | MIT |
| System.Management | 8.0.0 | MIT |
| System.ServiceProcess.ServiceController | 8.0.1 | MIT |
| WPF-UI | 3.0.5 | MIT |

## Nur fuer Tests (nicht ausgeliefert)

| Paket | Version | Lizenz |
|---|---|---|
| coverlet.collector | 6.0.2 | MIT |
| FluentAssertions | 6.12.2 | Apache-2.0 |
| Microsoft.CodeCoverage | 17.11.1 | MIT |
| Microsoft.NET.Test.Sdk | 17.11.1 | MIT |
| Microsoft.TestPlatform.ObjectModel | 17.11.1 | MIT |
| Microsoft.TestPlatform.TestHost | 17.11.1 | MIT |
| Newtonsoft.Json | 13.0.1 | MIT |
| System.Configuration.ConfigurationManager | 4.4.0 | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT |
| System.Reflection.Metadata | 1.6.0 | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT |
| System.Security.Cryptography.ProtectedData | 4.4.0 | https://github.com/dotnet/corefx/blob/master/LICENSE.TXT |
| xunit.abstractions | 2.0.3 | https://raw.githubusercontent.com/xunit/xunit/master/license.txt |
| xunit.analyzers | 1.16.0 | Apache-2.0 |
| xunit.assert | 2.9.2 | Apache-2.0 |
| xunit.core | 2.9.2 | Apache-2.0 |
| xunit.extensibility.core | 2.9.2 | Apache-2.0 |
| xunit.extensibility.execution | 2.9.2 | Apache-2.0 |
| xunit.runner.visualstudio | 2.8.2 | Apache-2.0 |
| xunit | 2.9.2 | Apache-2.0 |

FluentAssertions ist bewusst auf Version 6 festgelegt: Ab Version 8 gilt eine kommerzielle Lizenz.
