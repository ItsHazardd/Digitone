using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Digitone
{
    // Code-drawn living background: light shafts, soft caustic glows, drifting shards, and music-reactive waves.
    internal sealed class BetaScene
    {
        private sealed class Shard { internal Polygon Shape; internal RotateTransform Spin; internal TranslateTransform Move; internal double X, Y, Speed, Sway, Phase, Turn; }
        private readonly Canvas canvas; private readonly Random random = new Random(11);
        private readonly List<Rectangle> shafts = new List<Rectangle>(); private readonly List<double[]> shaftMotion = new List<double[]>();
        private readonly List<Ellipse> glows = new List<Ellipse>(); private readonly List<double[]> glowMotion = new List<double[]>();
        private readonly List<Shard> shards = new List<Shard>();
        private Polyline waveFront, waveBack; private double time;
        private Ellipse moon, moonHalo; private TextBlock watermark;
        // Everything moves through transforms (no layout work per frame), and soft gradients are drawn once into a cached picture.
        private readonly List<TranslateTransform> shaftMoves = new List<TranslateTransform>(), glowMoves = new List<TranslateTransform>();
        private TranslateTransform watermarkMove, moonMove, haloMove; private ScaleTransform moonScale, haloScale; private double laidOutHeight = -1;
        internal BetaScene(Canvas canvas) { this.canvas = canvas; }
        private static void Place(UIElement element) { Canvas.SetLeft(element, 0); Canvas.SetTop(element, 0); }
        private static BitmapCache Cached() { return new BitmapCache { SnapsToDevicePixels = false }; }
        private static Color Alpha(Color color, double alpha) { return Color.FromArgb((byte)Math.Max(0, Math.Min(255, alpha * 255)), color.R, color.G, color.B); }
        internal void Build(Color accent, Color deep, bool light, bool midnight = false, bool floatingShards = true)
        {
            canvas.Children.Clear(); shafts.Clear(); shaftMotion.Clear(); glows.Clear(); glowMotion.Clear(); shards.Clear(); moon = null; moonHalo = null; watermark = null; shaftMoves.Clear(); glowMoves.Clear(); laidOutHeight = -1;
            double strength = light ? .7 : 1;
            if (midnight)
            {
                // Midnight extras: a huge slanted watermark and a glowing moon, both drawn by code.
                watermark = new TextBlock { Text = "DIGITONE", FontFamily = new FontFamily("Bahnschrift"), FontWeight = FontWeights.Bold, FontStretch = FontStretches.Condensed, FontStyle = FontStyles.Italic, FontSize = 260, Foreground = new SolidColorBrush(Alpha(accent, .03 * strength)), IsHitTestVisible = false }; // not cached: Windows cannot take screenshots of cached text
                watermarkMove = new TranslateTransform(); var watermarkTurn = new TransformGroup(); watermarkTurn.Children.Add(new RotateTransform(-12)); watermarkTurn.Children.Add(watermarkMove); watermark.RenderTransform = watermarkTurn; Place(watermark);
                canvas.Children.Add(watermark);
                moonHalo = new Ellipse { Width = 420, Height = 420, IsHitTestVisible = false, CacheMode = Cached(), RenderTransformOrigin = new Point(.5, .5) };
                var halo = new RadialGradientBrush(); halo.GradientStops.Add(new GradientStop(Alpha(accent, .22 * strength), .45)); halo.GradientStops.Add(new GradientStop(Alpha(accent, 0), 1)); moonHalo.Fill = halo;
                moon = new Ellipse { Width = 170, Height = 170, IsHitTestVisible = false, CacheMode = Cached(), RenderTransformOrigin = new Point(.5, .5) };
                var face = new RadialGradientBrush { GradientOrigin = new Point(.35, .3) }; face.GradientStops.Add(new GradientStop(Alpha(Colors.White, .5 * strength), 0)); face.GradientStops.Add(new GradientStop(Alpha(accent, .32 * strength), .75)); face.GradientStops.Add(new GradientStop(Alpha(accent, .12 * strength), 1)); moon.Fill = face;
                moonScale = new ScaleTransform(); moonMove = new TranslateTransform(); var moonGroup = new TransformGroup(); moonGroup.Children.Add(moonScale); moonGroup.Children.Add(moonMove); moon.RenderTransform = moonGroup;
                haloScale = new ScaleTransform(); haloMove = new TranslateTransform(); var haloGroup = new TransformGroup(); haloGroup.Children.Add(haloScale); haloGroup.Children.Add(haloMove); moonHalo.RenderTransform = haloGroup;
                Place(moon); Place(moonHalo); canvas.Children.Add(moonHalo); canvas.Children.Add(moon);
            }
            for (int i = 0; i < 3; i++)
            {
                var glowMove = new TranslateTransform(); glowMoves.Add(glowMove);
                var glow = new Ellipse { Width = 560 + 140 * i, Height = 560 + 140 * i, IsHitTestVisible = false, RenderTransform = glowMove, CacheMode = Cached() }; Place(glow);
                var brush = new RadialGradientBrush(); brush.GradientStops.Add(new GradientStop(Alpha(i == 1 ? deep : accent, .16 * strength), 0)); brush.GradientStops.Add(new GradientStop(Alpha(accent, 0), 1));
                glow.Fill = brush; canvas.Children.Add(glow); glows.Add(glow);
                glowMotion.Add(new[] { random.NextDouble(), random.NextDouble(), .025 + .02 * random.NextDouble(), .018 + .02 * random.NextDouble(), random.NextDouble() * 6 });
            }
            for (int i = 0; i < 5; i++)
            {
                var shaftMove = new TranslateTransform(); shaftMoves.Add(shaftMove); var shaftTurn = new TransformGroup(); shaftTurn.Children.Add(new RotateTransform(24)); shaftTurn.Children.Add(shaftMove);
                var shaft = new Rectangle { Width = 90 + random.Next(170), IsHitTestVisible = false, RenderTransform = shaftTurn, CacheMode = Cached() }; Place(shaft);
                var brush = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
                brush.GradientStops.Add(new GradientStop(Alpha(accent, 0), 0)); brush.GradientStops.Add(new GradientStop(Alpha(accent, .13 * strength), .5)); brush.GradientStops.Add(new GradientStop(Alpha(accent, 0), 1));
                shaft.Fill = brush; canvas.Children.Add(shaft); shafts.Add(shaft);
                shaftMotion.Add(new[] { (i + .5) / 5.0, .08 + .07 * random.NextDouble(), random.NextDouble() * 6, .5 + .4 * random.NextDouble() });
            }
            waveBack = new Polyline { Stroke = new SolidColorBrush(Alpha(accent, .22 * strength)), StrokeThickness = 1.5, IsHitTestVisible = false };
            waveFront = new Polyline { Stroke = new SolidColorBrush(Alpha(accent, .45 * strength)), StrokeThickness = 2.2, IsHitTestVisible = false };
            Place(waveBack); Place(waveFront); canvas.Children.Add(waveBack); canvas.Children.Add(waveFront);
            for (int i = 0; i < (floatingShards ? 16 : 0); i++)
            {
                double size = 6 + random.NextDouble() * 18; bool sliver = i % 3 == 0;
                var shape = new Polygon { Stroke = new SolidColorBrush(Alpha(accent, .55 * strength)), StrokeThickness = 1.1, Fill = new SolidColorBrush(Alpha(accent, .1 * strength)), IsHitTestVisible = false };
                shape.Points = sliver ? new PointCollection { new Point(size * .4, 0), new Point(size * 1.4, 0), new Point(size, size * .45), new Point(0, size * .45) } : new PointCollection { new Point(0, size), new Point(size * .5, 0), new Point(size, size * .9) };
                var spin = new RotateTransform(random.NextDouble() * 360, size / 2, size / 2); var drift = new TranslateTransform(); var shardMotion = new TransformGroup(); shardMotion.Children.Add(spin); shardMotion.Children.Add(drift); shape.RenderTransform = shardMotion;
                Place(shape); canvas.Children.Add(shape);
                shards.Add(new Shard { Shape = shape, Spin = spin, Move = drift, X = random.NextDouble(), Y = random.NextDouble(), Speed = 10 + random.NextDouble() * 22, Sway = 6 + random.NextDouble() * 14, Phase = random.NextDouble() * 6, Turn = (random.NextDouble() - .5) * 40 });
            }
        }
        internal void Step(double seconds, double energy)
        {
            time += seconds; double w = canvas.ActualWidth, h = canvas.ActualHeight; if (w < 10 || h < 10) return;
            if (watermark != null) { watermarkMove.X = w * .18 + Math.Sin(time * .05) * 60; watermarkMove.Y = h * .22 + Math.Cos(time * .04) * 20; }
            if (moon != null)
            {
                double moonSwell = 1 + .06 * energy, haloSwell = 1 + .14 * energy, x = w * .8, y = h * .2;
                moonScale.ScaleX = moonScale.ScaleY = moonSwell; haloScale.ScaleX = haloScale.ScaleY = haloSwell;
                moonMove.X = x - 85; moonMove.Y = y - 85; haloMove.X = x - 210; haloMove.Y = y - 210;
                moonHalo.Opacity = .7 + .3 * energy;
            }
            for (int i = 0; i < glows.Count; i++)
            {
                var m = glowMotion[i]; var glow = glows[i];
                glowMoves[i].X = (m[0] * .8 + .1) * w - glow.Width / 2 + Math.Sin(time * m[2] * 6 + m[4]) * w * .18;
                glowMoves[i].Y = (m[1] * .8 + .1) * h - glow.Height / 2 + Math.Cos(time * m[3] * 6 + m[4]) * h * .16;
                glow.Opacity = .75 + .25 * energy;
            }
            for (int i = 0; i < shafts.Count; i++)
            {
                var m = shaftMotion[i]; var shaft = shafts[i];
                if (h != laidOutHeight) { shaft.Height = h * 1.9; shaftMoves[i].Y = -h * .45; }
                shaftMoves[i].X = m[0] * w + Math.Sin(time * m[1] + m[2]) * 110;
                shaft.Opacity = Math.Max(0, Math.Min(1, m[3] + .3 * Math.Sin(time * .4 + m[2]) + .45 * energy));
            }
            var back = new PointCollection(); var front = new PointCollection(); double baseY = h - 54, swell = 7 + 26 * energy;
            for (int i = 0; i <= 64; i++)
            {
                double x = w * i / 64.0;
                front.Add(new Point(x, baseY + Math.Sin(x * .011 + time * 1.3) * swell + Math.Sin(x * .027 - time * .8) * 4));
                back.Add(new Point(x, baseY + 16 + Math.Sin(x * .008 - time * .9 + 1.7) * swell * .7 + Math.Sin(x * .021 + time * .6) * 3));
            }
            front.Freeze(); back.Freeze(); waveFront.Points = front; waveBack.Points = back; laidOutHeight = h;
            foreach (var shard in shards)
            {
                shard.Y -= shard.Speed * seconds * (1 + 1.6 * energy) / h;
                if (shard.Y < -.08) { shard.Y = 1.06; shard.X = random.NextDouble(); }
                shard.Move.X = shard.X * w + Math.Sin(time * .7 + shard.Phase) * shard.Sway;
                shard.Move.Y = shard.Y * h;
                shard.Spin.Angle += shard.Turn * seconds * (1 + energy);
                shard.Shape.Opacity = .5 + .5 * Math.Sin(Math.PI * Math.Max(0, Math.Min(1, shard.Y)));
            }
        }
    }

    public sealed partial class PlayerApp
    {
        private bool betaActive; private bool betaReady;
        private Canvas betaBackdrop, betaHeaderLayer, betaListLayer;
        private Rectangle betaTabBar, betaTabTrail, betaSheen; private Polygon betaCursor;
        private FrameClock betaTimer; private BetaScene betaScene; private DateTime betaLastTick;
        private double betaEnergy, betaPeak = .05; private ListBoxItem betaHovered; private double[] betaWave;
        private readonly List<Action> betaRestore = new List<Action>();
        private readonly Dictionary<UIElement, Effect> betaEffects = new Dictionary<UIElement, Effect>();
        private static PlayerApp betaOwner; private static bool betaWindowsHooked; private DispatcherTimer betaShineTimer;
        private static readonly string[] BetaTransport = { "ShuffleButton", "PreviousButton", "PlayButton", "NextButton", "RepeatButton", "MuteButton" };
        private static readonly string[] BetaNav = { "LibraryButton", "FavoritesButton", "DownloadsButton", "PlaylistsButton" };
        internal bool BetaActive { get { return betaActive; } }
        internal double BetaEnergy { get { return betaEnergy; } }
        internal Canvas BetaBackdrop { get { return betaBackdrop; } }

        private void SetupBeta()
        {
            betaOwner = this; UiSounds.MusicPlaying = delegate { return playing; };
            var root = (Grid)Window.Content;
            betaBackdrop = new Canvas { Name = "BetaBackdrop", IsHitTestVisible = false, ClipToBounds = true, Visibility = Visibility.Collapsed };
            Grid.SetRowSpan(betaBackdrop, 4); root.Children.Insert(0, betaBackdrop);
            betaHeaderLayer = new Canvas { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
            root.Children.Insert(2, betaHeaderLayer);
            var tracks = Find<ListBox>("Tracks"); var listHost = (Grid)tracks.Parent;
            betaListLayer = new Canvas { IsHitTestVisible = false, ClipToBounds = true, Visibility = Visibility.Collapsed }; Panel.SetZIndex(betaListLayer, 5); listHost.Children.Add(betaListLayer);
            betaScene = new BetaScene(betaBackdrop);

            betaTabBar = new Rectangle { Height = 5, RenderTransform = new SkewTransform(-24, 0) }; betaTabBar.SetResourceReference(Shape.FillProperty, "BC1D3A8");
            betaTabTrail = new Rectangle { Height = 2, RenderTransform = new SkewTransform(-24, 0), Opacity = .8 }; betaTabTrail.SetResourceReference(Shape.FillProperty, "B657460");
            betaHeaderLayer.Children.Add(betaTabTrail); betaHeaderLayer.Children.Add(betaTabBar);
            betaSheen = new Rectangle { RenderTransformOrigin = new Point(0, .5), RenderTransform = new ScaleTransform(0, 1), Visibility = Visibility.Collapsed };
            betaCursor = new Polygon { Visibility = Visibility.Collapsed }; betaCursor.SetResourceReference(Shape.FillProperty, "BC1D3A8");
            betaListLayer.Children.Add(betaSheen); betaListLayer.Children.Add(betaCursor);
            // Animations need real starting values (Canvas positions and Width start as "not set").
            foreach (var shape in new FrameworkElement[] { betaTabBar, betaTabTrail, betaSheen, betaCursor }) { Canvas.SetLeft(shape, 0); Canvas.SetTop(shape, 0); }
            betaTabBar.Width = 0; betaTabTrail.Width = 0; betaSheen.Width = 0; betaSheen.Height = 0;

            betaTimer = new FrameClock(); // every screen refresh (165 times a second on a 165 Hz screen)
            betaTimer.Tick += BetaTick;
            Window.SizeChanged += delegate { if (betaActive) { BetaPlaceTab(false); BetaPlaceCursor(false); } };
            Window.StateChanged += delegate { if (!betaActive) return; if (Window.WindowState == WindowState.Minimized) betaTimer.Stop(); else { BetaGuardRest(); betaTimer.Start(); } };
            Window.Closed += delegate { betaTimer.Stop(); UiSounds.Enabled = false; };

            foreach (string name in BetaNav)
            {
                var button = Find<Button>(name);
                button.MouseEnter += delegate { if (!betaActive || !button.IsEnabled) return; UiSounds.Play("tick"); BetaMotion.ScaleTo(button, 1.07, 280, BetaMotion.Back(.7)); BetaMotion.To(BetaMotion.Parts(button).Skew, SkewTransform.AngleXProperty, -6, 280, BetaMotion.Back(.7)); };
                button.MouseLeave += delegate { if (!betaActive) return; BetaMotion.ScaleTo(button, 1, 260, BetaMotion.Back(.5)); BetaMotion.To(BetaMotion.Parts(button).Skew, SkewTransform.AngleXProperty, 0, 260, BetaMotion.Back(.5)); };
            }
            Window.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
            {
                var button = e.OriginalSource as Button; if (!betaActive || button == null) return;
                bool transport = BetaTransport.Contains(button.Name), nav = BetaNav.Contains(button.Name);
                BetaMotion.Punch(button, transport ? .8 : nav ? .9 : .92);
                if (!nav) UiSounds.Play(button.Name == "CloseExpandedButton" || button.Name == "CloseQueueButton" ? "back" : button.Name == "PlayButton" ? "confirm" : "select");
            }), true);
            tracks.SelectionChanged += delegate { if (!betaActive) return; if (betaHovered != null && betaHovered.IsSelected) { BetaMotion.To(BetaMotion.Parts(betaHovered).Move, TranslateTransform.XProperty, 0, 200, BetaMotion.Snap()); betaHovered = null; } BetaPlaceCursor(true); if (tracks.IsMouseOver || tracks.IsKeyboardFocusWithin) UiSounds.Play("tick"); };
            tracks.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(delegate { if (betaActive) BetaPlaceCursor(false); }));
            tracks.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!betaActive) return;
                var item = ItemsControl.ContainerFromElement(tracks, e.OriginalSource as DependencyObject) as ListBoxItem;
                if (item != null && item.IsSelected) item = null; // the selected row stays put under its cursor
                if (item == betaHovered) return;
                if (betaHovered != null) BetaMotion.To(BetaMotion.Parts(betaHovered).Move, TranslateTransform.XProperty, 0, 200, BetaMotion.Snap());
                betaHovered = item;
                if (item != null) BetaMotion.To(BetaMotion.Parts(item).Move, TranslateTransform.XProperty, 7, 240, BetaMotion.Back(.6));
            };
            tracks.MouseLeave += delegate { if (betaHovered != null) BetaMotion.To(BetaMotion.Parts(betaHovered).Move, TranslateTransform.XProperty, 0, 200, BetaMotion.Snap()); betaHovered = null; };

            if (!betaWindowsHooked)
            {
                betaWindowsHooked = true;
                EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(delegate(object sender, RoutedEventArgs e)
                {
                    var window = sender as Window; var owner = betaOwner;
                    if (window != null && owner != null && owner.betaActive && window != owner.Window && window.Owner == owner.Window) owner.BetaDialogEnter(window);
                }));
            }
            SetupBeta3D();
            Window.Loaded += delegate { betaReady = true; if (Data.BetaTheme) ApplyBeta(true); };
        }

        internal void SetBeta(bool on) { Data.BetaTheme = on; Save(); ApplyBeta(on); }
        internal void SetBetaQuality(string quality) { Data.BetaQuality = quality; Save(); BetaGuardRest(); if (betaActive) { BetaStyle(true); BetaRecolor(); BetaApply3D(true); } }
        // The guard waits 10 seconds after Beta starts or the quality changes, so building the 3D scene never counts as stutter.
        internal void BetaGuardWake() { betaGuardQuiet = DateTime.MinValue; betaGuardStrikes = 0; } // tests only
        private void BetaGuardRest() { betaGuardQuiet = DateTime.UtcNow.AddSeconds(10); betaGuardStrikes = 0; betaGuardStart = DateTime.UtcNow; betaGuardTicks = 0; }
        internal void SetBetaSounds(bool on) { Data.BetaSounds = on; Save(); UiSounds.Enabled = betaActive && on; if (on && betaActive) UiSounds.Play("confirm"); }

        private void ApplyBeta(bool on)
        {
            if (!betaReady) return;
            betaActive = on; UiSounds.Enabled = on && Data.BetaSounds != false;
            foreach (var layer in new UIElement[] { betaBackdrop, betaHeaderLayer, betaListLayer }) layer.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            BetaStyle(on);
            if (!on) BetaUntiltCards();
            if (on)
            {
                BetaRecolor(); BetaApply3D(true); betaLastTick = DateTime.UtcNow; BetaGuardRest(); betaTimer.Start();
                Window.Dispatcher.BeginInvoke(new Action(delegate { BetaPlaceTab(false); BetaPlaceCursor(false); BetaIntro(); }), DispatcherPriority.Loaded);
            }
            else
            {
                betaTimer.Stop(); betaEnergy = 0; betaHovered = null; BetaApply3D(false); BetaMotion.Reset(betaBackdrop); BetaMotion.Reset(Find<Grid>("ContentStage"));
                foreach (var element in BetaTouched()) BetaMotion.Reset(element);
                betaCursor.Visibility = Visibility.Collapsed; betaSheen.Visibility = Visibility.Collapsed;
            }
        }
        private IEnumerable<UIElement> BetaTouched()
        {
            var names = BetaNav.Concat(BetaTransport).Concat(new[] { "NowTitle", "NowDetail", "PlayerTitle", "PlayerDetail", "ExpandedTitle", "RecordArt", "CollectionTitle", "LibraryPane", "PlaylistPane", "DownloadPane", "BrandTitleFit" });
            foreach (string name in names) { var element = Window.FindName(name) as UIElement; if (element != null) yield return element; }
            foreach (var item in BetaListItems(1000)) yield return item;
            var grid = Find<WrapPanel>("PlaylistGrid"); foreach (UIElement card in grid.Children) yield return card;
        }

        // Fonts and glows: Bahnschrift for headings on themes that use the plain display font; every change is undone when Beta is off.
        private void BetaStyle(bool on)
        {
            foreach (var undo in betaRestore) undo(); betaRestore.Clear();
            foreach (var saved in betaEffects) ((UIElement)saved.Key).Effect = saved.Value; betaEffects.Clear();
            if (!on) return;
            bool plainFont = Data.ThemeName == "Midnight" || Data.ThemeName == "Collective" || Data.ThemeName == "Custom";
            var headings = new List<FrameworkElement> { (FrameworkElement)Find<Viewbox>("BrandTitleFit").Child, Find<TextBlock>("NowTitle"), Find<TextBlock>("PlayerTitle"), Find<TextBlock>("CollectionTitle"), Find<TextBlock>("ExpandedTitle") };
            foreach (string name in BetaNav) headings.Add(Find<Button>(name));
            foreach (var element in headings)
            {
                bool nav = element is Button; if (!nav && !plainFont) continue;
                var target = element;
                DependencyProperty familyProperty = element is Control ? Control.FontFamilyProperty : TextBlock.FontFamilyProperty, weightProperty = element is Control ? Control.FontWeightProperty : TextBlock.FontWeightProperty, stretchProperty = element is Control ? Control.FontStretchProperty : TextBlock.FontStretchProperty;
                var family = target.ReadLocalValue(familyProperty);
                bool resource = family is Expression; object oldFamily = family, oldWeight = target.ReadLocalValue(weightProperty), oldStretch = target.ReadLocalValue(stretchProperty);
                target.SetValue(familyProperty, new FontFamily("Bahnschrift")); target.SetValue(weightProperty, FontWeights.Bold); target.SetValue(stretchProperty, FontStretches.Condensed);
                betaRestore.Add(delegate
                {
                    if (resource) target.SetResourceReference(familyProperty, "ThemeDisplayFont"); else if (oldFamily == DependencyProperty.UnsetValue) target.ClearValue(familyProperty); else target.SetValue(familyProperty, oldFamily);
                    if (oldWeight == DependencyProperty.UnsetValue || oldWeight is Expression) target.ClearValue(weightProperty); else target.SetValue(weightProperty, oldWeight);
                    if (oldStretch == DependencyProperty.UnsetValue || oldStretch is Expression) target.ClearValue(stretchProperty); else target.SetValue(stretchProperty, oldStretch);
                });
            }
            var trackList = Find<ListBox>("Tracks"); trackList.ItemContainerStyle = (Style)Window.FindResource("BetaTrackRow");
            betaRestore.Add(delegate { trackList.ClearValue(ItemsControl.ItemContainerStyleProperty); });
            foreach (var element in new UIElement[] { Find<Button>("PlayButton"), Find<Slider>("Seek") })
                if (element.Effect == null) { betaEffects[element] = null; element.Effect = new DropShadowEffect { ShadowDepth = 0, BlurRadius = 10, Opacity = 0, Color = BetaAccent() }; }
            // A bright sheen sweeps across the logo every few seconds.
            var brand = (TextBlock)Find<Viewbox>("BrandTitleFit").Child; var oldForeground = brand.ReadLocalValue(TextBlock.ForegroundProperty);
            var baseBrush = brand.Foreground as SolidColorBrush;
            if (baseBrush != null)
            {
                Color baseColor = baseBrush.Color, shine = Data.AppearanceMode == "Light" ? BetaAccent() : Colors.White;
                var sweep = new TranslateTransform(-1.2, 0);
                var gradient = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, .35), RelativeTransform = sweep };
                gradient.GradientStops.Add(new GradientStop(baseColor, 0)); gradient.GradientStops.Add(new GradientStop(baseColor, .42)); gradient.GradientStops.Add(new GradientStop(shine, .5)); gradient.GradientStops.Add(new GradientStop(baseColor, .58)); gradient.GradientStops.Add(new GradientStop(baseColor, 1));
                brand.Foreground = gradient;
                if (BetaMotion.Animate)
                {
                    // One sweep every 6 seconds. A timer starts each sweep, so nothing redraws during the pause between them.
                    Action shinePass = delegate
                    {
                        var pass = new DoubleAnimation(-1.2, 1.2, TimeSpan.FromMilliseconds(1100)) { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } }; sweep.BeginAnimation(TranslateTransform.XProperty, pass);
                    };
                    var shineTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) }; shineTimer.Tick += delegate { shinePass(); };
                    shinePass(); shineTimer.Start(); betaShineTimer = shineTimer;
                    betaRestore.Add(delegate { shineTimer.Stop(); if (betaShineTimer == shineTimer) betaShineTimer = null; });
                }
                betaRestore.Add(delegate { if (oldForeground == DependencyProperty.UnsetValue || oldForeground is Expression) brand.ClearValue(TextBlock.ForegroundProperty); else brand.Foreground = (Brush)oldForeground; });
            }
        }
        private Color BetaAccent() { var brush = Window.TryFindResource("BC1D3A8") as SolidColorBrush; return brush != null ? brush.Color : Colors.DeepSkyBlue; }
        private Color BetaDeep() { var brush = Window.TryFindResource("B354233") as SolidColorBrush; return brush != null ? brush.Color : Colors.Navy; }
        private void BetaRecolor()
        {
            betaScene.Build(BetaAccent(), BetaDeep(), Data.AppearanceMode == "Light", Data.ThemeName == "Midnight", !BetaWants3D);
            var accentColor = BetaAccent(); var sheen = new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) };
            sheen.GradientStops.Add(new GradientStop(Color.FromArgb(90, accentColor.R, accentColor.G, accentColor.B), 0)); sheen.GradientStops.Add(new GradientStop(Color.FromArgb(0, accentColor.R, accentColor.G, accentColor.B), 1));
            betaSheen.Fill = sheen;
            foreach (var effect in new[] { Find<Button>("PlayButton").Effect, Find<Slider>("Seek").Effect }.OfType<DropShadowEffect>()) if (betaEffects.Count > 0) effect.Color = accentColor;
            betaScene.Step(0, 0);
        }
        internal void BetaThemeChanged() { if (!betaActive) return; BetaStyle(true); BetaRecolor(); BetaApply3D(true); BetaPlaceTab(false); }

        // Startup flourish: the logo and header snap in.
        private void BetaIntro()
        {
            BetaMotion.Arrive(Find<Viewbox>("BrandTitleFit"), -70, 0, -18, .9, 620, 0, .6);
            int index = 0; foreach (string name in BetaNav) BetaMotion.Arrive(Find<Button>(name), 0, -28, -10, .9, 520, 90 + 60 * index++, .55);
            BetaMotion.Arrive(Find<Border>("NowPane"), -60, 0, -6, .97, 560, 120, .4);
            BetaCascade();
            UiSounds.Play("whoosh");
        }

        // Moves the slanted bar under whichever tab is active.
        private void BetaPlaceTab(bool animate)
        {
            if (!betaActive) return;
            string active = Find<Grid>("DownloadPane").Visibility == Visibility.Visible ? "DownloadsButton" : playlistsView ? "PlaylistsButton" : favorites ? "FavoritesButton" : selectedPlaylist == null ? "LibraryButton" : null;
            if (active == null) { betaTabBar.Opacity = 0; betaTabTrail.Opacity = 0; return; }
            var button = Find<Button>(active); if (!button.IsLoaded || betaHeaderLayer.ActualWidth <= 0) return;
            Point at; try { at = button.TranslatePoint(new Point(0, 0), betaHeaderLayer); } catch (InvalidOperationException) { return; }
            double width = button.ActualWidth, top = at.Y + button.ActualHeight + 4;
            betaTabBar.Opacity = 1; betaTabTrail.Opacity = .8; Canvas.SetTop(betaTabBar, top); Canvas.SetTop(betaTabTrail, top + 8);
            if (!animate || !BetaMotion.Animate) { betaTabBar.BeginAnimation(Canvas.LeftProperty, null); betaTabBar.BeginAnimation(FrameworkElement.WidthProperty, null); betaTabTrail.BeginAnimation(Canvas.LeftProperty, null); betaTabTrail.BeginAnimation(FrameworkElement.WidthProperty, null); Canvas.SetLeft(betaTabBar, at.X); betaTabBar.Width = width; Canvas.SetLeft(betaTabTrail, at.X + 10); betaTabTrail.Width = width; return; }
            // A smooth glide with a soft landing (no overshoot); the thin trail follows a beat later.
            var glide = new QuinticEase { EasingMode = EasingMode.EaseOut };
            BetaMotion.To(betaTabBar, Canvas.LeftProperty, at.X, 420, glide); BetaMotion.To(betaTabBar, FrameworkElement.WidthProperty, width, 420, glide);
            BetaMotion.To(betaTabTrail, Canvas.LeftProperty, at.X + 10, 520, glide, 50); BetaMotion.To(betaTabTrail, FrameworkElement.WidthProperty, width, 520, glide, 50);
        }

        // Called whenever the main view changes. The page itself glides in (PageGlide); Beta adds its sound and slides the tab bar.
        internal void BetaNavigate(string paneName)
        {
            if (!betaActive) return;
            UiSounds.Play("whoosh");
            Window.Dispatcher.BeginInvoke(new Action(delegate { BetaPlaceTab(true); }), DispatcherPriority.Loaded);
        }

        private List<ListBoxItem> BetaListItems(int limit)
        {
            var tracks = Find<ListBox>("Tracks"); var list = new List<ListBoxItem>();
            for (int i = 0; i < tracks.Items.Count && list.Count < limit; i++) { var item = tracks.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem; if (item != null && item.IsVisible) list.Add(item); }
            return list;
        }
        // Song rows cascade in quickly: lively, but calm enough for long listening sessions.
        private void BetaCascade()
        {
            if (!betaActive) return;
            Window.Dispatcher.BeginInvoke(new Action(delegate
            {
                var rows = BetaListItems(60); var tracks = Find<ListBox>("Tracks"); int shown = 0;
                foreach (var row in rows)
                {
                    Point at; try { at = row.TranslatePoint(new Point(0, 0), tracks); } catch (InvalidOperationException) { continue; }
                    if (at.Y > tracks.ActualHeight || at.Y + row.ActualHeight < 0) continue;
                    BetaMotion.Arrive(row, 36, 0, -6, 1, 300, 30 + 18 * shown++, .25);
                    if (shown >= 16) break;
                }
                BetaPlaceCursor(false);
            }), DispatcherPriority.Loaded);
        }
        // The slanted cursor and sheen that slide to the selected song.
        private void BetaPlaceCursor(bool animate)
        {
            if (!betaActive) return;
            var tracks = Find<ListBox>("Tracks"); var item = tracks.SelectedItem == null ? null : tracks.ItemContainerGenerator.ContainerFromItem(tracks.SelectedItem) as ListBoxItem;
            Point at = new Point(); bool show = item != null && item.IsVisible && betaListLayer.ActualHeight > 0;
            if (show) { try { at = item.TranslatePoint(new Point(0, 0), betaListLayer); } catch (InvalidOperationException) { show = false; } }
            if (show && (at.Y + item.ActualHeight < 0 || at.Y > betaListLayer.ActualHeight)) show = false;
            betaCursor.Visibility = show ? Visibility.Visible : Visibility.Collapsed; betaSheen.Visibility = betaCursor.Visibility; if (!show) return;
            double h = item.ActualHeight;
            betaCursor.Points = new PointCollection { new Point(5, 0), new Point(13, 0), new Point(8, h), new Point(0, h) };
            Canvas.SetLeft(betaCursor, at.X - 6); betaSheen.Width = Math.Max(0, item.ActualWidth); betaSheen.Height = h; Canvas.SetLeft(betaSheen, at.X);
            if (!animate || !BetaMotion.Animate) { betaCursor.BeginAnimation(Canvas.TopProperty, null); betaSheen.BeginAnimation(Canvas.TopProperty, null); Canvas.SetTop(betaCursor, at.Y); Canvas.SetTop(betaSheen, at.Y); ((ScaleTransform)betaSheen.RenderTransform).ScaleX = 1; return; }
            BetaMotion.To(betaCursor, Canvas.TopProperty, at.Y, 260, BetaMotion.Back(.5)); Canvas.SetTop(betaSheen, at.Y); betaSheen.BeginAnimation(Canvas.TopProperty, null);
            ((ScaleTransform)betaSheen.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = BetaMotion.Snap() });
        }

        // New song: titles snap in from the side with a skew.
        internal void BetaTrackChanged()
        {
            if (!betaActive) return;
            foreach (string name in new[] { "NowTitle", "PlayerTitle", "ExpandedTitle" }) { var title = Window.FindName(name) as UIElement; if (title != null) BetaMotion.Arrive(title, 90, 0, -16, 1, 520, 0, .55); }
            foreach (string name in new[] { "NowDetail", "PlayerDetail" }) { var detail = Window.FindName(name) as UIElement; if (detail != null) BetaMotion.Arrive(detail, 60, 0, -10, 1, 460, 90, .35); }
            if (BetaTileShown) BetaFlipTile(); else BetaMotion.Punch(Find<Border>("RecordArt"), .93);
        }

        private readonly System.Diagnostics.Stopwatch betaCost = new System.Diagnostics.Stopwatch(); private int betaTicks;
        internal double BetaAverageFrameMs { get { return betaTicks == 0 ? 0 : betaCost.Elapsed.TotalMilliseconds / betaTicks; } }
        internal double BetaCostMs { get { return betaCost.Elapsed.TotalMilliseconds; } }
        internal int BetaTickCount { get { return betaTicks; } }
        private void BetaTick()
        {
            betaCost.Start(); try { BetaFrame(); } finally { betaCost.Stop(); betaTicks++; }
            // Full screen refresh rate while Digitone is in front (or the visualizer is open); 30 fps while another window (a game, say) is in front.
            var interval = TimeSpan.FromMilliseconds(BetaFocused || Find<Grid>("ExpandedView").Visibility == Visibility.Visible ? 0 : 33);
            if (betaTimer.Interval != interval) { betaTimer.Interval = interval; betaGuardStart = DateTime.UtcNow; betaGuardTicks = 0; betaGuardShortest = double.MaxValue; }
            BetaGuardTick();
        }

        // Auto quality guard: if Beta keeps falling well behind its frame rate, step 3D depth down (Ultra → Balanced → Flat) and say so.
        private DateTime betaGuardStart = DateTime.UtcNow, betaGuardQuiet = DateTime.MinValue, betaGuardLast = DateTime.MinValue; private int betaGuardTicks, betaGuardStrikes; private double betaGuardShortest = double.MaxValue;
        internal string BetaGuardMessage { get; private set; }
        internal static bool? FocusOverride; // speed tests only
        private bool BetaFocused { get { return FocusOverride ?? Window.IsActive; } }
        private void BetaGuardTick()
        {
            var now = DateTime.UtcNow; if (betaGuardLast != DateTime.MinValue) betaGuardShortest = Math.Min(betaGuardShortest, Math.Max(4, (now - betaGuardLast).TotalMilliseconds)); betaGuardLast = now;
            betaGuardTicks++; double span = (now - betaGuardStart).TotalMilliseconds; if (span < 3000) return;
            // Expected gap: the interval asked for, or one screen refresh (the shortest gap seen) when running every frame.
            double expected = Math.Max(betaTimer.Interval.TotalMilliseconds, betaGuardShortest == double.MaxValue ? 16.7 : betaGuardShortest);
            BetaGuardFeed(span / betaGuardTicks, expected, BetaFocused && Window.WindowState != WindowState.Minimized);
            betaGuardStart = now; betaGuardTicks = 0; betaGuardShortest = double.MaxValue;
        }
        // One 3-second sample: average gap between Beta frames vs. the expected gap. Two slow samples in a row step the quality down.
        internal bool BetaGuardFeed(double averageGapMs, double expectedGapMs, bool focused)
        {
            if (!betaActive || Data.BetaAutoQualityOff || Data.BetaQuality == "Flat" || !BetaWants3D || !focused || DateTime.UtcNow < betaGuardQuiet) { betaGuardStrikes = 0; return false; }
            // A stutter safety net, not a speed limit: it only acts below ~45 fps (or far below what was asked for).
            if (averageGapMs < Math.Max(expectedGapMs * 1.7, 1000 / 45.0)) { betaGuardStrikes = 0; return false; }
            if (++betaGuardStrikes < 2) return false;
            betaGuardStrikes = 0; string lower = BetaUltra ? null : "Flat";
            BetaGuardMessage = "Digitone lowered Beta 3D depth to " + (lower == null ? "Balanced" : "Flat") + " to keep things smooth on this PC. You can change it back in Settings > Appearance.";
            SetBetaQuality(lower); Status(BetaGuardMessage); return true;
        }
        private void BetaFrame()
        {
            var now = DateTime.UtcNow; double seconds = Math.Min(.1, (now - betaLastTick).TotalSeconds); betaLastTick = now;
            FeedRave(); double level = 0; betaWave = null;
            if (playing && ready && liveWave != null)
            {
                var frame = liveWave.Sample(media.Position.TotalSeconds); betaWave = frame;
                if (frame != null && frame.Length > 0) { double sum = 0; foreach (double sample in frame) sum += sample * sample; level = Math.Sqrt(sum / frame.Length); }
            }
            betaPeak = Math.Max(.02, Math.Max(level, betaPeak * BetaMotion.Decay(.996, seconds))); double target = Math.Min(1, level / betaPeak);
            betaEnergy += (target - betaEnergy) * BetaMotion.Ease(target > betaEnergy ? .45 : .1, seconds);
            if (!BetaMotion.Animate) { betaEnergy = 0; return; }
            betaScene.Step(seconds, betaEnergy);
            BetaFrame3D(seconds);
            var record = BetaMotion.Parts(Find<Border>("RecordArt"));
            if (record.Scale.HasAnimatedProperties == false && !BetaTileShown) { record.Scale.ScaleX = 1 + .035 * betaEnergy; record.Scale.ScaleY = record.Scale.ScaleX; }
            // Glows are only touched when the change is big enough to see; every touch makes Windows redo the blur.
            var play = Find<Button>("PlayButton").Effect as DropShadowEffect; if (play != null && betaEffects.ContainsKey(Find<Button>("PlayButton"))) BetaGlow(play, 8 + 30 * betaEnergy, (playing ? .3 : .12) + .6 * betaEnergy);
            var seek = Find<Slider>("Seek").Effect as DropShadowEffect; if (seek != null && betaEffects.ContainsKey(Find<Slider>("Seek"))) BetaGlow(seek, 6 + 14 * betaEnergy, .15 + .55 * betaEnergy);
        }

        private static void BetaGlow(DropShadowEffect glow, double radius, double opacity)
        {
            if (Math.Abs(glow.BlurRadius - radius) >= .75) glow.BlurRadius = radius;
            if (Math.Abs(glow.Opacity - opacity) >= .015) glow.Opacity = opacity;
        }

        // Dialogs and windows fly in; their cards cascade.
        private void BetaDialogEnter(Window window)
        {
            UiSounds.Play("pop");
            var content = window.Content as UIElement; if (content == null) return;
            BetaMotion.Arrive(content, 70, 18, -6, .95, 460, 0, .4);
            var panel = BetaFirstBusyPanel(content);
            if (panel != null)
            {
                int i = 0;
                foreach (var child in panel.Children.OfType<FrameworkElement>().Where(c => c.IsVisible).Take(14))
                {
                    if (child.RenderTransform != null && !child.RenderTransform.Value.IsIdentity && !(child.RenderTransform is TranslateTransform) && !BetaMotion.Has(child)) continue;
                    BetaMotion.Arrive(child, 50, 0, -8, 1, 380, 70 + 34 * i++, .35);
                }
            }
            window.Closed += delegate { if (betaActive) UiSounds.Play("back"); };
        }
        private static Panel BetaFirstBusyPanel(DependencyObject root)
        {
            var queue = new Queue<DependencyObject>(); queue.Enqueue(root); int visited = 0;
            while (queue.Count > 0 && visited++ < 400)
            {
                var node = queue.Dequeue(); var panel = node as Panel;
                if (panel != null && panel.Children.OfType<FrameworkElement>().Count(c => c.IsVisible) >= 3 && !(panel is WrapPanel)) return panel;
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) queue.Enqueue(VisualTreeHelper.GetChild(node, i));
            }
            return null;
        }

        private StackPanel BetaSettingsPanel()
        {
            var panel = new StackPanel { Name = "BetaThemeSettingsSection" };
            panel.Children.Add(SettingsText("Beta Theme", 16));
            panel.Children.Add(SettingsText("A bouncier, livelier interface on top of any theme: slanted tabs with a sliding bar, sweeping screen changes, songs that cascade in, a music-reactive glow, and a living background drawn entirely by code. Experimental.", 12, true));
            var toggle = new CheckBox { Name = "BetaThemeToggle", Content = "Use the Beta Theme", IsChecked = Data.BetaTheme, Margin = new Thickness(0, 10, 0, 6) };
            var sounds = new CheckBox { Name = "BetaSoundsToggle", Content = "Menu sounds (quiet, made by code; softer while music plays)", IsChecked = Data.BetaSounds != false, IsEnabled = Data.BetaTheme, Margin = new Thickness(0, 0, 0, 4) };
            panel.Children.Add(toggle); panel.Children.Add(sounds);
            var depthLabel = SettingsText("3D depth", 12); depthLabel.Margin = new Thickness(0, 10, 0, 4); panel.Children.Add(depthLabel);
            var quality = new ComboBox { Name = "BetaQualityChoice", HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 240 };
            foreach (string option in new[] { "Flat (no 3D)", "Balanced 3D (recommended)", "Ultra 3D" }) quality.Items.Add(option);
            quality.SelectedIndex = Data.BetaQuality == "Flat" ? 0 : Data.BetaQuality == "Ultra" ? 2 : 1; panel.Children.Add(quality);
            panel.Children.Add(SettingsText(HardwareFor3D ? "Balanced: glass crystals, a floating 3D album cover, and pages that swing in 3D. Ultra adds more crystals and an extra light." : "This PC's graphics cannot run the 3D layer smoothly, so the Beta Theme stays flat here.", 11, true));
            quality.SelectionChanged += delegate { SetBetaQuality(quality.SelectedIndex == 0 ? "Flat" : quality.SelectedIndex == 2 ? "Ultra" : null); };
            var guard = new CheckBox { Name = "BetaAutoQualityToggle", Content = "Lower 3D depth automatically if Digitone starts to stutter", IsChecked = !Data.BetaAutoQualityOff, Margin = new Thickness(0, 8, 0, 2) };
            guard.Click += delegate { Data.BetaAutoQualityOff = guard.IsChecked != true; Save(); }; panel.Children.Add(guard);
            var flashes = new CheckBox { Name = "VisualizerFlashesToggle", Content = "Flashing lights in the visualizers", IsChecked = !Data.VisualizerFlashesOff, Margin = new Thickness(0, 4, 0, 2), ToolTip = "Kicks and drops flash the stage, at most about 3 times a second. Turn off for gentle lighting if flashing lights bother you." };
            flashes.Click += delegate { Data.VisualizerFlashesOff = flashes.IsChecked != true; Save(); }; panel.Children.Add(flashes);
            panel.Children.Add(SettingsText(SystemParameters.ClientAreaAnimation ? "Turning it off puts everything back exactly as it was." : "Windows animations are turned off, so Beta keeps its look but stays still.", 11, true));
            toggle.Click += delegate { SetBeta(toggle.IsChecked == true); sounds.IsEnabled = toggle.IsChecked == true; };
            sounds.Click += delegate { SetBetaSounds(sounds.IsChecked == true); };
            return panel;
        }
    }
}
