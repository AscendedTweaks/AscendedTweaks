<#
.SYNOPSIS
  Qualitaetstor (Abschnitt 11.4): Build ohne Warnungen, alle Tests, Katalog-Linter, Kontrastpruefung, Formatpruefung,
  Pakettest in einem Temp-Ordner. Mit -Package danach der fertige Ordner auf dem Desktop (package.ps1, V2 Abschnitt 7).
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy RemoteSigned -File build.ps1 -Package
#>
[CmdletBinding()]
param([string]$Configuration = 'Debug', [switch]$Coverage, [switch]$Package)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Step([string]$Name, [scriptblock]$Action) {
    Write-Host "== $Name" -ForegroundColor Cyan
    & $Action
    if ($LASTEXITCODE -ne 0) { Write-Host "FEHLGESCHLAGEN: $Name" -ForegroundColor Red; exit $LASTEXITCODE }
}

Step 'Build (Warnungen sind Fehler)' { dotnet build -c $Configuration --nologo }
if ($Coverage) {
    Step 'Tests mit Abdeckung' { dotnet test -c $Configuration --no-build --collect:'XPlat Code Coverage' --results-directory TestResults }
} else {
    Step 'Tests' { dotnet test -c $Configuration --no-build }
}
Step 'Katalog-Linter' { dotnet run --project tools/catalog-lint -c $Configuration --no-build }
Step 'Kontrastpruefung' { dotnet run --project tools/contrast-check -c $Configuration --no-build | Select-String -Pattern 'FAIL|Kontrastpruefung' }
Step 'Formatpruefung' { dotnet format --verify-no-changes }
Step 'Pakettest (Temp-Ordner)' { & (Join-Path $PSScriptRoot 'tests\package\Test-Package.ps1') }
Write-Host 'Qualitaetstor gruen.' -ForegroundColor Green
if ($Package) {
    Step 'Paket' { & (Join-Path $PSScriptRoot 'package.ps1'); $global:LASTEXITCODE = 0 }
}
