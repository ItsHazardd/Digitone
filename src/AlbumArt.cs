using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Digitone
{
    internal sealed class ArtFound { internal byte[] Image; internal string Source; }

    // Finds album art automatically, most reliable first:
    // a picture in the song's folder → Cover Art Archive for the exact album recognition found → an album search (identified songs only).
    internal static class AlbumArt
    {
        internal static Func<Uri, CancellationToken, byte[]> Fetch = ArtworkFinder.Request;
        private static bool IsId(string value) { Guid id; return !String.IsNullOrWhiteSpace(value) && Guid.TryParse(value, out id); }
        internal static ArtFound Find(Track track, bool identified, CancellationToken token)
        {
            var local = ArtworkFinder.Local(track.Path);
            if (local != null) return new ArtFound { Image = local.Image, Source = "picture in the song's folder" };
            foreach (var pair in new[] { new[] { "release", track.MusicBrainzReleaseId }, new[] { "release-group", track.MusicBrainzReleaseGroupId } })
            {
                if (!IsId(pair[1])) continue;
                try { return new ArtFound { Image = ArtworkFinder.Normalize(Fetch(new Uri("https://coverartarchive.org/" + pair[0] + "/" + pair[1] + "/front-500"), token)), Source = "Cover Art Archive (exact album)" }; }
                catch (OperationCanceledException) { throw; }
                catch (WebException error) { var response = error.Response as HttpWebResponse; if (response == null || response.StatusCode != HttpStatusCode.NotFound) throw; response.Close(); }
            }
            if (identified && !String.IsNullOrWhiteSpace(track.Album) && !String.IsNullOrWhiteSpace(track.Artist))
            {
                var match = ArtworkFinder.Online(track.Artist, track.Album, "", token, Fetch);
                if (match != null) return new ArtFound { Image = match.Image, Source = "Cover Art Archive (album search)" };
            }
            return null;
        }
    }

    public sealed partial class PlayerApp
    {
        private readonly HashSet<Track> pendingArtWrites = new HashSet<Track>();
        // Art you chose (or removed) is never touched; songs that already have art are left alone.
        internal static bool UserOwnsArt(Track track) { return track.ArtworkSource == "User" || track.ArtworkSource == "UserRemoved"; }
        private readonly HashSet<Track> requestedArt = new HashSet<Track>();
        internal bool NeedsArt(Track track, bool retry)
        {
            bool asked = requestedArt.Contains(track);
            if ((Data.AutoArt == false && !asked) || !String.IsNullOrEmpty(track.CoverData) || UserOwnsArt(track) || !File.Exists(track.Path)) return false;
            DateTime checkedAt; return retry || asked || String.IsNullOrEmpty(track.ArtworkCheckedUtc) || !DateTime.TryParse(track.ArtworkCheckedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out checkedAt) || DateTime.UtcNow - checkedAt > TimeSpan.FromDays(7);
        }
        // Looks up art for one song and applies it, unless the user has set or removed art in the meantime.
        internal async Task<string> FillArt(Track track, CancellationToken token)
        {
            bool identified = IdentityConfirmed(track);
            var found = await Task.Run(delegate { return AlbumArt.Find(track, identified, token); });
            track.ArtworkCheckedUtc = DateTime.UtcNow.ToString("o");
            if (found == null || !String.IsNullOrEmpty(track.CoverData) || UserOwnsArt(track) || !Data.Tracks.Contains(track)) { Save(); return null; }
            track.CoverData = Convert.ToBase64String(found.Image); track.ArtworkSource = "Auto · " + found.Source;
            Save(); if (track == current) UpdateCover();
            if (SongTags.CanEdit(track.Path))
            {
                if (track == current) { pendingArtWrites.Add(track); if (tagWriteTimer != null) tagWriteTimer.Start(); }
                else await EmbedArt(track);
            }
            return found.Source;
        }
        // Saves automatic art into the song file too, so it travels with the music.
        private async Task EmbedArt(Track track)
        {
            if (UserOwnsArt(track) || String.IsNullOrEmpty(track.CoverData) || !track.ArtworkSource.StartsWith("Auto")) { pendingArtWrites.Remove(track); return; }
            string path = track.Path; byte[] picture = Convert.FromBase64String(track.CoverData);
            try { await Task.Run(delegate { SongTags.SaveArtwork(path, picture); }); }
            catch (Exception error) { Status("Album art was saved in your library, but not into the file: " + error.Message); }
            pendingArtWrites.Remove(track);
        }
        private void FindArtForSelected()
        {
            var chosen = Selected().Where(t => String.IsNullOrEmpty(t.CoverData)).ToList();
            if (chosen.Count == 0) { Status("The selected songs already have album art."); return; }
            foreach (var track in chosen) { if (track.ArtworkSource == "UserRemoved") track.ArtworkSource = null; requestedArt.Add(track); }
            QueueLyricsLookup(chosen); Status("Looking for album art for " + chosen.Count + " song" + (chosen.Count == 1 ? "" : "s") + "…");
        }
    }
}
