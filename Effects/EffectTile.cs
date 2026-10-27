using System.Drawing.Drawing2D;

namespace WallpaperStudio.Effects;

/// <summary>A clickable tile with a live animated preview of an effect. Clicking toggles it on/off.</summary>
internal sealed class EffectTile : Control
{
    private static readonly Color Accent = Color.FromArgb(124, 92, 255);
    private static readonly Color OnGreen = Color.FromArgb(46, 196, 120);

    public OverlayEffect Effect { get; }
    public event EventHandler? CheckedChanged;

    private readonly IEffectPreview preview;
    private bool isChecked, hover;

    public EffectTile(OverlayEffect effect)
    {
        Effect = effect;
        preview = effect.CreatePreview();
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Size = new Size(224, 160);
        Margin = new Padding(0, 0, 14, 0);
        Cursor = Cursors.Hand;
        Font = new Font("Segoe UI", 9.75f);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Checked
    {
        get => isChecked;
        set
        {
            if (isChecked == value) return;
            isChecked = value;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Advances the preview animation and repaints.</summary>
    public void Advance(float dt)
    {
        preview.Update(dt);
        Invalidate();
    }

    protected override void OnClick(EventArgs e)
    {
        base.OnClick(e);
        Checked = !Checked;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = DeviceDpi / 96f;

        var previewRect = new Rectangle(1, 1, Width - 2, Height - (int)(30 * s));
        using var clip = RoundRect(previewRect, 8 * s);

        // The preview draws in logical pixels; scale it to this monitor's DPI.
        var state = g.Save();
        g.SetClip(clip);
        g.TranslateTransform(previewRect.X, previewRect.Y);
        g.ScaleTransform(s, s);
        preview.Draw(g, new Rectangle(0, 0, (int)(previewRect.Width / s), (int)(previewRect.Height / s)));
        g.Restore(state);

        if (!isChecked)
        {
            using var dim = new SolidBrush(Color.FromArgb(hover ? 70 : 115, 10, 10, 16));
            g.FillPath(dim, clip);
        }

        var border = isChecked ? Accent : hover ? Color.FromArgb(110, 110, 130) : Color.FromArgb(55, 55, 68);
        using (var pen = new Pen(border, isChecked ? 2.2f * s : 1.2f * s))
            g.DrawPath(pen, clip);

        // ON / OFF pill in the corner.
        var label = isChecked ? "ON" : "OFF";
        using var pillFont = new Font("Segoe UI Semibold", 7.5f);
        var size = TextRenderer.MeasureText(label, pillFont);
        var pill = new Rectangle(previewRect.Right - size.Width - (int)(14 * s), previewRect.Y + (int)(7 * s),
                                 size.Width + (int)(8 * s), size.Height + (int)(2 * s));
        using (var pillPath = RoundRect(pill, pill.Height / 2f))
        using (var pillBrush = new SolidBrush(isChecked ? OnGreen : Color.FromArgb(200, 40, 40, 50)))
            g.FillPath(pillBrush, pillPath);
        TextRenderer.DrawText(g, label, pillFont, pill, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var textRect = new Rectangle(2, previewRect.Bottom + (int)(4 * s), Width - 4, (int)(24 * s));
        TextRenderer.DrawText(g, Effect.Name, Font, textRect, isChecked ? Color.White : Color.FromArgb(180, 180, 192),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static GraphicsPath RoundRect(Rectangle r, float radius)
    {
        float d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
