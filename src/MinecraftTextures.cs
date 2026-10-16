using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Digitone
{
    // Block and music-disc textures for the Minecraft theme: Faithful 64x (Release 15), credited in the README.
    // Embedded as "Minecraft.<name>.png". Pixels are kept crisp, and the grass top gets Minecraft's own green tint.
    internal static class MinecraftTextures
    {
        internal static readonly string[] Blocks = { "grass_block_top", "grass_block_side", "dirt", "stone", "cobblestone", "oak_log", "oak_log_top", "oak_planks", "diamond_ore", "gold_ore", "emerald_ore", "redstone_ore", "glowstone", "jukebox_side", "jukebox_top", "note_block" };
        internal static readonly string[] Discs = { "music_disc_cat", "music_disc_pigstep", "music_disc_otherside", "music_disc_blocks" };
        private static readonly Color GrassTint = Color.FromRgb(0x91, 0xBD, 0x59);
        private static readonly Dictionary<string, BitmapSource> loaded = new Dictionary<string, BitmapSource>();

        // The texture at its original 64x64 size (null if missing).
        internal static BitmapSource Get(string name)
        {
            BitmapSource found; if (loaded.TryGetValue(name, out found)) return found;
            BitmapSource bitmap = null;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Minecraft." + name + ".png"))
            {
                if (stream != null)
                {
                    var decoded = new BitmapImage(); decoded.BeginInit(); decoded.CacheOption = BitmapCacheOption.OnLoad; decoded.StreamSource = stream; decoded.EndInit(); decoded.Freeze();
                    bitmap = name == "grass_block_top" ? Tint(decoded, GrassTint) : decoded;
                }
            }
            loaded[name] = bitmap; return bitmap;
        }

        // The grass top is grey in the pack because Minecraft colors it in-game: multiplied by the same plains-green tint the game uses.
        private static BitmapSource Tint(BitmapSource source, Color tint)
        {
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); int w = bgra.PixelWidth, h = bgra.PixelHeight; var pixels = new byte[w * h * 4];
            bgra.CopyPixels(pixels, w * 4, 0);
            for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = (byte)(pixels[i] * tint.B / 255); pixels[i + 1] = (byte)(pixels[i + 1] * tint.G / 255); pixels[i + 2] = (byte)(pixels[i + 2] * tint.R / 255); }
            var result = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, w * 4); result.Freeze(); return result;
        }

        // Enlarged by whole pixels (nearest neighbour), so the pixel art stays sharp even where WPF smooths textures (3D, baked art).
        private static readonly Dictionary<string, BitmapSource> enlarged = new Dictionary<string, BitmapSource>();
        internal static BitmapSource Sharp(string name, int factor = 4)
        {
            string key = name + "@" + factor; BitmapSource found; if (enlarged.TryGetValue(key, out found)) return found;
            var source = Get(name); if (source == null) return null;
            var bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); int w = bgra.PixelWidth, h = bgra.PixelHeight; var pixels = new byte[w * h * 4]; bgra.CopyPixels(pixels, w * 4, 0);
            int W = w * factor, H = h * factor; var big = new byte[W * H * 4];
            for (int y = 0; y < H; y++) for (int x = 0; x < W; x++) Buffer.BlockCopy(pixels, ((y / factor) * w + x / factor) * 4, big, (y * W + x) * 4, 4);
            var result = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, big, W * 4); result.Freeze(); enlarged[key] = result; return result;
        }

        // A crisp brush for 2D (optionally tiled every 'tile' units).
        internal static ImageBrush Brush(string name, double tile = 0)
        {
            var image = Get(name); if (image == null) return null;
            var brush = new ImageBrush(image) { Stretch = Stretch.Fill };
            if (tile > 0) { brush.TileMode = TileMode.Tile; brush.ViewportUnits = BrushMappingMode.Absolute; brush.Viewport = new Rect(0, 0, tile, tile); }
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor); brush.Freeze(); return brush;
        }
        // A brush for 3D faces: pre-enlarged so it stays sharp under 3D texture smoothing.
        internal static ImageBrush Brush3D(string name)
        {
            var image = Sharp(name); if (image == null) return null;
            var brush = new ImageBrush(image) { Stretch = Stretch.Fill }; RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor); brush.Freeze(); return brush;
        }
        internal static bool Available { get { return Get("dirt") != null; } }
    }
}
