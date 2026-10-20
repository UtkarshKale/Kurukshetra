using System.Collections.Generic;
using System.IO;
using Kurukshetra.Scenario;
using Kurukshetra.Scoring;
using UnityEngine;

namespace Kurukshetra.Trainer
{
    // Checks for the AAR cards: card data, ordering, CSV and the HTML report's card grid and export buttons.
    // Run via the CLI: Kurukshetra.Trainer.AarCardsSelfTest.Run()
    public static class AarCardsSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();

            var s = Sample();
            var trend = new List<TrendPoint>();
            for (int i = 0; i < 6; i++) trend.Add(new TrendPoint { scorePercent = 40f + i * 5f, sample = i < 3 });
            var weakest = new List<LearnerModel.Cell> { new LearnerModel.Cell { task = "classify", cls = "CivilianQuad", light = "Night", rangeBin = 2, p = 0.41f, seen = 5 } };
            var cards = AarCards.Build(s, trend, weakest);

            Check("at least 12 cards (" + cards.Count + ")", cards.Count >= 12);
            bool ordered = true;
            for (int i = 1; i < cards.Count; i++) if (cards[i].severity > cards[i - 1].severity) ordered = false;
            Check("cards are ordered weakest problem first", ordered);
            var byId = new Dictionary<string, AarCard>();
            bool verdicts = true, unique = true;
            foreach (var c in cards)
            {
                if (string.IsNullOrWhiteSpace(c.verdict) || string.IsNullOrWhiteSpace(c.title) || string.IsNullOrWhiteSpace(c.hero)) verdicts = false;
                if (byId.ContainsKey(c.id)) unique = false;
                byId[c.id] = c;
            }
            Check("every card has a title, hero and verdict", verdicts);
            Check("card ids are unique", unique);
            Check("clock card flags the 2 misses", byId["clock"].status != CardStatus.Good && byId["clock"].severity >= 40f && byId["clock"].hero == "2 missed");
            Check("a clean card is good (AI cue: nothing followed wrong)", byId["ai"].status == CardStatus.Good || byId["ai"].status == CardStatus.Warn);
            Check("classification card names the worst mix-up", byId["classification"].verdict.Contains("Civilian quad called Bird"));
            Check("ungated d'/c shows insufficient data on the report slider", !s.reportSdt.gated && byId["sdt"].chart == CardChart.Sliders);
            Check("weakest card sits above a strong card", cards.FindIndex(c => c.id == byId["clock"].id) < cards.FindIndex(c => c.id == "ai"));

            // No data: still builds every card, none crashes, the no-data ones sort last.
            var empty = AarCards.Build(new SessionResult());
            bool allVerdicts = empty.Count == cards.Count && empty.TrueForAll(c => !string.IsNullOrWhiteSpace(c.verdict));
            Check("an empty session builds all cards with verdicts", allVerdicts);
            Check("an empty session: every card is no-data", empty.TrueForAll(c => c.status == CardStatus.Neutral));

