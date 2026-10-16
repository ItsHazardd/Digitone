using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Digitone
{
    // Scale + skew + move on one element, shared by Motion and the Beta Theme so neither overwrites the other.
    internal sealed class MotionParts
    {
        internal readonly ScaleTransform Scale = new ScaleTransform(1, 1);
        internal readonly SkewTransform Skew = new SkewTransform(0, 0);
        internal TranslateTransform Move = new TranslateTransform();
    }

    // Ticks once per screen refresh (WPF's own frame callback), or no more often than Interval when one is set.
    // Replaces a DispatcherTimer: Windows timers move in ~15.6 ms steps, so "every 33 ms" really ran every ~47 ms (about 21 fps).
    internal sealed class FrameClock
    {
        private bool enabled; private TimeSpan lastFrame = TimeSpan.Zero, lastTick = TimeSpan.Zero;
        internal TimeSpan Interval = TimeSpan.Zero;
        internal event Action Tick;
        internal bool IsEnabled { get { return enabled; } }
        internal void Start() { if (enabled) return; enabled = true; lastTick = TimeSpan.Zero; CompositionTarget.Rendering += OnFrame; }
        internal void Stop() { if (!enabled) return; enabled = false; CompositionTarget.Rendering -= OnFrame; }
        private void OnFrame(object sender, EventArgs e)
        {
            var frame = e as RenderingEventArgs; if (frame == null || frame.RenderingTime == lastFrame) return; // WPF can call twice for one frame
            lastFrame = frame.RenderingTime;
            if (Interval > TimeSpan.Zero && lastTick != TimeSpan.Zero && lastFrame - lastTick < Interval - TimeSpan.FromMilliseconds(2)) return;
            lastTick = lastFrame; var tick = Tick; if (tick != null) tick();
        }
    }

    internal static class BetaMotion
    {
        // Smoothing was tuned per tick of the old ~21.3 fps timer. These keep the exact same feel at any frame rate.
        internal const double TuningRate = 1000 / 46.875;
        internal static double Ease(double perTick, double seconds) { return 1 - Math.Pow(1 - perTick, seconds * TuningRate); }
        internal static double Decay(double perTick, double seconds) { return Math.Pow(perTick, seconds * TuningRate); }
        private static readonly ConditionalWeakTable<UIElement, MotionParts> parts = new ConditionalWeakTable<UIElement, MotionParts>();
        internal static bool Animate { get { return SystemParameters.ClientAreaAnimation; } }

        internal static MotionParts Parts(UIElement element)
        {
            MotionParts found;
            if (parts.TryGetValue(element, out found)) return found;
            found = new MotionParts();
            var existing = element.RenderTransform as TranslateTransform;
            if (existing != null) found.Move = existing;
            var group = new TransformGroup();
            var other = element.RenderTransform; if (other != null && existing == null && !other.Value.IsIdentity) group.Children.Add(other);
            group.Children.Add(found.Scale); group.Children.Add(found.Skew); group.Children.Add(found.Move);
            element.RenderTransform = group; element.RenderTransformOrigin = new Point(.5, .5);
            parts.Add(element, found);
            return found;
        }
        internal static bool Has(UIElement element) { MotionParts found; return parts.TryGetValue(element, out found); }

        internal static IEasingFunction Back(double amplitude) { return new BackEase { Amplitude = amplitude, EasingMode = EasingMode.EaseOut }; }
        internal static IEasingFunction Elastic() { return new ElasticEase { Oscillations = 2, Springiness = 7, EasingMode = EasingMode.EaseOut }; }
        internal static IEasingFunction Snap() { return new CubicEase { EasingMode = EasingMode.EaseOut }; }

        // Animates a property, or jumps straight there when Windows animations are turned off.
        internal static void To(Animatable target, DependencyProperty property, double to, int ms, IEasingFunction ease = null, int delayMs = 0, double? from = null)
        {
            if (!Animate) { target.BeginAnimation(property, null); target.SetValue(property, to); return; }
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Snap(), BeginTime = TimeSpan.FromMilliseconds(delayMs) };
            if (from.HasValue) animation.From = from.Value;
            target.BeginAnimation(property, animation);
        }
        internal static void To(UIElement target, DependencyProperty property, double to, int ms, IEasingFunction ease = null, int delayMs = 0, double? from = null)
        {
            if (!Animate) { target.BeginAnimation(property, null); target.SetValue(property, to); return; }
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Snap(), BeginTime = TimeSpan.FromMilliseconds(delayMs) };
            if (from.HasValue) animation.From = from.Value;
            target.BeginAnimation(property, animation);
        }
        internal static void ScaleTo(UIElement element, double scale, int ms, IEasingFunction ease = null, int delayMs = 0)
        {
            var p = Parts(element);
            To(p.Scale, ScaleTransform.ScaleXProperty, scale, ms, ease, delayMs); To(p.Scale, ScaleTransform.ScaleYProperty, scale, ms, ease, delayMs);
        }
        // Quick squash, then a springy return: used for presses.
        internal static void Punch(UIElement element, double squash)
        {
            if (!Animate) return;
            var p = Parts(element);
            foreach (var property in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
            {
                var frames = new DoubleAnimationUsingKeyFrames();
                frames.KeyFrames.Add(new EasingDoubleKeyFrame(squash, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70)), Snap()));
                frames.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520)), Elastic()));
                p.Scale.BeginAnimation(property, frames);
            }
        }
        // Slides, skews, and fades an element in with a little overshoot.
        internal static void Arrive(UIElement element, double fromX, double fromY, double fromSkew, double fromScale, int ms, int delayMs, double overshoot)
        {
            var p = Parts(element);
            if (!Animate) { Reset(element); return; }
            To(p.Move, TranslateTransform.XProperty, 0, ms, Back(overshoot), delayMs, fromX);
            To(p.Move, TranslateTransform.YProperty, 0, ms, Back(overshoot), delayMs, fromY);
            To(p.Skew, SkewTransform.AngleXProperty, 0, ms, Back(overshoot), delayMs, fromSkew);
            To(p.Scale, ScaleTransform.ScaleXProperty, 1, ms, Back(overshoot), delayMs, fromScale);
            To(p.Scale, ScaleTransform.ScaleYProperty, 1, ms, Back(overshoot), delayMs, fromScale);
            if (delayMs > 0) element.Opacity = 0;
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(Math.Min(ms, 220))) { BeginTime = TimeSpan.FromMilliseconds(delayMs), FillBehavior = FillBehavior.Stop });
            if (delayMs > 0) { var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs + 5) }; timer.Tick += delegate { timer.Stop(); element.Opacity = 1; }; timer.Start(); }
        }
        // Puts an element back exactly as it was before any Beta motion.
        internal static void Reset(UIElement element)
        {
            if (!Has(element)) return;
            var p = Parts(element);
            p.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); p.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); p.Scale.ScaleX = 1; p.Scale.ScaleY = 1;
            p.Skew.BeginAnimation(SkewTransform.AngleXProperty, null); p.Skew.BeginAnimation(SkewTransform.AngleYProperty, null); p.Skew.AngleX = 0; p.Skew.AngleY = 0;
            p.Move.BeginAnimation(TranslateTransform.XProperty, null); p.Move.BeginAnimation(TranslateTransform.YProperty, null); p.Move.X = 0; p.Move.Y = 0;
            element.BeginAnimation(UIElement.OpacityProperty, null);
        }
    }

    // Small menu sounds made by code (no recordings): quiet, throttled, and quieter still while music plays.
    internal static class UiSounds
    {
        internal static bool Enabled;
        internal static Func<bool> MusicPlaying = delegate { return false; };
        internal const int Rate = 44100;
        private static readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
        private static readonly Queue<SoundPlayer> alive = new Queue<SoundPlayer>();
        private static DateTime last = DateTime.MinValue; private static string lastName;
        internal static readonly string[] Names = { "tick", "select", "back", "whoosh", "pop", "confirm" };

        internal static byte[] Make(string name, bool quiet)
        {
            double gain = quiet ? .07 : .16; var samples = new List<double>(); var random = new Random(7);
            Action<double, double, double, double> tone = delegate(double startHz, double endHz, double ms, double level)
            {
                int count = (int)(Rate * ms / 1000); double phase = 0;
                for (int i = 0; i < count; i++)
                {
                    double t = i / (double)count, hz = startHz + (endHz - startHz) * t; phase += 2 * Math.PI * hz / Rate;
                    double envelope = Math.Min(1, i / (Rate * .003)) * Math.Exp(-4.2 * t);
                    samples.Add(level * envelope * (Math.Sin(phase) + .22 * Math.Sin(2 * phase)));
                }
            };
            switch (name)
            {
                case "tick": tone(2600, 2300, 26, 1); break;
                case "select": tone(900, 1650, 70, 1); break;
                case "back": tone(1400, 700, 70, 1); break;
                case "pop": tone(520, 760, 90, 1); break;
                case "confirm": tone(880, 880, 45, .9); tone(1109, 1109, 45, .9); tone(1319, 1319, 80, 1); break;
                case "whoosh":
                    {
                        int count = (int)(Rate * .19); double low = 0;
                        for (int i = 0; i < count; i++)
                        {
                            double t = i / (double)count, cutoff = .04 + .5 * Math.Sin(Math.PI * t) * Math.Sin(Math.PI * t);
                            low += cutoff * ((random.NextDouble() * 2 - 1) - low);
                            samples.Add(1.6 * low * Math.Sin(Math.PI * t));
                        }
                        break;
                    }
                default: throw new ArgumentException("Unknown sound " + name);
            }
            double loudest = samples.Count == 0 ? 1 : Math.Max(1e-6, samples.Max(s => Math.Abs(s)));
            for (int i = 0; i < samples.Count; i++) samples[i] /= loudest; // every sound peaks at exactly the chosen volume
            int fade = Math.Min(samples.Count, (int)(Rate * .004));
            for (int i = 0; i < fade; i++) samples[samples.Count - 1 - i] *= i / (double)fade;
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                int bytes = samples.Count * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' }); writer.Write(36 + bytes); writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(Rate); writer.Write(Rate * 2); writer.Write((short)2); writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' }); writer.Write(bytes);
                foreach (double sample in samples) writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, sample * gain)) * 32767));
                writer.Flush(); return stream.ToArray();
            }
        }
        internal static void Play(string name)
        {
            if (!Enabled) return;
            var now = DateTime.UtcNow;
            if ((now - last).TotalMilliseconds < (name == lastName ? 55 : 25)) return;
            last = now; lastName = name;
            try
            {
                bool quiet = MusicPlaying(); string key = name + (quiet ? "-quiet" : "");
                byte[] wave; if (!cache.TryGetValue(key, out wave)) cache[key] = wave = Make(name, quiet);
                var player = new SoundPlayer(new MemoryStream(wave)); player.Play();
                alive.Enqueue(player); while (alive.Count > 4) alive.Dequeue().Dispose();
            }
            catch { }
        }
    }
}
