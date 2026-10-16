using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Digitone
{
    // Per-theme 3D shapes and code-drawn textures (no images, no AI art).
    internal static class ThemeShapes
    {
        internal sealed class Made { internal Model3D Model; internal SolidColorBrush Blink; internal double SpeedScale = 1, TurnScale = 1; internal bool Flutter, Late; internal string Texture; }
        // Late: see-through objects (music discs) are drawn after the solid ones, so their clear corners never hide what is behind them.

        // Texture edges are sampled a hair inside (Inset): sampling exactly on the edge blends with "nothing" and let the background show as a thin line.
        internal const double Inset = .003;
        private static void Quad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d, bool uv)
        {
            int i = mesh.Positions.Count; mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c); mesh.Positions.Add(d);
            var n = Vector3D.CrossProduct(b - a, c - a); n.Normalize(); for (int k = 0; k < 4; k++) mesh.Normals.Add(n);
            if (uv) { double lo = Inset, hi = 1 - Inset; mesh.TextureCoordinates.Add(new Point(lo, hi)); mesh.TextureCoordinates.Add(new Point(hi, hi)); mesh.TextureCoordinates.Add(new Point(hi, lo)); mesh.TextureCoordinates.Add(new Point(lo, lo)); }
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);
        }
        private static void Tri(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c)
        {
            int i = mesh.Positions.Count; mesh.Positions.Add(a); mesh.Positions.Add(b); mesh.Positions.Add(c);
            var n = Vector3D.CrossProduct(b - a, c - a); n.Normalize(); mesh.Normals.Add(n); mesh.Normals.Add(n); mesh.Normals.Add(n);
            mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2);
        }
        // Box centred on the origin (or standing on y=0 when grounded), every face textured 0..1.
        internal static MeshGeometry3D Box(double w, double h, double d, bool grounded = false)
        {
            var mesh = new MeshGeometry3D(); double x = w / 2, z = d / 2, y0 = grounded ? 0 : -h / 2, y1 = grounded ? h : h / 2;
            var p = new[] { new Point3D(-x, y0, z), new Point3D(x, y0, z), new Point3D(x, y1, z), new Point3D(-x, y1, z), new Point3D(-x, y0, -z), new Point3D(x, y0, -z), new Point3D(x, y1, -z), new Point3D(-x, y1, -z) };
            Quad(mesh, p[0], p[1], p[2], p[3], true); Quad(mesh, p[5], p[4], p[7], p[6], true); Quad(mesh, p[4], p[0], p[3], p[7], true);
            Quad(mesh, p[1], p[5], p[6], p[2], true); Quad(mesh, p[3], p[2], p[6], p[7], true); Quad(mesh, p[4], p[5], p[1], p[0], true);
            mesh.Freeze(); return mesh;
        }
        internal static MeshGeometry3D TriPrism(double r, double h)
        {
            var mesh = new MeshGeometry3D(); var top = new Point3D[3]; var bottom = new Point3D[3];
            for (int i = 0; i < 3; i++) { double a = Math.PI / 2 + i * 2 * Math.PI / 3; top[i] = new Point3D(Math.Cos(a) * r, h / 2, Math.Sin(a) * r); bottom[i] = new Point3D(Math.Cos(a) * r, -h / 2, Math.Sin(a) * r); }
            for (int i = 0; i < 3; i++) { int j = (i + 1) % 3; Quad(mesh, bottom[j], bottom[i], top[i], top[j], true); }
            Tri(mesh, top[0], top[2], top[1]); Tri(mesh, bottom[0], bottom[1], bottom[2]);
            mesh.Freeze(); return mesh;
        }
        internal static MeshGeometry3D Star(double outer, double inner, double depth)
        {
            var mesh = new MeshGeometry3D(); var front = new List<Point3D>(); var back = new List<Point3D>(); double z = depth / 2;
            for (int i = 0; i < 10; i++) { double a = Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? outer : inner; front.Add(new Point3D(Math.Cos(a) * r, Math.Sin(a) * r, z)); back.Add(new Point3D(Math.Cos(a) * r, Math.Sin(a) * r, -z)); }
            var cf = new Point3D(0, 0, z * 1.6); var cb = new Point3D(0, 0, -z * 1.6);
            for (int i = 0; i < 10; i++) { int j = (i + 1) % 10; Tri(mesh, cf, front[i], front[j]); Tri(mesh, cb, back[j], back[i]); Quad(mesh, front[j], front[i], back[i], back[j], false); }
            mesh.Freeze(); return mesh;
        }
        internal static MeshGeometry3D Column(double r, double h, int flutes)
        {
            var mesh = new MeshGeometry3D(); int sides = flutes * 2; var top = new Point3D[sides]; var bottom = new Point3D[sides];
            for (int i = 0; i < sides; i++) { double a = 2 * Math.PI * i / sides, rr = i % 2 == 0 ? r : r * .88; top[i] = new Point3D(Math.Cos(a) * rr, h / 2, Math.Sin(a) * rr); bottom[i] = new Point3D(Math.Cos(a) * rr, -h / 2, Math.Sin(a) * rr); }
            for (int i = 0; i < sides; i++) { int j = (i + 1) % sides; Quad(mesh, bottom[i], bottom[j], top[j], top[i], true); }
            var ct = new Point3D(0, h / 2, 0); var cb = new Point3D(0, -h / 2, 0);
            for (int i = 0; i < sides; i++) { int j = (i + 1) % sides; Tri(mesh, ct, top[j], top[i]); Tri(mesh, cb, bottom[i], bottom[j]); }
            mesh.Freeze(); return mesh;
        }
        // Low-poly sphere (an octahedron split once).
        internal static MeshGeometry3D Orb(double r)
        {
            var mesh = new MeshGeometry3D();
            var v = new[] { new Vector3D(1, 0, 0), new Vector3D(-1, 0, 0), new Vector3D(0, 1, 0), new Vector3D(0, -1, 0), new Vector3D(0, 0, 1), new Vector3D(0, 0, -1) };
            int[][] faces = { new[] { 2, 4, 0 }, new[] { 2, 0, 5 }, new[] { 2, 5, 1 }, new[] { 2, 1, 4 }, new[] { 3, 0, 4 }, new[] { 3, 5, 0 }, new[] { 3, 1, 5 }, new[] { 3, 4, 1 } };
            Func<Vector3D, Vector3D, Vector3D> mid = (a, b) => { var m = a + b; m.Normalize(); return m; };
            foreach (var f in faces)
            {
                Vector3D a = v[f[0]], b = v[f[1]], c = v[f[2]], ab = mid(a, b), bc = mid(b, c), ca = mid(c, a);
                foreach (var t in new[] { new[] { a, ab, ca }, new[] { ab, b, bc }, new[] { ca, bc, c }, new[] { ab, bc, ca } })
                    Tri(mesh, (Point3D)(t[0] * r), (Point3D)(t[1] * r), (Point3D)(t[2] * r));
            }
            mesh.Freeze(); return mesh;
        }
        internal static MeshGeometry3D Leaf(double w, double h)
        {
            var mesh = new MeshGeometry3D(); var pts = new List<Point3D>();
            for (int i = 0; i <= 8; i++) { double t = i / 8.0, half = Math.Sin(Math.PI * t) * w / 2; pts.Add(new Point3D(-half, -h / 2 + h * t, Math.Sin(Math.PI * t) * .05)); pts.Add(new Point3D(half, -h / 2 + h * t, Math.Sin(Math.PI * t) * .05)); }
            for (int i = 0; i < 8; i++) { int a = i * 2; Quad(mesh, pts[a], pts[a + 1], pts[a + 3], pts[a + 2], false); }
            mesh.TextureCoordinates.Clear(); foreach (var p in mesh.Positions) mesh.TextureCoordinates.Add(new Point(p.X / w + .5, .5 - p.Y / h));
            mesh.Freeze(); return mesh;
        }
        // A ring tube for shock waves.
        internal static MeshGeometry3D Ring(double radius, double tube, int segments)
        {
            var mesh = new MeshGeometry3D();
            for (int i = 0; i < segments; i++)
            {
                double a0 = 2 * Math.PI * i / segments, a1 = 2 * Math.PI * (i + 1) / segments;
                for (int k = 0; k < 4; k++)
                {
                    double b0 = 2 * Math.PI * k / 4, b1 = 2 * Math.PI * (k + 1) / 4;
                    Func<double, double, Point3D> at = (a, b) => new Point3D(Math.Cos(a) * (radius + Math.Cos(b) * tube), Math.Sin(a) * (radius + Math.Cos(b) * tube), Math.Sin(b) * tube);
                    Quad(mesh, at(a0, b0), at(a1, b0), at(a1, b1), at(a0, b1), false);
                }
            }
            mesh.Freeze(); return mesh;
        }

        // Code-drawn pixel texture for Minecraft-style blocks.
        internal static Brush PixelBlock(Random random, Color main, Color second, Color fleck, bool grassTop)
        {
            var bitmap = new WriteableBitmap(16, 16, 96, 96, PixelFormats.Bgr32, null); var pixels = new int[256];
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    double n = random.NextDouble(); Color c = n < .55 ? main : n < .9 ? second : fleck;
                    if (grassTop && y < 4 + (x * 7 % 3)) c = Shapes3D.Mix(Color.FromRgb(70, 160, 60), Color.FromRgb(110, 200, 80), random.NextDouble());
                    pixels[y * 16 + x] = c.R << 16 | c.G << 8 | c.B;
                }
            bitmap.WritePixels(new Int32Rect(0, 0, 16, 16), pixels, 64, 0); bitmap.Freeze();
            var brush = new ImageBrush(bitmap); RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor); brush.Freeze(); return brush;
        }
        // Code-drawn card face for Slay the Spire style cards.
        // Paints a code-drawn texture once into a picture at the given size. Same look; 3D samples a picture much faster than it redraws a vector drawing.
        internal static Brush Bake(Drawing art, int width, int height)
        {
            var visual = new DrawingVisual(); var bounds = art.Bounds;
            using (var dc = visual.RenderOpen()) { dc.PushTransform(new ScaleTransform(width / bounds.Width, height / bounds.Height)); dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y)); dc.DrawDrawing(art); }
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze();
            var brush = new ImageBrush(bitmap); brush.Freeze(); return brush;
        }
        private static readonly Dictionary<string, Brush> baked = new Dictionary<string, Brush>();
        internal const double CardMargin = 4; // art units of empty space around the card frame (the frame's outer edge is the card's outline)
        internal static Brush CardFace(Random random, Color accent, Color deep)
        {
            var art = new DrawingGroup(); var gold = Color.FromRgb(214, 176, 92);
            art.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(-CardMargin, -CardMargin, 200 + 2 * CardMargin, 280 + 2 * CardMargin)))); // breathing room around the frame
            art.Children.Add(new GeometryDrawing(new SolidColorBrush(Shapes3D.Mix(deep, Colors.Black, .4)), new Pen(new SolidColorBrush(gold), 6), new RectangleGeometry(new Rect(3, 3, 194, 274), 16, 16)));
            art.Children.Add(new GeometryDrawing(new LinearGradientBrush(Shapes3D.Mix(accent, Colors.White, .2), Shapes3D.Mix(deep, accent, .3), 70), new Pen(new SolidColorBrush(Shapes3D.Mix(gold, Colors.Black, .3)), 3), new RectangleGeometry(new Rect(22, 46, 156, 110), 6, 6)));
            int points = 3 + random.Next(4); string key = "card" + points + accent + deep; Brush cached; if (baked.TryGetValue(key, out cached)) return cached;
            var glyph = new StreamGeometry(); using (var g = glyph.Open()) { double cx = 100, cy = 101; g.BeginFigure(new Point(cx, cy - 38), true, true); for (int i = 1; i < points * 2; i++) { double a = -Math.PI / 2 + i * Math.PI / points, r = i % 2 == 0 ? 38 : 16; g.LineTo(new Point(cx + Math.Cos(a) * r, cy + Math.Sin(a) * r), true, true); } }
            art.Children.Add(new GeometryDrawing(new SolidColorBrush(Shapes3D.Alpha(Colors.White, .85)), null, glyph));
            art.Children.Add(new GeometryDrawing(new SolidColorBrush(gold), new Pen(new SolidColorBrush(Colors.Black), 2), new EllipseGeometry(new Point(26, 26), 18, 18)));
            for (int i = 0; i < 3; i++) art.Children.Add(new GeometryDrawing(new SolidColorBrush(Shapes3D.Alpha(gold, .5)), null, new RectangleGeometry(new Rect(36, 180 + i * 22, 128 - i * 30, 7), 3, 3)));
            var brush = Bake(art, 400 + 4 * (int)CardMargin, 560 + 4 * (int)CardMargin); baked[key] = brush; return brush;
        }
        internal static Brush Marble(Random random)
        {
            var art = new DrawingGroup(); art.Children.Add(new GeometryDrawing(new LinearGradientBrush(Color.FromRgb(238, 236, 230), Color.FromRgb(206, 204, 198), 60), null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
            for (int i = 0; i < 6; i++)
            {
                var vein = new StreamGeometry(); using (var g = vein.Open()) { g.BeginFigure(new Point(random.Next(100), 0), false, false); g.BezierTo(new Point(random.Next(100), 30), new Point(random.Next(100), 60), new Point(random.Next(100), 100), true, true); }
                art.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(40 + random.Next(60)), 120, 118, 128)), .6 + random.NextDouble()), vein));
            }
            return Bake(art, 256, 256);
        }
        internal static Brush LeafFace(Color accent)
        {
            string key = "leaf" + accent; Brush cached; if (baked.TryGetValue(key, out cached)) return cached;
            var art = new DrawingGroup(); var green = Shapes3D.Mix(Color.FromRgb(60, 140, 70), accent, .3);
            art.Children.Add(new GeometryDrawing(new LinearGradientBrush(Shapes3D.Mix(green, Colors.White, .25), Shapes3D.Mix(green, Colors.Black, .3), 90), null, new RectangleGeometry(new Rect(0, 0, 100, 100))));
            art.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Shapes3D.Alpha(Colors.White, .45)), 2), new LineGeometry(new Point(50, 0), new Point(50, 100))));
            for (int i = 1; i < 5; i++) { art.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Shapes3D.Alpha(Colors.White, .25)), 1), new LineGeometry(new Point(50, i * 20), new Point(20, i * 20 - 12)))); art.Children.Add(new GeometryDrawing(null, new Pen(new SolidColorBrush(Shapes3D.Alpha(Colors.White, .25)), 1), new LineGeometry(new Point(50, i * 20), new Point(80, i * 20 - 12)))); }
            var brush = Bake(art, 256, 256); baked[key] = brush; return brush;
        }
        internal static Color Rainbow(double t) { var hues = new[] { Color.FromRgb(228, 3, 3), Color.FromRgb(255, 140, 0), Color.FromRgb(255, 237, 0), Color.FromRgb(0, 128, 38), Color.FromRgb(36, 64, 142), Color.FromRgb(115, 41, 130) }; t = (t % 1 + 1) % 1 * hues.Length; int a = (int)t % hues.Length, b = (a + 1) % hues.Length; return Shapes3D.Mix(hues[a], hues[b], t - Math.Floor(t)); }
        private static Material Glass(Color tint, double fade, double specular = .9)
        {
            var group = new MaterialGroup();
            group.Children.Add(new DiffuseMaterial(new SolidColorBrush(Shapes3D.Alpha(Shapes3D.Mix(tint, Colors.White, .25), .32 * fade))));
            group.Children.Add(new SpecularMaterial(new SolidColorBrush(Shapes3D.Alpha(Colors.White, specular * fade)), 70));
            group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Shapes3D.Alpha(tint, .07 * fade))));
            return group;
        }
        private static Material Solid(Brush face, double shine, Color? glow = null)
        {
            var group = new MaterialGroup(); group.Children.Add(new DiffuseMaterial(face));
            if (shine > 0) group.Children.Add(new SpecularMaterial(new SolidColorBrush(Shapes3D.Alpha(Colors.White, shine)), 40));
            if (glow.HasValue) group.Children.Add(new EmissiveMaterial(new SolidColorBrush(glow.Value)));
            return group;
        }
        // The exact shape of a pixel-art picture: one strip per row of solid pixels, so see-through areas simply are not there
        // (a square plate showed a grey square around the disc, because the shine lit the whole square).
        internal static MeshGeometry3D PixelCutout(BitmapSource image, double size)
        {
            var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0); int w = bgra.PixelWidth, h = bgra.PixelHeight; var pixels = new byte[w * h * 4]; bgra.CopyPixels(pixels, w * 4, 0);
            var mesh = new MeshGeometry3D(); double px = size / Math.Max(w, h), left = -w * px / 2, top = h * px / 2, edge = .25;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; )
                {
                    if (pixels[(y * w + x) * 4 + 3] < 128) { x++; continue; }
                    int start = x; while (x < w && pixels[(y * w + x) * 4 + 3] >= 128) x++;
                    double x0 = left + start * px, x1 = left + x * px, y0 = top - (y + 1) * px, y1 = top - y * px;
                    double u0 = (start + edge) / w, u1 = (x - edge) / w, v0 = (y + edge) / h, v1 = (y + 1 - edge) / h;
                    int i = mesh.Positions.Count;
                    mesh.Positions.Add(new Point3D(x0, y0, 0)); mesh.Positions.Add(new Point3D(x1, y0, 0)); mesh.Positions.Add(new Point3D(x1, y1, 0)); mesh.Positions.Add(new Point3D(x0, y1, 0));
                    for (int k = 0; k < 4; k++) mesh.Normals.Add(new Vector3D(0, 0, 1));
                    mesh.TextureCoordinates.Add(new Point(u0, v1)); mesh.TextureCoordinates.Add(new Point(u1, v1)); mesh.TextureCoordinates.Add(new Point(u1, v0)); mesh.TextureCoordinates.Add(new Point(u0, v0));
                    mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 1); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i); mesh.TriangleIndices.Add(i + 2); mesh.TriangleIndices.Add(i + 3);
                }
            mesh.Freeze(); return mesh;
        }
        // A card with rounded corners matching its gold frame: picture on the front, dark back, plain gold edge
        // (the old flat box stretched the whole picture across its thin sides and left square corners poking out).
        internal static Model3DGroup RoundedCard(double width, double height, double depth, double radius, Brush face, Brush back, Brush edge)
        {
            var outline = new List<Point>(); int steps = 6;
            double[][] corners = { new[] { width / 2 - radius, height / 2 - radius, 0.0 }, new[] { -width / 2 + radius, height / 2 - radius, 90.0 }, new[] { -width / 2 + radius, -height / 2 + radius, 180.0 }, new[] { width / 2 - radius, -height / 2 + radius, 270.0 } };
            foreach (var c in corners) for (int s = 0; s <= steps; s++) { double a = (c[2] + 90.0 * s / steps) * Math.PI / 180; outline.Add(new Point(c[0] + Math.Cos(a) * radius, c[1] + Math.Sin(a) * radius)); }
            double z = depth / 2; MeshGeometry3D front = new MeshGeometry3D(), rear = new MeshGeometry3D(), rim = new MeshGeometry3D();
            front.Positions.Add(new Point3D(0, 0, z)); front.Normals.Add(new Vector3D(0, 0, 1)); front.TextureCoordinates.Add(new Point(.5, .5));
            rear.Positions.Add(new Point3D(0, 0, -z)); rear.Normals.Add(new Vector3D(0, 0, -1));
            foreach (var p in outline)
            {
                front.Positions.Add(new Point3D(p.X, p.Y, z)); front.Normals.Add(new Vector3D(0, 0, 1)); front.TextureCoordinates.Add(new Point(p.X / width + .5, .5 - p.Y / height));
                rear.Positions.Add(new Point3D(p.X, p.Y, -z)); rear.Normals.Add(new Vector3D(0, 0, -1));
            }
            for (int i = 1; i <= outline.Count; i++)
            {
                int a = i, b = i % outline.Count + 1;
                front.TriangleIndices.Add(0); front.TriangleIndices.Add(a); front.TriangleIndices.Add(b);
                rear.TriangleIndices.Add(0); rear.TriangleIndices.Add(b); rear.TriangleIndices.Add(a);
                var p0 = outline[a - 1]; var p1 = outline[b - 1];
                Quad(rim, new Point3D(p0.X, p0.Y, -z), new Point3D(p1.X, p1.Y, -z), new Point3D(p1.X, p1.Y, z), new Point3D(p0.X, p0.Y, z), false);
            }
            front.Freeze(); rear.Freeze(); rim.Freeze();
            var card = new Model3DGroup();
            card.Children.Add(new GeometryModel3D(front, Solid(face, .35))); card.Children.Add(new GeometryModel3D(rear, new DiffuseMaterial(back))); card.Children.Add(new GeometryModel3D(rim, Solid(edge, .5)));
            return card;
        }
        // A flat square (both sides textured).
        internal static MeshGeometry3D Plate(double size)
        {
            var mesh = new MeshGeometry3D(); double h = size / 2;
            Quad(mesh, new Point3D(-h, -h, 0), new Point3D(h, -h, 0), new Point3D(h, h, 0), new Point3D(-h, h, 0), true); mesh.Freeze(); return mesh;
        }
        // A real Minecraft block with the right texture on each face (grass on top, grass side around, dirt underneath, and so on).
        private static readonly Dictionary<string, Material> blockFaces = new Dictionary<string, Material>();
        private static Material BlockFace(string texture)
        {
            Material found; if (blockFaces.TryGetValue(texture, out found)) return found;
            var art = MinecraftTextures.Brush3D(texture); var group = new MaterialGroup(); group.Children.Add(new DiffuseMaterial(art)); group.Children.Add(new SpecularMaterial(new SolidColorBrush(Shapes3D.Alpha(Colors.White, .15)), 40));
            if (texture == "glowstone") { var glow = art.Clone(); glow.Opacity = .65; group.Children.Add(new EmissiveMaterial(glow)); }
            group.Freeze(); blockFaces[texture] = group; return group;
        }
        internal static Model3DGroup MinecraftBlock(double size, string kind)
        {
            string top, side, bottom;
            switch (kind)
            {
                case "grass": top = "grass_block_top"; side = "grass_block_side"; bottom = "dirt"; break;
                case "log": top = bottom = "oak_log_top"; side = "oak_log"; break;
                case "jukebox": top = "jukebox_top"; side = "jukebox_side"; bottom = "oak_planks"; break;
                case "planks": top = side = bottom = "oak_planks"; break;
                case "note": top = side = bottom = "note_block"; break;
                case "diamond": case "gold": case "emerald": case "redstone": top = side = bottom = kind + "_ore"; break;
                default: top = side = bottom = kind; break;
            }
            double x = size / 2, y = size / 2, z = size / 2;
            var p = new[] { new Point3D(-x, -y, z), new Point3D(x, -y, z), new Point3D(x, y, z), new Point3D(-x, y, z), new Point3D(-x, -y, -z), new Point3D(x, -y, -z), new Point3D(x, y, -z), new Point3D(-x, y, -z) };
            MeshGeometry3D tops = new MeshGeometry3D(), bottoms = new MeshGeometry3D(), sides = new MeshGeometry3D();
            Quad(tops, p[3], p[2], p[6], p[7], true); Quad(bottoms, p[4], p[5], p[1], p[0], true);
            Quad(sides, p[0], p[1], p[2], p[3], true); Quad(sides, p[5], p[4], p[7], p[6], true); Quad(sides, p[4], p[0], p[3], p[7], true); Quad(sides, p[1], p[5], p[6], p[2], true);
            tops.Freeze(); bottoms.Freeze(); sides.Freeze();
            var block = new Model3DGroup();
            block.Children.Add(new GeometryModel3D(sides, BlockFace(side))); block.Children.Add(new GeometryModel3D(tops, BlockFace(top))); block.Children.Add(new GeometryModel3D(bottoms, BlockFace(bottom)));
            return block;
        }
        internal static string KindFor(string theme)
        {
            switch (theme) { case "Overworld": return "blocks"; case "Spire": return "cards"; case "Collective": return "stars"; case "Sanctuary": return "fireflies"; case "Olympus": return "marble"; case "Pride": return "prisms"; default: return "crystals"; }
        }
        // One floating object for the theme's 3D world. fade: 0 far … 1 near.
        internal static Made Make(string theme, Random random, int index, Color accent, Color deep, double fade, bool light)
        {
            var made = new Made();
            switch (KindFor(theme))
            {
                case "blocks":
                {
                    if (MinecraftTextures.Available)
                    {
                        if (index % 5 == 4)
                        {
                            // A spinning music disc.
                            string disc = MinecraftTextures.Discs[(index / 5) % MinecraftTextures.Discs.Length]; var art = MinecraftTextures.Brush3D(disc);
                            var shine = new MaterialGroup(); shine.Children.Add(new DiffuseMaterial(art)); shine.Children.Add(new SpecularMaterial(new SolidColorBrush(Shapes3D.Alpha(Colors.White, .35)), 30));
                            made.Model = new GeometryModel3D(PixelCutout(MinecraftTextures.Get(disc), .8 + random.NextDouble() * .3), shine) { BackMaterial = new DiffuseMaterial(art) };
                            made.TurnScale = 2.2; made.SpeedScale = .8; made.Late = true; made.Texture = disc; break;
                        }
                        string[] picks = { "grass", "grass", "dirt", "stone", "cobblestone", "log", "planks", "diamond", "gold", "emerald", "redstone", "glowstone", "jukebox", "note" };
                        string pick = picks[random.Next(picks.Length)];
                        made.Model = MinecraftBlock(.55 + random.NextDouble() * .55, pick); made.TurnScale = .35; made.SpeedScale = .7; made.Texture = pick; break;
                    }
                    double size = .55 + random.NextDouble() * .55; int kind = index % 4;
                    var face = kind == 0 ? PixelBlock(random, Color.FromRgb(121, 85, 58), Color.FromRgb(96, 67, 45), Color.FromRgb(140, 100, 70), true)
                        : kind == 1 ? PixelBlock(random, Color.FromRgb(125, 125, 125), Color.FromRgb(105, 105, 105), Color.FromRgb(150, 150, 150), false)
                        : kind == 2 ? PixelBlock(random, Color.FromRgb(121, 85, 58), Color.FromRgb(96, 67, 45), Color.FromRgb(140, 100, 70), false)
                        : PixelBlock(random, Color.FromRgb(120, 120, 120), Color.FromRgb(100, 100, 100), Shapes3D.Mix(accent, Colors.White, .2), false);
                    made.Model = new GeometryModel3D(Box(size, size, size), Solid(face, .15)) { BackMaterial = new DiffuseMaterial(face) };
                    made.TurnScale = .35; made.SpeedScale = .7; break;
                }
                case "cards":
                {
                    if (index % 3 == 2)
                    {
                        var ember = Color.FromRgb(255, 140, 50); made.Blink = new SolidColorBrush(ember);
                        var glow = new MaterialGroup(); glow.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(80, 30, 10)))); glow.Children.Add(new EmissiveMaterial(made.Blink));
                        made.Model = new GeometryModel3D(Orb(.06 + random.NextDouble() * .05), glow); made.SpeedScale = 4; made.TurnScale = 2; break;
                    }
                    var front = CardFace(random, accent, deep); var back = new SolidColorBrush(Shapes3D.Mix(deep, Colors.Black, .5));
                    // The picture includes a margin, so its brush is stretched to the outline plus that margin (the frame lands exactly on the card's edge).
                    var framed = ((ImageBrush)front).Clone(); double mx = CardMargin / (200 + 2 * CardMargin), my = CardMargin / (280 + 2 * CardMargin);
                    framed.Viewbox = new Rect(mx, my, 1 - 2 * mx, 1 - 2 * my); framed.ViewboxUnits = BrushMappingMode.RelativeToBoundingBox; framed.Freeze();
                    made.Model = RoundedCard(1.1, 1.54, .04, 1.1 * 19 / 200, framed, back, new SolidColorBrush(Color.FromRgb(214, 176, 92)));
                    made.TurnScale = .5; break;
                }
                case "stars":
                {
                    var red = Shapes3D.Mix(Color.FromRgb(200, 20, 30), accent, .3); var gold = Color.FromRgb(230, 180, 60);
                    bool star = index % 3 != 2;
                    var material = new MaterialGroup(); material.Children.Add(new DiffuseMaterial(new SolidColorBrush(star ? red : Shapes3D.Alpha(red, .6 * fade + .2)))); material.Children.Add(new SpecularMaterial(new SolidColorBrush(gold), 30)); material.Children.Add(new EmissiveMaterial(new SolidColorBrush(Shapes3D.Alpha(red, .12))));
                    made.Model = new GeometryModel3D(star ? Star(.6 + random.NextDouble() * .4, .26, .22) : Shapes3D.Crystal(random, 4, .2, 1.4, .8), material) { BackMaterial = new DiffuseMaterial(new SolidColorBrush(red)) };
                    made.TurnScale = .8; break;
                }
                case "fireflies":
                {
                    if (index % 5 < 2)
                    {
                        made.Blink = new SolidColorBrush(Color.FromRgb(240, 230, 120));
                        var glow = new MaterialGroup(); glow.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(60, 60, 20)))); glow.Children.Add(new EmissiveMaterial(made.Blink));
                        made.Model = new GeometryModel3D(Orb(.07 + random.NextDouble() * .06), glow); made.SpeedScale = .5; made.TurnScale = 0; break;
                    }
                    var face = LeafFace(accent); var leaf = Leaf(.7 + random.NextDouble() * .4, 1.2 + random.NextDouble() * .5);
                    made.Model = new GeometryModel3D(leaf, Solid(face, .1)) { BackMaterial = new DiffuseMaterial(face) };
                    made.Flutter = true; made.SpeedScale = -.6; break; // leaves drift down
                }
                case "marble":
                {
                    var marble = Marble(random); var gold = Color.FromRgb(214, 176, 92);
                    if (index % 3 == 0) { made.Model = new GeometryModel3D(Column(.35 + random.NextDouble() * .15, 1.4 + random.NextDouble() * 1.4, 10), Solid(marble, .35)) { BackMaterial = new DiffuseMaterial(marble) }; made.TurnScale = .4; }
                    else
                    {
                        var shard = new MaterialGroup(); shard.Children.Add(new DiffuseMaterial(marble)); shard.Children.Add(new SpecularMaterial(new SolidColorBrush(index % 3 == 1 ? gold : Colors.White), 50));
                        made.Model = new GeometryModel3D(Shapes3D.Crystal(random, 5, .3 + random.NextDouble() * .3, .8 + random.NextDouble(), .5 + random.NextDouble() * .5), shard) { BackMaterial = new DiffuseMaterial(marble) };
                    }
                    break;
                }
                case "prisms":
                {
                    var group = new Model3DGroup(); var mesh = TriPrism(.38 + random.NextDouble() * .25, 1.4 + random.NextDouble() * 1.2);
                    double hue = random.NextDouble();
                    group.Children.Add(new GeometryModel3D(mesh, Glass(Rainbow(hue), fade)) { BackMaterial = new DiffuseMaterial(new SolidColorBrush(Shapes3D.Alpha(Rainbow(hue + .5), .3 * fade))) });
                    made.Model = group; break;
                }
                default:
                {
                    var mesh = index % 4 == 3 ? Shapes3D.Slab(.6 + random.NextDouble() * .8, 1.6 + random.NextDouble() * 1.8, .12) : Shapes3D.Crystal(random, 5 + random.Next(3), .35 + random.NextDouble() * .45, 1 + random.NextDouble() * 1.6, .6 + random.NextDouble());
                    made.Model = new GeometryModel3D(mesh, Glass(theme == "Midnight" ? Shapes3D.Mix(accent, Color.FromRgb(200, 230, 255), .3) : accent, fade)) { BackMaterial = new DiffuseMaterial(new SolidColorBrush(Shapes3D.Alpha(accent, .12 * fade))) };
                    break;
                }
            }
            return made;
        }
    }

    public sealed partial class PlayerApp
    {
        private Visualizer3D betaVisualizer;
        internal string BetaVisualizerMode { get { return betaVisualizer != null && betaVisualizer.View.Visibility == Visibility.Visible ? betaVisualizer.Mode : null; } }
        internal string BetaWorldKind { get { return betaWorld == null ? null : betaWorld.Kind; } }
        private void SetupBetaVisuals()
        {
            betaVisualizer = new Visualizer3D();
            var expanded = Find<Grid>("ExpandedView"); Grid.SetRow(betaVisualizer.View, 2); betaVisualizer.View.Margin = new Thickness(37, 5, 37, 9); // fills the visualizer frame right up to its border (no inner rectangle) Panel.SetZIndex(betaVisualizer.View, 5);
            expanded.Children.Add(betaVisualizer.View);
        }
        // The record and the 3D tile are never shown together: while the tile is up, anything the old code turns back on is hidden again.
        private void BetaEnforceTile()
        {
            if (!BetaTileShown) return;
            foreach (UIElement child in betaTileHost.Children)
            {
                if (child == betaTile || child == betaTileShadow || child.Visibility == Visibility.Collapsed) continue;
                betaHidden[child] = child.Visibility; child.Visibility = Visibility.Collapsed;
            }
        }
        private void BetaFrameVisuals(double seconds)
        {
            BetaEnforceTile();
            bool show = Beta3D && Find<Grid>("ExpandedView").Visibility == Visibility.Visible;
            var flat = Find<Canvas>("ExpandedWaveform");
            if (!show) { if (betaVisualizer.View.Visibility != Visibility.Collapsed) { betaVisualizer.View.Visibility = Visibility.Collapsed; flat.Opacity = 1; flat.Visibility = Visibility.Visible; } return; }
            if (betaVisualizer.View.Visibility != Visibility.Visible || betaVisualizer.Mode != ActiveVisualizer || betaVisualizer.Theme != Data.ThemeName)
            {
                betaVisualizer.Build(ActiveVisualizer, BetaAccent(), BetaDeep(), Data.AppearanceMode == "Light", Data.ThemeName);
                betaVisualizer.View.Visibility = Visibility.Visible; flat.Opacity = 0; flat.Visibility = Visibility.Hidden; // the flat one underneath is skipped entirely
            }
            betaVisualizer.Step(seconds, betaWave, betaEnergy, rave, !Data.VisualizerFlashesOff);
        }
        private void BetaHideVisuals()
        {
            if (betaVisualizer == null) return;
            betaVisualizer.View.Visibility = Visibility.Collapsed; Find<Canvas>("ExpandedWaveform").Opacity = 1; Find<Canvas>("ExpandedWaveform").Visibility = Visibility.Visible;
        }
    }
}
