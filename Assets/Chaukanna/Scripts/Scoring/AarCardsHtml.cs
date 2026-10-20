using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Chaukanna.Scoring
{
    // The card grid for the HTML report: one <article class="card"> per AarCard with an inline SVG chart. No scripts, no network.
    public static class AarCardsHtml
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string N(float v) => v.ToString("0.#", Inv);
        static string Esc(string s) => HtmlReport.Esc(s);
        static string Cls(CardStatus s) => s == CardStatus.Good ? "sg" : s == CardStatus.Warn ? "sw" : s == CardStatus.Bad ? "sb" : "sn";

        public const string GridMarker = "class=\"cards\"";

        // The report's two views, switched by a pair of radio buttons (no script): Cards, and Charts (the same six charts as the dashboard).
        public static void Append(StringBuilder h, List<AarCard> cards, SessionResult s, IList<Chaukanna.Trainer.TrendPoint> history)
        {
            h.Append("<input type=\"radio\" name=\"view\" id=\"v-cards\" class=\"vt\" checked><input type=\"radio\" name=\"view\" id=\"v-charts\" class=\"vt\">");
            h.Append("<div class=\"viewtabs noprint\"><label for=\"v-cards\">Cards</label><label for=\"v-charts\">Charts</label></div><div class=\"viewbody\">");
            Append(h, cards);
            ChartSvg.Section(h, s, history);
            h.Append("</div>");
        }

        // One filterable view of the report: everything (all players), one unit, or one player.
        public class ScopeView
        {
            public string key, label;
            public SessionResult session;
            public IList<Chaukanna.Trainer.TrendPoint> history;
        }

        // The same two views (Cards / Charts) once per scope, with a drop-down above them that shows one scope at a time. The scopes are
        // pre-rendered; the few lines of script only toggle which one is visible, so the file still works offline and without the script
        // (the first scope, All, is then the one shown).
        public static void AppendScoped(StringBuilder h, IList<ScopeView> views, IList<LearnerModel.Cell> weakest)
        {
            h.Append("<div class=\"scopebar noprint\"><label for=\"scope-sel\">Show</label><select id=\"scope-sel\">");
            for (int i = 0; i < views.Count; i++)
                h.Append("<option value=\"").Append(Esc(views[i].key)).Append("\"").Append(i == 0 ? " selected" : "").Append(">").Append(Esc(views[i].label)).Append("</option>");
            h.Append("</select></div>");
            h.Append("<input type=\"radio\" name=\"view\" id=\"v-cards\" class=\"vt\" checked><input type=\"radio\" name=\"view\" id=\"v-charts\" class=\"vt\">");
            h.Append("<div class=\"viewtabs noprint\"><label for=\"v-cards\">Cards</label><label for=\"v-charts\">Charts</label></div><div class=\"viewbody\">");
            for (int i = 0; i < views.Count; i++)
            {
                var v = views[i];
                var part = new StringBuilder();
                Append(part, AarCards.Build(v.session, v.history, weakest));
                ChartSvg.Section(part, v.session, v.history);
                h.Append("<div class=\"scope\" data-scope=\"").Append(Esc(v.key)).Append("\"").Append(i == 0 ? "" : " hidden").Append(">")
                    .Append(part.ToString().Replace("id=\"card-", "id=\"s" + i + "-card-").Replace("id=\"chart-", "id=\"s" + i + "-chart-")).Append("</div>");
            }
            h.Append("</div>");
        }

        public const string ScopeScript = "(function(){var s=document.getElementById('scope-sel');if(!s)return;function go(){var v=s.value,a=document.querySelectorAll('.scope');for(var i=0;i<a.length;i++)a[i].hidden=a[i].getAttribute('data-scope')!==v;}s.addEventListener('change',go);go();})();";

        public static void Append(StringBuilder h, List<AarCard> cards)
        {
            h.Append("<section class=\"cardsec\"><h2>Cards: weakest problem first</h2><div ").Append(GridMarker).Append(">");
            foreach (var c in cards)
            {
                h.Append("<article class=\"card ").Append(Cls(c.status)).Append(c.tall ? " tall" : "").Append("\" id=\"card-").Append(c.id).Append("\">");
                h.Append("<div class=\"ct\"><span class=\"dot\"></span><span class=\"cn\">").Append(Esc(c.title)).Append("</span><span class=\"cw\">").Append(c.StatusWord).Append("</span></div>");
                h.Append("<div class=\"hero\">").Append(Esc(c.hero)).Append("</div>");
                h.Append("<p class=\"verdict\">").Append(Esc(c.verdict)).Append("</p>");
                h.Append("<svg class=\"chart\" viewBox=\"0 0 320 ").Append(c.tall ? "150" : "110").Append("\" role=\"img\" aria-label=\"").Append(Esc(c.title)).Append("\">");
                Chart(h, c);
                h.Append("</svg><div class=\"cf\">").Append(Esc(c.footer)).Append("</div></article>");
            }
            h.Append("</div></section>");
        }

        static void Text(StringBuilder h, float x, float y, string t, string cls = "", string anchor = "start")
        {
            h.Append("<text x=\"").Append(N(x)).Append("\" y=\"").Append(N(y)).Append("\" text-anchor=\"").Append(anchor).Append("\" class=\"").Append(cls).Append("\">").Append(Esc(t)).Append("</text>");
        }

        static void Rect(StringBuilder h, float x, float y, float w, float hh, string cls)
        {
            h.Append("<rect x=\"").Append(N(x)).Append("\" y=\"").Append(N(y)).Append("\" width=\"").Append(N(Mathf.Max(0f, w))).Append("\" height=\"").Append(N(Mathf.Max(0f, hh))).Append("\" rx=\"2\" class=\"").Append(cls).Append("\"/>");
        }

        static void Chart(StringBuilder h, AarCard c)
        {
            if (c.model != null && (c.chart == CardChart.Histogram || c.chart == CardChart.Line)) { ChartSvg.Draw(h, c.model, 320f, 150f, 10f, true); return; }
            switch (c.chart)
            {
                case CardChart.Bars: Bars(h, c); break;
                case CardChart.Histogram: Histogram(h, c); break;
                case CardChart.Heatmap: Heatmap(h, c); break;
                case CardChart.Stacked: Stacked(h, c); break;
                case CardChart.Grid: GridChart(h, c); break;
                case CardChart.Sliders: Sliders(h, c); break;
                case CardChart.Ticks: Ticks(h, c); break;
                case CardChart.Timeline: Timeline(h, c); break;
                case CardChart.Line: Line(h, c); break;
                case CardChart.CardChange: CardChange(h, c); break;
            }
        }

        static void Empty(StringBuilder h, string text = "no data") { Text(h, 160f, 60f, text, "mut", "middle"); }

        static void Bars(StringBuilder h, AarCard c)
        {
            if (c.values == null || c.values.Length == 0) { Empty(h); return; }
            int n = c.values.Length;
            float rowH = Mathf.Min(26f, 104f / n), x0 = 150f, w = 120f;
            for (int i = 0; i < n; i++)
            {
                float y = 4f + i * rowH;
                Text(h, 0f, y + rowH * 0.62f, Trim(c.labels[i], 28), "lab");
                Rect(h, x0, y + 2f, w, rowH - 6f, "trk");
                Rect(h, x0, y + 2f, w * Mathf.Clamp01(c.values[i] / Mathf.Max(0.0001f, c.max)), rowH - 6f, Cls(c.barStatus != null ? c.barStatus[i] : CardStatus.Neutral));
                Text(h, x0 + w + 6f, y + rowH * 0.62f, c.valueText != null ? c.valueText[i] : N(c.values[i]), "val");
            }
        }

        static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + ".";

        static void Histogram(StringBuilder h, AarCard c)
        {
            if (c.values == null || c.values.Length == 0) { Empty(h, "nothing detected"); return; }
            int n = Mathf.Min(c.values.Length, 40);
            float max = 1f, sum = 0f;
            for (int i = 0; i < n; i++) { max = Mathf.Max(max, c.values[i]); sum += c.values[i]; }
            float bw = Mathf.Min(18f, 300f / n), gap = Mathf.Min(3f, bw * 0.2f), top = 8f, bottom = 92f;
            for (int i = 0; i < n; i++)
            {
                float v = c.values[i], bh = (bottom - top) * v / max;
                Rect(h, 10f + i * bw, bottom - bh, bw - gap, bh, v > 10f ? "sb" : v > 6f ? "sw" : "sg");
            }
            float avg = sum / n, ay = bottom - (bottom - top) * avg / max;
            h.Append("<line x1=\"8\" x2=\"312\" y1=\"").Append(N(ay)).Append("\" y2=\"").Append(N(ay)).Append("\" class=\"avg\"/>");
            h.Append("<line x1=\"8\" x2=\"312\" y1=\"").Append(N(bottom)).Append("\" y2=\"").Append(N(bottom)).Append("\" class=\"axis\"/>");
            Text(h, 8f, 106f, "one bar per contact, seconds; dashed = average", "mut");
        }

        static void Heatmap(StringBuilder h, AarCard c)
        {
            var s = c.session;
            if (s == null || s.confusion == null || s.confusionRows == null) { Empty(h); return; }
            int rows = s.confusionRows.Length, cols = s.confusionColumns.Length;
            float lw = 78f, cw = (316f - lw) / cols, ch = 20f, max = 1f;
            foreach (int v in s.confusion) max = Mathf.Max(max, v);
            for (int k = 0; k < cols; k++) Text(h, lw + k * cw + cw / 2f, 10f, ShortName(s.confusionColumns[k]), "mut", "middle");
            for (int r = 0; r < rows; r++)
            {
                Text(h, 0f, 22f + r * ch + 14f, ShortName(s.confusionRows[r]), "lab");
                for (int k = 0; k < cols; k++)
                {
                    int n = s.confusion[r * cols + k];
                    string tone = k == cols - 1 ? "hn" : k == r ? "hg" : "hb";
                    float op = n == 0 ? 0.05f : 0.3f + 0.7f * n / max;
                    h.Append("<rect x=\"").Append(N(lw + k * cw + 1f)).Append("\" y=\"").Append(N(22f + r * ch + 1f)).Append("\" width=\"").Append(N(cw - 2f)).Append("\" height=\"").Append(N(ch - 2f)).Append("\" rx=\"2\" class=\"").Append(tone).Append("\" opacity=\"").Append(op.ToString("0.00", Inv)).Append("\"/>");
                    if (n > 0) Text(h, lw + k * cw + cw / 2f, 22f + r * ch + 14f, n.ToString(), "num", "middle");
                }
            }
        }

        static string ShortName(string name)
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

        static void Stacked(StringBuilder h, AarCard c)
        {
            if (c.labels == null || c.labels.Length == 0) { Empty(h, "no decisions"); return; }
            int n = c.labels.Length, mx = 1;
            for (int i = 0; i < n; i++) mx = Mathf.Max(mx, c.right[i] + c.wrong[i]);
            float rowH = Mathf.Min(30f, 100f / n), x0 = 56f, w = 150f;
            for (int i = 0; i < n; i++)
            {
                float y = 6f + i * rowH, wr = w * c.right[i] / mx, ww = w * c.wrong[i] / mx;
                Text(h, 0f, y + rowH * 0.6f, "Card " + Trim(c.labels[i], 6), "lab");
                Rect(h, x0, y + 2f, wr, rowH - 6f, "sg");
                Rect(h, x0 + wr, y + 2f, ww, rowH - 6f, "sb");
                Text(h, x0 + wr + ww + 6f, y + rowH * 0.6f, $"{c.right[i]} right, {c.wrong[i]} wrong", "val");
            }
        }

        static void GridChart(StringBuilder h, AarCard c)
        {
            if (c.counts == null) { Empty(h); return; }
            float cw = 154f, ch = 44f;
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2) * (cw + 8f), y = 4f + (i / 2) * (ch + 8f);
                Rect(h, x, y, cw, ch, "trk");
                Rect(h, x, y, 5f, ch, Cls(c.barStatus[i]));
                Text(h, x + 14f, y + 28f, c.counts[i].ToString(), "bigv");
                Text(h, x + 44f, y + 27f, c.labels[i], "lab");
            }
        }

        static void Sliders(StringBuilder h, AarCard c)
        {
            for (int i = 0; i < 2; i++)
            {
                var r = c.sdt[i];
                float y = 14f + i * 48f;
                Text(h, 0f, y, c.labels[i] + $"  {r.hits}/{r.signals} hits, {r.falseAlarms}/{r.noise} false alarms", "lab");
                if (!r.gated) { Text(h, 0f, y + 20f, "d' and c: insufficient data (needs 20 of each)", "mut"); continue; }
                Rect(h, 4f, y + 12f, 210f, 8f, "trk");
                h.Append("<line x1=\"109\" x2=\"109\" y1=\"").Append(N(y + 8f)).Append("\" y2=\"").Append(N(y + 24f)).Append("\" class=\"axis\"/>");
                float t = Mathf.InverseLerp(-1.5f, 1.5f, r.c);
                Rect(h, 4f + t * 210f - 4f, y + 8f, 8f, 16f, Mathf.Abs(r.c) < 0.3f ? "sg" : "sw");
                Text(h, 222f, y + 20f, $"d' {r.dPrime.ToString("F2", Inv)}  c {r.c.ToString("+0.00;-0.00;0.00", Inv)}", "val");
                Text(h, 4f, y + 34f, "trigger-happy", "mut");
                Text(h, 214f, y + 34f, "hesitant", "mut", "end");
            }
        }

        static void Ticks(StringBuilder h, AarCard c)
        {
            if (c.counts == null || c.counts.Length == 0) { Empty(h, "no clock started"); return; }
            int n = c.counts.Length, perRow = Mathf.Max(1, Mathf.Min(n, 20));
            float sz = Mathf.Min(14f, 300f / perRow - 2f);
            for (int i = 0; i < n; i++)
            {
                float x = 8f + (i % perRow) * (sz + 3f), y = 8f + (i / perRow) * (sz + 4f);
                Rect(h, x, y, sz, sz, c.counts[i] == 1 ? "sb" : c.counts[i] == 2 ? "sn" : "sg");
            }
            Text(h, 8f, 104f, "green = decided in time, red = missed, grey = correctly left alone", "mut");
        }

        static void Timeline(StringBuilder h, AarCard c)
        {
            var s = c.session;
            if (s == null || s.contacts.Count == 0) { Empty(h); return; }
            float dur = Mathf.Max(1f, s.duration);
            int rows = Mathf.Min(s.contacts.Count, 24);
            float rowH = Mathf.Min(5.5f, 120f / rows) ;
            rowH = Mathf.Max(3.5f, rowH);
            for (int i = 0; i < rows; i++)
            {
                var k = s.contacts[i];
                float y = 6f + i * (rowH + 1f);
                if (k.spawnTime < 0f) continue;
                float end = k.despawnTime >= 0f ? k.despawnTime : dur;
                Rect(h, 4f + 312f * Mathf.Clamp01(k.spawnTime / dur), y, 312f * Mathf.Clamp01((end - k.spawnTime) / dur), rowH, "air");
                if (k.firstInViewTime >= 0f) Rect(h, 4f + 312f * Mathf.Clamp01(k.firstInViewTime / dur), y, 312f * Mathf.Clamp01((end - k.firstInViewTime) / dur), rowH, "inv");
                if (k.actionTime >= 0f) Rect(h, 4f + 312f * Mathf.Clamp01(k.actionTime / dur) - 1.5f, y - 0.5f, 3f, rowH + 1f, k.nodes.Count > 2 && k.nodes[2].passed ? "sg" : "sb");
            }
            Text(h, 4f, 146f, "grey = in the air, blue = in view, bar = action (green right, red wrong)", "mut");
        }

        static void Line(StringBuilder h, AarCard c)
        {
            if (c.values == null || c.values.Length == 0) { Empty(h, "no earlier sessions"); return; }
            int n = c.values.Length;
            float x0 = 12f, w = 296f, top = 8f, bottom = 92f;
            h.Append("<line x1=\"8\" x2=\"312\" y1=\"").Append(N(bottom)).Append("\" y2=\"").Append(N(bottom)).Append("\" class=\"axis\"/>");
            h.Append("<line x1=\"8\" x2=\"312\" y1=\"").Append(N((top + bottom) / 2f)).Append("\" y2=\"").Append(N((top + bottom) / 2f)).Append("\" class=\"grid\"/>");
            float px = 0f, py = 0f;
            for (int i = 0; i < n; i++)
            {
                float x = n > 1 ? x0 + w * i / (n - 1) : x0 + w / 2f, y = bottom - (bottom - top) * Mathf.Clamp01(c.values[i] / 100f);
                if (i > 0) h.Append("<line x1=\"").Append(N(px)).Append("\" y1=\"").Append(N(py)).Append("\" x2=\"").Append(N(x)).Append("\" y2=\"").Append(N(y)).Append("\" class=\"ln").Append(c.sample[i] || c.sample[i - 1] ? " dash" : "").Append("\"/>");
                h.Append("<circle cx=\"").Append(N(x)).Append("\" cy=\"").Append(N(y)).Append("\" r=\"3.5\" class=\"pt").Append(c.sample[i] ? " smp" : "").Append("\"/>");
                px = x; py = y;
            }
            Text(h, 8f, 106f, "score %, oldest left; dashed = sample data", "mut");
        }

        static void CardChange(StringBuilder h, AarCard c)
        {
            var cc = c.session != null ? c.session.cardChange : null;
            if (cc == null || !cc.happened) { Empty(h, "card did not change"); return; }
            Rect(h, 10f, 20f, 90f, 44f, "trk"); Text(h, 55f, 48f, "Card " + cc.from, "bigv", "middle");
            h.Append("<line x1=\"104\" x2=\"206\" y1=\"42\" y2=\"42\" class=\"ln\"/><polygon points=\"206,36 218,42 206,48\" class=\"pt\"/>");
            Rect(h, 220f, 20f, 90f, 44f, Cls(c.status)); Text(h, 265f, 48f, "Card " + cc.to, "bigv", "middle");
            Text(h, 160f, 36f, "at " + N(cc.time) + " s", "mut", "middle");
            Text(h, 10f, 92f, cc.adaptationSeconds < 0f ? "no decision has used the new card yet" : "first right decision under the new card after " + N(cc.adaptationSeconds) + " s", "mut");
            Text(h, 10f, 106f, cc.staleErrors + " decision(s) followed the old card", "mut");
        }

        // Card CSS. The report page puts these tokens on :root (see HtmlReport.Css).
        public static string Css => ChartSvg.Css() + CardsCss;

        const string CardsCss = @"
