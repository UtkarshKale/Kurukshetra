using System;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // A scripted scenario the trainer can pick: a named, fixed-seed run configuration.
    [CreateAssetMenu(menuName = "Chaukanna/Trainer Preset", fileName = "TrainerPreset")]
    public class TrainerPreset : ScriptableObject
    {
        public ScenarioConfig config = new ScenarioConfig();
    }

    // Limits for Randomize. Kept as data so a later difficulty system can drive them.
    [Serializable]
    public class RandomizeBounds
    {
        public Vector2Int hostiles = new Vector2Int(1, 6);
        [Header("Decoys")]
        public Vector2Int friendlies = new Vector2Int(0, 4);
        public Vector2Int civilians = new Vector2Int(0, 4);
        public Vector2Int birds = new Vector2Int(2, 12);

        [Header("Timing")]
        public Vector2 speedMultiplier = new Vector2(0.8f, 1.3f);
        [Tooltip("Last spawn happens at this fraction of the run (min, max).")]
        public Vector2 spawnEndFraction = new Vector2(0.6f, 0.85f);

        [Header("Sensors and ROE")]
        [Range(0f, 1f)] public float degradedChance = 0.25f;
        [Range(0f, 1f)] public float roeFlipChance = 0.4f;
        [Tooltip("When the ROE flips, as a fraction of the run (min, max).")]
        public Vector2 roeFlipFraction = new Vector2(0.3f, 0.7f);

        [Header("Conditions")]
        [Range(0f, 1f)] public float duskChance = 0.2f;
        [Range(0f, 1f)] public float nightChance = 0.2f;
        [Range(0f, 1f)] public float hazyChance = 0.25f;
        [Range(0f, 1f)] public float foggyChance = 0.1f;
    }

    public static class ScenarioRandomizer
    {
        // Same seed, bounds and base always give the same scenario. Duration, arc and ring radius are kept from the base;
        // approach bearings and spawn times follow from the seed inside the spawner.
        // A difficulty level, when given, supplies the degraded-sensor and ROE-flip chances instead of the bounds.
        public static ScenarioConfig Randomize(int seed, RandomizeBounds b, ScenarioConfig baseConfig, DifficultyProfile difficulty = null)
        {
            var rng = new System.Random(seed);
            var c = baseConfig != null ? baseConfig.Clone() : new ScenarioConfig();
            c.presetName = "Randomized";
            c.seed = seed;
            c.hostileCount = Int(rng, b.hostiles);
            c.friendlyCount = Int(rng, b.friendlies);
            c.civilianCount = Int(rng, b.civilians);
            c.birdCount = Int(rng, b.birds);
            c.speedMultiplier = Float(rng, b.speedMultiplier);
            c.spawnWindowStart = 3f;
            c.spawnWindowEnd = c.duration * Float(rng, b.spawnEndFraction);
            c.sensorsDegraded = rng.NextDouble() < (difficulty != null ? difficulty.sensorDegradedProbability : b.degradedChance);
            c.roeAtStart = RoeCard.CardB;
            c.roeFlip = rng.NextDouble() < (difficulty != null ? difficulty.roeFlipProbability : b.roeFlipChance);
            c.roeFlipTime = c.duration * Float(rng, b.roeFlipFraction);   // drawn even when unused, so later draws don't shift
            c.roeFlipTo = RoeCard.CardA;

            // Conditions are drawn last so the values above stay the same for a seed as they were before conditions existed.
            double time = rng.NextDouble(), air = rng.NextDouble();
            c.timeOfDay = time < b.nightChance ? TimeOfDay.Night : time < b.nightChance + b.duskChance ? TimeOfDay.Dusk : TimeOfDay.Day;
            c.visibility = air < b.foggyChance ? Visibility.Foggy : air < b.foggyChance + b.hazyChance ? Visibility.Hazy : Visibility.Clear;
            c.detectionSizeMultiplier = 1f;
            c.difficulty = "";
            return c.Validate();
        }

        static int Int(System.Random rng, Vector2Int range)
        {
            int lo = Mathf.Min(range.x, range.y), hi = Mathf.Max(range.x, range.y);
            return rng.Next(lo, hi + 1);
        }

        static float Float(System.Random rng, Vector2 range)
        {
            return Mathf.Lerp(range.x, range.y, (float)rng.NextDouble());
        }
    }
}
