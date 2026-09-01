using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Digitone
{
    internal enum PlaylistRemoval { KeepFiles, MoveFiles, DeleteFiles }
    internal sealed class PlaylistRemovalResult
    {
        internal readonly List<string> Removed = new List<string>();
        internal readonly List<string> Errors = new List<string>();
        internal string Folder;
        internal int Missing;
    }
    internal static class PlaylistFiles
    {
        internal static PlaylistRemovalResult Remove(IEnumerable<string> paths, IEnumerable<string> libraryPaths, PlaylistRemoval mode, string deletedRoot)
        {
            var result = new PlaylistRemovalResult();
            if (mode == PlaylistRemoval.KeepFiles) return result;
            var allowed = new HashSet<string>(libraryPaths, StringComparer.OrdinalIgnoreCase);
            string[] selected = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (string path in selected) if (!allowed.Contains(path) || !LocalFiles.IsLocal(path) || !LocalFiles.IsAudio(path)) throw new IOException("One of the playlist paths is not an authorized local audio file. No files were changed.");
            if (mode == PlaylistRemoval.MoveFiles)
            {
                if (!LocalFiles.IsLocal(deletedRoot)) throw new IOException("Choose a local Deleted folder.");
                result.Folder = Path.Combine(deletedRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6));
                Directory.CreateDirectory(result.Folder);
            }
            foreach (string path in selected)
            {
                try
                {
                    if (!LocalFiles.IsLocal(path)) throw new IOException("File became unavailable or linked.");
                    if (!File.Exists(path)) { result.Missing++; result.Removed.Add(path); continue; }
                    if (mode == PlaylistRemoval.DeleteFiles) File.Delete(path);
                    else
                    {
                        string destination = Path.Combine(result.Folder, Path.GetFileName(path)); int suffix = 2;
                        while (File.Exists(destination)) destination = Path.Combine(result.Folder, Path.GetFileNameWithoutExtension(path) + " (" + suffix++ + ")" + Path.GetExtension(path));
                        if (String.Equals(Path.GetPathRoot(path), Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase)) File.Move(path, destination);
                        else
                        {
                            File.Copy(path, destination, false);
                            using (var hash = SHA256.Create())
                            using (var original = File.OpenRead(path))
                            using (var copy = File.OpenRead(destination))
                                if (!hash.ComputeHash(original).SequenceEqual(hash.ComputeHash(copy))) throw new IOException("Copied file did not verify. Original was kept.");
                            File.Delete(path);
                        }
                    }
                    result.Removed.Add(path);
                }
                catch (Exception e) { result.Errors.Add(Path.GetFileName(path) + ": " + e.Message); }
            }
            return result;
        }
    }
    internal sealed class DeletePlaylistDialog : Window
    {
        internal PlaylistRemoval Mode;
        internal string DeletedRoot;
        internal Button Confirm;
        internal RadioButton Keep, Move, Delete;
        internal TextBox Confirmation;
        internal DeletePlaylistDialog(Window owner, string name, int count, int shared)
        {
            Owner = owner; Title = "Delete playlist"; Width = 540; Height = 580; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner; Resources = owner.Resources; Background = owner.Background; Foreground = owner.Foreground; FontFamily = owner.FontFamily;
            DeletedRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Deleted");
            var panel = new StackPanel { Margin = new Thickness(26) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = owner.Background };
            panel.Children.Add(new TextBlock { Text = "Delete “" + name + "”?", FontSize = 24, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBlock { Text = count + " songs · " + shared + " also used in other playlists", Foreground = Brushes.Gray, Margin = new Thickness(0, 10, 0, 18) });
            Keep = Choice("Delete playlist only. Keep all music files", true); Move = Choice("Move songs to a Deleted folder", false); Delete = Choice("Permanently delete songs from disk", false);
            panel.Children.Add(Keep); panel.Children.Add(Move); panel.Children.Add(Delete);
            var folder = new TextBlock { Text = DeletedRoot, TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 10, 0, 8) }; panel.Children.Add(folder);
            var browse = new Button { Content = "Choose Deleted folder…", HorizontalAlignment = HorizontalAlignment.Left }; panel.Children.Add(browse);
            browse.Click += delegate { using (var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose the parent folder for moved songs" }) if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) { DeletedRoot = dialog.SelectedPath; folder.Text = DeletedRoot; Move.IsChecked = true; } };
            panel.Children.Add(new TextBlock { Text = "Moving or deleting files removes them from the library and every playlist that uses them. Backups and unrelated files are left alone. Permanent deletion does not use the Recycle Bin.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 18, 0, 12), FontSize = 12 });
            panel.Children.Add(new TextBlock { Text = "For permanent deletion, type DELETE below:", FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
            Confirmation = new TextBox { MaxLength = 6 }; panel.Children.Add(Confirmation);
            var error = new TextBlock { Foreground = Brushes.IndianRed, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) }; panel.Children.Add(error);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal }; panel.Children.Add(buttons);
            Confirm = new Button { Content = "Continue" }; var cancel = new Button { Content = "Cancel", IsCancel = true }; buttons.Children.Add(Confirm); buttons.Children.Add(cancel);
            Confirm.Click += delegate { Mode = Delete.IsChecked == true ? PlaylistRemoval.DeleteFiles : Move.IsChecked == true ? PlaylistRemoval.MoveFiles : PlaylistRemoval.KeepFiles; if (Mode == PlaylistRemoval.DeleteFiles && Confirmation.Text != "DELETE") { error.Text = "Type DELETE to confirm permanent file deletion."; return; } if (Mode == PlaylistRemoval.MoveFiles && !LocalFiles.IsLocal(DeletedRoot)) { error.Text = "Choose a valid local Deleted folder."; return; } DialogResult = true; };
        }
        private RadioButton Choice(string text, bool selected) { return new RadioButton { Content = text, IsChecked = selected, GroupName = "Removal", Foreground = Foreground, Margin = new Thickness(0, 0, 0, 14), FontSize = 13 }; }
    }
}
