namespace Digitone
{
    internal static class AppInfo
    {

        internal const string Version = "5.4.14";
        // Shown once in the What's new window after updating to this version. Keep it to about five short lines.
        internal static readonly string[] WhatsNew = {
            "Portable: move the whole Digitone folder anywhere. Repair Digitone fixes it if it ever breaks.",
            "Songs get identified, with karaoke lyrics and album art.",
            "Beta look: real 3D and rave-stage visualizers.",
            "Full refresh rate, smoother pages, and less clutter.",
            "Age-restricted YouTube songs download when you sign in."
        };
        // Digitone's AcoustID application key (acoustid.org/new-application). Empty until registered; users can also set their own in Settings.
        internal const string AcoustIdKey = "es4hOG501t";
        internal const string Changelog =
@"VERSION 5 · CURRENT RELEASE

PORTABLE FOLDER
• Digitone now keeps program files in an app folder and everything personal in a userdata folder.
• Music, playlists, settings, and lyrics are saved with paths relative to userdata, so the whole Digitone folder can be moved to another PC or a USB drive.
• Existing Data and DigiMusic folders move into userdata automatically the first time this version opens.
• Update downloads are staged inside the Digitone folder instead of the Windows profile.
• Added Export portable copy in Settings: one folder with Digitone, its tools, and all your music, playlists, settings, and lyrics, tested automatically after copying.
• Songs from folders outside Digitone are copied into the portable copy's userdata\Music.
• Download tools now keep their caches inside userdata instead of the Windows profile.

REPAIR DIGITONE
• Added Repair Digitone beside Digitone.exe: double-click it to reinstall the program files from GitHub (latest or a chosen version) or from a downloaded ZIP, even when Digitone will not open.
• Repair backs up your settings, playlists, library, and lyrics first, never writes to your music, tests the new version with your data, and puts the previous version back if anything fails.
• Added Repair / reinstall Digitone to Settings, and updates now install through the same safe process.

SONG RECOGNITION
• Downloaded songs are identified from the audio itself (AcoustID fingerprint + MusicBrainz), so wrong YouTube titles are fixed automatically.
• When the fingerprint is unknown, Digitone checks YouTube's title, channel, and YouTube Music details against MusicBrainz instead.
• Every match shows a confidence level; only matches of 85% or higher are applied without asking, and the rest wait in Review songs.
• Review songs lets you pick another match, search MusicBrainz, edit the details, or keep what you have.
• Confirmed titles, artists, albums, and MusicBrainz IDs are written into the file's tags and your library, and results are cached for offline use.
• Added Identify song to the song menu and Identify my whole library to Settings.

LYRICS
• Lyrics now come from several sources in order: a .lrc file beside the song, lyrics inside the song file, LRCLIB (time-synced), then lyrics.ovh.
• Results must match the song's title, artist, and length; versions with a different length are rejected.
• Only confident matches are saved automatically. Anything less certain waits in the lyrics window for you to accept, pick another result, edit, or reject.
• Identified songs look up lyrics with their corrected title and artist.
• Lyrics are saved as .lrc (time-synced) or .txt files in userdata\lyrics, and search results are cached so they work offline.

KARAOKE AND LYRICS REVIEW
• Time-synced lyrics now play karaoke-style: the sung line is highlighted and kept centred, word by word when the lyrics include word timing.
• Click any line to jump the song there.
• Added Lyrics review: see every song's lyrics status, accept or reject results, swap to another result, edit the text, or remove lyrics.
• Added a timing editor: tap Space as each line starts to time plain lyrics, nudge lines by 0.1 s, or shift every line at once.
• Open it from the lyrics panel, the song menu, or Settings.
• Lyrics are now automatic by default: Digitone fills in missing lyrics for your whole library in the background, and new downloads get theirs right after they are identified.
• Likely matches (75–84%) are added automatically and listed under ""Added automatically – please check"" in Lyrics review; only uncertain ones wait for you.
• Added Find lyrics for my whole library now to Settings.
• Instrumental-only results no longer replace a sung song's lyrics, and edits like ""Slowed"" find the song's own lyrics.

BETA THEME
• Added the Beta Theme (Settings > Appearance): a bouncier, livelier interface that works with every color theme. Turning it off restores the original look exactly.
• Slanted tabs with a sliding highlight bar, sweeping diagonal screen changes, and Bahnschrift headings.
• Songs cascade in, rows lean toward the pointer, and a slanted cursor and sheen slide to the selected song.
• Buttons squash and spring when pressed; new song titles snap in with a skew; the record and Play button pulse with the music.
• A living background drawn entirely by code: light shafts, soft glows, drifting shards, and music-reactive waves. Midnight adds a glowing moon and a giant slanted watermark.
• Windows and dialogs fly in with their cards cascading.
• Optional quiet menu sounds made by code, softer while music plays.
• Respects the Windows setting to turn off animations.
• Beta now has real 3D: lit glass crystals drift in depth behind the app, the camera follows your mouse for parallax, and a light follows your cursor.
• The Now Playing art becomes a floating 3D tile that tilts toward the mouse, catches the light, pulses with the music, and spins when the song changes.
• Switching tabs sinks the old page back into the 3D space while the new page eases forward; playlist cards tilt with a sliding glare, and panels float on soft shadows.
• Added 3D depth quality: Flat, Balanced, or Ultra. PCs without 3D graphics stay flat automatically.
• Fixed the record showing up next to the 3D album tile: in Beta 3D only the tile shows, otherwise only the record.
• Each theme now has its own 3D world in Beta: pixel blocks (Minecraft), floating cards and embers (Slay the Spire), red and gold stars (Communism), leaves and fireflies (Tree House Sanctuary), marble columns and shards (Olympus), rainbow prisms (Pride), and glass crystals (Persona 3 Reload and Custom).
• The expanded visualizers have 3D Beta versions: layered waveform ribbons, spectrum bars on a mirror floor, a radial ring around a glossy core, glass prisms, and a pulsing core with shock rings. Outside Beta they are unchanged.

CLEANER AND SMOOTHER
• Page changes now glide: the new page rises gently and fades in as one piece with a soft landing, locked to your screen's refresh rate. No snapshot hitch, no bounce, no busy row-by-row cascade.
• Beta: the selected song no longer has its own left bar under the slanted cursor, and it stays put instead of leaning toward the mouse.
• Much less text everywhere. Filler labels are gone, the status line only shows results and problems and fades on its own, and Settings explanations moved into ⓘ tooltips (hover a heading or switch to read them).

RAVE VISUALIZERS (BETA 3D)
• The expanded visualizers are now rave stages: Laser Show, LED Wall, Halo, Moving Heads, and Drop. Outside Beta 3D they keep their old names and look.
• Everything moves on the music's beat: Digitone finds the kicks and the tempo, lights change pattern every bar, colors change every 8 bars, and the camera stays steady.
• The bass is clear on every stage: kicks punch the floor light and the lasers, LED wall, rings, beams, or speaker, and long bass notes swell the haze.
• Builds and drops: when the bass drops out and comes back hard, the stage hits with shockwaves and a confetti burst.
• Flashing lights stay gentle (at most about 3 flashes a second) and can be turned off in Settings > Appearance.
• The normal-look visualizers are calmer too: bars and rings glide instead of jittering, and the bass bars, rings, and circles punch on every kick.

MINECRAFT THEME
• The Minecraft theme now uses detailed 64x block textures: real grass blocks along the ground and textured floating blocks (grass, diamond ore, jukebox).
• In Beta 3D, the floating blocks show the right texture on every face (grass on top and dirt underneath, log rings on the ends, glowing glowstone), with spinning music discs among them.
• In Beta 3D, songs without album art show a music disc on the floating tile.
• Music discs float as round discs (no grey square around them), Slay the Spire cards have rounded gold edges, and 3D textures no longer show thin lines along their edges.

YOUTUBE
• Age-restricted songs now download: when YouTube asks for a signed-in account, Digitone retries with the sign-in from its own YouTube viewer (use an 18+ account). The sign-in is used for that one download from a temporary file inside userdata, then deleted; other browsers are never touched.
• If you are not signed in, Digitone says so plainly and offers a Sign in to YouTube button, then Retry.
• Pasting an album or playlist link (for example a YouTube Music album) as a single song now asks whether to download the whole album.

SPEED AND SMOOTHNESS
• Digitone now animates at your screen's full refresh rate (for example 165 fps on a 165 Hz screen). Beta motion used to update only about 21 times a second because of coarse Windows timers; it now updates every frame, at the same speeds as before.
• The seek bar and the background orbit ring glide every frame instead of stepping.
• Removed a hidden cost: an invisible caption shadow sat on most labels and was redrawn every frame. Only Sanctuary dark has a visible caption shadow, and there still captions are drawn once and reused.
• Beta: the 3D album tile, search box, and player bar shadows are drawn once and reused instead of re-blurred every frame. The background, glows, and 3D textures were also made cheaper, with the same look.
• The 3D visualizers do their maths once instead of every frame, and the live waveform's per-frame work only runs while a song plays.
• Theme scenery is drawn once and reused instead of redrawn every frame: Pride's flag banner, Sanctuary's glowing string lights and caption shadows, and every drifting layer. Pride and Sanctuary went from about 45 fps to well over 90 in Beta on a 165 Hz screen.
• While another window (a game, say) is in front, Beta's background runs at 30 fps to leave power for it; the open visualizer keeps full speed.
• Added an automatic 3D depth guard: if Beta keeps stuttering below about 45 fps, Digitone steps 3D depth down (Ultra → Balanced → Flat) and tells you. It can be turned off in Settings > Appearance.

AUTOMATIC ALBUM ART
• Songs without art now get it automatically in the background: a cover picture in the song's folder first, then the official cover for the exact album recognition found.
• Art you set or remove yourself always wins; automatic art only fills songs that have none and never replaces a cover.
• Automatic art is saved into the song file and your library, and waits until a playing song stops before writing to it.
• Added Find album art automatically to the song menu, plus an on/off switch and a whole-library button in Settings.
• Songs already in your library are now identified automatically in the background too (85%+ matches only), so they get correct names, lyrics, and album art. Details you edited yourself are never overwritten.

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
• After an update, a short What's new window shows the highlights once. It also lets you choose whether Digitone fixes song names, finds lyrics, and adds missing album art in the background; nothing runs until you close it.
• Removed feature spoilers and release notes from automatic update prompts.
• Approved updates continue to preserve personal data while replacing program files.

APPEARANCE
• Improved Tree House Sanctuary text contrast in light and dark modes.
• Added softly animated lo-fi string lights with stronger glow in Neon mode.";

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
