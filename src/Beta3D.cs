using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Digitone
{
    // Geometry for the 3D Beta Theme, built by code (no images or models).
    internal static class Shapes3D
    {
        // Flat-shaded triangles: every face gets its own vertices so facets catch light like cut glass.
        private static void Face(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
        {
            int i = mesh.Positions.Count; mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c);
            var normal = Vector3D.CrossProduct(b - a, c - a); normal.Normalize();
            mesh.Normals.Add(normal); mesh.Normals.Add(normal); mesh.Normals.Add(normal);
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
        }
        // An elongated cut crystal (bipyramid with a slightly uneven ring).
        internal static MeshGeometry3D Crystal(Random random, int sides, double radius, double top, double bottom)
        {
            var mesh = new MeshGeometry3D(); var ring = new List<Point3D>(); double twist = random.NextDouble();
            for (int i = 0; i < sides; i++) { double angle = 2 * Math.PI * (i + twist) / sides, r = radius * (.82 + .3 * random.NextDouble()); ring.Add(new Point3D(Math.Cos(angle) * r, (random.NextDouble() - .5) * radius * .4, Math.Sin(angle) * r)); }
            var up = new Point3D(radius * .15 * (random.NextDouble() - .5), top, 0); var down = new Point3D(0, -bottom, radius * .15 * (random.NextDouble() - .5));
            for (int i = 0; i < sides; i++) { var a = ring[i]; var b = ring[(i + 1) % sides]; Face(mesh, up, b, a); Face(mesh, down, a, b); }
            mesh.Freeze(); return mesh;
        }
        // A thin glass slab with bevelled ends.
        internal static MeshGeometry3D Slab(double w, double h, double d)
        {
            var mesh = new MeshGeometry3D(); double x = w / 2, y = h / 2, z = d / 2, bevel = w * .25;
            var p = new[] { new Point3D(-x + bevel, -y, z), new Point3D(x, -y, z), new Point3D(x - bevel, y, z), new Point3D(-x, y, z), new Point3D(-x + bevel, -y, -z), new Point3D(x, -y, -z), new Point3D(x - bevel, y, -z), new Point3D(-x, y, -z) };
            int[][] quads = { new[] { 0, 1, 2, 3 }, new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 }, new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 } };
            foreach (var q in quads) { Face(mesh, p[q[0]], p[q[1]], p[q[2]]); Face(mesh, p[q[0]], p[q[2]], p[q[3]]); }
            mesh.Freeze(); return mesh;
        }
        // A box with the front face kept separate so it can carry the album cover.
        internal static void Tile(double w, double h, double d, out MeshGeometry3D front, out MeshGeometry3D body)
        {
            double x = w / 2, y = h / 2, z = d / 2;
            front = new MeshGeometry3D();
            front.Positions = new Point3DCollection { new Point3D(-x, -y, z), new Point3D(x, -y, z), new Point3D(x, y, z), new Point3D(-x, y, z) };
            double lo = ThemeShapes.Inset, hi = 1 - ThemeShapes.Inset; // sample a hair inside the picture's edge (no bleeding line)
            front.TextureCoordinates = new PointCollection { new Point(lo, hi), new Point(hi, hi), new Point(hi, lo), new Point(lo, lo) };
            front.TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 }; front.Normals = new Vector3DCollection { new Vector3D(0, 0, 1), new Vector3D(0, 0, 1), new Vector3D(0, 0, 1), new Vector3D(0, 0, 1) };
            front.Freeze();
            body = new MeshGeometry3D(); var p = new[] { new Point3D(-x, -y, z), new Point3D(x, -y, z), new Point3D(x, y, z), new Point3D(-x, y, z), new Point3D(-x, -y, -z), new Point3D(x, -y, -z), new Point3D(x, y, -z), new Point3D(-x, y, -z) };
            int[][] quads = { new[] { 5, 4, 7, 6 }, new[] { 4, 0, 3, 7 }, new[] { 1, 5, 6, 2 }, new[] { 3, 2, 6, 7 }, new[] { 4, 5, 1, 0 } };
            foreach (var q in quads) { Face(body, p[q[0]], p[q[1]], p[q[2]]); Face(body, p[q[0]], p[q[2]], p[q[3]]); }
            body.Freeze();
        }
        // A flat quad in screen pixels, used to swing page snapshots in 3D.
        internal static MeshGeometry3D Card(double w, double h, double hingeX)
        {
            var mesh = new MeshGeometry3D();
            mesh.Positions = new Point3DCollection { new Point3D(-hingeX, 0, 0), new Point3D(w - hingeX, 0, 0), new Point3D(w - hingeX, h, 0), new Point3D(-hingeX, h, 0) };
            mesh.TextureCoordinates = new PointCollection { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) };
            mesh.TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 };
            return mesh;
        }
        internal static Color Alpha(Color color, double alpha) { return Color.FromArgb((byte)Math.Max(0, Math.Min(255, alpha * 255)), color.R, color.G, color.B); }
        internal static Color Mix(Color a, Color b, double t) { return Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t)); }
    }

    // The 3D world behind the app: lit glass crystals drifting in depth, a camera that follows the mouse, and a light that follows the cursor.
    internal sealed class CrystalWorld
    {
        private sealed class Crystal { internal AxisAngleRotation3D Spin; internal TranslateTransform3D Place; internal double X, Y, Z, Speed, Turn, Sway, Phase; internal SolidColorBrush Blink; internal bool Flutter; internal AxisAngleRotation3D Wobble; }
        internal readonly Viewport3D View = new Viewport3D { IsHitTestVisible = false, ClipToBounds = true };
        private readonly PerspectiveCamera camera = new PerspectiveCamera { FieldOfView = 50, NearPlaneDistance = .5, FarPlaneDistance = 80 };
        private readonly List<Crystal> crystals = new List<Crystal>();
        private PointLight orbit, cursor, rim; private double time; private readonly Random random = new Random(23);
        internal int Count { get { return crystals.Count; } }
        internal string Kind { get; private set; }
        internal readonly List<string> Textures = new List<string>(); // which Minecraft blocks and discs are floating (tests)
        internal CrystalWorld() { View.Camera = camera; camera.Position = new Point3D(0, 0, 14); camera.LookDirection = new Vector3D(0, 0, -14); }
        internal void Build(Color accent, Color deep, bool light, bool ultra, string theme = "Midnight")
        {
            View.Children.Clear(); crystals.Clear(); Textures.Clear(); Kind = ThemeShapes.KindFor(theme); var late = new List<Model3D>();
            var world = new Model3DGroup();
            world.Children.Add(new AmbientLight(Shapes3D.Mix(Colors.Black, accent, light ? .45 : .28)));
            world.Children.Add(new DirectionalLight(Color.FromRgb(200, 205, 220), new Vector3D(-.4, -.7, -1)));
            orbit = new PointLight(accent, new Point3D(0, 4, 4)) { Range = 30, ConstantAttenuation = .2, LinearAttenuation = .04 };
            cursor = new PointLight(Shapes3D.Mix(Colors.White, accent, .35), new Point3D(0, 0, 4)) { Range = 14, ConstantAttenuation = .5, LinearAttenuation = .12 };
            world.Children.Add(orbit); world.Children.Add(cursor);
            if (ultra) { rim = new PointLight(Shapes3D.Mix(deep, accent, .5), new Point3D(-8, -4, -6)) { Range = 30, ConstantAttenuation = .3, LinearAttenuation = .05 }; world.Children.Add(rim); } else rim = null;
            int count = (ultra ? 18 : 10) * (Kind == "fireflies" || Kind == "cards" ? 2 : 1);
            for (int i = 0; i < count; i++)
            {
                double z = -12 + 13 * random.NextDouble(), depth = (z + 12) / 13.0; // 0 far → 1 near
                double fade = light ? .5 + .5 * depth : .35 + .65 * depth; // far objects fade like fog
                var made = ThemeShapes.Make(theme, random, i, accent, deep, fade, light); var model = made.Model;
                var item = new Crystal { X = -10 + 20 * random.NextDouble(), Y = -6 + 12 * random.NextDouble(), Z = z, Speed = (.08 + .22 * random.NextDouble()) * made.SpeedScale, Turn = (8 + 22 * random.NextDouble()) * made.TurnScale, Sway = .2 + .5 * random.NextDouble(), Phase = random.NextDouble() * 6, Blink = made.Blink, Flutter = made.Flutter };
                var axis = new Vector3D(random.NextDouble() - .5, 1, random.NextDouble() - .5); axis.Normalize();
                item.Spin = new AxisAngleRotation3D(axis, random.NextDouble() * 360); item.Place = new TranslateTransform3D(); item.Wobble = new AxisAngleRotation3D(new Vector3D(1, 0, 0), random.NextDouble() * 40 - 20);
                var transforms = new Transform3DGroup(); transforms.Children.Add(new RotateTransform3D(item.Wobble)); transforms.Children.Add(new RotateTransform3D(item.Spin)); transforms.Children.Add(item.Place);
                model.Transform = transforms; if (made.Late) late.Add(model); else world.Children.Add(model); crystals.Add(item); if (made.Texture != null) Textures.Add(made.Texture);
            }
            foreach (var model in late) world.Children.Add(model);
            View.Children.Add(new ModelVisual3D { Content = world });
            Step(0, 0, 0, 0, -1, -1);
        }
        // mouseX/mouseY are -1..1; cursorX/cursorY are 0..1 inside the window, or -1 when the mouse is away.
        internal void Step(double seconds, double energy, double mouseX, double mouseY, double cursorX, double cursorY)
        {
            time += seconds;
            camera.Position = new Point3D(mouseX * 1.1 + Math.Sin(time * .07) * .4, -mouseY * .7 + Math.Cos(time * .05) * .25, 14);
            camera.LookDirection = new Vector3D(-camera.Position.X * .9, -camera.Position.Y * .9, -14);
            foreach (var c in crystals)
            {
                c.Y += c.Speed * seconds * (1 + 1.4 * energy);
                if (c.Y > 7.5) { c.Y = -7.5; c.X = -10 + 20 * random.NextDouble(); } else if (c.Y < -7.5) { c.Y = 7.5; c.X = -10 + 20 * random.NextDouble(); }
                if (c.Flutter) c.Wobble.Angle = Math.Sin(time * 1.7 + c.Phase) * 50;
                if (c.Blink != null) { double pulse = .35 + .65 * Math.Max(0, Math.Sin(time * 2.2 + c.Phase * 3)); c.Blink.Color = Color.FromArgb((byte)(255 * Math.Min(1, pulse + energy * .3)), c.Blink.Color.R, c.Blink.Color.G, c.Blink.Color.B); }
                c.Spin.Angle += c.Turn * seconds * (1 + .8 * energy);
                c.Place.OffsetX = c.X + Math.Sin(time * .3 + c.Phase) * c.Sway; c.Place.OffsetY = c.Y; c.Place.OffsetZ = c.Z;
            }
            if (orbit != null)
            {
                orbit.Position = new Point3D(Math.Sin(time * .35) * 9, 3 + Math.Cos(time * .27) * 3, 3 + Math.Cos(time * .35) * 4);
                orbit.ConstantAttenuation = .45 - .3 * energy;
            }
            if (cursor != null) { bool here = cursorX >= 0; cursor.Position = new Point3D((cursorX - .5) * 18, (.5 - cursorY) * 10, here ? 3 : 40); }
            if (rim != null) rim.Position = new Point3D(-8 + Math.Sin(time * .2) * 3, -4, -6);
        }
    }

    public sealed partial class PlayerApp
    {
        private CrystalWorld betaWorld; private Viewport3D betaTile; private Grid betaTileHost;
        private GeometryModel3D betaTileFront, betaTileBody; private AxisAngleRotation3D betaTileYaw, betaTilePitch, betaTileSpin; private TranslateTransform3D betaTileLift; private ScaleTransform3D betaTileBeat;
        private PointLight betaTileLight; private Grid betaTileShadow; private Rectangle betaTileShadowShape; private Transform3DGroup betaTileMotion; private ModelVisual3D betaTileVisual; private readonly Dictionary<UIElement, Visibility> betaHidden = new Dictionary<UIElement, Visibility>();
        private Point betaMouse = new Point(0, 0), betaMouseSmooth = new Point(0, 0), betaCursor01 = new Point(-1, -1);
        private Grid betaBarShadow;
        internal FrameworkElement BetaBarShadow { get { return betaBarShadow; } }
        internal bool Beta3D { get { return betaActive && betaWorld != null && betaWorld.View.Visibility == Visibility.Visible; } }
        internal int BetaCrystalCount { get { return betaWorld == null ? 0 : betaWorld.Count; } }
        internal bool BetaTileShown { get { return betaTile != null && betaTile.Visibility == Visibility.Visible; } }
        internal static bool HardwareFor3D { get { return (RenderCapability.Tier >> 16) >= 2; } }
        private bool BetaUltra { get { return Data.BetaQuality == "Ultra"; } }
        private bool BetaWants3D { get { return HardwareFor3D && BetaMotion.Animate && Data.BetaQuality != "Flat"; } }

        private void SetupBeta3D()
        {
            var root = (Grid)Window.Content;
            betaWorld = new CrystalWorld(); betaWorld.View.Visibility = Visibility.Collapsed; Grid.SetRowSpan(betaWorld.View, 4);
            root.Children.Insert(root.Children.IndexOf(betaBackdrop) + 1, betaWorld.View);
            // The 3D album tile sits where the record is.
            var recordHost = (Grid)Find<Border>("RecordArt").Child;
            betaTile = new Viewport3D { IsHitTestVisible = false, Visibility = Visibility.Collapsed, ClipToBounds = false };
            // The tile's soft shadow: a blurred square drawn once, then bent each frame to sit under the tilted tile (a live blur of the moving tile cost a full effect pass every frame).
            betaTileShadowShape = new Rectangle { Fill = Brushes.Black, Effect = new BlurEffect { Radius = 34, KernelType = KernelType.Gaussian } };
            betaTileShadow = new Grid { IsHitTestVisible = false, Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Opacity = .55, CacheMode = new BitmapCache(), RenderTransform = new MatrixTransform() };
            betaTileShadow.Children.Add(betaTileShadowShape);
            betaTile.Camera = new PerspectiveCamera(new Point3D(0, 0, 6.2), new Vector3D(0, 0, -6.2), new Vector3D(0, 1, 0), 38);
            recordHost.Children.Add(betaTileShadow); recordHost.Children.Add(betaTile); betaTileHost = recordHost;
            // Keep the tile square and inside its area, even when it tilts toward you.
            betaTile.HorizontalAlignment = HorizontalAlignment.Center; betaTile.VerticalAlignment = VerticalAlignment.Center;
            recordHost.SizeChanged += delegate { BetaFitTile(); };
            SetupBetaVisuals();
            Window.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!betaActive) return; var at = e.GetPosition(Window); double w = Math.Max(1, Window.ActualWidth), h = Math.Max(1, Window.ActualHeight);
                betaMouse = new Point(at.X / w * 2 - 1, at.Y / h * 2 - 1); betaCursor01 = new Point(at.X / w, at.Y / h);
            };
            Window.MouseLeave += delegate { betaMouse = new Point(0, 0); betaCursor01 = new Point(-1, -1); };
            Find<WrapPanel>("PlaylistGrid").MouseMove += delegate(object sender, MouseEventArgs e) { if (betaActive) BetaTiltCard(e); };
            Find<WrapPanel>("PlaylistGrid").MouseLeave += delegate { BetaUntiltCards(); };
        }

        // Turns the 3D layers on or off (called from ApplyBeta and when quality changes).
        private void BetaApply3D(bool on)
        {
            bool show = on && BetaWants3D;
            if (show) foreach (string mode in VisualizerModes)
                {
                    // In Beta 3D the expanded visualizers are rave set-ups, and the buttons say so (the normal look keeps the old names).
                    var button = Find<Button>("Mode" + mode); object before = button.Content; button.Content = Visualizer3D.StageName(mode);
                    betaRestore.Add(delegate { button.Content = before; });
                }
            betaWorld.View.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) betaWorld.Build(BetaAccent(), BetaDeep(), Data.AppearanceMode == "Light", BetaUltra, Data.ThemeName);
            BetaShowTile(show);
            if (!show) { BetaHideVisuals(); var stage = Find<Grid>("ContentStage"); stage.BeginAnimation(UIElement.OpacityProperty, null); stage.Opacity = 1; }
            BetaPanels(on);
        }

        private void BetaShowTile(bool show)
        {
            foreach (var hidden in betaHidden) hidden.Key.Visibility = hidden.Value; betaHidden.Clear();
            betaTile.Visibility = show ? Visibility.Visible : Visibility.Collapsed; betaTileShadow.Visibility = betaTile.Visibility;
            if (!show) { betaTile.Children.Clear(); return; }
            foreach (UIElement child in betaTileHost.Children) if (child != betaTile && child != betaTileShadow) { betaHidden[child] = child.Visibility; child.Visibility = Visibility.Collapsed; }
            BetaBuildTile();
        }
        private void BetaBuildTile()
        {
            if (betaTile == null || betaTile.Visibility != Visibility.Visible) return;
            betaTile.Children.Clear(); var accent = BetaAccent(); var deep = BetaDeep(); BetaTileDisc = null;
            MeshGeometry3D front, body; Shapes3D.Tile(2.45, 2.45, .2, out front, out body);
            var cover = Find<Image>("CoverArt").Source as ImageSource;
            Brush face;
            if (cover != null) face = new ImageBrush(cover) { Stretch = Stretch.UniformToFill };
            else if (Data.ThemeName == "Overworld" && MinecraftTextures.Available)
            {
                // Minecraft theme: a song without art shows a music disc (each song always gets the same one of four).
                string disc = BetaDiscFor(current); var art = new DrawingGroup();
                art.Children.Add(new GeometryDrawing(new LinearGradientBrush(Shapes3D.Mix(deep, accent, .35), Shapes3D.Mix(Colors.Black, deep, .6), 45), null, new RectangleGeometry(new Rect(0, 0, 256, 256))));
                art.Children.Add(new ImageDrawing(MinecraftTextures.Sharp(disc, 6), new Rect(32, 32, 192, 192))); // 384 px drawn at 384 px: pixel-sharp
                face = ThemeShapes.Bake(art, 512, 512); BetaTileDisc = disc;
            }
            else
            {
                // No cover yet: a code-drawn face with the Digitone mark on a deep gradient.
                var art = new DrawingGroup(); var gradient = new LinearGradientBrush(Shapes3D.Mix(deep, accent, .35), Shapes3D.Mix(Colors.Black, deep, .6), 45);
                art.Children.Add(new GeometryDrawing(gradient, null, new RectangleGeometry(new Rect(0, 0, 256, 256))));
                art.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Shapes3D.Alpha(accent, .5)), 3), new EllipseGeometry(new Point(128, 128), 92, 92)));
                var logo = Find<Image>("RecordLogo").Source; if (logo != null) art.Children.Add(new ImageDrawing(logo, new Rect(78, 78, 100, 100)));
                face = ThemeShapes.Bake(art, 512, 512);
            }
            var frontMaterial = new MaterialGroup(); frontMaterial.Children.Add(new DiffuseMaterial(face)); frontMaterial.Children.Add(new SpecularMaterial(new SolidColorBrush(Shapes3D.Alpha(Colors.White, .55)), 40));
            var edge = new MaterialGroup(); edge.Children.Add(new DiffuseMaterial(new SolidColorBrush(Shapes3D.Mix(deep, accent, .45)))); edge.Children.Add(new SpecularMaterial(new SolidColorBrush(Colors.White), 90)); edge.Children.Add(new EmissiveMaterial(new SolidColorBrush(Shapes3D.Alpha(accent, .25))));
            betaTileFront = new GeometryModel3D(front, frontMaterial); betaTileBody = new GeometryModel3D(body, edge);
            var group = new Model3DGroup(); group.Children.Add(betaTileFront); group.Children.Add(betaTileBody);
            betaTileYaw = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0); betaTilePitch = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0); betaTileSpin = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
            betaTileLift = new TranslateTransform3D(); betaTileBeat = new ScaleTransform3D(1, 1, 1);
            var transforms = new Transform3DGroup(); transforms.Children.Add(betaTileBeat); transforms.Children.Add(new RotateTransform3D(betaTileSpin)); transforms.Children.Add(new RotateTransform3D(betaTilePitch)); transforms.Children.Add(new RotateTransform3D(betaTileYaw)); transforms.Children.Add(betaTileLift);
            group.Transform = transforms; betaTileMotion = transforms;
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(110, 110, 120)));
            lights.Children.Add(new DirectionalLight(Color.FromRgb(210, 210, 220), new Vector3D(-.3, -.4, -1)));
            betaTileLight = new PointLight(Shapes3D.Mix(Colors.White, accent, .5), new Point3D(2, 2, 3)) { Range = 12, ConstantAttenuation = .6, LinearAttenuation = .1 };
            lights.Children.Add(betaTileLight);
            betaTileVisual = new ModelVisual3D { Content = group };
            betaTile.Children.Add(new ModelVisual3D { Content = lights }); betaTile.Children.Add(betaTileVisual);
        }
        internal string BetaTileDisc { get; private set; }
        internal List<string> BetaWorldTextures { get { return betaWorld == null ? new List<string>() : betaWorld.Textures; } }
        private static string BetaDiscFor(Track track)
        {
            int sum = 0; foreach (char ch in track == null ? "" : track.Path ?? "") sum = (sum * 31 + ch) & 0x7FFFFFFF;
            return MinecraftTextures.Discs[sum % MinecraftTextures.Discs.Length];
        }
        private void BetaFitTile()
        {
            if (betaTileHost == null) return;
            double size = Math.Max(0, Math.Min(betaTileHost.ActualWidth, betaTileHost.ActualHeight) - 8);
            betaTile.Width = size; betaTile.Height = size;
            // The shadow square matches the tile face's on-screen size at rest (camera 6.2 away, 38° view, 2.45-wide tile), so its blur stays 34 px like before.
            double face = Math.Max(10, size * 2.45 / (2 * 6.1 * Math.Tan(19 * Math.PI / 180)));
            betaTileShadow.Width = face; betaTileShadow.Height = face; betaTileShadowShape.Width = face; betaTileShadowShape.Height = face;
        }
        internal Rect BetaTileBounds { get { try { return new Rect(betaTile.TranslatePoint(new Point(0, 0), betaTileHost), new Size(betaTile.ActualWidth, betaTile.ActualHeight)); } catch (InvalidOperationException) { return Rect.Empty; } } }
        internal Size BetaTileArea { get { return new Size(betaTileHost.ActualWidth, betaTileHost.ActualHeight); } }
        internal void BetaCoverChanged() { if (betaActive && BetaTileShown) { BetaEnforceTile(); BetaBuildTile(); } }
        // New song: the tile does a springy full turn.
        private void BetaFlipTile()
        {
            if (betaTileSpin == null || !BetaMotion.Animate) return;
            betaTileSpin.BeginAnimation(AxisAngleRotation3D.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(900)) { EasingFunction = BetaMotion.Back(.35) });
        }

        // Called every Beta frame: parallax, crystals, cursor light, and the tile's tilt toward the mouse.
        private void BetaFrame3D(double seconds)
        {
            double follow = BetaMotion.Ease(.08, seconds);
            betaMouseSmooth = new Point(betaMouseSmooth.X + (betaMouse.X - betaMouseSmooth.X) * follow, betaMouseSmooth.Y + (betaMouse.Y - betaMouseSmooth.Y) * follow);
            BetaFrameVisuals(seconds);
            if (betaWorld.View.Visibility == Visibility.Visible) betaWorld.Step(seconds, betaEnergy, betaMouseSmooth.X, betaMouseSmooth.Y, betaCursor01.X, betaCursor01.Y);
            // Layers move by different amounts, so the window feels deep.
            // Parallax only moves when the mouse has: a still mouse means the page itself is not redrawn for it.
            var backdrop = BetaMotion.Parts(betaBackdrop); BetaNudge(backdrop.Move, betaMouseSmooth.X * 14, betaMouseSmooth.Y * 9);
            var stage = BetaMotion.Parts(Find<Grid>("ContentStage")); if (!stage.Move.HasAnimatedProperties) BetaNudge(stage.Move, -betaMouseSmooth.X * 4, -betaMouseSmooth.Y * 2.5);
            if (betaTileYaw != null && BetaTileShown)
            {
                betaTileYaw.Angle = -12 + betaMouseSmooth.X * 18 + Math.Sin(DateTime.UtcNow.TimeOfDay.TotalSeconds * .6) * 4;
                betaTilePitch.Angle = 6 + betaMouseSmooth.Y * 12 + Math.Cos(DateTime.UtcNow.TimeOfDay.TotalSeconds * .5) * 3;
                betaTileLift.OffsetY = Math.Sin(DateTime.UtcNow.TimeOfDay.TotalSeconds * 1.1) * .08;
                double beat = 1 + .05 * betaEnergy; betaTileBeat.ScaleX = beat; betaTileBeat.ScaleY = beat; betaTileBeat.ScaleZ = beat;
                betaTileLight.Position = new Point3D(betaMouseSmooth.X * 3 + 1.5, -betaMouseSmooth.Y * 3 + 1.5, 3);
            }
            BetaPlaceTileShadow();
        }

        private static void BetaNudge(TranslateTransform move, double x, double y) { if (Math.Abs(move.X - x) > .01) move.X = x; if (Math.Abs(move.Y - y) > .01) move.Y = y; }

        // Bends the pre-blurred shadow square onto the tile's current on-screen shape, 22 px lower (same as the old drop shadow).
        private void BetaPlaceTileShadow()
        {
            if (betaTileVisual == null || betaTileMotion == null || !BetaTileShown || betaTileShadow.Width <= 0) return;
            GeneralTransform3DTo2D toScreen; try { toScreen = betaTileVisual.TransformToAncestor(betaTile); } catch (InvalidOperationException) { return; }
            if (toScreen == null) return;
            Point topLeft, topRight, bottomLeft; double h = 1.225, z = .1;
            if (!toScreen.TryTransform(betaTileMotion.Transform(new Point3D(-h, h, z)), out topLeft) || !toScreen.TryTransform(betaTileMotion.Transform(new Point3D(h, h, z)), out topRight) || !toScreen.TryTransform(betaTileMotion.Transform(new Point3D(-h, -h, z)), out bottomLeft)) return;
            Point at; try { at = betaTile.TranslatePoint(new Point(0, 0), betaTileHost); } catch (InvalidOperationException) { return; }
            double face = betaTileShadow.Width;
            ((MatrixTransform)betaTileShadow.RenderTransform).Matrix = new Matrix((topRight.X - topLeft.X) / face, (topRight.Y - topLeft.Y) / face, (bottomLeft.X - topLeft.X) / face, (bottomLeft.Y - topLeft.Y) / face, at.X + topLeft.X, at.Y + topLeft.Y + 22);
        }
        internal Rect BetaTileShadowBounds { get { return betaTileShadow == null || !betaTileShadow.IsVisible ? Rect.Empty : betaTileShadow.RenderTransform.TransformBounds(new Rect(0, 0, betaTileShadow.Width, betaTileShadow.Height)); } }

        // Playlist cards tilt toward the pointer with a glare sliding across them.
        private Button betaTilted;
        private void BetaTiltCard(MouseEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject; Button card = null;
            while (source != null && card == null) { card = source as Button; source = source is Visual ? VisualTreeHelper.GetParent(source) : null; }
            if (card != betaTilted) BetaUntiltCards();
            if (card == null) return; betaTilted = card;
            var at = e.GetPosition(card); double x = at.X / Math.Max(1, card.ActualWidth) - .5, y = at.Y / Math.Max(1, card.ActualHeight) - .5;
            var parts = BetaMotion.Parts(card);
            parts.Skew.BeginAnimation(SkewTransform.AngleYProperty, null); parts.Skew.BeginAnimation(SkewTransform.AngleXProperty, null);
            parts.Skew.AngleY = x * 7; parts.Skew.AngleX = -y * 5; parts.Scale.BeginAnimation(ScaleTransform.ScaleXProperty, null); parts.Scale.BeginAnimation(ScaleTransform.ScaleYProperty, null); parts.Scale.ScaleX = parts.Scale.ScaleY = 1.05;
            var glare = BetaGlare(card); if (glare != null) { glare.Visibility = Visibility.Visible; var brush = (LinearGradientBrush)glare.Fill; brush.StartPoint = new Point(x + .1, y); brush.EndPoint = new Point(x + .9, y + 1); }
            card.Effect = card.Effect ?? new DropShadowEffect { ShadowDepth = 14, BlurRadius = 26, Opacity = .45, Direction = 270 - x * 40, Color = Colors.Black };
        }
        private void BetaUntiltCards()
        {
            if (betaTilted == null) return; var card = betaTilted; betaTilted = null; var parts = BetaMotion.Parts(card);
            BetaMotion.To(parts.Skew, SkewTransform.AngleYProperty, 0, 380, BetaMotion.Back(.5)); BetaMotion.To(parts.Skew, SkewTransform.AngleXProperty, 0, 380, BetaMotion.Back(.5));
            BetaMotion.ScaleTo(card, 1, 380, BetaMotion.Back(.5));
            var glare = BetaGlare(card); if (glare != null) glare.Visibility = Visibility.Collapsed;
            if (card.Effect is DropShadowEffect && ((DropShadowEffect)card.Effect).ShadowDepth == 14) card.Effect = null;
        }
        private Rectangle BetaGlare(Button card)
        {
            var host = FindDescendant<Grid>(card); if (host == null) return null;
            var glare = host.Children.OfType<Rectangle>().FirstOrDefault(r => (r.Tag as string) == "BetaGlare");
            if (glare == null)
            {
                var brush = new LinearGradientBrush(); brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(70, 255, 255, 255), .5)); brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));
                glare = new Rectangle { Tag = "BetaGlare", Fill = brush, IsHitTestVisible = false, Visibility = Visibility.Collapsed }; Grid.SetRowSpan(glare, 9); Grid.SetColumnSpan(glare, 9); host.Children.Add(glare);
            }
            return glare;
        }
        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) { var child = VisualTreeHelper.GetChild(root, i); var match = child as T; if (match != null) return match; var deeper = FindDescendant<T>(child); if (deeper != null) return deeper; }
            return null;
        }

        // A big soft shadow under a solid panel, drawn once and reused. Effects on the panel itself are redone every frame,
        // because the 3D world behind the window changes every frame; a solid copy of the panel's shape sits right behind it,
        // casts the identical shadow, never changes, and so its blurred picture is simply reused.
        private Grid BetaCastShadow(FrameworkElement target, DropShadowEffect shadow, string name)
        {
            var grid = VisualTreeHelper.GetParent(target) as Grid; if (grid == null || grid.Children.OfType<FrameworkElement>().Any(c => c.Name == name)) return null;
            var control = target as Control; var border = target as Border;
            var fill = (control != null ? control.Background : border != null ? border.Background : null) as SolidColorBrush;
            if (fill == null || fill.Color.A != 255) return null; // see-through panels keep a live shadow so the look is exact
            var caster = new Border { Background = Brushes.Black, CornerRadius = border != null ? border.CornerRadius : new CornerRadius(0), Effect = shadow, IsHitTestVisible = false };
            caster.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = target });
            caster.SetBinding(FrameworkElement.HeightProperty, new System.Windows.Data.Binding("ActualHeight") { Source = target });
            var host = new Grid { Name = name, IsHitTestVisible = false, CacheMode = new BitmapCache(), Margin = target.Margin, HorizontalAlignment = target.HorizontalAlignment, VerticalAlignment = target.VerticalAlignment };
            host.Children.Add(caster);
            Grid.SetRow(host, Grid.GetRow(target)); Grid.SetRowSpan(host, Grid.GetRowSpan(target)); Grid.SetColumn(host, Grid.GetColumn(target)); Grid.SetColumnSpan(host, Grid.GetColumnSpan(target)); Panel.SetZIndex(host, Panel.GetZIndex(target));
            grid.Children.Insert(grid.Children.IndexOf(target), host);
            betaRestore.Add(delegate { grid.Children.Remove(host); if (betaBarShadow == host) betaBarShadow = null; });
            return host;
        }

        // Floating panels with soft layered shadows.
        private void BetaPanels(bool on)
        {
            var root = (Grid)Window.Content;
            var playerBar = root.Children.OfType<FrameworkElement>().FirstOrDefault(c => Grid.GetRow(c) == 3 && c is Border);
            var lifted = new List<FrameworkElement> { Find<TextBox>("Search") }; if (playerBar != null) lifted.Add(playerBar);
            foreach (var element in lifted)
            {
                if (!on || element.Effect != null) continue;
                var shadow = new DropShadowEffect { ShadowDepth = 10, BlurRadius = 30, Opacity = Data.AppearanceMode == "Light" ? .22 : .5, Direction = 270, Color = Colors.Black };
                var host = BetaCastShadow(element, shadow, element == playerBar ? "BetaBarShadow" : "BetaSearchShadow");
                if (host != null) { if (element == playerBar) betaBarShadow = host; continue; }
                betaEffects[element] = null; element.Effect = shadow;
            }
        }
    }
}
