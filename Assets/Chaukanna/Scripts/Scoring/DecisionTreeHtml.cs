using System.Globalization;
using System.Text;
using Chaukanna.Trainer;

namespace Chaukanna.Scoring
{
    // The decision tree in the HTML report: the same checkpoints and branches as the SCORE dashboard's tree (DecisionTreeData),
    // first for the whole run (how many contacts took each branch), then one foldable entry per contact with its own path and the
    // details behind it (seen after, call, action, card in force, outcome, decision quality, points). Plain HTML <details>, no script.
    public static class DecisionTreeHtml
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Esc(string s) => HtmlReport.Esc(s ?? "");
        static string Tone(int t) => t == 1 ? "tg" : t == 2 ? "tw" : t == 3 ? "tb" : "tn";
        static string Sec(float v) => v < 0f ? "-" : v.ToString("F1", Inv) + " s";

        public static void Append(StringBuilder h, SessionResult s)
        {
            if (s == null || s.contacts == null) return;
            h.Append("<section class=\"dtree\"><h2>Decision tree</h2>");
            if (s.contacts.Count == 0) { h.Append("<p class=\"small\">No contacts in this run.</p></section>"); return; }

            // Whole run: every checkpoint with the branches and how many contacts took each.
            var stages = DecisionTreeData.Build(s);
            int total = s.contacts.Count;
            h.Append("<p class=\"small\">").Append(total).Append(" contacts. Each column is a checkpoint; the bar is the share of all contacts that took that branch.</p><div class=\"dt-run\">");
            foreach (var st in stages)
            {
                h.Append("<div class=\"dt-col\"><div class=\"dt-h\">").Append(Esc(st.title)).Append(" <span>").Append(st.reached).Append("</span></div>");
                foreach (var n in st.nodes)
                {
                    if (n.count == 0) continue;
                    h.Append("<div class=\"dt-n ").Append(Tone(n.tone)).Append("\"><span class=\"dt-l\">").Append(Esc(n.label)).Append("</span><span class=\"dt-c\">")
                        .Append(n.count).Append(" (").Append(n.percent.ToString("F0", Inv)).Append(" %)</span><i style=\"width:")
                        .Append(n.percent.ToString("F0", Inv)).Append("%\"></i></div>");
                }
                h.Append("</div>");
            }
            h.Append("</div>");

            // One foldable entry per contact.
            h.Append("<h3>Every contact's path</h3><p class=\"small\">Click a contact to unfold its decision tree.</p>");
            foreach (var c in s.contacts)
            {
                var path = DecisionTreeData.Build(s, c);
                string grid = string.IsNullOrEmpty(c.grid) ? "No decision" : c.grid;
                h.Append("<details class=\"dt-c1\"><summary><b>#").Append(c.id).Append("</b> ").Append(Esc(c.truthClass)).Append(c.deceptive ? " (blend-in)" : "")
                    .Append(" &middot; call ").Append(Esc(string.IsNullOrEmpty(c.call) ? "none" : c.call))
                    .Append(" &middot; action ").Append(Esc(string.IsNullOrEmpty(c.action) ? "none" : c.action))
                    .Append(" &middot; ").Append(Esc(grid))
                    .Append(c.processScore >= 0 ? " &middot; decision quality " + c.processScore + " / 100" : "")
                    .Append(c.scored ? "" : " &middot; not scored").Append("</summary>");

                // The path: the branch taken at each checkpoint.
                h.Append("<ol class=\"dt-path\">");
                foreach (var st in path)
                {
                    TreeNode taken = null;
                    foreach (var n in st.nodes) if (n.taken) { taken = n; break; }
                    h.Append("<li class=\"").Append(taken != null ? Tone(taken.tone) : "tn").Append("\"><span class=\"dt-h\">").Append(Esc(st.title)).Append("</span> ")
                        .Append(taken != null ? Esc(taken.label) : "not reached").Append("</li>");
                }
                h.Append("</ol>");

                // The details behind it.
                h.Append("<table class=\"dt-facts\">");
                Row(h, "Truth", Esc(c.truthClass) + (c.deceptive ? " (hostile flying an own-drone pattern)" : ""));
                if (!c.scored) Row(h, "Not scored", Esc(c.notScoredReason));
                Row(h, "Seen", c.detected ? "after " + Sec(c.detectionSeconds) : "never");
                if (c.n1aSeconds >= 0f || c.n1bSeconds >= 0f) Row(h, "Search / perception", Sec(c.n1aSeconds) + " to come into view, " + Sec(c.n1bSeconds) + " to lock");
                Row(h, "Call", Esc(string.IsNullOrEmpty(c.call) ? "none" : c.call) + (c.callChanges > 0 ? " (first " + Esc(c.firstCall) + ", changed " + c.callChanges + "x)" : ""));
                if (!string.IsNullOrEmpty(c.aiHint)) Row(h, "AI cue", Esc(c.aiHint) + " " + c.aiConfidence + " %" + (c.aiHintWrong ? " (wrong)" : "") + (c.followedAi ? ", followed" : ", not followed"));
                Row(h, "Action", Esc(string.IsNullOrEmpty(c.action) ? "none" : c.action) + (c.group ? " (group decision)" : "") + (c.actionChanges > 0 ? ", changed " + c.actionChanges + "x" : ""));
                Row(h, "Call to action", Sec(c.decisionLatency) + (c.holds > 0 ? ", " + c.holds + " hold(s), " + Sec(c.holdSeconds) + " on hold" : ""));
                Row(h, "Card in force", Esc(string.IsNullOrEmpty(c.roeAtAction) ? "-" : c.roeAtAction) + (c.staleCard ? " (followed the old card)" : ""));
                if (c.clockOpen >= 0f) Row(h, "Clock", "opened at " + Sec(c.clockOpen) + ", deadline " + Sec(c.deadline) + (c.clockMiss ? ", MISSED" : "") + (c.correctRejection ? ", correctly left alone" : ""));
                Row(h, "Outcome", Esc(c.outcome) + (c.unverified ? " (unverified engagement)" : ""));
                Row(h, "Decision vs result", Esc(grid) + (string.IsNullOrEmpty(c.severity) ? "" : ", severity " + Esc(c.severity)));
                Row(h, "Decision quality", c.processScore >= 0 ? c.processScore + " / 100" : "n/a (no decision needed or made)");
                if (c.nodes != null)
                    foreach (var n in c.nodes)
                        Row(h, Esc(n.label) + (n.passed ? " passed" : " failed"), (n.points >= 0 ? "+" : "") + n.points + " points" + (string.IsNullOrEmpty(n.reason) ? "" : ": " + Esc(n.reason)));
                Row(h, "Points", c.total.ToString());
                h.Append("</table></details>");
            }
            h.Append("</section>");
        }

