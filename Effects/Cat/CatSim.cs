using System.Numerics;

namespace WallpaperStudio.Effects.Cat;

/// <summary>
/// The cat-and-cotton-ball play scene: ball physics plus the cat's behavior. It stalks, wiggles,
/// swats the ball away, walks or pounces after it, and now and then sits down for a rest.
/// Pure simulation + drawing in logical pixels, so the overlay and the preview tile share it.
/// </summary>
internal sealed class CatSim : IPlayScene
{
    public const float W = 440, H = 170, Ground = H - 10;

    private const float WallL = 18, WallR = W - 18;
    private const float MinX = 55, MaxX = W - 55;
    private const float Reach = 60;      // ball distance in front of the cat's middle for a swat
    private const float BallR = 10;
    private const float Gravity = 900;
    private const float WalkSpeed = 70;
    private const float StrideCycle = 22f / 0.6f; // distance covered per walk cycle (see CatArt.Paw)

    private enum Mode { Watch, Stalk, Swat, Walk, Pounce, Rest }

    private readonly Random rng;
    private Mode mode;
    private float modeTime, modeDuration;

    private float catX, facing = -1, turn = -1, jumpY, speed;
    private float pounceFrom, pounceTo;
    private bool swatHit;
    private float sinceRest, blinkTimer = 2, blink, tiltTimer = 3, tiltTarget;
    private Vector2 look;
    private CatPose pose;

    private float ballX, ballH, ballVx, ballVy, ballSpin;

    public CatSim(Random rng)
    {
        this.rng = rng;
        catX = W - 110;
        ballX = catX - Reach;
        look = new Vector2(ballX, Ground - BallR);
        pose = new CatPose { SwatT = -1, Pupil = 0.5f };
        Enter(Mode.Stalk, 1.2f);
    }

    /// <param name="attention">Something more interesting than the ball (the mouse), in play-area coords.</param>
    public void Update(float dt, Vector2? attention)
    {
        UpdateBall(dt);
        UpdateCat(dt);

        // Smoothly turn around (squash through the middle), ease the gaze, breathe.
        turn += Math.Clamp(facing - turn, -dt * 8f, dt * 8f);
        var target = attention is { } a && mode is Mode.Watch or Mode.Rest ? a : new Vector2(ballX, Ground - BallR - ballH);
        look = Vector2.Lerp(look, target, Math.Min(1f, 9f * dt));
        pose.Breath += dt * 2.2f;
    }

    public void Draw(Graphics g)
    {
        CatArt.DrawShadow(g, catX, Ground, 110, jumpY);
        CatArt.DrawShadow(g, ballX, Ground, BallR * 2.4f, ballH);

        bool ballInFront = BallAhead > Reach - 18;
        if (!ballInFront) DrawBall(g);

        var state = g.Save();
        g.TranslateTransform(catX, Ground - jumpY);
        float sx = MathF.Abs(turn) < 0.12f ? 0.12f * MathF.Sign(facing) : turn;
        g.ScaleTransform(sx, 1);
        pose.Look = new Vector2((look.X - catX) * MathF.Sign(sx), look.Y - (Ground - jumpY));
        CatArt.Draw(g, pose);
        g.Restore(state);

        if (ballInFront) DrawBall(g);
    }

    private void DrawBall(Graphics g) =>
        CatArt.DrawCottonBall(g, new PointF(ballX, Ground - BallR - ballH), BallR, ballSpin);

    // ---------- Ball ----------

    private void UpdateBall(float dt)
    {
        if (ballH > 0 || ballVy > 0)
        {
            ballVy -= Gravity * dt;
            ballH += ballVy * dt;
            if (ballH <= 0)
            {
                ballH = 0;
                ballVy = ballVy < -70 ? -ballVy * 0.38f : 0; // soft cotton barely bounces
            }
        }

        ballVx *= MathF.Exp(-(ballH > 0 ? 0.4f : 1.7f) * dt);
        // Rolling resistance so the ball settles instead of creeping for seconds.
        if (ballH == 0) ballVx -= MathF.Sign(ballVx) * Math.Min(MathF.Abs(ballVx), 70f * dt);
        if (MathF.Abs(ballVx) < 4 && ballH == 0) ballVx = 0;
        ballX += ballVx * dt;
        ballSpin += ballVx * dt / BallR;

        if (ballX < WallL + BallR) { ballX = WallL + BallR; ballVx = MathF.Abs(ballVx) * 0.55f; }
        if (ballX > WallR - BallR) { ballX = WallR - BallR; ballVx = -MathF.Abs(ballVx) * 0.55f; }
    }

