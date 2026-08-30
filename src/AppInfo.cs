namespace Digitone
{
    internal static class AppInfo
    {
        // Release.Batch.Day — update this single value after each completed batch.
        internal const string Version = "2.9.30";
        internal const string Changelog =
@"VERSION 2 · CURRENT CHANGES

LIBRARY AND PLAYLISTS
• Restored the Add to playlist submenu in the song right-click menu for single and Ctrl/Shift selections.
• New manual, downloaded, and folder playlists stay in the playlist grid until explicitly added to quick access.

PLAYBACK AND SOUND
• Removed experimental record scratching and restored the normal playback path; ordinary record rotation remains unchanged.
• Made UI playback startup deterministic so the selected song reliably reaches the audio engine.
• Added a persistent Audio Output selector with Windows-default fallback for disconnected devices.

ACCESSIBILITY
• Rebuilt the changelog as a version browser with retained release history.
• Added optional startup checks for stable releases from the official Digitone GitHub repository; downloads always require permission.
• Fixed GitHub update checks on Windows systems that require explicit TLS 1.2.
• Approved updates now download, verify, install, preserve personal data, restart automatically, and roll back after an unhealthy launch.
• Added the signed-package release path used to validate automatic updates from an older portable build.
• Kept the themed library surface intact while batch artwork is being written.";

        internal const string Release1Changelog =
@"VERSION 1 · RELEASE HISTORY

PLAYBACK AND SOUND
• Added a five-band playback equalizer with smooth click-and-drag controls.
• Preserved volume between songs and application restarts.
• Added a short seek-release delay to prevent scrubber static.
• Added Waveform, Spectrum, and Off modes to the main player.
• Active collections restart from their first song after natural queue completion.

LIBRARY AND PLAYLISTS
• Added a dedicated playlist grid with customizable square covers.
• Added synchronized playlists for additional folders imported through Add Folder while keeping the main library root out of the playlist grid.
• Rescanning removes missing folder tracks and deleted generated playlists.
• Added direct playlist ordering and queue drag-and-drop.
• Added a default DigiMusic library and download folder beside the application.
• Added right-click playlist assignment for one song or Ctrl/Shift multi-selections.

ARTWORK AND LYRICS
• Added individual verified batch-artwork writing for MP3, FLAC, and M4A files without audio re-encoding or song backups.
• Added local, online, and manual artwork lookup from the song context menu.
• Added automatic and manual lyrics lookup with saved scrolling lyrics.
• Songs without artwork use the supplied Digitone mark; real artwork is never covered by it.

APPEARANCE AND SETUP
• Added four themed animated backgrounds, custom accent colors, and Light/Dark surfaces.
• Added an optional persistent Neon mode for controls, visualizers, and moving theme accents.
• Added the Spectrum visualizer while keeping the turntable prominent.
• Added first-run folder setup, dependency updates, GEMMA and Hazzy credits, centralized versioning, and this changelog.";
        internal static readonly string[] ChangelogVersions = { "Version 2 · Current", "Version 1 · Release history" };
        internal static readonly string[] Changelogs = { Changelog, Release1Changelog };
    }
}
