using System;
using System.Collections.Generic;
using System.Globalization;
using Chaukanna.Trainer;
using UnityEngine;

namespace Chaukanna.Scoring
{
    // The charts of the score dashboard, the AAR cards and the HTML report, as plain data. One ChartModel is drawn by the IMGUI
    // dashboard (Trainer/ChartKit) and by the HTML report (ChartSvg) through the same layout (ChartLayout), so the two always show the
    // same chart. Everything here comes from a SessionResult or from saved sessions: nothing is invented, and a chart with no data
    // says so (`empty`) instead of drawing a made-up one.

    // Colour roles. Good / Warn / Bad are the status colours and mean the same thing on every chart; S1..S5 are neutral category
    // colours (never green, amber or red) used to tell series apart.
    public enum ChartTone { Accent, Good, Warn, Bad, Neutral, S1, S2, S3, S4, S5 }
    public enum ChartKind { Line, Bars, Stacked, Histogram }

    public class ChartSeries
    {
        public string name;
        public float[] values;                       // NaN = no value (a gap in a line)
        public ChartTone tone = ChartTone.Accent;
        public bool dashed;
        public ChartTone[] pointTones;               // optional: the tone of each point / bar / segment
        public string[] tips;                        // optional: extra tooltip text per category

        public ChartTone ToneAt(int i) => pointTones != null && i < pointTones.Length ? pointTones[i] : tone;
    }

    public class ChartRef
    {
        public float value;                          // y value (line, bars) or x in category units (histogram)
        public string label;
        public ChartTone tone = ChartTone.Good;
        public bool dashed = true;
    }

    public class ChartModel
    {
        public string id, title, note = "", xTitle = "", yTitle = "", unit = "", empty = "No data yet.";
        public ChartKind kind;
        public string[] cats = new string[0];
        public List<ChartSeries> series = new List<ChartSeries>();
        public List<ChartRef> refs = new List<ChartRef>();
        public string[] rowText;                     // stacked: text after each row
        public string[] catTips;                     // tooltip text per category (first line)
        public float yMin, yMax = 100f;
        public bool yFixed;                          // keep yMax as given (percentages) instead of rounding up from the data
        public string legendExtra = "";              // e.g. "Right call" swatch for the green diagonal of the calls chart

        public bool HasData => cats != null && cats.Length > 0 && series.Count > 0 && AnyValue();

        bool AnyValue()
        {
            foreach (var s in series) foreach (float v in s.values) if (!float.IsNaN(v)) return true;
            return false;
        }
    }

    public static class ChartModels
    {
        public const float PassPercent = 70f, WarnPercent = 45f, DetectGoodSeconds = 6f, DetectBadSeconds = 12f;
        public const float HistBin = 2f;
        public const int HistBins = 10;      // 0-2 ... 18-20 s, then "20+" and "never seen"
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static ChartTone PercentTone(float p) => p >= PassPercent ? ChartTone.Good : p >= WarnPercent ? ChartTone.Warn : ChartTone.Bad;
        public static ChartTone SecondsTone(float s) => s <= DetectGoodSeconds ? ChartTone.Good : s <= DetectBadSeconds ? ChartTone.Warn : ChartTone.Bad;

        public static string ClassName(string raw)
        {
            switch (raw)
            {
                case "HostileQuad": return "Hostile quad";
                case "HostileFixedWing": return "Hostile fixed-wing";
                case "FriendlyISR": case "OwnIsrDrone": return "Own ISR drone";
                case "CivilianQuad": case "CivilianDrone": return "Civilian drone";
                case "NoCall": return "No call";
                default: return HtmlReport.Pretty(raw);
            }
        }

        // All six charts of the dashboard's Charts view, in order. `history` = saved sessions, oldest first (a sample-flagged point is skipped).
        public static List<ChartModel> All(SessionResult s, IList<TrendPoint> history)
        {
            return new List<ChartModel>
            {
                ScoreTrend(history), DetectionTrend(history), AccuracyTrend(history),
                WentWrong(s), CallsByClass(s), DetectionHistogram(s),
            };
        }

        // --- Trends over saved sessions -------------------------------------------------------------------------------------

        static List<TrendPoint> Real(IList<TrendPoint> history)
        {
            var list = new List<TrendPoint>();
            if (history != null) foreach (var p in history) if (!p.sample) list.Add(p);
            return list;
        }

        static string[] SessionLabels(List<TrendPoint> pts)
        {
            var l = new string[pts.Count];
            for (int i = 0; i < l.Length; i++) l[i] = (i + 1).ToString(Inv);
            return l;
        }

