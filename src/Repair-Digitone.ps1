<#
  Repair Digitone
  Reinstalls Digitone's program files (the app folder) from the official GitHub
  releases or a downloaded release ZIP. Everything in userdata (music, playlists,
  settings, lyrics, caches) is kept. Personal data is backed up first, and the
  previous program files are put back if anything goes wrong.
  Everything happens inside this Digitone folder. Nothing is installed on Windows.

  Double-click "Repair Digitone.cmd", or run with options:
    -Version latest | 5.4.14   install that release without asking
    -SourceZip <file>          use a downloaded Digitone-<version>-Windows-x64.zip
    -ListOnly                  show the releases on GitHub and stop
    -Yes -NoPause -NoLaunch    no questions, no final pause, do not start Digitone
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$SourceZip,
    [int]$WaitForPid = 0,
    [switch]$Yes,
    [switch]$NoPause,
    [switch]$NoLaunch,
    [switch]$ListOnly
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$Repo = 'ItsHazardd/Digitone'
$MinimumVersion = [version]'5.4.14'   # first version with the app + userdata layout
$Root = $PSScriptRoot
$App = Join-Path $Root 'app'
$UserData = Join-Path $Root 'userdata'
$Stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$StagingParent = Join-Path $Root 'update-staging'
$Staging = Join-Path $StagingParent "repair-$Stamp"
$BackupRoot = Join-Path $UserData 'backups'
$BrowserCaches = @('Cache', 'Code Cache', 'GPUCache', 'GrShaderCache', 'ShaderCache', 'DawnCache', 'DawnGraphiteCache', 'DawnWebGPUCache', 'Crashpad')
$Host.UI.RawUI.WindowTitle = 'Repair Digitone'

function Say([string]$Text, [string]$Color = 'Gray') { Write-Host $Text -ForegroundColor $Color }
function Finish([int]$Code) {
    if (-not $NoPause) { Write-Host ''; [void](Read-Host 'Press Enter to close') }
    exit $Code
}
function Ask([string]$Question) {
    if ($Yes) { return $true }
    return (Read-Host "$Question [Y/N]") -match '^\s*(y|yes)\s*$'
}
function Invoke-Copy([string]$From, [string]$To, [string[]]$Extra = @()) {
    & robocopy.exe $From $To /E /R:1 /W:1 /NFL /NDL /NJH /NJS /NP @Extra | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying '$From' failed (robocopy code $LASTEXITCODE)." }
}
function Get-MusicInventory {
    $music = Join-Path $UserData 'Music'
    if (-not (Test-Path -LiteralPath $music)) { return @() }
    return @(Get-ChildItem -LiteralPath $music -Recurse -File -Force | ForEach-Object { '{0}|{1}|{2}' -f $_.FullName.Substring($music.Length + 1), $_.Length, $_.LastWriteTimeUtc.Ticks } | Sort-Object)
}
function Get-DigitoneProcesses {
    $prefix = $Root.TrimEnd('\') + '\'
    return @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $path = $null; try { $path = $_.Path } catch { }
        $path -and $path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -and $_.Id -ne $PID
    })
}
function Get-Releases {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent' = 'Digitone-Repair'; 'Accept' = 'application/vnd.github+json' }
    $list = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases?per_page=50" -Headers $headers -TimeoutSec 25
    $official = "^https://github\.com/$([regex]::Escape($Repo))/releases/download/"
    $releases = foreach ($release in @($list)) {
        if ($release.draft) { continue }
        $plain = ([string]$release.tag_name).TrimStart('v', 'V'); $parsed = $null
        if (-not [version]::TryParse($plain, [ref]$parsed)) { continue }
        $zip = @($release.assets) | Where-Object { $_.name -eq "Digitone-$plain-Windows-x64.zip" -and $_.browser_download_url -match $official } | Select-Object -First 1
        $sum = @($release.assets) | Where-Object { $_.name -eq "Digitone-$plain-SHA256.txt" -and $_.browser_download_url -match $official } | Select-Object -First 1
        [pscustomobject]@{
            Version = $parsed; Plain = $plain; Prerelease = [bool]$release.prerelease
            ZipName = "Digitone-$plain-Windows-x64.zip"; ZipUrl = $(if ($zip) { $zip.browser_download_url }); SumUrl = $(if ($sum) { $sum.browser_download_url })
            Usable = ($parsed -ge $MinimumVersion -and $zip -and $sum)
        }
    }
    return @($releases | Sort-Object Version -Descending)
}
function Get-File([string]$Url, [string]$Path) {
    Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing -TimeoutSec 900 -Headers @{ 'User-Agent' = 'Digitone-Repair' }
}
function Test-Checksum([string]$Zip, [string]$SumFile) {
    $name = Split-Path $Zip -Leaf; $expected = $null
    foreach ($line in Get-Content -LiteralPath $SumFile) {
        if ($line -match '^\s*([A-Fa-f0-9]{64})\s+\*?(.+?)\s*$' -and $Matches[2] -eq $name) { $expected = $Matches[1].ToUpperInvariant() }
    }
    if (-not $expected) { throw "The checksum file does not list $name." }
    if ((Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash -ne $expected) { throw 'The package is damaged or was changed (SHA-256 does not match). Nothing was changed.' }
}
function Expand-Package([string]$Zip, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Zip)
    try {
        if ($archive.Entries.Count -gt 2000) { throw 'The package contains too many files.' }
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName -replace '/', '\'
            if ($name.StartsWith('\') -or $name.Contains(':') -or $name -match '(^|\\)\.\.(\\|$)') { throw 'The package contains an unsafe path. Nothing was changed.' }
        }
    } finally { $archive.Dispose() }
    [IO.Compression.ZipFile]::ExtractToDirectory($Zip, $Destination)
    $apps = @(Get-ChildItem -LiteralPath $Destination -Directory -Recurse | Where-Object { $_.Name -eq 'app' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'DigitoneApp.exe')) })
    if ($apps.Count -ne 1) { throw "This package is from before Digitone's portable layout (it has no app folder), so Repair cannot install it. Use version $MinimumVersion or newer." }
    $packageRoot = Split-Path $apps[0].FullName
    foreach ($personal in 'userdata', 'Data', 'DigiMusic') {
        if (Test-Path -LiteralPath (Join-Path $packageRoot $personal)) { throw 'The package tried to include personal data. Nothing was changed.' }
    }
    return $packageRoot
}

