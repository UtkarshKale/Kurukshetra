using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chaukanna.Trainer;
using UnityEngine;

namespace Chaukanna.Scoring
{
    public enum CardStatus { Neutral, Good, Warn, Bad }
    public enum CardChart { Bars, Histogram, Heatmap, Stacked, Grid, Sliders, Ticks, Timeline, Line, CardChange }

    // One AAR card: a title, a hero number, one plain sentence, a status and a small chart. Pure data, so the IMGUI dashboard,
    // the HTML report and the self-test all read the same thing. `severity` (0-100, higher = worse; -1 = no data) orders the
    // grid weakest problem first.
    public class AarCard
    {
        public string id, title, hero, verdict, footer;
        public CardStatus status;
        public float severity;
        public int order;
        public bool tall;
        public CardChart chart;
        public SessionResult session;               // heatmap, timeline and card change read the session directly

        // Chart payloads (which are used depends on `chart`).
        public string[] labels;
        public float[] values;                      // bars, histogram, line
        public float max = 100f;                    // bars: value that fills the bar
        public string[] valueText;                  // bars: text at the end of each bar
        public CardStatus[] barStatus;
        public int[] right, wrong;                  // stacked
        public int[] counts;                        // grid (4), ticks (0 = in time, 1 = missed, 2 = correct rejection)
        public bool[] sample;                       // line: generated filler points
        public SdtResult[] sdt;                     // sliders

        public string StatusWord => status == CardStatus.Good ? "GOOD" : status == CardStatus.Warn ? "WATCH" : status == CardStatus.Bad ? "PROBLEM" : "NO DATA";
    }

    public static class AarCards
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static CardStatus StatusFor(float severity) => severity < 0f ? CardStatus.Neutral : severity < 30f ? CardStatus.Good : severity < 60f ? CardStatus.Warn : CardStatus.Bad;
        static float Clamp100(float v) => Mathf.Clamp(v, 0f, 100f);

        // Builds every card, weakest problem first (ties keep the fixed order below; cards with no data go last).
        public static List<AarCard> Build(SessionResult s, IList<TrendPoint> trend = null, IList<LearnerModel.Cell> weakest = null)
        {
            var list = new List<AarCard>
            {
                Overall(s), Detection(s), Classification(s), Engagement(s), Quality(s), SdtCard(s), Clock(s),
                SearchPerception(s), Swarm(s), CardChange(s), Timeline(s), Learner(weakest), Trends(trend), AiCue(s),
            };
            for (int i = 0; i < list.Count; i++)
            {
                list[i].order = i;
                list[i].status = StatusFor(list[i].severity);
                if (string.IsNullOrEmpty(list[i].verdict)) list[i].verdict = "No verdict.";
            }
            list.Sort((a, b) => { int c = b.severity.CompareTo(a.severity); return c != 0 ? c : a.order.CompareTo(b.order); });
            return list;
        }

        static AarCard Make(string id, string title, CardChart chart, SessionResult s) => new AarCard { id = id, title = title, chart = chart, session = s, hero = "-", verdict = "", footer = "", severity = -1f };

        static int Scored(SessionResult s) { int n = 0; foreach (var c in s.contacts) if (c.scored) n++; return n; }

        static AarCard Overall(SessionResult s)
        {
            var c = Make("overall", "OVERALL SCORE", CardChart.Bars, s);
            float pct = s.ScorePercent;
            int scored = Mathf.Max(0, s.scoredContacts);
            float seen = scored > 0 ? 100f * s.detected / scored : 0f;
            c.hero = $"{s.totalPoints} / {s.maxPoints}";
            c.labels = new[] { "Score", "Seen", "Classified right", "Right action" };
            c.values = new[] { pct, seen, s.classificationAccuracy, s.engagementDecisionScore };
            c.valueText = new[] { pct.ToString("F0", Inv) + " %", seen.ToString("F0", Inv) + " %", s.classificationAccuracy.ToString("F0", Inv) + " %", s.engagementDecisionScore.ToString("F0", Inv) + " %" };
            c.barStatus = new[] { PctStatus(pct), PctStatus(seen), PctStatus(s.classificationAccuracy), PctStatus(s.engagementDecisionScore) };
            if (s.maxPoints <= 0) { c.verdict = "No scored contacts yet."; c.footer = "n = 0"; return c; }
            c.severity = Clamp100(100f - pct);
            c.verdict = pct >= 70f ? $"Solid run: {pct:F0} % of the points available." : pct >= 45f ? $"Mixed run: {pct:F0} % of the points; the weak cards below say where they went." : $"Weak run: only {pct:F0} % of the points. Start with the first card.";
            c.footer = $"n = {scored} scored contacts";
            return c;
        }

