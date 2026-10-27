using System.Drawing.Drawing2D;
using System.Numerics;

namespace WallpaperStudio.Effects.Dog;

/// <summary>Everything needed to draw one frame of the dog, in its own local space.</summary>
internal struct DogPose
{
    public float Sit;        // 0..1 sitting
    public float Bow;        // 0..1 "ready" play bow (front end low)
    public float GaitPhase;  // leg cycle (radians), advanced by distance travelled
    public float GaitBlend;  // 0 legs planted .. 1 moving
    public float Gallop;     // 0 trot .. 1 full gallop
    public float Air;        // 0..1 mid-leap leg tuck
    public float Pitch;      // degrees, nose up when positive
    public float Squash;     // landing squash, decays to 0
    public float MouthOpen;  // 0..1
    public float Pant;       // 0..1 tongue out
    public float TailPhase;
    public float TailUp;     // 0 resting on the floor .. 1 held high
    public float HeadDown;   // 0..1 nose to the ground (picking something up)
    public float Blink;
    public Vector2 Look;     // local coords
}

/// <summary>
/// Procedural side-view border collie: black coat, white blaze, chest, socks and tail tip.
/// Local space: origin on the ground under the dog's middle, facing +x, y up is negative.
/// </summary>
internal static class DogArt
{
    private static readonly Color Coat = Color.FromArgb(54, 54, 62);
    private static readonly Color CoatLight = Color.FromArgb(78, 78, 90);
    private static readonly Color CoatShade = Color.FromArgb(36, 36, 42);
    private static readonly Color Far = Color.FromArgb(32, 32, 38);
    private static readonly Color White = Color.FromArgb(246, 246, 249);
    private static readonly Color Outline = Color.FromArgb(18, 18, 22);

