using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;

namespace Digitone
{
    public sealed partial class PlayerApp
    {
        // Background fixes (song names, lyrics, art) and the update check wait on this.
        // Startup holds it while the Welcome or What's new window is open, so choices made there apply before anything runs.
        private TaskCompletionSource<bool> backgroundGate = DoneSource();
        internal Task BackgroundWorkAllowed { get { return backgroundGate.Task; } }
        internal void HoldBackgroundWork() { if (backgroundGate.Task.IsCompleted) backgroundGate = new TaskCompletionSource<bool>(); }
        internal void ReleaseBackgroundWork() { backgroundGate.TrySetResult(true); }

        // Shown once per version to people who updated; new installs get the Welcome window instead.
        internal bool ShouldShowWhatsNew { get { return Data.SetupCompleted && AppInfo.WhatsNew.Length > 0 && Data.LastSeenVersion != AppInfo.Version; } }
        internal void MarkVersionSeen() { if (Data.LastSeenVersion == AppInfo.Version) return; Data.LastSeenVersion = AppInfo.Version; Save(); }

        internal Window CreateWhatsNew()
        {
            var dialog = new Window { Owner = Window, Title = "What's new in Digitone", Width = 540, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Resources = Window.Resources, FontFamily = Window.FontFamily, Icon = Window.Icon, UseLayoutRounding = true, ShowInTaskbar = false };
            dialog.SetResourceReference(Control.BackgroundProperty, "B161917"); dialog.SetResourceReference(Control.ForegroundProperty, "BEEEFE8");
            var frame = new Border { Padding = new Thickness(30, 24, 30, 24) }; frame.SetResourceReference(Border.BackgroundProperty, "B161917"); dialog.Content = frame;
            var body = new StackPanel(); frame.Child = body;
            var updated = SettingsText("Updated to " + AppInfo.Version, 12, true); updated.Margin = new Thickness(0); body.Children.Add(updated);
            var heading = SettingsText("WHAT'S NEW", 28); heading.Margin = new Thickness(0, 2, 0, 16); body.Children.Add(heading);
            foreach (string line in AppInfo.WhatsNew)
            {
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
                var dot = new Ellipse { Width = 7, Height = 7, Fill = accent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 7, 12, 0) }; DockPanel.SetDock(dot, Dock.Left); row.Children.Add(dot);
                var item = SettingsText(line, 14); item.Name = "WhatsNewItem"; item.Margin = new Thickness(0); row.Children.Add(item);
                body.Children.Add(row);
            }
            var divider = new Border { Height = 1, Margin = new Thickness(0, 8, 0, 14) }; divider.SetResourceReference(Border.BackgroundProperty, "B4C574B"); body.Children.Add(divider);
            body.Children.Add(SettingsText("Fix up my library in the background", 16));
            var note = SettingsText("Names and art get saved into your song files. Anything unsure waits for you to check.", 12, true); note.Margin = new Thickness(0, 0, 0, 6); body.Children.Add(note);
            var names = new CheckBox { Name = "WhatsNewNames", Content = "Fix song names", IsChecked = Data.AutoRecognize != false, Margin = new Thickness(0, 4, 0, 4) }; body.Children.Add(names);
            var lyrics = new CheckBox { Name = "WhatsNewLyrics", Content = "Find lyrics", IsChecked = Data.AutoLyrics != false, Margin = new Thickness(0, 4, 0, 4) }; body.Children.Add(lyrics);
            var art = new CheckBox { Name = "WhatsNewArt", Content = "Add missing album art", IsChecked = Data.AutoArt != false, Margin = new Thickness(0, 4, 0, 4) }; body.Children.Add(art);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) }; body.Children.Add(buttons);
            var changelog = new Button { Name = "WhatsNewChangelog", Content = "Full changelog", Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(14, 8, 14, 8) }; buttons.Children.Add(changelog);
            var beta = new Button { Name = "WhatsNewBeta", Content = "Try the Beta look", Margin = new Thickness(0, 0, 10, 0), Padding = new Thickness(14, 8, 14, 8), Visibility = Data.BetaTheme ? Visibility.Collapsed : Visibility.Visible }; buttons.Children.Add(beta);
            var done = new Button { Name = "WhatsNewDone", Content = "Got it", IsDefault = true, IsCancel = true, MinWidth = 88, Padding = new Thickness(14, 8, 14, 8) }; buttons.Children.Add(done);
            bool tryBeta = false;
            changelog.Click += delegate { ShowChangelog(dialog); };
            beta.Click += delegate { tryBeta = true; dialog.Close(); };
            done.Click += delegate { dialog.Close(); };
            dialog.Closed += delegate
            {
                // Closing any way (Got it, Esc, or the X) keeps the switches as they were left.
                Data.AutoRecognize = names.IsChecked == true; Data.AutoLyrics = lyrics.IsChecked == true; Data.AutoArt = art.IsChecked == true;
                Data.LastSeenVersion = AppInfo.Version; Save();
                ReleaseBackgroundWork();
                if (tryBeta) Window.Dispatcher.BeginInvoke(new Action(delegate { SetBeta(true); }));
            };
            NativeChrome.Apply(dialog);
            return dialog;
        }
    }
}
