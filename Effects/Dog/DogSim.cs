using System.Numerics;

namespace WallpaperStudio.Effects.Dog;

/// <summary>
/// The frisbee game. Someone just past the screen edge throws a frisbee; the dog gallops after it,
/// leaps and snatches it out of the air (or fetches it off the ground if it lands first), trots
/// back, drops it, and sits wagging until the next throw.
/// </summary>
internal sealed class DogSim : IPlayScene
{
    public const float W = 540, H = 220, Ground = H - 10;

    private const float Gravity = 1150;      // dog
    private const float DiscGravity = 200;   // frisbees float
    private const float RunSpeed = 340, TrotSpeed = 115;
    private const float CatchRadius = 24;
    private const float MinX = 70, MaxX = W - 70; // keeps the whole dog inside the area

    private enum Mode { Wait, Chase, Leap, Fetch, PickUp, Return, Drop }
    private enum Disc { Thrower, Flying, Grounded, Mouth, Taken }

    private readonly Random rng;
    private Mode mode;
    private float modeTime, modeDuration, time;

    private bool homeRight = true, wantHomeRight = true;
    private float dogX, dogH, dvx, dvy, facing, turn, speed;
    private DogPose pose;
    private Vector2 look;
    private float blinkTimer = 2, blink;

    private Disc disc = Disc.Thrower;
    private bool clumsy;
    private float fx, fh, fvx, fvy, fspin, ftilt;
    private Vector2 takenFrom;

    private float HomeX => homeRight ? W - 95 : 95;
    private float ThrowerSide => homeRight ? 1 : -1;   // the thrower stands just past this edge
    private float EdgeX => homeRight ? W + 40 : -40;

    public DogSim(Random rng)
    {
        this.rng = rng;
        dogX = HomeX;
        facing = turn = ThrowerSide;
        pose = new DogPose { Sit = 1 };
        look = new Vector2(EdgeX, Ground - 80);
        Enter(Mode.Wait, Rand(1f, 2f));
    }

    public void SetPlacement(ScreenPlacement placement) =>
        wantHomeRight = placement switch
        {
            ScreenPlacement.BottomLeft or ScreenPlacement.TopLeft => false,
            ScreenPlacement.Center => wantHomeRight,
            _ => true,
        };

    public void Update(float dt, Vector2? attention)
    {
        time += dt;
        UpdateDisc(dt);
        UpdateDog(dt);

        turn += Math.Clamp(facing - turn, -dt * 9f, dt * 9f);
        var target = attention is { } a && mode is Mode.Wait ? a : LookTarget();
        look = Vector2.Lerp(look, target, Math.Min(1f, 10f * dt));
        pose.Look = new Vector2((look.X - dogX) * facing, look.Y - (Ground - dogH));

        if (disc == Disc.Mouth)
        {
            var m = MouthWorld();
            fx = m.X;
            fh = Ground - m.Y;
        }
    }

    // ---------- Frisbee ----------

    private void Throw()
    {
        float startX = homeRight ? W + 25 : -25, h0 = 95;
        // Pick a landing spot out in the field, then solve for the throw that gets there.
        float land = HomeX - ThrowerSide * Rand(W * 0.3f, W * 0.68f);
        land = Math.Clamp(land, 40, W - 40);
        float vy = Rand(120f, 230f);
        float t = (vy + MathF.Sqrt(vy * vy + 2 * DiscGravity * h0)) / DiscGravity;
        fx = startX; fh = h0; fvx = (land - startX) / t; fvy = vy;
        disc = Disc.Flying;
        // Now and then the dog misjudges the jump and has to go fetch it instead.
        clumsy = rng.NextDouble() < 0.08;
    }

