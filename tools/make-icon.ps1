# Renders assets/icon/*.svg with Microsoft Edge (headless) and packs a multi-size tweakpilot.ico.
# Sizes 16 to 48 use the simplified SVG, 64 to 256 the detailed one. All ICO entries are PNG-compressed
# (allowed since Windows Vista). Writes only to assets/icon/. Usage: powershell -File tools\make-icon.ps1
[CmdletBinding()]
param(
    [string]$Edge = "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe"
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$iconDir = Join-Path $root 'assets\icon'
if (-not (Test-Path $Edge)) { $Edge = "$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe" }
if (-not (Test-Path $Edge)) { throw "Microsoft Edge not found. Pass -Edge <path to msedge.exe>." }

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$temp = Join-Path ([IO.Path]::GetTempPath()) ("tweakpilot-icon-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $pngs = @{}
    foreach ($size in $sizes) {
        $source = if ($size -le 48) { 'tweakpilot-small.svg' } else { 'tweakpilot.svg' }
        $svg = Get-Content -Raw (Join-Path $iconDir $source)
        $svg = $svg -replace 'width="\d+" height="\d+"', "width=`"$size`" height=`"$size`""
        $html = Join-Path $temp "icon-$size.html"
        Set-Content -Path $html -Value "<html><body style=`"margin:0;background:transparent`">$svg</body></html>" -Encoding UTF8
        $png = Join-Path $temp "icon-$size.png"
        & $Edge --headless --disable-gpu --hide-scrollbars --default-background-color=00000000 `
            "--window-size=$size,$size" "--screenshot=$png" "file:///$($html -replace '\\', '/')" 2>$null | Out-Null
        if (-not (Test-Path $png)) { throw "Edge did not render $size px." }
        $pngs[$size] = [IO.File]::ReadAllBytes($png)
    }

    # ICO: 6-byte header, 16 bytes per entry, then the PNG data.
    $out = Join-Path $iconDir 'tweakpilot.ico'
    $stream = [IO.File]::Create($out)
    $writer = New-Object IO.BinaryWriter($stream)
    try {
        $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        foreach ($size in $sizes) {
            $dim = if ($size -ge 256) { 0 } else { $size }
            $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([uint16]1); $writer.Write([uint16]32)
            $writer.Write([uint32]$pngs[$size].Length); $writer.Write([uint32]$offset)
            $offset += $pngs[$size].Length
        }
        foreach ($size in $sizes) { $writer.Write($pngs[$size]) }
    }
    finally {
        $writer.Dispose()
    }

    Copy-Item (Join-Path $temp 'icon-32.png') (Join-Path $iconDir 'tweakpilot-32.png') -Force

    # Check: list the sizes stored in the ICO.
    $bytes = [IO.File]::ReadAllBytes($out)
    $count = [BitConverter]::ToUInt16($bytes, 4)
    $found = for ($i = 0; $i -lt $count; $i++) { $w = $bytes[6 + 16 * $i]; if ($w -eq 0) { 256 } else { $w } }
    Write-Host "Wrote $out with sizes: $($found -join ', ')"
}
finally {
    Remove-Item -Recurse -Force $temp
}
