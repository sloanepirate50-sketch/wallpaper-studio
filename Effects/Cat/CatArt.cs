using System.Drawing.Drawing2D;
using System.Numerics;

namespace WallpaperStudio.Effects.Cat;

/// <summary>Everything needed to draw one frame of the cat, in its own local space.</summary>
internal struct CatPose
{
    public float Crouch;      // 0 = standing, 1 = low stalking crouch
    public float Sit;         // 0..1 sitting down
    public float Wiggle;      // butt-wiggle offset before a pounce
    public float GaitPhase;   // walk cycle (radians), advanced by distance travelled
    public float GaitBlend;   // 0 = legs planted, 1 = full walk cycle
    public float SwatT;       // 0..1 progress of a paw swat, < 0 when not swatting
    public Vector2 SwatTarget;
    public float Air;         // 0..1 mid-leap stretch
    public float Squash;      // landing squash, decays to 0
    public Vector2 Look;      // where the eyes look (local coords)
    public float Blink;       // 0 open .. 1 closed
    public float Pupil;       // 0 slit .. 1 big round hunting pupils
    public float TailPhase;
    public float TailEnergy;  // 0 lazy sway .. 1 twitchy
    public float HeadTilt;    // degrees
    public float Breath;
}

/// <summary>
/// Procedural side-view ginger tabby. Shapes are smooth splines and legs are two-jointed (solved
/// with simple IK), so poses blend naturally. Local space: origin on the ground under the cat's
/// middle, facing +x, y up is negative. The caller mirrors for facing left.
/// </summary>
internal static class CatArt
{
    private static readonly Color Fur = Color.FromArgb(243, 166, 88);
    private static readonly Color FurLight = Color.FromArgb(250, 190, 120);
    private static readonly Color FurShade = Color.FromArgb(212, 128, 58);
    private static readonly Color FarFur = Color.FromArgb(200, 116, 50);
    private static readonly Color StripeColor = Color.FromArgb(205, 186, 98, 36);
    private static readonly Color Belly = Color.FromArgb(254, 234, 208);
    private static readonly Color Outline = Color.FromArgb(92, 52, 26);
    private static readonly Color Pink = Color.FromArgb(243, 158, 170);