    private void UpdateDisc(float dt)
    {
        switch (disc)
        {
            case Disc.Flying:
                fvy -= DiscGravity * dt;
                fh += fvy * dt;
                fx += fvx * dt;
                fspin += dt * 24;
                ftilt = -6 * MathF.Sign(fvx) + MathF.Sin(time * 7) * 4; // a little flutter
                if (fh <= 0)
                {
                    fh = 0;
                    fvx *= 0.45f;
                    disc = Disc.Grounded;
                }
                break;

            case Disc.Grounded:
                fvx *= MathF.Exp(-6 * dt);
                fx = Math.Clamp(fx + fvx * dt, 15, W - 15);
                ftilt *= 1 - Math.Min(1, 8 * dt);
                break;

            case Disc.Taken:
            {
                // The thrower picks it back up just past the edge.
                float u = Math.Min(1, modeTime / 0.45f);
                fx = takenFrom.X + (EdgeX - takenFrom.X) * u;
                fh = takenFrom.Y + MathF.Sin(MathF.PI * u * 0.5f) * 70;
                if (u >= 1) disc = Disc.Thrower;
                break;
            }
        }
    }

    /// <summary>Where the frisbee will be when it comes down to <paramref name="height"/>.</summary>
    private float PredictCatchX(float height)
    {
        float x = fx, h = fh, vx = fvx, vy = fvy;
        for (int i = 0; i < 600; i++)
        {
            const float step = 1 / 120f;
            vy -= DiscGravity * step;
            h += vy * step;
            x += vx * step;
            if ((vy < 0 && h < height) || h <= 0) break;
        }
        return x;
    }

    private Vector2 MouthWorld()
    {
        var m = DogArt.MouthLocal(pose);
        return new Vector2(dogX + m.X * facing, Ground - dogH + m.Y);
    }

    private Vector2 LookTarget() => disc switch
    {
        Disc.Flying or Disc.Grounded => new Vector2(fx, Ground - fh),
        Disc.Mouth => new Vector2(HomeX + ThrowerSide * 200, Ground - 60),
        _ => new Vector2(EdgeX, Ground - 90), // watching the thrower
    };

    // ---------- Dog ----------

