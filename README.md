# Digitone

Digitone is a local-first Windows music player with animated themes, playlists, lyrics, artwork tools, visualizers, queue management, and high-quality WASAPI playback.

Built by GEMMA in conjunction with Hazzy: 70% GEMMA, 30% Hazzy.

Current release: 3.3.31 for Windows x64.

## Privacy

Digitone stores its library and settings beside the application. The repository and release packages never include a user's music, library database, lyrics, artwork, playlists, or personal folders.

## Building

Digitone currently targets Windows x64 and the installed .NET Framework compiler.

```powershell
cd src
.\Build.ps1 -OutputName Digitone.dev.exe
```

Run the local test harness:

```powershell
.\Digitone.dev.exe --self-test TestResults
```

A release build excludes the test harness:

```powershell
.\Build.ps1 -OutputName Digitone.exe -Release
```

Third-party DLLs used at build time are included with their license notices. Download-tool executables and personal runtime data are intentionally excluded from source control.

## License

Digitone is available under the MIT License.