    private const float EdgeWidth = 1.8f;
    private static readonly Pen Edge = new(Outline, EdgeWidth) { LineJoin = LineJoin.Round };
    private static readonly Pen ThickEdge = new(Outline, EdgeWidth * 2) { LineJoin = LineJoin.Round };
    private static readonly Pen FineEdge = new(Outline, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
    private static readonly Pen StripePen = new(StripeColor, 3.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    private static readonly Pen FaceStripe = new(StripeColor, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    private static readonly Pen Whisker = new(Color.FromArgb(230, 255, 250, 240), 0.9f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    private static readonly SolidBrush FurBrush = new(Fur);
    private static readonly SolidBrush FarBrush = new(FarFur);
    private static readonly SolidBrush BellyBrush = new(Belly);
    private static readonly SolidBrush PinkBrush = new(Pink);
    private static readonly SolidBrush InnerEar = new(Color.FromArgb(235, 150, 155));
    private static readonly SolidBrush TipBrush = new(Color.FromArgb(196, 106, 40));
    private static readonly SolidBrush IrisBrush = new(Color.FromArgb(172, 220, 92));
    private static readonly SolidBrush PupilBrush = new(Color.FromArgb(28, 22, 16));
    private static readonly SolidBrush Shine = new(Color.White);

    private static Vector2 V(float x, float y) => new(x, y);
    private static PointF P(Vector2 v) => new(v.X, v.Y);
    private static PointF[] P(Vector2[] vs) => Array.ConvertAll(vs, P);

    public static void DrawShadow(Graphics g, float x, float groundY, float width, float lift)
    {
        float k = 1f - Math.Min(0.45f, lift / 90f);
        float w = width * k, h = 10 * k;
        using var path = new GraphicsPath();
        path.AddEllipse(x - w / 2, groundY - h / 2, w, h);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb((int)(80 * k), 0, 0, 0),
            SurroundColors = [Color.FromArgb(0, 0, 0, 0)],
        };
        g.FillPath(brush, path);
    }

    public static void Draw(Graphics g, in CatPose p)
    {
        float c = p.Crouch, s = p.Sit;
        float bob = p.GaitBlend * MathF.Sin(p.GaitPhase * 2) * 1.3f + MathF.Sin(p.Breath) * 0.5f * (1 - p.GaitBlend);
        float lean = p.SwatT >= 0 ? MathF.Sin(MathF.PI * p.SwatT) * 4f : 0f;

        // --- Skeleton: hip and shoulder drive everything else.
        var hip = V(-28, -36) + V(0, c * 6) + V(p.Wiggle, -MathF.Abs(p.Wiggle) * 0.6f);
        var sh = V(22, -37) + V(c * 3, c * 13);
        hip = Vector2.Lerp(hip, V(-20, -17), s);
        sh = Vector2.Lerp(sh, V(13, -41), s);
        sh += V(lean, lean * 0.5f);
        hip += V(-p.Air * 4, -p.Air * 2);
        sh += V(p.Air * 4, -p.Air * 2);
        var drop = V(0, p.Squash * 5 + bob);
        hip += drop;
        sh += drop;

        var dir = Vector2.Normalize(sh - hip);
        var up = V(dir.Y, -dir.X);
        var mid = (hip + sh) / 2;

        var head = Vector2.Lerp(Vector2.Lerp(sh + V(17, -15), sh + V(22, -6), c), sh + V(8, -17), s) + V(lean * 1.5f, lean);

        // --- Legs: paw targets (gait / leap / swat), then IK.
        var frontRoot = sh + dir * 5 - up * 4;
        var hindRoot = hip - dir * 1 - up * 6;
        float frontBase = Lerp(sh.X + 6, sh.X + 3, s);
        float hindBase = Lerp(hip.X - 1, hip.X + 18, s);

        var nearFront = Paw(p, frontBase, 0.25f, front: true, hip, sh);
        var farFront = Paw(p, frontBase + 3, 0.75f, front: true, hip, sh);
        var nearHind = Paw(p, hindBase, 0f, front: false, hip, sh);
        var farHind = Paw(p, hindBase + 3, 0.5f, front: false, hip, sh);
        if (p.SwatT >= 0) nearFront = SwatPaw(p.SwatT, nearFront, p.SwatTarget);

        // Far-side legs sit behind the body in a darker shade.
        DrawLeg(g, frontRoot + V(3, 0), farFront, 19, 20, 7.5f, FarBrush);
        DrawLeg(g, hindRoot + V(3, 0), farHind, 15, 17, 8f, FarBrush);

        // Standing, the tail is behind the body; sitting, it wraps around the front (drawn later).
        bool tailInFront = s > 0.5f;
        if (!tailInFront) DrawTail(g, p, hip, sh, dir, up);

        // --- Body + neck: outline pass first, then fill, so the shapes merge without seams.
        Vector2[] body =
        [
            hip - dir * 19 + up * 1,
            hip - dir * 9 + up * 15,
            mid + up * (14.5f - c * 1.5f),
            sh + dir * 2 + up * 15,
            sh + dir * 15 + up * 3,
            sh + dir * 9 - up * 11,
            mid - up * 11.5f,
            hip - dir * 3 - up * 13,
        ];
        Vector2[] neck = [sh + dir * 1 + up * 13, head + V(-11, 3), head + V(3, 12), sh + dir * 14 + up * 1];
        var bodyPts = P(body);
        var neckPts = P(neck);
        g.DrawClosedCurve(ThickEdge, bodyPts, 0.6f, FillMode.Winding);
        g.DrawClosedCurve(ThickEdge, neckPts, 0.6f, FillMode.Winding);

        using (var shade = new LinearGradientBrush(P(mid + up * 17), P(mid - up * 14), FurLight, FurShade))
            g.FillClosedCurve(shade, bodyPts, FillMode.Winding, 0.6f);
        g.FillClosedCurve(FurBrush, neckPts, FillMode.Winding, 0.6f);

        // Belly patch and tabby stripes, clipped to the body.
        using (var bodyPath = new GraphicsPath())
        {
            bodyPath.AddClosedCurve(bodyPts, 0.6f);
            var saved = g.Save();
            g.SetClip(bodyPath);
            var bellyState = g.Save();
            g.TranslateTransform(mid.X - up.X * 10 + dir.X * 8, mid.Y - up.Y * 10 + dir.Y * 8);
            g.RotateTransform(MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI);
            g.FillEllipse(BellyBrush, -30, -6, 60, 16);
            g.Restore(bellyState);
            foreach (float t in (float[])[0.12f, 0.32f, 0.52f, 0.72f])
            {
                var top = Vector2.Lerp(body[1], body[3], t) + up * 3;
                var bottom = top - up * 15 - dir * 3;
                var ctrl = Vector2.Lerp(top, bottom, 0.5f) + dir * 3;
                g.DrawBezier(StripePen, P(top), P(ctrl), P(ctrl), P(bottom));
            }
            g.Restore(saved);
        }

        // Near-side thigh, then the near legs on top.
        // Sitting, the folded hind leg is hidden by the thigh and only the paw peeks out.
        if (s < 0.5f) DrawLeg(g, hindRoot, nearHind, 15, 17, 8.5f, FurBrush);
        DrawHaunch(g, hip, dir, up, s);
        if (s >= 0.5f) DrawPaw(g, nearHind, FurBrush);
        DrawLeg(g, frontRoot, nearFront, 19, 20, 8f, FurBrush);
        if (tailInFront) DrawTail(g, p, hip, sh, dir, up);

        DrawHead(g, p, head);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    private static Vector2 Paw(in CatPose p, float baseX, float offset, bool front, Vector2 hip, Vector2 sh)
    {
        var paw = V(baseX, -3);
        if (p.GaitBlend > 0.001f)
        {
            // Stance (60% of the cycle): the paw is planted and slides back at body speed.
            // Swing (40%): it lifts and moves forward. Driven by distance, so paws never skate.
            const float stride = 22f;
            float u = (p.GaitPhase / MathF.Tau + offset) % 1f;
            if (u < 0) u += 1;
            Vector2 walk;
            if (u < 0.6f) walk = V(baseX + stride / 2 - stride * u / 0.6f, -3);
            else
            {
                float v = (u - 0.6f) / 0.4f;
                float ease = v * v * (3 - 2 * v);
                walk = V(baseX - stride / 2 + stride * ease, -3 - MathF.Sin(MathF.PI * v) * 6f);
            }
            paw = Vector2.Lerp(paw, walk, p.GaitBlend);
        }
        if (p.Air > 0)
            paw = Vector2.Lerp(paw, front ? sh + V(24, 8) : hip + V(-24, 12), p.Air);
        return paw;
    }

    private static Vector2 SwatPaw(float t, Vector2 rest, Vector2 target)
    {
        const float hit = 0.6f;
        if (t < hit)
        {
            // Lift up and arc over onto the target.
            float a = t / hit;
            float ease = a * a * (3 - 2 * a);
            return Vector2.Lerp(rest, target, ease) - V(0, MathF.Sin(MathF.PI * a) * 22f);
        }
        float b = (t - hit) / (1 - hit);
        return Vector2.Lerp(target, rest, b * b * (3 - 2 * b));
    }

    /// <summary>Two-bone IK: the joint between root and paw, bent backward (elbow/hock behind).</summary>
    private static Vector2 Joint(Vector2 root, ref Vector2 paw, float l1, float l2)
    {
        var d = paw - root;
        float len = d.Length();
        var u = len > 0.001f ? d / len : V(0, 1);
        float dist = Math.Clamp(len, MathF.Abs(l1 - l2) + 0.5f, l1 + l2 - 0.5f);
        paw = root + u * dist; // out of reach: the leg straightens and the paw comes along
        float x = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist);
        float h = MathF.Sqrt(MathF.Max(0, l1 * l1 - x * x));
        var n = V(-u.Y, u.X);
        return root + u * x + n * h;
    }

    private static void DrawLeg(Graphics g, Vector2 root, Vector2 paw, float l1, float l2, float width, SolidBrush fill)
    {
        var joint = Joint(root, ref paw, l1, l2);
        PointF[] pts = [P(root), P(joint), P(paw)];
        using var outline = new Pen(Outline, width + EdgeWidth * 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var inner = new Pen(fill.Color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(outline, pts);
        g.DrawLines(inner, pts);
        DrawPaw(g, paw, fill);
    }

    private static void DrawPaw(Graphics g, Vector2 paw, SolidBrush fill)
    {
        var pawRect = new RectangleF(paw.X - 5, paw.Y - 3.5f, 12, 7);
        g.FillEllipse(fill, pawRect);
        g.DrawEllipse(Edge, pawRect);
    }

    private static void DrawHaunch(Graphics g, Vector2 hip, Vector2 dir, Vector2 up, float sit)
    {
        var center = hip - dir * 3 - up * 2;
        var state = g.Save();
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI);
        float w = 28 + sit * 10, h = 26 + sit * 8;
        var rect = new RectangleF(-w / 2, -h / 2, w, h);
        using (var shade = new LinearGradientBrush(new PointF(0, -h / 2), new PointF(0, h / 2), FurLight, FurShade))
            g.FillEllipse(shade, rect);
        // Only the lower/back edge of the thigh gets an outline, like a cartoon thigh line.
        g.DrawArc(Edge, rect, 40, 200);
        g.Restore(state);
    }

    private static void DrawTail(Graphics g, in CatPose p, Vector2 hip, Vector2 sh, Vector2 dir, Vector2 up)
    {
        float sway = MathF.Sin(p.TailPhase) * (7 + 5 * (1 - p.TailEnergy));
        float twitch = MathF.Sin(p.TailPhase * 3.3f) * 5 * p.TailEnergy;
        float lift = 1 - p.Crouch * 0.5f;
        var b = hip - dir * 15 + up * 9;

        // Standing/stalking: an S-curve up and over. Sitting: wrapped forward along the floor.
        var c1 = Vector2.Lerp(b + V(-16, 2), b + V(-12, 14), p.Sit);
        var c2 = Vector2.Lerp(b + V(-30 + sway, -26 * lift), V(hip.X + 2, -3), p.Sit);
        var tip = Vector2.Lerp(b + V(-16 + sway * 1.5f + twitch, -50 * lift), V(sh.X + 14 + sway * 0.4f, -4), p.Sit);
        if (p.Air > 0)
        {
            c2 = Vector2.Lerp(c2, b + V(-30, -4), p.Air);
            tip = Vector2.Lerp(tip, b + V(-50, -8), p.Air);
        }

        // Sample the curve and build a tapered outline around it.
        const int n = 20;
        var left = new PointF[n];
        var right = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (n - 1f);
            var pt = Bezier(b, c1, c2, tip, t);
            var tan = Vector2.Normalize(BezierTangent(b, c1, c2, tip, t) + V(1e-4f, 0));
            var nrm = V(-tan.Y, tan.X);
            float w = Lerp(4.6f, 2.6f, t);
            left[i] = P(pt + nrm * w);
            right[n - 1 - i] = P(pt - nrm * w);
        }
        var poly = left.Concat(right).ToArray();
        g.FillPolygon(FurBrush, poly);

        // Darker tip over the last third.
        int from = (int)(n * 0.68f);
        var tipPoly = left[from..].Concat(right[..(n - from)]).ToArray();
        g.FillPolygon(TipBrush, tipPoly);
        g.DrawPolygon(Edge, poly);
        g.FillEllipse(TipBrush, tip.X - 2.6f, tip.Y - 2.6f, 5.2f, 5.2f);
        g.DrawArc(Edge, tip.X - 2.6f, tip.Y - 2.6f, 5.2f, 5.2f, 0, 360);
    }

    private static Vector2 Bezier(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float u = 1 - t;
        return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d;
    }

    private static Vector2 BezierTangent(Vector2 a, Vector2 b, Vector2 c, Vector2 d, float t)
    {
        float u = 1 - t;
        return 3 * u * u * (b - a) + 6 * u * t * (c - b) + 3 * t * t * (d - c);
    }

    private static void DrawHead(Graphics g, in CatPose p, Vector2 h)
    {
        var look = p.Look - h;
        float lookAngle = MathF.Atan2(look.Y, MathF.Max(12, look.X)) * 180f / MathF.PI;
        var state = g.Save();
        g.TranslateTransform(h.X, h.Y);
        g.RotateTransform(p.HeadTilt + Math.Clamp(lookAngle * 0.3f, -12f, 12f));

        // Ears (behind the head): rounded triangles with pink insides.
        DrawEar(g, [new(-14, -6), new(-15, -28), new(-2, -15)]);
        DrawEar(g, [new(3, -15), new(13, -29), new(15, -5)]);

        PointF[] skull = [new(-15, 0), new(-13, -11), new(-3, -16), new(9, -14), new(16, -5), new(17, 5), new(10, 12), new(-2, 13), new(-12, 9)];
        g.DrawClosedCurve(ThickEdge, skull, 0.55f, FillMode.Winding);
        using (var shade = new LinearGradientBrush(new PointF(0, -16), new PointF(0, 14), FurLight, Fur))
            g.FillClosedCurve(shade, skull, FillMode.Winding, 0.55f);

        // Muzzle, forehead stripes.
        g.FillEllipse(BellyBrush, 5, 1, 15, 10);
        g.FillEllipse(BellyBrush, -6, 5, 12, 7);
        g.DrawLine(FaceStripe, -3, -14, -2, -9);
        g.DrawLine(FaceStripe, 2, -15, 2, -9.5f);
        g.DrawLine(FaceStripe, 7, -14, 6, -9);

        // Eyes: the far one is narrower (3/4 view). Pupils follow the look target.
        var gaze = look.LengthSquared() > 1 ? Vector2.Normalize(look) * 1.5f : Vector2.Zero;
        DrawEye(g, V(-2, -2), 6.5f, gaze, p);
        DrawEye(g, V(9, -2), 8f, gaze, p);

        // Nose, mouth, whiskers.
        PointF[] nose = [new(14, 3.5f), new(19, 3.5f), new(16.5f, 7)];
        g.FillPolygon(PinkBrush, nose);
        g.DrawPolygon(FineEdge, nose);
        g.DrawLine(FineEdge, 16.5f, 7, 16.5f, 8.5f);
        g.DrawArc(FineEdge, 13.2f, 6.5f, 3.3f, 3.3f, 10, 150);
        g.DrawArc(FineEdge, 16.5f, 6.5f, 3.3f, 3.3f, 20, 150);
        g.DrawBezier(Whisker, 18, 8, 25, 6, 30, 5, 35, 3);
        g.DrawBezier(Whisker, 18, 9, 25, 9, 30, 9.5f, 36, 10);
        g.DrawBezier(Whisker, 18, 10, 25, 12, 29, 14, 33, 16);

        g.Restore(state);
    }

    private static void DrawEar(Graphics g, PointF[] tri)
    {
        g.DrawClosedCurve(ThickEdge, tri, 0.25f, FillMode.Winding);
        g.FillClosedCurve(FurBrush, tri, FillMode.Winding, 0.25f);
        var cx = (tri[0].X + tri[1].X + tri[2].X) / 3;
        var cy = (tri[0].Y + tri[1].Y + tri[2].Y) / 3 + 2;
        var inner = tri.Select(t => new PointF(cx + (t.X - cx) * 0.55f, cy + (t.Y - cy) * 0.55f)).ToArray();
        g.FillClosedCurve(InnerEar, inner, FillMode.Winding, 0.25f);
    }

    private static void DrawEye(Graphics g, Vector2 c, float width, Vector2 gaze, in CatPose p)
    {
        float open = 1f - p.Blink;
        if (open < 0.18f)
        {
            g.DrawArc(FineEdge, c.X - width / 2, c.Y - 1.5f, width, 3.5f, 10, 160);
            return;
        }
        float h = 9f * open;
        var eye = new RectangleF(c.X - width / 2, c.Y - h / 2, width, h);
        g.FillEllipse(IrisBrush, eye);

        float pw = 1.6f + 3.6f * p.Pupil * (width / 8f);
        float ph = Math.Max(1, h - 1.5f);
        g.FillEllipse(PupilBrush, c.X - pw / 2 + gaze.X, c.Y - ph / 2 + gaze.Y * 0.5f, pw, ph);
        g.FillEllipse(Shine, c.X - width * 0.25f + gaze.X * 0.5f, c.Y - h / 2 + 1.2f, 2, 2);
        g.DrawEllipse(FineEdge, eye);
        // Upper lid line gives the eye a little shape.
        g.DrawArc(Edge, eye.X - 0.3f, eye.Y - 0.3f, eye.Width + 0.6f, eye.Height, 195, 150);
    }

    // ---- Cotton ball ----

    private static readonly SolidBrush CottonBrush = new(Color.FromArgb(250, 250, 252));
    private static readonly SolidBrush CottonEdge = new(Color.FromArgb(196, 198, 210));
    private static readonly SolidBrush CottonShade = new(Color.FromArgb(38, 90, 90, 120));
    private static readonly Pen FiberPen = new(Color.FromArgb(170, 175, 180, 195), 1.1f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

    /// <summary>A fluffy cotton ball; <paramref name="spin"/> rotates its fibers so rolling reads.</summary>
    public static void DrawCottonBall(Graphics g, PointF center, float r, float spin)
    {
        var state = g.Save();
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(spin * 180f / MathF.PI);

        // Puffs around the edge, drawn twice: a slightly larger grey pass makes a soft outline.
        for (int pass = 0; pass < 2; pass++)
        {
            var brush = pass == 0 ? CottonEdge : CottonBrush;
            float grow = pass == 0 ? 1.2f : 0f;
            for (int i = 0; i < 9; i++)
            {
                float a = i * MathF.Tau / 9;
                float pr = r * (0.52f + 0.08f * MathF.Sin(i * 2.3f)) + grow;
                float px = MathF.Cos(a) * r * 0.48f, py = MathF.Sin(a) * r * 0.48f;
                g.FillEllipse(brush, px - pr, py - pr, pr * 2, pr * 2);
            }
            g.FillEllipse(brush, -r * 0.6f - grow, -r * 0.6f - grow, r * 1.2f + grow * 2, r * 1.2f + grow * 2);
        }

        g.DrawBezier(FiberPen, -r * 0.5f, -r * 0.1f, -r * 0.2f, -r * 0.5f, r * 0.2f, -r * 0.1f, r * 0.45f, -r * 0.4f);
        g.DrawBezier(FiberPen, -r * 0.3f, r * 0.35f, 0, r * 0.1f, r * 0.2f, r * 0.5f, r * 0.5f, r * 0.2f);
        g.Restore(state);

        // Shading stays put (light from the top-left) while the fibers spin.
        g.FillEllipse(CottonShade, center.X - r * 0.2f, center.Y - r * 0.05f, r * 1.05f, r * 0.95f);
    }
}
