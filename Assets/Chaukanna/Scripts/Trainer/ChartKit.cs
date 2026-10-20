using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Chaukanna.Scoring;
using UnityEngine;
using C = Chaukanna.Trainer.DashboardCharts;

namespace Chaukanna.Trainer
{
    // Draws a ChartModel (Scoring/ChartModels.cs) with IMGUI. The layout comes from ChartLayout, the same one the HTML report uses, so
    // the dashboard, the AAR cards and the report show the same chart. Every chart is drawn inside its own GUI group, so nothing can
    // land outside its rectangle, and every line goes through GuiDraw (clipped, never a rotated strip under a scaled GUI.matrix: that
    // was the bug that threw blue diagonals over the game view).
    public static class ChartKit
    {
        // Category colours: none of them is green, amber, red or the accent blue, so they never read as a status.
        public static readonly Color S1 = new Color(0.25f, 0.72f, 0.72f), S2 = new Color(0.78f, 0.38f, 0.70f), S3 = new Color(0.60f, 0.48f, 0.90f),
            S4 = new Color(0.92f, 0.60f, 0.72f), S5 = new Color(0.66f, 0.50f, 0.36f);

        public static Color ToneColour(ChartTone t)
        {
            switch (t)
            {
                case ChartTone.Accent: return C.Accent;
                case ChartTone.Good: return C.Good;
                case ChartTone.Warn: return C.Warn;
                case ChartTone.Bad: return C.Bad;
                case ChartTone.S1: return S1;
                case ChartTone.S2: return S2;
                case ChartTone.S3: return S3;
                case ChartTone.S4: return S4;
                case ChartTone.S5: return S5;
                default: return C.Neutral;
            }
        }

        class Cached { public float w, h, font; public bool compact; public List<Prim> prims; }
        static readonly ConditionalWeakTable<ChartModel, Cached> cache = new ConditionalWeakTable<ChartModel, Cached>();
        static GUIStyle textStyle;

        // Fraction used for layout: the label font of the dashboard, a little smaller in a card.
        static float BaseFont(bool compact) => compact ? Mathf.Max(9f, C.Small.fontSize + 1f) : Mathf.Max(10f, C.Label.fontSize - 1f);

        // Draws `m` in `r` (a panel with its border unless `compact`, for use inside a card). Call from OnGUI; it draws on repaint only.
        public static void Draw(Rect r, ChartModel m, bool compact = false)
        {
            if (m == null || Event.current.type != EventType.Repaint) return;
            if (textStyle == null) textStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Overflow, richText = false, padding = new RectOffset(), margin = new RectOffset() };

            if (!compact)
            {
                C.Fill(r, new Color(1f, 1f, 1f, 0.10f));
                C.Fill(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), C.Panel);
            }
            float font = BaseFont(compact);
            var c = cache.GetOrCreateValue(m);
            if (c.prims == null || c.w != r.width || c.h != r.height || c.font != font || c.compact != compact)
            {
                c.w = r.width; c.h = r.height; c.font = font; c.compact = compact;
                c.prims = ChartLayout.Build(m, r.width, r.height, font, compact);
            }

