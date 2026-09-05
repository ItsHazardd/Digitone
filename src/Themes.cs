using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Digitone
{
    internal static class Themes
    {
        internal static readonly string[] Names = { "Midnight", "Overworld", "Spire", "Collective", "Sanctuary", "Olympus", "Pride" };
        internal static string Label(string name) { return name=="Midnight" ? "Persona 3 Reload" : name=="Overworld" ? "Minecraft" : name=="Spire" ? "Slay the Spire 2" : name=="Collective" ? "Communism" : name=="Sanctuary" ? "Tree House Sanctuary" : name=="Olympus" ? "EPIC: Mount Olympus" : name=="Pride" ? "Pride" : name; }
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
            if(theme=="Pride")
            {
                Color night=Color.FromRgb(17,17,39),pink=Color.FromRgb(226,58,132),sky=Color.FromRgb(91,207,250),gold=Color.FromRgb(255,216,67);
                if(color==(Color)ColorConverter.ConvertFromString("#354233"))return Blend(night,pink,.22);
                if(luminance>210)return Color.FromRgb(255,248,255);
                if(luminance>175)return sky;
                if(luminance>120)return gold;
                return Blend(night,pink,Math.Max(.02,(luminance-20)/420));
            }
            if(theme=="Sanctuary")
            {
                Color leaf=Color.FromRgb(137,211,119),forest=Color.FromRgb(16,43,41);
                if(color==(Color)ColorConverter.ConvertFromString("#354233"))return Blend(forest,leaf,.34);
                if(color==(Color)ColorConverter.ConvertFromString("#919A92")||color==(Color)ColorConverter.ConvertFromString("#BDCCB0"))return Color.FromRgb(229,246,220);
                if(luminance>210)return Color.FromRgb(248,255,235);
                if(luminance>175)return Color.FromRgb(202,255,159);
                if(luminance>120)return Color.FromRgb(184,215,190);
                return Blend(forest,leaf,Math.Max(.03,(luminance-20)/360));
            }
            if(theme=="Olympus")
            {
                Color gold=Color.FromRgb(242,198,91),divine=Color.FromRgb(117,189,238),night=Color.FromRgb(17,21,46);
                if(color==(Color)ColorConverter.ConvertFromString("#354233"))return Blend(night,gold,.24);
                if(luminance>210)return Color.FromRgb(248,242,218);
                if(luminance>175)return gold;
                if(luminance>120)return Blend(divine,gold,.22);
                return Blend(night,divine,Math.Max(.02,(luminance-20)/390));
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
                if(light){ double original=.2126*Convert.ToInt32(key.Substring(1,2),16)+.7152*Convert.ToInt32(key.Substring(3,2),16)+.0722*Convert.ToInt32(key.Substring(5,2),16);bool keepAccent=key=="CC1D3A8"||key=="CC3D4AA"||key=="CBACBA5";if(theme=="Sanctuary"&&keepAccent)color=Color.FromRgb(35,94,58);else if(theme=="Sanctuary"&&original>=140)color=original>210?Color.FromRgb(20,39,28):Color.FromRgb(52,76,59);else if(theme=="Pride"&&keepAccent)color=Color.FromRgb(132,39,113);else if(theme=="Pride"&&original>=140)color=original>210?Color.FromRgb(40,24,54):Color.FromRgb(80,47,91);else if(original>=140&&!keepAccent)color=original>210?Color.FromRgb(31,27,23):Blend(Color.FromRgb(80,69,59),lightSeed,.08);else if(original<30)color=Blend(Color.FromRgb(250,242,220),lightSeed,.14);else if(original<55)color=Blend(Color.FromRgb(232,220,193),lightSeed,.42);else if(original<90)color=Blend(Color.FromRgb(211,194,158),lightSeed,.52);else if(original<140)color=Blend(Color.FromRgb(119,105,80),lightSeed,.55);else color=Blend(lightSeed,Color.FromRgb(42,35,28),.18); }
                resources[key] = color; resources["B" + key.Substring(1)] = new SolidColorBrush(color);
            }
            resources["CaptionShadow"]=theme=="Sanctuary"&&!light?new DropShadowEffect{Color=Color.FromRgb(3,18,13),BlurRadius=4,ShadowDepth=1,Opacity=.95}:new DropShadowEffect{BlurRadius=0,ShadowDepth=0,Opacity=0};
            var headerFade=new LinearGradientBrush{StartPoint=new Point(0,.5),EndPoint=new Point(1,.5)};
            var helperFade=new LinearGradientBrush{StartPoint=new Point(0,.5),EndPoint=new Point(1,.5)};
            if(theme=="Sanctuary"&&light)
            {
                headerFade.GradientStops.Add(new GradientStop(Color.FromArgb(205,247,242,211),0));
                headerFade.GradientStops.Add(new GradientStop(Color.FromArgb(125,224,241,204),.72));
                headerFade.GradientStops.Add(new GradientStop(Color.FromArgb(0,224,241,204),1));
                helperFade.GradientStops.Add(new GradientStop(Color.FromArgb(185,247,242,211),0));
                helperFade.GradientStops.Add(new GradientStop(Color.FromArgb(100,224,241,204),.76));
                helperFade.GradientStops.Add(new GradientStop(Color.FromArgb(0,224,241,204),1));
                resources["DownloadPrimaryText"]=new SolidColorBrush(Color.FromRgb(18,58,35));
                resources["DownloadSecondaryText"]=new SolidColorBrush(Color.FromRgb(32,76,47));
            }
            else
            {
                headerFade.GradientStops.Add(new GradientStop(Color.FromArgb(201,10,21,18),0));
                headerFade.GradientStops.Add(new GradientStop(Color.FromArgb(135,10,21,18),.72));
                headerFade.GradientStops.Add(new GradientStop(Color.FromArgb(0,10,21,18),1));
                helperFade.GradientStops.Add(new GradientStop(Color.FromArgb(184,10,21,18),0));
                helperFade.GradientStops.Add(new GradientStop(Color.FromArgb(98,10,21,18),.76));
                helperFade.GradientStops.Add(new GradientStop(Color.FromArgb(0,10,21,18),1));
                resources["DownloadPrimaryText"]=resources["BEEEFE8"];
                resources["DownloadSecondaryText"]=resources["BBDCCB0"];
            }
            resources["DownloadHeaderBackground"]=headerFade;
            resources["DownloadHelperBackground"]=helperFade;
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
            var scene=ThemeScenes.Build(Data.ThemeName,Math.Max(240,h/w*1000),AnimateTheme,Data.AppearanceMode=="Light",true,Data.NeonEnabled);
            themeGeometry.Children.Add(new Viewbox { Width=w,Height=h,Stretch=Stretch.UniformToFill,ClipToBounds=true,Child=scene });
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
            Window.Resources["ThemeDisplayFont"] = new FontFamily(Data.ThemeName=="Overworld" ? "Consolas" : Data.ThemeName=="Spire" ? "Georgia" : Data.ThemeName=="Sanctuary" ? "Segoe Print" : Data.ThemeName=="Olympus" ? "Palatino Linotype" : Data.ThemeName=="Pride" ? "Trebuchet MS" : "Impact");
            var headerBackdrop=Find<Grid>("TopHeaderBackdrop");headerBackdrop.Children.Clear();if(Data.ThemeName=="Pride")headerBackdrop.Children.Add(ThemeScenes.PrideBanner(Data.AppearanceMode=="Light"));
            Find<Canvas>("AmbientCanvas").Opacity=Data.ThemeName=="Custom" ? .42 : .70;
            LayoutAmbient();
            accent = Themes.Brush(Window, "C1D3A8"); dim = Themes.Brush(Window, "354233");
            Find<Button>("ThemeButton").Content = "Settings";
            var queueButton=Find<Button>("QueueToggleButton");var settingsButton=Find<Button>("ThemeButton");if(Data.ThemeName=="Pride"){var prideNavStyle=(Style)Window.FindResource("NavButton");foreach(var button in new[]{queueButton,settingsButton}){button.Style=prideNavStyle;button.Background=Brushes.Transparent;button.FontSize=17;button.Padding=new Thickness(12,10,12,10);button.HorizontalContentAlignment=HorizontalAlignment.Left;}}else{foreach(var button in new[]{queueButton,settingsButton}){button.ClearValue(FrameworkElement.StyleProperty);button.ClearValue(Control.BackgroundProperty);button.ClearValue(Control.FontSizeProperty);button.ClearValue(Control.PaddingProperty);button.ClearValue(Control.HorizontalContentAlignmentProperty);}}
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




