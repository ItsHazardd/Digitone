using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Digitone
{
    // The expanded visualizers in Beta 3D, set up like a rave stage: lights move on the beat (from RaveClock), in symmetric
    // patterns that change per bar and phrase, colors change every 8 bars, the bass is always obvious, and the big moments are
    // saved for the drop. Laser Show, LED Wall, Halo, Moving Heads, Drop (stored as Waveform, Spectrum, Radial, Prism, Pulse).
    internal sealed class Visualizer3D
    {
        internal static readonly string[] StageNames = { "Laser Show", "LED Wall", "Halo", "Moving Heads", "Drop" };
        internal static string StageName(string mode) { int i = Array.IndexOf(PlayerApp.VisualizerModes, mode); return i < 0 ? mode : StageNames[i]; }

        internal readonly Viewport3D View = new Viewport3D { IsHitTestVisible = false, ClipToBounds = true, Visibility = Visibility.Collapsed };
        private readonly PerspectiveCamera camera = new PerspectiveCamera { FieldOfView = 50, NearPlaneDistance = .5, FarPlaneDistance = 90 };
        internal string Mode { get; private set; }
        internal string Theme { get; private set; }
        internal int Flashes { get; private set; } // bright flashes shown (the limiter keeps them at most ~3 a second)
        internal int Sparks { get { return particles.Count; } } // confetti in the air (tests)

        private Color accent, deep, C0, C1, C2; private bool pride; private int paletteFor = -1;
        private readonly List<Action> recolor = new List<Action>();
        private double time, kickGain = 1, kickVis, dropVis, lastFlash = -10, push, fovPunch;
        private RaveClock rave; private bool flashesOn = true;
        private PointLight stageLight; private RadialGradientBrush floorGlow, haze;

        // ---------- small helpers ----------
        private sealed class Mesh
        {
            private readonly Point3DCollection points = new Point3DCollection(); private readonly Int32Collection indices = new Int32Collection(); private readonly PointCollection uv = new PointCollection();
            internal void Quad(Point3D a, Point3D b, Point3D c, Point3D d)
            {
                int i = points.Count; points.Add(a); points.Add(b); points.Add(c); points.Add(d);
                uv.Add(new Point(0, 1)); uv.Add(new Point(1, 1)); uv.Add(new Point(1, 0)); uv.Add(new Point(0, 0));
                indices.Add(i); indices.Add(i + 1); indices.Add(i + 2); indices.Add(i); indices.Add(i + 2); indices.Add(i + 3);
            }
            internal MeshGeometry3D Done() { var m = new MeshGeometry3D { Positions = points, TriangleIndices = indices, TextureCoordinates = uv }; m.Freeze(); return m; }
        }
        private static Color Alpha(Color c, double a) { return Shapes3D.Alpha(c, a); }
        private static double Ease(double seconds, double tau) { return 1 - Math.Exp(-seconds / tau); }
        private static Color Hue(Color c, double degrees)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0, max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), h = 0, s = max <= 0 ? 0 : (max - min) / max, v = max;
            if (max > min) { if (max == r) h = 60 * (((g - b) / (max - min)) % 6); else if (max == g) h = 60 * ((b - r) / (max - min) + 2); else h = 60 * ((r - g) / (max - min) + 4); }
            h = ((h + degrees) % 360 + 360) % 360; s = Math.Max(s, .55); v = Math.Max(v, .75);
            double cc = v * s, x = cc * (1 - Math.Abs((h / 60) % 2 - 1)), m2 = v - cc; double[] rgb = h < 60 ? new[] { cc, x, 0 } : h < 120 ? new[] { x, cc, 0 } : h < 180 ? new[] { 0, cc, x } : h < 240 ? new[] { 0, x, cc } : h < 300 ? new[] { x, 0, cc } : new[] { cc, 0, x };
            return Color.FromRgb((byte)((rgb[0] + m2) * 255), (byte)((rgb[1] + m2) * 255), (byte)((rgb[2] + m2) * 255));
        }
        // A glowing, light-adding material (lasers, beams, LEDs): its color follows the palette role, its strength is set per frame.
        private SolidColorBrush Glow(int role, out GeometryModel3D model)
        {
            var brush = new SolidColorBrush(Colors.White); recolor.Add(delegate { brush.Color = role == 0 ? C0 : role == 1 ? C1 : role == 2 ? C2 : Colors.White; });
            var material = new EmissiveMaterial(brush); model = new GeometryModel3D(new MeshGeometry3D(), material) { BackMaterial = material }; return brush;
        }
        private LinearGradientBrush Soft(int role, bool across, out GeometryModel3D model)
        {
            var brush = across ? new LinearGradientBrush { StartPoint = new Point(0, .5), EndPoint = new Point(1, .5) } : new LinearGradientBrush { StartPoint = new Point(.5, 0), EndPoint = new Point(.5, 1) };
            brush.GradientStops.Add(new GradientStop(Colors.Transparent, 0)); brush.GradientStops.Add(new GradientStop(Colors.White, .5)); brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            recolor.Add(delegate { var c = role == 0 ? C0 : role == 1 ? C1 : C2; brush.GradientStops[0].Color = Alpha(c, 0); brush.GradientStops[1].Color = Alpha(c, .9); brush.GradientStops[2].Color = Alpha(c, 0); });
            var material = new EmissiveMaterial(brush); model = new GeometryModel3D(new MeshGeometry3D(), material) { BackMaterial = material }; return brush;
        }
        private LinearGradientBrush Fade(int role, out GeometryModel3D model)
        {
            var brush = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) }; brush.GradientStops.Add(new GradientStop(Colors.White, 0)); brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            recolor.Add(delegate { var c = role == 0 ? C0 : C1; brush.GradientStops[0].Color = Alpha(c, .95); brush.GradientStops[1].Color = Alpha(c, .04); });
            var material = new EmissiveMaterial(brush); model = new GeometryModel3D(new MeshGeometry3D(), material) { BackMaterial = material }; return brush;
        }
        private RadialGradientBrush Pool(int role)
        {
            var brush = new RadialGradientBrush(); brush.GradientStops.Add(new GradientStop(Colors.White, 0)); brush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            recolor.Add(delegate { var c = role == 0 ? C0 : role == 1 ? C1 : C2; brush.GradientStops[0].Color = Alpha(c, .9); brush.GradientStops[1].Color = Alpha(c, 0); });
            return brush;
        }
        private static MeshGeometry3D Flat(Point3D a, Point3D b, Point3D c, Point3D d) { var m = new Mesh(); m.Quad(a, b, c, d); return m.Done(); }
        private static void Beam(Mesh mesh, Point3D from, double degrees, double length, double startWidth, double endWidth)
        {
            double a = degrees * Math.PI / 180; var dir = new Vector3D(Math.Cos(a), Math.Sin(a), 0); var n = new Vector3D(-Math.Sin(a), Math.Cos(a), 0); var end = from + dir * length;
            mesh.Quad(from - n * startWidth / 2, from + n * startWidth / 2, end + n * endWidth / 2, end - n * endWidth / 2);
        }
        private static void Square(Mesh mesh, double x, double y, double z, double size) { double h = size / 2; mesh.Quad(new Point3D(x - h, y - h, z), new Point3D(x + h, y - h, z), new Point3D(x + h, y + h, z), new Point3D(x - h, y + h, z)); }

        // ---------- the shared stage ----------
        internal void Build(string mode, Color accentColor, Color deepColor, bool light, string theme)
        {
            Mode = mode; Theme = theme; accent = accentColor; deep = deepColor; pride = theme == "Pride"; paletteFor = -1; recolor.Clear();
            View.Children.Clear(); View.Camera = camera; laserAngles = new double[14]; heads = new Point[8]; ledLevels = new double[24]; ledPeaks = new double[24]; ledPeakHold = new double[24]; ledMax = new double[24];
            ringLevels = new double[6]; pulses.Clear(); particles.Clear(); barAngle = 0;
            var root = new Model3DGroup(); var late = new Model3DGroup();
            root.Children.Add(new AmbientLight(Color.FromRgb(22, 22, 30)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(60, 60, 75), new Vector3D(-.2, -1, -.4)));
            stageLight = new PointLight(Colors.White, new Point3D(0, 5, 3)) { Range = 30, ConstantAttenuation = .9, LinearAttenuation = .05 }; root.Children.Add(stageLight);
            // No visible edges or lines anywhere: the haze is a huge backdrop that fades out long before its own edges, and the floor
            // fades away into the distance so the haze shows through behind it (haze first, then the fading floor on top of it).
            haze = Pool(1); haze.Center = haze.GradientOrigin = new Point(.5, .97); haze.RadiusX = .22; haze.RadiusY = .45;
            root.Children.Add(new GeometryModel3D(Flat(new Point3D(-90, -6, -30), new Point3D(90, -6, -30), new Point3D(90, 60, -30), new Point3D(-90, 60, -30)), new EmissiveMaterial(haze)));
            Color floorColor = Shapes3D.Mix(Colors.Black, deep, .35);
            var floorFade = new LinearGradientBrush { StartPoint = new Point(.5, 1), EndPoint = new Point(.5, 0) }; floorFade.GradientStops.Add(new GradientStop(floorColor, 0)); floorFade.GradientStops.Add(new GradientStop(floorColor, .3)); floorFade.GradientStops.Add(new GradientStop(Alpha(floorColor, 0), .62));
            var shineFade = new LinearGradientBrush { StartPoint = new Point(.5, 1), EndPoint = new Point(.5, 0) }; shineFade.GradientStops.Add(new GradientStop(Alpha(Colors.White, .35), 0)); shineFade.GradientStops.Add(new GradientStop(Alpha(Colors.White, 0), .55));
            var floorMaterial = new MaterialGroup(); floorMaterial.Children.Add(new DiffuseMaterial(floorFade)); floorMaterial.Children.Add(new SpecularMaterial(shineFade, 25));
            root.Children.Add(new GeometryModel3D(Flat(new Point3D(-80, -2, 12.4), new Point3D(80, -2, 12.4), new Point3D(80, -2, -29), new Point3D(-80, -2, -29)), floorMaterial));
            floorGlow = Pool(0); late.Children.Add(new GeometryModel3D(Flat(new Point3D(-8, -1.99, 3.5), new Point3D(8, -1.99, 3.5), new Point3D(8, -1.99, -4), new Point3D(-8, -1.99, -4)), new EmissiveMaterial(floorGlow)));
            if (mode == "Spectrum") BuildWall(root, late); else if (mode == "Radial") BuildHalo(root, late); else if (mode == "Prism") BuildHeads(root, late); else if (mode == "Pulse") BuildDrop(root, late); else BuildLasers(root, late);
            root.Children.Add(late); View.Children.Add(new ModelVisual3D { Content = root });
            Palette(0);
        }
        private void Palette(int phrase)
        {
            if (phrase == paletteFor) return; paletteFor = phrase;
            Color[] set = pride ? new[] { ThemeShapes.Rainbow(0), ThemeShapes.Rainbow(1 / 6.0), ThemeShapes.Rainbow(2 / 6.0), ThemeShapes.Rainbow(3 / 6.0), ThemeShapes.Rainbow(4 / 6.0), ThemeShapes.Rainbow(5 / 6.0) } : new[] { Hue(accent, 0), Hue(accent, 38), Hue(accent, -38) };
            int k = phrase % set.Length; C0 = set[k]; C1 = set[(k + 1) % set.Length]; C2 = Shapes3D.Mix(set[(k + 2) % set.Length], Colors.White, .45);
            foreach (var apply in recolor) apply(); stageLight.Color = C0;
        }

        internal void Step(double seconds, double[] wave, double energy, RaveClock clock, bool flashing)
        {
            rave = clock ?? new RaveClock(); flashesOn = flashing; seconds = Math.Max(0, Math.Min(.1, seconds)); time += seconds;
            Palette(rave.Phrase);
            // Flash limiter: kicks only flash brightly if the last bright flash was over a third of a second ago (and never with flashes off).
            if (rave.KickNow) { bool allowed = flashesOn && time - lastFlash >= .34; kickGain = allowed ? 1 : .5; if (allowed) { lastFlash = time; Flashes++; } }
            kickVis = rave.Kick * kickGain * (flashesOn ? 1 : .45); dropVis = rave.Drop * (flashesOn ? 1 : .35);
            // Camera: steady, a slow push-in through each phrase, and a small punch on kicks.
            double target = ((rave.Beat % 32) + rave.BeatPhase) / 32; push += (target - push) * Ease(seconds, 1.2);
            double aspect = View.ActualWidth > 1 && View.ActualHeight > 1 ? View.ActualWidth / View.ActualHeight : 2.2, distance = 13 - .7 * push, halfWidth = Math.Max(6.8, 3.15 * aspect);
            fovPunch += (kickVis - fovPunch) * Ease(seconds, kickVis > fovPunch ? .02 : .12);
            camera.Position = new Point3D(0, 1, distance); camera.LookDirection = new Vector3D(0, 0, -distance); camera.UpDirection = new Vector3D(0, 1, 0);
            camera.FieldOfView = 2 * Math.Atan(halfWidth / distance) * 180 / Math.PI * (1 - .03 * fovPunch);
            // Shared stage: floor flash on kicks, haze swelling with the sub-bass, the stage light pumping.
            floorGlow.Opacity = Math.Min(1, .1 + .5 * kickVis + .25 * rave.Sub + .6 * dropVis);
            haze.Opacity = Math.Min(1, .05 + .3 * rave.Sub + .08 * energy + .45 * dropVis);
            stageLight.ConstantAttenuation = Math.Max(.25, 1 - .55 * kickVis - .3 * dropVis);
            double beats = rave.Bpm > 0 ? rave.Beat + rave.BeatPhase : time * .8; // without a steady tempo, a calm 48 BPM drift
            if (Mode == "Spectrum") StepWall(seconds, wave); else if (Mode == "Radial") StepHalo(seconds, wave); else if (Mode == "Prism") StepHeads(seconds, beats); else if (Mode == "Pulse") StepDrop(seconds); else StepLasers(seconds, wave, beats);
        }

        // ---------- 1. Laser Show ----------
        private double[] laserAngles = new double[14];
        private GeometryModel3D laserCoreA, laserGlowA, laserCoreB, laserGlowB, lineCore, lineGlow; private SolidColorBrush laserCoreABrush, laserCoreBBrush, lineCoreBrush; private LinearGradientBrush laserGlowABrush, laserGlowBBrush, lineGlowBrush;
        private void BuildLasers(Model3DGroup root, Model3DGroup late)
        {
            laserGlowABrush = Soft(0, true, out laserGlowA); laserGlowBBrush = Soft(1, true, out laserGlowB); laserCoreABrush = Glow(0, out laserCoreA); laserCoreBBrush = Glow(1, out laserCoreB); lineGlowBrush = Soft(2, false, out lineGlow); lineCoreBrush = Glow(2, out lineCore);
            foreach (var model in new[] { laserGlowA, laserGlowB, lineGlow, laserCoreA, laserCoreB, lineCore }) late.Children.Add(model);
            for (int i = 0; i < 7; i++) laserAngles[i] = 90;
        }
        private void StepLasers(double seconds, double[] wave, double beats)
        {
            int pattern = rave.Drop > .3 ? 4 : (rave.Bar + rave.Phrase) % 4; double center = 0; var target = new double[7];
            for (int i = 0; i < 7; i++)
            {
                double t = i / 6.0;
                target[i] = pattern == 0 ? 55 + t * 70 : pattern == 1 ? 38 + t * 34 : pattern == 2 ? 90 + 32 * Math.Sin(2 * Math.PI * beats / 2) + (i - 3) * 3 : pattern == 3 ? 58 + (i - 3) * 2.4 : 25 + t * 130;
                center += target[i] / 7;
            }
            double spread = 1 + .35 * kickVis, follow = Ease(seconds, .1);
            for (int i = 0; i < 7; i++) { double aim = center + (target[i] - center) * spread; laserAngles[i] += (aim - laserAngles[i]) * follow; }
            Mesh coreA = new Mesh(), glowA = new Mesh(), coreB = new Mesh(), glowB = new Mesh(), core = new Mesh(), glow = new Mesh();
            for (int k = 0; k < 14; k++)
            {
                bool left = k < 7; int i = left ? k : k - 7; double angle = left ? laserAngles[i] : 180 - laserAngles[i]; var from = new Point3D(left ? -4.2 : 4.2, -2, .6);
                var c = i % 2 == 0 ? coreA : coreB; var g = i % 2 == 0 ? glowA : glowB;
                Beam(c, from, angle, 16, .035, .05); Beam(g, from, angle, 16, .22, .9);
            }
            for (int j = 0; j < 12; j++) { var from = new Point3D(-5.5 + j, -2, 2.2); double length = .25 + 2.4 * kickVis + .9 * rave.Bass; Beam(coreB, from, 90, length, .03, .03); Beam(glowB, from, 90, length, .24, .36); } // floor lasers punch with the bass
            if (wave != null && wave.Length > 1)
            {
                // The live waveform as one horizontal laser line.
                Point3D previous = new Point3D(); for (int i = 0; i < 96; i++)
                {
                    double t = i / 95.0, x = -6.8 + 13.6 * t, y = 1.3 + wave[(int)(t * (wave.Length - 1))] * 3.2; var point = new Point3D(x, y, -1.4);
                    if (i > 0) { var d = point - previous; d.Normalize(); var n = new Vector3D(-d.Y, d.X, 0); core.Quad(previous - n * .02, point - n * .02, point + n * .02, previous + n * .02); glow.Quad(previous - n * .16, point - n * .16, point + n * .16, previous + n * .16); }
                    previous = point;
                }
            }
            laserCoreA.Geometry = coreA.Done(); laserGlowA.Geometry = glowA.Done(); laserCoreB.Geometry = coreB.Done(); laserGlowB.Geometry = glowB.Done(); lineCore.Geometry = core.Done(); lineGlow.Geometry = glow.Done();
            double coreOn = Math.Min(1, .5 + .5 * kickVis + .2 * rave.Sub + .3 * dropVis), glowOn = Math.Min(.8, .1 + .35 * kickVis + .12 * rave.Sub + .3 * dropVis);
            laserCoreABrush.Opacity = laserCoreBBrush.Opacity = coreOn; laserGlowABrush.Opacity = laserGlowBBrush.Opacity = glowOn; lineCoreBrush.Opacity = .9; lineGlowBrush.Opacity = .2 + .2 * kickVis;
        }

        // ---------- 2. LED Wall ----------
        private double[] ledLevels = new double[24], ledPeaks = new double[24], ledPeakHold = new double[24], ledMax = new double[24];
        private GeometryModel3D ledOff, ledLow, ledHigh, ledBass, ledPeak, ledMirror; private SolidColorBrush ledOffBrush, ledLowBrush, ledHighBrush, ledBassBrush, ledPeakBrush, ledMirrorBrush;
        private void BuildWall(Model3DGroup root, Model3DGroup late)
        {
            ledOffBrush = Glow(0, out ledOff); ledLowBrush = Glow(0, out ledLow); ledHighBrush = Glow(1, out ledHigh); ledBassBrush = Glow(2, out ledBass); ledPeakBrush = Glow(3, out ledPeak); ledMirrorBrush = Glow(0, out ledMirror);
            var frame = new MaterialGroup(); frame.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(14, 14, 18)))); frame.Children.Add(new SpecularMaterial(new SolidColorBrush(Alpha(Colors.White, .2)), 30));
            root.Children.Add(new GeometryModel3D(Flat(new Point3D(-6.2, -2, -3.05), new Point3D(6.2, -2, -3.05), new Point3D(6.2, 3.3, -3.05), new Point3D(-6.2, 3.3, -3.05)), frame));
            foreach (var model in new[] { ledMirror, ledOff, ledLow, ledHigh, ledBass, ledPeak }) late.Children.Add(model);
        }
        private void StepWall(double seconds, double[] wave)
        {
            var bands = Spectrum(wave, 24);
            for (int c = 0; c < 24; c++)
            {
                ledMax[c] = Math.Max(.02, Math.Max(bands[c], ledMax[c] * Math.Exp(-seconds / 6)));
                double level = wave == null ? 0 : Math.Min(1, Math.Sqrt(bands[c] / ledMax[c]));
                ledLevels[c] += (level - ledLevels[c]) * Ease(seconds, level > ledLevels[c] ? .05 : .22);
                if (ledLevels[c] >= ledPeaks[c]) { ledPeaks[c] = ledLevels[c]; ledPeakHold[c] = .35; } else if ((ledPeakHold[c] -= seconds) < 0) ledPeaks[c] = Math.Max(ledLevels[c], ledPeaks[c] - seconds * .7);
            }
            Mesh off = new Mesh(), low = new Mesh(), high = new Mesh(), bass = new Mesh(), peak = new Mesh(), mirror = new Mesh();
            double bassWidth = rave.Bass * 12.5;
            for (int c = 0; c < 24; c++)
            {
                int lit = (int)Math.Round(ledLevels[c] * 9), peakRow = 3 + Math.Min(8, (int)Math.Round(ledPeaks[c] * 9) - 1);
                for (int r = 0; r < 12; r++)
                {
                    double x0 = -6 + c * .5 + .03, y0 = -1.85 + r * .42 + .03; var a = new Point3D(x0, y0, -3); var b = new Point3D(x0 + .44, y0, -3); var cc = new Point3D(x0 + .44, y0 + .36, -3); var d = new Point3D(x0, y0 + .36, -3);
                    bool on; Mesh target;
                    if (r < 3) { on = Math.Abs(c - 11.5) < bassWidth; target = bass; } // the bottom three rows are the bass meter, growing from the middle
                    else { on = r - 3 < lit; target = r < 7 ? low : high; if (!on && r == peakRow && ledPeaks[c] > .08) { peak.Quad(a, b, cc, d); continue; } }
                    if (on) { target.Quad(a, b, cc, d); if (r < 6) mirror.Quad(new Point3D(a.X, -4 - a.Y, -3), new Point3D(b.X, -4 - b.Y, -3), new Point3D(cc.X, -4 - cc.Y, -3), new Point3D(d.X, -4 - d.Y, -3)); }
                    else off.Quad(a, b, cc, d);
                }
            }
            ledOff.Geometry = off.Done(); ledLow.Geometry = low.Done(); ledHigh.Geometry = high.Done(); ledBass.Geometry = bass.Done(); ledPeak.Geometry = peak.Done(); ledMirror.Geometry = mirror.Done();
            double thump = .55 + .45 * kickVis + .3 * dropVis;
            ledLowBrush.Opacity = ledHighBrush.Opacity = Math.Min(1, thump); ledBassBrush.Opacity = Math.Min(1, .6 + .4 * kickVis + .3 * dropVis); ledPeakBrush.Opacity = .85; ledOffBrush.Opacity = .05 + .07 * rave.Sub; ledMirrorBrush.Opacity = .08 + .1 * kickVis;
        }

        // ---------- 3. Halo ----------
        private double[] ringLevels = new double[6]; private double barAngle;
        private readonly List<SolidColorBrush> ringBrushes = new List<SolidColorBrush>();
        private ScaleTransform3D coreScale; private SolidColorBrush coreGlow; private AxisAngleRotation3D haloTurn;
        private sealed class Pulse { internal double Age = 9, Strength, Grow, Life; internal ScaleTransform3D Scale; internal SolidColorBrush Brush; }
        private readonly List<Pulse> pulses = new List<Pulse>();
        private void BuildHalo(Model3DGroup root, Model3DGroup late)
        {
            ringBrushes.Clear();
            coreScale = new ScaleTransform3D(1, 1, 1); coreGlow = new SolidColorBrush(Colors.White); recolor.Add(delegate { coreGlow.Color = C0; });
            var coreMaterial = new MaterialGroup(); coreMaterial.Children.Add(new DiffuseMaterial(new SolidColorBrush(Shapes3D.Mix(deep, Colors.Black, .4)))); coreMaterial.Children.Add(new SpecularMaterial(new SolidColorBrush(Alpha(Colors.White, .6)), 40)); coreMaterial.Children.Add(new EmissiveMaterial(coreGlow));
            var coreMove = new Transform3DGroup(); coreMove.Children.Add(coreScale); coreMove.Children.Add(new TranslateTransform3D(0, 1, 0));
            root.Children.Add(new GeometryModel3D(ThemeShapes.Orb(.75), coreMaterial) { Transform = coreMove });
            haloTurn = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0); var tilt = new Transform3DGroup(); tilt.Children.Add(new RotateTransform3D(haloTurn)); tilt.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), -10))); tilt.Children.Add(new TranslateTransform3D(0, 1, 0));
            var rings = new Model3DGroup { Transform = tilt };
            for (int i = 0; i < 6; i++)
            {
                GeometryModel3D unused; var brush = Glow(i % 2 == 0 ? 0 : 1, out unused); ringBrushes.Add(brush);
                var material = new EmissiveMaterial(brush); rings.Children.Add(new GeometryModel3D(ThemeShapes.Ring(1.3 + i * .45, .035, 96), material) { BackMaterial = material });
            }
            late.Children.Add(rings); AddPulses(late, 6, 1, .05);
        }
        private void AddPulses(Model3DGroup late, int count, double radius, double tube)
        {
            pulses.Clear(); var ring = ThemeShapes.Ring(radius, tube, 96);
            for (int i = 0; i < count; i++)
            {
                GeometryModel3D unused; var pulse = new Pulse { Scale = new ScaleTransform3D(1, 1, 1), Brush = Glow(2, out unused) }; pulse.Brush.Opacity = 0;
                var move = new Transform3DGroup(); move.Children.Add(pulse.Scale); move.Children.Add(new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), Mode == "Pulse" ? -62 : -10))); move.Children.Add(new TranslateTransform3D(0, Mode == "Pulse" ? -.4 : 1, 0));
                var material = new EmissiveMaterial(pulse.Brush); late.Children.Add(new GeometryModel3D(ring, material) { BackMaterial = material, Transform = move }); pulses.Add(pulse);
            }
        }
        private void Spawn(double strength, double grow, double life)
        {
            var free = pulses.OrderByDescending(p => p.Age).FirstOrDefault(); if (free == null) return;
            free.Age = 0; free.Strength = strength; free.Grow = grow; free.Life = life;
        }
        private void StepPulses(double seconds)
        {
            foreach (var p in pulses)
            {
                p.Age += seconds; double t = Math.Min(1, p.Age / Math.Max(.01, p.Life)), s = 1 + p.Grow * Math.Pow(t, .7);
                p.Scale.ScaleX = p.Scale.ScaleY = p.Scale.ScaleZ = s; p.Brush.Opacity = t >= 1 ? 0 : p.Strength * (1 - t) * (1 - t);
            }
        }
        private void StepHalo(double seconds, double[] wave)
        {
            var bands = Spectrum(wave, 6);
            for (int i = 0; i < 6; i++) { double level = wave == null ? 0 : Math.Min(1, Math.Sqrt(bands[i]) * 1.8); if (i == 0) level = Math.Max(level, rave.Bass); ringLevels[i] += (level - ringLevels[i]) * Ease(seconds, level > ringLevels[i] ? .05 : .25); ringBrushes[i].Opacity = Math.Min(1, .1 + .8 * ringLevels[i] + .2 * dropVis); }
            barAngle += (rave.Bar * 15 - barAngle) * Ease(seconds, .18); haloTurn.Angle = barAngle; // turns one step per bar
            double pump = 1 + .28 * kickVis + .08 * rave.Sub + .2 * dropVis; coreScale.ScaleX = coreScale.ScaleY = coreScale.ScaleZ = pump; coreGlow.Opacity = Math.Min(1, .2 + .7 * kickVis + .2 * rave.Sub);
            if (rave.KickNow) Spawn(.85 * kickGain, 5, 1.1); if (rave.DropNow) { Spawn(1, 8, 1.4); Spawn(1, 6, 1.2); }
            StepPulses(seconds);
        }

        // ---------- 4. Moving Heads ----------
        private Point[] heads = new Point[8]; private const double TrussY = 3.55;
        private GeometryModel3D beamsOn, beamsOff, spotsOn, spotsOff, trussLights; private LinearGradientBrush beamsOnBrush, beamsOffBrush; private SolidColorBrush trussBrush; private RadialGradientBrush spotOnBrush, spotOffBrush;
        private void BuildHeads(Model3DGroup root, Model3DGroup late)
        {
            var metal = new MaterialGroup(); metal.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(40, 42, 50)))); metal.Children.Add(new SpecularMaterial(new SolidColorBrush(Alpha(Colors.White, .5)), 40));
            var truss = new GeometryModel3D(ThemeShapes.Box(13, .24, .24), metal) { Transform = new TranslateTransform3D(0, TrussY, 0) }; root.Children.Add(truss);
            for (int i = 0; i < 8; i++) root.Children.Add(new GeometryModel3D(ThemeShapes.Box(.42, .36, .36), metal) { Transform = new TranslateTransform3D(-5.25 + i * 1.5, TrussY - .28, 0) });
            beamsOnBrush = Fade(0, out beamsOn); beamsOffBrush = Fade(1, out beamsOff); trussBrush = Glow(2, out trussLights);
            spotOnBrush = Pool(0); spotOffBrush = Pool(1); var onMaterial = new EmissiveMaterial(spotOnBrush); var offMaterial = new EmissiveMaterial(spotOffBrush);
            spotsOn = new GeometryModel3D(new MeshGeometry3D(), onMaterial) { BackMaterial = onMaterial }; spotsOff = new GeometryModel3D(new MeshGeometry3D(), offMaterial) { BackMaterial = offMaterial };
            foreach (var model in new[] { spotsOff, spotsOn, beamsOff, beamsOn, trussLights }) late.Children.Add(model);
            for (int i = 0; i < 8; i++) heads[i] = new Point((i - 3.5) * 1.75, .6);
        }
        private void StepHeads(double seconds, double beats)
        {
            int pattern = rave.Drop > .3 ? 4 : (rave.Bar + rave.Phrase) % 4; double follow = Ease(seconds, .22);
            Mesh on = new Mesh(), off = new Mesh(), spotOn = new Mesh(), spotOff = new Mesh(), lights = new Mesh();
            for (int i = 0; i < 8; i++)
            {
                double o = i - 3.5; var aim = pattern == 0 ? new Point(o * 1.75, .6) : pattern == 1 ? new Point(o * .22, 0) : pattern == 2 ? new Point(-o * 1.3, .4) : pattern == 3 ? new Point(o * 1.25 + 1.5 * Math.Sin(2 * Math.PI * beats / 4 + i * .75), .3) : new Point(o * 2.2, .9);
                heads[i] = new Point(heads[i].X + (aim.X - heads[i].X) * follow, heads[i].Y + (aim.Y - heads[i].Y) * follow);
                bool lit = (i + rave.Beat) % 2 == 0; // a beat-locked chase: alternate heads swap every beat
                double x = -5.25 + i * 1.5, bottom = 1 + .5 * kickVis; var target = lit ? on : off;
                target.Quad(new Point3D(x - .07, TrussY - .44, 0), new Point3D(x + .07, TrussY - .44, 0), new Point3D(heads[i].X + bottom / 2, -2, heads[i].Y), new Point3D(heads[i].X - bottom / 2, -2, heads[i].Y));
                var spot = lit ? spotOn : spotOff; double sx = heads[i].X, sz = heads[i].Y; spot.Quad(new Point3D(sx - .9, -1.97, sz + .5), new Point3D(sx + .9, -1.97, sz + .5), new Point3D(sx + .9, -1.97, sz - .5), new Point3D(sx - .9, -1.97, sz - .5));
                Square(lights, x - .45, TrussY, .14, .14); Square(lights, x + .45, TrussY, .14, .14);
            }
            beamsOn.Geometry = on.Done(); beamsOff.Geometry = off.Done(); spotsOn.Geometry = spotOn.Done(); spotsOff.Geometry = spotOff.Done(); trussLights.Geometry = lights.Done();
            beamsOnBrush.Opacity = Math.Min(1, .55 + .45 * kickVis + .3 * dropVis); beamsOffBrush.Opacity = .25 + .2 * rave.Sub; spotOnBrush.Opacity = Math.Min(1, .5 + .5 * kickVis); spotOffBrush.Opacity = .2;
            trussBrush.Opacity = Math.Min(1, .2 + .8 * rave.Bass); // the truss lights bump with the bass
        }

        // ---------- 5. Drop ----------
        private sealed class Spark { internal Vector3D Position, Velocity; internal double Life, Age; internal int Color; }
        private readonly List<Spark> particles = new List<Spark>(); private readonly Random random = new Random(7);
        private GeometryModel3D confettiA, confettiB, confettiC, buildSparks; private SolidColorBrush speakerGlow, chargeBrush, buildBrush; private ScaleTransform3D speakerPump; private AxisAngleRotation3D chargeTurn;
        private void BuildDrop(Model3DGroup root, Model3DGroup late)
        {
            speakerPump = new ScaleTransform3D(1, 1, 1); speakerGlow = new SolidColorBrush(Colors.White); recolor.Add(delegate { speakerGlow.Color = C0; });
            var dark = new MaterialGroup(); dark.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(20, 20, 26)))); dark.Children.Add(new SpecularMaterial(new SolidColorBrush(Alpha(Colors.White, .45)), 30)); dark.Children.Add(new EmissiveMaterial(speakerGlow));
            var speaker = new Model3DGroup(); speaker.Children.Add(new GeometryModel3D(ThemeShapes.Ring(1.55, .14, 64), dark)); speaker.Children.Add(new GeometryModel3D(ThemeShapes.Orb(1.05), dark) { Transform = new ScaleTransform3D(1, 1, .35) });
            var place = new Transform3DGroup(); place.Children.Add(speakerPump); place.Children.Add(new TranslateTransform3D(0, 1.2, 0)); speaker.Transform = place; root.Children.Add(speaker);
            GeometryModel3D charge; chargeBrush = Glow(1, out charge); chargeTurn = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0); var chargeMove = new Transform3DGroup(); chargeMove.Children.Add(new RotateTransform3D(chargeTurn)); chargeMove.Children.Add(new TranslateTransform3D(0, 1.2, 0));
            charge.Geometry = ThemeShapes.Ring(2.3, .05, 96); charge.Transform = chargeMove; late.Children.Add(charge);
            buildBrush = Glow(1, out buildSparks); late.Children.Add(buildSparks);
            GeometryModel3D unused; Glow(0, out confettiA); Glow(1, out confettiB); Glow(2, out confettiC); late.Children.Add(confettiA); late.Children.Add(confettiB); late.Children.Add(confettiC);
            AddPulses(late, 8, 1, .06);
        }
        private void StepDrop(double seconds)
        {
            double pump = 1 + .2 * kickVis + .15 * dropVis; speakerPump.ScaleX = speakerPump.ScaleY = speakerPump.ScaleZ = pump; speakerGlow.Opacity = Math.Min(1, .15 + .6 * kickVis + .5 * dropVis);
            chargeBrush.Opacity = Math.Min(1, rave.Build * .95 + .05); chargeTurn.Angle += seconds * (30 + 320 * rave.Build);
            if (rave.KickNow) { Spawn(.45 * kickGain, 3.5, .7); for (int i = 0; i < 4; i++) Launch(2.5); }
            if (rave.DropNow) { Spawn(1, 9, 1.4); Spawn(.9, 7, 1.2); Spawn(.8, 5, 1); for (int i = 0; i < 110; i++) Launch(6.5); } // the drop: big shockwaves and a confetti burst
            StepPulses(seconds);
            Mesh a = new Mesh(), b = new Mesh(), c = new Mesh(), build = new Mesh();
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i]; p.Age += seconds; if (p.Age >= p.Life) { particles.RemoveAt(i); continue; }
                p.Velocity = new Vector3D(p.Velocity.X * Math.Exp(-seconds * .6), p.Velocity.Y - 3.2 * seconds, p.Velocity.Z); p.Position += p.Velocity * seconds;
                double size = .13 * (1 - p.Age / p.Life) + .02; Square(p.Color == 0 ? a : p.Color == 1 ? b : c, p.Position.X, p.Position.Y, p.Position.Z, size);
            }
            if (rave.Build > .02) for (int i = 0; i < 24; i++) { double f = (time * .35 + i / 24.0) % 1, r = 4.2 * (1 - f), ang = i * 2 * Math.PI / 24 + time * .8; Square(build, Math.Cos(ang) * r, 1.2 + Math.Sin(ang) * r * .55, 0, .08); } // sparks drawn inward while the build rises
            confettiA.Geometry = a.Done(); confettiB.Geometry = b.Done(); confettiC.Geometry = c.Done(); buildSparks.Geometry = build.Done(); buildBrush.Opacity = Math.Min(1, rave.Build);
        }
        private void Launch(double speed)
        {
            if (particles.Count > 180) return; double a = random.NextDouble() * 2 * Math.PI, s = speed * (.5 + random.NextDouble() * .7);
            particles.Add(new Spark { Position = new Vector3D(0, 1.2, .3), Velocity = new Vector3D(Math.Cos(a) * s, Math.Abs(Math.Sin(a)) * s * .9 + 1.5, (random.NextDouble() - .5) * 2), Life = 1.6 + random.NextDouble() * 1.2, Color = random.Next(3) });
        }

        // ---------- spectrum maths (shared with tests) ----------
        // The window and the sine/cosine tables only depend on the sizes, so they are worked out once and reused every frame.
        private static int tableSize, tableBands; private static double[][] cosTable, sinTable;
        internal static double[] Spectrum(double[] wave, int bands)
        {
            var result = new double[bands]; if (wave == null || wave.Length < 16) return result;
            double rate = 8000; int n = wave.Length;
            if (n != tableSize || bands != tableBands)
            {
                var cosRows = new double[bands][]; var sinRows = new double[bands][];
                for (int b = 0; b < bands; b++)
                {
                    double hz = 55 * Math.Pow(3600 / 55.0, b / (double)(bands - 1)), w = 2 * Math.PI * hz / rate; cosRows[b] = new double[n]; sinRows[b] = new double[n];
                    for (int i = 0; i < n; i++) { double win = .5 - .5 * Math.Cos(2 * Math.PI * i / (n - 1)); cosRows[b][i] = win * Math.Cos(w * i); sinRows[b][i] = win * Math.Sin(w * i); }
                }
                cosTable = cosRows; sinTable = sinRows; tableSize = n; tableBands = bands;
            }
            for (int b = 0; b < bands; b++)
            {
                double re = 0, im = 0; var c = cosTable[b]; var s = sinTable[b];
                for (int i = 0; i < n; i++) { re += wave[i] * c[i]; im += wave[i] * s[i]; }
                result[b] = Math.Sqrt(re * re + im * im) / n * 4;
            }
            return result;
        }
    }
}
