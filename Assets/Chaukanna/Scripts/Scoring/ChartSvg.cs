using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Chaukanna.Scoring
{
    // Draws a laid-out chart (ChartLayout) as inline SVG for the HTML report: no scripts, no network, hover tooltips through <title>.
    public static class ChartSvg
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string N(float v) => v.ToString("0.##", Inv);
        static string Esc(string s) => HtmlReport.Esc(s);

        public static string ToneName(ChartTone t) => t.ToString().ToLowerInvariant();

        // The elements of one chart (no <svg> wrapper), so a card can embed it in its own svg.
        public static void Draw(StringBuilder h, ChartModel m, float w, float hh, float font, bool compact)
        {
            var prims = ChartLayout.Build(m, w, hh, font, compact);
            for (int pass = 0; pass < 2; pass++)   // hover areas last, so they sit on top and every bar and point shows its tooltip
            foreach (var p in prims)
            {
                if ((p.hit ? 1 : 0) != pass) continue;
                switch (p.kind)
                {
                    case PrimKind.Rect:
                        if (p.hit)
                        {
                            h.Append("<rect class=\"hit\" x=\"").Append(N(p.x)).Append("\" y=\"").Append(N(p.y)).Append("\" width=\"").Append(N(p.w)).Append("\" height=\"").Append(N(p.h)).Append("\"><title>").Append(Esc(p.tip)).Append("</title></rect>");
                        }
                        else
                        {
                            h.Append("<rect class=\"f-").Append(ToneName(p.tone)).Append("\" x=\"").Append(N(p.x)).Append("\" y=\"").Append(N(p.y)).Append("\" width=\"").Append(N(p.w)).Append("\" height=\"").Append(N(p.h)).Append("\" rx=\"1.5\"");
                            if (p.alpha < 0.999f) h.Append(" opacity=\"").Append(p.alpha.ToString("0.##", Inv)).Append("\"");
                            h.Append("/>");
                        }
                        break;
                    case PrimKind.Line:
                        h.Append("<line class=\"s-").Append(ToneName(p.tone)).Append("\" x1=\"").Append(N(p.x)).Append("\" y1=\"").Append(N(p.y)).Append("\" x2=\"").Append(N(p.x2)).Append("\" y2=\"").Append(N(p.y2)).Append("\" stroke-width=\"").Append(N(p.width)).Append("\"");
                        if (p.alpha < 0.999f) h.Append(" opacity=\"").Append(p.alpha.ToString("0.##", Inv)).Append("\"");
                        if (p.dashed) h.Append(" stroke-dasharray=\"5 4\"");
                        h.Append("/>");
                        break;
                    case PrimKind.Dot:
                        h.Append("<circle class=\"f-").Append(ToneName(p.tone)).Append(" dotring\" cx=\"").Append(N(p.x)).Append("\" cy=\"").Append(N(p.y)).Append("\" r=\"").Append(N(p.w * 0.5f)).Append("\"/>");
                        break;
                    case PrimKind.Text:
                        h.Append("<text x=\"").Append(N(p.x)).Append("\" y=\"").Append(N(p.y)).Append("\" font-size=\"").Append(N(p.size)).Append("\" text-anchor=\"").Append(p.anchor == 1 ? "middle" : p.anchor == 2 ? "end" : "start").Append("\" class=\"")
                            .Append(p.useTone ? "f-" + ToneName(p.tone) : p.dim ? "mut" : "fg").Append(p.bold ? " b" : "").Append("\">").Append(Esc(p.text)).Append("</text>");
                        break;
                }
            }
        }

        // One chart as its own <figure><svg>.
        public static void Figure(StringBuilder h, ChartModel m)
        {
            const float W = 560f, H = 300f;
            h.Append("<figure class=\"cfig\"><svg class=\"ckchart\" viewBox=\"0 0 ").Append(N(W)).Append(' ').Append(N(H)).Append("\" role=\"img\" aria-label=\"").Append(Esc(m.title)).Append("\" id=\"chart-").Append(m.id).Append("\">");
            Draw(h, m, W, H, 12f, false);
            h.Append("</svg></figure>");
        }

        // The Charts view of the report: the same six charts as the dashboard's Charts tab.
        public static void Section(StringBuilder h, SessionResult s, IList<Chaukanna.Trainer.TrendPoint> history)
        {
            h.Append("<section class=\"chartsec\"><h2>Charts</h2>");
            h.Append("<p class=\"small\">Trends use every saved session on this PC; the last three charts are this run. Hover a bar or point for the exact numbers. Green, amber and red mean the same on every chart.</p><div class=\"charts\">");
            foreach (var m in ChartModels.All(s, history)) Figure(h, m);
            h.Append("</div></section>");
        }

        // Tone colours as CSS classes, using the report's own tokens so light, dark and print all work.
        public static string Css()
        {
            var sb = new StringBuilder();
            sb.Append(":root{--s1:#1f9a9a;--s2:#b5559e;--s3:#7a5fd0;--s4:#d9779c;--s5:#8c6444}");
            sb.Append("@media (prefers-color-scheme:dark){:root{--s1:#4fd0d0;--s2:#d87bc4;--s3:#a08ef0;--s4:#f0a0bc;--s5:#c09870}}");
            string[] tones = { "accent", "good", "warn", "bad", "neutral", "s1", "s2", "s3", "s4", "s5" };
            foreach (string t in tones)
            {
                string v = t == "neutral" ? "var(--dim)" : "var(--" + t + ")";
                sb.Append(".f-").Append(t).Append("{fill:").Append(v).Append("}.s-").Append(t).Append("{stroke:").Append(v).Append(";fill:none}svg text.f-").Append(t).Append("{fill:").Append(v).Append("}");
            }
            sb.Append(".ckchart{width:100%;height:auto;font-family:system-ui,Segoe UI,Arial,sans-serif}svg text.fg{fill:var(--fg)}svg text.mut{fill:var(--dim)}svg text.b{font-weight:700}");
            sb.Append("svg .hit{fill:transparent}svg .hit:hover{fill:var(--fg);fill-opacity:.07}svg .dotring{stroke:var(--panel);stroke-width:1.5}");
            sb.Append(".charts{display:grid;grid-template-columns:repeat(auto-fill,minmax(420px,1fr));gap:12px}");
            sb.Append(".cfig{margin:0;background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:4px;break-inside:avoid;page-break-inside:avoid}");
            sb.Append(".viewtabs{display:flex;gap:6px;margin:12px 0 4px}.viewtabs label{padding:7px 16px;border:1px solid var(--line);border-radius:6px;cursor:pointer;font:600 13px system-ui,Segoe UI,Arial,sans-serif;background:var(--panel);color:var(--fg)}");
            sb.Append(".vt{position:absolute;opacity:0;pointer-events:none}");
            sb.Append("#v-cards:checked~.viewtabs label[for=v-cards],#v-charts:checked~.viewtabs label[for=v-charts]{background:var(--accent);color:#fff;border-color:var(--accent)}");
            sb.Append("#v-cards:checked~.viewbody .chartsec{display:none}#v-charts:checked~.viewbody .cardsec{display:none}");
            sb.Append("#v-cards:focus-visible~.viewtabs label[for=v-cards],#v-charts:focus-visible~.viewtabs label[for=v-charts]{outline:2px solid var(--accent);outline-offset:2px}");
            sb.Append("@media print{.viewtabs{display:none}.chartsec{display:block!important}.cardsec{display:block!important}.charts{grid-template-columns:1fr 1fr;gap:8px}}");
            return sb.ToString();
        }
    }
}