    private const float EdgeWidth = 1.8f;
    private static readonly Pen Edge = new(Outline, EdgeWidth) { LineJoin = LineJoin.Round };
    private static readonly Pen ThickEdge = new(Outline, EdgeWidth * 2) { LineJoin = LineJoin.Round };
    private static readonly Pen FineEdge = new(Outline, 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
    private static readonly SolidBrush CoatBrush = new(Coat);
    private static readonly SolidBrush FarBrush = new(Far);
    private static readonly SolidBrush WhiteBrush = new(White);
    private static readonly SolidBrush NoseBrush = new(Color.FromArgb(16, 16, 18));
    private static readonly SolidBrush MouthBrush = new(Color.FromArgb(120, 30, 40));
    private static readonly SolidBrush TongueBrush = new(Color.FromArgb(240, 118, 140));
    private static readonly SolidBrush EyeBrush = new(Color.FromArgb(70, 40, 20));
    private static readonly SolidBrush Shine = new(Color.White);
    private static readonly SolidBrush InnerEar = new(Color.FromArgb(120, 90, 95));
    private static readonly SolidBrush TagBrush = new(Color.FromArgb(250, 200, 60));

    private static Vector2 V(float x, float y) => new(x, y);
    private static PointF P(Vector2 v) => new(v.X, v.Y);
    private static PointF[] P(Vector2[] vs) => Array.ConvertAll(vs, P);
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>The dog's skeleton for a pose: hip, shoulder, body axes, head position and angle.</summary>
    internal readonly record struct Rig(Vector2 Hip, Vector2 Sh, Vector2 Dir, Vector2 Up, Vector2 Head, float HeadDegrees);

    public static Rig Skeleton(in DogPose p)
    {
        var hip = V(-30, -46);
        var sh = V(26, -48);
        sh += V(2, p.Bow * 14);
        hip += V(0, -p.Bow * 3);
        hip = Vector2.Lerp(hip, V(-22, -21), p.Sit);
        sh = Vector2.Lerp(sh, V(14, -57), p.Sit);

        // Galloping dogs flex and extend their spine; everything bobs a little with the stride.
        hip.X += MathF.Sin(p.GaitPhase) * 5f * p.Gallop * p.GaitBlend;
        float bob = p.GaitBlend * MathF.Sin(p.GaitPhase * 2) * (1.5f + p.Gallop * 2f);
        hip.Y += bob + p.Squash * 5;
        sh.Y += bob + p.Squash * 5;

        // Pitch the whole body around its middle (nose up while leaping).
        var mid = (hip + sh) / 2;
        float a = -p.Pitch * MathF.PI / 180f;
        hip = mid + Rotate(hip - mid, a);
        sh = mid + Rotate(sh - mid, a);

        var dir = Vector2.Normalize(sh - hip);
        var up = V(dir.Y, -dir.X);
        var head = sh + dir * 17 + up * 20 - up * (p.HeadDown * 26) + dir * (p.HeadDown * 8);

        float bodyDeg = MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI;
        var look = p.Look - head;
        float lookDeg = MathF.Atan2(look.Y, MathF.Max(14, MathF.Abs(look.X))) * 180f / MathF.PI;
        float headDeg = bodyDeg * 0.6f + Math.Clamp(lookDeg * 0.45f, -28f, 22f) + p.HeadDown * 45f;
        return new Rig(hip, sh, dir, up, head, headDeg);
    }

    /// <summary>Where the frisbee goes when the dog holds it (local coords).</summary>
    public static Vector2 MouthLocal(in DogPose p)
    {
        var r = Skeleton(p);
        return r.Head + Rotate(V(24, 8), r.HeadDegrees * MathF.PI / 180f);
    }

    private static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return V(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    public static void DrawShadow(Graphics g, float x, float groundY, float width, float lift)
    {
        float k = 1f - Math.Min(0.5f, lift / 120f);
        float w = width * k, h = 10 * k;
        using var path = new GraphicsPath();
        path.AddEllipse(x - w / 2, groundY - h / 2, w, h);
        using var brush = new PathGradientBrush(path)
        {
            CenterColor = Color.FromArgb((int)(85 * k), 0, 0, 0),
            SurroundColors = [Color.FromArgb(0, 0, 0, 0)],
        };
        g.FillPath(brush, path);
    }

    public static void Draw(Graphics g, in DogPose p)
    {
        var (hip, sh, dir, up, head, headDeg) = Skeleton(p);
        var mid = (hip + sh) / 2;

        // Legs: trot uses diagonal pairs, gallop moves the hind pair then the front pair.
        float frontBase = Lerp(sh.X + 4, sh.X + 2, p.Sit), hindBase = Lerp(hip.X - 2, hip.X + 20, p.Sit);
        var frontRoot = sh + dir * 6 - up * 8;
        var hindRoot = hip - dir * 2 - up * 8;
        var nearHind = Paw(p, hindBase, 0f, false, hip, sh, dir, up);
        var farHind = Paw(p, hindBase + 4, Lerp(0.5f, 0.08f, p.Gallop), false, hip, sh, dir, up);
        var nearFront = Paw(p, frontBase, 0.5f, true, hip, sh, dir, up);
        var farFront = Paw(p, frontBase + 4, Lerp(0f, 0.58f, p.Gallop), true, hip, sh, dir, up);

        DrawLeg(g, frontRoot + V(4, 0), farFront, 22, 23, 8f, FarBrush);
        DrawLeg(g, hindRoot + V(4, 0), farHind, 18, 22, 9f, FarBrush);
        DrawTail(g, p, hip, dir, up);

        // Body + neck: outline pass, then fill pass, so the shapes merge without seams.
        Vector2[] body =
        [
            hip - dir * 20 + up * 2,
            hip - dir * 8 + up * 16,
            mid + up * 15,
            sh + dir * 2 + up * 16,
            sh + dir * 16 + up * 4,
            sh + dir * 10 - up * 15,
            mid - up * 9,
            hip - dir * 4 - up * 12,
        ];
        var rot = headDeg * MathF.PI / 180f;
        Vector2[] neck = [sh + up * 15 + dir * 2, head + Rotate(V(-10, 0), rot), head + Rotate(V(4, 11), rot), sh + dir * 17 + up * 2];
        var bodyPts = P(body);
        var neckPts = P(neck);
        g.DrawClosedCurve(ThickEdge, bodyPts, 0.6f, FillMode.Winding);
        g.DrawClosedCurve(ThickEdge, neckPts, 0.6f, FillMode.Winding);
        using (var shade = new LinearGradientBrush(P(mid + up * 17), P(mid - up * 12), CoatLight, CoatShade))
            g.FillClosedCurve(shade, bodyPts, FillMode.Winding, 0.6f);
        g.FillClosedCurve(CoatBrush, neckPts, FillMode.Winding, 0.6f);

        // White chest and ruff, clipped to body + neck.
        using (var clip = new GraphicsPath())
        {
            clip.AddClosedCurve(bodyPts, 0.6f);
            clip.AddClosedCurve(neckPts, 0.6f);
            var saved = g.Save();
            g.SetClip(clip);
            var chest = sh + dir * 14 - up * 6;
            g.FillEllipse(WhiteBrush, chest.X - 14, chest.Y - 16, 26, 30);
            var ruff = head + Rotate(V(0, 12), rot);
            g.FillEllipse(WhiteBrush, ruff.X - 10, ruff.Y - 9, 20, 18);
            g.Restore(saved);
        }

        DrawHaunch(g, hip, dir, up, p.Sit);
        if (p.Sit < 0.5f) DrawLeg(g, hindRoot, nearHind, 18, 22, 9.5f, CoatBrush);
        else DrawPaw(g, nearHind);
        DrawLeg(g, frontRoot, nearFront, 22, 23, 9f, CoatBrush);

        // Red collar with a little gold tag.
        var c1 = Vector2.Lerp(sh + up * 15, head + Rotate(V(-9, 0), rot), 0.5f);
        var c2 = Vector2.Lerp(sh + dir * 17, head + Rotate(V(4, 11), rot), 0.5f);
        using (var collarEdge = new Pen(Outline, 7f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(collarEdge, P(c1), P(c2));
        using (var collar = new Pen(Color.FromArgb(222, 46, 58), 4.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(collar, P(c1), P(c2));
        g.FillEllipse(TagBrush, c2.X - 3, c2.Y, 6, 6);
        g.DrawEllipse(FineEdge, c2.X - 3, c2.Y, 6, 6);

        DrawHead(g, p, head, headDeg);
    }

    private static Vector2 Paw(in DogPose p, float baseX, float offset, bool front, Vector2 hip, Vector2 sh, Vector2 dir, Vector2 up)
    {
        var paw = V(baseX, -3);
        if (p.GaitBlend > 0.001f)
        {
            float stance = Lerp(0.55f, 0.42f, p.Gallop);
            float stride = Lerp(30f, 56f, p.Gallop);
            float lift = Lerp(8f, 16f, p.Gallop);
            float u = (p.GaitPhase / MathF.Tau + offset) % 1f;
            if (u < 0) u += 1;
            Vector2 walk;
            if (u < stance) walk = V(baseX + stride / 2 - stride * u / stance, -3);
            else
            {
                float v = (u - stance) / (1 - stance);
                float ease = v * v * (3 - 2 * v);
                walk = V(baseX - stride / 2 + stride * ease, -3 - MathF.Sin(MathF.PI * v) * lift);
            }
            paw = Vector2.Lerp(paw, walk, p.GaitBlend);
        }
        if (p.Air > 0)
            paw = Vector2.Lerp(paw, front ? sh + dir * 16 - up * 16 : hip - dir * 26 - up * 14, p.Air);
        return paw;
    }

    /// <summary>Two-bone IK: the joint between root and paw, bent backward (elbow/hock behind).</summary>
    private static Vector2 Joint(Vector2 root, ref Vector2 paw, float l1, float l2)
    {
        var d = paw - root;
        float len = d.Length();
        var u = len > 0.001f ? d / len : V(0, 1);
        float dist = Math.Clamp(len, MathF.Abs(l1 - l2) + 0.5f, l1 + l2 - 0.5f);
        paw = root + u * dist;
        float x = (l1 * l1 - l2 * l2 + dist * dist) / (2 * dist);
        float h = MathF.Sqrt(MathF.Max(0, l1 * l1 - x * x));
        return root + u * x + V(-u.Y, u.X) * h;
    }

    private static void DrawLeg(Graphics g, Vector2 root, Vector2 paw, float l1, float l2, float width, SolidBrush fill)
    {
        var joint = Joint(root, ref paw, l1, l2);
        PointF[] pts = [P(root), P(joint), P(paw)];
        using var outline = new Pen(Outline, width + EdgeWidth * 2) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var inner = new Pen(fill.Color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(outline, pts);
        g.DrawLines(inner, pts);
        // White sock on the lower leg.
        var sock = Vector2.Lerp(joint, paw, 0.55f);
        using (var white = new Pen(White, width - 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawLine(white, P(sock), P(paw));
        DrawPaw(g, paw);
    }

    private static void DrawPaw(Graphics g, Vector2 paw)
    {
        var rect = new RectangleF(paw.X - 5.5f, paw.Y - 4, 13, 7.5f);
        g.FillEllipse(WhiteBrush, rect);
        g.DrawEllipse(Edge, rect);
    }

    private static void DrawHaunch(Graphics g, Vector2 hip, Vector2 dir, Vector2 up, float sit)
    {
        var center = hip - dir * 4 - up * 2;
        var state = g.Save();
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(MathF.Atan2(dir.Y, dir.X) * 180f / MathF.PI);
        float w = 30 + sit * 10, h = 28 + sit * 8;
        var rect = new RectangleF(-w / 2, -h / 2, w, h);
        using (var shade = new LinearGradientBrush(new PointF(0, -h / 2), new PointF(0, h / 2), CoatLight, CoatShade))
            g.FillEllipse(shade, rect);
        g.DrawArc(Edge, rect, 40, 200);
        g.Restore(state);
    }

    private static void DrawTail(Graphics g, in DogPose p, Vector2 hip, Vector2 dir, Vector2 up)
    {
        float wag = MathF.Sin(p.TailPhase) * 9f;
        var b = hip - dir * 18 + up * 10;
        // Held high (running, happy) vs. lying on the floor (sitting), with the wag sweeping.
        var c1 = Vector2.Lerp(b + V(-8, 12), b + V(-12, -8), p.TailUp);
        var c2 = Vector2.Lerp(V(b.X - 22, -4), b + V(-26, -22 + wag * 0.5f), p.TailUp);
        var tip = Vector2.Lerp(V(b.X - 38 + wag * 0.7f, -5), b + V(-22 + wag, -40), p.TailUp);

        const int n = 22;
        var left = new PointF[n];
        var right = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (n - 1f);
            var pt = Bezier(b, c1, c2, tip, t);
            var tan = Vector2.Normalize(BezierTangent(b, c1, c2, tip, t) + V(1e-4f, 0));
            var nrm = V(-tan.Y, tan.X);
            float w = Lerp(5.5f, 4f, t) * (t > 0.85f ? 1 - (t - 0.85f) * 3 : 1); // bushy, rounded tip
            left[i] = P(pt + nrm * w);
            right[n - 1 - i] = P(pt - nrm * w);
        }
        var poly = left.Concat(right).ToArray();
        g.FillPolygon(CoatBrush, poly);
        int from = (int)(n * 0.7f);
        g.FillPolygon(WhiteBrush, left[from..].Concat(right[..(n - from)]).ToArray());
        g.DrawPolygon(Edge, poly);
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

    private static void DrawHead(Graphics g, in DogPose p, Vector2 h, float degrees)
    {
        var state = g.Save();
        g.TranslateTransform(h.X, h.Y);
        g.RotateTransform(degrees);

        // Far ear (behind), then skull, muzzle and jaw.
        DrawEar(g, new PointF(-3, 1), FarBrush);

        PointF[] skull = [new(-13, 2), new(-11, -9), new(-2, -14), new(9, -12), new(14, -5), new(15, 3), new(8, 10), new(-4, 11), new(-11, 8)];
        PointF[] snout = [new(9, -5), new(22, -4), new(30, -1), new(30, 5), new(22, 7), new(10, 8)];
        float open = p.MouthOpen * 26f;

        // Open mouth: dark inside, tongue, then the lower jaw rotated down.
        if (open > 1)
        {
            g.FillPolygon(MouthBrush, new PointF[] { new(10, 6), new(28, 5), new(26, 10 + open * 0.35f), new(11, 10) });
        }
        if (p.Pant > 0.05f)
        {
            float t = p.Pant;
            g.FillEllipse(TongueBrush, 15, 6, 9, 7 + t * 8);
            g.DrawEllipse(FineEdge, 15, 6, 9, 7 + t * 8);
        }
        var jawState = g.Save();
        g.TranslateTransform(11, 7);
        g.RotateTransform(open);
        PointF[] jaw = [new(-1, 0), new(15, -1), new(17, 2), new(14, 5), new(0, 5)];
        g.DrawClosedCurve(ThickEdge, jaw, 0.4f, FillMode.Winding);
        g.FillClosedCurve(WhiteBrush, jaw, FillMode.Winding, 0.4f);
        g.Restore(jawState);

        g.DrawClosedCurve(ThickEdge, skull, 0.55f, FillMode.Winding);
        g.DrawClosedCurve(ThickEdge, snout, 0.45f, FillMode.Winding);
        using (var shade = new LinearGradientBrush(new PointF(0, -14), new PointF(0, 11), CoatLight, Coat))
            g.FillClosedCurve(shade, skull, FillMode.Winding, 0.55f);
        g.FillClosedCurve(WhiteBrush, snout, FillMode.Winding, 0.45f);

        // White blaze running from the forehead down to the muzzle.
        g.FillPolygon(WhiteBrush, new PointF[] { new(1, -14), new(5, -14), new(13, -5), new(9, -3) });
        g.FillEllipse(NoseBrush, 26, -3.5f, 7, 6);
        g.FillEllipse(Shine, 28, -2.5f, 2, 1.5f);
        g.DrawLine(FineEdge, 29, 3, 20, 6);

        // Eye.
        float eyeH = 5.2f * (1 - p.Blink);
        if (eyeH < 1) g.DrawLine(FineEdge, 4, -4, 9, -4);
        else
        {
            g.FillEllipse(EyeBrush, 4.2f, -4 - eyeH / 2, 5, eyeH);
            g.FillEllipse(Shine, 6.5f, -4 - eyeH / 2 + 0.8f, 1.6f, 1.6f);
            g.DrawEllipse(FineEdge, 4.2f, -4 - eyeH / 2, 5, eyeH);
        }

        DrawEar(g, new PointF(0, 0), CoatBrush);
        g.Restore(state);
    }

    /// <summary>Collie ear: semi-erect with the tip folded forward.</summary>
    private static void DrawEar(Graphics g, PointF o, SolidBrush fill)
    {
        PointF[] ear = [new(o.X - 8, o.Y - 9), new(o.X - 5, o.Y - 25), new(o.X + 3, o.Y - 12)];
        g.DrawClosedCurve(ThickEdge, ear, 0.2f, FillMode.Winding);
        g.FillClosedCurve(fill, ear, FillMode.Winding, 0.2f);
        if (fill == CoatBrush)
            g.FillPolygon(InnerEar, new PointF[] { new(o.X - 5, o.Y - 11), new(o.X - 4.5f, o.Y - 19), new(o.X, o.Y - 13) });
        // The folded-over tip.
        PointF[] fold = [new(o.X - 6.5f, o.Y - 21), new(o.X - 5, o.Y - 25), new(o.X + 1, o.Y - 19)];
        g.FillPolygon(FarBrush, fold);
        g.DrawPolygon(FineEdge, fold);
    }

    // ---- Frisbee ----

    private static readonly Pen RimPen = new(Color.FromArgb(120, 30, 10), 1.3f);
    private static readonly Pen HighlightPen = new(Color.FromArgb(200, 255, 255, 255), 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };

    /// <summary>A bright orange flying disc seen edge-on, tilted by <paramref name="degrees"/>.</summary>
    public static void DrawFrisbee(Graphics g, PointF center, float degrees, float spin, float r = 13)
    {
        var state = g.Save();
        g.TranslateTransform(center.X, center.Y);
        g.RotateTransform(degrees);
        var rect = new RectangleF(-r, -r * 0.3f, r * 2, r * 0.6f);
        using (var fill = new LinearGradientBrush(new PointF(0, -r * 0.3f), new PointF(0, r * 0.3f),
                   Color.FromArgb(255, 170, 60), Color.FromArgb(225, 70, 20)))
            g.FillEllipse(fill, rect);
        g.DrawEllipse(RimPen, rect);
        g.DrawArc(HighlightPen, -r * 0.8f, -r * 0.24f, r * 1.6f, r * 0.4f, 200, 140);
        // A mark on the rim sweeps across as it spins.
        float sx = MathF.Cos(spin) * r * 0.7f;
        if (MathF.Sin(spin) > 0)
            using (var mark = new SolidBrush(Color.FromArgb(200, 255, 230, 120)))
                g.FillEllipse(mark, sx - 1.8f, -r * 0.12f, 3.6f, r * 0.22f);
        g.Restore(state);
    }
}
