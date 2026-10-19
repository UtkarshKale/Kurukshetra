using System;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // Clipped IMGUI drawing for the replay map (fix for the "green lines" glitch: view lines used to run past the map square).
    // Everything here clips to a rectangle before it draws, so nothing can land outside it. A test can set DrawHook to receive
    // every rectangle that would be drawn instead of drawing it.
    public static class GuiDraw
    {
        public static Action<Rect, Color> DrawHook;

        // Liang-Barsky: clips the segment to the rectangle in place. false = nothing of it is inside.
        public static bool ClipSegment(ref Vector2 a, ref Vector2 b, Rect r)
        {
            float t0 = 0f, t1 = 1f, dx = b.x - a.x, dy = b.y - a.y;
            if (!Clip(-dx, a.x - r.xMin, ref t0, ref t1)) return false;
            if (!Clip(dx, r.xMax - a.x, ref t0, ref t1)) return false;
            if (!Clip(-dy, a.y - r.yMin, ref t0, ref t1)) return false;
            if (!Clip(dy, r.yMax - a.y, ref t0, ref t1)) return false;
            Vector2 na = new Vector2(a.x + t0 * dx, a.y + t0 * dy), nb = new Vector2(a.x + t1 * dx, a.y + t1 * dy);
            a = na; b = nb;
            return true;
        }

        static bool Clip(float p, float q, ref float t0, ref float t1)
        {
            if (Mathf.Abs(p) < 1e-9f) return q >= 0f;
            float t = q / p;
            if (p < 0f) { if (t > t1) return false; if (t > t0) t0 = t; }
            else { if (t < t0) return false; if (t < t1) t1 = t; }
            return true;
        }

        static void Quad(Rect r, Color c)
        {
            if (DrawHook != null) { DrawHook(r, c); return; }
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        // Line clipped to `clip`. Under a scaled GUI.matrix (the dashboard) it is a row of small squares; otherwise a rotated strip.
        public static void Line(Vector2 a, Vector2 b, float width, Color c, Rect clip)
        {
            if (!ClipSegment(ref a, ref b, clip)) return;
            Vector2 d = b - a;
            float len = d.magnitude;
            float half = width * 0.5f;
            if (len < 0.01f) { Quad(Inside(new Rect(a.x - half, a.y - half, width, width), clip), c); return; }
            if (DrawHook != null || !GUI.matrix.isIdentity)
            {
                float step = Mathf.Max(1f, width * 0.75f);
                for (float t = 0f; t <= len + 0.001f; t += step)
                {
                    Vector2 p = a + d * (Mathf.Min(t, len) / len);
                    Quad(Inside(new Rect(p.x - half, p.y - half, width, width), clip), c);
                }
                return;
            }
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Quad(new Rect(a.x, a.y - half, len, width), c);
            GUI.matrix = matrix;
        }

        // The part of r that lies inside clip (zero-size if none).
        public static Rect Inside(Rect r, Rect clip)
        {
            float x0 = Mathf.Max(r.xMin, clip.xMin), y0 = Mathf.Max(r.yMin, clip.yMin);
            float x1 = Mathf.Min(r.xMax, clip.xMax), y1 = Mathf.Min(r.yMax, clip.yMax);
            return new Rect(x0, y0, Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
        }

        public static Vector2 Dir(float bearingDeg)
        {
            float a = bearingDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), -Mathf.Cos(a));     // 0 = up, clockwise
        }

        // A filled view cone (triangle from the apex to the two edge points) drawn as 2 px horizontal spans, all clipped.
        public static void Wedge(Vector2 apex, float yawDeg, float halfFovDeg, float length, Color fill, Rect clip)
        {
            halfFovDeg = Mathf.Clamp(halfFovDeg, 0.5f, 85f);
            Vector2 p1 = apex + Dir(yawDeg - halfFovDeg) * length / Mathf.Cos(halfFovDeg * Mathf.Deg2Rad);
            Vector2 p2 = apex + Dir(yawDeg + halfFovDeg) * length / Mathf.Cos(halfFovDeg * Mathf.Deg2Rad);
            float yMin = Mathf.Max(Mathf.Min(apex.y, Mathf.Min(p1.y, p2.y)), clip.yMin);
            float yMax = Mathf.Min(Mathf.Max(apex.y, Mathf.Max(p1.y, p2.y)), clip.yMax);
            const float Row = 2f;
            for (float y = yMin; y < yMax; y += Row)
            {
                float yc = y + Row * 0.5f;
                float lo = float.MaxValue, hi = float.MinValue;
                Edge(apex, p1, yc, ref lo, ref hi);
                Edge(p1, p2, yc, ref lo, ref hi);
                Edge(p2, apex, yc, ref lo, ref hi);
                if (hi < lo) continue;
                Rect span = Inside(new Rect(lo, y, hi - lo, Row), clip);
                if (span.width > 0f && span.height > 0f) Quad(span, fill);
            }
        }

        static void Edge(Vector2 a, Vector2 b, float y, ref float lo, ref float hi)
        {
            if ((a.y > y) == (b.y > y)) return;
            float x = a.x + (y - a.y) / (b.y - a.y) * (b.x - a.x);
            if (x < lo) lo = x;
            if (x > hi) hi = x;
        }

        // The cone's two edges, clipped.
        public static void WedgeEdges(Vector2 apex, float yawDeg, float halfFovDeg, float length, float width, Color c, Rect clip)
        {
            Line(apex, apex + Dir(yawDeg - halfFovDeg) * length, width, c, clip);
            Line(apex, apex + Dir(yawDeg + halfFovDeg) * length, width, c, clip);
        }
    }
}
