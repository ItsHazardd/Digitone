using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Digitone
{
    internal static class Themes
    {
        internal static readonly string[] Names = { "Midnight", "Overworld", "Spire", "Collective" };
        internal static string Label(string name) { return name=="Midnight" ? "Persona 3 Reload" : name=="Overworld" ? "Minecraft" : name=="Spire" ? "Slay the Spire 2" : name=="Collective" ? "Communism" : name; }
        internal static bool TryColor(string text, out Color color)
        {
            color = Colors.White;
            string hex = (text ?? "").Trim().TrimStart('#');
            if (hex.Length == 3) hex = String.Concat(hex.Select(c => new string(c, 2)));
            if (hex.Length != 6 || hex.Any(c => !Uri.IsHexDigit(c))) return false;
            color = (Color)ColorConverter.ConvertFromString("#" + hex); return true;
        }
        private static Color Blend(Color a, Color b, double amount)
        {
            return Color.FromRgb((byte)(a.R + (b.R-a.R)*amount), (byte)(a.G + (b.G-a.G)*amount), (byte)(a.B + (b.B-a.B)*amount));
        }
        internal static Color Map(Color color, string theme, string custom = null)
        {
            double luminance = .2126 * color.R + .7152 * color.G + .0722 * color.B;
            if (theme == "Custom")
            {
                Color seed; if (!TryColor(custom, out seed)) seed = Color.FromRgb(134,244,194);
                if (luminance > 210) return Blend(Colors.White, seed, .06);
                if (luminance > 175) { while (.2126*seed.R + .7152*seed.G + .0722*seed.B < 180) seed = Blend(seed, Colors.White, .15); return seed; }
                if (luminance > 120) return Blend(Color.FromRgb(180,180,180),seed,.15);
                if (color == (Color)ColorConverter.ConvertFromString("#354233")) return Blend(Color.FromRgb(20,20,20),seed,.42);
                byte dark = (byte)(luminance*.52); return Blend(Color.FromRgb(dark,dark,dark),seed,.16);
            }
            if (theme == "Midnight")
            {
                if (color == (Color)ColorConverter.ConvertFromString("#354233")) return Color.FromRgb(12, 65, 170);
                if (luminance > 210) return Color.FromRgb(240, 249, 255);
                if (luminance > 175) return Color.FromRgb(73, 224, 255);
                if (luminance > 120) return Color.FromRgb(163, 188, 215);
                byte shade = (byte)Math.Round(luminance * .5);
                return Color.FromRgb((byte)(shade / 2), (byte)(shade + 8), (byte)(shade * 2 + 24));
            }
            Color paletteAccent = theme=="Overworld" ? Color.FromRgb(155,214,103) : theme=="Spire" ? Color.FromRgb(226,190,126) : Color.FromRgb(255,112,91);
            Color baseColor = theme=="Overworld" ? Color.FromRgb(24,30,22) : theme=="Spire" ? Color.FromRgb(26,20,35) : Color.FromRgb(35,15,18);
            if(color == (Color)ColorConverter.ConvertFromString("#354233")) return theme=="Collective" ? Color.FromRgb(144,34,37) : Blend(baseColor,paletteAccent,.25);
            if(luminance>210) return Color.FromRgb(250,240,217);
            if(luminance>175) return paletteAccent;
            if(luminance>120) return Blend(Color.FromRgb(168,168,168),paletteAccent,.25);
            return Blend(baseColor,paletteAccent,Math.Max(0,(luminance-25)/400));
        }
        internal static void Apply(ResourceDictionary resources, string theme, string custom = null, bool light = false)
        {
            Color lightSeed=Map((Color)ColorConverter.ConvertFromString("#C1D3A8"),theme,custom);
            foreach (string key in resources.Keys.OfType<string>().Where(k => k.Length == 7 && k[0] == 'C').ToArray())
            {
                Color color = Map((Color)ColorConverter.ConvertFromString("#" + key.Substring(1)), theme, custom);
                if(light){ double original=.2126*Convert.ToInt32(key.Substring(1,2),16)+.7152*Convert.ToInt32(key.Substring(3,2),16)+.0722*Convert.ToInt32(key.Substring(5,2),16);bool keepAccent=key=="CC1D3A8"||key=="CC3D4AA"||key=="CBACBA5";if(original>=140&&!keepAccent)color=original>210?Color.FromRgb(31,27,23):Blend(Color.FromRgb(80,69,59),lightSeed,.08);else if(original<30)color=Blend(Color.FromRgb(250,242,220),lightSeed,.14);else if(original<55)color=Blend(Color.FromRgb(232,220,193),lightSeed,.42);else if(original<90)color=Blend(Color.FromRgb(211,194,158),lightSeed,.52);else if(original<140)color=Blend(Color.FromRgb(119,105,80),lightSeed,.55);else color=Blend(lightSeed,Color.FromRgb(42,35,28),.18); }
                resources[key] = color; resources["B" + key.Substring(1)] = new SolidColorBrush(color);
            }
        }
        internal static Brush Brush(Window window, string hex) { return (Brush)window.FindResource("B" + hex); }
    }
    public sealed partial class PlayerApp
    {
        private Canvas themeGeometry;
        private void LayoutThemeGeometry(Canvas canvas,double w,double h)
        {
            if(themeGeometry==null) { themeGeometry=new Canvas { IsHitTestVisible=false }; canvas.Children.Add(themeGeometry); }
            foreach(var clock in themeClocks) clock.Controller.Remove(); themeClocks.Clear();
            themeGeometry.Children.Clear();
            bool custom=Data.ThemeName=="Custom";
            foreach(string name in new[]{"AmbientRing","AmbientOrbit","AmbientBand","AmbientDots","AmbientShard","AmbientSteps"})
                if(!custom && (name=="AmbientRing" || name=="AmbientOrbit")){ Find<FrameworkElement>(name).Visibility=Visibility.Visible; Find<FrameworkElement>(name).Opacity=.01; }
                else if(!custom) Find<FrameworkElement>(name).Visibility=Visibility.Collapsed;
                else if(name!="AmbientShard" && name!="AmbientSteps"){ Find<FrameworkElement>(name).Visibility=Visibility.Visible; Find<FrameworkElement>(name).Opacity=name=="AmbientOrbit"?.45:1; }
            if(custom || w<=0 || h<=0) return;
            var scene=ThemeScenes.Build(Data.ThemeName,Math.Max(600,h/w*1000),AnimateTheme);
            themeGeometry.Children.Add(new Viewbox { Width=w,Height=h,Stretch=Stretch.Fill,Child=scene });
            SetAmbientVisibility();
        }        private void SetupThemes()
        {
            ApplyTheme(Data.ThemeName);
        }
        internal void ApplyTheme(string name)
        {
            name = name=="Green" ? "Overworld" : name=="Gold" ? "Spire" : name=="Monochrome" ? "Collective" : name;
            Data.ThemeName = Themes.Names.Contains(name) || name == "Custom" ? name : "Midnight";
            Themes.Apply(Window.Resources, Data.ThemeName, Data.CustomThemeColor, Data.AppearanceMode=="Light");
            Window.Resources["ThemeDisplayFont"] = new FontFamily(Data.ThemeName=="Overworld" ? "Consolas" : Data.ThemeName=="Spire" ? "Georgia" : "Impact");
            Find<Canvas>("AmbientCanvas").Opacity=Data.ThemeName=="Custom" ? .42 : .70;
            LayoutAmbient();
            accent = Themes.Brush(Window, "C1D3A8"); dim = Themes.Brush(Window, "354233");
            Find<Button>("ThemeButton").Content = "Settings";
            bool downloads = Find<Grid>("DownloadPane").Visibility == Visibility.Visible;
            Find<Button>("LibraryButton").Background = !downloads && !playlistsView && selectedPlaylist == null && !favorites ? dim : Brushes.Transparent;
            Find<Button>("FavoritesButton").Background = !downloads && favorites ? dim : Brushes.Transparent;
            Find<Button>("DownloadsButton").Background = downloads ? dim : Brushes.Transparent;
            Find<Button>("PlaylistsButton").Background = !downloads && playlistsView ? dim : Brushes.Transparent;
            UpdateNavigationOutline();
            Find<Button>("ShuffleButton").Foreground = shuffle ? accent : Themes.Brush(Window, "919A92");
            Find<Button>("RepeatButton").Foreground = repeat != 0 ? accent : Themes.Brush(Window, "919A92");
            RefreshLiveTheme();
            RefreshPlaylistGrid();
            ApplyNeon();
            NativeChrome.Apply(Window);
            foreach (Window child in Window.OwnedWindows) NativeChrome.Apply(child);
        }
        private void UpdateNavigationOutline()
        {
            bool downloads = Find<Grid>("DownloadPane").Visibility == Visibility.Visible;
            Find<Button>("LibraryButton").BorderBrush = !downloads && !playlistsView && selectedPlaylist == null && !favorites ? accent : Brushes.Transparent;
            Find<Button>("FavoritesButton").BorderBrush = !downloads && favorites ? accent : Brushes.Transparent;
            Find<Button>("DownloadsButton").BorderBrush = downloads ? accent : Brushes.Transparent;
            Find<Button>("PlaylistsButton").BorderBrush = !downloads && playlistsView ? accent : Brushes.Transparent;
            ApplyNeon();
        }
    }
}