        static CardStatus PctStatus(float p) => p >= 70f ? CardStatus.Good : p >= 45f ? CardStatus.Warn : CardStatus.Bad;

        static AarCard Detection(SessionResult s)
        {
            var c = Make("detection", "DETECTION TIME", CardChart.Histogram, s);
            var times = new List<float>();
            foreach (var k in s.contacts) if (k.scored && k.detected && k.detectionSeconds >= 0f) times.Add(k.detectionSeconds);
            c.values = times.ToArray();
            int scored = Scored(s);
            c.footer = $"n = {times.Count} detected of {scored}";
            if (s.averageDetectionSeconds < 0f)
            {
                c.verdict = scored > 0 ? "Nothing was brought into view." : "No scored contacts yet.";
                if (scored > 0) c.severity = 100f;
                return c;
            }
            c.hero = s.averageDetectionSeconds.ToString("F1", Inv) + " s";
            float timeSev = Clamp100((s.averageDetectionSeconds - 5f) / 10f * 100f);
            float missSev = scored > 0 ? 100f * s.missed / scored : 0f;
            c.severity = Clamp100(Mathf.Max(timeSev * 0.8f, missSev * 1.2f));
            c.verdict = s.missed > 0 ? $"Average {s.averageDetectionSeconds:F1} s to bring a contact into view, and {s.missed} never came into view." : s.averageDetectionSeconds <= 6f ? $"Quick: average {s.averageDetectionSeconds:F1} s and nothing missed." : $"Everything was seen, but slowly: average {s.averageDetectionSeconds:F1} s.";
            return c;
        }

        static AarCard Classification(SessionResult s)
        {
            var c = Make("classification", "CLASSIFICATION", CardChart.Heatmap, s);
            c.tall = true;
            c.footer = $"n = {s.detected} detected contacts";
            if (s.detected <= 0 || s.confusion == null || s.confusionRows == null) { c.verdict = "Nothing was detected, so nothing was classified."; return c; }
            c.hero = s.classificationAccuracy.ToString("F0", Inv) + " %";
            c.severity = Clamp100(100f - s.classificationAccuracy);
            int cols = s.confusionColumns.Length, best = 0, br = -1, bc = -1;
            for (int r = 0; r < s.confusionRows.Length; r++)
                for (int k = 0; k < cols - 1; k++)
                {
                    if (k == r) continue;
                    int n = s.confusion[r * cols + k];
                    if (n > best) { best = n; br = r; bc = k; }
                }
            c.verdict = best > 0
                ? $"{s.classificationAccuracy:F0} % right. Most confused: {HtmlReport.Pretty(s.confusionRows[br])} called {HtmlReport.Pretty(s.confusionColumns[bc])} ({best}x)."
                : $"{s.classificationAccuracy:F0} % right, with no repeated mix-up.";
            return c;
        }

        static AarCard Engagement(SessionResult s)
        {
            var c = Make("engagement", "ENGAGEMENT DECISIONS", CardChart.Stacked, s);
            var names = new List<string>();
            var right = new List<int>();
            var wrong = new List<int>();
            int actioned = 0;
            foreach (var k in s.contacts)
            {
                if (!k.scored || string.IsNullOrEmpty(k.action) || k.action == "none") continue;
                actioned++;
                string card = string.IsNullOrEmpty(k.roeAtAction) ? "?" : k.roeAtAction;
                int i = names.IndexOf(card);
                if (i < 0) { names.Add(card); right.Add(0); wrong.Add(0); i = names.Count - 1; }
                if (k.nodes.Count > 2 && k.nodes[2].passed) right[i]++; else wrong[i]++;
            }
            c.labels = names.ToArray();
            c.right = right.ToArray();
            c.wrong = wrong.ToArray();
            c.footer = $"n = {actioned} decisions";
            if (actioned == 0) { c.verdict = s.missedHostiles > 0 ? $"No action taken; {s.missedHostiles} hostile(s) went unanswered." : "No decisions were made."; if (s.missedHostiles > 0) c.severity = 90f; return c; }
            c.hero = s.engagementDecisionScore.ToString("F0", Inv) + " %";
            c.severity = Clamp100(100f - s.engagementDecisionScore + s.falseEngagements * 5f);
            c.verdict = s.falseEngagements > 0 ? $"{s.falseEngagements} false engagement(s) on something that was not hostile; {s.engagementDecisionScore:F0} % of actions were right."
                : s.engagementDecisionScore >= 70f ? $"{s.engagementDecisionScore:F0} % of actions were right for the card in force." : $"Only {s.engagementDecisionScore:F0} % of actions were right for the card in force.";
            return c;
        }

