using System.Collections.Generic;
using System.IO;
using Kurukshetra.Scenario;
using UnityEngine;

namespace Kurukshetra.Scoring
{
    // Checks for module 9: events from a run, the logistic fit, pooling, the 75 % plan, saving. Uses a temporary folder,
    // never the real learner files. CLI: Kurukshetra.Scoring.LearnerSelfTest.Run()
    public static class LearnerSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var rules = RuleSet.Defaults();

            // Range bins.
            Check("range bins: 200 m = 0, 450 m = 1, 800 m = 2, 1500 m = 3", LearnerModel.RangeBin(200f) == 0 && LearnerModel.RangeBin(450f) == 1 && LearnerModel.RangeBin(800f) == 2 && LearnerModel.RangeBin(1500f) == 3);

            // Events from a run: a right call on a quad, a wrong call on a bird, an unseen fixed-wing.
            var quad = Rec(1, ContactClass.HostileQuad, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, 450f);
            var bird = Rec(2, ContactClass.Bird, 12f, PlayerCall.HostileQuad, PlayerAction.None, 800f);
            var wing = new ContactRecord { id = 3, truthClass = ContactClass.HostileFixedWing, spawnTime = 5f, firstDetectableTime = 6f };
            var s = ScoreCalculator.Calculate(new[] { quad, bird, wing }, rules, 300f);
            s.timeOfDay = "Night";
            s.timestamp = "test";
            var obs = LearnerModel.Observe(s, new[] { quad, bird, wing }, rules);
            Check("events: 3 detect, 2 classify, decide for the jam and the bird left alone", obs.FindAll(o => o.task == "detect").Count == 3 && obs.FindAll(o => o.task == "classify").Count == 2 && obs.FindAll(o => o.task == "decide").Count == 2);
            var birdCall = obs.Find(o => o.task == "classify" && o.cls == "Bird");
            Check("the bird's wrong call is a classify miss at 600-1000 m in the light it was called in", birdCall != null && !birdCall.correct && birdCall.rangeBin == 2 && birdCall.light == "Day");
            Check("the unseen fixed-wing is a detect miss", obs.Exists(o => o.task == "detect" && o.cls == "HostileFixedWing" && !o.correct));

            // Fit: a soldier who always gets quads right and birds wrong.
            var p = new LearnerProfile { player = "Test" };
            var synthetic = new List<LearnerObservation>();
            for (int i = 0; i < 40; i++)
            {
                synthetic.Add(new LearnerObservation { task = "classify", cls = "HostileQuad", light = "Day", rangeBin = 1, correct = i % 10 != 0 });   // 90 %
                synthetic.Add(new LearnerObservation { task = "classify", cls = "Bird", light = "Day", rangeBin = 1, correct = i % 10 < 3 });          // 30 %
            }
            LearnerModel.Add(p, synthetic);
            LearnerModel.Fit(p, null);
            float pq = LearnerModel.Predict(p, "classify", "HostileQuad", "Day", 1), pb = LearnerModel.Predict(p, "classify", "Bird", "Day", 1);
            Check($"fit: quads predicted high ({pq * 100f:F0} %), birds low ({pb * 100f:F0} %), near what was seen (90 / 30)", pq > 0.75f && pb < 0.45f && pq > pb + 0.3f);
            var weak = LearnerModel.Weakest(p, 3);
            Check("weakest cell is the bird", weak.Count > 0 && weak[0].cls == "Bird" && weak[0].seen == 40);
            Check("unseen factors keep their prior (night is harder than day)", LearnerModel.Predict(p, "classify", "HostileQuad", "Night", 1) < pq);

            // The plan aims at 75 %.
            var plan = LearnerModel.Plan(p);
            Check($"plan: predicted {plan.predicted * 100f:F0} % for {plan.focusClass} at {plan.light}, within 10 points of 75 %", Mathf.Abs(plan.predicted - 0.75f) < 0.10f && plan.text.Contains("target 75 %"));
            var cfg = new ScenarioConfig();
            int before = cfg.hostileCount + cfg.friendlyCount + cfg.civilianCount + cfg.birdCount;
            LearnerModel.Apply(plan, cfg);
            Check("applying the plan sets the light and range and adds two of the focus class", cfg.timeOfDay == plan.light && Mathf.Approximately(cfg.spawnRingRadius, plan.spawnRadius) && cfg.hostileCount + cfg.friendlyCount + cfg.civilianCount + cfg.birdCount == before + 2);

            // Pooling: a new soldier with no data starts from the cohort.
            var cohort = Pooled(p);
            var fresh = new LearnerProfile { player = "New" };
            LearnerModel.Fit(fresh, cohort);
            Check("a new soldier with no events starts from the pooled cohort weights", Mathf.Abs(fresh.Beta("class:Bird") - p.Beta("class:Bird")) < 1e-4f);

            // Caps and storage (temporary folder).
            var big = new LearnerProfile { player = "Cap" };
            var many = new List<LearnerObservation>();
            for (int i = 0; i < LearnerModel.MaxObservations + 50; i++) many.Add(new LearnerObservation { task = "detect", cls = "Bird", light = "Day", rangeBin = 0, correct = true });
            LearnerModel.Add(big, many);
            Check("events are capped (oldest dropped)", big.observations.Count == LearnerModel.MaxObservations);
            string dir = Path.Combine(Application.temporaryCachePath, "learner_selftest");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            LearnerModel.Save(p, dir);
            var back = LearnerModel.Load("Test", dir);
            Check("saved and loaded back with the same weights and events", back.observations.Count == p.observations.Count && Mathf.Approximately(back.Beta("class:Bird"), p.Beta("class:Bird")) && back.sessions == p.sessions);
            var updated = LearnerModel.Update("Other", s, new[] { quad, bird, wing }, rules, dir);
            Check("update after a run: pooled with the other soldier in the folder, saved, one session", updated.sessions == 1 && File.Exists(Path.Combine(dir, "Other.json")) && LearnerModel.LoadOthers("Other", dir).Count == 1);
            Directory.Delete(dir, true);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            LearnerModel.Fit(big, null);
            sw.Stop();
            Check($"a full refit on {LearnerModel.MaxObservations} events takes {sw.ElapsedMilliseconds} ms (under 1000 ms, once per run)", sw.ElapsedMilliseconds < 1000);

            string summary = $"[LearnerSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static Dictionary<string, float> Pooled(LearnerProfile p) => LearnerModel.Pooled(new List<LearnerProfile> { p });

        static ContactRecord Rec(int id, ContactClass truth, float inView, PlayerCall call, PlayerAction action, float range)
        {
            var r = new ContactRecord
            {
                id = id, truthClass = truth, spawnTime = 10f, firstDetectableTime = 10f, firstInViewTime = inView, firstLockTime = inView,
                call = call, callTime = inView + 1f, callRange = range, callLight = (int)TimeOfDay.Day,
                action = action, actionTime = action == PlayerAction.None ? -1f : inView + 2f, roeAtAction = RoeCard.WeaponsHold,
            };
            r.rangeTrack.Add(new Vector2(inView, range));
            return r;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
