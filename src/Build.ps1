param([string]$OutputName = 'Digitone.exe', [switch]$Release, [string]$IconPath = (Join-Path $PSScriptRoot 'Digitone.ico'))
$ErrorActionPreference = 'Stop'
$version = (Get-Content -LiteralPath (Join-Path $PSScriptRoot 'VERSION') -Raw).Trim()
if ((Get-Content -LiteralPath (Join-Path $PSScriptRoot 'AppInfo.cs') -Raw) -notmatch ('internal const string Version = "' + [regex]::Escape($version) + '";')) { throw "AppInfo.Version does not match VERSION ($version)." }
if ([IO.Path]::GetFileName($OutputName) -ne $OutputName -or [IO.Path]::GetExtension($OutputName) -ne '.exe') { throw 'OutputName must be an executable filename inside DAP.' }
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$refs = @('System.dll', 'System.Drawing.dll', 'System.Core.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'System.Xaml.dll', 'System.Web.Extensions.dll', 'System.Windows.Forms.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')
$arguments = @('/nologo', '/target:winexe', '/optimize+', ('/out:' + (Join-Path $PSScriptRoot $OutputName)), ('/resource:' + (Join-Path $PSScriptRoot 'MainWindow.xaml') + ',MainWindow.xaml'))
foreach ($reference in $refs) { $arguments += '/reference:' + (Join-Path $framework $reference) }
$arguments += Join-Path $PSScriptRoot 'Digitone.cs'
$arguments += Join-Path $PSScriptRoot 'Portable.cs'
$arguments += Join-Path $PSScriptRoot 'Export.cs'
$arguments += Join-Path $PSScriptRoot 'Recognition.cs'
$arguments += Join-Path $PSScriptRoot 'LyricsEngine.cs'
$arguments += Join-Path $PSScriptRoot 'LyricsReview.cs'
$arguments += Join-Path $PSScriptRoot 'BetaMotion.cs'
$arguments += Join-Path $PSScriptRoot 'BetaTheme.cs'
$arguments += Join-Path $PSScriptRoot 'Beta3D.cs'
$arguments += Join-Path $PSScriptRoot 'BetaVisuals.cs'
$arguments += Join-Path $PSScriptRoot 'MinecraftTextures.cs'
$arguments += Join-Path $PSScriptRoot 'RaveClock.cs'
$arguments += Join-Path $PSScriptRoot 'BetaRave.cs'
$arguments += Join-Path $PSScriptRoot 'AlbumArt.cs'
$arguments += Join-Path $PSScriptRoot 'RecognitionView.cs'
if (!$Release) { $arguments += Join-Path $PSScriptRoot 'SelfTest.cs'; $arguments += Join-Path $PSScriptRoot 'PerfTest.cs' } else { $arguments += '/define:RELEASE'; $arguments += '/platform:x64' }
$arguments += Join-Path $PSScriptRoot 'FirstRun.cs'
$arguments += Join-Path $PSScriptRoot 'WhatsNew.cs'
$arguments += Join-Path $PSScriptRoot 'Downloads.cs'
$arguments += Join-Path $PSScriptRoot 'SpotifyImport.cs'
$arguments += Join-Path $PSScriptRoot 'SongTags.cs'
$arguments += Join-Path $PSScriptRoot 'Visualizer.cs'
$arguments += Join-Path $PSScriptRoot 'PlaylistFiles.cs'
$arguments += Join-Path $PSScriptRoot 'Themes.cs'
$arguments += Join-Path $PSScriptRoot 'ThemeScenes.cs'
$arguments += Join-Path $PSScriptRoot 'NativeChrome.cs'
$arguments += Join-Path $PSScriptRoot 'MediaKeys.cs'
$arguments += Join-Path $PSScriptRoot 'Lyrics.cs'
$arguments += Join-Path $PSScriptRoot 'Motion.cs'
$arguments += Join-Path $PSScriptRoot 'LiveWave.cs'
$arguments += Join-Path $PSScriptRoot 'WaveView.cs'
$arguments += Join-Path $PSScriptRoot 'AudioScene.cs'
$arguments += Join-Path $PSScriptRoot 'AudioPlayback.cs'
$arguments += Join-Path $PSScriptRoot 'Updates.cs'
$arguments += Join-Path $PSScriptRoot 'FrequencyBands.cs'
$arguments += Join-Path $PSScriptRoot 'Settings.cs'
$arguments += Join-Path $PSScriptRoot 'SettingsView.cs'
$arguments += Join-Path $PSScriptRoot 'AppInfo.cs'
$arguments += Join-Path $PSScriptRoot 'ArtworkFinder.cs'
$arguments += Join-Path $PSScriptRoot 'ReleaseDate.cs'
$arguments += Join-Path $PSScriptRoot 'CoverSearch.cs'
$arguments += Join-Path $PSScriptRoot 'CoverGallery.cs'
if (!$Release) { $arguments += Join-Path $PSScriptRoot 'CoverBrowserTest.cs' }
if (!$Release) { foreach ($fixture in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'TestFixtures') -Filter '*.json') { $arguments += '/resource:' + $fixture.FullName + ',Fixture.' + $fixture.Name } }
$arguments += '/win32icon:' + $IconPath
$arguments += '/resource:' + $IconPath + ',Digitone.ico'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'DigitoneMark.png') + ',DigitoneMark.png'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'HazzySignature.png') + ',HazzySignature.png'
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'SanctuaryPlant.png') + ',SanctuaryPlant.png'
# Minecraft theme textures (Faithful 64x, credited in the README)
foreach ($texture in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'Minecraft') -Filter '*.png') { $arguments += '/resource:' + $texture.FullName + ',Minecraft.' + $texture.Name }
$arguments += '/resource:' + (Join-Path $PSScriptRoot 'CoverIntegration.js') + ',CoverIntegration.js'
$arguments += '/reference:' + (Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.Core.dll')
$arguments += '/reference:' + (Join-Path $PSScriptRoot 'Microsoft.Web.WebView2.Wpf.dll')
$arguments += '/reference:' + (Join-Path $PSScriptRoot 'NAudio.dll')
$arguments += '/reference:' + (Join-Path $PSScriptRoot 'NAudio.Core.dll')
$arguments += '/reference:' + (Join-Path $PSScriptRoot 'NAudio.Wasapi.dll')
$arguments += '/reference:' + (Join-Path $framework 'netstandard.dll')
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Output "Built $OutputName. No downloads or external packages required for the build."