        static AarCard Quality(SessionResult s)
        {
            var c = Make("quality", "DECISION QUALITY", CardChart.Grid, s);
            c.counts = new[] { s.gridSoundSuccessful, s.gridSoundUnlucky, s.gridLucky, s.gridUnsound };
            c.labels = new[] { "sound, good result", "sound, bad luck", "unsound, lucky", "unsound" };
            c.barStatus = new[] { CardStatus.Good, CardStatus.Warn, CardStatus.Warn, CardStatus.Bad };
            int total = s.gridSoundSuccessful + s.gridSoundUnlucky + s.gridLucky + s.gridUnsound;
            c.footer = $"n = {total} decisions";
            if (total == 0) { c.verdict = "No decisions to grade yet."; return c; }
            c.hero = s.processScore < 0f ? "-" : s.processScore.ToString("F0", Inv) + " / 100";
            c.severity = Clamp100(130f * (s.gridUnsound + 0.5f * s.gridLucky) / total);
            int sound = s.gridSoundSuccessful + s.gridSoundUnlucky;
            c.verdict = s.gridLucky > 0 ? $"{sound} of {total} decisions were sound; {s.gridLucky} worked out only by luck."
                : $"{sound} of {total} decisions were sound" + (s.gridUnsound > 0 ? $"; {s.gridUnsound} were not." : ".");
            return c;
        }

        static AarCard SdtCard(SessionResult s)
        {
            var c = Make("sdt", "REPORT AND ENGAGE: d' / c", CardChart.Sliders, s);
            c.sdt = new[] { s.reportSdt ?? new SdtResult(), s.engageSdt ?? new SdtResult() };
            c.labels = new[] { "Report: is there a drone?", "Engage: is it hostile?" };
            var r = c.sdt[1].gated ? c.sdt[1] : c.sdt[0].gated ? c.sdt[0] : null;
            c.footer = $"engage n = {c.sdt[1].signals}+{c.sdt[1].noise}, report n = {c.sdt[0].signals}+{c.sdt[0].noise}";
            if (r == null) { c.hero = "n/a"; c.verdict = "Insufficient data: d' and c need 20 hostile and 20 non-hostile trials."; return c; }
            c.hero = "d' " + r.dPrime.ToString("F1", Inv);
            c.severity = Clamp100(Mathf.Abs(r.c) * 50f + Mathf.Max(0f, 1.5f - r.dPrime) * 30f);
            string bias = r.c < -0.3f ? "Trigger-happy: acts on thin evidence" : r.c > 0.3f ? "Hesitant: waits for more than the evidence needs" : "Balanced between acting and waiting";
            c.verdict = $"{bias}; " + (r.dPrime < 1f ? "weak at telling hostile from non-hostile." : "tells hostile from non-hostile well enough.");
            return c;
        }

        static AarCard Clock(SessionResult s)
        {
            var c = Make("clock", "DECISION CLOCK", CardChart.Ticks, s);
            var states = new List<int>();
            foreach (var k in s.contacts)
            {
                if (!k.scored || k.deadline < 0f) continue;
                states.Add(k.clockMiss ? 1 : k.correctRejection ? 2 : 0);
            }
            c.counts = states.ToArray();
            c.footer = $"n = {states.Count} contacts with a clock";
            if (states.Count == 0) { c.verdict = "No contact was in view long enough to start a clock."; return c; }
            int miss = s.clockMisses;
            c.hero = miss + " missed";
            c.severity = miss > 0 ? Mathf.Max(40f, Clamp100(150f * miss / states.Count)) : 5f;
            c.verdict = miss > 0 ? $"{miss} hostile(s) were held or acted on after the release point." : "Every decision came inside its window.";
            return c;
        }

