# Dev-only test harness for Repair Digitone and the 5.3.5 -> portable upgrade bridge.
# Uses the package from Package.ps1 and throwaway folders under %TEMP%\dgr. Never touches DevDIGITONE's own app or userdata.
param(
    [string]$Zip = (Join-Path (Split-Path $PSScriptRoot) 'Releases\Digitone-5.4.14-Windows-x64.zip'),
    [string]$OldZip = (Join-Path (Split-Path (Split-Path $PSScriptRoot)) 'Digitone5.3.5\Digitone-5.3.5-Windows-x64.zip'),
    [string]$SampleSong = (Get-ChildItem (Join-Path (Split-Path $PSScriptRoot) 'userdata\Music') -Filter *.mp3 -Recurse | Select-Object -First 1).FullName,
    [switch]$SkipNetwork
)
$ErrorActionPreference = 'Stop'
$base = Join-Path $env:TEMP 'dgr'
if (Test-Path $base) { Remove-Item $base -Recurse -Force }
New-Item -ItemType Directory -Force -Path $base | Out-Null
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$script:failures = 0; $script:passes = 0
function Check([bool]$Condition, [string]$Name) { if ($Condition) { $script:passes++; Write-Host "PASS: $Name" -ForegroundColor Green } else { $script:failures++; Write-Host "FAIL: $Name" -ForegroundColor Red } }
function Snapshot([string]$Folder, [string[]]$Skip = @('backups')) {
    if (-not (Test-Path $Folder)) { return '' }
    (Get-ChildItem $Folder -Recurse -File -Force | Where-Object { $rel = $_.FullName.Substring($Folder.Length + 1); -not ($Skip | Where-Object { $rel -like "$_\*" }) } | ForEach-Object { $_.FullName.Substring($Folder.Length + 1) + '|' + (Get-FileHash $_.FullName -Algorithm SHA256).Hash } | Sort-Object) -join "`n"
}
function Repair([string]$Root, [string[]]$Arguments) {
    $log = Join-Path $base ("log-" + [Guid]::NewGuid().ToString('N') + '.txt')
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $Root 'Repair-Digitone.ps1') -NoPause -NoLaunch @Arguments *> $log
    $code = $LASTEXITCODE
    return [pscustomobject]@{ Code = $code; Log = (Get-Content $log -Raw) }
}
$extracted = Join-Path $base 'package'
[IO.Compression.ZipFile]::ExtractToDirectory($Zip, $extracted)
$packageRoot = (Get-ChildItem $extracted -Directory | Select-Object -First 1).FullName
function New-Fixture([string]$Name) {
    $root = Join-Path $base $Name
    Copy-Item $packageRoot $root -Recurse
    $user = Join-Path $root 'userdata'
    foreach ($dir in 'Music\Album', 'data\WebView2\YouTube\Default', 'lyrics') { New-Item -ItemType Directory -Force -Path (Join-Path $user $dir) | Out-Null }
    Copy-Item $SampleSong (Join-Path $user 'Music\Album\song.mp3'); Copy-Item $SampleSong (Join-Path $user 'Music\loose.mp3')
    '{"Tracks":[{"Path":"Music\\Album\\song.mp3","SongTitle":"Song","Lyrics":"la la"},{"Path":"Music\\loose.mp3"}],"Playlists":[{"Name":"Road","Paths":["Music\\Album\\song.mp3"]}],"SetupCompleted":true,"ThemeName":"Pride","DownloadFolder":"Music","MainMusicFolder":"Music","MusicFolders":["Music"]}' | Set-Content (Join-Path $user 'data\library.json') -Encoding UTF8
    'cookie' | Set-Content (Join-Path $user 'data\WebView2\YouTube\Default\Cookies')
    '[00:01.00]la la' | Set-Content (Join-Path $user 'lyrics\song.lrc')
    return $root
}
$sum = Join-Path (Split-Path $Zip) ((Split-Path $Zip -Leaf) -replace '-Windows-x64\.zip$', '-SHA256.txt')

# 1. Broken program files are restored; personal data untouched.
$root = New-Fixture 'broken'
$userBefore = Snapshot (Join-Path $root 'userdata')
Remove-Item (Join-Path $root 'app\DigitoneApp.exe'), (Join-Path $root 'app\NAudio.dll'), (Join-Path $root 'app\Tools\yt-dlp.exe')
$result = Repair $root @('-SourceZip', $Zip, '-Yes')
Check ($result.Code -eq 0) 'Repair succeeds on a broken app folder'
Check ((Snapshot (Join-Path $root 'app')) -eq (Snapshot (Join-Path $packageRoot 'app'))) 'Every program file matches the package after repair'
Check ((Snapshot (Join-Path $root 'userdata')) -eq $userBefore) 'Music, library, lyrics, and browser sign-in are byte-for-byte unchanged'
$backup = Get-ChildItem (Join-Path $root 'userdata\backups') -Directory -Filter 'repair-*' | Select-Object -First 1
Check ($backup -and (Test-Path (Join-Path $backup.FullName 'userdata\data\library.json')) -and (Test-Path (Join-Path $backup.FullName 'userdata\lyrics\song.lrc')) -and -not (Test-Path (Join-Path $backup.FullName 'userdata\Music')) -and ((Get-Content (Join-Path $backup.FullName 'music-inventory.txt')).Count -eq 2)) 'Personal data is backed up first; music is listed, not copied'
Check (-not (Get-ChildItem $root -Directory -Filter 'app.old-*') -and -not (Test-Path (Join-Path $root 'update-staging'))) 'Repair leaves no temporary folders behind'
Check ($result.Log -match 'Songs: 2 of 2 found' -and $result.Log -match 'Music folder: 2 files untouched') 'Repair summary reports songs found and music untouched'