    private bool BallResting => MathF.Abs(ballVx) < 20 && ballH == 0 && ballVy == 0;

    private float BallAhead => (ballX - catX) * facing;

    // ---------- Cat ----------

    private void UpdateCat(float dt)
    {
        modeTime += dt;
        sinceRest += dt;
        float crouchTarget = 0.35f, sitTarget = 0f, gaitTarget = 0f, airTarget = 0f;
        float tailSpeed = 2.4f;
        pose.SwatT = -1;
        pose.Wiggle = 0;

        switch (mode)
        {
            case Mode.Watch:
                FaceBall();
                pose.TailEnergy = 0.4f;
                if (BallResting && modeTime > 0.35f) Enter(Mode.Stalk, Rand(0.6f, 1.4f));
                break;

            case Mode.Stalk:
                FaceBall();
                crouchTarget = 1f;
                tailSpeed = 5f;
                pose.TailEnergy = 1f;
                // The classic butt wiggle right before committing.
                float left = modeDuration - modeTime;
                if (left < 0.6f) pose.Wiggle = MathF.Sin(modeTime * 24f) * 2f * Math.Min(1f, (0.6f - left) * 4);
                if (!BallResting) { Enter(Mode.Watch, 0); break; }
                if (modeTime >= modeDuration) DecideNextMove();
                break;

            case Mode.Swat:
                crouchTarget = 0.6f;
                tailSpeed = 4f;
                pose.SwatT = Math.Min(1f, modeTime / 0.5f);
                pose.SwatTarget = new Vector2(BallAhead - 7, -(BallR * 2 + ballH) + 5);
                if (!swatHit && pose.SwatT >= 0.6f)
                {
                    swatHit = true;
                    Hit();
                }
                if (modeTime >= 0.5f) Enter(Mode.Watch, 0);
                break;

            case Mode.Walk:
            {
                crouchTarget = 0.1f;
                gaitTarget = 1f;
                pose.TailEnergy = 0.25f;
                var (target, standFacing) = StandSpot();
                float diff = target - catX;
                if (MathF.Abs(diff) > 3) facing = MathF.Sign(diff);
                // Ease in and out instead of snapping to full speed.
                float cruise = MathF.Abs(diff) > 140 ? WalkSpeed * 1.6f : WalkSpeed; // trot when far
                float want = Math.Min(cruise, MathF.Abs(diff) * 3f + 12f);
                speed += (want - speed) * Math.Min(1f, 5f * dt);
                float dx = Math.Clamp(diff, -speed * dt, speed * dt);
                catX += dx;
                pose.GaitPhase += MathF.Abs(dx) / StrideCycle * MathF.Tau;
                if (MathF.Abs(diff) < 1.5f || modeTime > 7)
                {
                    facing = standFacing;
                    speed = 0;
                    Enter(Mode.Stalk, Rand(0.6f, 1.4f));
                }
                else if (MathF.Abs(ballVx) > 40) Enter(Mode.Watch, 0);
                break;
            }

            case Mode.Pounce:
            {
                float u = Math.Min(1f, modeTime / 0.55f);
                float ease = u * u * (3 - 2 * u);
                catX = pounceFrom + (pounceTo - pounceFrom) * ease;
                jumpY = MathF.Sin(MathF.PI * u) * 36f;
                airTarget = MathF.Sin(MathF.PI * u);
                crouchTarget = 0.2f;
                pose.TailEnergy = 0.7f;
                if (u >= 1f)
                {
                    jumpY = 0;
                    pose.Squash = 1f;
                    // Land on the ball and send it skittering.
                    if (MathF.Abs(BallAhead - Reach) < 30)
                    {
                        ballVx = facing * Rand(40f, 140f);
                        ballVy = Rand(40f, 120f);
                    }
                    Enter(Mode.Watch, 0);
                }
                break;
            }

            case Mode.Rest:
                crouchTarget = 0f;
                sitTarget = 1f;
                tailSpeed = 1.1f;
                pose.TailEnergy = 0f;
                if (modeTime >= modeDuration) { sinceRest = 0; Enter(Mode.Watch, 0); }
                break;
        }

        float k = Math.Min(1f, 7f * dt);
        pose.Crouch += (crouchTarget - pose.Crouch) * k;
        pose.Sit += (sitTarget - pose.Sit) * Math.Min(1f, 3.5f * dt);
        pose.GaitBlend += (gaitTarget - pose.GaitBlend) * Math.Min(1f, 10f * dt);
        pose.Air = mode == Mode.Pounce ? airTarget : pose.Air * (1 - k);
        pose.Squash = Math.Max(0, pose.Squash - dt * 4f);
        pose.TailPhase += dt * tailSpeed;
        pose.Pupil += ((mode is Mode.Stalk or Mode.Pounce or Mode.Swat ? 1f : 0.35f) - pose.Pupil) * Math.Min(1f, 4f * dt);

        // Curious head tilts while watching or resting.
        tiltTimer -= dt;
        if (tiltTimer <= 0)
        {
            tiltTarget = mode is Mode.Watch or Mode.Rest && rng.NextDouble() < 0.6 ? Rand(-10f, 10f) : 0f;
            tiltTimer = Rand(1.5f, 4f);
        }
        if (mode is not (Mode.Watch or Mode.Rest)) tiltTarget = 0;
        pose.HeadTilt += (tiltTarget - pose.HeadTilt) * Math.Min(1f, 4f * dt);

        UpdateBlink(dt);
        catX = Math.Clamp(catX, MinX, MaxX);
    }