        static AarCard SearchPerception(SessionResult s)
        {
            var c = Make("search", "SEARCH vs PERCEPTION", CardChart.Bars, s);
            int scored = Scored(s);
            c.max = Mathf.Max(1f, scored);
            c.labels = new[] { "Search failures", "Perception failures" };
            c.values = new float[] { s.searchFailures, s.perceptionFailures };
            c.valueText = new[] { s.searchFailures.ToString(), s.perceptionFailures.ToString() };
            c.barStatus = new[] { s.searchFailures > 0 ? CardStatus.Bad : CardStatus.Good, s.perceptionFailures > 0 ? CardStatus.Bad : CardStatus.Good };
            c.footer = $"n = {scored} scored contacts";
            if (scored == 0) { c.verdict = "No scored contacts yet."; return c; }
            int total = s.searchFailures + s.perceptionFailures;
            c.hero = total.ToString();
            c.severity = total > 0 ? Mathf.Max(35f, Clamp100(150f * total / scored)) : 3f;
            c.verdict = total == 0 ? "Everything catchable was seen and marked."
                : s.searchFailures >= s.perceptionFailures ? $"Search is the gap: {s.searchFailures} contact(s) were never in view. Sweep the whole sector."
                : $"Marking is the gap: {s.perceptionFailures} contact(s) were on screen but never marked.";
            return c;
        }

        static AarCard Swarm(SessionResult s)
        {
            var c = Make("swarm", "SWARM TRIAGE AND GROUP CALLS", CardChart.Bars, s);
            bool sw = s.swarm != null && s.swarm.happened, gc = s.groupCall != null && s.groupCall.happened;
            var labels = new List<string>();
            var vals = new List<float>();
            var txt = new List<string>();
            var st = new List<CardStatus>();
            float sev = -1f;
            var verdict = new List<string>();
            if (sw)
            {
                float nf = s.swarm.nearestFirstRate < 0f ? 0f : 100f * s.swarm.nearestFirstRate;
                labels.Add("Nearest first"); vals.Add(nf); txt.Add(nf.ToString("F0", Inv) + " %"); st.Add(PctStatus(nf));
                labels.Add("Leakers (of " + s.swarm.size + ")"); vals.Add(s.swarm.leakers * 100f / Mathf.Max(1, s.swarm.size)); txt.Add(s.swarm.leakers.ToString()); st.Add(s.swarm.leakers > 0 ? CardStatus.Bad : CardStatus.Good);
                sev = s.swarm.leakers > 0 ? Mathf.Max(60f, 100f * s.swarm.leakers / Mathf.Max(1, s.swarm.size)) : s.swarm.nearestFirstRate >= 0.5f ? 10f : 40f;
                verdict.Add(s.swarm.leakers > 0 ? $"{s.swarm.leakers} of {s.swarm.size} swarm members got through." : s.swarm.nearestFirstRate >= 0.5f ? "Swarm handled nearest first, none leaked." : "Swarm held, but not nearest first.");
            }
            if (gc)
            {
                int tot = Mathf.Max(1, s.groupCall.calls);
                labels.Add("Group calls right"); vals.Add(100f * s.groupCall.right / tot); txt.Add(s.groupCall.right + " / " + s.groupCall.calls); st.Add(s.groupCall.wrong > 0 ? CardStatus.Warn : CardStatus.Good);
                float g = s.groupCall.wrong > 0 ? Mathf.Max(35f, 100f * s.groupCall.wrong / tot) : 8f;
                sev = Mathf.Max(sev, g);
                verdict.Add(s.groupCall.wrong > 0 ? $"{s.groupCall.wrong} group call(s) wrong." : "Group calls all right.");
            }
            c.labels = labels.ToArray(); c.values = vals.ToArray(); c.valueText = txt.ToArray(); c.barStatus = st.ToArray();
            if (!sw && !gc) { c.verdict = "No swarm or group in this run."; c.footer = "n = 0"; return c; }
            c.severity = Clamp100(sev);
            c.hero = sw ? $"{s.swarm.leakers} leaked" : $"{s.groupCall.right} / {s.groupCall.calls}";
            c.verdict = string.Join(" ", verdict);
            c.footer = $"n = {(sw ? s.swarm.size + " swarm members" : "")}{(sw && gc ? ", " : "")}{(gc ? s.groupCall.calls + " group calls" : "")}";
            return c;
        }

