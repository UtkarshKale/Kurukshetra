using System.Collections.Generic;
using System.IO;
using Kurukshetra.Scenario;
using Kurukshetra.Scoring;
using UnityEngine;

namespace Kurukshetra.Trainer
{
    // Checks for modules 10-12: the Director's decisions, the scenario form's validator and expander, packages on disk,
    // sign-in, and the proficiency board (only reference packages, intervals, not-yet-trained, duplicate timing).
    // Uses temporary folders only. CLI: Kurukshetra.Trainer.PackageSelfTest.Run()
    public static class PackageSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();

            // --- Director (module 10).
            var good = new Director.Assessment { recentCalls = 5, recentRight = 5 };
            var bad = new Director.Assessment { recentCalls = 6, recentRight = 2 };
            var leak = new Director.Assessment { recentCalls = 2, recentRight = 2, leaks = 1 };
            var sandbag = new Director.Assessment { recentCalls = 6, recentRight = 2, fastWrong = 3 };
            Check("Director: doing well pushes harder, struggling eases off, a leak eases off",
                Director.Decide(good, true) == 1 && Director.Decide(bad, true) == -1 && Director.Decide(leak, true) == -1);
            Check("Director: no easing off once the cap is reached, or when wrong calls are suspiciously fast (sandbagging)",
                Director.Decide(bad, false) == 0 && Director.Decide(sandbag, true) == 0);
            var recs = new List<ContactRecord>
            {
                new ContactRecord { id = 1, truthClass = ContactClass.Bird, call = PlayerCall.HostileQuad, callTime = 95f, firstLockTime = 94.8f },
                new ContactRecord { id = 2, truthClass = ContactClass.HostileQuad, call = PlayerCall.HostileQuad, callTime = 90f, firstLockTime = 85f },
                new ContactRecord { id = 3, truthClass = ContactClass.HostileQuad, despawnReason = "ReachedDefendedPoint", despawnTime = 80f },
                new ContactRecord { id = 4, truthClass = ContactClass.Bird, call = PlayerCall.Bird, callTime = 10f },
            };
            var a = Director.Assess(recs, null, 100f, 60f, 0.6f);
            Check("Director reads ground truth over the last 60 s: 2 calls, 1 right, 1 leak, 1 very fast wrong call", a.recentCalls == 2 && a.recentRight == 1 && a.leaks == 1 && a.fastWrong == 1);

            // --- Validator and expander (module 11).
            var spec = new ScenarioSpec { name = "Night strike 1", durationMinutes = 10, light = TimeOfDay.Night, roeStart = RoeCard.CardB, roeChangeSecond = 300, roeAfter = RoeCard.CardA, hostiles = 3, birds = 6 };
            var issues = SpecValidator.Validate(spec);
            Check("a sensible package is valid (only the 'not checked yet' notes V5 and V10)", SpecValidator.Ok(issues) && issues.TrueForAll(i => !i.blocking && (i.code == "V5" || i.code == "V10")));
            Check("V1: 60 hostiles is out of bounds", Has(new ScenarioSpec { hostiles = 60 }, "V1"));
            Check("V2: more than 60 objects in total", Has(new ScenarioSpec { hostiles = 20, birds = 30, civilDrones = 15 }, "V2"));
            Check("V3: a card change to the same card", Has(new ScenarioSpec { roeChangeSecond = 60, roeStart = RoeCard.CardA, roeAfter = RoeCard.CardA }, "V3"));
            Check("V4: a swarm arriving in the last minute", Has(new ScenarioSpec { durationMinutes = 2, swarmSize = 8, swarmSecond = 100 }, "V4"));
            Check("V6: night, fog and degraded sensors", Has(new ScenarioSpec { light = TimeOfDay.Night, weather = Visibility.Foggy, sensorsDegraded = true }, "V6"));
            Check("V7 (warning only): low prevalence over 3 contacts", HasWarning(new ScenarioSpec { prevalence = Prevalence.Low, prevalenceTotal = 3 }, "V7"));
            Check("V8: a Quiet watch with 6 contacts per hour", Has(new ScenarioSpec { quietWatch = true, contactsPerHour = 6 }, "V8"));
            Check("V9: a reference package with the Director on, or without a fairness id", Has(new ScenarioSpec { reference = true, director = true, fairnessId = "F1" }, "V9") && Has(new ScenarioSpec { reference = true }, "V9"));
            var cfg = SpecExpander.Expand(spec);
            Check("expander: 10 min, night, Card B then A at 300 s, counts as typed",
                Mathf.Approximately(cfg.duration, 600f) && cfg.timeOfDay == TimeOfDay.Night && cfg.roeAtStart == RoeCard.CardB && cfg.roeFlip && Mathf.Approximately(cfg.roeFlipTime, 300f) && cfg.roeFlipTo == RoeCard.CardA && cfg.hostileCount == 3 && cfg.birdCount == 6 && cfg.packageId == "Night strike 1");
            Check("expander: a reference package always has the Director off", !SpecExpander.Expand(new ScenarioSpec { reference = true, director = true, fairnessId = "F" }).director);
            Check("the same spec always expands to the same config", JsonUtility.ToJson(SpecExpander.Expand(spec)) == JsonUtility.ToJson(SpecExpander.Expand(spec.Clone())));

