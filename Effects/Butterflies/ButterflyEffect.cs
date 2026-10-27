using WallpaperStudio.Interop;

namespace WallpaperStudio.Effects.Butterflies;

/// <summary>
/// The "butterfly effect": a flock of butterflies fluttering over everything on screen.
/// Purely visual: every sprite is click-through and never takes focus.
/// </summary>
internal sealed class ButterflyEffect : OverlayEffect
{
    public override string Id => "butterflies";
    public override string Name => "Butterflies";
    public override string Description => "Colorful butterflies fly around, dodge your mouse and land on windows.";

    public override IEffectPreview CreatePreview() => new ButterflyPreview();

    private readonly List<(Butterfly Bug, SpriteWindow Sprite)> flock = [];
    private readonly Random rng = new();
    private readonly float span = 58f * Scale;
    private int count = 8;

    public int Count
    {
        get => count;
        set
        {
            count = value;
            if (Running) SyncFlock();
        }
    }

    private int SpriteSize => (int)MathF.Ceiling(span * 1.35f);

    protected override void OnStart() => SyncFlock();

    protected override void OnFrame(float dt)
    {
        var world = CaptureWorld();
        foreach (var (bug, sprite) in flock)
        {
            bug.Update(dt, world);
            sprite.Begin();
            sprite.Graphics.TranslateTransform(sprite.Width / 2f, sprite.Height / 2f);
            sprite.Graphics.RotateTransform(bug.HeadingDegrees);
            ButterflyArt.Draw(sprite.Graphics, bug.Species, span, bug.Openness);
            sprite.Present((int)(bug.Pos.X - sprite.Width / 2f), (int)(bug.Pos.Y - sprite.Height / 2f));
        }
    }

    protected override void OnHide()
    {
        foreach (var (_, sprite) in flock) sprite.Hide();
    }

    protected override void OnKeepOnTop()
    {
        foreach (var (_, sprite) in flock) sprite.KeepOnTop();
    }

    protected override void OnStop()
    {
        foreach (var (_, sprite) in flock) sprite.Dispose();
        flock.Clear();
    }

    private void SyncFlock()
    {
        var world = CaptureWorld();
        while (flock.Count < count)
            flock.Add((new Butterfly(rng, span, world), new SpriteWindow(SpriteSize, SpriteSize)));
        while (flock.Count > count)
        {
            flock[^1].Sprite.Dispose();
            flock.RemoveAt(flock.Count - 1);
        }
    }

    private static World CaptureWorld()
    {
        Native.GetCursorPos(out var p);
        return new World(new PointF(p.X, p.Y), Screen.AllScreens);
    }
}
