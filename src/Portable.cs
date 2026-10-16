using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Web.Script.Serialization;

namespace Digitone
{
    // One portable Digitone folder:
    //   <root>\Digitone.exe        small launcher
    //   <root>\app\                program files (replaceable by updates and repair)
    //   <root>\userdata\           everything personal (never replaced)
    internal sealed class PortableFolders
    {
        internal readonly string Root, UserData, Music, Data, Deleted, Cache, Lyrics, LibraryFile, YouTubeBrowser, CoverBrowser, UpdateStaging;
        private static PortableFolders current;
        internal PortableFolders(string root)
        {
            Root = Path.GetFullPath(root).TrimEnd('\\', '/');
            UserData = Path.Combine(Root, "userdata");
            Music = Path.Combine(UserData, "Music");
            Data = Path.Combine(UserData, "data");
            Deleted = Path.Combine(UserData, "Deleted");
            Cache = Path.Combine(UserData, "cache");
            Lyrics = Path.Combine(UserData, "lyrics");
            LibraryFile = Path.Combine(Data, "library.json");
            YouTubeBrowser = Path.Combine(Data, "WebView2", "YouTube");
            CoverBrowser = Path.Combine(Data, "CoverBrowser");
            UpdateStaging = Path.Combine(Root, "update-staging");
        }
        internal static PortableFolders Current { get { return current ?? (current = new PortableFolders(DetectRoot(AppDomain.CurrentDomain.BaseDirectory))); } }
        internal static void UseRoot(string root) { current = new PortableFolders(root); }
        internal static string DetectRoot(string programFolder)
        {
            string folder = Path.GetFullPath(programFolder).TrimEnd('\\', '/');
            return String.Equals(Path.GetFileName(folder), "app", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(folder) : folder;
        }
        private static bool Inside(string path, string folder) { return path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase); }