        static string[] SessionTips(List<TrendPoint> pts)
        {
            var t = new string[pts.Count];
            for (int i = 0; i < t.Length; i++) t[i] = "Session " + (i + 1) + (string.IsNullOrEmpty(pts[i].label) ? "" : ", " + pts[i].label);
            return t;
        }

        public static float[] Rolling(float[] v, int window)
        {
            var r = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float sum = 0f; int n = 0;
                for (int k = Mathf.Max(0, i - window + 1); k <= i; k++) if (!float.IsNaN(v[k])) { sum += v[k]; n++; }
                r[i] = n > 0 ? sum / n : float.NaN;
            }
            return r;
        }

        // Round up to a tidy axis maximum (5, 10, 15, 20, 30, 40, 60, 80, 100, 150 ...).
        public static float NiceMax(float v, float floor = 1f)
        {
            v = Mathf.Max(v, floor);
            float[] ladder = { 1, 2, 3, 4, 5, 6, 8, 10, 12, 15, 20, 25, 30, 40, 50, 60, 80, 100, 120, 150, 200, 250, 300, 400, 500, 600, 800, 1000 };
            foreach (float l in ladder) if (v <= l) return l;
            return Mathf.Ceil(v / 1000f) * 1000f;
        }

        static void SessionTrend(ChartModel m, List<TrendPoint> pts, Func<TrendPoint, float> value, string seriesName, Func<float, ChartTone> tone, bool average)
        {
            m.cats = SessionLabels(pts);
            m.catTips = SessionTips(pts);
            var v = new float[pts.Count];
            var tones = new ChartTone[pts.Count];
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = pts[i].noData ? float.NaN : value(pts[i]);
                tones[i] = float.IsNaN(v[i]) ? ChartTone.Neutral : tone(v[i]);
            }
            m.series.Add(new ChartSeries { name = seriesName, values = v, tone = ChartTone.Accent, pointTones = tones });
            if (average && pts.Count >= 3) m.series.Add(new ChartSeries { name = "3-session average", values = Rolling(v, 3), tone = ChartTone.S1, dashed = true });
        }

        public static ChartModel ScoreTrend(IList<TrendPoint> history)
        {
            var pts = Real(history);
            var m = new ChartModel { id = "score-trend", title = "Score by session", kind = ChartKind.Line, xTitle = "saved session (oldest to newest)", yTitle = "score, % of points available", unit = " %", yMin = 0f, yMax = 100f, yFixed = true,
                empty = "No saved sessions yet. Finish a run and it appears here." };
            m.refs.Add(new ChartRef { value = PassPercent, label = "pass mark " + PassPercent.ToString("0", Inv) + " %", tone = ChartTone.Good });
            if (pts.Count == 0) return m;
            SessionTrend(m, pts, p => p.scorePercent, "Score", PercentTone, true);
            m.note = $"{pts.Count} saved session{(pts.Count == 1 ? "" : "s")}; dot: green passes, amber from {WarnPercent:0} %, red below";
            return m;
        }

        public static ChartModel DetectionTrend(IList<TrendPoint> history)
        {
            var pts = Real(history);
            var m = new ChartModel { id = "detection-trend", title = "Detection time by session", kind = ChartKind.Line, xTitle = "saved session (oldest to newest)", yTitle = "average time to first seen, seconds", unit = " s", yMin = 0f,
                empty = "No saved session has a detected contact yet." };
            m.refs.Add(new ChartRef { value = DetectGoodSeconds, label = "target " + DetectGoodSeconds.ToString("0", Inv) + " s", tone = ChartTone.Good });
            float max = 0f;
            foreach (var p in pts) if (!p.noData && !p.noDetection) max = Mathf.Max(max, p.detectionSeconds);
            m.yMax = NiceMax(Mathf.Max(max * 1.1f, DetectGoodSeconds * 1.5f), 10f);
            if (pts.Count == 0) return m;
            var flat = pts;
            SessionTrend(m, flat, p => p.noDetection ? float.NaN : p.detectionSeconds, "Detection time", SecondsTone, true);
            // A session where nothing was seen has no average: it is a gap, not a zero.
            for (int i = 0; i < pts.Count; i++) if (pts[i].noDetection) { m.series[0].values[i] = float.NaN; m.series[0].pointTones[i] = ChartTone.Neutral; }
            if (m.series.Count > 1) m.series[1].values = Rolling(m.series[0].values, 3);
            m.note = "lower is better; a gap = nothing was seen that session";
            return m;
        }

        public static ChartModel AccuracyTrend(IList<TrendPoint> history)
        {
            var pts = Real(history);
            var m = new ChartModel { id = "accuracy-trend", title = "Classification and engagement by session", kind = ChartKind.Line, xTitle = "saved session (oldest to newest)", yTitle = "right, % of decisions", unit = " %", yMin = 0f, yMax = 100f, yFixed = true,
                empty = "No saved sessions yet. Finish a run and it appears here." };
            m.refs.Add(new ChartRef { value = PassPercent, label = "target " + PassPercent.ToString("0", Inv) + " %", tone = ChartTone.Good });
            if (pts.Count == 0) return m;
            m.cats = SessionLabels(pts);
            m.catTips = SessionTips(pts);
            var a = new float[pts.Count]; var b = new float[pts.Count];
            for (int i = 0; i < a.Length; i++) { a[i] = pts[i].noData ? float.NaN : pts[i].classificationAccuracy; b[i] = pts[i].noData ? float.NaN : pts[i].engagementScore; }
            m.series.Add(new ChartSeries { name = "Classified right", values = a, tone = ChartTone.Accent });
            m.series.Add(new ChartSeries { name = "Right action", values = b, tone = ChartTone.S2 });
            m.note = $"{pts.Count} saved session{(pts.Count == 1 ? "" : "s")}";
            return m;
        }

        // --- This run ---------------------------------------------------------------------------------------------------

        public static ChartModel WentWrong(SessionResult s)
        {
            var m = new ChartModel { id = "went-wrong", title = "What went wrong this run", kind = ChartKind.Bars, xTitle = "contacts", empty = "No scored contacts in this run yet." };
            if (s == null || s.scoredContacts <= 0) return m;
            string[] names = { "Never seen (missed)", "Left unresolved", "False engagements", "Hostiles not engaged", "Followed a wrong AI cue" };
            float[] v = { s.missed, s.unresolved, s.falseEngagements, s.missedHostiles, s.followedWrongHint };
            ChartTone[] bad = { ChartTone.Bad, ChartTone.Warn, ChartTone.Bad, ChartTone.Bad, ChartTone.Warn };
            var tones = new ChartTone[v.Length];
            float max = 0f; int total = 0;
            for (int i = 0; i < v.Length; i++) { tones[i] = v[i] > 0f ? bad[i] : ChartTone.Good; max = Mathf.Max(max, v[i]); total += (int)v[i]; }
            m.cats = names;
            m.series.Add(new ChartSeries { name = "Contacts", values = v, tone = ChartTone.Bad, pointTones = tones });
            m.yMin = 0f; m.yMax = NiceMax(max, 4f);
            m.note = total == 0 ? $"nothing went wrong in {s.scoredContacts} scored contacts" : $"of {s.scoredContacts} scored contacts; green = none";
            return m;
        }

        public static ChartModel CallsByClass(SessionResult s)
        {
            var m = new ChartModel { id = "calls-by-class", title = "Called as, by true class", kind = ChartKind.Stacked, xTitle = "detected contacts", empty = "Nothing was classified in this run yet.", legendExtra = "Right call" };
            if (s == null || s.confusionRows == null || s.confusionColumns == null || s.confusion == null) return m;
            int rows = s.confusionRows.Length, cols = s.confusionColumns.Length;
            var keep = new List<int>();
            for (int r = 0; r < rows; r++)
            {
                int t = 0;
                for (int c = 0; c < cols; c++) t += s.confusion[r * cols + c];
                if (t > 0) keep.Add(r);
            }
            if (keep.Count == 0) return m;
            ChartTone[] palette = { ChartTone.S1, ChartTone.S2, ChartTone.S3, ChartTone.S4, ChartTone.S5 };
            m.cats = new string[keep.Count];
            m.rowText = new string[keep.Count];
            m.catTips = new string[keep.Count];
            float max = 0f;
            for (int c = 0; c < cols; c++)
            {
                var v = new float[keep.Count];
                var tones = new ChartTone[keep.Count];
                var tips = new string[keep.Count];
                for (int k = 0; k < keep.Count; k++)
                {
                    int r = keep[k];
                    int n = s.confusion[r * cols + c], tot = 0;
                    for (int q = 0; q < cols; q++) tot += s.confusion[r * cols + q];
                    v[k] = n;
                    bool noCall = c == cols - 1;
                    tones[k] = noCall ? ChartTone.Neutral : c == r ? ChartTone.Good : palette[c % palette.Length];
                    tips[k] = $"{ClassName(s.confusionRows[r])} called as {ClassName(s.confusionColumns[c])}: {n} of {tot} ({(tot > 0 ? 100f * n / tot : 0f):0} %)" + (c == r ? " (right)" : "");
                }
                m.series.Add(new ChartSeries { name = noCall(c, cols) ? "No call" : "Called " + ClassName(s.confusionColumns[c]), values = v, tone = c == cols - 1 ? ChartTone.Neutral : palette[c % palette.Length], pointTones = tones, tips = tips });
            }
            for (int k = 0; k < keep.Count; k++)
            {
                int r = keep[k], tot = 0;
                for (int q = 0; q < cols; q++) tot += s.confusion[r * cols + q];
                int right = s.confusion[r * cols + r];
                m.cats[k] = ClassName(s.confusionRows[r]);
                m.rowText[k] = $"n = {tot}, {100f * right / Mathf.Max(1, tot):0} % right";
                m.catTips[k] = m.cats[k];
                max = Mathf.Max(max, tot);
            }
            m.yMin = 0f; m.yMax = NiceMax(max, 4f);
            m.note = "one bar per true class, split by what the soldier called it";
            return m;
        }

        static bool noCall(int c, int cols) => c == cols - 1;

        // Seconds until first seen, in 2 s bins, with the median marked. `never` = scored contacts that never came into view.
        public static ChartModel DetectionHistogram(SessionResult s)
        {
            var times = new List<float>();
            int never = 0, scored = 0;
            if (s != null && s.contacts != null)
                foreach (var k in s.contacts)
                {
                    if (!k.scored) continue;
                    scored++;
                    if (k.detected && k.detectionSeconds >= 0f) times.Add(k.detectionSeconds); else never++;
                }
            return DetectionHistogram(times, never, scored);
        }

        public static ChartModel DetectionHistogram(IList<float> times, int never, int scored)
        {
            var m = new ChartModel { id = "detect-hist", title = "Time to detect each contact", kind = ChartKind.Histogram, xTitle = "seconds until first seen", yTitle = "contacts", empty = scored > 0 ? "Nothing was brought into view yet." : "No scored contacts in this run yet." };
            if (scored <= 0 && (times == null || times.Count == 0)) return m;
            int nb = HistBins + 2;
            var counts = new float[nb];
            var cats = new string[nb];
            var tones = new ChartTone[nb];
            var tips = new string[nb];
            for (int i = 0; i < HistBins; i++) { cats[i] = (i * HistBin).ToString("0", Inv) + "-" + ((i + 1) * HistBin).ToString("0", Inv); tones[i] = SecondsTone(i * HistBin + 0.01f); }
            cats[HistBins] = (HistBins * HistBin).ToString("0", Inv) + "+"; tones[HistBins] = ChartTone.Bad;
            cats[HistBins + 1] = "never"; tones[HistBins + 1] = ChartTone.Bad;
            var sorted = new List<float>();
            if (times != null)
                foreach (float t in times)
                {
                    sorted.Add(t);
                    int b = Mathf.Min(HistBins, Mathf.FloorToInt(t / HistBin));
                    counts[b]++;
                }
            counts[HistBins + 1] = never;
            for (int i = 0; i < nb; i++)
                tips[i] = i < HistBins ? $"{cats[i]} s: {counts[i]:0} contact{(counts[i] == 1 ? "" : "s")}" : i == HistBins ? $"over {HistBins * HistBin:0} s: {counts[i]:0} contact{(counts[i] == 1 ? "" : "s")}" : $"never came into view: {counts[i]:0}";
            m.cats = cats;
            m.series.Add(new ChartSeries { name = "Contacts", values = counts, tone = ChartTone.Accent, pointTones = tones, tips = tips });
            float max = 0f;
            foreach (float c in counts) max = Mathf.Max(max, c);
            m.yMin = 0f; m.yMax = NiceMax(max, 4f);
            m.refs.Add(new ChartRef { value = DetectGoodSeconds / HistBin, label = "target " + DetectGoodSeconds.ToString("0", Inv) + " s", tone = ChartTone.Good });
            if (sorted.Count > 0)
            {
                sorted.Sort();
                float med = sorted.Count % 2 == 1 ? sorted[sorted.Count / 2] : 0.5f * (sorted[sorted.Count / 2 - 1] + sorted[sorted.Count / 2]);
                float x = med < HistBins * HistBin ? med / HistBin : HistBins + 0.5f;
                m.refs.Add(new ChartRef { value = x, label = "median " + med.ToString("0.0", Inv) + " s", tone = ChartTone.Accent, dashed = false });
                m.note = $"{sorted.Count} seen" + (never > 0 ? $", {never} never seen" : "") + $"; median {med:0.0} s";
            }
            else m.note = never > 0 ? $"{never} scored contact{(never == 1 ? "" : "s")}, none seen" : "";
            return m;
        }
    }
}