    private void UpdateDog(float dt)
    {
        modeTime += dt;
        float sitTarget = 0, bowTarget = 0, mouthTarget = 0, pantTarget = 0, tailUpTarget = 0.8f, headDownTarget = 0;
        float wagSpeed = 6;
        bool moving = false;

        switch (mode)
        {
            case Mode.Wait:
                facing = ThrowerSide;
                sitTarget = 1; pantTarget = 1; mouthTarget = 0.45f; tailUpTarget = 0; wagSpeed = 14;
                if (wantHomeRight != homeRight) { Enter(Mode.Return, 0); break; }
                // Up and into a play bow just before the throw.
                if (modeDuration - modeTime < 0.7f) { sitTarget = 0; bowTarget = 1; pantTarget = 0; mouthTarget = 0.2f; tailUpTarget = 1; }
                if (modeTime >= modeDuration && disc == Disc.Thrower)
                {
                    Throw();
                    Enter(Mode.Chase, 0);
                }
                break;

            case Mode.Chase:
            {
                if (disc == Disc.Grounded) { Enter(Mode.Fetch, 0); break; }
                var mouth = MouthWorld();
                float mouthHeight = Ground - mouth.Y;
                // Stand so the mouth is at the catch point while facing the incoming frisbee.
                float catchX = PredictCatchX(mouthHeight + 55);
                float faceDisc = MathF.Sign(fx - catchX);
                if (faceDisc == 0) faceDisc = facing;
                float reach = MathF.Abs(DogArt.MouthLocal(pose).X);
                moving = Run(catchX - faceDisc * reach, RunSpeed, dt, out bool there);
                if (there || MathF.Abs(catchX - faceDisc * reach - dogX) < 12) facing = faceDisc;
                mouth = MouthWorld();
                mouthTarget = 0.7f; tailUpTarget = 1;

                // Low throw that comes right to the mouth: snatch it without jumping.
                if (disc == Disc.Flying && Vector2.Distance(mouth, new Vector2(fx, Ground - fh)) < CatchRadius)
                {
                    disc = Disc.Mouth;
                    Enter(Mode.Return, 0);
                    break;
                }

                // Plan an intercept: where will the frisbee be at the top of a leap started now?
                // Only once it's coming down, so the dog has to run it down first.
                if (disc == Disc.Flying && fvy < 0)
                {
                    // Guess the apex time, then refine it once from the lift that implies.
                    float apexTime = 0.26f, discX = 0, discH, mouthX = 0, lift = 0;
                    for (int pass = 0; pass < 2; pass++)
                    {
                        discX = fx + fvx * apexTime;
                        discH = fh + fvy * apexTime - 0.5f * DiscGravity * apexTime * apexTime;
                        mouthX = mouth.X + facing * speed * 0.5f * apexTime;
                        lift = discH - mouthHeight;
                        apexTime = MathF.Sqrt(2 * Gravity * MathF.Max(1, lift)) / Gravity;
                    }
                    if (lift > 8 && lift < 150 && MathF.Abs(discX - mouthX) < 60)
                    {
                        dvy = MathF.Sqrt(2 * Gravity * (clumsy ? lift + 55 : lift));
                        dvx = facing * speed * 0.5f;
                        Enter(Mode.Leap, 0);
                    }
                }
                break;
            }

            case Mode.Leap:
                dogH += dvy * dt;
                dvy -= Gravity * dt;
                // Steer in the air so the mouth meets the frisbee (real dogs twist to adjust).
                if (disc == Disc.Flying && !clumsy)
                {
                    float gap = fx - MouthWorld().X;
                    dvx += Math.Clamp(gap * 10f - dvx * 0.5f, -1400f, 1400f) * dt;
                    dvx = Math.Clamp(dvx, -420f, 420f);
                }
                dogX = Math.Clamp(dogX + dvx * dt, MinX, MaxX);
                mouthTarget = disc == Disc.Mouth ? 0.15f : 0.85f;
                tailUpTarget = 1;
                if (disc == Disc.Flying && Vector2.Distance(MouthWorld(), new Vector2(fx, Ground - fh)) < CatchRadius)
                    disc = Disc.Mouth; // caught it!
                if (dogH <= 0)
                {
                    dogH = 0;
                    pose.Squash = 1;
                    speed = MathF.Abs(dvx) * 0.5f;
                    Enter(disc switch { Disc.Mouth => Mode.Return, Disc.Flying => Mode.Chase, _ => Mode.Fetch }, 0);
                }
                break;

            case Mode.Fetch:
            {
                float target = fx - MathF.Sign(fx - dogX) * 32;
                moving = Run(target, RunSpeed * 0.8f, dt, out bool arrived);
                tailUpTarget = 1; mouthTarget = 0.3f;
                if (arrived) { facing = MathF.Sign(fx - dogX); Enter(Mode.PickUp, 0.45f); }
                break;
            }

            case Mode.PickUp:
                headDownTarget = 1;
                if (modeTime > 0.25f && disc == Disc.Grounded) disc = Disc.Mouth;
                if (modeTime >= modeDuration) Enter(Mode.Return, 0);
                break;

            case Mode.Return:
            {
                homeRight = wantHomeRight;
                moving = Run(HomeX, TrotSpeed, dt, out bool arrived);
                mouthTarget = disc == Disc.Mouth ? 0.15f : 0.3f;
                pantTarget = disc == Disc.Mouth ? 0 : 0.6f;
                tailUpTarget = 0.9f; wagSpeed = 11;
                if (arrived)
                {
                    facing = ThrowerSide;
                    if (disc == Disc.Mouth) Enter(Mode.Drop, 0.7f);
                    else Enter(Mode.Wait, Rand(1.2f, 2.4f));
                }
                break;
            }

            case Mode.Drop:
                headDownTarget = modeTime < 0.35f ? 1 : 0;
                tailUpTarget = 0.9f; wagSpeed = 14;
                if (modeTime > 0.3f && disc == Disc.Mouth) { disc = Disc.Grounded; fh = 0; fvx = 0; }
                if (modeTime >= modeDuration)
                {
                    takenFrom = new Vector2(fx, fh);
                    disc = Disc.Taken;
                    Enter(Mode.Wait, Rand(1.4f, 2.8f));
                }
                break;
        }

        if (!moving) speed *= 1 - Math.Min(1f, 6f * dt);

        float k = Math.Min(1f, 7f * dt);
        pose.Sit += (sitTarget - pose.Sit) * Math.Min(1f, 4f * dt);
        pose.Bow += (bowTarget - pose.Bow) * k;
        pose.MouthOpen += (mouthTarget - pose.MouthOpen) * Math.Min(1f, 12f * dt);
        pose.Pant += (pantTarget - pose.Pant) * k;
        pose.TailUp += (tailUpTarget - pose.TailUp) * k;
        pose.HeadDown += (headDownTarget - pose.HeadDown) * Math.Min(1f, 10f * dt);
        pose.GaitBlend += ((moving && speed > 12 ? 1f : 0f) - pose.GaitBlend) * Math.Min(1f, 10f * dt);
        pose.Gallop += ((speed > 170 ? 1f : 0f) - pose.Gallop) * Math.Min(1f, 4f * dt);
        pose.Air += ((mode == Mode.Leap ? 1f : 0f) - pose.Air) * Math.Min(1f, 14f * dt);
        float pitchTarget = mode == Mode.Leap ? Math.Clamp(dvy / 420f, -0.5f, 1f) * 24f : 0f;
        pose.Pitch += (pitchTarget - pose.Pitch) * Math.Min(1f, 10f * dt);
        pose.Squash = Math.Max(0, pose.Squash - dt * 4);
        pose.TailPhase += dt * wagSpeed;

        blinkTimer -= dt;
        if (blinkTimer <= 0) { blink = 1; blinkTimer = Rand(2.5f, 5.5f); }
        blink = Math.Max(0, blink - dt * 7);
        pose.Blink = blink;
    }

