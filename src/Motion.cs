using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Digitone
{
    internal static class Motion
    {
        internal static void Enter(FrameworkElement element, double x, double y)
        {
            if (!SystemParameters.ClientAreaAnimation || !element.IsLoaded) return;
            var move = BetaMotion.Parts(element).Move;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            move.X=x; move.Y=y;
            move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd });
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(230)) { FillBehavior = FillBehavior.Stop });
        }
        internal static void WireButtons(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i); var button = child as Button;
                if (button != null)
                {
                    var move = new TranslateTransform(); button.RenderTransform = move;
                    Action<double> shift = delegate(double x) { if (SystemParameters.ClientAreaAnimation) move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(130)) { EasingFunction = new QuadraticEase() }); };
                    button.MouseEnter += delegate { if (button.IsEnabled) shift(3); };
                    button.MouseLeave += delegate { shift(0); };
                    button.IsEnabledChanged += delegate { if (!button.IsEnabled) shift(0); };
                }
                else WireButtons(child);
            }
        }
    }
    public sealed partial class PlayerApp
    {
        private readonly System.Collections.Generic.List<AnimationClock> ambientClocks = new System.Collections.Generic.List<AnimationClock>();
        private readonly System.Collections.Generic.List<AnimationClock> themeClocks = new System.Collections.Generic.List<AnimationClock>();
        private void AnimateTheme(System.Windows.Media.Animation.IAnimatable target,DependencyProperty property,double from,double to,double seconds)
        {
            if(!SystemParameters.ClientAreaAnimation) return;
            var animation=new DoubleAnimation(from,to,TimeSpan.FromSeconds(seconds)) { AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever,EasingFunction=new SineEase() };
            var clock=(AnimationClock)animation.CreateClock(true); target.ApplyAnimationClock(property,clock); themeClocks.Add(clock);
        }
        private void StartAmbientClock(System.Windows.Media.Animation.IAnimatable target, DependencyProperty property, DoubleAnimation animation)
        {
            var clock = (AnimationClock)animation.CreateClock(true); target.ApplyAnimationClock(property,clock); ambientClocks.Add(clock);
        }
        // Page change: the page that changed rises a little and fades in as one piece, with a soft landing (no bounce).
        // During the glide the page is a ready-made picture the graphics card slides, so it stays locked to the screen's refresh rate;
        // it starts only after the new page is laid out, so no frames are skipped. Panels that did not change stay still.
        private readonly System.Collections.Generic.Dictionary<FrameworkElement, int> glides = new System.Collections.Generic.Dictionary<FrameworkElement, int>();
        internal int PageGlideCount { get; private set; }
        internal const double GlideRise = 18; internal const int GlideMs = 420, GlideFadeMs = 260;
        private void PageGlide(params FrameworkElement[] panes)
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            bool depth = BetaActive;
            foreach (var pane in panes)
            {
                if (pane == null || pane.Visibility != Visibility.Visible) continue;
                int version; glides.TryGetValue(pane, out version); glides[pane] = ++version; int mine = version; var target = pane;
                var parts = BetaMotion.Parts(pane);
                pane.BeginAnimation(UIElement.OpacityProperty, null); parts.Move.BeginAnimation(TranslateTransform.XProperty, null); parts.Move.BeginAnimation(TranslateTransform.YProperty, null);
                parts.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); parts.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                pane.Opacity = 0; parts.Move.X = 0; parts.Move.Y = GlideRise; // set before the next frame is drawn, so the new page never flashes in early
                if (depth) { pane.RenderTransformOrigin = new Point(.5, .35); parts.Scale.ScaleX = parts.Scale.ScaleY = .985; }
                pane.CacheMode = new BitmapCache();
                Window.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (glides[target] != mine) return;
                    var land = new QuinticEase { EasingMode = EasingMode.EaseOut };
                    var move = new DoubleAnimation(GlideRise, 0, TimeSpan.FromMilliseconds(GlideMs)) { EasingFunction = land };
                    move.Completed += delegate
                    {
                        if (glides[target] != mine) return;
                        var settled = BetaMotion.Parts(target); target.CacheMode = null;
                        target.BeginAnimation(UIElement.OpacityProperty, null); target.Opacity = 1;
                        settled.Move.BeginAnimation(TranslateTransform.YProperty, null); settled.Move.Y = 0;
                        settled.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); settled.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); settled.Scale.ScaleX = settled.Scale.ScaleY = 1;
                    };
                    target.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(GlideFadeMs)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    BetaMotion.Parts(target).Move.BeginAnimation(TranslateTransform.YProperty, move);
                    if (depth) { var grow = new DoubleAnimation(.985, 1, TimeSpan.FromMilliseconds(GlideMs)) { EasingFunction = land }; BetaMotion.Parts(target).Scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow); BetaMotion.Parts(target).Scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow); }
                    PageGlideCount++;
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }
        private void SetAmbientVisibility()
        {
            bool hidden = Find<Grid>("ExpandedView").Visibility == Visibility.Visible;
            foreach(var clock in ambientClocks) { if(hidden) clock.Controller.Pause(); else clock.Controller.Resume(); }
            foreach(var clock in themeClocks) { if(hidden) clock.Controller.Pause(); else clock.Controller.Resume(); }
        }
        private void SetupMotion()
        {
            // The two background rings only rotate as a whole, so each is drawn once (at double resolution, to stay crisp while turning) and reused.
            foreach (string ring in new[] { "AmbientRing", "AmbientOrbit" }) Find<System.Windows.Shapes.Ellipse>(ring).CacheMode = new BitmapCache { RenderAtScale = 2 };
            Window.Loaded += delegate { UpdateAmbientMotion(); Motion.WireButtons(Window); Motion.Enter(Find<Border>("NowPane"), -24, 0); Motion.Enter(Find<Grid>("LibraryPane"), 32, 0); };
            Find<Canvas>("AmbientCanvas").SizeChanged += delegate { LayoutAmbient(); };
            Window.Closed += delegate { foreach(var clock in ambientClocks) clock.Controller.Remove(); ambientClocks.Clear(); };
            Window.Closed += delegate { foreach(var clock in themeClocks) clock.Controller.Remove(); themeClocks.Clear(); };
            Find<Border>("QueueDrawer").IsVisibleChanged += delegate { if (Find<Border>("QueueDrawer").IsVisible) Motion.Enter(Find<Border>("QueueDrawer"), 48, 0); };
            Find<ListBox>("Tracks").SelectionChanged += delegate { var list = Find<ListBox>("Tracks"); if (list.SelectedItem == null) return; var item = list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) as ListBoxItem; if (item != null) Motion.Enter(item, -10, 0); };
        }
        private void UpdateAmbientMotion()
        {
            var dots = Find<System.Windows.Shapes.Line>("AmbientDots");
            foreach(var clock in ambientClocks) clock.Controller.Remove(); ambientClocks.Clear();
            if (SystemParameters.ClientAreaAnimation) StartAmbientClock(dots,System.Windows.Shapes.Shape.StrokeDashOffsetProperty, new DoubleAnimation(0,20,TimeSpan.FromSeconds(8)) { RepeatBehavior = RepeatBehavior.Forever });
            if (SystemParameters.ClientAreaAnimation) foreach (string name in new[] { "AmbientShard", "AmbientSteps" }) StartAmbientClock((TranslateTransform)Find<System.Windows.Shapes.Shape>(name).RenderTransform,TranslateTransform.YProperty,new DoubleAnimation(-8,8,TimeSpan.FromSeconds(name == "AmbientShard" ? 14 : 19)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            foreach (string name in new[] { "AmbientRing", "AmbientOrbit" })
            {
                var rotation = (RotateTransform)Find<System.Windows.Shapes.Ellipse>(name).RenderTransform;
                if (SystemParameters.ClientAreaAnimation && name=="AmbientRing")
                    StartAmbientClock(rotation,RotateTransform.AngleProperty, new DoubleAnimation(0, name == "AmbientRing" ? 360 : -360, TimeSpan.FromSeconds(name == "AmbientRing" ? 70 : 42)) { RepeatBehavior = RepeatBehavior.Forever });
                else if(name=="AmbientRing") rotation.BeginAnimation(RotateTransform.AngleProperty, null);
            }
            var orbitTurn = (RotateTransform)Find<System.Windows.Shapes.Ellipse>("AmbientOrbit").RenderTransform;
            if (SystemParameters.ClientAreaAnimation) StartAmbientClock(orbitTurn, RotateTransform.AngleProperty, new DoubleAnimation(0, -360, TimeSpan.FromSeconds(60.3)) { RepeatBehavior = RepeatBehavior.Forever });
            SetAmbientVisibility();
        }
        private void LayoutAmbient()
        {
            var canvas = Find<Canvas>("AmbientCanvas"); double w = canvas.ActualWidth, h = canvas.ActualHeight;
            double size = Math.Min(680,Math.Max(300,Math.Min(w,h)*1.05));
            var ring = Find<System.Windows.Shapes.Ellipse>("AmbientRing"); ring.Width = ring.Height = size; Canvas.SetLeft(ring,-size*.14); Canvas.SetTop(ring,20);
            var orbit = Find<System.Windows.Shapes.Ellipse>("AmbientOrbit"); orbit.Width = orbit.Height = size*.74; Canvas.SetLeft(orbit,w-size*.53); Canvas.SetTop(orbit,h-size*.55);
            Find<System.Windows.Shapes.Polygon>("AmbientBand").Points = new PointCollection { new Point(0,h*.66),new Point(w,h*.07),new Point(w,h*.17),new Point(0,h*.8) };
            var dots = Find<System.Windows.Shapes.Line>("AmbientDots"); dots.X1 = w*.64; dots.Y1 = 12; dots.X2 = w*.91; dots.Y2 = h-12;
            bool tall = h > w;
            var shard = Find<System.Windows.Shapes.Polygon>("AmbientShard"); shard.Visibility = tall ? Visibility.Visible : Visibility.Collapsed;
            shard.Points = new PointCollection { new Point(w*.76,h*.21),new Point(w*.92,h*.29),new Point(w*.81,h*.40) };
            var steps = Find<System.Windows.Shapes.Polyline>("AmbientSteps"); steps.Visibility = tall ? Visibility.Visible : Visibility.Collapsed;
            steps.Points = new PointCollection { new Point(w*.03,h*.70),new Point(w*.12,h*.74),new Point(w*.05,h*.78),new Point(w*.14,h*.82) };
            LayoutThemeGeometry(canvas,w,h);
        }
        private void UpdateTransport()
        {
            bool available = current != null;
            Find<Button>("PlayButton").IsEnabled = !opening && (available || Find<ListBox>("Tracks").SelectedItem != null);
            foreach (string name in new[] { "PreviousButton", "NextButton", "ShuffleButton", "RepeatButton" }) Find<Button>(name).IsEnabled = available && !opening;
            Find<Slider>("Seek").IsEnabled = ready;
            Find<Button>("PlayButton").ToolTip = opening ? "Loading audio…" : available ? "Play / pause (Space)" : "Select a song to play";
        }
    }
}
