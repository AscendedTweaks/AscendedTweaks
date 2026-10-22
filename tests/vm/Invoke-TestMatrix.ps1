<#
.SYNOPSIS
  Testmatrix nach Abschnitt 11.2 fuer eine VM: alle Tweaks einzeln (Schritte 2 bis 7), danach pro Modul
  "alles anwenden, dann alles zuruecksetzen" (Schritt 8) und der Neustart-Test (Schritt 9) in zwei Laeufen.

.DESCRIPTION
  In einer VM mit Pruefpunkt "sauber" als Administrator ausfuehren (Maschinen-Tweaks brauchen Administratorrechte).
  Vor jedem Lauf den Pruefpunkt wiederherstellen (Schritt 1, Hyper-V: Restore-VMSnapshot).

  Ablauf je VM (Windows 10 22H2 19045, Windows 11 23H2 22631, Windows 11 24H2 26100, je Home und Pro):
    1. .\Invoke-TestMatrix.ps1 -Phase Single         Schritte 2 bis 7 fuer jeden verfuegbaren Tweak
    2. .\Invoke-TestMatrix.ps1 -Phase Module         Schritt 8 pro Modul
    3. .\Invoke-TestMatrix.ps1 -Phase RebootApply    Tweaks mit "Neustart noetig" anwenden, dann von Hand neu starten
    4. .\Invoke-TestMatrix.ps1 -Phase RebootVerify   nach dem Neustart: Zustand pruefen und zuruecksetzen
  Ergebnis: report-<Build>-<Edition>-<Phase>.json. Bestandene Eintraege gehoeren in "verifiedOn" des Tweaks.

  Das Skript legt keine geplanten Aufgaben an und startet den Rechner nicht selbst neu.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Single', 'Module', 'RebootApply', 'RebootVerify')][string]$Phase,
    [string[]]$Modules = @('PRIV', 'PERF', 'GAME', 'UI', 'DEB', 'NET', 'DISK', 'UPD'),
    [string]$Configuration = 'Debug',
    [string]$OutDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Die Testmatrix braucht Administratorrechte (Maschinen-Tweaks). PowerShell als Administrator starten.'
    exit 2
}

$Modules = @($Modules | ForEach-Object { $_.Split(',') } | Where-Object { $_ })
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cli = Join-Path $repo "src\AscendedTweaks.Cli\bin\$Configuration\net8.0-windows10.0.19041.0\ascendedtweaks-cli.exe"
$snap = Join-Path $repo "tools\registry-snapshot\bin\$Configuration\net8.0-windows\registry-snapshot.exe"
$single = Join-Path $PSScriptRoot 'Invoke-TweakIntegrationTest.ps1'
$build = [Environment]::OSVersion.Version.Build
$edition = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').EditionID
$state = Join-Path $env:ProgramData 'AscendedTweaks.TestMatrix\state'
$report = Join-Path $OutDir "report-$build-$edition-$Phase.json"
$roots = @('HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion', 'HKLM\SOFTWARE\Policies', 'HKLM\SYSTEM\CurrentControlSet\Services', 'HKLM\SYSTEM\CurrentControlSet\Control', 'HKCU')

function Invoke-Cli([string[]]$Arguments) {
    $ErrorActionPreference = 'Continue'
    $output = & $cli @Arguments --state-root $state 2>&1 | ForEach-Object { "$_" } | Out-String
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Output = $output }
}

# Verfuegbare Tweaks laut CLI (Build und Edition prueft die Engine; "Unavailable" wird nicht getestet, aber berichtet).
function Get-Tweaks([string]$Module) {
    $list = Invoke-Cli @('--list', '--module', $Module, '--json')
    return @($list.Output | ConvertFrom-Json)
}

$results = New-Object System.Collections.Generic.List[object]
switch ($Phase) {
    'Single' {
        foreach ($module in $Modules) {
            foreach ($tweak in Get-Tweaks $module) {
                if ($tweak.State -eq 'Unavailable') {
                    $results.Add([pscustomobject]@{ Tweak = $tweak.Id; Build = $build; Edition = $edition; Passed = $null; Note = "nicht verfuegbar: $($tweak.Reason)" })
                    continue
                }

                $file = Join-Path ([IO.Path]::GetTempPath()) "matrix-$($tweak.Id).json"
                & powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File $single -TweakIds $tweak.Id -Configuration $Configuration -ReportPath $file | Out-Host
                $results.AddRange([object[]]@(Get-Content -LiteralPath $file -Raw | ConvertFrom-Json))
            }
        }
    }
    'Module' {
        foreach ($module in $Modules) {
            $ids = @(Get-Tweaks $module | Where-Object { $_.State -eq 'NotApplied' } | ForEach-Object { $_.Id })
            if ($ids.Count -eq 0) { continue }
            $a = Join-Path $OutDir "matrix-$module-A.txt"
            $d = Join-Path $OutDir "matrix-$module-D.txt"
            & $snap snapshot --out $a @roots | Out-Null
            $apply = Invoke-Cli @('--apply', ($ids -join ','), '--silent', '--allow-expert', '--no-restore-point')
            $revert = Invoke-Cli @('--revert-all')
            & $snap snapshot --out $d @roots | Out-Null
            $diff = & $snap compare $a $d
            $results.Add([pscustomobject]@{
                Module = $module; Build = $build; Edition = $edition; Tweaks = $ids
                ApplyExit = $apply.ExitCode; RevertExit = $revert.ExitCode
                Differences = @($diff)
                Passed = ($apply.ExitCode -eq 0 -and $revert.ExitCode -eq 0 -and $LASTEXITCODE -eq 0)
                Date = (Get-Date -Format 'yyyy-MM-dd')
            })
        }
    }
    'RebootApply' {
        $catalog = Join-Path $repo 'src\AscendedTweaks.Catalog\tweaks'
        $reboot = @(Get-ChildItem $catalog -Recurse -Filter '*.json' | Where-Object { $_.DirectoryName -notlike '*i18n*' } |
            ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } |
            Where-Object { $_.requiresReboot -and ($Modules -contains $_.id.Split('-')[0]) } | ForEach-Object { $_.id })
        $available = @($reboot | Where-Object { (Invoke-Cli @('--status', $_)).Output -match "${_}: NotApplied" })
        $apply = Invoke-Cli @('--apply', ($available -join ','), '--silent', '--allow-expert', '--no-restore-point')
        $results.Add([pscustomobject]@{ Phase = 'RebootApply'; Build = $build; Edition = $edition; Tweaks = $available; ApplyExit = $apply.ExitCode })
        Write-Host 'Jetzt von Hand neu starten und danach -Phase RebootVerify ausfuehren.'
    }
    'RebootVerify' {
        $applied = (Get-Content -LiteralPath (Join-Path $OutDir "report-$build-$edition-RebootApply.json") -Raw | ConvertFrom-Json).Tweaks
        foreach ($id in $applied) {
            $after = (Invoke-Cli @('--status', $id)).Output -match "${id}: Applied"
            $revert = Invoke-Cli @('--revert', $id)
            $back = (Invoke-Cli @('--status', $id)).Output -match "${id}: NotApplied"
            $results.Add([pscustomobject]@{ Tweak = $id; Build = $build; Edition = $edition; AppliedAfterReboot = $after; RevertExit = $revert.ExitCode; NotAppliedAfterRevert = $back; Passed = ($after -and $back) })
        }
    }
}

$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $report -Encoding UTF8
Write-Host "Bericht: $report"
if ($results | Where-Object { $_.PSObject.Properties['Passed'] -and $_.Passed -eq $false }) { exit 1 }
exit 0
