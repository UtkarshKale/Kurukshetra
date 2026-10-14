using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Scoring
{
    // The AAR as one static HTML file (doc §1.11): opens in any browser on the CO's PC with nothing installed and no network.
    // Everything is inline: styles, SVG charts, the replay data and a small plain-JavaScript replay player. Prints on A4.
    // Debrief text is templated (no generated prose), framed by the four TC 25-20 questions.
    public static class HtmlReport
    {
        public const string Disclaimer = "Training use only. Not for ACRs or selection.";
        public static string ReportsFolder => Path.Combine(Application.persistentDataPath, "reports");

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Writes the report next to the session files and returns its path (null on failure).
        public static string Save(SessionResult s, IList<ContactRecord> records, string player)
        {
            try
            {
                Directory.CreateDirectory(ReportsFolder);
                string path = Path.Combine(ReportsFolder, $"aar_{DateTime.Now:yyyyMMdd_HHmmss}_seed{s.seed}.html");
                File.WriteAllText(path, Build(s, records, player), new UTF8Encoding(false));
                return path;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Report] Could not write the HTML report: " + e.Message);
                return null;
            }
        }

        public static string Build(SessionResult s, IList<ContactRecord> records, string player)
        {
            var h = new StringBuilder(64 * 1024);
            h.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            h.Append("<title>Chaukanna AAR ").Append(Esc(s.timestamp)).Append("</title><style>").Append(Css).Append("</style></head><body>");

            // Header.
            h.Append("<header><div class=\"disclaimer\">").Append(Disclaimer).Append("</div>");
            h.Append("<h1>Chaukanna after-action review</h1>");
            h.Append("<p class=\"meta\">").Append(Esc(player)).Append(" &middot; ").Append(Esc(s.timestamp ?? "")).Append(" &middot; seed ").Append(s.seed)
                .Append(" &middot; ").Append(F0(s.duration)).Append(" s &middot; ").Append(Esc(s.timeOfDay)).Append(", ").Append(Esc(s.visibility))
                .Append(" &middot; difficulty ").Append(Esc(string.IsNullOrEmpty(s.difficulty) ? "none" : s.difficulty))
                .Append(" &middot; display ").Append(Esc(s.displayTier)).Append("</p></header>");

            // Numbers.
            h.Append("<section class=\"tiles\">");
            Tile(h, $"{s.totalPoints} / {s.maxPoints}", "Total score", Grade(s.ScorePercent));
            Tile(h, s.averageDetectionSeconds < 0f ? "-" : F1(s.averageDetectionSeconds) + " s", "Avg detection time", "");
            Tile(h, F0(s.classificationAccuracy) + " %", "Classification accuracy", Grade(s.classificationAccuracy));
            Tile(h, F0(s.engagementDecisionScore) + " %", "Engagement decisions", Grade(s.engagementDecisionScore));
            Tile(h, s.processScore < 0f ? "-" : F0(s.processScore) + " / 100", "Process score (on belief)", s.processScore < 0f ? "" : Grade(s.processScore));
            h.Append("</section>");

            Debrief(h, s);

            // Decision quality.
            h.Append("<section><h2>Decision quality</h2><div class=\"grid2\">");
            h.Append($"<div class=\"g good\"><b>{s.gridSoundSuccessful}</b> sound call, good result</div><div class=\"g warn\"><b>{s.gridSoundUnlucky}</b> sound call, bad luck</div>");
            h.Append($"<div class=\"g warn\"><b>{s.gridLucky}</b> unsound call, lucky</div><div class=\"g bad\"><b>{s.gridUnsound}</b> unsound call</div></div>");
            h.Append($"<p>Clock misses <b>{s.clockMisses}</b> &middot; correct rejections <b>{s.correctRejections}</b> &middot; unverified engagements <b>{s.unverifiedEngagements}</b> &middot; search failures <b>{s.searchFailures}</b> &middot; perception failures <b>{s.perceptionFailures}</b> &middot; missed {s.missed} &middot; false engagements {s.falseEngagements}</p>");
            Sdt(h, "Report: is there a drone?", s.reportSdt);
            Sdt(h, "Engage: is it hostile?", s.engageSdt);
            if (s.cardChange != null && s.cardChange.happened) h.Append("<p><b>Card change:</b> ").Append(Esc(s.cardChange.summary)).Append("</p>");
            if (s.swarm != null && s.swarm.happened) h.Append("<p><b>Swarm triage:</b> ").Append(Esc(s.swarm.summary)).Append("</p>");
            h.Append("</section>");

            Timeline(h, s, records);
            Replay(h, s, records);
            Contacts(h, s);
            Confusion(h, s);

            h.Append("<footer>").Append(Disclaimer).Append(" Cards A / B / C, release points, deadlines and bands are placeholders, not Indian doctrine; an officer must set them. ")
                .Append("\"In view\" means on the player's screen and passing the detection test, never \"saw\". The sound call is an annotated replay of the logged run, not a re-simulation.</footer>");
            h.Append("<script>").Append(ReplayScript).Append("</script></body></html>");
            return h.ToString();
        }

        // The four TC 25-20 questions, answered from the numbers with fixed sentences.
        static void Debrief(StringBuilder h, SessionResult s)
        {
            int hostiles = 0, decided = 0;
            var wrongPairs = new Dictionary<string, int>();
            foreach (var c in s.contacts)
            {
                if (!c.scored) continue;
                if (c.truthClass.StartsWith("Hostile")) hostiles++;
                if (c.action != "none") decided++;
                if (c.detected && c.call != "none" && c.classBranch != "Right" && c.classBranch != "Caught deception" && c.classBranch != "Second guess")
                {
                    string key = $"{Pretty(c.truthClass)} called {c.call}";
                    wrongPairs.TryGetValue(key, out int n);
                    wrongPairs[key] = n + 1;
                }
            }
            string worstPair = null; int worstN = 0;
            foreach (var kv in wrongPairs) if (kv.Value > worstN) { worstN = kv.Value; worstPair = kv.Key; }

            var improve = new List<string>();
            if (s.searchFailures > 0) improve.Add($"Sector search: {s.searchFailures} contact(s) could have been seen but were never brought into view. Sweep the whole sector in a set pattern; use the binoculars on the horizon.");
            if (s.perceptionFailures > 0) improve.Add($"Mark what you see: {s.perceptionFailures} contact(s) were on screen but never marked before their deadline.");
            if (worstPair != null) improve.Add($"Recognition: {worstPair} ({worstN}x). Practise this pair in the Range drill and look through the binoculars before calling.");
            if (s.clockMisses > 0) improve.Add($"Decide before the release point: {s.clockMisses} hostile(s) were held or acted on too late.");
            if (s.unverifiedEngagements > 0) improve.Add($"Look before you fire: {s.unverifiedEngagements} engagement(s) followed the AI cue without a look through the binoculars.");
            if (s.falseEngagements > 0) improve.Add($"Hold fire on non-hostiles: {s.falseEngagements} false engagement(s).");
            if (s.cardChange != null && s.cardChange.happened && s.cardChange.staleErrors > 0) improve.Add($"Re-read the card when it changes: {s.cardChange.staleErrors} decision(s) still followed the old card.");
            bool clean = improve.Count == 0;
            if (clean) improve.Add("Next step: no weak area stood out in this run. Move up one difficulty level.");

            h.Append("<section><h2>Debrief</h2><ol class=\"debrief\">");
            h.Append("<li><b>What was planned?</b> Watch the sector, report every drone, and act on hostiles by the card in force")
                .Append(s.cardChange != null && s.cardChange.happened ? $" (the card changed from {Esc(s.cardChange.from)} to {Esc(s.cardChange.to)} at {F0(s.cardChange.time)} s)." : ".").Append("</li>");
            h.Append($"<li><b>What happened?</b> {s.scoredContacts} contacts were scored ({hostiles} hostile). {s.detected} were brought into view, {s.missed} were not; {decided} got a decision. Score {s.totalPoints} of {s.maxPoints}.</li>");
            h.Append("<li><b>Why?</b> ").Append(clean ? "Nothing stood out: detection, recognition and decisions were all sound." : Esc(improve[0].Split(':')[0]) + " was the weakest link in this run.").Append("</li>");
            h.Append("<li><b>How to improve?</b><ul>");
            foreach (var line in improve) h.Append("<li>").Append(Esc(line)).Append("</li>");
            h.Append("</ul></li></ol></section>");
        }

        static void Sdt(StringBuilder h, string name, SdtResult r)
        {
            h.Append("<p><b>").Append(Esc(name)).Append("</b> ").Append(r.hits).Append('/').Append(r.signals).Append(" hits, ").Append(r.falseAlarms).Append('/').Append(r.noise).Append(" false alarms. ");
            if (!r.gated) { h.Append("d&prime; and c: <i>insufficient data</i> (needs 20 of each).</p>"); return; }
            float t = Mathf.InverseLerp(-1.5f, 1.5f, r.c) * 100f;
            h.Append($"d&prime; {F2(r.dPrime)}, c {r.c.ToString("+0.00;-0.00;0.00", Inv)}</p><div class=\"slider\"><span style=\"left:{F1(t)}%\"></span></div><div class=\"ends\"><span>trigger-happy</span><span>hesitant</span></div>");
        }

        // In view vs ground truth per contact: grey = in the air, green = on the player's screen, ticks = call, action, deadline.
        static void Timeline(StringBuilder h, SessionResult s, IList<ContactRecord> records)
        {
            float dur = Mathf.Max(1f, s.duration);
            int rows = s.contacts.Count;
            const float rowH = 14f, left = 70f, width = 900f;
            float height = rows * rowH + 24f;
            h.Append("<section><h2>Timeline: in view vs in the air</h2><svg class=\"tl\" viewBox=\"0 0 ").Append(F0(left + width + 10f)).Append(' ').Append(F0(height)).Append("\" role=\"img\" aria-label=\"timeline\">");
            for (int i = 0; i < rows; i++)
            {
                var c = s.contacts[i];
                float y = i * rowH;
                float X(float t) => left + Mathf.Clamp01(t / dur) * width;
                h.Append($"<text x=\"0\" y=\"{F1(y + 10f)}\">#{c.id:00} {Esc(Short(c.truthClass))}</text>");
                float end = c.despawnTime >= 0f ? c.despawnTime : dur;
                h.Append($"<rect class=\"air\" x=\"{F1(X(c.spawnTime))}\" y=\"{F1(y + 3f)}\" width=\"{F1(Mathf.Max(1f, X(end) - X(c.spawnTime)))}\" height=\"8\"/>");
                var r = Find(records, c.id);
                if (r != null)
                {
                    float start = -1f, last = -1f;
                    foreach (var p in r.track)
                    {
                        if (p.inView) { if (start < 0f) start = p.t; last = p.t; }
                        else if (start >= 0f) { Seg(h, X(start), X(last + 0.5f), y); start = -1f; }
                    }
                    if (start >= 0f) Seg(h, X(start), X(last + 0.5f), y);
                }
                if (c.callTime >= 0f) h.Append($"<circle class=\"call\" cx=\"{F1(X(c.callTime))}\" cy=\"{F1(y + 7f)}\" r=\"3\"/>");
                if (c.actionTime >= 0f) h.Append($"<rect class=\"{(c.nodes.Count > 2 && c.nodes[2].passed ? "act" : "actbad")}\" x=\"{F1(X(c.actionTime) - 3f)}\" y=\"{F1(y + 2f)}\" width=\"6\" height=\"10\"/>");
                if (c.deadline >= 0f) h.Append($"<rect class=\"dl\" x=\"{F1(X(c.deadline) - 1f)}\" y=\"{F1(y + 1f)}\" width=\"2\" height=\"12\"/>");
            }
            h.Append($"<text x=\"{F0(left)}\" y=\"{F1(height - 4f)}\">0 s</text><text x=\"{F0(left + width - 40f)}\" y=\"{F1(height - 4f)}\">{F0(dur)} s</text></svg>");
            h.Append("<p class=\"legend\"><i class=\"k air\"></i> in the air <i class=\"k inview\"></i> in view <i class=\"k call\"></i> call <i class=\"k act\"></i> right action <i class=\"k actbad\"></i> wrong action <i class=\"k dl\"></i> decision deadline</p></section>");
        }

        static void Seg(StringBuilder h, float x0, float x1, float y)
        {
            h.Append($"<rect class=\"inview\" x=\"{F1(x0)}\" y=\"{F1(y + 3f)}\" width=\"{F1(Mathf.Max(1f, x1 - x0))}\" height=\"8\"/>");
        }

        // The logged run as data for the replay player: positions relative to the post, turned so up is the sector centre.
        static void Replay(StringBuilder h, SessionResult s, IList<ContactRecord> records)
        {
            var j = new StringBuilder();
            j.Append("{\"duration\":").Append(F1(s.duration)).Append(",\"radius\":").Append(F0(Mathf.Max(100f, RunLog.spawnRadius))).Append(",\"contacts\":[");
            bool first = true;
            Quaternion turn = Quaternion.Euler(0f, -RunLog.sectorYaw, 0f);
            foreach (var c in s.contacts)
            {
                var r = Find(records, c.id);
                if (r == null || r.track.Count == 0) continue;
                if (!first) j.Append(',');
                first = false;
                j.Append("{\"id\":").Append(c.id).Append(",\"cls\":\"").Append(Short(c.truthClass)).Append("\",\"call\":\"").Append(JsonEsc(c.call))
                    .Append("\",\"action\":\"").Append(JsonEsc(c.action)).Append("\",\"at\":").Append(F1(c.actionTime)).Append(",\"grid\":\"").Append(JsonEsc(c.grid))
                    .Append("\",\"your\":\"").Append(JsonEsc(c.yourCall)).Append("\",\"sound\":\"").Append(JsonEsc(c.soundCall)).Append("\",\"cons\":\"").Append(JsonEsc(c.consequence)).Append("\",\"p\":[");
                for (int i = 0; i < r.track.Count; i++)
                {
                    var p = r.track[i];
                    Vector3 d = turn * (new Vector3(p.x, 0f, p.z) - RunLog.post);
                    if (i > 0) j.Append(',');
                    j.Append('[').Append(F1(p.t)).Append(',').Append(F0(d.x)).Append(',').Append(F0(d.z)).Append(',').Append(p.inView ? 1 : 0).Append(']');
                }
                j.Append("]}");
            }
            j.Append("],\"view\":[");
            for (int i = 0; i < RunLog.view.Count; i++)
            {
                var v = RunLog.view[i];
                Vector3 d = turn * (new Vector3(v.x, 0f, v.z) - RunLog.post);
                if (i > 0) j.Append(',');
                j.Append('[').Append(F1(v.t)).Append(',').Append(F0(Mathf.DeltaAngle(0f, v.yaw - RunLog.sectorYaw))).Append(',').Append(F0(v.fov)).Append(',').Append(F0(d.x)).Append(',').Append(F0(d.z)).Append(']');
            }
            j.Append("]}");

            h.Append("<section class=\"noprint\"><h2>Replay</h2><p class=\"small\">Top-down, up = the centre of the sector, the post in the middle. Green arc = where the soldier was looking. A ring = on the soldier's screen. Click a contact to see your call against the sound call.</p>");
            h.Append("<div class=\"replay\"><canvas id=\"rp\" width=\"560\" height=\"560\"></canvas><div class=\"side\"><div class=\"ctl\"><button id=\"play\">Play</button> <input id=\"t\" type=\"range\" min=\"0\" max=\"1000\" value=\"0\"> <span id=\"tt\">0 s</span></div><div id=\"info\">Click a contact.</div></div></div>");
            h.Append("<script id=\"replay-data\" type=\"application/json\">").Append(j.ToString().Replace("</", "<\\/")).Append("</script></section>");
        }

        static void Contacts(StringBuilder h, SessionResult s)
        {
            h.Append("<section><h2>Every contact</h2><table class=\"list\"><tr><th>#</th><th>Truth</th><th>Your call</th><th>Sound call</th><th>Consequence</th><th>Clock</th><th>N1a / N1b</th><th>Process, result</th><th>Points</th></tr>");
            foreach (var c in s.contacts)
            {
                string cls = !c.scored ? "ns" : c.matchedSound ? "ok" : c.clockMiss || c.grid == "Unsound" ? "badrow" : "";
                h.Append("<tr class=\"").Append(cls).Append("\"><td>").Append(c.id).Append("</td><td>").Append(Esc(Pretty(c.truthClass))).Append(c.deceptive ? " (blend-in)" : "").Append("</td>");
                if (!c.scored)
                {
                    h.Append("<td colspan=\"7\">not scored: ").Append(Esc(c.notScoredReason)).Append("</td><td>-</td></tr>");
                    continue;
                }
                h.Append("<td>").Append(Esc(c.yourCall)).Append("</td><td>").Append(Esc(c.soundCall)).Append("</td><td>").Append(Esc(c.consequence)).Append("</td>");
                h.Append("<td>").Append(c.deadline >= 0f ? $"{F0(c.clockOpen)}-{F0(c.deadline)} s" : "-").Append(c.clockMiss ? " <b>MISS</b>" : c.correctRejection ? " rejected" : "").Append("</td>");
                h.Append("<td>").Append(c.n1aSeconds >= 0f ? F1(c.n1aSeconds) + " s" : "-").Append(" / ").Append(c.n1bSeconds >= 0f ? F1(c.n1bSeconds) + " s " + Esc(c.perceptionBand) : Esc(c.missKind.Split(':')[0])).Append("</td>");
                h.Append("<td>").Append(Esc(string.IsNullOrEmpty(c.processSeverity) ? "-" : c.processSeverity + ", " + c.outcome)).Append(c.unverified ? " UNVERIFIED" : "").Append("</td>");
                h.Append("<td>").Append(c.total).Append("</td></tr>");
                h.Append("<tr class=\"path\"><td></td><td colspan=\"8\">").Append(Esc(c.path)).Append("</td></tr>");
            }
            h.Append("</table></section>");
        }

        static void Confusion(StringBuilder h, SessionResult s)
        {
            if (s.confusionRows == null || s.confusionColumns == null) return;
            int cols = s.confusionColumns.Length;
            h.Append("<section><h2>Confusion (rows = truth, columns = call)</h2><table class=\"conf\"><tr><th></th>");
            foreach (var c in s.confusionColumns) h.Append("<th>").Append(Esc(Pretty(c))).Append("</th>");
            h.Append("</tr>");
            for (int row = 0; row < s.confusionRows.Length; row++)
            {
                h.Append("<tr><th>").Append(Esc(Pretty(s.confusionRows[row]))).Append("</th>");
                for (int col = 0; col < cols; col++)
                {
                    int n = s.confusion[row * cols + col];
                    h.Append("<td class=\"").Append(n == 0 ? "z" : col == row ? "d" : "o").Append("\">").Append(n).Append("</td>");
                }
                h.Append("</tr>");
            }
            h.Append("</table></section>");
        }

        static void Tile(StringBuilder h, string value, string caption, string grade)
        {
            h.Append("<div class=\"tile ").Append(grade).Append("\"><b>").Append(Esc(value)).Append("</b><span>").Append(Esc(caption)).Append("</span></div>");
        }

        static string Grade(float percent) => percent >= 70f ? "good" : percent >= 45f ? "warn" : "bad";

        static ContactRecord Find(IList<ContactRecord> records, int id)
        {
            if (records == null) return null;
            foreach (var r in records) if (r.id == id) return r;
            return null;
        }

        static string Short(string truth)
        {
            switch (truth)
            {
                case "HostileQuad": return "H-quad";
                case "HostileFixedWing": return "H-wing";
                case "FriendlyISR": return "Own";
                case "CivilianQuad": return "Civil";
                case "Bird": return "Bird";
                default: return truth;
            }
        }

        // "HostileFixedWing" -> "Hostile fixed wing".
        static string Pretty(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char ch = name[i];
                if (i > 0 && char.IsUpper(ch) && !char.IsUpper(name[i - 1])) sb.Append(' ').Append(char.ToLowerInvariant(ch));
                else if (i > 0 && char.IsUpper(ch)) sb.Append(char.ToLowerInvariant(ch));
                else sb.Append(ch);
            }
            return sb.ToString().Replace(" i s r", " ISR").Replace("isr", "ISR");
        }

        public static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        static string JsonEsc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char ch in s)
            {
                if (ch == '"' || ch == '\\') sb.Append('\\').Append(ch);
                else if (ch < 32) sb.Append(' ');
                else sb.Append(ch);
            }
            return sb.ToString();
        }

        static string F0(float v) => v.ToString("F0", Inv);
        static string F1(float v) => v.ToString("F1", Inv);
        static string F2(float v) => v.ToString("F2", Inv);

        const string Css = @"
