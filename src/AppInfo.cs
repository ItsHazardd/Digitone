namespace Digitone
{
    internal static class AppInfo
    {

        internal const string Version = "5.0.5";
        internal const string Changelog =
@"VERSION 5 · CURRENT RELEASE

SPOTIFY IMPORTS
• Added public Spotify track, album, playlist, and shortened share-link imports without requiring a Spotify login.
• Spotify links automatically find YouTube matches while uncertain results remain unchecked for approval.
• Each approved Spotify import creates a timestamped download folder and matching Digitone playlist.
• Finished Spotify downloads use Spotify's title and artist without changing or verifying unrelated metadata.

SETTINGS AND SUPPORT
• Added The Future tab with Hazzy's message, Ko-fi link, animated neon border, and glowing signature.
• Enlarged the Settings window while keeping it within the usable desktop area on smaller displays.
• Simplified the Made for music footer while keeping version history and Changelog access.
• Added an optional Ko-fi support link.
• Cleaned the retained Version 1 through Version 4 changelog archives so they describe shipped features only.

UPDATES
• Removed feature spoilers and release notes from automatic update prompts.
• Approved updates continue to preserve personal data while replacing program files.";

        internal const string Release4Changelog =
@"VERSION 4 · RELEASE HISTORY

DOWNLOADS AND YOUTUBE
• Added downloads to existing playlists and new playlists with separate audio folders.
• Added a sequential download queue with progress, cancellation, retry, and open-folder controls.
• Added the separate YouTube viewer for recommendations and quick audio downloads.
• Kept download diagnostics collapsed until requested.

LIBRARY AND UNDO
• Added the compact neon Undo notice with a ten-second countdown bar.
• Undo restores removals from playlists, folder playlists, Favorites, the library, playback queue, and pending downloads.
• Rescanning remembers intentionally removed library and folder-playlist songs.
• Moved song removal into the right-click menu while preserving Ctrl and Shift multi-selection.

SETTINGS AND ACCESSIBILITY
• Reorganized Settings into Appearance, Audio, Music folders, and General tabs.
• Preserved vertical settings scrolling over the horizontal theme strip.
• Made long playlist names readable in cards, selectors, and collection headers.";

        internal const string Release3Changelog =
@"VERSION 3 · RELEASE HISTORY

LIBRARY AND PLAYLISTS
• Added an Add to playlist submenu for one song or Ctrl and Shift multi-selections.
• Manual, downloaded, and folder playlists remain in the playlist grid until explicitly added to quick access.

PLAYBACK AND SOUND
• Added a persistent Audio Output selector with Windows-default fallback when a saved device is unavailable.
• Improved playback startup and reliable advancement through queued songs.
• Collections restart from their first available song after natural completion.

UPDATES AND ACCESSIBILITY
• Added optional startup checks for stable releases from the official Digitone GitHub repository.
• Approved updates download, verify, install, preserve personal data, restart automatically, and roll back after an unhealthy launch.
• Added retained Version 1 through Version 3 changelog browsing.
• Removed minimize and maximize controls from secondary Digitone windows.
• Kept the themed library visible while batch artwork is being written.

APPEARANCE
• Improved Custom Color dark surfaces while leaving preset theme palettes unchanged.
• Added separate visual states for the playing song and selected songs.
• Added fixed-size horizontally scrolling theme cards with complete labels.
• Added Tree House Sanctuary with animated greenery, a daytime sun, a night sky, and Neon stars.
• Added Neon glow to fullscreen visualizers.
• Made the main turntable resize with the window without overlapping song information.
• Added the Mount Olympus theme with a summit temple, rocky cliffs, stairs, moving fog, flowing music, and day and night skies.";

        internal const string Release2Changelog =
@"VERSION 2 · RELEASE HISTORY

PLAYBACK AND SOUND
• Added a five-band playback equalizer with click-and-drag controls.
• Preserved volume between songs and application restarts.
• Added a short seek-release delay to prevent scrubber static.
• Added Waveform, Spectrum, and Off modes to the main player.

LIBRARY AND PLAYLISTS
• Added a separate playlist grid with customizable square covers.
• Added synchronized playlists for folders imported through Add Folder while keeping the main music root out of the playlist grid.
• Rescanning removes missing folder songs and playlists whose folders were deleted.
• Added playlist ordering, queue drag-and-drop, and right-click playlist assignment for multi-selected songs.
• Added the default DigiMusic library and download folder.

ARTWORK AND LYRICS
• Added individual and batch artwork editing for MP3, FLAC, and M4A without re-encoding audio.
• Added local, online, and manual artwork lookup from the song menu.
• Added automatic and manual lyrics lookup with saved scrolling lyrics.
• Songs without artwork use the Digitone mark while existing artwork remains unobstructed.

APPEARANCE AND SETUP
• Added animated preset themes, Custom Color, Light and Dark modes, and optional Neon accents.
• Added first-run music-folder setup, dependency updates, version numbers, and changelog access.";

        internal const string Release1Changelog =
@"VERSION 1 · RELEASE HISTORY

CORE PLAYER
• Introduced the portable Windows music player and local music library.
• Added play, pause, previous, next, seek, and volume controls.
• Added All Music, Favorites, Downloads, search, and basic playback queue support.
• Added the animated record player and fullscreen music visualization.

LIBRARY
• Added music folder importing and library rescanning.
• Added song metadata editing, album artwork display, and lyrics viewing.
• Added playlist creation and saved library data between launches.

APPEARANCE
• Added the original Digitone interface, theme selection, custom accent color, and responsive window layouts.";

        internal static readonly string[] ChangelogVersions = { "Version 5 · Current release", "Version 4 · Release history", "Version 3 · Release history", "Version 2 · Release history", "Version 1 · Release history" };
        internal static readonly string[] Changelogs = { Changelog, Release4Changelog, Release3Changelog, Release2Changelog, Release1Changelog };
    }
}