        // Paths inside userdata are saved relative to it, so the whole folder can move.
        internal string ToStored(string path)
        {
            if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return path;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                if (String.Equals(full, UserData, StringComparison.OrdinalIgnoreCase)) return ".";
                return Inside(full, UserData) ? full.Substring(UserData.Length + 1) : path;
            }
            catch { return path; }
        }
        internal string FromStored(string stored)
        {
            if (String.IsNullOrWhiteSpace(stored)) return stored;
            if (Path.IsPathRooted(stored)) return Relocate(stored);
            try
            {
                string full = Path.GetFullPath(Path.Combine(UserData, stored)).TrimEnd('\\', '/');
                return String.Equals(full, UserData, StringComparison.OrdinalIgnoreCase) || Inside(full, UserData) ? full : null;
            }
            catch { return null; }
        }
        // Absolute paths from older versions or from a copy that was moved before paths were relative.
        internal string Relocate(string path)
        {
            string legacyMusic = Path.Combine(Root, "DigiMusic");
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\', '/');
                if (!Directory.Exists(legacyMusic))
                {
                    if (String.Equals(full, legacyMusic, StringComparison.OrdinalIgnoreCase)) return Music;
                    if (Inside(full, legacyMusic)) return Path.Combine(Music, full.Substring(legacyMusic.Length + 1));
                }
                if (File.Exists(full) || Directory.Exists(full)) return path;
                foreach (var marker in new[] { new { Text = "\\userdata\\", Folder = UserData }, new { Text = "\\DigiMusic\\", Folder = Music } })
                {
                    int index = full.LastIndexOf(marker.Text, StringComparison.OrdinalIgnoreCase);
                    if (index < 0) continue;
                    string candidate = Path.Combine(marker.Folder, full.Substring(index + marker.Text.Length));
                    if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
                }
                if (full.EndsWith("\\DigiMusic", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Music)) return Music;
                if (full.EndsWith("\\userdata\\Music", StringComparison.OrdinalIgnoreCase) && Directory.Exists(Music)) return Music;
            }
            catch { }
            return path;
        }

        // Moves a pre-6 layout (Data and DigiMusic beside the exe) into userdata. Returns what moved, or null.
        internal string Migrate()
        {
            string legacyData = Path.Combine(Root, "Data"), legacyMusic = Path.Combine(Root, "DigiMusic"), legacyDeleted = Path.Combine(Root, "Deleted");
            bool moveData = Directory.Exists(legacyData) && !Directory.Exists(Data), moveMusic = Directory.Exists(legacyMusic) && !Directory.Exists(Music);
            if (!moveData && !moveMusic) { Directory.CreateDirectory(UserData); return null; }
            Directory.CreateDirectory(UserData);
            var moved = new List<string>();
            if (moveMusic) { Directory.Move(legacyMusic, Music); moved.Add("DigiMusic → userdata\\Music"); }
            if (moveData)
            {
                try { Directory.Move(legacyData, Data); moved.Add("Data → userdata\\data"); }
                catch
                {
                    if (moveMusic) try { Directory.Move(Music, legacyMusic); } catch { }
                    throw new IOException("Digitone could not move its Data folder into userdata because a file in it is in use. Close any other Digitone window or program using that folder, then open Digitone again. Nothing was lost.");
                }
            }
            if (Directory.Exists(legacyDeleted) && !Directory.Exists(Deleted)) try { Directory.Move(legacyDeleted, Deleted); moved.Add("Deleted → userdata\\Deleted"); } catch { }
            return String.Join(", ", moved);
        }
    }

    // Writes the library with userdata paths stored relative to userdata.
    internal sealed class PortablePathConverter : JavaScriptConverter
    {
        private static readonly Dictionary<Type, string[]> PathProperties = new Dictionary<Type, string[]>
        {
            { typeof(Track), new[] { "Path" } },
            { typeof(Playlist), new[] { "Paths", "FolderPath", "ExcludedPaths" } },
            { typeof(Library), new[] { "DownloadFolder", "MainMusicFolder", "MusicFolders", "RemovedPaths" } }
        };
        internal static IEnumerable<string> PathPropertyNames(Type type) { return PathProperties[type]; }
        private readonly PortableFolders folders;
        internal PortablePathConverter(PortableFolders folders) { this.folders = folders; }
        public override IEnumerable<Type> SupportedTypes { get { return PathProperties.Keys; } }
        public override object Deserialize(IDictionary<string, object> dictionary, Type type, JavaScriptSerializer serializer) { throw new NotSupportedException("Read the library with a plain serializer."); }
        public override IDictionary<string, object> Serialize(object value, JavaScriptSerializer serializer)
        {
            var result = new Dictionary<string, object>();
            string[] paths = PathProperties[value.GetType()];
            foreach (PropertyInfo property in value.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetSetMethod() == null || property.GetIndexParameters().Length > 0 || property.IsDefined(typeof(ScriptIgnoreAttribute), true)) continue;
                object item = property.GetValue(value, null);
                // Lyrics live in userdata\lyrics files; the library only keeps the file name.
                var track = value as Track;
                if (track != null && property.Name == "Lyrics" && !String.IsNullOrEmpty(track.LyricsFile)) continue;
                if (paths.Contains(property.Name))
                {
                    var list = item as List<string>;
                    if (list != null) item = list.Select(folders.ToStored).ToList();
                    else if (item is string) item = folders.ToStored((string)item);
                }
                result[property.Name] = item;
            }
            return result;
        }
        internal static void Resolve(Library library, PortableFolders folders) { MapPaths(library, folders.FromStored); }
        // Applies one path mapping to every path the library stores.
        internal static void MapPaths(Library library, Func<string, string> map)
        {
            foreach (Track track in library.Tracks) track.Path = map(track.Path);
            foreach (Playlist playlist in library.Playlists)
            {
                playlist.Paths = playlist.Paths.Select(map).ToList();
                if (playlist.ExcludedPaths != null) playlist.ExcludedPaths = playlist.ExcludedPaths.Select(map).ToList();
                playlist.FolderPath = map(playlist.FolderPath);
            }
            library.DownloadFolder = map(library.DownloadFolder);
            library.MainMusicFolder = map(library.MainMusicFolder);
            if (library.MusicFolders != null) library.MusicFolders = library.MusicFolders.Select(map).Where(p => p != null).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (library.RemovedPaths != null) library.RemovedPaths = library.RemovedPaths.Select(map).ToList();
        }
    }
}
