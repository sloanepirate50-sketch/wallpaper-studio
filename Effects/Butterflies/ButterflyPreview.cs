namespace WallpaperStudio.Effects.Butterflies;

/// <summary>Tile animation: butterflies looping over a mini desktop, one resting on the window.</summary>
internal sealed class ButterflyPreview : IEffectPreview
{
    private sealed record Flyer(Species Species, float Cx, float Cy, float Rx, float Ry, float A, float B, float Phase);

    private const float Span = 24;
    private readonly Flyer[] flyers;
    private readonly Species percher;
    private float time;

    public ButterflyPreview()
    {
        var all = ButterflyArt.All;
        flyers =
        [
            new(all[0], 0.30f, 0.35f, 0.22f, 0.18f, 0.9f, 1.7f, 0.0f),
            new(all[1], 0.70f, 0.45f, 0.20f, 0.22f, 1.1f, 1.3f, 2.1f),
            new(all[3], 0.50f, 0.25f, 0.30f, 0.12f, 0.7f, 2.1f, 4.0f),
            new(all[4], 0.80f, 0.70f, 0.12f, 0.15f, 1.4f, 1.0f, 1.2f),
        ];
        percher = all[2];
    }

    public void Update(float dt) => time += dt;

    public void Draw(Graphics g, Rectangle b)
    {
        PreviewBackdrop.Draw(g, b, out var window);

        // One butterfly resting on the window's title bar, slowly fanning its wings.
        float restOpen = 0.12f + 0.88f * (0.5f + 0.5f * MathF.Cos(time * 2.2f));
        DrawButterfly(g, percher, window.X + window.Width * 0.72f, window.Y - Span * 0.12f, 0, restOpen);

        foreach (var f in flyers)
        {
            float u = time * f.A + f.Phase, v = time * f.B + f.Phase * 1.3f;
            float x = b.X + (f.Cx + f.Rx * MathF.Sin(u)) * b.Width;
            float y = b.Y + (f.Cy + f.Ry * MathF.Sin(v)) * b.Height;
            float dx = f.Rx * f.A * MathF.Cos(u) * b.Width, dy = f.Ry * f.B * MathF.Cos(v) * b.Height;
            float flap = time * MathF.Tau * 7f + f.Phase;
            y += MathF.Cos(flap) * 1.2f;
            float heading = MathF.Atan2(dy, dx) * 180f / MathF.PI + 90f;
            DrawButterfly(g, f.Species, x, y, heading, 0.12f + 0.88f * (0.5f + 0.5f * MathF.Cos(flap)));
        }
    }

    private static void DrawButterfly(Graphics g, Species sp, float x, float y, float degrees, float openness)
    {
        var state = g.Save();
        g.TranslateTransform(x, y);
        g.RotateTransform(degrees);
        ButterflyArt.Draw(g, sp, Span, openness);
        g.Restore(state);
    }
}