        static void Row(StringBuilder h, string k, string v) => h.Append("<tr><th>").Append(k).Append("</th><td>").Append(v).Append("</td></tr>");

        public const string Css = @"
.dtree{margin-top:16px}
.dt-run{display:grid;grid-template-columns:repeat(auto-fit,minmax(170px,1fr));gap:10px;margin:8px 0 14px}
.dt-col{background:var(--panel);border:1px solid var(--line);border-radius:8px;padding:8px}
.dt-h{font-size:11px;font-weight:700;letter-spacing:.04em;color:var(--dim);text-transform:uppercase}
.dt-col>.dt-h{margin-bottom:6px}.dt-col>.dt-h span{float:right}
.dt-n{position:relative;display:flex;justify-content:space-between;gap:6px;font-size:12px;padding:3px 6px;margin:3px 0;border-left:4px solid var(--dim);border-radius:3px;overflow:hidden}
.dt-n i{position:absolute;left:0;top:0;bottom:0;background:var(--line);opacity:.45;z-index:0}
.dt-n span{position:relative;z-index:1}.dt-c{font-weight:700;white-space:nowrap}
.tg{border-left-color:var(--good)!important}.tw{border-left-color:var(--warn)!important}.tb{border-left-color:var(--bad)!important}
details.dt-c1{background:var(--panel);border:1px solid var(--line);border-radius:6px;margin:6px 0;padding:6px 10px}
details.dt-c1 summary{cursor:pointer;font-size:13px}
.dt-path{display:flex;flex-wrap:wrap;gap:6px;list-style:none;padding:0;margin:8px 0}
.dt-path li{border:1px solid var(--line);border-left:4px solid var(--dim);border-radius:4px;padding:4px 8px;font-size:12px}
.dt-path li .dt-h{display:block}
.dt-facts{border-collapse:collapse;font-size:12px;margin:4px 0 6px}
.dt-facts th{text-align:left;color:var(--dim);font-weight:600;padding:2px 12px 2px 0;vertical-align:top;white-space:nowrap}
.dt-facts td{padding:2px 0}
@media print{details.dt-c1{break-inside:avoid}}
";
    }
}
