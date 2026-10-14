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

            // Decision tree: four checkpoints, branch counts and shares of the run, and one contact's path.
            var treeRules = RuleSet.Defaults();
            treeRules.fullScoring = false;   // the classic branches first; the full-scoring branches are checked below
            var seen = new Chaukanna.Scenario.ContactRecord { id = 1, truthClass = Chaukanna.Scenario.ContactClass.HostileQuad, spawnTime = 0f, firstDetectableTime = 0f, firstInViewTime = 2f,
                call = Chaukanna.Scenario.PlayerCall.HostileQuad, callTime = 3f, action = Chaukanna.Scenario.PlayerAction.Jam, actionTime = 4f };
            var birdCalledQuad = new Chaukanna.Scenario.ContactRecord { id = 2, truthClass = Chaukanna.Scenario.ContactClass.Bird, spawnTime = 0f, firstDetectableTime = 0f, firstInViewTime = 20f,
                call = Chaukanna.Scenario.PlayerCall.HostileFixedWing, callTime = 21f, callRange = 100f, action = Chaukanna.Scenario.PlayerAction.Engage, actionTime = 22f, shots = 1 };
            var neverSeen = new Chaukanna.Scenario.ContactRecord { id = 3, truthClass = Chaukanna.Scenario.ContactClass.CivilianQuad, spawnTime = 0f, firstDetectableTime = 0f };
            var noCall = new Chaukanna.Scenario.ContactRecord { id = 4, truthClass = Chaukanna.Scenario.ContactClass.Bird, spawnTime = 0f, firstDetectableTime = 0f, firstInViewTime = 7f };
            var treeSession = ScoreCalculator.Calculate(new[] { seen, birdCalledQuad, neverSeen, noCall }, treeRules, 300f);
            var tree = DecisionTreeData.Build(treeSession);
            System.Func<int, string, TreeNode> node = (stage, key) => tree[stage].nodes.Find(n => n.key == key);
            Check("tree has five checkpoints", tree.Count == 5 && tree[0].title == "DETECTED?" && tree[3].title == "OUTCOME" && tree[4].title == "DECISION vs RESULT");
            Check("tree detection branches: 1 fast, 1 medium, 1 slow, 1 missed", node(0, "Fast").count == 1 && node(0, "Medium").count == 1 && node(0, "Slow").count == 1 && node(0, "Missed").count == 1);
            Check("tree shares are of the whole run (25 % each)", Mathf.Approximately(node(0, "Fast").percent, 25f) && Mathf.Approximately(node(1, "Right").percent, 25f));
            Check("tree classification: 1 right, 1 wrong, 1 no call; the missed contact never reaches it", node(1, "Right").count == 1 && node(1, "Wrong").count == 1 && node(1, "No call").count == 1 && tree[1].reached == 3);
            Check("tree outcome: 1 right action, 1 false engagement, 1 unresolved", node(3, "Right action").count == 1 && node(3, "False engagement").count == 1 && node(3, "Unresolved").count == 1);
            var onePath = DecisionTreeData.Build(treeSession, treeSession.contacts[1]);
            Check("one contact's path is marked at every checkpoint it reached",
                onePath[0].nodes.Find(n => n.taken).key == "Slow" && onePath[1].nodes.Find(n => n.taken).key == "Wrong" && onePath[2].nodes.Find(n => n.taken).key == "Engage" && onePath[3].nodes.Find(n => n.taken).key == "False engagement");
            var missedPath = DecisionTreeData.Build(treeSession, treeSession.contacts[2]);
            Check("a missed contact's path stops at the first checkpoint", missedPath[0].focusReached && !missedPath[1].focusReached && missedPath[1].nodes.TrueForAll(n => !n.taken));
            // With full scoring the bird left alone for 30 s is a correct rejection, and the fifth checkpoint fills in.
            var fullTree = DecisionTreeData.Build(ScoreCalculator.Calculate(new[] { seen, birdCalledQuad, neverSeen, noCall }, RuleSet.Defaults(), 300f));
            System.Func<int, string, TreeNode> fullNode = (stage, key) => fullTree[stage].nodes.Find(n => n.key == key);
            Check("full scoring: bird left alone = 'Left alone' + right action; decision vs result has 3 decided contacts",
                fullNode(2, "Left alone").count == 1 && fullNode(3, "Right action").count == 2 && fullTree[4].reached == 3 && fullNode(4, "Sound, successful").count == 1 && fullNode(4, "Pending").count == 1 && fullNode(4, "Sound, unlucky").count == 1);
            Check("an empty run gives an empty tree without errors", DecisionTreeData.Build(new SessionResult()).TrueForAll(st => st.reached == 0));

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
