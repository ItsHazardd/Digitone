using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;

namespace Digitone
{
    public sealed class PortableCheck
    {
        public string Version { get; set; }
        public string Root { get; set; }
        public int Tracks { get; set; }
        public int TracksFound { get; set; }
        public int Playlists { get; set; }
        public bool SettingsLoaded { get; set; }
        public bool PathsInsideCopy { get; set; }
        public bool LibraryExists { get; set; }
        public List<string> Missing { get; set; }
        public string Error { get; set; }
        internal bool PassedFor(ExportResult export) { return String.IsNullOrEmpty(Error) && SettingsLoaded && PathsInsideCopy && Tracks == export.Songs && TracksFound == export.Songs - export.MissingSongs; }
    }
    internal sealed class ExportResult
    {
        internal string Destination;
        internal int Files, Songs, OutsideSongs, MissingSongs;
        internal long Bytes;
        internal PortableCheck Check;
    }

    // Copies launcher + app + userdata into one self-contained folder that runs from anywhere.
    internal static class PortableExport
    {
        private static readonly string[] BrowserCaches = { "Cache", "Code Cache", "GPUCache", "GrShaderCache", "ShaderCache", "DawnCache", "DawnGraphiteCache", "DawnWebGPUCache", "Crashpad" };
        private static readonly string[] SkippedProgramFolders = { "TestResults", "Backups", "Source" };
        private static bool Inside(string path, string folder) { return path.StartsWith(folder.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }
        private static bool Same(string first, string second) { return String.Equals(Path.GetFullPath(first).TrimEnd('\\'), Path.GetFullPath(second).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        internal static string ExeName { get { return Path.GetFileName(Assembly.GetEntryAssembly().Location); } }
        // Release: Digitone.exe launcher starts app\DigitoneApp.exe. Dev: both are Digitone.dev.exe.
        internal static string LauncherName(string exeName) { return String.Equals(exeName, "DigitoneApp.exe", StringComparison.OrdinalIgnoreCase) ? "Digitone.exe" : exeName; }
        internal static readonly string[] RootTools = { "Repair Digitone.cmd", "Repair-Digitone.ps1", "LICENSE.txt" };
        internal static string UniqueFolder(string parent, string name)
        {
            string path = Path.Combine(parent, name); int suffix = 2;
            while (Directory.Exists(path) || File.Exists(path)) path = Path.Combine(parent, name + " (" + suffix++ + ")");
            return path;
        }
        private static bool SkipUserFile(PortableFolders source, string file)
        {
            if (Same(file, source.LibraryFile) || Same(file, source.LibraryFile + ".bak") || Same(file, source.LibraryFile + ".tmp")) return true;
            if (Inside(file, Path.Combine(source.Cache, "temp"))) return true;
            if (Inside(file, Path.Combine(source.Data, "WebView2")) || Inside(file, source.CoverBrowser))
            {
                string[] parts = file.Substring(source.Data.Length).Split('\\');
                if (parts.Any(p => BrowserCaches.Contains(p, StringComparer.OrdinalIgnoreCase))) return true;
            }
            return false;
        }

        internal static ExportResult Export(PortableFolders source, string programFolder, string exeName, string parent, Action<string> status)
        {
            programFolder = Path.GetFullPath(programFolder).TrimEnd('\\');
            if (!LocalFiles.IsLocal(parent) || !Directory.Exists(parent)) throw new IOException("Choose an existing local folder or USB drive for the portable copy.");
            parent = Path.GetFullPath(parent);
            if (Same(parent, source.Root) || Inside(parent, source.Root)) throw new IOException("Choose a folder outside this Digitone folder. A copy cannot be placed inside itself.");
            string launcherName = LauncherName(exeName), launcher = Path.Combine(source.Root, launcherName);
            if (Same(programFolder, source.Root) || !File.Exists(launcher)) throw new IOException("This copy of Digitone is not in the portable layout (launcher plus app folder), so it cannot be exported.");

            status("Reading your library…");
            Library library = File.Exists(source.LibraryFile) ? new LibraryStore(source.LibraryFile, source).Load() : new Library();
            string destination = UniqueFolder(parent, "Digitone Portable " + DateTime.Now.ToString("yyyy-MM-dd HH-mm"));
            var target = new PortableFolders(destination);
            var copies = new List<KeyValuePair<string, string>>();
            copies.Add(new KeyValuePair<string, string>(launcher, Path.Combine(destination, launcherName)));
            foreach (string tool in RootTools) if (File.Exists(Path.Combine(source.Root, tool))) copies.Add(new KeyValuePair<string, string>(Path.Combine(source.Root, tool), Path.Combine(destination, tool)));
            foreach (string file in Directory.EnumerateFiles(programFolder, "*", SearchOption.AllDirectories))
            {
                string relative = file.Substring(programFolder.Length + 1);
                if (SkippedProgramFolders.Contains(relative.Split('\\')[0], StringComparer.OrdinalIgnoreCase) || file.EndsWith(".digitone-new", StringComparison.OrdinalIgnoreCase)) continue;
                copies.Add(new KeyValuePair<string, string>(file, Path.Combine(destination, "app", relative)));
            }
            if (Directory.Exists(source.UserData))
                foreach (string file in Directory.EnumerateFiles(source.UserData, "*", SearchOption.AllDirectories))
                    if (!SkipUserFile(source, file)) copies.Add(new KeyValuePair<string, string>(file, Path.Combine(target.UserData, file.Substring(source.UserData.Length + 1))));

            // Music outside userdata is copied into userdata\Music\Imported - <folder name>.
            var outsideRoots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var folderCandidates = new[] { library.MainMusicFolder, library.DownloadFolder }.Concat(library.MusicFolders ?? new List<string>()).Concat(library.Playlists.Select(p => p.FolderPath))
                .Where(p => !String.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p) && !Same(p, source.UserData) && !Inside(p, source.UserData)).Select(p => Path.GetFullPath(p).TrimEnd('\\')).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length).ToList();
            Func<string, string> outsideRootFor = delegate(string path)
            {
                string owner = folderCandidates.Where(f => Inside(path, f)).OrderBy(f => f.Length).FirstOrDefault() ?? Path.GetDirectoryName(path);
                string existing = outsideRoots.Keys.FirstOrDefault(r => Same(r, owner) || Inside(owner, r));
                if (existing != null) return existing;
                string name = Path.GetFileName(owner.TrimEnd('\\')); if (String.IsNullOrWhiteSpace(name)) name = "Drive " + owner.Substring(0, 1);
                string mapped = Path.Combine(target.Music, "Imported - " + name); int suffix = 2;
                while (outsideRoots.Values.Contains(mapped, StringComparer.OrdinalIgnoreCase) || Directory.Exists(Path.Combine(source.Music, Path.GetFileName(mapped)))) mapped = Path.Combine(target.Music, "Imported - " + name + " (" + suffix++ + ")");
                outsideRoots[owner] = mapped; return owner;
            };
            foreach (string folder in folderCandidates) outsideRootFor(folder + "\\x");
            var result = new ExportResult { Destination = destination };
            foreach (Track track in library.Tracks)
            {
                result.Songs++;
                if (!File.Exists(track.Path)) { result.MissingSongs++; continue; }
                if (Inside(track.Path, source.UserData)) continue;
                string root = outsideRootFor(track.Path);
                copies.Add(new KeyValuePair<string, string>(track.Path, Path.Combine(outsideRoots[root], track.Path.Substring(root.Length + 1))));
                result.OutsideSongs++;
            }
            copies = copies.GroupBy(c => c.Value, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToList();

            long needed = copies.Sum(c => new FileInfo(c.Key).Length), free = new DriveInfo(Path.GetPathRoot(parent)).AvailableFreeSpace;
            if (needed + 64L * 1024 * 1024 > free) throw new IOException("Not enough space: the portable copy needs " + Size(needed) + " but only " + Size(free) + " is free there.");

            Directory.CreateDirectory(destination);
            try
            {
                for (int i = 0; i < copies.Count; i++)
                {
                    string from = copies[i].Key, to = copies[i].Value;
                    Directory.CreateDirectory(Path.GetDirectoryName(to));
                    File.Copy(from, to, false);
                    if (new FileInfo(to).Length != new FileInfo(from).Length) throw new IOException("A copied file did not verify: " + Path.GetFileName(from));
                    result.Files++; result.Bytes += new FileInfo(to).Length;
                    if (i % 25 == 0 || i == copies.Count - 1) status("Copying " + (i + 1) + " of " + copies.Count + " files · " + Size(result.Bytes) + " of " + Size(needed));
                }
                Directory.CreateDirectory(target.Music);
                foreach (string mapped in outsideRoots.Values) Directory.CreateDirectory(mapped);
                status("Writing the portable library…");
                PortablePathConverter.MapPaths(library, delegate(string path)
                {
                    if (String.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path)) return path;
                    string full = Path.GetFullPath(path).TrimEnd('\\');
                    if (Same(full, source.UserData)) return target.UserData;
                    if (Inside(full, source.UserData)) return Path.Combine(target.UserData, full.Substring(source.UserData.Length + 1));
                    string root = outsideRoots.Keys.Where(r => Same(full, r) || Inside(full, r)).OrderByDescending(r => r.Length).FirstOrDefault();
                    return root == null ? path : Same(full, root) ? outsideRoots[root] : Path.Combine(outsideRoots[root], full.Substring(root.Length + 1));
                });
                new LibraryStore(target.LibraryFile, target).Save(library);
                File.WriteAllText(Path.Combine(destination, "README.txt"), Readme(launcherName));
            }
            catch
            {
                try { Directory.Delete(destination, true); } catch { }
                throw;
            }
            return result;
        }
        internal static string Readme(string exeName)
        {
            return "DIGITONE · PORTABLE COPY\r\n\r\n" +
                "Double-click " + exeName + " to start Digitone. Nothing needs to be installed.\r\n\r\n" +
                "You can move or copy this whole folder anywhere: another PC, a USB drive, a different folder.\r\n" +
                "Digitone only reads and writes inside this folder.\r\n\r\n" +
                "  Repair Digitone.cmd   Reinstalls Digitone's program files if something breaks. Your music and settings are kept.\r\n" +
                "  app\\        Digitone's program files and download tools. Safe to replace when repairing or updating.\r\n" +
                "  userdata\\   YOUR stuff: music (userdata\\Music), playlists, settings, lyrics, and caches.\r\n" +
                "              Back this up if you care about it.\r\n\r\n" +
                "Needs Windows 10 or 11 (64-bit). The YouTube viewer and cover search use Microsoft Edge WebView2,\r\n" +
                "which is built into Windows 11 and almost every Windows 10 PC.\r\n" +
                "YouTube sign-ins do not carry over to a different PC or Windows account; sign in again there.\r\n";
        }
        internal static string Explain(Exception error) { return error is PathTooLongException ? "Some of Digitone's files would end up with paths longer than Windows allows there. Choose a folder closer to the top of the drive, like D:\\ or C:\\Digitone." : error.Message; }
        internal static string Size(long bytes) { return bytes >= 1L << 30 ? (bytes / (double)(1L << 30)).ToString("0.0") + " GB" : (bytes / (double)(1L << 20)).ToString("0.0") + " MB"; }

        // Runs inside the copy (--portable-check) and reports whether it finds its own library.
        // Repair uses --repair-check, which first runs any layout migration this version needs.
        internal static int WriteCheck(string reportPath, bool migrate = false)
        {
            var folders = PortableFolders.Current; var check = new PortableCheck { Version = AppInfo.Version, Root = folders.Root, Missing = new List<string>() };
            try
            {
                if (migrate) folders.Migrate();
                check.LibraryExists = File.Exists(folders.LibraryFile);
                Library library = new LibraryStore(folders.LibraryFile, folders).Load();
                check.Tracks = library.Tracks.Count;
                foreach (Track track in library.Tracks) { if (File.Exists(track.Path)) check.TracksFound++; else if (check.Missing.Count < 20) check.Missing.Add(track.Path); }
                check.Playlists = library.Playlists.Count;
                check.SettingsLoaded = File.Exists(folders.LibraryFile) && library.SetupCompleted;
                check.PathsInsideCopy = library.Tracks.Where(t => File.Exists(t.Path)).All(t => Inside(t.Path, folders.UserData));
            }
            catch (Exception error) { check.Error = error.Message; }
            File.WriteAllText(reportPath, new JavaScriptSerializer().Serialize(check));
            return String.IsNullOrEmpty(check.Error) ? 0 : 1;
        }
        // Digitone.exe --export-portable "<folder>" ["<report.txt>"]: export without opening the player.
        internal static int RunFromCommandLine(string parent, string reportPath)
        {
            string summary; bool passed = false;
            try
            {
                var folders = PortableFolders.Current;
                var result = Export(folders, AppDomain.CurrentDomain.BaseDirectory, ExeName, parent, delegate { });
                Directory.CreateDirectory(Path.Combine(folders.Cache, "temp"));
                result.Check = RunCheck(result.Destination, ExeName, Path.Combine(folders.Cache, "temp", "portable-check-" + Guid.NewGuid().ToString("N") + ".json"));
                passed = result.Check.PassedFor(result);
                summary = (passed ? "Portable copy ready and tested: " : "Portable copy made, but its self-check found a problem: ") + result.Destination + "\r\n" +
                    result.Files + " files, " + Size(result.Bytes) + ", " + result.Check.TracksFound + " of " + result.Check.Tracks + " songs found, " + result.Check.Playlists + " playlists, settings " + (result.Check.SettingsLoaded ? "loaded" : "missing") + ".\r\n" +
                    result.OutsideSongs + " outside songs copied in, " + result.MissingSongs + " songs already missing." + (String.IsNullOrEmpty(result.Check.Error) ? "" : "\r\n" + result.Check.Error);
            }
            catch (Exception error) { summary = "Export stopped. Nothing was left half-copied.\r\n" + Explain(error); }
            if (!String.IsNullOrWhiteSpace(reportPath)) try { File.WriteAllText(reportPath, summary); } catch { return 2; }
            else MessageBox.Show(summary, "Digitone portable copy", MessageBoxButton.OK, passed ? MessageBoxImage.Information : MessageBoxImage.Warning);
            return passed ? 0 : 1;
        }
        internal static PortableCheck RunCheck(string portableRoot, string exeName, string reportPath)
        {
            string exe = Path.Combine(portableRoot, "app", exeName);
            if (File.Exists(reportPath)) File.Delete(reportPath);
            using (var process = Process.Start(new ProcessStartInfo(exe, "--portable-check " + AudioDownloader.Quote(reportPath)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe) }))
                if (!process.WaitForExit(60000)) { try { process.Kill(); } catch { } return new PortableCheck { Error = "The portable copy did not answer within a minute." }; }
            if (!File.Exists(reportPath)) return new PortableCheck { Error = "The portable copy could not start." };
            try { return new JavaScriptSerializer().Deserialize<PortableCheck>(File.ReadAllText(reportPath)); }
            finally { try { File.Delete(reportPath); } catch { } }
        }
    }

