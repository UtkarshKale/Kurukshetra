using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // Checks for Randomize and config validation. Run via the CLI: Chaukanna.Trainer.TrainerSelfTest.Run()
    public static class TrainerSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var bounds = new RandomizeBounds();
            var baseConfig = new ScenarioConfig();

            // Same seed, same scenario.
            bool same = true;
            for (int seed = 1; seed <= 200; seed++)
                if (JsonUtility.ToJson(ScenarioRandomizer.Randomize(seed, bounds, baseConfig)) != JsonUtility.ToJson(ScenarioRandomizer.Randomize(seed, bounds, baseConfig))) same = false;
            Check("same seed gives the same scenario", same);

            // Different seeds vary, and every value stays inside its bounds.
            var seen = new HashSet<string>();
            bool inBounds = true, baseKept = true;
            int degraded = 0, flips = 0;
            const int n = 2000;
            for (int seed = 1; seed <= n; seed++)
            {
                var c = ScenarioRandomizer.Randomize(seed, bounds, baseConfig);
                seen.Add($"{c.hostileCount}/{c.friendlyCount}/{c.civilianCount}/{c.birdCount}/{c.sensorsDegraded}/{c.roeFlip}");
                if (!In(c.hostileCount, bounds.hostiles) || !In(c.friendlyCount, bounds.friendlies) || !In(c.civilianCount, bounds.civilians) || !In(c.birdCount, bounds.birds)) inBounds = false;
                if (c.speedMultiplier < bounds.speedMultiplier.x || c.speedMultiplier > bounds.speedMultiplier.y) inBounds = false;
                if (c.spawnWindowEnd < c.duration * bounds.spawnEndFraction.x - 0.01f || c.spawnWindowEnd > c.duration * bounds.spawnEndFraction.y + 0.01f) inBounds = false;
                if (c.roeFlipTime < c.duration * bounds.roeFlipFraction.x - 0.01f || c.roeFlipTime > c.duration * bounds.roeFlipFraction.y + 0.01f) inBounds = false;
                if (c.duration != baseConfig.duration || c.spawnRingRadius != baseConfig.spawnRingRadius || c.sectorArcDegrees != baseConfig.sectorArcDegrees || c.seed != seed) baseKept = false;
                if (c.sensorsDegraded) degraded++;
                if (c.roeFlip) flips++;
            }
            Check("different seeds give varied scenarios", seen.Count > 200);
            Check("counts, speed, spawn window and flip time stay in bounds", inBounds);
            Check("duration, ring, arc and seed are kept", baseKept);
            Check($"about 25 % degraded (got {100f * degraded / n:F1} %)", Mathf.Abs(degraded / (float)n - bounds.degradedChance) < 0.04f);
            Check($"about 40 % ROE flips (got {100f * flips / n:F1} %)", Mathf.Abs(flips / (float)n - bounds.roeFlipChance) < 0.04f);
            Check("always at least one hostile by default", bounds.hostiles.x >= 1);

            // Validation clamps silly values.
            var bad = new ScenarioConfig { duration = 5f, hostileCount = -3, birdCount = 9999, spawnRingRadius = 10f, spawnWindowStart = 500f, spawnWindowEnd = 1f, roeFlipTime = 9999f }.Validate();
            Check("validate clamps out-of-range values",
                bad.duration == 30f && bad.hostileCount == 0 && bad.birdCount == 100 && bad.spawnRingRadius == 200f
                && bad.spawnWindowStart <= bad.duration && bad.spawnWindowEnd >= bad.spawnWindowStart && bad.roeFlipTime <= bad.duration);

            // Conditions from Randomize: every time of day and visibility turns up, roughly at the configured rates.
            int nights = 0, dusks = 0, foggy = 0, hazy = 0;
            for (int seed = 1; seed <= n; seed++)
            {
                var c = ScenarioRandomizer.Randomize(seed, bounds, baseConfig);
                if (c.timeOfDay == TimeOfDay.Night) nights++; else if (c.timeOfDay == TimeOfDay.Dusk) dusks++;
                if (c.visibility == Visibility.Foggy) foggy++; else if (c.visibility == Visibility.Hazy) hazy++;
            }
            Check($"randomize conditions near their chances (night {100f * nights / n:F0}%, dusk {100f * dusks / n:F0}%, hazy {100f * hazy / n:F0}%, foggy {100f * foggy / n:F0}%)",
                Mathf.Abs(nights / (float)n - bounds.nightChance) < 0.04f && Mathf.Abs(dusks / (float)n - bounds.duskChance) < 0.04f
                && Mathf.Abs(hazy / (float)n - bounds.hazyChance) < 0.04f && Mathf.Abs(foggy / (float)n - bounds.foggyChance) < 0.04f);

            // Difficulty levels.
            var trained = ScriptableObject.CreateInstance<DifficultyProfile>();
            var expert = ScriptableObject.CreateInstance<DifficultyProfile>();
            expert.displayName = "Expert"; expert.contactCountMultiplier = 1.4f; expert.decoyShare = 1.5f; expert.speedMultiplier = 1.25f;
            expert.extraHazeLevels = 1; expert.minVisibleSizeMultiplier = 1.25f; expert.aiErrorRateOk = 0.25f; expert.aiErrorRateDegraded = 0.45f;
            expert.sensorDegradedProbability = 1f; expert.roeFlipProbability = 0f;
            var source = new ScenarioConfig { hostileCount = 3, friendlyCount = 2, civilianCount = 1, birdCount = 0, visibility = Visibility.Hazy };

            var unchanged = trained.Apply(source);
            Check("default level changes nothing but the name and AI rate",
                unchanged.hostileCount == 3 && unchanged.friendlyCount == 2 && unchanged.civilianCount == 1 && unchanged.birdCount == 0 && unchanged.speedMultiplier == 1f
                && unchanged.visibility == Visibility.Hazy && unchanged.detectionSizeMultiplier == 1f && unchanged.difficulty == "Trained" && Mathf.Approximately(unchanged.aiErrorOverride, 0.15f));

            var hard = expert.Apply(source);
            Check("expert scales counts: 3 hostile -> 4, 2 own -> 4, 1 civilian -> 2, 0 birds stay 0",
                hard.hostileCount == 4 && hard.friendlyCount == 4 && hard.civilianCount == 2 && hard.birdCount == 0);
            Check("expert is faster, hazier and harder to spot",
                Mathf.Approximately(hard.speedMultiplier, 1.25f) && hard.visibility == Visibility.Foggy && Mathf.Approximately(hard.detectionSizeMultiplier, 1.25f) && Mathf.Approximately(hard.aiErrorOverride, 0.25f));
            Check("applying a level leaves the source config alone", source.hostileCount == 3 && source.visibility == Visibility.Hazy && source.difficulty == "");
            source.sensorsDegraded = true;
            Check("degraded sensors use the level's degraded AI rate", Mathf.Approximately(expert.Apply(source).aiErrorOverride, 0.45f));
            source.visibility = Visibility.Foggy;
            Check("visibility cannot get worse than foggy", expert.Apply(source).visibility == Visibility.Foggy);

            // Randomize takes its sensor and ROE chances from the level.
            bool allDegraded = true, noFlips = true;
            for (int seed = 1; seed <= 200; seed++)
            {
                var c = ScenarioRandomizer.Randomize(seed, bounds, baseConfig, expert);
                if (!c.sensorsDegraded) allDegraded = false;
                if (c.roeFlip) noFlips = false;
            }
            Check("randomize uses the level's degraded and flip chances", allDegraded && noFlips);
            Object.DestroyImmediate(trained);
            Object.DestroyImmediate(expert);

            // Recommendation rule: strictly over 80 = harder, strictly under 50 = easier, boundaries stay.
            Check("81 % recommends one level harder", DifficultyAdvisor.Recommend(1, 3, 81f) == 2);
            Check("exactly 80 % stays", DifficultyAdvisor.Recommend(1, 3, 80f) == 1);
            Check("65 % stays", DifficultyAdvisor.Recommend(1, 3, 65f) == 1);
            Check("exactly 50 % stays", DifficultyAdvisor.Recommend(1, 3, 50f) == 1);
            Check("49 % recommends one level easier", DifficultyAdvisor.Recommend(1, 3, 49f) == 0);
            Check("cannot go above the hardest or below the easiest", DifficultyAdvisor.Recommend(2, 3, 95f) == 2 && DifficultyAdvisor.Recommend(0, 3, 10f) == 0);
            Check("thresholds are parameters", DifficultyAdvisor.Recommend(1, 3, 75f, 70f, 40f) == 2);

            // Clone is independent.
            var a = new ScenarioConfig();
            var b = a.Clone();
            b.hostileCount = 42;
            Check("clone does not share state", a.hostileCount != 42);

            string summary = $"[TrainerSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static bool In(int v, Vector2Int range)
        {
            return v >= range.x && v <= range.y;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
