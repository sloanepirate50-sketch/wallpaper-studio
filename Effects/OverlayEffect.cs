using System.Diagnostics;

namespace WallpaperStudio.Effects;

/// <summary>
/// Base class for everything that animates on top of the screen (butterflies, the cat, ...).
/// It owns the frame timer and the start / stop / hide lifecycle; subclasses only draw.
/// See <see cref="TemplateEffect"/> for a fully commented starting point.
/// </summary>
internal abstract class OverlayEffect : IDisposable
{
    private readonly System.Windows.Forms.Timer timer = new();
    private readonly Stopwatch clock = new();
    private TimeSpan last;
    private double sinceTopmost;

    /// <summary>Stable id, saved in settings. Never change it once released.</summary>
    public abstract string Id { get; }

    /// <summary>Label shown next to the checkbox in the app.</summary>
    public abstract string Name { get; }

    /// <summary>Tooltip text for the checkbox.</summary>
    public virtual string Description => "";

    /// <summary>The live animation shown on this effect's tile in the app.</summary>
    public virtual IEffectPreview CreatePreview() => new PlaceholderPreview(Name);

    /// <summary>Milliseconds between frames. 15 ≈ 60 fps, 33 ≈ 30 fps.</summary>
    protected virtual int FrameIntervalMs => 15;

    public bool Running => timer.Enabled;

    /// <summary>True while the app hides effects (fullscreen app, or you're watching a video).</summary>
    public bool Hidden { get; private set; }

    /// <summary>Multiply logical sizes by this to get physical pixels for the current display scale.</summary>
    protected static float Scale { get; } = GetScale();

    protected OverlayEffect()
    {
        timer.Tick += (_, _) => Tick();
    }

    public void Start()
    {
        if (Running) return;
        OnStart();
        clock.Restart();
        last = TimeSpan.Zero;
        timer.Interval = FrameIntervalMs;
        timer.Start();
    }

    public void Stop()
    {
        if (!Running) return;
        timer.Stop();
        OnStop();
    }

    public void SetHidden(bool hidden)
    {
        if (Hidden == hidden) return;
        Hidden = hidden;
        if (hidden && Running) OnHide();
    }

    private void Tick()
    {
        var now = clock.Elapsed;
        float dt = (float)Math.Min(0.05, (now - last).TotalSeconds); // clamp so a stall doesn't teleport things
        last = now;
        if (Hidden) return;

        OnFrame(dt);

        if ((sinceTopmost += dt) > 2)
        {
            sinceTopmost = 0;
            OnKeepOnTop();
        }
    }

    /// <summary>Create your <see cref="SpriteWindow"/>s and initial state.</summary>
    protected abstract void OnStart();

    /// <summary>Advance the animation by <paramref name="dt"/> seconds and draw a frame.</summary>
    protected abstract void OnFrame(float dt);

    /// <summary>Hide your sprites. They reappear on the next <see cref="SpriteWindow.Present"/>.</summary>
    protected abstract void OnHide();

    /// <summary>Dispose your sprites.</summary>
    protected abstract void OnStop();

    /// <summary>Call <see cref="SpriteWindow.KeepOnTop"/> on your sprites.</summary>
    protected abstract void OnKeepOnTop();

    private static float GetScale()
    {
        using var g = Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96f;
    }

    public void Dispose()
    {
        Stop();
        timer.Dispose();
    }
}