            string tmp = Path.Combine(Application.temporaryCachePath, "package_selftest");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            PackageStore.Save(spec, tmp);
            PackageStore.Save(new ScenarioSpec { name = "Quiet watch 1", quietWatch = true, reference = true, fairnessId = "F1" }, tmp);
            var loaded = PackageStore.LoadAll(tmp);
            Check("packages save and load back, sorted by name", loaded.Count == 2 && loaded[0].name == "Night strike 1" && loaded[1].reference && loaded[0].roeChangeSecond == 300);
            Check("a package name cannot make a path (no folders, no dots)", PackageStore.FileName("../x/y") == "___x_y.json");

            // --- Sign-in (module 12).
            Check("register a pseudonymous ID with a PIN", Login.SignIn("S-014", "4321", true, tmp) == "");
            Check("sign in with the right PIN; the wrong PIN and unknown IDs are refused", Login.SignIn("S-014", "4321", false, tmp) == "" && Login.SignIn("S-014", "1111", false, tmp).Contains("Wrong") && Login.SignIn("S-099", "4321", false, tmp).Contains("No soldier"));
            Check("the roster keeps only the ID, a salt and a hash (no PIN, no name)", !File.ReadAllText(Path.Combine(tmp, "roster.json")).Contains("4321"));
            Check("ID and PIN rules: a PIN needs 4-8 characters", Login.SignIn("S-015", "12", true, tmp).Contains("PIN"));

            // --- Board.
            var sessions = new List<SessionResult>
            {
                Session("S-014", "Night strike 1", true, false, 60, 100, "2026-10-15 10:00:00", 1f),
                Session("S-014", "Night strike 1", true, false, 80, 100, "2026-10-15 11:00:00", 1f),
                Session("S-014", "Night strike 1", true, true, 99, 100, "2026-10-15 11:30:00", 1f),    // Director on: not counted
                Session("S-020", "Night strike 1", false, false, 90, 100, "2026-10-15 12:00:00", 1f),  // not a reference package
                Session("S-020", "Night strike 1", true, false, 50, 100, "2026-10-15 12:05:00", 1f),
            };
            var cells = ProficiencyBoard.Cells(sessions);
            var c14 = cells.Find(c => c.player == "S-014");
            Check("board counts only reference packages with the Director off (S-014: 2 sessions, S-020: 1)", cells.Count == 2 && c14.n == 2 && cells.Find(c => c.player == "S-020").n == 1);
            Check($"board: mean 70 %, latest 80 %, interval shown from n = 2 ({c14.low:F0}-{c14.high:F0}); n = 1 has none", Mathf.Approximately(c14.mean, 70f) && Mathf.Approximately(c14.latest, 80f) && c14.low >= 0f && c14.low < 70f && c14.high > 70f && cells.Find(c => c.player == "S-020").low < 0f);
            var notYet = ProficiencyBoard.NotYetTrained(new[] { "S-014", "S-020", "S-030" }, new[] { "Night strike 1", "Quiet watch 1" }, cells);
            Check("not yet trained: S-030 on both, and the other two on Quiet watch 1", notYet.Count == 4 && notYet.Contains("S-030: Night strike 1"));
            Check("KS: identical samples give p near 1, very different ones near 0",
                ProficiencyBoard.KsP(new List<float> { 1, 2, 3, 4, 5, 6 }, new List<float> { 1, 2, 3, 4, 5, 6 }) > 0.9f && ProficiencyBoard.KsP(new List<float> { 1, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f }, new List<float> { 9, 9.1f, 9.2f, 9.3f, 9.4f, 9.5f }) < 0.05f);
            var twins = new List<SessionResult> { Session("S-030", "P", true, false, 50, 100, "2026-10-15 13:00:00", 2f), Session("S-031", "P", true, false, 50, 100, "2026-10-15 13:20:00", 2f) };
            var apart = new List<SessionResult> { Session("S-030", "P", true, false, 50, 100, "2026-10-15 13:00:00", 2f), Session("S-031", "P", true, false, 50, 100, "2026-10-15 13:20:00", 9f) };
            Check("duplicate timing: two IDs back to back with the same timing are flagged, different timing is not", ProficiencyBoard.DuplicateTiming(twins).Count == 1 && ProficiencyBoard.DuplicateTiming(apart).Count == 0);
            string html = ProficiencyBoard.Html(cells, notYet, ProficiencyBoard.DuplicateTiming(twins));
            Check("board HTML carries 'Training use only. Not for ACRs or selection.' twice", html.IndexOf(ProficiencyBoard.Disclaimer) != html.LastIndexOf(ProficiencyBoard.Disclaimer));
            Directory.Delete(tmp, true);

            string summary = $"[PackageSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static SessionResult Session(string player, string package, bool reference, bool director, int points, int max, string when, float timing)
        {
            var s = new SessionResult { playerId = player, packageId = package, referencePackage = reference, directorOn = director, totalPoints = points, maxPoints = max, timestamp = when, processScore = 80f };
            for (int i = 0; i < 8; i++) s.contacts.Add(new ContactResult { decisionLatency = timing + 0.1f * i });
            return s;
        }

        static bool Has(ScenarioSpec s, string code) => SpecValidator.Validate(s).Exists(i => i.code == code && i.blocking);
        static bool HasWarning(ScenarioSpec s, string code) => SpecValidator.Validate(s).Exists(i => i.code == code && !i.blocking);

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
