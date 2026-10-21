<#
.SYNOPSIS
    Skriptgesteuerter Pakettest in einem Temp-Ordner (V2 Abschnitt 7.4): Markierung, Ueberschreiben nur markierter Ordner,
    "(2)" bei fremden Ordnern, Inhalt und Pruefsummen. Nutzt Platzhalter-Programme statt eines echten Veroeffentlichens.
#>
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$root = Join-Path ([IO.Path]::GetTempPath()) ("TweakPilot.PackageTest." + [guid]::NewGuid().ToString('N'))
$failures = @()
function Check([bool]$Condition, [string]$Text) { if (-not $Condition) { $script:failures += $Text; Write-Host "FEHLER: $Text" -ForegroundColor Red } }

try {
    $publish = Join-Path $root 'publish'
    foreach ($part in 'app\TweakPilot.App.exe', 'helper\TweakPilot.Helper.exe', 'cli\tweakpilot-cli.exe') {
        $file = Join-Path $publish $part
        New-Item -ItemType Directory -Path (Split-Path $file) -Force | Out-Null
        Set-Content -LiteralPath $file -Value "Platzhalter $part" -Encoding ascii
    }

    # Zwischendateien, die nie ins Paket duerfen.
    Set-Content -LiteralPath (Join-Path $publish 'app\TweakPilot.App.pdb') -Value 'pdb'
    $target = Join-Path $root 'Desktop\TweakPilot'
    $zips = Join-Path $root 'zip'
    & (Join-Path $repo 'package.ps1') -Destination $target -PublishDir $publish -ZipDirectory $zips | Out-Null

    $expected = @('.tweakpilot-package', 'START HERE.txt', 'TweakPilot.exe', 'app\TweakPilot.Helper.exe', 'app\tweakpilot-cli.exe',
        'Docs\Privacy.md', 'Docs\Licenses.md', 'Docs\Changelog.md', 'Docs\Sources.md', 'Docs\Checksums.txt')
    $actual = Get-ChildItem -LiteralPath $target -Recurse -File -Force | ForEach-Object { $_.FullName.Substring($target.Length + 1) } | Sort-Object
    Check ((Compare-Object ($expected | Sort-Object) $actual | Measure-Object).Count -eq 0) "Paketinhalt weicht ab: $($actual -join ', ')"
    Check (-not (Get-ChildItem -LiteralPath $target -Recurse -Filter '*.pdb')) 'Keine .pdb im Paket'
    $sums = Get-Content (Join-Path $target 'Docs\Checksums.txt')
    Check ($sums.Count -eq 3) 'Checksums for three programs'
    $exeHash = (Get-FileHash (Join-Path $target 'TweakPilot.exe') -Algorithm SHA256).Hash
    Check (($sums | Where-Object { $_ -like "$exeHash  TweakPilot.exe" }).Count -eq 1) 'Pruefsumme von TweakPilot.exe stimmt'
    Check ((Get-ChildItem $zips -Filter 'TweakPilot-*-win-x64.zip').Count -eq 1) 'ZIP vorhanden'

    # Zweiter Lauf: markierter Ordner wird geleert und neu befuellt.
    Set-Content -LiteralPath (Join-Path $target 'alt.txt') -Value 'alt'
    & (Join-Path $repo 'package.ps1') -Destination $target -PublishDir $publish -ZipDirectory $zips | Out-Null
    Check (-not (Test-Path (Join-Path $target 'alt.txt'))) 'Markierter Ordner wird beim zweiten Lauf geleert'
    Check (Test-Path (Join-Path $target 'TweakPilot.exe')) 'Zweiter Lauf schreibt das Paket neu'

    # Fremder Ordner gleichen Namens: unberuehrt, Paket landet in "(2)".
    $foreign = Join-Path $root 'Desktop\Fremd'
    New-Item -ItemType Directory -Path $foreign | Out-Null
    Set-Content -LiteralPath (Join-Path $foreign 'wichtig.txt') -Value 'nicht anfassen'
    & (Join-Path $repo 'package.ps1') -Destination $foreign -PublishDir $publish -ZipDirectory $zips | Out-Null
    Check ((Get-ChildItem -LiteralPath $foreign -Force | Measure-Object).Count -eq 1) 'Fremder Ordner bleibt unveraendert'
    Check (Test-Path (Join-Path "$foreign (2)" 'TweakPilot.exe')) 'Paket liegt in "Fremd (2)"'
}
finally {
    if (Test-Path $root) { Remove-Item $root -Recurse -Force }
}

if ($failures.Count -gt 0) { Write-Host "Pakettest: $($failures.Count) Fehler" -ForegroundColor Red; exit 1 }
Write-Host 'Pakettest bestanden.' -ForegroundColor Green
exit 0
