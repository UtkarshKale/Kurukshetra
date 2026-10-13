using System.Collections.Generic;
using Chaukanna.Scoring;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // Immediate-mode drawing helpers for the score dashboard: tiles, line charts, heatmap, timeline, tree path.
    // No packages: everything is rectangles and rotated rectangles.
    public static class DashboardCharts
    {
        // One palette for the whole dashboard.
        public static readonly Color Background = new Color(0.05f, 0.06f, 0.08f, 1f);
        public static readonly Color Panel = new Color(0.10f, 0.12f, 0.15f, 1f);
        public static readonly Color PanelHover = new Color(0.16f, 0.19f, 0.24f, 1f);
        public static readonly Color Selected = new Color(0.16f, 0.30f, 0.45f, 1f);
        public static readonly Color Text = new Color(0.92f, 0.94f, 0.96f, 1f);
        public static readonly Color Dim = new Color(0.60f, 0.65f, 0.70f, 1f);
        public static readonly Color Accent = new Color(0.35f, 0.75f, 1f, 1f);
        public static readonly Color Good = new Color(0.35f, 0.85f, 0.45f, 1f);
        public static readonly Color Bad = new Color(0.95f, 0.35f, 0.30f, 1f);
        public static readonly Color Warn = new Color(1f, 0.75f, 0.25f, 1f);
        public static readonly Color Neutral = new Color(0.45f, 0.48f, 0.52f, 1f);

        public static GUIStyle Label, Small, Big, Header, Centre;

        public static void EnsureStyles(int fontSize)
        {
            if (Label == null)
            {
                Label = new GUIStyle(GUI.skin.label) { wordWrap = false, alignment = TextAnchor.MiddleLeft, richText = true, clipping = TextClipping.Clip };
                Small = new GUIStyle(Label);
                Big = new GUIStyle(Label) { fontStyle = FontStyle.Bold };
                Header = new GUIStyle(Label) { fontStyle = FontStyle.Bold };
                Centre = new GUIStyle(Label) { alignment = TextAnchor.MiddleCenter };
            }
            Label.fontSize = Centre.fontSize = fontSize;
            Small.fontSize = fontSize - 3;
            Big.fontSize = fontSize + 12;
            Header.fontSize = fontSize + 3;
            Label.normal.textColor = Big.normal.textColor = Header.normal.textColor = Centre.normal.textColor = Text;
            Small.normal.textColor = Dim;
        }

        public static void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        public static void Line(Vector2 a, Vector2 b, Color c, float thickness = 2f)
        {
            if (Event.current.type != EventType.Repaint) return;
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) return;
            var saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            Fill(new Rect(a.x, a.y - thickness * 0.5f, length, thickness), c);
            GUI.matrix = saved;
        }

        public static void DashedLine(Vector2 a, Vector2 b, Color c, float thickness = 2f, float dash = 6f)
        {
            Vector2 d = b - a;
            float length = d.magnitude;
            if (length < 0.01f) return;
            Vector2 dir = d / length;
            for (float t = 0f; t < length; t += dash * 2f)
                Line(a + dir * t, a + dir * Mathf.Min(length, t + dash), c, thickness);
        }

        public static void SectionTitle(Rect r, string text)
        {
            GUI.Label(r, text, Header);
            Fill(new Rect(r.x, r.yMax - 2f, r.width, 1f), new Color(1f, 1f, 1f, 0.12f));
        }

        // A stat tile: big value over a small caption.
        public static void Tile(Rect r, string value, string caption, Color valueColour)
        {
            Fill(r, Panel);
            Fill(new Rect(r.x, r.y, 4f, r.height), valueColour);
            var old = Big.normal.textColor;
            Big.normal.textColor = valueColour;
            GUI.Label(new Rect(r.x + 12f, r.y + 4f, r.width - 16f, r.height * 0.6f), value, Big);
            Big.normal.textColor = old;
            GUI.Label(new Rect(r.x + 12f, r.y + r.height * 0.6f, r.width - 16f, r.height * 0.36f), caption, Small);
        }

        // Rows = truth, columns = call. Cell shade grows with the count; right answers are green, wrong ones red.
        public static void Heatmap(Rect r, SessionResult s)
        {
            int rows = s.confusionRows.Length, cols = s.confusionColumns.Length;
            float labelW = r.width * 0.26f, headH = 22f;
            float cw = (r.width - labelW) / cols, ch = (r.height - headH) / rows;
            int max = 1;
            foreach (int n in s.confusion) if (n > max) max = n;

            for (int c = 0; c < cols; c++)
                GUI.Label(new Rect(r.x + labelW + c * cw, r.y, cw, headH), Short(s.confusionColumns[c]), Small);
            for (int row = 0; row < rows; row++)
            {
                GUI.Label(new Rect(r.x, r.y + headH + row * ch, labelW, ch), Short(s.confusionRows[row]), Small);
                for (int c = 0; c < cols; c++)
                {
                    int n = s.confusion[row * cols + c];
                    var cell = new Rect(r.x + labelW + c * cw + 1f, r.y + headH + row * ch + 1f, cw - 2f, ch - 2f);
                    bool right = row == c;   // the first five columns line up with the five rows
                    Color tone = c == cols - 1 ? Neutral : right ? Good : Bad;
                    Fill(cell, n == 0 ? Panel : Color.Lerp(Panel, tone, 0.35f + 0.65f * n / max));
                    if (n > 0) GUI.Label(cell, n.ToString(), Centre);
                }
            }
        }

        static string Short(string name)
        {
            switch (name)
            {
                case "HostileQuad": return "H quad";
                case "HostileFixedWing": return "H fixed";
                case "FriendlyISR":
                case "OwnIsrDrone": return "Own ISR";
                case "CivilianQuad":
                case "CivilianDrone": return "Civilian";
                case "NoCall": return "No call";
                default: return name;
            }
        }

        // Line chart of `total` sessions. Sample points are dashed and dimmer; real ones solid.
        public static void TrendChart(Rect r, string title, List<TrendPoint> points, System.Func<TrendPoint, float> value, float min, float max, string unit, bool higherIsBetter)
        {
            Fill(r, Panel);
            GUI.Label(new Rect(r.x + 8f, r.y + 2f, r.width - 16f, 20f), title, Small);
            var plot = new Rect(r.x + 10f, r.y + 26f, r.width - 20f, r.height - 48f);
            Fill(new Rect(plot.x, plot.yMax, plot.width, 1f), new Color(1f, 1f, 1f, 0.2f));
            Fill(new Rect(plot.x, plot.y + plot.height * 0.5f, plot.width, 1f), new Color(1f, 1f, 1f, 0.07f));
            if (points == null || points.Count == 0) return;

            Vector2 previous = Vector2.zero;
            for (int i = 0; i < points.Count; i++)
            {
                float v = Mathf.InverseLerp(min, max, value(points[i]));
                var p = new Vector2(points.Count > 1 ? plot.x + plot.width * i / (points.Count - 1) : plot.center.x, plot.yMax - v * plot.height);
                bool sample = points[i].sample;
                Color colour = sample ? new Color(Accent.r, Accent.g, Accent.b, 0.4f) : Accent;
                if (i > 0)
                {
                    if (sample || points[i - 1].sample) DashedLine(previous, p, new Color(Accent.r, Accent.g, Accent.b, 0.4f));
                    else Line(previous, p, Accent);
                }
                Fill(new Rect(p.x - 3f, p.y - 3f, 6f, 6f), colour);
                previous = p;
            }

            var last = points[points.Count - 1];
            string trend = "";
            if (points.Count > 1)
            {
                int t = TrendData.Trend(value(points[points.Count - 2]), value(last), higherIsBetter, (max - min) * 0.02f);
                trend = $"   <color=#{ColorUtility.ToHtmlStringRGB(t > 0 ? Good : t < 0 ? Bad : Dim)}>{(t > 0 ? "better" : t < 0 ? "worse" : "flat")}</color>";
            }
            GUI.Label(new Rect(r.x + 8f, r.yMax - 22f, r.width - 16f, 20f), $"latest {value(last):F0}{unit}{(last.sample ? " (sample)" : "")}{trend}", Small);
        }

        // DETECTED > CLASSIFIED > ACTION as three boxes; lit green for a pass, red for a fail, grey when skipped.
        public static void TreePath(Rect r, ContactResult c)
        {
            string[] names = { "DETECTED", "CLASSIFIED", "ACTION" };
            float gap = 14f, w = (r.width - gap * 2f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var box = new Rect(r.x + i * (w + gap), r.y, w, r.height);
                bool reached = c.scored && i < c.nodes.Count;
                Color colour = !reached ? Neutral : c.nodes[i].passed ? Good : c.nodes[i].points < 0 ? Bad : i == 2 && c.unresolved ? Neutral : Bad;
                Fill(box, Color.Lerp(Panel, colour, reached ? 0.55f : 0.2f));
                Fill(new Rect(box.x, box.yMax - 3f, box.width, 3f), colour);
                string points = reached ? c.nodes[i].points.ToString("+0;-0;0") : "-";
                GUI.Label(new Rect(box.x + 6f, box.y, box.width - 12f, box.height * 0.5f), $"{names[i]}  <b>{points}</b>", Small);
                GUI.Label(new Rect(box.x + 6f, box.y + box.height * 0.45f, box.width - 12f, box.height * 0.5f), reached ? c.nodes[i].reason : (c.scored ? "skipped" : c.notScoredReason), Small);
                if (i < 2) Line(new Vector2(box.xMax, box.center.y), new Vector2(box.xMax + gap, box.center.y), reached && c.nodes[i].passed ? Good : Neutral, 2f);
            }
        }

        // One contact on the replay strip: grey = in the air unseen, blue = seen, amber = locked/called, then the action marker.
        public static void TimelineRow(Rect r, ContactResult c, float duration)
        {
            Fill(new Rect(r.x, r.center.y, r.width, 1f), new Color(1f, 1f, 1f, 0.06f));
            if (duration <= 0f || c.spawnTime < 0f) return;
            float end = c.despawnTime >= 0f ? c.despawnTime : duration;
            Segment(r, c.spawnTime, end, duration, Neutral, 0.35f);
            if (c.firstInViewTime >= 0f) Segment(r, c.firstInViewTime, end, duration, Accent, 0.5f);
            if (c.callTime >= 0f) Segment(r, c.callTime, c.actionTime >= 0f ? c.actionTime : end, duration, Warn, 0.7f);
            if (c.actionTime >= 0f)
            {
                bool pass = c.nodes.Count > 2 && c.nodes[2].passed;
                float x = r.x + r.width * Mathf.Clamp01(c.actionTime / duration);
                Fill(new Rect(x - 2f, r.y + 1f, 5f, r.height - 2f), pass ? Good : Bad);
            }
        }

        static void Segment(Rect r, float from, float to, float duration, Color colour, float heightShare)
        {
            float x0 = r.x + r.width * Mathf.Clamp01(from / duration), x1 = r.x + r.width * Mathf.Clamp01(to / duration);
            float h = r.height * heightShare;
            Fill(new Rect(x0, r.center.y - h * 0.5f, Mathf.Max(2f, x1 - x0), h), colour);
        }
    }
}
