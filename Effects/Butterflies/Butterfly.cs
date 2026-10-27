using WallpaperStudio.Interop;
using static WallpaperStudio.Interop.Native;

namespace WallpaperStudio.Effects.Butterflies;

/// <summary>What every butterfly can see this frame.</summary>
internal readonly record struct World(PointF Cursor, Screen[] Screens);

/// <summary>
/// Flight model: fluttering wander toward a goal, bobbing with each wing beat, startled by the
/// mouse, and now and then landing on the top edge of the active window or the taskbar to rest.
/// </summary>
internal sealed class Butterfly
{
    private enum Mode { Flying, Landing, Resting }

    private const float ScareRadius = 150f;

    public Species Species { get; }
    public PointF Pos { get; private set; }
    public float HeadingDegrees => heading * 180f / MathF.PI + 90f; // art faces up (-y)
    public float Openness { get; private set; } = 1f;

    private readonly Random rng;
    private readonly float span;
    private readonly float cruiseSpeed;
    private PointF vel;
    private PointF goal;
    private float heading = -MathF.PI / 2;
    private float wander;
    private float flapPhase;
    private float timer;
    private Mode mode;

    private IntPtr perchWindow;
    private RECT perchRect;

    public Butterfly(Random rng, float span, World world)
    {
        this.rng = rng;
        this.span = span;
        Species = ButterflyArt.All[rng.Next(ButterflyArt.All.Length)];
        cruiseSpeed = span * Rand(1.4f, 2.4f);
        flapPhase = Rand(0, MathF.Tau);

        // Enter from just outside a random screen edge.
        var area = world.Screens[rng.Next(world.Screens.Length)].WorkingArea;
        bool fromLeft = rng.Next(2) == 0;
        Pos = new PointF(fromLeft ? area.Left - span : area.Right + span, Rand(area.Top + 80, area.Bottom - 80));
        vel = new PointF(fromLeft ? cruiseSpeed : -cruiseSpeed, 0);
        heading = MathF.Atan2(vel.Y, vel.X);
        PickGoal(world);
    }

    public void Update(float dt, World world)
    {
        float toCursor = Dist(Pos, world.Cursor);
        switch (mode)
        {
            case Mode.Flying: Fly(dt, world, toCursor); break;
            case Mode.Landing: Land(dt, world, toCursor); break;
            case Mode.Resting: Rest(dt, world, toCursor); break;
        }
    }

    private void Fly(float dt, World world, float toCursor)
    {
        timer -= dt;
        if (Dist(Pos, goal) < span || timer < 0) PickGoal(world);

        // Butterflies never fly straight: a random-walk offset keeps the path loopy.
        wander = Math.Clamp(wander + (Rand(-1f, 1f) * 9f * dt), -1.3f, 1.3f);
        float dir = MathF.Atan2(goal.Y - Pos.Y, goal.X - Pos.X) + wander;
        float speed = cruiseSpeed;
        float flapHz = 8f;

        if (toCursor < ScareRadius)
        {
            dir = MathF.Atan2(Pos.Y - world.Cursor.Y, Pos.X - world.Cursor.X) + wander * 0.3f;
            speed *= 2.6f;
            flapHz = 13f;
        }

        Steer(dir, speed, 2.8f, dt);
        Flap(flapHz, dt);
        // Each downstroke gives a little lift, so the flight path bobs.
        vel.Y -= MathF.Cos(flapPhase) * span * 5f * dt;
        Move(dt);
        Openness = 0.12f + 0.88f * (0.5f + 0.5f * MathF.Cos(flapPhase));
        TurnToward(MathF.Atan2(vel.Y, vel.X), 6f, dt);

        if (Rand(0, 1) < dt * 0.05f) TryPickPerch(world);
    }

    private void Land(float dt, World world, float toCursor)
    {
        if (toCursor < ScareRadius || !PerchStillThere()) { TakeOff(world); return; }

        float d = Dist(Pos, goal);
        if (d < 3f)
        {
            Pos = goal;
            vel = PointF.Empty;
            mode = Mode.Resting;
            timer = Rand(4f, 12f);
            return;
        }

        float speed = Math.Min(cruiseSpeed, d * 2.5f + 20f);
        Steer(MathF.Atan2(goal.Y - Pos.Y, goal.X - Pos.X), speed, 5f, dt);
        Flap(9f, dt);
        Move(dt);
        Openness = 0.12f + 0.88f * (0.5f + 0.5f * MathF.Cos(flapPhase));
        TurnToward(MathF.Atan2(vel.Y, vel.X), 6f, dt);
    }

