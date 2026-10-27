using System.Drawing.Drawing2D;

namespace WallpaperStudio.Effects;

/// <summary>
/// A small live animation of an effect, shown on its tile in the app.
/// Coordinates are logical pixels; <c>bounds</c> is the tile's preview area.
/// </summary>
internal interface IEffectPreview
{
    void Update(float dt);
    void Draw(Graphics g, Rectangle bounds);
}

/// <summary>A miniature desktop (wallpaper, one window, taskbar) for previews to play on top of.</summary>
internal static class PreviewBackdrop
{
    private static readonly SolidBrush WindowBody = new(Color.FromArgb(236, 238, 245));
    private static readonly SolidBrush WindowBar = new(Color.FromArgb(208, 212, 225));
    private static readonly SolidBrush WindowLine = new(Color.FromArgb(205, 209, 220));
    private static readonly SolidBrush Taskbar = new(Color.FromArgb(225, 22, 22, 30));
    private static readonly SolidBrush TaskIcon = new(Color.FromArgb(120, 255, 255, 255));

    public const float TaskbarHeight = 12;

    /// <summary>Draws the mini desktop. <paramref name="window"/> is the fake app window's bounds.</summary>
    public static void Draw(Graphics g, Rectangle b, out RectangleF window)
    {
        using (var bg = new LinearGradientBrush(b, Color.FromArgb(48, 60, 120), Color.FromArgb(130, 70, 140), 30f))
            g.FillRectangle(bg, b);

        window = new RectangleF(b.X + b.Width * 0.14f, b.Y + b.Height * 0.30f, b.Width * 0.48f, b.Height * 0.42f);
        g.FillRectangle(WindowBody, window);
        g.FillRectangle(WindowBar, window.X, window.Y, window.Width, 7);
        for (int i = 0; i < 3; i++)
            g.FillRectangle(WindowLine, window.X + 6, window.Y + 14 + i * 8, window.Width * (0.7f - i * 0.15f), 3);

        float top = b.Bottom - TaskbarHeight;
        g.FillRectangle(Taskbar, b.X, top, b.Width, TaskbarHeight);
        float cx = b.X + b.Width / 2f;
        for (int i = -2; i <= 2; i++)
            g.FillRectangle(TaskIcon, cx + i * 10 - 3, top + 3, 6, 6);
    }
}

/// <summary>Fallback preview: the mini desktop with the effect's name.</summary>
internal sealed class PlaceholderPreview(string name) : IEffectPreview
{
    public void Update(float dt) { }

    public void Draw(Graphics g, Rectangle bounds)
    {
        PreviewBackdrop.Draw(g, bounds, out _);
        using var font = new Font("Segoe UI Semibold", 9f);
        TextRenderer.DrawText(g, name, font, bounds, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
    }
}