Say ''
Say '  REPAIR DIGITONE' Cyan
Say '  Reinstalls the program files in the app folder.'
Say '  Your music, playlists, settings, and lyrics in userdata are kept.'
Say "  Folder: $Root" DarkGray
Say ''

try {
    # 1. Decide what to install.
    $releases = $null; $chosen = $null
    if (-not $SourceZip) {
        if (-not $Version -and -not $Yes -and -not $ListOnly) {
            Say '  [1] Latest version from GitHub (recommended)'
            Say '  [2] Choose a version from GitHub'
            Say '  [3] Use a Digitone ZIP I already downloaded'
            Say '  [Q] Quit'
            $answer = (Read-Host '  Choose').Trim()
            switch -Regex ($answer) {
                '^(1|)$' { $Version = 'latest' }
                '^2$' { $Version = 'choose' }
                '^3$' { $SourceZip = (Read-Host '  Full path to the ZIP').Trim().Trim('"') }
                default { Say 'Nothing was changed.'; Finish 0 }
            }
        }
        if (-not $Version) { $Version = 'latest' }
    }
    if (-not $SourceZip) {
        Say 'Checking GitHub for Digitone releases...'
        try { $releases = Get-Releases } catch { throw "Could not reach GitHub. Check your internet connection, or run Repair again and choose a downloaded ZIP.`n$($_.Exception.Message)" }
        if ($ListOnly) {
            foreach ($release in $releases) { Say ('  {0,-10} {1}' -f $release.Plain, $(if ($release.Usable) { if ($release.Prerelease) { 'test release' } else { 'can be installed' } } elseif ($release.Version -lt $MinimumVersion) { 'too old for Repair (before the portable layout)' } else { 'missing ZIP or checksum' })) }
            Finish 0
        }
        $usable = @($releases | Where-Object { $_.Usable })
        if ($Version -eq 'latest') {
            $chosen = $usable | Where-Object { -not $_.Prerelease } | Select-Object -First 1
            if (-not $chosen) { throw "GitHub has no Digitone release that Repair can install yet (it needs version $MinimumVersion or newer). Use a downloaded ZIP instead." }
        } elseif ($Version -eq 'choose') {
            if ($usable.Count -eq 0) { throw "GitHub has no Digitone release that Repair can install yet (it needs version $MinimumVersion or newer)." }
            for ($i = 0; $i -lt [Math]::Min($usable.Count, 15); $i++) { Say ('  [{0}] {1}{2}' -f ($i + 1), $usable[$i].Plain, $(if ($usable[$i].Prerelease) { '  (test release)' } else { '' })) }
            $pick = 0; if (-not [int]::TryParse((Read-Host '  Version number').Trim(), [ref]$pick) -or $pick -lt 1 -or $pick -gt [Math]::Min($usable.Count, 15)) { Say 'Nothing was changed.'; Finish 0 }
            $chosen = $usable[$pick - 1]
        } else {
            $chosen = $usable | Where-Object { $_.Plain -eq $Version.TrimStart('v', 'V') } | Select-Object -First 1
            if (-not $chosen) { throw "Version $Version is not available for Repair on GitHub." }
        }
    } elseif (-not (Test-Path -LiteralPath $SourceZip -PathType Leaf)) { throw "The ZIP was not found: $SourceZip" }

    $label = if ($chosen) { "Digitone $($chosen.Plain) from GitHub" } else { Split-Path $SourceZip -Leaf }
    if (Get-ChildItem -LiteralPath $App -Filter '*.dev.exe' -ErrorAction SilentlyContinue) { Say 'This is a development copy. Repair will replace it with a public release build.' Yellow }
    if (-not (Ask "Reinstall the program files with $label?")) { Say 'Nothing was changed.'; Finish 0 }

    # 2. Get and verify the package before touching anything.
    New-Item -ItemType Directory -Force -Path $Staging | Out-Null
    if ($chosen) {
        $zip = Join-Path $Staging $chosen.ZipName; $sum = Join-Path $Staging 'SHA256.txt'
        Say "Downloading $($chosen.ZipName)..."
        Get-File $chosen.SumUrl $sum
        Get-File $chosen.ZipUrl $zip
        Say 'Checking the download (SHA-256)...'
        Test-Checksum $zip $sum
    } else {
        $zip = (Resolve-Path -LiteralPath $SourceZip).Path
        $sum = Join-Path (Split-Path $zip) ((Split-Path $zip -Leaf) -replace '-Windows-x64\.zip$', '-SHA256.txt')
        if (Test-Path -LiteralPath $sum) { Say 'Checking the ZIP (SHA-256)...'; Test-Checksum $zip $sum }
        elseif (-not (Ask 'No checksum file was found next to the ZIP, so it cannot be verified. Continue anyway?')) { throw 'Stopped before changing anything.' }
    }
    Say 'Unpacking...'
    $package = Expand-Package $zip (Join-Path $Staging 'package')

    # 3. Make sure Digitone is closed.
    if ($WaitForPid) { try { Wait-Process -Id $WaitForPid -Timeout 30 -ErrorAction Stop } catch { } }
    $running = Get-DigitoneProcesses
    if ($running.Count -gt 0) {
        Say 'Digitone (or one of its download tools) is still running from this folder.' Yellow
        if (-not (Ask 'Close it now?')) { throw 'Close Digitone and run Repair again. Nothing was changed.' }
        foreach ($process in $running) { try { [void]$process.CloseMainWindow() } catch { } }
        Start-Sleep -Seconds 5
        Get-DigitoneProcesses | ForEach-Object { try { $_.Kill() } catch { } }
        Start-Sleep -Seconds 1
        if ((Get-DigitoneProcesses).Count -gt 0) { throw 'Digitone could not be closed. Close it and run Repair again. Nothing was changed.' }
    }

    # 4. Back up personal data (music is only listed: Repair never writes to it).
    $musicBefore = Get-MusicInventory
    $backup = $null
    if (Test-Path -LiteralPath $UserData) {
        $backup = Join-Path $BackupRoot "repair-$Stamp"
        Say 'Backing up your settings, playlists, lyrics, and library...'
        $exclude = @('/XD', (Join-Path $UserData 'Music'), $BackupRoot, (Join-Path $UserData 'cache\temp')) + $BrowserCaches
        Invoke-Copy $UserData (Join-Path $backup 'userdata') $exclude
        $musicBefore | Set-Content -LiteralPath (Join-Path $backup 'music-inventory.txt') -Encoding UTF8
        @(Get-ChildItem -LiteralPath $BackupRoot -Directory -Filter 'repair-*' | Sort-Object Name -Descending | Select-Object -Skip 10) | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
    }

    # 5. Swap the program files, test the new version, and roll back on any failure.
    $oldApp = Join-Path $Root "app.old-$Stamp"; $hadApp = Test-Path -LiteralPath $App
    $rootFiles = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -notin 'Repair Digitone.cmd', 'Repair-Digitone.ps1' })
    $savedRoot = Join-Path $Staging 'previous-root-files'; New-Item -ItemType Directory -Force -Path $savedRoot | Out-Null
    $check = $null
    try {
        Say 'Replacing program files...'
        if ($hadApp) { Rename-Item -LiteralPath $App -NewName (Split-Path $oldApp -Leaf) }
        Invoke-Copy (Join-Path $package 'app') $App
        foreach ($file in $rootFiles) {
            $target = Join-Path $Root $file.Name
            if (Test-Path -LiteralPath $target) { Copy-Item -LiteralPath $target -Destination (Join-Path $savedRoot $file.Name) -Force }
            Copy-Item -LiteralPath $file.FullName -Destination $target -Force
        }
        Say 'Testing the new version with your data...'
        $report = Join-Path $Staging 'check.json'
        $process = Start-Process -FilePath (Join-Path $App 'DigitoneApp.exe') -ArgumentList '--repair-check', ('"' + $report + '"') -WorkingDirectory $App -WindowStyle Hidden -PassThru
        if (-not $process.WaitForExit(90000)) { try { $process.Kill() } catch { }; throw 'The new version did not respond.' }
        if (-not (Test-Path -LiteralPath $report)) { throw 'The new version could not start on this PC.' }
        $check = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
        if ($check.Error) { throw "The new version could not read your library: $($check.Error)" }
        if ($check.LibraryExists -and -not $check.SettingsLoaded) { throw 'The new version did not load your settings.' }
    } catch {
        $reason = $_.Exception.Message
        Say 'Something went wrong. Putting everything back...' Yellow
        $problems = @()
        try {
            if ((Test-Path -LiteralPath $App) -and ($hadApp -eq $false -or (Test-Path -LiteralPath $oldApp))) { Remove-Item -LiteralPath $App -Recurse -Force }
            if ($hadApp -and (Test-Path -LiteralPath $oldApp)) { Rename-Item -LiteralPath $oldApp -NewName 'app' }
        } catch { $problems += "The previous program files are in $oldApp. Rename that folder to 'app'." }
        foreach ($file in $rootFiles) {
            try {
                $saved = Join-Path $savedRoot $file.Name; $target = Join-Path $Root $file.Name
                if (Test-Path -LiteralPath $saved) { Copy-Item -LiteralPath $saved -Destination $target -Force } elseif (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force }
            } catch { $problems += "Could not restore $($file.Name)." }
        }
        if ($backup) { try { Invoke-Copy (Join-Path $backup 'userdata') $UserData } catch { $problems += "Your data backup is in $backup." } }
        if ($problems.Count -gt 0) { throw "Repair stopped, but not everything could be put back automatically:`n$($problems -join "`n")`n$reason" }
        throw "Repair stopped and your previous Digitone was restored. Your personal data is unchanged.`n$reason"
    }

    # 6. Success: clean up and refresh the repair tool itself last.
    if (Test-Path -LiteralPath $oldApp) { Remove-Item -LiteralPath $oldApp -Recurse -Force }
    foreach ($tool in 'Repair Digitone.cmd', 'Repair-Digitone.ps1') {
        $fresh = Join-Path $package $tool
        if (Test-Path -LiteralPath $fresh) { Copy-Item -LiteralPath $fresh -Destination (Join-Path $Root $tool) -Force }
    }
    $musicAfter = Get-MusicInventory
    $musicSame = (@($musicBefore) -join "`n") -eq (@($musicAfter) -join "`n")
    Remove-Item -LiteralPath $Staging -Recurse -Force -ErrorAction SilentlyContinue
    if ((Test-Path -LiteralPath $StagingParent) -and -not (Get-ChildItem -LiteralPath $StagingParent -Force)) { Remove-Item -LiteralPath $StagingParent -Force }

    Say ''
    Say "  DIGITONE $($check.Version) REPAIRED" Green
    Say ('  Songs: {0} of {1} found, playlists: {2}, settings: {3}' -f $check.TracksFound, $check.Tracks, $check.Playlists, $(if ($check.SettingsLoaded) { 'kept' } elseif ($check.LibraryExists) { 'not loaded' } else { 'new setup' })) Green
    Say ('  Music folder: {0} files {1}' -f $musicAfter.Count, $(if ($musicSame) { 'untouched' } else { 'CHANGED - check userdata\Music' })) $(if ($musicSame) { 'Green' } else { 'Yellow' })
    if ($backup) { Say "  Backup of your data: $backup" DarkGray }
    if (-not $NoLaunch -and (Ask 'Start Digitone now?')) {
        $launcher = Join-Path $Root 'Digitone.exe'
        if (Test-Path -LiteralPath $launcher) { Start-Process -FilePath $launcher -WorkingDirectory $Root }
    }
    Finish 0
} catch {
    Say ''
    Say '  REPAIR DID NOT FINISH' Red
    Say ('  ' + $_.Exception.Message) Red
    Remove-Item -LiteralPath $Staging -Recurse -Force -ErrorAction SilentlyContinue
    if ((Test-Path -LiteralPath $StagingParent) -and -not (Get-ChildItem -LiteralPath $StagingParent -Force -ErrorAction SilentlyContinue)) { Remove-Item -LiteralPath $StagingParent -Force -ErrorAction SilentlyContinue }
    Finish 1
}
