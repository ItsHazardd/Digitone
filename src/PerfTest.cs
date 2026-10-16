using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Digitone
{
    // Dev-only speed test: plays a music-like test file and measures CPU, graphics card load, and frame pacing
    // for the normal look, every Beta quality level, the expanded visualizers, and every theme. Usage: --perf-test <folder> [seconds]
    internal static class PerfTest
    {
        private sealed class Row { internal string Name; internal double Cpu, Gpu, Fps, Slow, BetaMs, Low, Ui, Render, BetaFps; }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern int GetCurrentThreadId();

        private static void Music(string path, int seconds)
        {
            const int rate = 44100; int samples = rate * seconds;
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((ushort)1); writer.Write((ushort)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((ushort)2); writer.Write((ushort)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
                var random = new Random(5); double noise = 0;
                for (int i = 0; i < samples; i++)
                {
                    double t = i / (double)rate, beat = t * 2 % 1, kick = Math.Exp(-beat * 9) * Math.Sin(2 * Math.PI * (55 + 60 * Math.Exp(-beat * 20)) * t);
                    double chord = (Math.Sin(2 * Math.PI * 220 * t) + Math.Sin(2 * Math.PI * 277 * t) * .7 + Math.Sin(2 * Math.PI * 330 * t) * .6) * (.25 + .2 * Math.Sin(t * .7));
                    noise = noise * .6 + (random.NextDouble() - .5) * .4; double hat = (t * 4 % 1) < .05 ? noise : 0;
                    writer.Write((short)(Math.Max(-1, Math.Min(1, kick * .5 + chord * .25 + hat * .5)) * 30000));
                }
            }
        }

        private static IEnumerable<UIElement> AllElements(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); var e = child as UIElement; if (e != null) yield return e; foreach (var n in AllElements(child)) yield return n; }
        }
        // Puts effects back exactly as they were: ones that came from a style are cleared rather than pinned in place.
        private static void Restore(Dictionary<UIElement, object> saved) { foreach (var p in saved) { if (p.Value == DependencyProperty.UnsetValue || p.Value is Expression) p.Key.ClearValue(UIElement.EffectProperty); else p.Key.SetValue(UIElement.EffectProperty, p.Value); } }
        // Real pixels of Digitone's own (top-most) test window, to check that cached layers stay sharp. Never captures anything else.
        private static void Shoot(PlayerApp player, string path)
        {
            player.Window.Activate(); player.Window.UpdateLayout();
            var m = PresentationSource.FromVisual(player.Window).CompositionTarget.TransformToDevice;
            var content = (FrameworkElement)player.Window.Content; var topLeft = content.PointToScreen(new Point(0, 0));
            int w = (int)(content.ActualWidth * m.M11), h = (int)(content.ActualHeight * m.M22);
            using (var bmp = new System.Drawing.Bitmap(w, h)) using (var g = System.Drawing.Graphics.FromImage(bmp)) { g.CopyFromScreen((int)topLeft.X, (int)topLeft.Y, 0, 0, new System.Drawing.Size(w, h)); bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        }
        private static object Field(PlayerApp player, string name) { return typeof(PlayerApp).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(player); }
        private static void Click(PlayerApp player, string name) { player.Find<Button>(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
        private static double Percentile(List<double> values, double p) { if (values.Count == 0) return 0; var sorted = values.OrderBy(v => v).ToList(); return sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * p))]; }

        private static List<PerformanceCounter> GpuCounters()
        {
            var list = new List<PerformanceCounter>();
            try
            {
                string id = "pid_" + Process.GetCurrentProcess().Id + "_";
                var category = new PerformanceCounterCategory("GPU Engine");
                foreach (string instance in category.GetInstanceNames()) if (instance.Contains(id) && instance.Contains("engtype_3D")) list.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, true));
            }
            catch (Exception) { }
            return list;
        }

        private static async Task<Row> Measure(PlayerApp player, string name, double seconds)
        {
            await Task.Delay(1500); // let the scene settle
            var frames = new List<double>(); TimeSpan last = TimeSpan.Zero;
            EventHandler tick = delegate(object sender, EventArgs e)
            {
                var args = e as RenderingEventArgs; if (args == null || args.RenderingTime == last) return;
                if (last != TimeSpan.Zero) frames.Add((args.RenderingTime - last).TotalMilliseconds); last = args.RenderingTime;
            };
            var gpu = GpuCounters(); foreach (var counter in gpu) try { counter.NextValue(); } catch (Exception) { }
            var process = Process.GetCurrentProcess(); process.Refresh(); var cpuStart = process.TotalProcessorTime; var clock = Stopwatch.StartNew();
            double betaStart = player.BetaCostMs; int betaTicksStart = player.BetaTickCount;
            int uiId = GetCurrentThreadId(); var threadStart = new Dictionary<int, TimeSpan>(); foreach (ProcessThread t in process.Threads) try { threadStart[t.Id] = t.TotalProcessorTime; } catch (Exception) { }
            CompositionTarget.Rendering += tick;
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            CompositionTarget.Rendering -= tick;
            process.Refresh(); double wall = clock.Elapsed.TotalSeconds;
            double ui = 0; var others = new List<double>(); foreach (ProcessThread t in process.Threads) try { TimeSpan before; double used = (t.TotalProcessorTime - (threadStart.TryGetValue(t.Id, out before) ? before : TimeSpan.Zero)).TotalSeconds / wall * 100; if (t.Id == uiId) ui = used; else others.Add(used); } catch (Exception) { }
            double gpuLoad = 0; foreach (var counter in gpu) try { gpuLoad += counter.NextValue(); } catch (Exception) { }
            int betaTicks = player.BetaTickCount - betaTicksStart;
            return new Row
            {
                Name = name, Cpu = (process.TotalProcessorTime - cpuStart).TotalSeconds / wall * 100, Gpu = gpuLoad,
                Fps = frames.Count == 0 ? 0 : 1000 / frames.Average(), Slow = Percentile(frames, .95),
                BetaMs = betaTicks == 0 ? 0 : (player.BetaCostMs - betaStart) / betaTicks, Low = frames.Count == 0 ? 0 : 1000 / Percentile(frames, .99), Ui = ui, Render = others.Count == 0 ? 0 : others.Max(), BetaFps = betaTicks / wall
            };
        }

        public static int Run(string output, double seconds)
        {
            string cap = Environment.GetEnvironmentVariable("DIGITONE_FPS_CAP"); if (!String.IsNullOrEmpty(cap)) System.Windows.Media.Animation.Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(System.Windows.Media.Animation.Timeline), new FrameworkPropertyMetadata { DefaultValue = Int32.Parse(cap) });
            Directory.CreateDirectory(output);
            string run = Path.Combine(output, "perf-" + DateTime.Now.ToString("yyyyMMdd-HHmmss")); Directory.CreateDirectory(run);
            PortableFolders.UseRoot(run);
            string music = Path.Combine(run, "Music"); Directory.CreateDirectory(music);
            string song = Path.Combine(music, "Perf test groove.wav"); Music(song, 240);
            var app = new Application(); int exitCode = 1; var rows = new List<Row>(); string failure = null;
            var store = new LibraryStore(Path.Combine(run, "Data", "library.json"));
            var library = store.Load(); library.SetupCompleted = true; library.GlobalMediaKeys = false; library.AutoLyrics = false; library.AutoRecognize = false; library.AutoLyricsDefaultApplied = true; library.AutoArt = false; library.AutoCheckUpdates = false; library.ThemeName = "Midnight"; library.BetaAutoQualityOff = true; PlayerApp.FocusOverride = true;
            var player = new PlayerApp(store, library);
            player.Window.Width = 1500; player.Window.Height = 900; player.Window.WindowStartupLocation = WindowStartupLocation.Manual; player.Window.Left = 40; player.Window.Top = 40;
            player.Window.Topmost = true; // other windows covering it make Windows deprioritize its drawing and skew the numbers
            player.Window.Loaded += async delegate
            {
                try
                {
                    await player.Import(new[] { song }); player.Find<Button>("MuteButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    player.Find<ListBox>("Tracks").SelectedIndex = 0; player.Find<Button>("PlayButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    for (int i = 0; i < 100 && !player.MediaReady; i++) await Task.Delay(100); await Task.Delay(4000); // let the waveform analysis finish
                    if (!player.IsPlaying) throw new Exception("The test song did not start playing.");
                    player.SetBeta(false); player.ShowExpanded(false);
                    if (Environment.GetEnvironmentVariable("DIGITONE_PERF_PROBE") == "1")
                    {
                        // Finds where the time goes by switching single parts off (dev investigation only).
                        rows.Add(await Measure(player, "Probe · baseline", seconds));
                        player.TogglePlay(); rows.Add(await Measure(player, "Probe · paused", seconds)); player.TogglePlay();
                        Click(player, "MainVisualizerOffButton"); rows.Add(await Measure(player, "Probe · main visualizer off", seconds)); Click(player, "VisualizerButton");
                        player.Find<Canvas>("AmbientCanvas").Visibility = Visibility.Collapsed; rows.Add(await Measure(player, "Probe · ambient hidden", seconds)); player.Find<Canvas>("AmbientCanvas").Visibility = Visibility.Visible;
                        player.Find<Grid>("RecordDisc").Visibility = Visibility.Collapsed; rows.Add(await Measure(player, "Probe · record hidden", seconds)); player.Find<Grid>("RecordDisc").Visibility = Visibility.Visible;
                        player.Find<FrameworkElement>("NowPane").Visibility = Visibility.Collapsed; rows.Add(await Measure(player, "Probe · now-playing pane hidden", seconds)); player.Find<FrameworkElement>("NowPane").Visibility = Visibility.Visible;
                        player.Find<ListBox>("Tracks").Visibility = Visibility.Collapsed; rows.Add(await Measure(player, "Probe · song list hidden", seconds)); player.Find<ListBox>("Tracks").Visibility = Visibility.Visible;
                        player.ApplyTheme("Collective"); rows.Add(await Measure(player, "Probe · Collective theme", seconds)); player.ApplyTheme("Midnight");
                        exitCode = 0; return;
                    }
                    if (Environment.GetEnvironmentVariable("DIGITONE_PERF_PROBE") == "shots")
                    {
                        foreach (bool beta in new[] { false, true }) { player.SetBeta(beta); if (beta) player.SetBetaQuality(null); foreach (string theme in new[] { "Pride", "Sanctuary", "Olympus" }) { player.ApplyTheme(theme); await Task.Delay(1500); Shoot(player, Path.Combine(output, "real-" + (beta ? "beta-" : "") + theme + ".png")); } }
                        player.ApplyTheme("Midnight"); exitCode = 0; return;
                    }
                    if (Environment.GetEnvironmentVariable("DIGITONE_PERF_PROBE") == "expanded")
                    {
                        Func<string> state = () => "active=" + player.BetaActive + " ticks=" + player.BetaTickCount + " playing=" + player.IsPlaying + " ready=" + player.MediaReady + " pos=" + player.Position.ToString("0.0") + " vis=" + player.BetaVisualizerMode;
                        rows.Add(new Row { Name = "start: " + state() });
                        player.SetBeta(true); rows.Add(new Row { Name = "beta on: " + state() }); player.SetBetaQuality(null); rows.Add(new Row { Name = "quality: " + state() }); await Task.Delay(1500);
                        rows.Add(new Row { Name = "before: " + state() });
                        rows.Add(await Measure(player, "Beta main", seconds));
                        player.SetVisualizer("Spectrum"); player.ShowExpanded(true); await Task.Delay(800);
                        rows.Add(new Row { Name = "opened: " + state() });
                        var vis3d = ((Visualizer3D)Field(player, "betaVisualizer")).View; vis3d.Visibility = Visibility.Hidden; rows.Add(await Measure(player, "expanded, 3D visualizer hidden", seconds)); vis3d.Visibility = Visibility.Visible;
                        rows.Add(await Measure(player, "Beta expanded Spectrum", seconds));
                        rows.Add(new Row { Name = "after: " + state() });
                        foreach (string mode in PlayerApp.VisualizerModes) { player.SetVisualizer(mode); rows.Add(await Measure(player, "Beta expanded " + mode, seconds)); }
                        exitCode = 0; return;
                    }
                    if (Environment.GetEnvironmentVariable("DIGITONE_PERF_PROBE") == "layers")
                    {
                        player.SetBeta(true); player.SetBetaQuality(null); await Task.Delay(500);
                        var ambient = player.Find<Canvas>("AmbientCanvas");
                        Func<string, Action, Action, Task> probe = async (label, off, on) => { off(); await Task.Delay(300); rows.Add(await Measure(player, label, seconds)); on(); };
                        foreach (string theme in new[] { "Pride", "Sanctuary" })
                        {
                            player.ApplyTheme(theme); await Task.Delay(800);
                            rows.Add(await Measure(player, theme + " · baseline", seconds));
                            var header = player.Find<Grid>("TopHeaderBackdrop"); await probe(theme + " · header art hidden", () => header.Visibility = Visibility.Hidden, () => header.Visibility = Visibility.Visible);
                            var wave = player.Find<Canvas>("Waveform"); await probe(theme + " · main waveform hidden", () => wave.Visibility = Visibility.Hidden, () => wave.Visibility = Visibility.Visible);
                            var scene = AllElements(ambient).OfType<Canvas>().Where(c => c != ambient && c.Width == 1000).FirstOrDefault();
                            if (scene == null) continue;
                            int index = 0;
                            foreach (var layer in scene.Children.OfType<UIElement>().ToList())
                            {
                                var fe = layer as FrameworkElement; string label = theme + " · layer " + index++ + " " + layer.GetType().Name + (fe != null && !String.IsNullOrEmpty(fe.Name) ? " " + fe.Name : "") + (layer.Effect != null ? " +glow" : "");
                                if (layer is System.Windows.Shapes.Rectangle) continue;
                                await probe(label, () => layer.Visibility = Visibility.Hidden, () => layer.Visibility = Visibility.Visible);
                            }
                        }
                        exitCode = 0; return;
                    }
                    if (Environment.GetEnvironmentVariable("DIGITONE_PERF_PROBE") == "themes")
                    {
                        // Which part of each theme keeps it under 90 fps (dev investigation only).
                        player.SetBeta(true); player.SetBetaQuality(null); await Task.Delay(500);
                        var ambient = player.Find<Canvas>("AmbientCanvas"); var backdrop = (Panel)Field(player, "betaBackdrop"); var world = ((CrystalWorld)Field(player, "betaWorld")).View;
                        Func<string, Action, Action, Task> probe = async (label, off, on) => { off(); await Task.Delay(300); rows.Add(await Measure(player, label, seconds)); on(); };
                        foreach (string theme in new[] { "Midnight", "Pride", "Sanctuary", "Olympus" })
                        {
                            player.ApplyTheme(theme); await Task.Delay(800);
                            rows.Add(await Measure(player, theme + " · baseline", seconds));
                            await probe(theme + " · theme scenery hidden", () => ambient.Visibility = Visibility.Hidden, () => ambient.Visibility = Visibility.Visible);
                            var sceneFx = AllElements(ambient).Where(e => e.Effect != null).ToList(); var savedFx = sceneFx.ToDictionary(e => e, e => e.ReadLocalValue(UIElement.EffectProperty));
                            await probe(theme + " · scenery glows off (" + sceneFx.Count + ")", () => { foreach (var e in sceneFx) e.Effect = null; }, () => Restore(savedFx));
                            await probe(theme + " · Beta backdrop hidden", () => backdrop.Visibility = Visibility.Hidden, () => backdrop.Visibility = Visibility.Visible);
                            var labelFx = AllElements(player.Window).OfType<TextBlock>().Where(t => t.Effect != null).ToList(); var savedLabels = labelFx.ToDictionary(e => (UIElement)e, e => e.ReadLocalValue(UIElement.EffectProperty));
                            if (labelFx.Count > 0) await probe(theme + " · caption shadows off (" + labelFx.Count + ")", () => { foreach (var e in labelFx) e.Effect = null; }, () => Restore(savedLabels));
                            if (theme == "Midnight") { var mark = backdrop.Children.OfType<TextBlock>().FirstOrDefault(); if (mark != null) await probe("Midnight · watermark hidden", () => mark.Visibility = Visibility.Hidden, () => mark.Visibility = Visibility.Visible); }
                        }
                        player.ApplyTheme("Midnight"); player.SetVisualizer("Spectrum"); player.ShowExpanded(true); await Task.Delay(800);
                        rows.Add(await Measure(player, "Expanded · baseline", seconds));
                        await probe("Expanded · world + backdrop hidden", () => { world.Visibility = Visibility.Hidden; backdrop.Visibility = Visibility.Hidden; }, () => { world.Visibility = Visibility.Visible; backdrop.Visibility = Visibility.Visible; });
                        await probe("Expanded · ambient hidden", () => ambient.Visibility = Visibility.Hidden, () => ambient.Visibility = Visibility.Visible);
                        var glows = AllElements(player.Window).Where(e => e.Effect != null && e.IsVisible).ToList(); var savedGlows = glows.ToDictionary(e => e, e => e.ReadLocalValue(UIElement.EffectProperty));
                        await probe("Expanded · all visible effects off (" + glows.Count + ")", () => { foreach (var e in glows) e.Effect = null; }, () => Restore(savedGlows));
                        var bar = player.Find<Slider>("Seek"); await probe("Expanded · seek bar hidden", () => bar.Visibility = Visibility.Hidden, () => bar.Visibility = Visibility.Visible);
                        exitCode = 0; return;
                    }
                    if (Environment.GetEnvironmentVariable("DIGITONE_PERF_PROBE") == "beta")
                    {
                        player.SetBeta(true); player.SetBetaQuality("Flat");
                        rows.Add(await Measure(player, "Beta probe · Flat baseline", seconds));
                        var backdrop = (UIElement)Field(player, "betaBackdrop"); backdrop.Visibility = Visibility.Collapsed; rows.Add(await Measure(player, "Beta probe · Flat, backdrop hidden", seconds)); backdrop.Visibility = Visibility.Visible;
                        Func<Func<UIElement, bool>, string, Task> without = async (pick, label) => { var hidden = ((Panel)backdrop).Children.OfType<UIElement>().Where(pick).ToList(); foreach (var e in hidden) e.Visibility = Visibility.Hidden; rows.Add(await Measure(player, "Beta probe · Flat, no " + label, seconds)); foreach (var e in hidden) e.Visibility = Visibility.Visible; };
                        await without(e => e is System.Windows.Shapes.Polyline, "waves");
                        await without(e => e is System.Windows.Shapes.Polygon, "shards");
                        await without(e => e is System.Windows.Shapes.Rectangle || e is System.Windows.Shapes.Ellipse, "shafts+glows");
                        var cachedParts = ((Panel)backdrop).Children.OfType<UIElement>().Where(c => c.CacheMode != null).ToList(); foreach (var part in cachedParts) part.CacheMode = null;
                        rows.Add(await Measure(player, "Beta probe · Flat, scene NOT cached", seconds)); foreach (var part in cachedParts) part.CacheMode = new BitmapCache { SnapsToDevicePixels = false };
                        rows.Add(await Measure(player, "Beta probe · Flat, scene cached again", seconds));
                        var effects = ((System.Collections.Generic.Dictionary<UIElement, System.Windows.Media.Effects.Effect>)Field(player, "betaEffects")).Keys.ToList(); var saved = effects.ToDictionary(e => e, e => e.Effect);
                        foreach (var element in effects) element.Effect = null; rows.Add(await Measure(player, "Beta probe · Flat, no shadows/glows", seconds)); foreach (var pair in saved) pair.Key.Effect = pair.Value;
                        player.SetBetaQuality(null);
                        rows.Add(await Measure(player, "Beta probe · Balanced baseline", seconds));
                        var world = ((CrystalWorld)Field(player, "betaWorld")).View; world.Visibility = Visibility.Hidden; rows.Add(await Measure(player, "Beta probe · Balanced, world hidden", seconds)); world.Visibility = Visibility.Visible;
                        var tile = (UIElement)Field(player, "betaTile"); var tileEffect = tile.Effect; tile.Effect = null; rows.Add(await Measure(player, "Beta probe · Balanced, tile shadow off", seconds)); tile.Effect = tileEffect;
                        tile.Visibility = Visibility.Hidden; rows.Add(await Measure(player, "Beta probe · Balanced, tile hidden", seconds)); tile.Visibility = Visibility.Visible;
                        backdrop.Visibility = Visibility.Collapsed; rows.Add(await Measure(player, "Beta probe · Balanced, backdrop hidden", seconds)); backdrop.Visibility = Visibility.Visible;
                        exitCode = 0; return;
                    }
                    rows.Add(await Measure(player, "Normal look · main page", seconds));
                    player.TogglePlay(); rows.Add(await Measure(player, "Normal look · paused", seconds)); player.TogglePlay();
                    player.SetVisualizer("Waveform"); player.ShowExpanded(true); rows.Add(await Measure(player, "Normal look · expanded Waveform", seconds));
                    player.SetVisualizer("Spectrum"); rows.Add(await Measure(player, "Normal look · expanded Spectrum", seconds)); player.ShowExpanded(false);
                    player.SetBeta(true);
                    foreach (string quality in new[] { "Flat", null, "Ultra" })
                    {
                        player.SetBetaQuality(quality);
                        rows.Add(await Measure(player, "Beta " + (quality ?? "Balanced") + " · main page", seconds));
                    }
                    player.SetBetaQuality(null);
                    foreach (string mode in PlayerApp.VisualizerModes)
                    {
                        player.SetVisualizer(mode); player.ShowExpanded(true);
                        rows.Add(await Measure(player, "Beta Balanced · expanded " + mode, seconds));
                    }
                    player.ShowExpanded(false);
                    foreach (string theme in Themes.Names.Where(t => t != "Midnight"))
                    {
                        player.ApplyTheme(theme);
                        rows.Add(await Measure(player, "Beta Balanced · " + theme + " theme", seconds));
                    }
                    player.Data.AppearanceMode = "Light"; player.ApplyTheme("Sanctuary"); rows.Add(await Measure(player, "Beta Balanced · Sanctuary light", seconds)); player.Data.AppearanceMode = "Dark";
                    player.ApplyTheme("Midnight"); player.Window.WindowState = WindowState.Maximized; rows.Add(await Measure(player, "Beta Balanced · maximized window", seconds)); player.Window.WindowState = WindowState.Normal;
                    PlayerApp.FocusOverride = false; rows.Add(await Measure(player, "Beta Balanced · another window in front", seconds)); PlayerApp.FocusOverride = true;
                    exitCode = 0;
                }
                catch (Exception e) { failure = e.ToString(); }
                finally
                {
                    var lines = new List<string> { "Digitone " + AppInfo.Version + " speed test · " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · window 1500x900 · " + seconds + " s per row", "Graphics tier " + (RenderCapability.Tier >> 16) + " · " + Environment.ProcessorCount + " CPU threads", "", String.Format("{0,-44} {1,7}{2,8}{3,9}{4,8}{5,8}{6,8}{7,8}{8,9}{9,10}", "Scene", "FPS", "1% low", "Beta FPS", "CPU %", "UI %", "Draw %", "GPU %", "Beta ms", "ms/frame") };
                    foreach (var row in rows) lines.Add(String.Format("{0,-44} {1,7:0}{2,8:0}{3,9:0}{4,8:0}{5,8:0}{6,8:0}{7,8:0}{8,9:0.00}{9,10:0.0}", row.Name, row.Fps, row.Low, row.BetaFps, row.Cpu, row.Ui, row.Render, row.Gpu, row.BetaMs, row.Fps <= 0 ? 0 : (Math.Max(row.Ui, row.Render) * 10 / row.Fps)));
                    lines.Add(""); lines.Add("FPS = frames Windows actually drew per second · 1% low = FPS of the slowest 1% of frames · Beta FPS = Beta animation updates per second · CPU/UI/Draw % = share of one CPU thread (all / UI thread / WPF drawing thread) · GPU % = this app's 3D engine load · Beta ms = Beta code per update · ms/frame = busiest Digitone thread's work per frame (under 11 = room for 90+ fps; anything else is other apps or Windows)");
                    if (failure != null) { lines.Add(""); lines.Add("FAILED: " + failure); }
                    File.WriteAllLines(Path.Combine(run, "perf.txt"), lines, System.Text.Encoding.UTF8); File.WriteAllLines(Path.Combine(output, "perf-latest.txt"), lines, System.Text.Encoding.UTF8);
                    player.Window.Close();
                }
            };
            app.Run(player.Window);
            return exitCode;
        }
    }
}
