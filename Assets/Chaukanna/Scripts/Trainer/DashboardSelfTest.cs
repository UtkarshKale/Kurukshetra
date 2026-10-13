using System.Collections.Generic;
using Chaukanna.Scoring;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // Checks for the dashboard's chart data preparation. Run via the CLI: Chaukanna.Trainer.DashboardSelfTest.Run()
    public static class DashboardSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();

            // Padding: always ten points, samples first and flagged, real ones last and in order.
            var none = TrendData.PadWithSamples(new List<TrendPoint>(), 10);
            Check("no real sessions: 10 points, all sample", none.Count == 10 && none.TrueForAll(p => p.sample));

            var three = new List<TrendPoint> { Real(10f), Real(20f), Real(30f) };
            var padded = TrendData.PadWithSamples(three, 10);
            bool order = padded.Count == 10;
            for (int i = 0; i < 7 && order; i++) if (!padded[i].sample) order = false;
            for (int i = 7; i < 10 && order; i++) if (padded[i].sample) order = false;
            Check("3 real sessions: 7 sample then 3 real", order);
            Check("real sessions keep their order and values", padded[7].scorePercent == 10f && padded[8].scorePercent == 20f && padded[9].scorePercent == 30f);

            var many = new List<TrendPoint>();
            for (int i = 0; i < 14; i++) many.Add(Real(i));
            var newest = TrendData.PadWithSamples(many, 10);
            Check("more than 10 real sessions: newest 10, no samples", newest.Count == 10 && newest.TrueForAll(p => !p.sample) && newest[0].scorePercent == 4f && newest[9].scorePercent == 13f);

            var again = TrendData.PadWithSamples(new List<TrendPoint>(), 10);
            bool same = true;
            for (int i = 0; i < 10; i++) if (again[i].classificationAccuracy != none[i].classificationAccuracy) same = false;
            Check("sample points are the same every time", same);
            Check("sample values stay in range", none.TrueForAll(p => p.classificationAccuracy >= 0f && p.classificationAccuracy <= 100f && p.engagementScore >= 0f && p.engagementScore <= 100f && p.detectionSeconds > 0f));

            // Trend arrow.
            Check("score up is improving", TrendData.Trend(60f, 70f) == 1 && TrendData.TrendWord(1) == "UP");
            Check("score down is worse", TrendData.Trend(70f, 60f) == -1 && TrendData.TrendWord(-1) == "DOWN");
            Check("small change is flat", TrendData.Trend(70f, 71f) == 0 && TrendData.TrendWord(0) == "FLAT");
            Check("detection time down is improving", TrendData.Trend(12f, 8f, false) == 1);
            Check("detection time up is worse", TrendData.Trend(8f, 12f, false) == -1);

            // Session to trend point.
            var session = new SessionResult { totalPoints = 30, maxPoints = 40, averageDetectionSeconds = -1f, classificationAccuracy = 62f, engagementDecisionScore = 50f };
            var point = TrendData.FromSession(session);
            Check("session maps to a real trend point", !point.sample && point.scorePercent == 75f && point.detectionSeconds == 0f && point.classificationAccuracy == 62f);

            // Placeholder unit table.
            var unit = new SampleUnitStatsSource();
            Check("unit placeholder is marked as sample data with 6 soldiers", unit.IsSample && unit.Soldiers.Count == 6 && unit.Title.Contains("sample data"));

            // Missing folder is not an error.
            Check("missing sessions folder gives no points", TrendData.LoadSessions("Z:/does/not/exist", 10).Count == 0);

            string summary = $"[DashboardSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static TrendPoint Real(float score)
        {
            return new TrendPoint { scorePercent = score, classificationAccuracy = score, engagementScore = score, detectionSeconds = 5f };
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