:root{--bg:#fff;--fg:#14181f;--dim:#5b6573;--line:#d5dae1;--good:#1f8a46;--warn:#b7791f;--bad:#c53030;--accent:#1a5fb4;--panel:#f3f5f8}
@media (prefers-color-scheme:dark){:root{--bg:#12161c;--fg:#e8ecf1;--dim:#9aa5b1;--line:#2c343f;--panel:#1b222b;--accent:#6aa9ff}}
body{margin:0 auto;max-width:1100px;padding:16px;background:var(--bg);color:var(--fg);font:14px/1.45 system-ui,Segoe UI,Arial,sans-serif}
h1{font-size:22px;margin:4px 0}h2{font-size:16px;margin:22px 0 8px;border-bottom:1px solid var(--line);padding-bottom:4px}
.disclaimer{background:#7a1f1f;color:#fff;font-weight:700;padding:4px 10px;display:inline-block;border-radius:3px}
.meta,.small,.legend,footer{color:var(--dim);font-size:12px}
.tiles{display:grid;grid-template-columns:repeat(auto-fit,minmax(160px,1fr));gap:8px;margin-top:12px}
.tile{background:var(--panel);padding:10px;border-left:4px solid var(--accent)}.tile b{display:block;font-size:20px}.tile span{color:var(--dim);font-size:12px}
.tile.good{border-color:var(--good)}.tile.warn{border-color:var(--warn)}.tile.bad{border-color:var(--bad)}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:4px;max-width:520px}.g{background:var(--panel);padding:6px 10px;border-left:4px solid}.g.good{border-color:var(--good)}.g.warn{border-color:var(--warn)}.g.bad{border-color:var(--bad)}
.slider{position:relative;height:8px;background:var(--panel);max-width:420px;border:1px solid var(--line)}.slider span{position:absolute;top:-4px;width:8px;height:16px;margin-left:-4px;background:var(--accent)}
.ends{display:flex;justify-content:space-between;max-width:420px;color:var(--dim);font-size:11px}
.debrief li{margin:4px 0}
svg.tl{width:100%;height:auto;font-size:9px;fill:var(--fg)}svg .air{fill:var(--line)}svg .inview{fill:var(--good)}svg .call{fill:var(--accent)}svg .act{fill:var(--good);stroke:var(--fg);stroke-width:.5}svg .actbad{fill:var(--bad)}svg .dl{fill:var(--bad)}
.k{display:inline-block;width:10px;height:10px;margin:0 3px 0 10px;vertical-align:middle}.k.air{background:var(--line)}.k.inview{background:var(--good)}.k.call{background:var(--accent);border-radius:5px}.k.act{background:var(--good)}.k.actbad{background:var(--bad)}.k.dl{background:var(--bad);width:3px}
table{border-collapse:collapse;width:100%;font-size:12px}th,td{border-bottom:1px solid var(--line);padding:4px 6px;text-align:left;vertical-align:top}
tr.ok td:first-child{border-left:4px solid var(--good)}tr.badrow td:first-child{border-left:4px solid var(--bad)}tr.ns{color:var(--dim)}tr.path td{color:var(--dim);font-size:11px;border-bottom:2px solid var(--line)}
table.conf{width:auto}table.conf td{text-align:center;min-width:44px}td.d{background:rgba(31,138,70,.25)}td.o{background:rgba(197,48,48,.25)}td.z{color:var(--dim)}
.replay{display:flex;gap:16px;flex-wrap:wrap}.replay canvas{background:#0e141b;max-width:100%;height:auto;border:1px solid var(--line);cursor:pointer}.side{flex:1;min-width:240px}.ctl{display:flex;gap:6px;align-items:center}.ctl input{flex:1}#info{margin-top:10px;background:var(--panel);padding:8px;min-height:120px}
footer{margin-top:24px;border-top:1px solid var(--line);padding-top:8px}
@media print{.noprint{display:none}body{max-width:none;font-size:10px;padding:0}h2{margin-top:10px}tr.path{display:none}@page{size:A4;margin:10mm}}
";

        // Plain JavaScript replay: no libraries, works from a file:// URL with no network.
        const string ReplayScript = @"
(function(){var el=document.getElementById('replay-data');if(!el)return;var D=JSON.parse(el.textContent);var cv=document.getElementById('rp');var g=cv.getContext('2d');
var slider=document.getElementById('t'),label=document.getElementById('tt'),btn=document.getElementById('play'),info=document.getElementById('info');
var col={'H-quad':'#ff4a3d','H-wing':'#ff8a3d','Own':'#4d8dff','Civil':'#ffe14d','Bird':'#b3b3b3'};var R=D.radius*1.1,S=cv.width/2/R,t=D.contacts.reduce(function(m,k){return Math.min(m,k.p.length?k.p[0][0]:m);},D.duration),playing=false,sel=null,last=0;
function at(p,t){if(!p.length||t<p[0][0]||t>p[p.length-1][0]+0.6)return null;var lo=0,hi=p.length-1;while(hi-lo>1){var m=(lo+hi)>>1;if(p[m][0]<=t)lo=m;else hi=m;}var a=p[lo],b=p[hi],k=b[0]>a[0]?Math.min(1,Math.max(0,(t-a[0])/(b[0]-a[0]))):0;return[a[1]+(b[1]-a[1])*k,a[2]+(b[2]-a[2])*k,a[3]];}
function px(x,z){return[cv.width/2+x*S,cv.height/2-z*S];}
function draw(){g.clearRect(0,0,cv.width,cv.height);g.strokeStyle='rgba(255,255,255,.25)';g.beginPath();g.arc(cv.width/2,cv.height/2,D.radius*S,0,7);g.stroke();
var v=null;for(var i=0;i<D.view.length;i++){if(D.view[i][0]<=t)v=D.view[i];else break;}
if(v){var c=px(v[3],v[4]);g.fillStyle='rgba(80,255,120,.12)';g.beginPath();g.moveTo(c[0],c[1]);var a0=(v[1]-v[2]/2-90)*Math.PI/180,a1=(v[1]+v[2]/2-90)*Math.PI/180;g.arc(c[0],c[1],D.radius*S,a0,a1);g.closePath();g.fill();}
g.fillStyle='#fff';g.fillRect(cv.width/2-4,cv.height/2-4,8,8);
D.contacts.forEach(function(k){var q=at(k.p,t);if(!q)return;var c=px(q[0],q[1]);g.fillStyle=col[k.cls]||'#fff';g.beginPath();g.arc(c[0],c[1],4,0,7);g.fill();
if(q[2]){g.strokeStyle='#5cff7a';g.lineWidth=1.5;g.beginPath();g.arc(c[0],c[1],8,0,7);g.stroke();}
if(k.at>=0&&t>=k.at){g.fillStyle='#fff';g.font='10px sans-serif';g.fillText(k.action,c[0]+9,c[1]-6);}
if(sel===k){g.strokeStyle='#fff';g.lineWidth=2;g.strokeRect(c[0]-11,c[1]-11,22,22);}});
label.textContent=t.toFixed(0)+' s';}
function show(k){sel=k;info.innerHTML=k?('<b>#'+k.id+' '+k.cls+'</b><br><b>Your call:</b> '+esc(k.your)+'<br><b>Sound call:</b> '+esc(k.sound)+'<br><b>Consequence:</b> '+esc(k.cons)+'<br><span style=""opacity:.7"">'+esc(k.grid)+'</span>'):'Click a contact.';draw();}
function esc(s){return String(s).replace(/[&<>]/g,function(c){return{'&':'&amp;','<':'&lt;','>':'&gt;'}[c];});}
slider.oninput=function(){t=slider.value/1000*D.duration;draw();};
btn.onclick=function(){playing=!playing;btn.textContent=playing?'Pause':'Play';last=performance.now();if(playing)requestAnimationFrame(step);};
function step(now){if(!playing)return;t+=(now-last)/1000*10;last=now;if(t>=D.duration){t=D.duration;playing=false;btn.textContent='Play';}slider.value=t/D.duration*1000;draw();if(playing)requestAnimationFrame(step);}
cv.onclick=function(e){var r=cv.getBoundingClientRect(),mx=(e.clientX-r.left)*cv.width/r.width,my=(e.clientY-r.top)*cv.height/r.height,best=null,bd=14;
D.contacts.forEach(function(k){var q=at(k.p,t);if(!q)return;var c=px(q[0],q[1]),d=Math.hypot(c[0]-mx,c[1]-my);if(d<bd){bd=d;best=k;}});show(best);};
slider.value=t/D.duration*1000;draw();})();
";
    }
}
