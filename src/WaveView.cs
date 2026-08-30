using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Digitone
{
    public sealed partial class PlayerApp
    {
        private readonly Polyline smallTrace = new Polyline { StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        private readonly Polyline expandedTrace = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false };
        private readonly Polyline expandedGlow = new Polyline { StrokeThickness = 7, Opacity = .35, StrokeLineJoin = PenLineJoin.Round, IsHitTestVisible = false, Effect = new BlurEffect { Radius = 8 } };
        private LiveWaveSource liveWave;
        private readonly AudioScene audioScene = new AudioScene();
        private readonly AudioScene mainScene = new AudioScene { Mode="Spectrum" };
        internal static readonly string[] VisualizerModes = { "Waveform", "Spectrum", "Radial", "Prism", "Pulse" };
        internal string ActiveVisualizer { get { return Data.VisualizerMode ?? "Waveform"; } }
        internal int SceneRenderCount { get { return audioScene.RenderCount; } }
        private void SetMainVisualizer(string mode)
        {
            Data.MainVisualizerMode=mode=="Spectrum"?"Spectrum":mode=="Off"?"Off":"Waveform";
            Find<Canvas>("Waveform").Visibility=Data.MainVisualizerMode=="Waveform"?Visibility.Visible:Visibility.Collapsed;
            Find<Canvas>("MainSpectrum").Visibility=Data.MainVisualizerMode=="Spectrum"?Visibility.Visible:Visibility.Collapsed;
            Find<Button>("VisualizerButton").BorderBrush=Data.MainVisualizerMode=="Waveform"?accent:Brushes.Transparent;
            Find<Button>("MainSpectrumButton").BorderBrush=Data.MainVisualizerMode=="Spectrum"?accent:Brushes.Transparent;
            Find<Button>("MainVisualizerOffButton").BorderBrush=Data.MainVisualizerMode=="Off"?accent:Brushes.Transparent;
            Find<TextBlock>("WaveLabel").Visibility=Data.MainVisualizerMode=="Off"?Visibility.Collapsed:Visibility.Visible;
            Find<Canvas>("MainSpectrum").Height=Data.MainVisualizerMode=="Spectrum"?72:64;Find<Viewbox>("DefaultArt").MaxHeight=Double.PositiveInfinity;Find<Border>("RecordArt").MinHeight=Data.MainVisualizerMode=="Spectrum"?190:90;
            ApplyNeon();Save(); DrawLiveWave(lastWave);
        }
        internal void SetVisualizer(string mode)
        {
            if(mode == "Ribbon") mode = "Prism";
            Data.VisualizerMode = Array.IndexOf(VisualizerModes,mode)>=0 ? mode : "Waveform";
            audioScene.Mode=Data.VisualizerMode; audioScene.SetFrame(null);
            foreach(string name in VisualizerModes) Find<Button>("Mode"+name).BorderBrush = name==Data.VisualizerMode ? accent : Brushes.Transparent;
            ApplyNeon();LayoutExpanded();
        }
        private Effect NeonGlow(){if(!Data.NeonEnabled)return null;var color=(accent as SolidColorBrush)==null?Colors.Cyan:((SolidColorBrush)accent).Color;var glow=new DropShadowEffect{Color=color,BlurRadius=14,ShadowDepth=0,Opacity=.82,RenderingBias=RenderingBias.Performance};if(glow.CanFreeze)glow.Freeze();return glow;}
        internal void ApplyNeon()
        {
            Effect glow=NeonGlow();smallTrace.Effect=glow;Find<Canvas>("MainSpectrum").Effect=glow;Find<Grid>("RecordDisc").Effect=glow;Find<Slider>("Seek").Effect=glow;Find<Slider>("Volume").Effect=glow;Find<Button>("PlayButton").Effect=glow;
            foreach(string name in new[]{"AmbientRing","AmbientOrbit","AmbientBand","AmbientDots","AmbientShard","AmbientSteps"})Find<FrameworkElement>(name).Effect=glow;if(themeGeometry!=null)themeGeometry.Effect=glow;
            foreach(string name in new[]{"LibraryButton","FavoritesButton","DownloadsButton","PlaylistsButton","VisualizerButton","MainSpectrumButton","MainVisualizerOffButton"}){var button=Find<Button>(name);var brush=button.BorderBrush as SolidColorBrush;button.Effect=Data.NeonEnabled&&brush!=null&&brush.Color.A>0?glow:null;}
        }
        private double[] lastWave;
        private bool fullscreen;
        private WindowState previousState;
        private WindowStyle previousStyle;
        private ResizeMode previousResize;
        private TimeSpan lastWaveRender;
        private double waveRenderBudget;
        internal int WaveRenderCount { get; private set; }
        private void SetupWaveRendering()
        {
            EventHandler render = delegate(object sender, EventArgs args)
            {
                var frame = args as RenderingEventArgs;
                if (frame == null || frame.RenderingTime == lastWaveRender) return;
                double elapsed = Math.Max(0,(frame.RenderingTime-lastWaveRender).TotalSeconds);
                lastWaveRender = frame.RenderingTime;
                const double interval = 1.0/90;
                waveRenderBudget = Math.Min(interval*2,waveRenderBudget+elapsed);
                if(waveRenderBudget+0.000001 < interval) return;
                waveRenderBudget %= interval;
                if (Window.WindowState == WindowState.Minimized || !playing || !ready) return;
                if (!Find<Canvas>("Waveform").IsVisible && !Find<Canvas>("MainSpectrum").IsVisible && !Find<Grid>("ExpandedView").IsVisible) return;
                UpdateLiveWave(); WaveRenderCount++;
            };
            Window.Loaded += delegate { CompositionTarget.Rendering += render; };
            Window.Closed += delegate { CompositionTarget.Rendering -= render; };
        }
        private void SetupExpandedWaveform()
        {
            foreach(string mode in VisualizerModes) { string chosen=mode; Click("Mode"+mode,delegate { SetVisualizer(chosen); Save(); }); }
            SetVisualizer(Data.VisualizerMode);
            Click("ExpandWaveformButton", delegate { ShowExpanded(true); });
            Click("CloseExpandedButton", delegate { if (fullscreen) ToggleFullscreen(); ShowExpanded(false); SelectCollection(null, false); });
            Click("FullscreenButton", ToggleFullscreen);
            Find<Canvas>("ExpandedWaveform").SizeChanged += delegate { LayoutExpanded(); };
            Find<Canvas>("MainSpectrum").SizeChanged += delegate { var c=Find<Canvas>("MainSpectrum"); mainScene.Width=c.ActualWidth; mainScene.Height=c.ActualHeight; };
            Window.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.F11) { ToggleFullscreen(); e.Handled = true; } else if (e.Key == Key.Escape && Find<Grid>("ExpandedView").Visibility == Visibility.Visible) { if (fullscreen) ToggleFullscreen(); else ShowExpanded(false); e.Handled = true; } };
            ResetLiveWave();
            var main=Find<Canvas>("MainSpectrum"); main.Children.Add(mainScene); SetMainVisualizer(Data.MainVisualizerMode);
        }
        internal void ShowExpanded(bool show) { Find<Grid>("ExpandedView").Visibility = show ? Visibility.Visible : Visibility.Collapsed; SetAmbientVisibility(); UpdateRecordSpin(); if (show) { UpdateExpandedTitles(); LayoutExpanded(); Find<Grid>("ExpandedView").Focus(); } else DrawLiveWave(lastWave); }
        internal void ToggleFullscreen()
        {
            if (!fullscreen) { previousState = Window.WindowState; previousStyle = Window.WindowStyle; previousResize = Window.ResizeMode; fullscreen = true; ShowExpanded(true); Window.WindowState = WindowState.Normal; Window.WindowStyle = WindowStyle.None; Window.ResizeMode = ResizeMode.NoResize; Window.WindowState = WindowState.Maximized; }
            else { fullscreen = false; Window.WindowState = WindowState.Normal; Window.WindowStyle = previousStyle; Window.ResizeMode = previousResize; Window.WindowState = previousState; }
            Find<Button>("FullscreenButton").Content = fullscreen ? "Exit fullscreen" : "⛶ Fullscreen"; Find<Grid>("ExpandedView").Focus();
        }
        private void UpdateExpandedTitles() { Text("ExpandedTitle", current == null ? "Something worth listening to." : current.Title); Text("ExpandedDetail", current == null ? "Choose a song, then settle in." : current.Detail); var cover=SongTags.ImageFromData(current==null?null:current.CoverData); Find<Image>("ExpandedArt").Source=cover; Find<TextBlock>("ExpandedArtFallback").Visibility=cover==null?Visibility.Visible:Visibility.Collapsed; }
        private void ResetLiveWave()
        {
            if (liveWave != null) liveWave.Dispose(); liveWave = null; lastWave = null;
            var small = Find<Canvas>("Waveform"); small.Children.Clear(); small.Children.Add(smallTrace);
            LayoutExpanded(); DrawLiveWave(null);
            Text("ExpandedHint", current == null ? "Play a song to see its live waveform" : "Preparing live waveform…");
        }
        private void RefreshLiveTheme()
        {
            smallTrace.Stroke = accent; expandedTrace.Stroke = accent; expandedGlow.Stroke = accent;
            audioScene.Accent=accent;
            mainScene.Accent=accent;
            foreach(string mode in VisualizerModes) Find<Button>("Mode"+mode).BorderBrush=mode==ActiveVisualizer?accent:Brushes.Transparent;
            LayoutExpanded(); DrawLiveWave(lastWave);
        }
        private void LayoutExpanded()
        {
            var canvas = Find<Canvas>("ExpandedWaveform"); canvas.Children.Clear();
            double width = canvas.ActualWidth, height = canvas.ActualHeight;
            if(ActiveVisualizer!="Waveform") { audioScene.Width=Math.Max(0,width); audioScene.Height=Math.Max(0,height); canvas.Children.Add(audioScene); audioScene.SetFrame(lastWave); return; }
            for (int i = 0; i <= 12; i++) canvas.Children.Add(new Line { X1 = width * i / 12, X2 = width * i / 12, Y1 = 0, Y2 = height, Stroke = dim, Opacity = .45, StrokeThickness = .7, IsHitTestVisible = false });
            for (int i = 0; i <= 6; i++) canvas.Children.Add(new Line { X1 = 0, X2 = width, Y1 = height * i / 6, Y2 = height * i / 6, Stroke = dim, Opacity = i == 3 ? .75 : .45, StrokeThickness = .7, IsHitTestVisible = false });
            canvas.Children.Add(expandedGlow); canvas.Children.Add(expandedTrace); DrawLiveWave(lastWave);
        }
        private void UpdateLiveWave()
        {
            if (!playing || !ready) { lastWave = null; DrawLiveWave(null); Text("WaveLabel", "LIVE WAVE"); Text("ExpandedHint", "Play a song to see its live waveform"); return; }
            if (liveWave == null) return;
            double[] frame = liveWave.Sample(media.Position.TotalSeconds);
            if (frame == null) { DrawLiveWave(null); string message = liveWave.Failed ? "Live waveform unavailable for this file" : "Preparing live waveform…"; Text("WaveLabel", message); Text("ExpandedHint", message); return; }
            lastWave = frame; DrawLiveWave(frame);
            Text("WaveLabel", "LIVE WAVE · follows the current audio");
            Text("ExpandedHint", "LIVE AUDIO  ·  Space to play / pause  ·  F11 fullscreen  ·  Esc to return");
        }
        private static PointCollection TracePoints(double[] wave, double width, double height)
        {
            var points = new PointCollection(); int count = wave == null ? 2 : wave.Length;
            for (int i = 0; i < count; i++) points.Add(new Point(width * i / (count - 1), height * .5 - (wave == null ? 0 : wave[i]) * height * .43));
            points.Freeze(); return points;
        }
        private void DrawLiveWave(double[] wave)
        {
            var small = Find<Canvas>("Waveform");
            if (Find<Grid>("ExpandedView").Visibility != Visibility.Visible && (small.IsVisible || wave == null)) smallTrace.Points = TracePoints(wave, small.ActualWidth, small.ActualHeight);
            if(Find<Canvas>("MainSpectrum").IsVisible){ var c=Find<Canvas>("MainSpectrum"); mainScene.Width=c.ActualWidth; mainScene.Height=c.ActualHeight; mainScene.SetFrame(wave); }
            var large = Find<Canvas>("ExpandedWaveform");
            if(ActiveVisualizer!="Waveform") { if(large.IsVisible || wave==null) audioScene.SetFrame(wave, wave != null && liveWave != null && (ActiveVisualizer=="Pulse" || ActiveVisualizer=="Prism") ? liveWave.SampleBands(media.Position.TotalSeconds) : null); return; }
            if (large.IsVisible || wave == null) { var points = TracePoints(wave, large.ActualWidth, large.ActualHeight); expandedTrace.Points = points; expandedGlow.Points = points; }
        }
    }
}
