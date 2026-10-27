using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WallpaperStudio;

/// <summary>Builds the preview tiles shown in the library list.</summary>
internal static class Thumbnails
{
    public static Bitmap For(WallpaperItem item, Size size)
    {
        try
        {
            return item.Kind switch
            {
                WallpaperKind.Scene => item.Id switch
                {
                    "scene:matrix" => Matrix(size),
                    "scene:snake" => Snake(size),
                    "scene:pong" => Pong(size),
                    "scene:tetris" => Tetris(size),
                    "scene:synthwave" => Synthwave(size),
                    "scene:warp" => Warp(size),
                    "scene:life" => Life(size),
                    "scene:fireworks" => Fireworks(size),
                    _ => Placeholder(size),
                },
                WallpaperKind.Image or WallpaperKind.Gif => Cover(LoadImage(Library.FullPath(item)), size, item.Kind),
                WallpaperKind.Video => Cover(ShellThumbnail(Library.FullPath(item), size) ?? Placeholder(size), size, item.Kind),
                _ => Placeholder(size),
            };
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or OutOfMemoryException or COMException)
        {
            return Placeholder(size);
        }
    }

    private static Image LoadImage(string path)
    {
        // Load from a copy of the bytes so the library file isn't locked.
        var ms = new MemoryStream(File.ReadAllBytes(path));
        return Image.FromStream(ms);
    }

