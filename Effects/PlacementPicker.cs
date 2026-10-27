using System.Drawing.Drawing2D;

namespace WallpaperStudio.Effects;

/// <summary>
/// A tiny monitor with five dots (four corners and the center). Click a dot to choose where an
/// effect plays. Arrow keys move the selection too.
/// </summary>
internal sealed class PlacementPicker : Control
{
    private static readonly Color Accent = Color.FromArgb(124, 92, 255);
    private static readonly ScreenPlacement[] All =
        [ScreenPlacement.TopLeft, ScreenPlacement.TopRight, ScreenPlacement.Center, ScreenPlacement.BottomLeft, ScreenPlacement.BottomRight];

    public event EventHandler? ValueChanged;

    private ScreenPlacement value = ScreenPlacement.BottomRight;
    private ScreenPlacement? hover;

    public PlacementPicker()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Size = new Size(270, 128);
        TabStop = true;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ScreenPlacement Value
    {
        get => value;
        set
        {
            if (this.value == value) return;
            this.value = value;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private float S => DeviceDpi / 96f;

    /// <summary>The monitor's screen area (above its stand).</summary>
    private RectangleF ScreenRect
    {
        get
        {
            float h = Height - 22 * S, w = h * 16f / 9f;
            return new RectangleF((Width - w) / 2, 2 * S, w, h);
        }
    }

    private RectangleF WorkArea
    {
        get
        {
            var r = ScreenRect;
            return new RectangleF(r.X, r.Y, r.Width, r.Height - 9 * S); // minus the taskbar
        }
    }

    private PointF DotCenter(ScreenPlacement p)
    {
        var r = WorkArea;
        float pad = 15 * S;
        return p switch
        {
            ScreenPlacement.TopLeft => new(r.Left + pad, r.Top + pad),
            ScreenPlacement.TopRight => new(r.Right - pad, r.Top + pad),
            ScreenPlacement.BottomLeft => new(r.Left + pad, r.Bottom - pad),
            ScreenPlacement.BottomRight => new(r.Right - pad, r.Bottom - pad),
            _ => new(r.Left + r.Width / 2, r.Top + r.Height / 2),
        };
    }

    private ScreenPlacement? HitTest(Point pt)
    {
        foreach (var p in All)
        {
            var c = DotCenter(p);
            float dx = pt.X - c.X, dy = pt.Y - c.Y;
            if (dx * dx + dy * dy <= MathF.Pow(16 * S, 2)) return p;
        }
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = HitTest(e.Location);
        if (h != hover) { hover = h; Cursor = h is null ? Cursors.Default : Cursors.Hand; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        hover = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (HitTest(e.Location) is { } p) Value = p;
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    /// <summary>Dot positions on a 3x3 grid: corners at 0/2, center at 1.</summary>
    private static (int X, int Y) GridPos(ScreenPlacement p) => p switch
    {
        ScreenPlacement.TopLeft => (0, 0),
        ScreenPlacement.TopRight => (2, 0),
        ScreenPlacement.BottomLeft => (0, 2),
        ScreenPlacement.BottomRight => (2, 2),
        _ => (1, 1),
    };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var (dx, dy) = e.KeyCode switch
        {
            Keys.Left => (-1, 0), Keys.Right => (1, 0), Keys.Up => (0, -1), Keys.Down => (0, 1), _ => (0, 0),
        };
        if (dx == 0 && dy == 0) return;

        // Move to the nearest dot that lies in the pressed direction.
        var (cx, cy) = GridPos(value);
        ScreenPlacement best = value;
        int bestDist = int.MaxValue;
        foreach (var p in All)
        {
            var (px, py) = GridPos(p);
            if ((px - cx) * dx + (py - cy) * dy <= 0) continue;
            int d = (px - cx - dx) * (px - cx - dx) + (py - cy - dy) * (py - cy - dy);
            if (d < bestDist) { bestDist = d; best = p; }
        }
        Value = best;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float s = S;

        // Monitor: bezel, screen, taskbar, stand.
        var screen = ScreenRect;
        using (var bezel = RoundRect(RectangleF.Inflate(screen, 3 * s, 3 * s), 6 * s))
        using (var bezelBrush = new SolidBrush(Color.FromArgb(52, 52, 64)))
            g.FillPath(bezelBrush, bezel);
        using (var bg = new LinearGradientBrush(screen, Color.FromArgb(44, 40, 82), Color.FromArgb(70, 40, 88), 35f))
            g.FillRectangle(bg, screen);
        using (var taskbar = new SolidBrush(Color.FromArgb(22, 22, 30)))
            g.FillRectangle(taskbar, screen.X, screen.Bottom - 9 * s, screen.Width, 9 * s);
        using (var stand = new SolidBrush(Color.FromArgb(52, 52, 64)))
        {
            g.FillRectangle(stand, Width / 2f - 6 * s, screen.Bottom + 3 * s, 12 * s, 9 * s);
            g.FillRectangle(stand, Width / 2f - 26 * s, screen.Bottom + 11 * s, 52 * s, 4 * s);
        }

        foreach (var p in All)
        {
            var c = DotCenter(p);
            bool selected = p == value, hovered = p == hover;
            float r = (selected ? 8 : 6.5f) * s;
            if (selected)
            {
                using var glow = new SolidBrush(Color.FromArgb(70, Accent));
                g.FillEllipse(glow, c.X - r * 1.9f, c.Y - r * 1.9f, r * 3.8f, r * 3.8f);
                using var fill = new SolidBrush(Accent);
                g.FillEllipse(fill, c.X - r, c.Y - r, r * 2, r * 2);
                using var ring = new Pen(Color.White, 2f * s);
                g.DrawEllipse(ring, c.X - r, c.Y - r, r * 2, r * 2);
                if (Focused)
                {
                    using var focus = new Pen(Color.FromArgb(160, 255, 255, 255), 1f * s) { DashStyle = DashStyle.Dot };
                    g.DrawEllipse(focus, c.X - r * 2.2f, c.Y - r * 2.2f, r * 4.4f, r * 4.4f);
                }
            }
            else
            {
                using var fill = new SolidBrush(hovered ? Color.FromArgb(120, Accent) : Color.FromArgb(60, 255, 255, 255));
                g.FillEllipse(fill, c.X - r, c.Y - r, r * 2, r * 2);
                using var ring = new Pen(hovered ? Color.White : Color.FromArgb(150, 255, 255, 255), 1.5f * s);
                g.DrawEllipse(ring, c.X - r, c.Y - r, r * 2, r * 2);
            }
        }
    }

    private static GraphicsPath RoundRect(RectangleF r, float radius)
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
