<#
.SYNOPSIS
    Signiert EXE und DLL mit Authenticode (SHA-256, Zeitstempel), Abschnitt 12.2.
.DESCRIPTION
    Zwei Wege, je nach Entscheidung (offener Punkt 4):
      -CertificateThumbprint   Zertifikat im Zertifikatspeicher (Standard-Codesignierzertifikat oder Testzertifikat in der VM)
      -TrustedSigningMetadata  JSON-Datei fuer Azure Trusted Signing (signtool /dlib); der Pfad zur DLib steht in $env:TRUSTED_SIGNING_DLIB
    Signiert nur eigene Dateien (AscendedTweaks.*, ascendedtweaks-cli, Setup); Dateien von Microsoft und Dritten bleiben unveraendert.
    Das Skript legt keine Zertifikate an und vertraut keinem Zertifikat.
#>
param(
    [Parameter(Mandatory)][string]$Path,
    [string]$CertificateThumbprint,
    [string]$TrustedSigningMetadata,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -like '*x64*' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $signtool) { throw 'signtool.exe nicht gefunden (Windows SDK installieren).' }

$files = if (Test-Path $Path -PathType Leaf) { @(Get-Item $Path) } else {
    Get-ChildItem $Path -Recurse -Include 'Ascended Tweaks*.exe', 'Ascended Tweaks*.dll', 'ascendedtweaks-cli.exe', 'catalog-lint.exe'
}

foreach ($file in $files) {
    $args = @('sign', '/fd', 'SHA256', '/tr', $TimestampUrl, '/td', 'SHA256')
    if ($CertificateThumbprint) {
        $args += @('/sha1', $CertificateThumbprint)
    } elseif ($TrustedSigningMetadata) {
        $args += @('/dlib', $env:TRUSTED_SIGNING_DLIB, '/dmdf', $TrustedSigningMetadata)
    } else {
        throw 'Kein Zertifikat angegeben.'
    }

    & $signtool.FullName @args $file.FullName
    if ($LASTEXITCODE -ne 0) { throw "Signieren fehlgeschlagen: $($file.Name)" }
    & $signtool.FullName verify /pa $file.FullName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Signatur nicht gueltig: $($file.Name)" }
}

Write-Host "$($files.Count) Dateien signiert und geprueft."
