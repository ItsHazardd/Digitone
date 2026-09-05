# Digitone

Digitone is a local-first Windows music player with animated themes, playlists, lyrics, artwork tools, visualizers, queue management, Spotify share-link imports, YouTube audio downloads, and high-quality WASAPI playback.

Current release: 5.3.5 for Windows x64.

## Privacy

Digitone stores its library and settings beside the application. The repository and release packages never include a user's music, library database, lyrics, artwork, playlists, browser profile, or personal folders.

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

## Support

Digitone will always be free. Optional tips can be made through [Ko-fi](https://ko-fi.com/itshazzy).

## License

Digitone is available under the MIT License.
