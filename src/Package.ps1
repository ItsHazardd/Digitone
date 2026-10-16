# Builds the public release package: Digitone-<version>-Windows-x64.zip plus its SHA-256 file.
# Layout inside the ZIP:  Digitone-<version>\ Digitone.exe, Repair Digitone.cmd, Repair-Digitone.ps1, README.txt, LICENSE.txt, SHA256SUMS.txt, app\...
param(
    [string]$OutputRoot = (Join-Path (Split-Path $PSScriptRoot) 'Releases'),
    [string]$ToolsRoot = (Join-Path (Split-Path $PSScriptRoot) 'Tools')
)
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
$name = "Digitone-$version"
& (Join-Path $PSScriptRoot 'Build.ps1') -OutputName 'DigitoneApp.exe' -Release
& (Join-Path $PSScriptRoot 'BuildLauncher.ps1') -OutputName 'Digitone.exe'

New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null
$work = Join-Path $OutputRoot "$name-work"
if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
$stage = Join-Path $work $name
$app = Join-Path $stage 'app'
New-Item -ItemType Directory -Force -Path (Join-Path $app 'Tools\ffmpeg\bin') | Out-Null

Move-Item -LiteralPath (Join-Path $PSScriptRoot 'Digitone.exe') -Destination (Join-Path $stage 'Digitone.exe')
Move-Item -LiteralPath (Join-Path $PSScriptRoot 'DigitoneApp.exe') -Destination (Join-Path $app 'DigitoneApp.exe')
foreach ($file in 'Repair Digitone.cmd', 'Repair-Digitone.ps1', 'LICENSE.txt') { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $stage $file) }
(Get-Content -LiteralPath (Join-Path $PSScriptRoot 'README-release.txt') -Raw).Replace('{VERSION}', $version) | Set-Content -LiteralPath (Join-Path $stage 'README.txt') -Encoding ASCII
foreach ($file in 'NAudio.dll', 'NAudio.Core.dll', 'NAudio.Wasapi.dll', 'LICENSE-NAudio.txt', 'Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'WebView2Loader.dll') { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $app $file) }
foreach ($file in 'yt-dlp.exe', 'deno.exe', 'versions.json', 'WebView2-LICENSE.txt', 'ffmpeg\LICENSE.txt') { Copy-Item -LiteralPath (Join-Path $ToolsRoot $file) -Destination (Join-Path $app "Tools\$file") }
Get-ChildItem -LiteralPath (Join-Path $ToolsRoot 'ffmpeg\bin') -File | Where-Object { $_.Name -ne 'ffplay.exe' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $app "Tools\ffmpeg\bin\$($_.Name)") }

$sums = Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.FullName.Substring($stage.Length + 1) }
$sums | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding ASCII

$zip = Join-Path $OutputRoot "$name-Windows-x64.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Standard ZIP entry names use forward slashes (CreateFromDirectory on Windows PowerShell 5.1 writes backslashes).
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem -LiteralPath $work -Recurse -File | Sort-Object FullName) {
        $entryName = $file.FullName.Substring($work.Length + 1).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
"$hash  $name-Windows-x64.zip" | Set-Content -LiteralPath (Join-Path $OutputRoot "$name-SHA256.txt") -Encoding ASCII
Remove-Item -LiteralPath $work -Recurse -Force
Write-Output "Packaged $zip"
