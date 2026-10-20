using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Kurukshetra.Scoring
{
    public enum PrimKind { Rect, Line, Text, Dot }

    // One drawing primitive of a laid-out chart. The IMGUI dashboard (Trainer/ChartKit) and the HTML report (ChartSvg) both draw the
    // same list, so a chart looks the same in both. Coordinates are in the chart's own box, origin top-left, y down.
    public class Prim
    {
        public PrimKind kind;
        public float x, y, w, h, x2, y2;
        public ChartTone tone = ChartTone.Neutral;
        public float alpha = 1f, width = 1f, size;
        public string text, tip;
        public int anchor;                  // text: 0 start, 1 middle, 2 end
        public bool bold, dashed, dim, useTone, hit;
    }

    // Turns a ChartModel into primitives: titles, axes with ticks and units, legend, bars / lines / bins, value labels, reference lines
    // and invisible hover areas that carry the tooltip text. Pure maths, no drawing calls.
    public static class ChartLayout
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static float TextWidth(string s, float size, bool bold = false) => (s == null ? 0 : s.Length) * size * (bold ? 0.56f : 0.5f);

        static string Fit(string s, float maxWidth, float size)
        {
            if (s == null) return "";
            int max = Mathf.Max(3, Mathf.FloorToInt(maxWidth / (size * 0.5f)));
            return s.Length <= max ? s : s.Substring(0, Mathf.Max(1, max - 1)) + ".";
        }

        static string Num(float v) => v.ToString("0.#", Inv);

        static void T(List<Prim> p, float x, float y, string text, float size, int anchor = 0, bool bold = false, bool dim = false, ChartTone? tone = null)
        {
            p.Add(new Prim { kind = PrimKind.Text, x = x, y = y, text = text, size = size, anchor = anchor, bold = bold, dim = dim, useTone = tone.HasValue, tone = tone ?? ChartTone.Neutral });
        }

        static void L(List<Prim> p, float x1, float y1, float x2, float y2, ChartTone tone, float width, float alpha = 1f, bool dashed = false)
        {
            p.Add(new Prim { kind = PrimKind.Line, x = x1, y = y1, x2 = x2, y2 = y2, tone = tone, width = width, alpha = alpha, dashed = dashed });
        }

        static void R(List<Prim> p, float x, float y, float w, float h, ChartTone tone, float alpha = 1f)
        {
            p.Add(new Prim { kind = PrimKind.Rect, x = x, y = y, w = Mathf.Max(0f, w), h = Mathf.Max(0f, h), tone = tone, alpha = alpha });
        }

        static void Hit(List<Prim> p, float x, float y, float w, float h, string tip)
        {
            p.Add(new Prim { kind = PrimKind.Rect, hit = true, x = x, y = y, w = Mathf.Max(0f, w), h = Mathf.Max(0f, h), tip = tip });
        }

        // Tick step giving about four to five divisions.
        public static float Step(float range, bool integer)
        {
            float raw = range / 4f, mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(Mathf.Max(raw, 1e-6f))));
            float f = raw / mag, nice = f <= 1f ? 1f : f <= 2f ? 2f : f <= 2.5f ? 2.5f : f <= 5f ? 5f : 10f;
            float step = nice * mag;
            return integer ? Mathf.Max(1f, Mathf.Round(step)) : step;
        }

        public static List<Prim> Build(ChartModel m, float W, float H, float font, bool compact)
        {
            var P = new List<Prim>(128);
            float pad = compact ? 6f : 10f, fs = font, sm = Mathf.Max(8f, font - 2f), y = pad;

            if (!compact)
            {
                T(P, pad, y + fs, m.title, fs + 1f, 0, true);
                y += fs + 7f;
                if (!string.IsNullOrEmpty(m.note)) { T(P, pad, y + sm, Fit(m.note, W - pad * 2f, sm), sm, 0, false, true); y += sm + 6f; }
            }

            // Legend: one swatch per series, plus the green "right call" swatch when the chart uses green for it.
            bool legend = m.HasData && (m.series.Count > 1 || !string.IsNullOrEmpty(m.legendExtra));
            if (legend)
            {
                float lx = pad, ly = y;
                var entries = new List<KeyValuePair<string, ChartTone>>();
                if (!string.IsNullOrEmpty(m.legendExtra)) entries.Add(new KeyValuePair<string, ChartTone>(m.legendExtra, ChartTone.Good));
                foreach (var s in m.series) entries.Add(new KeyValuePair<string, ChartTone>(s.name, s.tone));
                foreach (var e in entries)
                {
                    float ew = 14f + TextWidth(e.Key, sm) + 12f;
                    if (lx + ew > W - pad && lx > pad) { lx = pad; ly += sm + 7f; }
                    R(P, lx, ly + 1f, 9f, 9f, e.Value);
                    T(P, lx + 13f, ly + sm - 1f, e.Key, sm, 0, false, true);
                    lx += ew;
                }
                y = ly + sm + 9f;
            }

            if (!m.HasData)
            {
                // Empty state: say why, never draw a made-up chart.
                float maxW = W - pad * 4f, cy = y + Mathf.Max(0f, (H - y - pad) * 0.5f) - fs;
                var words = (m.empty ?? "No data.").Split(' ');
                string line = "";
                var lines = new List<string>();
                foreach (string w in words)
                {
                    if (line.Length > 0 && TextWidth(line + " " + w, fs) > maxW) { lines.Add(line); line = w; }
                    else line = line.Length == 0 ? w : line + " " + w;
                }
                if (line.Length > 0) lines.Add(line);
                for (int i = 0; i < lines.Count; i++) T(P, W * 0.5f, cy + i * (fs + 3f) + fs, lines[i], fs, 1, false, true);
                return P;
            }

            switch (m.kind)
            {
                case ChartKind.Line: Vertical(P, m, W, H, y, pad, fs, sm, compact, false); break;
                case ChartKind.Histogram: Vertical(P, m, W, H, y, pad, fs, sm, compact, true); break;
                default: Horizontal(P, m, W, H, y, pad, fs, sm, compact); break;
            }
            return P;
        }

        // ---- Line and histogram: categories along x, values up ----

        static void Vertical(List<Prim> P, ChartModel m, float W, float H, float y, float pad, float fs, float sm, bool compact, bool hist)
        {
            int n = m.cats.Length;
            bool showTitles = !compact || H >= 130f;
            if (showTitles && !string.IsNullOrEmpty(m.yTitle)) { T(P, pad, y + sm, Fit(m.yTitle, W - pad * 2f, sm), sm, 0, false, true); y += sm + 5f; }
            float gutter = hist ? m.refs.Count * (sm + 2f) : 0f;
            float step = Step(m.yMax - m.yMin, hist);
            float labelW = TextWidth(Num(m.yMax), sm) + 9f;
            float bottomH = sm + 7f + (showTitles && !string.IsNullOrEmpty(m.xTitle) ? sm + 5f : 0f);
            var plot = new Rect(pad + labelW, y + 4f + gutter, W - pad * 2f - labelW, H - y - 4f - gutter - bottomH - pad);
            if (plot.width < 30f || plot.height < 24f) return;

            Func<float, float> py = v => plot.yMax - (v - m.yMin) / Mathf.Max(1e-6f, m.yMax - m.yMin) * plot.height;
            for (float t = m.yMin; t <= m.yMax + step * 0.001f; t += step)
            {
                float ty = py(t);
                if (t > m.yMin + 1e-4f) L(P, plot.x, ty, plot.xMax, ty, ChartTone.Neutral, 1f, 0.16f);
                T(P, plot.x - 5f, ty + sm * 0.35f, Num(t), sm, 2, false, true);
            }
            L(P, plot.x, plot.yMax, plot.xMax, plot.yMax, ChartTone.Neutral, 1.2f, 0.55f);
            L(P, plot.x, plot.y, plot.x, plot.yMax, ChartTone.Neutral, 1.2f, 0.4f);

            float slot = plot.width / n;
            // Hover areas first, so the tooltip texts are available to the renderers.
            for (int i = 0; i < n; i++) Hit(P, plot.x + slot * i, plot.y, slot, plot.height, TipFor(m, i, true));

            if (!hist)
            {
                var refLabelY = new List<float>();
                foreach (var r in m.refs)
                {
                    if (r.value < m.yMin || r.value > m.yMax) continue;
                    float ry = py(r.value);
                    refLabelY.Add(ry - 4f);
                    L(P, plot.x, ry, plot.xMax, ry, r.tone, 1.4f, 0.9f, r.dashed);
                    T(P, plot.xMax - 3f, ry - 4f, r.label, sm, 2, false, false, r.tone);
                }
                bool labelAll = m.series.Count == 1 && slot >= 34f;
                foreach (var s in m.series)
                {
                    float px = 0f, pyv = 0f; bool prev = false;
                    for (int i = 0; i < n; i++)
                    {
                        float v = s.values[i];
                        if (float.IsNaN(v)) { prev = false; continue; }
                        float x = plot.x + slot * (i + 0.5f), yy = py(Mathf.Clamp(v, m.yMin, m.yMax));
                        if (prev) L(P, px, pyv, x, yy, s.tone, s.dashed ? 1.6f : 2.2f, s.dashed ? 0.8f : 1f, s.dashed);
                        prev = true; px = x; pyv = yy;
                    }
                    if (s.dashed) continue;
                    for (int i = 0; i < n; i++)
                    {
                        float v = s.values[i];
                        if (float.IsNaN(v)) continue;
                        float x = plot.x + slot * (i + 0.5f), yy = py(Mathf.Clamp(v, m.yMin, m.yMax));
                        P.Add(new Prim { kind = PrimKind.Dot, x = x, y = yy, w = 7f, tone = s.ToneAt(i) });
                        bool last = LastReal(s.values, i);
                        if (labelAll || last)
                        {
                            bool below = yy < plot.y + sm + 4f;
                            if (!below && last && x > plot.xMax - plot.width * 0.2f) foreach (float ly in refLabelY) if (Mathf.Abs(ly - (yy - 7f)) < sm + 1f) below = true;   // keep clear of "pass mark" / "target"
                            T(P, x, below ? yy + sm + 5f : yy - 7f, Num(v) + m.unit, sm, last && x > plot.xMax - 26f ? 2 : 1, last, false, s.ToneAt(i));
                        }
                    }
                }
            }
            else
            {
                var s = m.series[0];
                float bw = slot * 0.78f;
                for (int i = 0; i < n; i++)
                {
                    float v = s.values[i], hh = (v - m.yMin) / Mathf.Max(1e-6f, m.yMax - m.yMin) * plot.height;
                    float bx = plot.x + slot * i + (slot - bw) * 0.5f;
                    if (v > 0f) R(P, bx, plot.yMax - hh, bw, hh, s.ToneAt(i), 0.92f);
                    if (v > 0f) T(P, bx + bw * 0.5f, plot.yMax - hh - 3f, Num(v), sm, 1, true);
                }
                int row = 0;
                var order = new List<ChartRef>(m.refs);
                order.Sort((a, b) => a.value.CompareTo(b.value));
                foreach (var r in order)
                {
                    float rx = plot.x + slot * r.value;
                    if (rx < plot.x || rx > plot.xMax) continue;
                    L(P, rx, plot.y - 3f - gutter + (row + 1) * (sm + 2f), rx, plot.yMax, r.tone, r.dashed ? 1.4f : 2f, 0.95f, r.dashed);
                    bool left = rx > plot.x + plot.width * 0.62f;
                    float ly = plot.y - gutter + (row + 1) * (sm + 2f) - 3f;
                    T(P, left ? rx - 4f : rx + 4f, ly, r.label, sm, left ? 2 : 0, false, false, r.tone);
                    row++;
                }
            }

            // X labels, thinned when they would collide.
            int every = 1;
            float maxLabel = 0f;
            foreach (string c in m.cats) maxLabel = Mathf.Max(maxLabel, TextWidth(c, sm));
            while (slot * every < maxLabel + 5f && every < n) every++;
            int lastShown = -1;
            for (int i = 0; i < n; i += every) { T(P, plot.x + slot * (i + 0.5f), plot.yMax + sm + 3f, m.cats[i], sm, 1, false, true); lastShown = i; }
            if (lastShown != n - 1 && slot * (n - 1 - lastShown) >= (TextWidth(m.cats[lastShown], sm) + TextWidth(m.cats[n - 1], sm)) * 0.5f + 9f)
                T(P, plot.x + slot * (n - 0.5f), plot.yMax + sm + 3f, m.cats[n - 1], sm, 1, false, true);   // the last bin ("never") is always named
            if (showTitles && !string.IsNullOrEmpty(m.xTitle)) T(P, plot.x + plot.width * 0.5f, H - pad - 1f, m.xTitle, sm, 1, false, true);
        }

        static bool LastReal(float[] v, int i)
        {
            for (int k = i + 1; k < v.Length; k++) if (!float.IsNaN(v[k])) return false;
            return true;
        }

        static string TipFor(ChartModel m, int i, bool allSeries)
        {
            if (m.series.Count == 1 && m.series[0].tips != null && i < m.series[0].tips.Length && !string.IsNullOrEmpty(m.series[0].tips[i])) return m.series[0].tips[i];
            var sb = new System.Text.StringBuilder();
            if (m.catTips != null && i < m.catTips.Length && !string.IsNullOrEmpty(m.catTips[i])) sb.Append(m.catTips[i]);
            else sb.Append(m.cats[i]);
            foreach (var s in m.series)
            {
                if (s.tips != null && i < s.tips.Length && !string.IsNullOrEmpty(s.tips[i])) { sb.Append('\n').Append(s.tips[i]); continue; }
                float v = s.values[i];
                sb.Append('\n').Append(s.name).Append(": ").Append(float.IsNaN(v) ? "no data" : Num(v) + m.unit);
            }
            return sb.ToString();
        }

        // ---- Bars and stacked bars: categories down the left, values across ----

        static void Horizontal(List<Prim> P, ChartModel m, float W, float H, float y, float pad, float fs, float sm, bool compact)
        {
            int n = m.cats.Length;
            bool stacked = m.kind == ChartKind.Stacked;
            bool showTitles = !compact || H >= 130f;
            float labelW = 0f;
            foreach (string c in m.cats) labelW = Mathf.Max(labelW, TextWidth(c, sm));
            labelW = Mathf.Clamp(labelW + 10f, 50f, W * 0.4f);
            float rightW = 0f;
            if (stacked && m.rowText != null) foreach (string t in m.rowText) rightW = Mathf.Max(rightW, TextWidth(t, sm) + 8f);
            else rightW = 34f;
            rightW = Mathf.Min(rightW, W * 0.36f);
            float bottomH = sm + 7f + (showTitles && !string.IsNullOrEmpty(m.xTitle) ? sm + 5f : 0f);
            var plot = new Rect(pad + labelW, y + 2f, W - pad * 2f - labelW - rightW, H - y - 2f - bottomH - pad);
            if (plot.width < 30f || plot.height < 16f) return;

            float step = Step(m.yMax - m.yMin, true);
            Func<float, float> px = v => plot.x + (v - m.yMin) / Mathf.Max(1e-6f, m.yMax - m.yMin) * plot.width;
            for (float t = m.yMin; t <= m.yMax + step * 0.001f; t += step)
            {
                float tx = px(t);
                if (t > m.yMin + 1e-4f) L(P, tx, plot.y, tx, plot.yMax, ChartTone.Neutral, 1f, 0.16f);
                T(P, tx, plot.yMax + sm + 3f, Num(t), sm, 1, false, true);
            }
            L(P, plot.x, plot.y, plot.x, plot.yMax, ChartTone.Neutral, 1.2f, 0.55f);
            L(P, plot.x, plot.yMax, plot.xMax, plot.yMax, ChartTone.Neutral, 1.2f, 0.4f);

            float rowH = plot.height / n, barH = Mathf.Min(rowH * 0.62f, 26f);
            for (int i = 0; i < n; i++)
            {
                float cy = plot.y + rowH * (i + 0.5f), by = cy - barH * 0.5f;
                T(P, plot.x - 6f, cy + sm * 0.35f, Fit(m.cats[i], labelW - 8f, sm), sm, 2);
                if (!stacked)
                {
                    var s = m.series[0];
                    float v = s.values[i], bx = px(v) - plot.x;
                    R(P, plot.x, by, Mathf.Max(v > 0f ? 2f : 2f, bx), barH, s.ToneAt(i), v > 0f ? 0.95f : 0.55f);
                    T(P, px(v) + 6f, cy + sm * 0.35f, Num(v), sm, 0, true);
                    Hit(P, plot.x, plot.y + rowH * i, plot.width + rightW, rowH, TipFor(m, i, false));
                }
                else
                {
                    float acc = 0f;
                    foreach (var s in m.series)
                    {
                        float v = s.values[i];
                        if (v <= 0f) continue;
                        float x0 = px(acc), x1 = px(acc + v);
                        R(P, x0, by, x1 - x0 - 1f, barH, s.ToneAt(i), 0.95f);
                        if (x1 - x0 >= TextWidth(Num(v), sm, true) + 6f) T(P, (x0 + x1) * 0.5f - 0.5f, cy + sm * 0.35f, Num(v), sm, 1, true);
                        Hit(P, x0, plot.y + rowH * i, x1 - x0, rowH, s.tips != null && i < s.tips.Length && !string.IsNullOrEmpty(s.tips[i]) ? s.tips[i] : TipFor(m, i, false));
                        acc += v;
                    }
                    if (m.rowText != null) T(P, px(acc) + 6f, cy + sm * 0.35f, m.rowText[i], sm, 0, false, true);
                }
            }
            if (showTitles && !string.IsNullOrEmpty(m.xTitle)) T(P, plot.x + plot.width * 0.5f, H - pad - 1f, m.xTitle, sm, 1, false, true);
        }
    }
}