        static AarCard CardChange(SessionResult s)
        {
            var c = Make("cardchange", "ROE CARD CHANGE", CardChart.CardChange, s);
            var cc = s.cardChange;
            if (cc == null || !cc.happened) { c.verdict = "The card did not change in this run."; c.footer = "n = 0"; return c; }
            c.hero = cc.adaptationSeconds < 0f ? "no switch" : cc.adaptationSeconds.ToString("F1", Inv) + " s";
            c.severity = cc.staleErrors > 0 ? 70f : cc.adaptationSeconds < 0f ? 50f : 10f;
            c.verdict = cc.staleErrors > 0 ? $"{cc.staleErrors} decision(s) still followed the old card ({cc.from} to {cc.to})."
                : cc.adaptationSeconds < 0f ? $"Card went {cc.from} to {cc.to}; no decision tested the new rule yet."
                : $"Adapted to Card {cc.to} in {cc.adaptationSeconds:F1} s.";
            c.footer = $"n = {cc.decisionsAfter} decisions after the change";
            return c;
        }

        static AarCard Timeline(SessionResult s)
        {
            var c = Make("timeline", "IN VIEW vs IN THE AIR", CardChart.Timeline, s);
            c.tall = true;
            int scored = 0, seen = 0;
            foreach (var k in s.contacts) { if (!k.scored) continue; scored++; if (k.firstInViewTime >= 0f) seen++; }
            c.footer = $"n = {scored} contacts, {s.duration:F0} s";
            if (scored == 0) { c.verdict = "No contacts to show."; return c; }
            c.hero = $"{seen} / {scored}";
            c.severity = Clamp100(100f * (scored - seen) / scored * 1.2f);
            c.verdict = seen == scored ? "Every contact came into view while it was in the air." : $"{scored - seen} contact(s) flew through without ever being in view.";
            return c;
        }

        static AarCard Learner(IList<LearnerModel.Cell> weakest)
        {
            var c = Make("learner", "WEAKEST AREAS (LEARNER MODEL)", CardChart.Bars, null);
            c.footer = "all sessions on this PC";
            if (weakest == null || weakest.Count == 0) { c.verdict = "No history yet. The model learns after the first finished run."; c.footer = "n = 0 sessions"; return c; }
            var labels = new List<string>();
            var vals = new List<float>();
            var txt = new List<string>();
            var st = new List<CardStatus>();
            foreach (var w in weakest)
            {
                labels.Add($"{LearnerModel.Pretty(w.cls)}, {w.light}, {LearnerModel.RangeNames[Mathf.Clamp(w.rangeBin, 0, LearnerModel.RangeNames.Length - 1)]}");
                vals.Add(w.p * 100f); txt.Add((w.p * 100f).ToString("F0", Inv) + " %"); st.Add(PctStatus(w.p * 100f));
            }
            c.labels = labels.ToArray(); c.values = vals.ToArray(); c.valueText = txt.ToArray(); c.barStatus = st.ToArray();
            float low = weakest[0].p * 100f;
            c.hero = low.ToString("F0", Inv) + " %";
            c.severity = Clamp100((100f - low) * 0.8f);
            c.verdict = $"Weakest: {labels[0]}, predicted {low:F0} % right. Practise this first.";
            return c;
        }

        static AarCard Trends(IList<TrendPoint> trend)
        {
            var c = Make("trends", "TREND OVER SESSIONS", CardChart.Line, null);
            c.footer = "score per session";
            if (trend == null || trend.Count == 0) { c.verdict = "No earlier sessions to compare."; c.footer = "n = 0"; return c; }
            c.values = new float[trend.Count];
            c.sample = new bool[trend.Count];
            int real = 0;
            for (int i = 0; i < trend.Count; i++) { c.values[i] = trend[i].scorePercent; c.sample[i] = trend[i].sample; if (!trend[i].sample) real++; }
            float last = c.values[c.values.Length - 1];
            c.hero = last.ToString("F0", Inv) + " %";
            c.footer = $"n = {real} saved session(s)" + (real < trend.Count ? ", rest sample data" : "");
            if (trend.Count < 2) { c.verdict = "Only one session so far."; c.severity = 20f; return c; }
            float prev = c.values[c.values.Length - 2], delta = last - prev;
            c.severity = delta < -10f ? 65f : delta < -3f ? 45f : delta > 3f ? 10f : 25f;
            c.verdict = delta > 3f ? $"Better than last time by {delta:F0} points." : delta < -3f ? $"Worse than last time by {-delta:F0} points." : "About the same as last time.";
            return c;
        }

