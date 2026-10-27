using System.Numerics;
using WallpaperStudio.Interop;

namespace WallpaperStudio.Effects;

/// <summary>Where on the screen a play-area effect (cat, dog, ...) lives.</summary>
internal enum ScreenPlacement { BottomRight, BottomLeft, TopRight, TopLeft, Center }

/// <summary>An effect whose position can be chosen with the placement picker.</summary>
internal interface IPlaceable
{
    ScreenPlacement Placement { get; set; }
}

/// <summary>A little self-contained animated scene, drawn in its own logical-pixel space.</summary>
internal interface IPlayScene
{
    /// <param name="attention">Something more interesting than the toy (the mouse), in scene coords.</param>
    void Update(float dt, Vector2? attention);
    void Draw(Graphics g);

    /// <summary>Lets the scene adapt to its corner (e.g. which side the frisbee is thrown from).</summary>
    void SetPlacement(ScreenPlacement placement) { }
}

/// <summary>
/// Base for effects that play out in a fixed-size area pinned to a corner or the center of the
/// primary screen. Subclasses only provide the scene and its size.
/// </summary>
internal abstract class PlayAreaEffect : OverlayEffect, IPlaceable
{
    public ScreenPlacement Placement { get; set; } = ScreenPlacement.BottomRight;

    /// <summary>Scene size in logical pixels.</summary>
    protected abstract SizeF AreaSize { get; }

    protected abstract IPlayScene CreateScene();

    public override IEffectPreview CreatePreview() => new PlayAreaPreview(this, CreateScene(), AreaSize);

    private SpriteWindow? sprite;
    private IPlayScene? scene;

    /// <summary>Top-left of a <paramref name="size"/> area placed inside <paramref name="area"/>.</summary>
    public static PointF Place(ScreenPlacement placement, RectangleF area, SizeF size, float margin)
    {
        float left = placement switch
        {
            ScreenPlacement.BottomRight or ScreenPlacement.TopRight => area.Right - size.Width - margin,
            ScreenPlacement.BottomLeft or ScreenPlacement.TopLeft => area.Left + margin,
            _ => area.Left + (area.Width - size.Width) / 2,
        };
        float top = placement switch
        {
            ScreenPlacement.BottomRight or ScreenPlacement.BottomLeft => area.Bottom - size.Height, // on the taskbar
            ScreenPlacement.TopRight or ScreenPlacement.TopLeft => area.Top + margin,
            _ => area.Top + (area.Height - size.Height) / 2,
        };
        return new PointF(left, top);
    }

    protected override void OnStart()
    {
        sprite = new SpriteWindow((int)MathF.Ceiling(AreaSize.Width * Scale), (int)MathF.Ceiling(AreaSize.Height * Scale));
        scene = CreateScene();
    }

    protected override void OnFrame(float dt)
    {
        if (sprite is null || scene is null) return;

        var spot = Place(Placement, Screen.PrimaryScreen!.WorkingArea, new SizeF(sprite.Width, sprite.Height), 12 * Scale);
        int left = (int)spot.X, top = (int)spot.Y;

        // The animal looks at your mouse when it comes close.
        Native.GetCursorPos(out var cursor);
        var local = new Vector2((cursor.X - left) / Scale, (cursor.Y - top) / Scale);
        bool near = local.X > -60 && local.X < AreaSize.Width + 60 && local.Y > -80 && local.Y < AreaSize.Height + 20;

        scene.SetPlacement(Placement);
        scene.Update(dt, near ? local : null);

        sprite.Begin();
        sprite.Graphics.ScaleTransform(Scale, Scale);
        scene.Draw(sprite.Graphics);
        sprite.Present(left, top);
    }

    protected override void OnHide() => sprite?.Hide();

    protected override void OnKeepOnTop() => sprite?.KeepOnTop();

    protected override void OnStop()
    {
        sprite?.Dispose();
        sprite = null;
        scene = null;
    }
}

/// <summary>Tile animation for a play-area effect: its scene on a mini desktop, at the chosen placement.</summary>
internal sealed class PlayAreaPreview(IPlaceable owner, IPlayScene scene, SizeF size) : IEffectPreview
{
    public void Update(float dt)
    {
        scene.SetPlacement(owner.Placement);
        scene.Update(dt, null);
    }

    public void Draw(Graphics g, Rectangle b)
    {
        PreviewBackdrop.Draw(g, b, out _);
        // Center/top placements need room, so the preview scene is a bit smaller there.
        bool bottom = owner.Placement is ScreenPlacement.BottomRight or ScreenPlacement.BottomLeft;
        float scale = Math.Min(b.Width / size.Width, b.Height / size.Height) * (bottom ? 0.98f : 0.6f);
        var work = new RectangleF(b.X, b.Y, b.Width, b.Height - PreviewBackdrop.TaskbarHeight);
        var spot = PlayAreaEffect.Place(owner.Placement, work, new SizeF(size.Width * scale, size.Height * scale), 3);
        var state = g.Save();
        g.TranslateTransform(spot.X, spot.Y);
        g.ScaleTransform(scale, scale);
        scene.Draw(g);
        g.Restore(state);
    }
}