    private void Rest(float dt, World world, float toCursor)
    {
        timer -= dt;
        if (timer < 0 || toCursor < ScareRadius || !PerchStillThere()) { TakeOff(world); return; }

        // Slow, lazy fanning of the wings while perched, facing up.
        Flap(0.35f, dt);
        Openness = 0.1f + 0.9f * (0.5f + 0.5f * MathF.Cos(flapPhase));
        TurnToward(-MathF.PI / 2, 3f, dt);
    }

    private void TakeOff(World world)
    {
        mode = Mode.Flying;
        perchWindow = IntPtr.Zero;
        vel = new PointF(Rand(-0.5f, 0.5f) * cruiseSpeed, -cruiseSpeed * 1.2f);
        PickGoal(world);
    }

    private void PickGoal(World world)
    {
        var area = world.Screens[rng.Next(world.Screens.Length)].WorkingArea;
        float m = span;
        goal = new PointF(Rand(area.Left + m, area.Right - m), Rand(area.Top + m, area.Bottom - m));
        timer = Rand(3f, 8f);
    }

    private void TryPickPerch(World world)
    {
        // Prefer the top edge of whatever window is active; otherwise the top of the taskbar.
        var fg = GetForegroundWindow();
        if (rng.Next(3) > 0 && fg != IntPtr.Zero && IsGoodPerch(fg, out var r))
        {
            perchWindow = fg;
            perchRect = r;
            goal = new PointF(Rand(r.Left + span, r.Right - span), r.Top - span * 0.14f);
        }
        else
        {
            var screen = world.Screens[rng.Next(world.Screens.Length)];
            if (screen.WorkingArea.Bottom >= screen.Bounds.Bottom) return; // no bottom taskbar here
            perchWindow = IntPtr.Zero;
            goal = new PointF(Rand(screen.WorkingArea.Left + span, screen.WorkingArea.Right - span),
                              screen.WorkingArea.Bottom - span * 0.14f);
        }
        mode = Mode.Landing;
    }

    private bool IsGoodPerch(IntPtr hwnd, out RECT r)
    {
        r = default;
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd)) return false;
        var cls = ClassNameOf(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!TryGetFrameBounds(hwnd, out r)) return false;
        if (r.Width < span * 4 || r.Height < span * 2) return false;
        // Skip maximized/fullscreen windows whose top edge is at the very top of the screen.
        var top = r.Top;
        return !Screen.AllScreens.Any(s => Math.Abs(s.Bounds.Top - top) < span * 0.5f);
    }

    private bool PerchStillThere()
    {
        if (perchWindow == IntPtr.Zero) return true;
        if (!IsWindow(perchWindow) || !IsWindowVisible(perchWindow) || IsIconic(perchWindow)) return false;
        if (!TryGetFrameBounds(perchWindow, out var r)) return false;
        return r.Top == perchRect.Top && r.Left == perchRect.Left && r.Right == perchRect.Right;
    }

    private void Steer(float dir, float speed, float responsiveness, float dt)
    {
        var desired = new PointF(MathF.Cos(dir) * speed, MathF.Sin(dir) * speed);
        float k = Math.Min(1f, responsiveness * dt);
        vel = new PointF(vel.X + (desired.X - vel.X) * k, vel.Y + (desired.Y - vel.Y) * k);
    }

    private void Flap(float hz, float dt) => flapPhase = (flapPhase + MathF.Tau * hz * dt) % MathF.Tau;

    private void Move(float dt) => Pos = new PointF(Pos.X + vel.X * dt, Pos.Y + vel.Y * dt);

    private void TurnToward(float target, float rate, float dt)
    {
        float diff = MathF.IEEERemainder(target - heading, MathF.Tau);
        heading += diff * Math.Min(1f, rate * dt);
    }

    private float Rand(float min, float max) => min + (float)rng.NextDouble() * (max - min);

    private static float Dist(PointF a, PointF b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
