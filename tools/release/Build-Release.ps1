<#
.SYNOPSIS
    Baut den Release (Abschnitt 12.1): App, Helfer und Kommandozeile self-contained in einen Ordner, portable ZIP,
    Pruefsummen, optional Signierung und Installer.
.DESCRIPTION
    - Ohne -CertificateThumbprint und ohne -TrustedSigningMetadata wird nichts signiert (Entwicklungs-Build).
    - Der Installer entsteht nur, wenn Inno Setup 6 (ISCC.exe) installiert ist.
    - Ausgabe: artifacts\publish, artifacts\AscendedTweaks-<Version>-portable-win-x64.zip, artifacts\AscendedTweaks-<Version>-Setup.exe, artifacts\SHA256SUMS.txt
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy RemoteSigned -File tools\release\Build-Release.ps1
#>
param(
    [string]$CertificateThumbprint,
    [string]$TrustedSigningMetadata,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifacts = Join-Path $repo 'artifacts'
$publish = Join-Path $artifacts 'publish'
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $publish | Out-Null

# Alle drei Programme in denselben Ordner: Der Helfer prueft, dass der Client aus seinem Installationsordner kommt.
foreach ($project in 'AscendedTweaks.App', 'AscendedTweaks.Helper', 'AscendedTweaks.Cli') {
    & dotnet publish (Join-Path $repo "src\$project") -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $publish --nologo
    if ($LASTEXITCODE -ne 0) { throw "Veroeffentlichen von $project fehlgeschlagen." }
}

if ($CertificateThumbprint -or $TrustedSigningMetadata) {
    & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -Path $publish -CertificateThumbprint $CertificateThumbprint -TrustedSigningMetadata $TrustedSigningMetadata
} else {
    Write-Warning 'Nicht signiert: Entwicklungs-Build. Fuer einen Release -CertificateThumbprint oder -TrustedSigningMetadata angeben.'
}

$zip = Join-Path $artifacts "AscendedTweaks-$version-portable-win-x64.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip

$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $SkipInstaller) {
    if ($iscc) {
        & $iscc "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$artifacts" (Join-Path $repo 'installer\AscendedTweaks.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer fehlgeschlagen.' }
        $setup = Join-Path $artifacts "AscendedTweaks-$version-Setup.exe"
        if ($CertificateThumbprint -or $TrustedSigningMetadata) {
            & (Join-Path $PSScriptRoot 'Sign-Release.ps1') -Path $setup -CertificateThumbprint $CertificateThumbprint -TrustedSigningMetadata $TrustedSigningMetadata
        }
    } else {
        Write-Warning 'Inno Setup 6 nicht gefunden: kein Installer. Installation: winget install JRSoftware.InnoSetup'
    }
}

Get-ChildItem $artifacts -File | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.Name } | Set-Content (Join-Path $artifacts 'SHA256SUMS.txt') -Encoding ascii
Get-ChildItem $artifacts -File | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
