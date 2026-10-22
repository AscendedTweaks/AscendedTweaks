<#
.SYNOPSIS
    Builds the finished "Ascended Tweaks" folder on the desktop: three programs as single files that need no installed .NET,
    Docs, START HERE.txt and checksums, plus a ZIP file with the same content in artifacts.
.DESCRIPTION
    - Der Zielordner wird vor dem Schreiben nur geleert, wenn er die Markierung ".ascendedtweaks-package" enthaelt.
      Existiert er ohne Markierung, entsteht "Ascended Tweaks (2)" (oder die naechste freie Nummer); fremde Ordner werden nie ueberschrieben.
    - Der SHA-256 des Helfers wird in App und Kommandozeile eingebettet; sie starten den Helfer nur mit genau diesem Inhalt.
    - -PublishDir ueberspringt das Veroeffentlichen (Test-Hook): erwartet app\AscendedTweaks.App.exe, helper\AscendedTweaks.Helper.exe, cli\ascendedtweaks-cli.exe.
.EXAMPLE
    powershell -NoProfile -ExecutionPolicy RemoteSigned -File package.ps1
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Ascended Tweaks'),
    [string]$PublishDir,
    [string]$ZipDirectory
)

$ErrorActionPreference = 'Stop'
$repo = $PSScriptRoot
[xml]$props = Get-Content (Join-Path $repo 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
if (-not $ZipDirectory) { $ZipDirectory = Join-Path $repo 'artifacts' }
$marker = '.ascendedtweaks-package'

function Publish([string]$Project, [string]$Output, [string]$HelperHash) {
    $arguments = @('publish', (Join-Path $repo "src\$Project"), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true', '-p:DebugType=None', '-p:DebugSymbols=false',
        '-o', $Output, '--nologo')
    if ($HelperHash) { $arguments += "-p:HelperSha256=$HelperHash" }
    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "Veroeffentlichen von $Project fehlgeschlagen." }
}

# 1. Veroeffentlichen (oder Test-Eingabe verwenden).
if (-not $PublishDir) {
    $PublishDir = Join-Path $repo 'artifacts\package-publish'
    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }
    Publish 'AscendedTweaks.Helper' (Join-Path $PublishDir 'helper') $null
    $helperHash = (Get-FileHash (Join-Path $PublishDir 'helper\AscendedTweaks.Helper.exe') -Algorithm SHA256).Hash
    Publish 'AscendedTweaks.Cli' (Join-Path $PublishDir 'cli') $helperHash
    Publish 'AscendedTweaks.App' (Join-Path $PublishDir 'app') $helperHash
}

$sources = @{
    'AscendedTweaks.exe'            = Join-Path $PublishDir 'app\AscendedTweaks.App.exe'
    'app\AscendedTweaks.Helper.exe' = Join-Path $PublishDir 'helper\AscendedTweaks.Helper.exe'
    'app\ascendedtweaks-cli.exe'    = Join-Path $PublishDir 'cli\ascendedtweaks-cli.exe'
}
foreach ($source in $sources.Values) {
    if (-not (Test-Path $source)) { throw "Fehlt: $source" }
}

# 2. Zielordner waehlen: nur markierte Ordner werden geleert, sonst die naechste freie Nummer.
$target = $Destination
$number = 2
while ((Test-Path $target) -and -not (Test-Path (Join-Path $target $marker))) {
    $target = "$Destination ($number)"
    $number++
}

if (Test-Path $target) {
    Get-ChildItem -LiteralPath $target -Force | Where-Object { $_.Name -ne $marker } | Remove-Item -Recurse -Force
} else {
    New-Item -ItemType Directory -Path $target | Out-Null
}

# 3. Content: only the three programs, Docs and START HERE.txt; no .pdb, no developer files.
New-Item -ItemType Directory -Path (Join-Path $target 'app'), (Join-Path $target 'Docs') -Force | Out-Null
foreach ($name in $sources.Keys) { Copy-Item -LiteralPath $sources[$name] -Destination (Join-Path $target $name) }
Copy-Item (Join-Path $repo 'docs\PRIVACY.md') (Join-Path $target 'Docs\Privacy.md')
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') (Join-Path $target 'Docs\Licenses.md')
Copy-Item (Join-Path $repo 'docs\CHANGELOG.md') (Join-Path $target 'Docs\Changelog.md')
Copy-Item (Join-Path $repo 'docs\SOURCES.md') (Join-Path $target 'Docs\Sources.md')
Copy-Item (Join-Path $repo 'tools\release\START HERE.txt') (Join-Path $target 'START HERE.txt')

$sums = Get-ChildItem -LiteralPath $target -Recurse -Filter '*.exe' | Sort-Object FullName | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash, $_.FullName.Substring($target.Length + 1)
}
Set-Content -LiteralPath (Join-Path $target 'Docs\Checksums.txt') -Value $sums -Encoding ascii
Set-Content -LiteralPath (Join-Path $target $marker) -Value "Ascended Tweaks $version" -Encoding ascii
(Get-Item -LiteralPath (Join-Path $target $marker) -Force).Attributes = 'Hidden'

# 4. ZIP with the same content for sharing.
New-Item -ItemType Directory -Path $ZipDirectory -Force | Out-Null
$zip = Join-Path $ZipDirectory "AscendedTweaks-$version-win-x64.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $target '*') -DestinationPath $zip

Write-Host "Paket: $target"
Write-Host "ZIP:   $zip"
$sums | ForEach-Object { Write-Host "  $_" }