# 2. Works when the whole app folder and launcher are gone.
$root = New-Fixture 'missing'
$userBefore = Snapshot (Join-Path $root 'userdata')
Remove-Item (Join-Path $root 'app') -Recurse; Remove-Item (Join-Path $root 'Digitone.exe')
$result = Repair $root @('-SourceZip', $Zip, '-Yes')
Check ($result.Code -eq 0 -and (Test-Path (Join-Path $root 'Digitone.exe')) -and (Test-Path (Join-Path $root 'app\DigitoneApp.exe')) -and (Snapshot (Join-Path $root 'userdata')) -eq $userBefore) 'Repair rebuilds a missing app folder and launcher without touching userdata'

# 3. A package that fails its health check is rolled back.
$badWork = Join-Path $base 'bad'; Copy-Item $extracted $badWork -Recurse
$badRoot = (Get-ChildItem $badWork -Directory | Select-Object -First 1).FullName
'not a program' | Set-Content (Join-Path $badRoot 'app\DigitoneApp.exe')
'changed readme' | Set-Content (Join-Path $badRoot 'README.txt')
$badZip = Join-Path $base 'Digitone-9.9.9-Windows-x64.zip'
$archive = [IO.Compression.ZipFile]::Open($badZip, 'Create'); foreach ($file in Get-ChildItem $badWork -Recurse -File) { [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $file.FullName.Substring($badWork.Length + 1).Replace('\', '/')) }; $archive.Dispose()
"$((Get-FileHash $badZip).Hash)  Digitone-9.9.9-Windows-x64.zip" | Set-Content (Join-Path $base 'Digitone-9.9.9-SHA256.txt')
$root = New-Fixture 'rollback'
$appBefore = Snapshot (Join-Path $root 'app'); $userBefore = Snapshot (Join-Path $root 'userdata'); $readmeBefore = Get-Content (Join-Path $root 'README.txt') -Raw
$result = Repair $root @('-SourceZip', $badZip, '-Yes')
Check ($result.Code -eq 1 -and $result.Log -match 'previous Digitone was restored') 'A package that cannot start is refused and reported'
Check ((Snapshot (Join-Path $root 'app')) -eq $appBefore -and (Get-Content (Join-Path $root 'README.txt') -Raw) -eq $readmeBefore -and (Snapshot (Join-Path $root 'userdata')) -eq $userBefore -and -not (Get-ChildItem $root -Directory -Filter 'app.old-*')) 'Failed repair puts the previous program files back and leaves personal data unchanged'

# 4. Pre-portable packages and damaged downloads are refused before anything changes.
$root = New-Fixture 'refuse'
$appBefore = Snapshot (Join-Path $root 'app'); $userBefore = Snapshot (Join-Path $root 'userdata')
$result = Repair $root @('-SourceZip', $OldZip, '-Yes')
Check ($result.Code -eq 1 -and $result.Log -match 'before Digitone''s portable layout' -and (Snapshot (Join-Path $root 'app')) -eq $appBefore) 'Version 5.3.5 (old layout) is refused without changes'
$wrongZip = Join-Path $base 'wrong\Digitone-5.4.14-Windows-x64.zip'; New-Item -ItemType Directory -Force (Split-Path $wrongZip) | Out-Null; Copy-Item $Zip $wrongZip
"$('0' * 64)  Digitone-5.4.14-Windows-x64.zip" | Set-Content (Join-Path (Split-Path $wrongZip) 'Digitone-5.4.14-SHA256.txt')
$result = Repair $root @('-SourceZip', $wrongZip, '-Yes')
Check ($result.Code -eq 1 -and $result.Log -match 'SHA-256 does not match' -and (Snapshot (Join-Path $root 'app')) -eq $appBefore -and (Snapshot (Join-Path $root 'userdata')) -eq $userBefore) 'A damaged or changed package is refused without changes'

# 5. Digitone still open from the folder is closed first.
$root = New-Fixture 'running'
$userBefore = Snapshot (Join-Path $root 'userdata')
Start-Process (Join-Path $root 'Digitone.exe'); Start-Sleep -Seconds 10
$wasRunning = [bool](Get-Process DigitoneApp -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$root\*" })
$result = Repair $root @('-SourceZip', $Zip, '-Yes')
$userAfter = Snapshot (Join-Path $root 'userdata') @('backups', 'data\library.json', 'data\library.json.bak', 'data\WebView2', 'data\CoverBrowser', 'cache')
$musicSame = (Snapshot (Join-Path $root 'userdata\Music')) -eq (Snapshot (Join-Path (Join-Path $base 'broken') 'userdata\Music'))
Check ($wasRunning -and $result.Code -eq 0 -and $musicSame -and -not (Get-Process DigitoneApp -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$root\*" })) 'Repair closes a running Digitone, then repairs with music intact'

if (-not $SkipNetwork) {
    # 6. GitHub: lists releases and refuses ones from before the portable layout.
    $root = New-Fixture 'github'
    $appBefore = Snapshot (Join-Path $root 'app')
    $result = Repair $root @('-ListOnly')
    Check ($result.Code -eq 0 -and $result.Log -match '5\.3\.5\s+too old for Repair') 'GitHub listing marks 5.3.5 as too old for Repair'
    $result = Repair $root @('-Version', 'latest', '-Yes')
    Check ($result.Code -eq 1 -and $result.Log -match 'no Digitone release that Repair can install yet' -and (Snapshot (Join-Path $root 'app')) -eq $appBefore) 'With no portable release on GitHub yet, Repair stops cleanly without changes'
}

# 7. Upgrade bridge: a 5.3.5 install updated the old way ends up in the portable layout.
$old = Join-Path $base 'old535'
[IO.Compression.ZipFile]::ExtractToDirectory($OldZip, (Join-Path $base 'old-extract'))
Move-Item (Join-Path $base 'old-extract\Digitone') $old
New-Item -ItemType Directory -Force (Join-Path $old 'DigiMusic\Mix'), (Join-Path $old 'Data') | Out-Null
Copy-Item $SampleSong (Join-Path $old 'DigiMusic\Mix\old.mp3')
$oldSong = Join-Path $old 'DigiMusic\Mix\old.mp3'
@{ Tracks = @(@{ Path = $oldSong; Lyrics = 'old lyrics' }); Playlists = @(@{ Name = 'Old mix'; Paths = @($oldSong); FolderPath = (Join-Path $old 'DigiMusic\Mix') }); SetupCompleted = $true; ThemeName = 'Pride'; MainMusicFolder = (Join-Path $old 'DigiMusic'); DownloadFolder = (Join-Path $old 'DigiMusic'); MusicFolders = @((Join-Path $old 'DigiMusic')) } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $old 'Data\library.json') -Encoding UTF8
$staging = Join-Path $base 'old-staging'; New-Item -ItemType Directory -Force $staging | Out-Null
$bridgePackage = Join-Path $base 'bridge-package'; Copy-Item $packageRoot $bridgePackage -Recurse
$deadPid = (Start-Process cmd.exe -ArgumentList '/c', 'exit' -PassThru -WindowStyle Hidden); $deadPid.WaitForExit()
$plan = @{ InstallRoot = $old; PackageRoot = $bridgePackage; StagingRoot = $staging; BackupRoot = (Join-Path $staging 'rollback'); HealthMarker = (Join-Path $staging 'healthy.txt'); ParentProcessId = $deadPid.Id } | ConvertTo-Json
$planPath = Join-Path $staging 'update-plan.json'; $plan | Set-Content $planPath -Encoding ASCII
Start-Process (Join-Path $bridgePackage 'Digitone.exe') -ArgumentList '--apply-update', ('"' + $planPath + '"') -WorkingDirectory $bridgePackage
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline -and -not ((Test-Path (Join-Path $staging 'healthy.txt')) -and -not (Get-Process DigitoneApp -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$bridgePackage\*" }))) { Start-Sleep -Seconds 1 }
Start-Sleep -Seconds 3
$library = if (Test-Path (Join-Path $old 'userdata\data\library.json')) { Get-Content (Join-Path $old 'userdata\data\library.json') -Raw } else { '' }
Check ((Test-Path (Join-Path $staging 'healthy.txt')) -and (Test-Path (Join-Path $old 'app\DigitoneApp.exe')) -and (Test-Path (Join-Path $old 'Repair Digitone.cmd'))) 'A 5.3.5 in-app update installs the portable layout and passes its health check'
Check ((Test-Path (Join-Path $old 'userdata\Music\Mix\old.mp3')) -and -not (Test-Path (Join-Path $old 'DigiMusic')) -and -not (Test-Path (Join-Path $old 'Data')) -and $library -match 'Music\\\\Mix\\\\old\.mp3' -and $library -match 'Pride' -and ($library -match 'old lyrics' -or (Get-ChildItem (Join-Path $old 'userdata\lyrics') -File -ErrorAction SilentlyContinue | Where-Object { [IO.File]::ReadAllText($_.FullName) -eq 'old lyrics' }))) 'After a 5.3.5 update, old Data and DigiMusic move into userdata with library, lyrics, and theme kept'
Get-Process DigitoneApp -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$base\*" } | ForEach-Object { [void]$_.CloseMainWindow() }
Start-Sleep -Seconds 4
Get-Process DigitoneApp, Digitone -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$base\*" } | Stop-Process -Force

Write-Host ''
Write-Host "$script:passes passed, $script:failures failed"
if ($script:failures -eq 0) { Remove-Item $base -Recurse -Force -ErrorAction SilentlyContinue }
exit $script:failures