            // CSV: header plus one row per contact (the sample has a comma inside a field, which must stay one row).
            string csv = AarCards.ToCsv(s);
            var rows = new List<string>(csv.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries));
            Check("CSV has a header and one row per contact (" + rows.Count + ")", rows.Count == s.contacts.Count + 1 && rows[0].StartsWith("id,truth_class"));
            Check("CSV quotes a field that holds a comma", csv.Contains("\"your call, with comma\""));

            // Data export writes both files.
            string dir = Path.Combine(Application.temporaryCachePath, "aar_cards_test");
            var files = AarCards.ExportData(s, dir);
            Check("data export wrote the JSON and the CSV", files != null && File.Exists(files[0]) && File.Exists(files[1]));
            if (files != null) { try { File.Delete(files[0]); File.Delete(files[1]); } catch (System.Exception) { } }

            // HTML.
            string html = HtmlReport.Build(s, new ContactRecord[0], "Test Player", trend, weakest);
            Check("HTML has the card grid", html.Contains(AarCardsHtml.GridMarker) && html.Contains("class=\"card "));
            Check("HTML has one card per AarCard", CountOf(html, "<article class=\"card ") == cards.Count);
            Check("HTML has the print CSS and cards that do not split", html.Contains("@media print") && html.Contains("break-inside:avoid"));
            Check("HTML has the Print / Save as PDF button", html.Contains("window.print()") && html.Contains("Print / Save as PDF"));
            Check("HTML has the Download data (JSON) button and the embedded data", html.Contains("Download data (JSON)") && html.Contains("id=\"session-data\"") && html.Contains("new Blob"));
            Check("HTML keeps the disclaimer and the CREDITS line", html.Contains(HtmlReport.Disclaimer) && html.Contains("CREDITS.txt"));
            Check("HTML keeps the replay and the contact table", html.Contains("id=\"rp\"") && html.Contains("Every contact"));
            Check("HTML cards carry inline SVG charts", CountOf(html, "<svg class=\"chart\"") == cards.Count);

            // Charts (shared by the dashboard Charts tab, the cards and the HTML report): real data only, empty states, layout inside the box.
            var real = new List<TrendPoint>();
            for (int i = 0; i < 6; i++) real.Add(new TrendPoint { scorePercent = new[] { 40f, 52f, 47f, 66f, 71f, 75f }[i], detectionSeconds = 12f - i, classificationAccuracy = 50f + i * 5f, engagementScore = 45f + i * 6f, noDetection = i == 2, label = "2026-10-1" + i });
            var models = ChartModels.All(s, real);
            Check("six chart models", models.Count == 6);
            Check("every chart of the sample run has data", models.TrueForAll(m => m.HasData));
            var scoreTrend = models[0];
            Check("score trend has the pass mark and a 3-session average", scoreTrend.refs.Count == 1 && scoreTrend.refs[0].value == ChartModels.PassPercent && scoreTrend.series.Count == 2 && scoreTrend.series[1].dashed);
            Check("detection trend leaves a gap (NaN) where nothing was seen", float.IsNaN(models[1].series[0].values[2]));
            var calls = models[4];
            float rightSeg = 0f; for (int i = 0; i < calls.cats.Length; i++) if (calls.cats[i] == "Hostile quad") rightSeg = calls.series[0].values[i];
            Check("calls chart is stacked by what was called: hostile quad called hostile quad = 2", calls.kind == ChartKind.Stacked && rightSeg == 2f && calls.series.Count == 6 && calls.series[0].ToneAt(0) == ChartTone.Good);
            var hist = models[5];
            float binned = 0f; foreach (float v in hist.series[0].values) binned += v;
            Check("histogram counts every scored contact (6 seen + 2 never = 8)", binned == 8f, binned.ToString());
            Check("histogram marks the median and the target", hist.refs.Count == 2 && hist.refs.Exists(r => r.label.StartsWith("median")) && hist.refs.Exists(r => r.label.StartsWith("target")));
            var none = ChartModels.All(new SessionResult(), new List<TrendPoint>());
            Check("with no data every chart says so and has no series", none.TrueForAll(m => !m.HasData && !string.IsNullOrEmpty(m.empty)));
            var sampleOnly = ChartModels.ScoreTrend(new List<TrendPoint> { new TrendPoint { scorePercent = 50f, sample = true } });
            Check("a sample-flagged point is never charted (no invented data)", !sampleOnly.HasData);
            bool inside = true; string where = "";
            foreach (var m in models)
                foreach (var box in new[] { new Vector2(560f, 300f), new Vector2(320f, 150f), new Vector2(430f, 290f) })
                    foreach (var p in ChartLayout.Build(m, box.x, box.y, 12f, box.x < 400f && box.y < 200f))
                    {
                        if (p.kind == PrimKind.Text) { if (p.y < 0f || p.y > box.y + 1f) { inside = false; where = m.id + " text " + p.text; } continue; }
                        float x0 = p.kind == PrimKind.Line ? Mathf.Min(p.x, p.x2) : p.kind == PrimKind.Dot ? p.x - p.w * 0.5f : p.x, x1 = p.kind == PrimKind.Line ? Mathf.Max(p.x, p.x2) : p.kind == PrimKind.Dot ? p.x + p.w * 0.5f : p.x + p.w;
                        float y0 = p.kind == PrimKind.Line ? Mathf.Min(p.y, p.y2) : p.kind == PrimKind.Dot ? p.y - p.w * 0.5f : p.y, y1 = p.kind == PrimKind.Line ? Mathf.Max(p.y, p.y2) : p.kind == PrimKind.Dot ? p.y + p.w * 0.5f : p.y + p.h;
                        if (x0 < -0.5f || y0 < -0.5f || x1 > box.x + 0.5f || y1 > box.y + 0.5f || float.IsNaN(x0 + x1 + y0 + y1)) { inside = false; where = m.id + " " + p.kind; }
                    }
            Check("every line, bar and dot of every chart stays inside its box", inside, where);
            string html2 = HtmlReport.Build(s, new ContactRecord[0], "Test Player", real, weakest);
            Check("HTML has a Charts view beside the cards (tabs, six inline SVG charts)", html2.Contains("id=\"v-charts\"") && html2.Contains("class=\"chartsec\"") && CountOf(html2, "<svg class=\"ckchart\"") == 6);
            Check("HTML charts carry hover titles and axis labels", html2.Contains("<title>") && html2.Contains("seconds until first seen") && html2.Contains("pass mark 70 %"));
            Check("HTML charts need no network or external script", !html2.Contains("src=\"http") && !html2.Contains("href=\"http") && !html2.Contains("<script src"));

            // Layout: 1, 2 or 3 columns by width.
            Check("grid columns by width", AarCardsView.ColumnsFor(500f) == 1 && AarCardsView.ColumnsFor(700f) == 2 && AarCardsView.ColumnsFor(1200f) == 3);

            foreach (var l in lines) Debug.Log("[AarCardsSelfTest] " + l);
            Debug.Log("[AarCardsSelfTest] " + (failures == 0 ? "ALL PASSED" : failures + " FAILED"));
            return failures == 0;
        }

        // A hand-made session that exercises every card. Also used to write the sample report (WriteSample).
        public static SessionResult Sample()
        {
            var s = new SessionResult
            {
                timestamp = "2026-10-20 09:30:00", seed = 42, duration = 240f, difficulty = "Standard", displayTier = "desktop",
                totalPoints = 31, maxPoints = 64, scoredContacts = 8, detected = 6, missed = 2, averageDetectionSeconds = 7.4f,
                classificationAccuracy = 66f, engagementDecisionScore = 50f, falseEngagements = 1, missedHostiles = 1,
                aiHintsShown = 5, wrongAiHints = 2, followedWrongHint = 0, overrodeWrongHintCorrectly = 2,
                processScore = 62f, gridSoundSuccessful = 3, gridSoundUnlucky = 1, gridLucky = 1, gridUnsound = 2,
                clockMisses = 2, correctRejections = 1, searchFailures = 1, perceptionFailures = 1, unverifiedEngagements = 1,
                confusionRows = new[] { "HostileQuad", "HostileFixedWing", "FriendlyISR", "CivilianQuad", "Bird" },
                confusionColumns = new[] { "HostileQuad", "HostileFixedWing", "OwnIsrDrone", "CivilianDrone", "Bird", "NoCall" },
                reportSdt = SdtResult.From(10, 12, 3, 8, 20),
                engageSdt = SdtResult.From(22, 25, 6, 22, 20),
            };
            s.confusion = new[]
            {
                2, 0, 0, 0, 0, 1,
                0, 1, 0, 0, 0, 0,
                0, 0, 1, 0, 0, 0,
                0, 0, 0, 0, 3, 0,
                0, 0, 0, 0, 1, 0,
            };
            s.swarm = new SwarmTriageResult { happened = true, size = 6, seen = 6, actedOn = 4, nearestFirst = 2, leakers = 1, nearestFirstRate = 0.5f, firstActionSeconds = 4f, summary = "6-member swarm, 1 leaked." };
            s.groupCall = new GroupCallResult { happened = true, calls = 3, right = 2, wrong = 1, meanSeconds = 5f, summary = "2 of 3 group calls right." };
            s.cardChange = new CardChangeResult { happened = true, from = "A", to = "B", time = 120f, adaptationSeconds = 9f, staleErrors = 1, decisionsAfter = 3, summary = "Card A to B at 120 s." };

            string[] truths = { "HostileQuad", "HostileQuad", "HostileFixedWing", "FriendlyISR", "CivilianQuad", "CivilianQuad", "Bird", "HostileQuad" };
            for (int i = 0; i < truths.Length; i++)
            {
                bool detected = i != 6 && i != 7;
                bool acted = i < 4;
                var c = new ContactResult
                {
                    id = i + 1, truthClass = truths[i], scored = true, detected = detected, detectionSeconds = detected ? 3f + i * 1.6f : -1f,
                    call = i == 4 || i == 5 ? "Bird" : truths[i], action = acted ? "Engage" : "none", roeAtAction = i < 2 ? "A" : "B",
                    spawnTime = 10f + i * 20f, despawnTime = 60f + i * 20f, firstInViewTime = detected ? 20f + i * 20f : -1f, callTime = detected ? 24f + i * 20f : -1f,
                    actionTime = acted ? 30f + i * 20f : -1f, clockOpen = 20f + i * 20f, deadline = detected ? 40f + i * 20f : -1f,
                    clockMiss = i == 1 || i == 2, correctRejection = i == 3, total = i < 4 ? 7 : 2, grid = i < 2 ? "Sound, successful" : "Unsound",
                    yourCall = i == 4 ? "your call, with comma" : "Hostile quad", soundCall = "Hostile quad", outcome = "Neutralised", missKind = i == 6 ? "search failure: never in view" : "",
                    classBranch = i == 4 || i == 5 ? "Wrong" : "Right", actBranch = acted ? "Right" : "None", processScore = 80,
                };
                c.nodes.Add(new NodeResult { label = "DET", points = 3, passed = detected, reason = "seen" });
                c.nodes.Add(new NodeResult { label = "CLASS", points = detected ? 3 : 0, passed = detected && i != 4 && i != 5, reason = "" });
                c.nodes.Add(new NodeResult { label = "ACT", points = acted ? 2 : 0, passed = acted && i != 1 && i != 3, reason = "" });
                s.contacts.Add(c);
            }
            return s;
        }

        // Writes a sample HTML report from the hand-made session (for rendering headless).
        public static string WriteSample(string path)
        {
            var trend = new List<TrendPoint>();
            for (int i = 0; i < 8; i++) trend.Add(new TrendPoint { scorePercent = new[] { 34f, 41f, 38f, 47f, 52f, 49f, 55f, 48f }[i], sample = i < 4 });
            var weakest = new List<LearnerModel.Cell>
            {
                new LearnerModel.Cell { task = "classify", cls = "CivilianQuad", light = "Night", rangeBin = 2, p = 0.41f, seen = 5 },
                new LearnerModel.Cell { task = "classify", cls = "Bird", light = "Day", rangeBin = 3, p = 0.58f, seen = 7 },
                new LearnerModel.Cell { task = "classify", cls = "HostileFixedWing", light = "Dusk", rangeBin = 1, p = 0.79f, seen = 4 },
            };
            File.WriteAllText(path, HtmlReport.Build(Sample(), new ContactRecord[0], "Sample Player", trend, weakest));
            return path;
        }

        static int CountOf(string text, string needle)
        {
            int n = 0, i = 0;
            while ((i = text.IndexOf(needle, i, System.StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
            return n;
        }

        static void Check(string name, bool ok, string detail = "")
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  [" + detail + "]" : ""));
        }
    }
}
