using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace Digitone
{
    public sealed partial class PlayerApp
    {
        internal void RememberMusicFolders(IEnumerable<string> paths)
        {
            if (Data.MusicFolders == null) Data.MusicFolders = new List<string>();
            foreach (string path in paths.Where(p => LocalFiles.IsLocal(p) && Directory.Exists(p)))
            {
                string full = Path.GetFullPath(path);
                if (!Data.MusicFolders.Contains(full, StringComparer.OrdinalIgnoreCase)) Data.MusicFolders.Add(full);
            }
        }
        private void SetupSettings()
        {
            if (Data.MusicFolders == null) Data.MusicFolders = new List<string>();
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Digitone.ico"))
                Window.Icon = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames.Last();
            Click("ThemeButton", delegate { CreateSettings().ShowDialog(); });
        }
        private void UpdateRecordSpin()
        {
            var rotation = (RotateTransform)Find<Grid>("RecordDisc").RenderTransform;
            double angle = rotation.Angle;
            rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            rotation.Angle = angle;
            if (playing && Find<Grid>("ExpandedView").Visibility != Visibility.Visible) rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(angle, angle + 360, TimeSpan.FromSeconds(12)) { RepeatBehavior = RepeatBehavior.Forever });
        }
    }
}