.cardsec{margin-top:8px}
.cards{display:grid;grid-template-columns:repeat(auto-fill,minmax(310px,1fr));gap:12px}
.card{background:var(--panel);border:1px solid var(--line);border-top:4px solid var(--dim);border-radius:8px;padding:10px 12px;break-inside:avoid;page-break-inside:avoid;display:flex;flex-direction:column}
.card.sg{border-top-color:var(--good)}.card.sw{border-top-color:var(--warn)}.card.sb{border-top-color:var(--bad)}.card.sn{border-top-color:var(--line)}
.ct{display:flex;align-items:center;gap:6px;font-size:11px;letter-spacing:.04em;color:var(--dim);text-transform:uppercase}
.ct .dot{width:9px;height:9px;border-radius:2px;background:var(--dim);display:inline-block}
.card.sg .dot{background:var(--good)}.card.sw .dot{background:var(--warn)}.card.sb .dot{background:var(--bad)}
.ct .cn{flex:1;font-weight:700}.ct .cw{font-weight:700}
.card.sg .cw{color:var(--good)}.card.sw .cw{color:var(--warn)}.card.sb .cw{color:var(--bad)}
.hero{font-size:30px;font-weight:800;line-height:1.15;margin:4px 0 2px}
.verdict{margin:0 0 6px;font-size:13px;min-height:2.6em}
.chart{width:100%;height:auto;font-size:10px}
.chart text{fill:var(--fg)}.chart .mut{fill:var(--dim)}.chart .lab{fill:var(--fg)}.chart .val{fill:var(--fg);font-weight:700}.chart .num{fill:var(--fg);font-weight:700}.chart .bigv{font-size:20px;font-weight:800}
.chart .sg{fill:var(--good)}.chart .sw{fill:var(--warn)}.chart .sb{fill:var(--bad)}.chart .sn{fill:var(--dim);opacity:.6}
.chart .trk{fill:var(--line);opacity:.55}.chart .hg{fill:var(--good)}.chart .hb{fill:var(--bad)}.chart .hn{fill:var(--dim)}
.chart .axis{stroke:var(--dim);stroke-width:1}.chart .grid{stroke:var(--line);stroke-width:1}.chart .avg{stroke:var(--accent);stroke-width:1.5;stroke-dasharray:4 3}
.chart .ln{stroke:var(--accent);stroke-width:2;fill:none}.chart .ln.dash{stroke-dasharray:4 3;opacity:.6}.chart .pt{fill:var(--accent)}.chart .pt.smp{opacity:.45}
.chart .air{fill:var(--line)}.chart .inv{fill:var(--accent)}
.cf{margin-top:auto;padding-top:4px;color:var(--dim);font-size:11px}
.actions{display:flex;gap:8px;flex-wrap:wrap;margin:12px 0}
.actions button{background:var(--accent);color:#fff;border:0;border-radius:6px;padding:8px 14px;font:600 13px system-ui,Segoe UI,Arial,sans-serif;cursor:pointer}
.actions button:hover{filter:brightness(1.1)}
@media print{.cards{grid-template-columns:1fr 1fr;gap:8px}.card{padding:6px 8px}.hero{font-size:22px}.verdict{font-size:11px}.actions{display:none}}
";
    }
}