    private void Hit()
    {
        if (MathF.Abs(BallAhead - Reach) >= 24 || ballH >= 8) return;
        // Ball pinned near a wall? Hook it back over the cat's head instead.
        float roomAhead = facing > 0 ? WallR - ballX : ballX - WallL;
        if (roomAhead < 90)
        {
            ballVx = -facing * Rand(120f, 220f);
            ballVy = Rand(300f, 380f);
        }
        else
        {
            ballVx = facing * Rand(150f, 360f);
            ballVy = Rand(100f, 300f);
        }
    }

    private void DecideNextMove()
    {
        float ahead = BallAhead;
        if (sinceRest > 25 && rng.NextDouble() < 0.35)
            Enter(Mode.Rest, Rand(5f, 9f));
        else if (MathF.Abs(ahead - Reach) < 14)
        {
            swatHit = false;
            Enter(Mode.Swat, 0.5f);
        }
        else if (ahead > Reach + 30 && ahead < 200 && StandSpot().Facing == facing && rng.NextDouble() < 0.6)
        {
            pounceFrom = catX;
            pounceTo = StandSpot().X;
            Enter(Mode.Pounce, 0.55f);
        }
        else
            Enter(Mode.Walk, 0);
    }

    /// <summary>
    /// Where the cat should stand to swat the ball: on whichever side of it fits inside the
    /// play area, preferring the side the cat is already on.
    /// </summary>
    private (float X, float Facing) StandSpot()
    {
        float right = ballX + Reach, left = ballX - Reach;
        bool rightOk = right <= MaxX, leftOk = left >= MinX;
        if (rightOk && (!leftOk || MathF.Abs(right - catX) <= MathF.Abs(left - catX))) return (right, -1);
        if (leftOk) return (left, 1);
        return (Math.Clamp(right, MinX, MaxX), -1);
    }

    private void FaceBall()
    {
        // Only turn around once the ball is clearly behind the cat's nose.
        if (BallAhead < 15) facing = -facing;
    }

    private void UpdateBlink(float dt)
    {
        blinkTimer -= dt;
        if (blinkTimer <= 0)
        {
            blink = 1f;
            blinkTimer = Rand(2.5f, 6f);
        }
        blink = Math.Max(0, blink - dt * 6f);
        // Slow, sleepy "cat kisses" while resting.
        float sleepy = mode == Mode.Rest ? 0.45f + 0.35f * MathF.Max(0, MathF.Sin(modeTime * 0.9f)) : 0f;
        pose.Blink = Math.Max(blink, sleepy);
    }

    private void Enter(Mode next, float duration)
    {
        mode = next;
        modeTime = 0;
        modeDuration = duration;
    }

    private float Rand(float min, float max) => min + (float)rng.NextDouble() * (max - min);
}