    /// <summary>Runs toward <paramref name="target"/>, easing speed in and out. Returns true while moving.</summary>
    private bool Run(float target, float maxSpeed, float dt, out bool arrived)
    {
        target = Math.Clamp(target, MinX, MaxX);
        float diff = target - dogX;
        arrived = MathF.Abs(diff) < 3;
        if (arrived) return false;

        facing = MathF.Sign(diff);
        float want = Math.Min(maxSpeed, MathF.Abs(diff) * 4f + 30f);
        speed += (want - speed) * Math.Min(1f, 6f * dt);
        float dx = Math.Clamp(diff, -speed * dt, speed * dt);
        dogX += dx;

        // Legs advance with distance so paws never skate.
        float cycle = pose.Gallop > 0.5f ? 56f / 0.42f : 30f / 0.55f;
        pose.GaitPhase += MathF.Abs(dx) / cycle * MathF.Tau;
        return true;
    }

    private void Enter(Mode next, float duration)
    {
        mode = next;
        modeTime = 0;
        modeDuration = duration;
    }

    private float Rand(float min, float max) => min + (float)rng.NextDouble() * (max - min);

    // ---------- Drawing ----------

    public void Draw(Graphics g)
    {
        DogArt.DrawShadow(g, dogX, Ground, 120, dogH);
        bool discVisible = disc is not (Disc.Thrower or Disc.Mouth);
        if (discVisible)
        {
            float shadowW = 26 * (1 - Math.Min(0.6f, fh / 200f));
            using var shadow = new SolidBrush(Color.FromArgb((int)(60 * (1 - Math.Min(0.8f, fh / 160f))), 0, 0, 0));
            g.FillEllipse(shadow, fx - shadowW / 2, Ground - 3, shadowW, 6);
        }

        var state = g.Save();
        g.TranslateTransform(dogX, Ground - dogH);
        float sx = MathF.Abs(turn) < 0.12f ? 0.12f * MathF.Sign(facing) : turn;
        g.ScaleTransform(sx, 1);
        DogArt.Draw(g, pose);
        g.Restore(state);

        if (discVisible)
            DogArt.DrawFrisbee(g, new PointF(fx, Ground - fh - (disc == Disc.Grounded ? 3 : 0)), disc == Disc.Grounded ? 0 : ftilt, fspin);
        else if (disc == Disc.Mouth)
        {
            // Held by the rim: tipped up and pointing forward from the mouth.
            var m = MouthWorld();
            DogArt.DrawFrisbee(g, new PointF(m.X + facing * 6, m.Y + 2), facing * 62, 0.8f);
        }
    }
}
