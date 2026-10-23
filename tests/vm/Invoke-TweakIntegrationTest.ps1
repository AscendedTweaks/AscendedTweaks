<#
.SYNOPSIS
  Integrationstest nach Abschnitt 11.2 (Schritte 2 bis 7) fuer einzelne Tweaks.

.DESCRIPTION
  Fuer jeden Tweak:
    2. Registry-Snapshot A
    3. Dry-Run, Snapshot B, Bedingung A == B
    4. Anwenden, Snapshot C, Differenz A/C enthaelt nur die im Tweak deklarierten Stellen
    5. Zustandserkennung meldet Applied
    6. Zuruecksetzen, Snapshot D, Bedingung D == A
    7. Zustandserkennung meldet NotApplied
  Schritt 1 (VM-Pruefpunkt "sauber" wiederherstellen) faellt bei einem Lauf auf dem Host weg.
  Der Zustand der App liegt in einem eigenen Testordner (--state-root), nicht in %LOCALAPPDATA%.

  -PrepareDefault setzt einen bereits angewendeten Wert vor dem Test auf den Windows-Standard
  und stellt nach dem Test den urspruenglichen Wert wieder her (Format: Tweak-ID=Pfad|Name|DWORD-Wert).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy RemoteSigned -File tests\vm\Invoke-TweakIntegrationTest.ps1 -TweakIds PRIV-013,UI-001
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string[]]$TweakIds,
    [string]$Configuration = 'Debug',
    [string[]]$PrepareDefault = @(),
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
# Bei Aufruf ueber -File kommt eine kommagetrennte Liste als ein einziger String an.
$TweakIds = @($TweakIds | ForEach-Object { $_.Split(',') } | Where-Object { $_ })
$repo =(Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cli = Join-Path $repo "src\AscendedTweaks.Cli\bin\$Configuration\net8.0-windows10.0.19041.0\ascendedtweaks-cli.exe"
$snap = Join-Path $repo "tools\registry-snapshot\bin\$Configuration\net8.0-windows\registry-snapshot.exe"
$catalog = Join-Path $repo 'src\AscendedTweaks.Catalog\tweaks'
foreach ($exe in $cli, $snap) {
    if (-not (Test-Path -LiteralPath $exe)) { throw "Nicht gebaut: $exe (zuerst 'dotnet build')." }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ("AscendedTweaks.IntegrationTest." + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$state = Join-Path $work 'state'
$build = [Environment]::OSVersion.Version.Build
$edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
$results = New-Object System.Collections.Generic.List[object]

function Invoke-Cli([string[]]$Arguments) {
    # Fehlerausgaben der CLI sind Testergebnisse, keine Skriptfehler.
    $ErrorActionPreference = 'Continue'
    $output = & $cli @Arguments --state-root $state 2>&1 | ForEach-Object { "$_" } | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

function New-Snapshot([string]$Name, [string[]]$Roots) {
    $file = Join-Path $work "$Name.txt"
    & $snap snapshot --out $file @Roots | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Snapshot $Name fehlgeschlagen." }
    return $file
}

function Compare-Snapshot([string]$A, [string]$B) {
    $out = & $snap compare $A $B | Out-String
    return [pscustomobject]@{ Equal = ($LASTEXITCODE -eq 0); Lines = @($out -split "`r?`n" | Where-Object { $_ -match '^[+-] ' }) }
}

# Registry-Abdruck von SystemParametersInfo (gleiche Tabelle wie SystemParameterWhitelist in AscendedTweaks.Core).
$spiFootprint = @{
    'ClientAreaAnimation'       = @{ hive = 'HKCU'; path = 'Control Panel\Desktop'; name = 'UserPreferencesMask' }
    'MinimizeMaximizeAnimation' = @{ hive = 'HKCU'; path = 'Control Panel\Desktop\WindowMetrics'; name = 'MinAnimate' }
    'MenuShowDelay'             = @{ hive = 'HKCU'; path = 'Control Panel\Desktop'; name = 'MenuShowDelay' }
    'MouseAcceleration'         = @{ hive = 'HKCU'; path = 'Control Panel\Mouse'; name = @('MouseSpeed', 'MouseThreshold1', 'MouseThreshold2') }
    'StickyKeysHotkey'          = @{ hive = 'HKCU'; path = 'Control Panel\Accessibility\StickyKeys'; name = 'Flags' }
    'FilterKeysHotkey'          = @{ hive = 'HKCU'; path = 'Control Panel\Accessibility\Keyboard Response'; name = 'Flags' }
    'ToggleKeysHotkey'          = @{ hive = 'HKCU'; path = 'Control Panel\Accessibility\ToggleKeys'; name = 'Flags' }
    'MouseSpeed'                = @{ hive = 'HKCU'; path = 'Control Panel\Mouse'; name = 'MouseSensitivity' }
    'KeyboardDelay'             = @{ hive = 'HKCU'; path = 'Control Panel\Keyboard'; name = 'KeyboardDelay' }
    'KeyboardSpeed'             = @{ hive = 'HKCU'; path = 'Control Panel\Keyboard'; name = 'KeyboardSpeed' }
}

# Registry-Aktionen eines Tweaks; SystemParametersInfo wird als Wert-Aenderung an seinem Abdruck gefuehrt.
function Get-RegistryActions($Tweak) {
    foreach ($a in @($Tweak.apply)) {
        if ($a.type -eq 'SystemParametersInfo') {
            $f = $spiFootprint[$a.parameter]
            foreach ($n in @($f.name)) { [pscustomobject]@{ type = 'RegistrySetValue'; hive = $f.hive; path = $f.path; name = $n } }
        } elseif ($a.hive) {
            $a
        }
    }
}

# Deklarierte Stellen eines Tweaks: Werte exakt, Schluessel inklusive angelegter Vorfahren.
function Get-Declared($Tweak) {
    $keys = New-Object System.Collections.Generic.List[string]
    $values = New-Object System.Collections.Generic.List[string]
    foreach ($a in @(Get-RegistryActions $Tweak)) {
        $full = ("$($a.hive)\$($a.path)").ToUpperInvariant()
        $keys.Add($full)
        if ($a.type -in 'RegistrySetValue', 'RegistryDeleteValue', 'RegistryListEntry') { $values.Add("$full|$($a.name.ToUpperInvariant())") }
        if ($a.type -eq 'RegistryCreateKey' -and $null -ne $a.defaultValue) { $values.Add("$full|") }
    }
    return [pscustomobject]@{ Keys = $keys; Values = $values }
}

function Test-DiffDeclared($Lines, $Declared) {
    $bad = @()
    foreach ($line in $Lines) {
        $body = $line.Substring(2)
        $parts = $body.Split('|')
        $ok = $false
        if ($parts[0] -in 'K', 'M') {
            foreach ($k in $Declared.Keys) { if ($k -eq $parts[1] -or $k.StartsWith($parts[1] + '\')) { $ok = $true } }
        } elseif ($parts[0] -eq 'V') {
            $ok = $Declared.Values -contains ($parts[1] + '|' + $parts[2])
        }
        if (-not $ok) { $bad += $line }
    }
    return $bad
}

function Get-Roots($Tweak) {
    # Wurzel ist der Schluessel der Aktion selbst (auf einem genutzten PC aendert Windows uebergeordnete Zweige laufend).
    # Bei CLSID-Schluesseln wird eine Ebene hoeher angesetzt, damit angelegte Elternschluessel sichtbar sind.
    $roots = @()
    foreach ($a in @(Get-RegistryActions $Tweak)) {
        $segments = $a.path.Split('\')
        $depth = if ($a.path -like 'Software\Classes\CLSID\*') { 4 } else { $segments.Length }
        $roots += "$($a.hive)\" + (($segments | Select-Object -First ([Math]::Min($depth, $segments.Length))) -join '\')
    }
    return $roots | Select-Object -Unique
}

$prepared = @{}
try {
    foreach ($spec in $PrepareDefault) {
        $id, $rest = $spec.Split('=', 2)
        $path, $name, $value = $rest.Split('|')
        $regPath = "Registry::HKEY_CURRENT_USER\$path"
        $old = (Get-ItemProperty -LiteralPath $regPath -Name $name -ErrorAction SilentlyContinue).$name
        $prepared[$id] = [pscustomobject]@{ Path = $regPath; Name = $name; Old = $old }
        Write-Host "[VORBEREITUNG] $id : $path\$name von '$old' auf Windows-Standard '$value' (wird danach zurueckgesetzt)"
        New-ItemProperty -LiteralPath $regPath -Name $name -Value ([int]$value) -PropertyType DWord -Force | Out-Null
    }

    foreach ($id in $TweakIds) {
        $file = Get-ChildItem -LiteralPath $catalog -Recurse -Filter "$id.json" | Select-Object -First 1
        $tweak = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        $roots = Get-Roots $tweak
        $declared = Get-Declared $tweak
        $r = [ordered]@{ Tweak = $id; Build = $build; Edition = $edition; Roots = ($roots -join ', ') }
        Write-Host ""
        Write-Host "=== $id (Wurzeln: $($r.Roots)) ==="

        # Vorbedingung: Der Tweak darf vorher nicht angewendet sein. Sonst aendert der Test nichts, und eine
        # vermeintliche Ruecksetzung koennte einen echten Zustand des Nutzers ueberschreiben. "Mixed" (ein Teil schon
        # gesetzt) ist erlaubt; nach dem Zuruecksetzen muss dann wieder genau "Mixed" gemeldet werden.
        $status0 = Invoke-Cli @('--status', $id)
        $startState = if ($status0.Output -match "${id}: (NotApplied|Mixed)") { $Matches[1] } else { $null }
        $r.Precondition = $null -ne $startState
        if (-not $r.Precondition) {
            Write-Host "Vorbedingung nicht erfuellt (Zustand vorher: $($status0.Output.Trim())). Uebersprungen, nichts geaendert." -ForegroundColor Yellow
            $r.Passed = $false
            $r.Date = (Get-Date -Format 'yyyy-MM-dd')
            $results.Add([pscustomobject]$r)
            continue
        }

        $a = New-Snapshot "$id-A" $roots

        $dry = Invoke-Cli @('--apply', $id, '--dry-run', '--silent', '--allow-expert')
        $b = New-Snapshot "$id-B" $roots
        $cmpAB = Compare-Snapshot $a $b
        $r.DryRunUnchanged = ($dry.ExitCode -eq 0) -and $cmpAB.Equal
        Write-Host "Schritt 3 Dry-Run unveraendert: $($r.DryRunUnchanged)"

        $apply = Invoke-Cli @('--apply', $id, '--silent', '--allow-expert', '--no-restore-point')
        $c = New-Snapshot "$id-C" $roots
        $cmpAC = Compare-Snapshot $a $c
        $undeclared = @(Test-DiffDeclared $cmpAC.Lines $declared)
        $r.ApplyOnlyDeclared = ($apply.ExitCode -eq 0) -and (-not $cmpAC.Equal) -and ($undeclared.Count -eq 0)
        Write-Host "Schritt 4 Anwenden aendert nur Deklariertes: $($r.ApplyOnlyDeclared)"
        $cmpAC.Lines | ForEach-Object { Write-Host "    $_" }
        if ($apply.ExitCode -ne 0) { Write-Host $apply.Output }
        $undeclared | ForEach-Object { Write-Host "    NICHT DEKLARIERT: $_" -ForegroundColor Red }

        $status1 = Invoke-Cli @('--status', $id)
        $r.DetectApplied = $status1.Output -match "${id}: Applied"
        Write-Host "Schritt 5 Erkennung Applied: $($r.DetectApplied)"

        $revert = Invoke-Cli @('--revert', $id, '--silent')
        $d = New-Snapshot "$id-D" $roots
        $cmpAD = Compare-Snapshot $a $d
        $r.RevertEqualsBaseline = ($revert.ExitCode -eq 0) -and $cmpAD.Equal
        Write-Host "Schritt 6 Revert gleich Ausgangszustand: $($r.RevertEqualsBaseline)"
        $cmpAD.Lines | ForEach-Object { Write-Host "    $_" }

        $status2 = Invoke-Cli @('--status', $id)
        $r.DetectNotApplied = $status2.Output -match "${id}: $startState"
        Write-Host "Schritt 7 Erkennung ${startState}: $($r.DetectNotApplied)"

        $r.Passed = $r.Precondition -and $r.DryRunUnchanged -and $r.ApplyOnlyDeclared -and $r.DetectApplied -and $r.RevertEqualsBaseline -and $r.DetectNotApplied
        $r.Date = (Get-Date -Format 'yyyy-MM-dd')
        $results.Add([pscustomobject]$r)
    }
}
finally {
    foreach ($p in $prepared.Values) {
        if ($null -eq $p.Old) {
            Remove-ItemProperty -LiteralPath $p.Path -Name $p.Name -ErrorAction SilentlyContinue
        } else {
            New-ItemProperty -LiteralPath $p.Path -Name $p.Name -Value $p.Old -PropertyType DWord -Force | Out-Null
        }
        $now = (Get-ItemProperty -LiteralPath $p.Path -Name $p.Name -ErrorAction SilentlyContinue).($p.Name)
        Write-Host "[NACHBEREITUNG] $($p.Path)\$($p.Name) wieder auf '$now' (vorher '$($p.Old)')"
    }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
$results | Format-Table Tweak, Build, Edition, Precondition, DryRunUnchanged, ApplyOnlyDeclared, DetectApplied, RevertEqualsBaseline, DetectNotApplied, Passed -AutoSize | Out-String | Write-Host
if ($ReportPath) {
    $results | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    Write-Host "Bericht: $ReportPath"
}
if ($results | Where-Object { -not $_.Passed }) { exit 1 }
exit 0
