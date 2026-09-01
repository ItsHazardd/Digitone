namespace Digitone
{
    internal static class AppInfo
    {
        // Release.Batch.Day: update this single value after each completed batch.
        internal const string Version = "4.0.1";
        internal const string Changelog =
@"VERSION 4 · CURRENT RELEASE

DOWNLOADS AND YOUTUBE
• Downloads can be sent directly to any existing playlist from the persistent Downloads destination menu.
• New downloaded playlists keep their audio in separate named folders instead of mixing every download together.
• Added a sequential download queue with pending, active, completed, failed, cancelled, retry, and open-folder controls.
• Simplified download destinations into mutually exclusive Existing playlist and New playlist modes, with diagnostics collapsed by default.
• Added a separate YouTube viewer for recommendations, persistent sign-in, and sending its current video or playlist directly to Digitone's downloader.
• Added audio-only Current video and Entire playlist actions plus an existing-playlist destination picker inside the YouTube viewer.

LIBRARY AND UNDO
• Added a compact neon Undo notice with a ten-second countdown bar.
• Undo restores removals from playlists, folder playlists, Favorites, the library, playback queue, and pending download queue.
• Library Undo restores metadata, ordering, playlist membership, and queue placement.
• Rescanning remembers intentional library and folder-playlist removals instead of silently adding those songs back.
• Moved song removal from the bottom action row into the song right-click menu while preserving Ctrl/Shift selection and Undo.

SETTINGS AND ACCESSIBILITY
• Reorganized Settings into Appearance, Audio, Music folders, and General tabs, with vertical wheel scrolling preserved over the horizontal theme strip.
• Made full playlist names visible through wrapped cards, wider selectors, fitted collection headers, and complete-title tooltips.
• Kept download diagnostics collapsed until Show details is selected.";

        internal const string Release3Changelog =
@"VERSION 3 · RELEASE HISTORY

LIBRARY AND PLAYLISTS
• Restored the Add to playlist submenu in the song right-click menu for single and Ctrl/Shift selections.
• New manual, downloaded, and folder playlists stay in the playlist grid until explicitly added to quick access.

PLAYBACK AND SOUND
• Removed experimental record scratching and restored the normal playback path; ordinary record rotation remains unchanged.
• Made UI playback startup deterministic so the selected song reliably reaches the audio engine.
• Added a persistent Audio Output selector with Windows-default fallback for disconnected devices.
• Made natural song endings advance on the UI thread, tolerate near-end decoder positions, and skip unavailable queued files.

ACCESSIBILITY
• Rebuilt the changelog as a version browser with retained release history.
• Added optional startup checks for stable releases from the official Digitone GitHub repository; downloads always require permission.
• Fixed GitHub update checks on Windows systems that require explicit TLS 1.2.
• Approved updates now download, verify, install, preserve personal data, restart automatically, and roll back after an unhealthy launch.
• Added the signed-package release path used to validate automatic updates from an older portable build.
• Kept the themed library surface intact while batch artwork is being written.

APPEARANCE
• Removed minimize and maximize controls from secondary Digitone windows.
• Gave Custom Color dark surfaces more of the selected color without changing preset themes.
• Made playlist names scale down automatically to fit their cards.
• Highlighted the currently playing library row with the active theme accent.
• Added Tree House Sanctuary with lo-fi typography, animated greenery, light-mode sun, dark-mode stars, and one user-supplied hanging plant processed without generative media.
• Separated the playing-song accent from the quieter multi-selection state.
• Made theme cards horizontally scroll at a fixed size, preserving artwork proportions and complete labels.
• Protected Sanctuary artwork proportions, fitted its header branding, and connected its stars to Neon mode.
• Extended optional Neon glow to the fullscreen visualizers without changing their modes or playback path.
• Restored fluid aspect-ratio record sizing inside a dedicated layout row so it never overlaps song information.
• Kept Sanctuary greenery visible across landscape and portrait layouts while preserving scene proportions.
• Added an original Mount Olympus theme with a summit temple and Athena owl relief, layered rocky cliffs, ascending stairs, moving fog, flowing music, and divine day and storm-night skies drawn entirely in code.
• Preserved the Olympus architecture proportions in tall windows and moved its focal group clear of the centered record.

UPDATES
• Removed release-note spoilers from automatic update prompts; full details remain in the Changelog menu.
• Added a guarded near-end playback fallback so a missed decoder event advances once without double-skipping.";

        internal const string Release2Changelog =
@"VERSION 2 · RELEASE HISTORY

PLAYBACK AND SOUND
• Added persistent volume, multimedia-key support, queue cycling, audio-output selection, and a real five-band equalizer.
• Added smooth main and fullscreen visualizers with Waveform, Spectrum, Radial, Prism, and three-band Pulse modes.

LIBRARY AND PLAYLISTS
• Added editable metadata, batch artwork, saved lyrics, synchronized folder playlists, drag ordering, and queue management.
• Added a separate customizable playlist grid and explicit quick-access favorites.

APPEARANCE AND DELIVERY
• Added responsive portrait and landscape layouts, preset themes, Custom Color, Light/Dark surfaces, and optional Neon accents.
• Added clean portable Windows packaging, first-run setup, dependency tools, versioned changelogs, and verified automatic updates.";

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
        internal static readonly string[] ChangelogVersions = { "Version 4 · Current release", "Version 3 · Release history", "Version 2 · Release history", "Version 1 · Release history" };
        internal static readonly string[] Changelogs = { Changelog, Release3Changelog, Release2Changelog, Release1Changelog };
    }
}