    /// <summary>Scales to fill the tile (cropping the overflow) and adds a type badge.</summary>
    private static Bitmap Cover(Image src, Size size, WallpaperKind kind)
    {
        var bmp = new Bitmap(size.Width, size.Height);
        using (src)
        using (var g = Graphics.FromImage(bmp))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            float scale = Math.Max((float)size.Width / src.Width, (float)size.Height / src.Height);
            float w = src.Width * scale, h = src.Height * scale;
            g.DrawImage(src, (size.Width - w) / 2, (size.Height - h) / 2, w, h);

            if (kind is WallpaperKind.Gif or WallpaperKind.Video)
            {
                var label = kind == WallpaperKind.Gif ? "GIF" : "VIDEO";
                using var font = new Font("Segoe UI", 7.5f, FontStyle.Bold);
                var ts = g.MeasureString(label, font);
                using var bg = new SolidBrush(Color.FromArgb(190, 0, 0, 0));
                g.FillRectangle(bg, 4, size.Height - ts.Height - 6, ts.Width + 6, ts.Height + 2);
                g.DrawString(label, font, Brushes.White, 7, size.Height - ts.Height - 5);
            }
        }
        return bmp;
    }

    private static Bitmap Placeholder(Size size)
    {
        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        using var bg = new LinearGradientBrush(new Rectangle(Point.Empty, size), Color.FromArgb(45, 45, 60), Color.FromArgb(20, 20, 28), 45f);
        g.FillRectangle(bg, 0, 0, size.Width, size.Height);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = size.Width / 2f, cy = size.Height / 2f;
        using var tri = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
        g.FillPolygon(tri, new[] { new PointF(cx - 12, cy - 16), new PointF(cx - 12, cy + 16), new PointF(cx + 16, cy) });
        return bmp;
    }

    private static Bitmap Matrix(Size size)
    {
        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Black);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        var rng = new Random(7);
        const string glyphs = "ｱｲｳｴｵｶｷｸｹｺｻｼｽ0123456789ﾊﾋﾌﾍﾎ";
        using var font = new Font("MS Gothic", 8f);
        for (int x = 2; x < size.Width; x += 10)
        {
            int head = rng.Next(-4, size.Height / 11 + 4), len = rng.Next(4, 12);
            for (int i = 0; i < len; i++)
            {
                int row = head - i;
                if (row < 0) continue;
                int alpha = i == 0 ? 255 : 230 - i * 200 / len;
                var color = i == 0 ? Color.FromArgb(alpha, 220, 255, 220) : Color.FromArgb(alpha, 0, 255, 70);
                using var b = new SolidBrush(color);
                g.DrawString(glyphs[rng.Next(glyphs.Length)].ToString(), font, b, x, row * 11);
            }
        }
        return bmp;
    }

    private static Bitmap Snake(Size size)
    {
        var bmp = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(6, 7, 10));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        const int cell = 12;
        using (var dot = new SolidBrush(Color.FromArgb(28, 255, 255, 255)))
            for (int y = cell / 2; y < size.Height; y += cell)
                for (int x = cell / 2; x < size.Width; x += cell)
                    g.FillRectangle(dot, x, y, 1, 1);

        var pts = new[] { new Point(3, 6), new Point(8, 6), new Point(8, 3), new Point(12, 3), new Point(12, 5) }
            .Select(p => new PointF(p.X * cell + cell / 2f, p.Y * cell + cell / 2f)).ToArray();
        using (var glow = new Pen(Color.FromArgb(40, 60, 255, 170), cell * 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(glow, pts);
        using (var body = new Pen(Color.FromArgb(70, 255, 170), cell * 0.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            g.DrawLines(body, pts);
        using var food = new SolidBrush(Color.FromArgb(255, 70, 120));
        g.FillEllipse(food, 12 * cell + 2, 7 * cell + 2, cell - 4, cell - 4);
        return bmp;
    }

    private static Bitmap Canvas(Size size, Color bg, out Graphics g)
    {
        var bmp = new Bitmap(size.Width, size.Height);
        g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(bg);
        return bmp;
    }

    private static Bitmap Pong(Size size)
    {
        var bmp = Canvas(size, Color.FromArgb(4, 5, 10), out var g);
        using (g)
        {
            int w = size.Width, h = size.Height;
            using (var net = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                for (int y = 3; y < h; y += 8) g.FillRectangle(net, w / 2 - 1, y, 2, 4);
            using (var font = new Font("Consolas", 16f, FontStyle.Bold))
            {
                using var left = new SolidBrush(Color.FromArgb(70, 60, 220, 255));
                using var right = new SolidBrush(Color.FromArgb(70, 255, 70, 200));
                g.DrawString("7", font, left, w / 2 - 30, 6);
                g.DrawString("5", font, right, w / 2 + 12, 6);
            }
            Glow(g, Color.FromArgb(60, 220, 255), new RectangleF(12, h * 0.3f, 4, 22));
            Glow(g, Color.FromArgb(255, 70, 200), new RectangleF(w - 16, h * 0.55f, 4, 22));
            using var trail = new SolidBrush(Color.FromArgb(50, 255, 255, 255));
            for (int i = 1; i < 6; i++) g.FillEllipse(trail, w * 0.62f - i * 6, h * 0.42f + i * 2.5f, 5, 5);
            g.FillEllipse(Brushes.White, w * 0.62f, h * 0.42f, 6, 6);
        }
        return bmp;
    }

    private static void Glow(Graphics g, Color color, RectangleF r)
    {
        using var halo = new SolidBrush(Color.FromArgb(50, color));
        g.FillRectangle(halo, RectangleF.Inflate(r, 3, 3));
        using var core = new SolidBrush(color);
        g.FillRectangle(core, r);
    }

    private static Bitmap Tetris(Size size)
    {
        var bmp = Canvas(size, Color.FromArgb(8, 7, 22), out var g);
        using (g)
        {
            const int c = 9;
            int cols = 10, rows = size.Height / c - 1;
            int ox = (size.Width - cols * c) / 2 - 18, oy = (size.Height - rows * c) / 2;
            using (var well = new SolidBrush(Color.FromArgb(14, 14, 32)))
                g.FillRectangle(well, ox - 2, oy - 2, cols * c + 4, rows * c + 4);
            using (var edge = new Pen(Color.FromArgb(120, 140, 100, 255)))
                g.DrawRectangle(edge, ox - 2, oy - 2, cols * c + 4, rows * c + 4);

            Color[] colors = [Color.FromArgb(34, 211, 238), Color.FromArgb(250, 204, 21), Color.FromArgb(168, 85, 247),
                              Color.FromArgb(34, 197, 94), Color.FromArgb(239, 68, 68), Color.FromArgb(59, 130, 246), Color.FromArgb(249, 115, 22)];
            var rng = new Random(11);
            int[] heights = [4, 5, 3, 5, 6, 4, 2, 5, 4, 3];
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < heights[x]; y++)
                    if (!(y == heights[x] - 1 && rng.Next(4) == 0))
                        Block(g, ox + x * c, oy + (rows - 1 - y) * c, c, colors[(x * 3 + y) % colors.Length]);
            // A falling T piece.
            foreach (var (x, y) in new[] { (4, 2), (3, 3), (4, 3), (5, 3) })
                Block(g, ox + x * c, oy + y * c, c, colors[2]);

            using var font = new Font("Segoe UI Semibold", 6.5f);
            using var dim = new SolidBrush(Color.FromArgb(170, 200, 180, 255));
            int px = ox + cols * c + 10;
            g.DrawString("NEXT", font, dim, px, oy);
            foreach (var (x, y) in new[] { (0, 0), (1, 0), (2, 0), (3, 0) })
                Block(g, px + x * 6, oy + 14 + y * 6, 6, colors[0]);
            g.DrawString("SCORE", font, dim, px, oy + 32);
            using var white = new SolidBrush(Color.White);
            using var big = new Font("Consolas", 8f, FontStyle.Bold);
            g.DrawString("4800", big, white, px, oy + 42);
        }
        return bmp;
    }

    private static void Block(Graphics g, int x, int y, int s, Color color)
    {
        using var b = new SolidBrush(color);
        g.FillRectangle(b, x + 1, y + 1, s - 2, s - 2);
        using var hi = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
        g.FillRectangle(hi, x + 1, y + 1, s - 2, 2);
    }

    private static Bitmap Synthwave(Size size)
    {
        var bmp = Canvas(size, Color.Black, out var g);
        using (g)
        {
            int w = size.Width, h = size.Height;
            float horizon = h * 0.6f;
            var skyRect = new RectangleF(0, 0, w, horizon);
            using (var sky = new LinearGradientBrush(skyRect, Color.FromArgb(9, 0, 20), Color.FromArgb(255, 60, 142), 90f))
            {
                sky.InterpolationColors = new ColorBlend
                {
                    Colors = [Color.FromArgb(9, 0, 20), Color.FromArgb(43, 10, 79), Color.FromArgb(255, 60, 142)],
                    Positions = [0f, 0.6f, 1f],
                };
                g.FillRectangle(sky, skyRect);

                float r = h * 0.3f, cx = w / 2f, cy = horizon - r * 0.35f;
                var sunRect = new RectangleF(cx - r, cy - r, r * 2, r * 2);
                g.SetClip(skyRect);
                using (var sun = new LinearGradientBrush(sunRect, Color.FromArgb(255, 242, 117), Color.FromArgb(255, 45, 149), 90f))
                    g.FillEllipse(sun, sunRect);
                for (int i = 0; i < 4; i++)
                {
                    float y = cy + r * 0.1f + i * r * 0.18f;
                    g.FillRectangle(sky, cx - r, y, r * 2, 1 + i * 1.3f);
                }
                g.ResetClip();
            }
            PointF[] hills = [new(0, horizon), new(0, horizon - 18), new(22, horizon - 8), new(40, horizon - 22), new(60, horizon - 6),
                              new(w - 60, horizon - 6), new(w - 40, horizon - 20), new(w - 20, horizon - 9), new(w, horizon - 24), new(w, horizon)];
            using (var hill = new SolidBrush(Color.FromArgb(31, 8, 56))) g.FillPolygon(hill, hills);
            using (var floor = new SolidBrush(Color.FromArgb(20, 2, 40))) g.FillRectangle(floor, 0, horizon, w, h - horizon);
            using var grid = new Pen(Color.FromArgb(255, 58, 214), 1f);
            for (int z = 1; z < 12; z++)
            {
                float y = horizon + (h - horizon) / (z * 0.8f);
                if (y < h) g.DrawLine(grid, 0, y, w, y);
            }
            for (int i = -8; i <= 8; i++) g.DrawLine(grid, w / 2f + i * 1.5f, horizon, w / 2f + i * 26, h);
        }
        return bmp;
    }

    private static Bitmap Warp(Size size)
    {
        var bmp = Canvas(size, Color.FromArgb(0, 0, 6), out var g);
        using (g)
        {
            float cx = size.Width / 2f, cy = size.Height / 2f;
            using (var neb = new GraphicsPath())
            {
                neb.AddEllipse(-20, -20, size.Width * 0.7f, size.Height * 0.9f);
                using var b = new PathGradientBrush(neb) { CenterColor = Color.FromArgb(70, 70, 40, 170), SurroundColors = [Color.Transparent] };
                g.FillPath(b, neb);
            }
            var rng = new Random(5);
            for (int i = 0; i < 90; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2;
                float d0 = 8 + (float)rng.NextDouble() * 70, len = d0 * 0.45f;
                float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a) * 0.65f;
                using var pen = new Pen(Color.FromArgb(Math.Min(255, 60 + (int)(d0 * 2.6f)), 200, 225, 255), d0 > 50 ? 1.6f : 1f);
                g.DrawLine(pen, cx + dx * d0, cy + dy * d0, cx + dx * (d0 + len), cy + dy * (d0 + len));
            }
        }
        return bmp;
    }

    private static Bitmap Life(Size size)
    {
        var bmp = Canvas(size, Color.FromArgb(3, 4, 10), out var g);
        using (g)
        {
            var rng = new Random(9);
            const int c = 6;
            for (int y = 0; y < size.Height / c; y++)
                for (int x = 0; x < size.Width / c; x++)
                {
                    double v = rng.NextDouble();
                    Color? col = v < 0.16 ? Color.FromArgb(60 + rng.Next(150), 230 - rng.Next(150), 255)
                               : v < 0.26 ? Color.FromArgb(60, 25, 100) : null;
                    if (col is { } cc)
                    {
                        using var b = new SolidBrush(cc);
                        g.FillRectangle(b, x * c, y * c, c - 1, c - 1);
                    }
                }
        }
        return bmp;
    }

    private static Bitmap Fireworks(Size size)
    {
        var bmp = Canvas(size, Color.FromArgb(9, 10, 28), out var g);
        using (g)
        {
            void Burst(float cx, float cy, float r, Color color, int n)
            {
                for (int i = 0; i < n; i++)
                {
                    double a = i * Math.PI * 2 / n;
                    float x0 = cx + (float)Math.Cos(a) * r * 0.35f, y0 = cy + (float)Math.Sin(a) * r * 0.35f;
                    float x1 = cx + (float)Math.Cos(a) * r, y1 = cy + (float)Math.Sin(a) * r + r * 0.12f;
                    using var pen = new Pen(color, 1.3f);
                    g.DrawLine(pen, x0, y0, x1, y1);
                }
            }
            Burst(55, 35, 26, Color.FromArgb(255, 90, 160), 22);
            Burst(130, 28, 22, Color.FromArgb(120, 220, 255), 20);
            Burst(150, 60, 16, Color.FromArgb(255, 210, 90), 16);
            var rng = new Random(3);
            float x = 0;
            using var city = new SolidBrush(Color.FromArgb(5, 6, 13));
            using var lit = new SolidBrush(Color.FromArgb(220, 255, 210, 120));
            while (x < size.Width)
            {
                float bw = 10 + rng.Next(18), bh = 10 + rng.Next(22);
                g.FillRectangle(city, x, size.Height - bh, bw + 1, bh);
                for (float wy = size.Height - bh + 3; wy < size.Height - 3; wy += 4)
                    for (float wx = x + 2; wx < x + bw - 2; wx += 3)
                        if (rng.Next(4) == 0) g.FillRectangle(lit, wx, wy, 1.4f, 1.6f);
                x += bw;
            }
        }
        return bmp;
    }

    // ---- Windows shell thumbnails (gives real frames for videos) ----

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(Interop.Native.SIZE size, int flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr bindCtx,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    private static Image? ShellThumbnail(string path, Size size)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
        try
        {
            if (factory.GetImage(new Interop.Native.SIZE(size.Width * 2, size.Height * 2), 0x1, out var hbm) != 0) return null;
            try { return Image.FromHbitmap(hbm); }
            finally { Interop.Native.DeleteObject(hbm); }
        }
        finally { Marshal.ReleaseComObject(factory); }
    }
}