    public sealed partial class PlayerApp
    {
        private bool exporting;
        private StackPanel PortableSettingsPanel()
        {
            var panel = new StackPanel { Name = "PortableSettingsSection" };
            panel.Children.Add(SettingsText("Portable copy", 16));
            panel.Children.Add(SettingsText("Copies Digitone, its tools, and all your music, playlists, settings, and lyrics into one folder that runs from anywhere: another PC or a USB drive, with nothing to install. Songs from folders outside Digitone are copied into userdata\\Music.", 12, true));
            var export = new Button { Name = "ExportPortableButton", Content = "Export portable copy…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
            var open = new Button { Content = "Open folder", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
            var status = SettingsText("", 11, true); status.Margin = new Thickness(0, 8, 0, 0);
            panel.Children.Add(export); panel.Children.Add(status); panel.Children.Add(open);
            string finished = null;
            open.Click += delegate { if (finished != null && Directory.Exists(finished)) Process.Start(new ProcessStartInfo(finished) { UseShellExecute = true }); };
            export.Click += async delegate
            {
                if (exporting) return;
                if (importing || Downloads.Busy) { status.Text = "Finish the current import or download first."; return; }
                string parent;
                using (var picker = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose where to put the portable Digitone folder (for example a USB drive).", ShowNewFolderButton = true })
                {
                    if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                    parent = picker.SelectedPath;
                }
                if (!Save()) { status.Text = "Your library could not be saved, so nothing was exported."; return; }
                exporting = true; export.IsEnabled = false; open.Visibility = Visibility.Collapsed;
                var folders = PortableFolders.Current; string program = AppDomain.CurrentDomain.BaseDirectory, exe = PortableExport.ExeName;
                Action<string> report = delegate(string text) { Window.Dispatcher.BeginInvoke(new Action(delegate { status.Text = text; })); };
                try
                {
                    var result = await Task.Run(delegate
                    {
                        var exported = PortableExport.Export(folders, program, exe, parent, report);
                        report("Testing the portable copy…");
                        Directory.CreateDirectory(Path.Combine(folders.Cache, "temp"));
                        exported.Check = PortableExport.RunCheck(exported.Destination, exe, Path.Combine(folders.Cache, "temp", "portable-check-" + Guid.NewGuid().ToString("N") + ".json"));
                        return exported;
                    });
                    finished = result.Destination; open.Visibility = Visibility.Visible;
                    var check = result.Check;
                    status.Text = (check.PassedFor(result) ? "Portable copy ready and tested: " : "Portable copy made, but its self-check found a problem: ") + result.Destination + "\n" +
                        result.Files + " files · " + PortableExport.Size(result.Bytes) + " · " + check.TracksFound + " of " + check.Tracks + " songs found · " + check.Playlists + " playlists · settings " + (check.SettingsLoaded ? "loaded" : "missing") +
                        (result.OutsideSongs > 0 ? "\n" + result.OutsideSongs + " songs from outside folders were copied into userdata\\Music." : "") +
                        (result.MissingSongs > 0 ? "\n" + result.MissingSongs + " songs were already missing from this PC and could not be copied." : "") +
                        (String.IsNullOrEmpty(check.Error) ? "" : "\n" + check.Error);
                }
                catch (Exception error) { status.Text = "Export stopped. Nothing was left half-copied.\n" + PortableExport.Explain(error); }
                finally { exporting = false; export.IsEnabled = true; }
            };
            return panel;
        }
    }
}