            var clip = new Rect(0f, 0f, r.width, r.height);
            string tip = null;
            Rect tipArea = default;
            GUI.BeginGroup(r);
            Vector2 local = Event.current.mousePosition;   // inside a group, mousePosition is already relative to it
            foreach (var p in c.prims)
            {
                switch (p.kind)
                {
                    case PrimKind.Rect:
                        if (p.hit)
                        {
                            if (new Rect(p.x, p.y, p.w, p.h).Contains(local) && p.tip != null)
                            {
                                tip = p.tip; tipArea = new Rect(p.x, p.y, p.w, p.h);
                                if (m.kind == ChartKind.Line || m.kind == ChartKind.Histogram) C.Fill(tipArea, new Color(1f, 1f, 1f, 0.06f));
                                else C.Fill(tipArea, new Color(1f, 1f, 1f, 0.05f));
                            }
                        }
                        else C.Fill(new Rect(p.x, p.y, p.w, p.h), WithAlpha(ToneColour(p.tone), p.alpha));
                        break;
                    case PrimKind.Line:
                        {
                            var col = WithAlpha(ToneColour(p.tone), p.alpha);
                            if (p.dashed) Dashed(new Vector2(p.x, p.y), new Vector2(p.x2, p.y2), p.width, col, clip);
                            else GuiDraw.Line(new Vector2(p.x, p.y), new Vector2(p.x2, p.y2), p.width, col, clip);
                        }
                        break;
                    case PrimKind.Dot:
                        {
                            float s = p.w;
                            C.Fill(new Rect(p.x - s * 0.5f - 1.5f, p.y - s * 0.5f - 1.5f, s + 3f, s + 3f), C.Panel);
                            C.Fill(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), ToneColour(p.tone));
                        }
                        break;
                    case PrimKind.Text:
                        Text(p);
                        break;
                }
            }
            if (tip != null) Tooltip(tip, local, clip);
            GUI.EndGroup();
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        static void Dashed(Vector2 a, Vector2 b, float width, Color col, Rect clip)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.01f) return;
            Vector2 dir = d / len;
            for (float t = 0f; t < len; t += 9f) GuiDraw.Line(a + dir * t, a + dir * Mathf.Min(len, t + 5f), width, col, clip);
        }

        static void Text(Prim p)
        {
            textStyle.fontSize = Mathf.RoundToInt(p.size);
            textStyle.fontStyle = p.bold ? FontStyle.Bold : FontStyle.Normal;
            var col = p.useTone ? ToneColour(p.tone) : p.dim ? C.Dim : C.Text;
            textStyle.normal.textColor = col;
            float h = p.size * 1.7f, top = p.y - p.size * 1.2f, w = ChartLayout.TextWidth(p.text, p.size, true) + 24f;
            Rect r;
            if (p.anchor == 1) { textStyle.alignment = TextAnchor.MiddleCenter; r = new Rect(p.x - w * 0.5f, top, w, h); }
            else if (p.anchor == 2) { textStyle.alignment = TextAnchor.MiddleRight; r = new Rect(p.x - w, top, w, h); }
            else { textStyle.alignment = TextAnchor.MiddleLeft; r = new Rect(p.x, top, w, h); }
            GUI.Label(r, p.text, textStyle);
        }

        static void Tooltip(string tip, Vector2 at, Rect bounds)
        {
            var lines = tip.Split('\n');
            float fs = Mathf.Max(10f, C.Label.fontSize - 1f), w = 0f;
            foreach (string l in lines) w = Mathf.Max(w, ChartLayout.TextWidth(l, fs));
            w += 16f;
            float h = lines.Length * (fs + 4f) + 8f;
            var box = new Rect(at.x + 14f, at.y + 16f, w, h);
            if (box.xMax > bounds.xMax - 2f) box.x = Mathf.Max(bounds.x + 2f, at.x - 14f - w);
            if (box.yMax > bounds.yMax - 2f) box.y = Mathf.Max(bounds.y + 2f, at.y - 12f - h);
            C.Fill(box, new Color(1f, 1f, 1f, 0.35f));
            C.Fill(new Rect(box.x + 1f, box.y + 1f, box.width - 2f, box.height - 2f), new Color(0.04f, 0.05f, 0.07f, 0.97f));
            textStyle.fontSize = Mathf.RoundToInt(fs);
            textStyle.alignment = TextAnchor.MiddleLeft;
            textStyle.fontStyle = FontStyle.Normal;
            for (int i = 0; i < lines.Length; i++)
            {
                textStyle.normal.textColor = i == 0 ? C.Text : C.Dim;
                GUI.Label(new Rect(box.x + 8f, box.y + 4f + i * (fs + 4f), w, fs + 4f), lines[i], textStyle);
            }
        }
    }
}
