<#
.SYNOPSIS
    Misst die Leistung der App gegen die Grenzen aus Abschnitt 11.4: Start bis erstes Fenster unter 2 Sekunden,
    Arbeitsspeicher nach 60 Sekunden Leerlauf unter 150 MB, 0 Prozent CPU im Leerlauf, keine Netzwerkverbindungen
    im Leerlauf, keine Prozesse nach dem Schliessen.
.DESCRIPTION
    Startet die App, bedient nichts und schliesst sie ueber das Fenster (wie der Nutzer). Aendert keine Einstellungen.
    Ergebnis als JSON. Auf der Referenz-VM (4 vCPU, 8 GB) ausfuehren; Werte vom Entwicklungsrechner sind nur ein Hinweis.
#>
param(
    [Parameter(Mandatory)] [string] $AppPath,
    [int] $Runs = 3,
    [int] $IdleSeconds = 60,
    [string] $Out
)

$ErrorActionPreference = 'Stop'
$results = @()
for ($run = 1; $run -le $Runs; $run++) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $AppPath -PassThru
    while ($process.MainWindowHandle -eq 0 -and $watch.Elapsed.TotalSeconds -lt 30) {
        Start-Sleep -Milliseconds 10
        $process.Refresh()
    }
    $startMs = [math]::Round($watch.Elapsed.TotalMilliseconds)

    Start-Sleep -Seconds $IdleSeconds
    $process.Refresh()
    $cpuBefore = $process.TotalProcessorTime
    $connections = @()
    $sampleEnd = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $sampleEnd) {
        $connections += @(Get-NetTCPConnection -OwningProcess $process.Id -ErrorAction SilentlyContinue | ForEach-Object { "TCP $($_.RemoteAddress):$($_.RemotePort) $($_.State)" })
        $connections += @(Get-NetUDPEndpoint -OwningProcess $process.Id -ErrorAction SilentlyContinue | ForEach-Object { "UDP $($_.LocalAddress):$($_.LocalPort)" })
        Start-Sleep -Seconds 2
    }
    $process.Refresh()
    $cpuMs = ($process.TotalProcessorTime - $cpuBefore).TotalMilliseconds
    $privateMb = [math]::Round($process.PrivateMemorySize64 / 1MB, 1)
    $workingMb = [math]::Round($process.WorkingSet64 / 1MB, 1)

    [void]$process.CloseMainWindow()
    if (-not $process.WaitForExit(15000)) { $process.Kill(); $closed = $false } else { $closed = $true }
    Start-Sleep -Seconds 2
    $left = @(Get-Process -Name 'TweakPilot.App', 'TweakPilot.Helper' -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID } | ForEach-Object { "$($_.Name) ($($_.Id))" })

    $results += [pscustomobject]@{
        Run = $run
        StartToWindowMs = $startMs
        WorkingSetMb = $workingMb
        PrivateMb = $privateMb
        IdleCpuMsIn30s = [math]::Round($cpuMs)
        IdleCpuPercent = [math]::Round($cpuMs / 30000 / [Environment]::ProcessorCount * 100, 3)
        Connections = @($connections | Sort-Object -Unique)
        ClosedByWindow = $closed
        ProcessesAfterClose = $left
    }
}

$report = [pscustomobject]@{
    Machine = "$env:PROCESSOR_IDENTIFIER, $([Environment]::ProcessorCount) logische Prozessoren"
    Windows = (Get-CimInstance Win32_OperatingSystem).Caption
    Limits = 'Start < 2000 ms, Arbeitsspeicher < 150 MB, CPU im Leerlauf 0 %, keine Verbindungen, keine Prozesse nach dem Schliessen'
    Runs = $results
}
$json = $report | ConvertTo-Json -Depth 4
if ($Out) { $json | Set-Content -Path $Out -Encoding utf8 }
$json
