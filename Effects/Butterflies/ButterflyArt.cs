using System.Drawing.Drawing2D;

namespace WallpaperStudio.Effects.Butterflies;

internal sealed record Species(string Name, Color Inner, Color Outer, Color Edge, Color Spot, bool Veins);

/// <summary>
/// Procedurally drawn top-down butterfly. Paths are in "unit" space where the wingspan is 1,
/// the body sits on x = 0 and the head points toward -y.
/// </summary>
internal static class ButterflyArt
{
    public static readonly Species[] All =
    [
        new("Monarch",     Color.FromArgb(255, 160, 40),  Color.FromArgb(225, 85, 0),    Color.FromArgb(20, 12, 8),  Color.White, true),
        new("Blue Morpho", Color.FromArgb(90, 220, 255),  Color.FromArgb(25, 70, 255),   Color.FromArgb(10, 12, 35), Color.FromArgb(220, 240, 255), false),
        new("Swallowtail", Color.FromArgb(255, 240, 120), Color.FromArgb(255, 196, 0),   Color.FromArgb(15, 15, 15), Color.FromArgb(120, 180, 255), true),
        new("Rose",        Color.FromArgb(255, 140, 220), Color.FromArgb(200, 25, 125),  Color.FromArgb(45, 8, 30),  Color.White, false),
        new("Emerald",     Color.FromArgb(150, 255, 190), Color.FromArgb(10, 160, 105),  Color.FromArgb(5, 35, 25),  Color.FromArgb(255, 255, 200), true),
        new("Lilac",       Color.FromArgb(215, 180, 255), Color.FromArgb(120, 60, 230),  Color.FromArgb(30, 15, 55), Color.White, false),
    ];

    private static readonly GraphicsPath ForeWing = new();
    private static readonly GraphicsPath HindWing = new();
    private static readonly PointF[] Spots = [new(0.43f, -0.35f), new(0.46f, -0.28f), new(0.38f, -0.39f), new(0.41f, -0.20f)];
    private static readonly (PointF A, PointF B)[] Veins =
    [
        (new(0.03f, -0.03f), new(0.30f, -0.36f)), (new(0.03f, -0.02f), new(0.42f, -0.22f)),
        (new(0.03f, -0.01f), new(0.30f, -0.04f)), (new(0.03f, 0.03f), new(0.32f, 0.16f)),
        (new(0.03f, 0.04f), new(0.18f, 0.32f)),
    ];

    private sealed record Paint(Brush Fore, Brush Hind, Pen Edge, Pen? Vein, Brush Spot);
    private static readonly Dictionary<Species, Paint> Paints = [];
    private static readonly SolidBrush BodyBrush = new(Color.FromArgb(40, 28, 22));
    private static readonly Pen AntennaPen = new(Color.FromArgb(40, 28, 22), 0.012f);

    static ButterflyArt()
    {
        ForeWing.StartFigure();
        ForeWing.AddBezier(0.02f, -0.06f, 0.10f, -0.30f, 0.34f, -0.47f, 0.50f, -0.41f);
        ForeWing.AddBezier(0.50f, -0.41f, 0.55f, -0.30f, 0.47f, -0.12f, 0.36f, -0.02f);
        ForeWing.AddBezier(0.36f, -0.02f, 0.24f, 0.02f, 0.10f, 0.02f, 0.02f, 0.00f);
        ForeWing.CloseFigure();

        HindWing.StartFigure();
        HindWing.AddBezier(0.02f, 0.00f, 0.18f, -0.04f, 0.40f, 0.02f, 0.38f, 0.16f);
        HindWing.AddBezier(0.38f, 0.16f, 0.36f, 0.30f, 0.22f, 0.40f, 0.12f, 0.36f);
        HindWing.AddBezier(0.12f, 0.36f, 0.06f, 0.32f, 0.03f, 0.18f, 0.02f, 0.06f);
        HindWing.CloseFigure();
    }

    private static Paint PaintFor(Species sp)
    {
        if (Paints.TryGetValue(sp, out var p)) return p;
        p = new Paint(
            new LinearGradientBrush(new PointF(0, 0), new PointF(0.5f, -0.35f), sp.Inner, sp.Outer),
            new LinearGradientBrush(new PointF(0, 0), new PointF(0.3f, 0.35f), sp.Inner, sp.Outer),
            new Pen(sp.Edge, 0.035f) { LineJoin = LineJoin.Round },
            sp.Veins ? new Pen(Color.FromArgb(150, sp.Edge), 0.011f) : null,
            new SolidBrush(sp.Spot));
        Paints[sp] = p;
        return p;
    }

    /// <param name="openness">1 = wings spread flat, ~0.1 = wings folded edge-on.</param>
    public static void Draw(Graphics g, Species sp, float span, float openness)
    {
        var paint = PaintFor(sp);
        var outer = g.Save();
        g.ScaleTransform(span, span);

        DrawWings(g, paint, openness);
        DrawWings(g, paint, -openness);

        g.FillEllipse(BodyBrush, -0.026f, -0.10f, 0.052f, 0.32f);
        g.FillEllipse(BodyBrush, -0.034f, -0.155f, 0.068f, 0.068f);
        g.DrawBezier(AntennaPen, -0.012f, -0.14f, -0.03f, -0.22f, -0.07f, -0.27f, -0.10f, -0.30f);
        g.DrawBezier(AntennaPen, 0.012f, -0.14f, 0.03f, -0.22f, 0.07f, -0.27f, 0.10f, -0.30f);
        g.FillEllipse(BodyBrush, -0.12f, -0.32f, 0.04f, 0.04f);
        g.FillEllipse(BodyBrush, 0.08f, -0.32f, 0.04f, 0.04f);

        g.Restore(outer);
    }

    private static void DrawWings(Graphics g, Paint p, float scaleX)
    {
        var s = g.Save();
        g.ScaleTransform(scaleX, 1f);

        g.FillPath(p.Hind, HindWing);
        g.DrawPath(p.Edge, HindWing);
        g.FillPath(p.Fore, ForeWing);
        if (p.Vein is { } vein)
            foreach (var (a, b) in Veins) g.DrawLine(vein, a, b);
        g.DrawPath(p.Edge, ForeWing);
        foreach (var spot in Spots)
            g.FillEllipse(p.Spot, spot.X - 0.016f, spot.Y - 0.016f, 0.032f, 0.032f);

        g.Restore(s);
    }

    /// <summary>A small butterfly icon for the tray and window.</summary>
    public static Icon MakeIcon(int size = 32)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(size / 2f, size / 2f + size * 0.04f);
            Draw(g, All[0], size * 0.98f, 1f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }
}
