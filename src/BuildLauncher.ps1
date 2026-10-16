param([string]$OutputName = 'Digitone.launcher.exe', [string]$IconPath = (Join-Path $PSScriptRoot 'Digitone.ico'))
$ErrorActionPreference = 'Stop'
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or [IO.Path]::GetExtension($OutputName) -ne '.exe') { throw 'OutputName must be an executable filename.' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$arguments = @('/nologo', '/target:winexe', '/optimize+', ('/out:' + (Join-Path $PSScriptRoot $OutputName)), ('/win32icon:' + $IconPath))
foreach ($reference in 'System.dll', 'System.Core.dll', 'System.Windows.Forms.dll') { $arguments += '/reference:' + (Join-Path $framework $reference) }
$arguments += Join-Path $PSScriptRoot 'Launcher.cs'
& (Join-Path $framework 'csc.exe') $arguments
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed.' }
Write-Output "Built $OutputName."