        static AarCard AiCue(SessionResult s)
        {
            var c = Make("ai", "AI CUE", CardChart.Bars, s);
            c.footer = $"n = {s.aiHintsShown} hints";
            if (s.aiHintsShown <= 0) { c.verdict = "No AI hints were shown in this run."; return c; }
            c.max = s.aiHintsShown;
            c.labels = new[] { "Hints shown", "Wrong hints", "Followed a wrong hint", "Overrode a wrong hint" };
            c.values = new float[] { s.aiHintsShown, s.wrongAiHints, s.followedWrongHint, s.overrodeWrongHintCorrectly };
            c.valueText = new[] { s.aiHintsShown.ToString(), s.wrongAiHints.ToString(), s.followedWrongHint.ToString(), s.overrodeWrongHintCorrectly.ToString() };
            c.barStatus = new[] { CardStatus.Neutral, CardStatus.Warn, s.followedWrongHint > 0 ? CardStatus.Bad : CardStatus.Good, CardStatus.Good };
            c.hero = $"{s.followedWrongHint} / {s.wrongAiHints}";
            c.severity = s.wrongAiHints > 0 ? Clamp100(80f * s.followedWrongHint / s.wrongAiHints) : 3f;
            c.verdict = s.wrongAiHints == 0 ? "The cue was never wrong; no test of trusting it." : s.followedWrongHint > 0 ? $"Followed the AI cue when it was wrong {s.followedWrongHint} time(s) of {s.wrongAiHints}." : $"Caught every wrong AI cue ({s.overrodeWrongHintCorrectly} overridden).";
            return c;
        }

        // --- Exports -------------------------------------------------------------------------------------------------

        static string Q(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        }

        static string F(float v) => v < 0f ? "" : v.ToString("0.##", Inv);

        // One header line and one row per contact.
        public static string ToCsv(SessionResult s)
        {
            var sb = new StringBuilder(4096);
            sb.Append("id,truth_class,scored,detected,detection_s,call,action,roe_at_action,class_branch,act_branch,outcome,grid,process_score,clock_open_s,deadline_s,clock_miss,correct_rejection,unverified,n1a_s,n1b_s,miss_kind,group,deceptive,points,your_call,sound_call\n");
            foreach (var c in s.contacts)
            {
                sb.Append(c.id).Append(',').Append(Q(c.truthClass)).Append(',').Append(c.scored ? 1 : 0).Append(',').Append(c.detected ? 1 : 0).Append(',').Append(F(c.detectionSeconds)).Append(',')
                  .Append(Q(c.call)).Append(',').Append(Q(c.action)).Append(',').Append(Q(c.roeAtAction)).Append(',').Append(Q(c.classBranch)).Append(',').Append(Q(c.actBranch)).Append(',')
                  .Append(Q(c.outcome)).Append(',').Append(Q(c.grid)).Append(',').Append(c.processScore < 0 ? "" : c.processScore.ToString()).Append(',').Append(F(c.clockOpen)).Append(',').Append(F(c.deadline)).Append(',')
                  .Append(c.clockMiss ? 1 : 0).Append(',').Append(c.correctRejection ? 1 : 0).Append(',').Append(c.unverified ? 1 : 0).Append(',').Append(F(c.n1aSeconds)).Append(',').Append(F(c.n1bSeconds)).Append(',')
                  .Append(Q(c.missKind)).Append(',').Append(c.group ? 1 : 0).Append(',').Append(c.deceptive ? 1 : 0).Append(',').Append(c.total).Append(',').Append(Q(c.yourCall)).Append(',').Append(Q(c.soundCall)).Append('\n');
            }
            return sb.ToString();
        }

        // Writes the session JSON and the contact CSV into `folder`; returns the two paths (null on failure).
        public static string[] ExportData(SessionResult s, string folder)
        {
            try
            {
                Directory.CreateDirectory(folder);
                string stem = Path.Combine(folder, $"aar_{DateTime.Now:yyyyMMdd_HHmmss}_seed{s.seed}");
                File.WriteAllText(stem + ".json", JsonUtility.ToJson(s, true), new UTF8Encoding(false));
                File.WriteAllText(stem + "_contacts.csv", ToCsv(s), new UTF8Encoding(false));
                return new[] { stem + ".json", stem + "_contacts.csv" };
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Report] Could not export data: " + e.Message);
                return null;
            }
        }
    }
}
