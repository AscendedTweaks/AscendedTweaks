<#
.SYNOPSIS
    Erzeugt THIRD-PARTY-NOTICES.md aus den NuGet-Abhaengigkeiten (Abschnitt 13): Paket, Version, Lizenz aus der .nuspec.
.DESCRIPTION
    Ausgeliefert: App, Helfer, Kommandozeile (inklusive transitiver Pakete). Nur fuer Tests: alle Testprojekte.
    Pakete ohne Lizenzausdruck werden mit ihrer Lizenz-URL oder als "pruefen" markiert, nie geraten.
#>
param([string]$Out)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Out) { $Out = Join-Path $repo 'THIRD-PARTY-NOTICES.md' }
$cache = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }

function Get-Packages([string[]]$Projects) {
    $all = @{}
    foreach ($project in $Projects) {
        $json = & dotnet list (Join-Path $repo $project) package --include-transitive --format json | Out-String | ConvertFrom-Json
        foreach ($framework in $json.projects.frameworks) {
            foreach ($p in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
                if ($p) { $all["$($p.id)|$($p.resolvedVersion)"] = $p }
            }
        }
    }
    return $all.Keys | Sort-Object | ForEach-Object {
        $id, $version = $_.Split('|')
        $nuspec = Get-ChildItem (Join-Path $cache "$($id.ToLowerInvariant())\$version") -Filter '*.nuspec' -ErrorAction SilentlyContinue | Select-Object -First 1
        $license = 'pruefen'
        if ($nuspec) {
            [xml]$x = Get-Content $nuspec.FullName
            $meta = $x.package.metadata
            if ($meta.license -and $meta.license.type -eq 'expression') { $license = $meta.license.'#text' }
            elseif ($meta.license) { $license = "Datei im Paket: $($meta.license.'#text')" }
            elseif ($meta.licenseUrl) { $license = $meta.licenseUrl }
        }
        "| $id | $version | $license |"
    }
}

$shipped = Get-Packages @('src\AscendedTweaks.App\AscendedTweaks.App.csproj', 'src\AscendedTweaks.Helper\AscendedTweaks.Helper.csproj', 'src\AscendedTweaks.Cli\AscendedTweaks.Cli.csproj')
$tests = Get-Packages (Get-ChildItem (Join-Path $repo 'tests') -Recurse -Filter '*.csproj' | ForEach-Object { $_.FullName.Substring($repo.Length + 1) }) |
    Where-Object { $shipped -notcontains $_ }

$lines = @(
    '# Drittanbieter-Lizenzen'
    ''
    "Erzeugt mit ``tools\release\New-ThirdPartyNotices.ps1`` am $(Get-Date -Format 'yyyy-MM-dd') aus ``dotnet list package --include-transitive`` und den Lizenzangaben der Pakete. Keines der ausgelieferten Pakete sendet Telemetrie."
    ''
    'Die .NET-Laufzeit (Microsoft, MIT) ist in der portablen Version und im Installer enthalten (self-contained).'
    ''
    '## Mit der Anwendung ausgeliefert'
    ''
    '| Paket | Version | Lizenz |'
    '|---|---|---|'
) + $shipped + @(
    ''
    '## Nur fuer Tests (nicht ausgeliefert)'
    ''
    '| Paket | Version | Lizenz |'
    '|---|---|---|'
) + $tests + @(
    ''
    'FluentAssertions ist bewusst auf Version 6 festgelegt: Ab Version 8 gilt eine kommerzielle Lizenz.'
)
$lines | Set-Content -Path $Out -Encoding utf8
Write-Host "Geschrieben: $Out ($($shipped.Count) ausgeliefert, $($tests.Count) nur Tests)"
