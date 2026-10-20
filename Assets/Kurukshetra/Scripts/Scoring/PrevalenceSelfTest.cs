using System.Collections.Generic;
using Kurukshetra.Scenario;
using UnityEngine;

namespace Kurukshetra.Scoring
{
    // Checks for module 5: prevalence bands, null missions, the Quiet watch set-up, realised share (doc V7) and vigilance.
    // CLI: Kurukshetra.Scoring.PrevalenceSelfTest.Run()
    public static class PrevalenceSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();

            // Prevalence bands over many seeds: the counts always add up, and the hostile share averages near the band centre.
            foreach (var p in new[] { Prevalence.Low, Prevalence.Medium, Prevalence.High })
            {
                int hostiles = 0, total = 0;
                bool sums = true;
                for (int seed = 1; seed <= 300; seed++)
                {
                    var c = new ScenarioConfig { seed = seed, prevalence = p, prevalenceTotal = 12, friendlyCount = 2, civilianCount = 2, birdCount = 6 }.ApplyPrevalence();
                    int n = c.hostileCount + c.friendlyCount + c.civilianCount + c.birdCount;
                    if (n != 12) sums = false;
                    hostiles += c.hostileCount; total += n;
                }
                float share = (float)hostiles / total;
                Check($"{p}: counts always add up to 12; mean hostile share {share * 100f:F1} % (target {ScenarioConfig.Share(p) * 100f:F0} %)", sums && Mathf.Abs(share - ScenarioConfig.Share(p)) < 0.02f);
            }
            var same1 = new ScenarioConfig { seed = 77, prevalence = Prevalence.Low }.ApplyPrevalence();
            var same2 = new ScenarioConfig { seed = 77, prevalence = Prevalence.Low }.ApplyPrevalence();
            Check("same seed, same counts", same1.hostileCount == same2.hostileCount && same1.birdCount == same2.birdCount);
            var custom = new ScenarioConfig { seed = 5, hostileCount = 4, birdCount = 3 }.ApplyPrevalence();
            Check("Custom leaves the typed counts alone and has no null mission", custom.hostileCount == 4 && custom.birdCount == 3 && !custom.nullMission);
            var decoyMix = new ScenarioConfig { seed = 3, prevalence = Prevalence.High, prevalenceTotal = 20, friendlyCount = 0, civilianCount = 0, birdCount = 5 }.ApplyPrevalence();
            Check("the decoys keep the trainer's mix (birds only stays birds only)", decoyMix.friendlyCount == 0 && decoyMix.civilianCount == 0 && decoyMix.birdCount == 20 - decoyMix.hostileCount);

            // Quiet watch: 45 min, 3 per hour.
            var qw = new ScenarioConfig { seed = 11, quietWatch = true, watchMinutes = 45f, contactsPerHour = 3f, swarmSize = 24, birdFlocks = 2 }.ApplyPrevalence();
            int qn = qw.hostileCount + qw.friendlyCount + qw.civilianCount + qw.birdCount;
            Check("Quiet watch: 45 min long, 2 contacts (3 per hour), spread over the watch, no swarm or flocks, low prevalence",
                Mathf.Approximately(qw.duration, 2700f) && qn == 2 && qw.spawnWindowStart <= 60f && qw.spawnWindowEnd >= 2700f - 120f && qw.swarmSize == 0 && qw.birdFlocks == 0 && qw.prevalence == Prevalence.Low);
            int nulls = 0;
            for (int seed = 1; seed <= 400; seed++) if (new ScenarioConfig { seed = seed, quietWatch = true }.ApplyPrevalence().nullMission) nulls++;
            Check($"Quiet watch null missions: {nulls} of 400 (about 20 %), and a null mission has no hostile", nulls > 50 && nulls < 110 && NullHasNoHostile());
            Check("too many contacts per hour for a Quiet watch gets a warning", new ScenarioConfig { quietWatch = true, contactsPerHour = 6f }.Warning().Contains("at most 4"));

            // Realised share against the band (doc V7).
            var rules = RuleSet.Defaults();
            var records = new List<ContactRecord>();
            for (int i = 0; i < 12; i++) records.Add(new ContactRecord { id = i + 1, truthClass = i < 9 ? ContactClass.HostileQuad : ContactClass.Bird, spawnTime = 10f, firstDetectableTime = 10f, firstInViewTime = 12f, firstLockTime = 13f });
            var s = ScoreCalculator.Calculate(records, rules, 300f);
            ScoreCalculator.StampPrevalence(s, Prevalence.Low, false, false);
            Check("9 hostiles of 12 under Low prevalence is flagged (realised 75 %)", Mathf.Approximately(s.realisedHostileShare, 0.75f) && s.prevalenceNote.Contains("realised 75 %"));
            s.realisedHostileShare = 0.6f;
            ScoreCalculator.StampPrevalence(s, Prevalence.High, false, false);
            Check("a 60 % share under High prevalence is inside the band, so no note", s.prevalenceNote.Length == 0);

            // Vigilance thirds over a 900 s watch: everything seen early, nothing late.
            var watch = new List<ContactRecord>();
            for (int i = 0; i < 6; i++)
            {
                float t = 50f + i * 150f;           // 50, 200 | 350, 500 | 650, 800
                bool early = i < 4;
                watch.Add(new ContactRecord { id = i + 1, truthClass = ContactClass.Bird, spawnTime = t, firstDetectableTime = t, firstInViewTime = early ? t + 2f : -1f, firstLockTime = early ? t + 3f + i : -1f });
            }
            var v = ScoreCalculator.Calculate(watch, rules, 900f).vigilance;
            Check($"vigilance: 2 contacts per third, hits 100 / 100 / 0 %, change -100 points ({v.summary})",
                v.contacts[0] == 2 && v.contacts[1] == 2 && v.contacts[2] == 2 && Mathf.Approximately(v.hitRate[0], 1f) && Mathf.Approximately(v.hitRate[2], 0f) && v.enough && Mathf.Approximately(v.hitChange, -1f));
            var few = ScoreCalculator.Calculate(watch.GetRange(0, 2), rules, 900f).vigilance;
            Check("with too few contacts per third it says so instead of a trend", !few.enough && few.summary.Contains("too few"));

            string summary = $"[PrevalenceSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static bool NullHasNoHostile()
        {
            for (int seed = 1; seed <= 400; seed++)
            {
                var c = new ScenarioConfig { seed = seed, quietWatch = true, contactsPerHour = 4f }.ApplyPrevalence();
                if (c.nullMission) return c.hostileCount == 0 && c.swarmSize == 0;
            }
            return false;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
