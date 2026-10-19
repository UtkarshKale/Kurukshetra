using Chaukanna.Environment;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // One difficulty level. Applied on top of whatever preset or randomized scenario the trainer picked.
    [CreateAssetMenu(menuName = "Chaukanna/Difficulty Profile", fileName = "DifficultyProfile")]
    public class DifficultyProfile : ScriptableObject
    {
        public string displayName = "Trained";

        [Header("Contacts")]
        [Tooltip("Multiplies every contact count.")]
        public float contactCountMultiplier = 1f;
        [Tooltip("Extra multiplier on the decoys only (own ISR, civilian, birds). Above 1 = more clutter per hostile.")]
        public float decoyShare = 1f;
        public float speedMultiplier = 1f;
        [Tooltip("Share of the hostiles (percent) that blend in by flying an own-drone patrol before breaking off. Used when the scenario leaves it at -1.")]
        [Range(0f, 100f)] public float blendInPercent;

        [Header("Seeing")]
        [Tooltip("Visibility is made this many levels worse (Clear > Hazy > Foggy).")]
        [Range(0, 2)] public int extraHazeLevels;
        [Tooltip("Multiplies the smallest apparent size that can be picked out. Above 1 = harder to spot.")]
        public float minVisibleSizeMultiplier = 1f;

        [Header("Sensors")]
        [Range(0f, 1f)] public float aiErrorRateOk = 0.15f;
        [Range(0f, 1f)] public float aiErrorRateDegraded = 0.35f;

        [Header("Used by Randomize")]
        [Range(0f, 1f)] public float sensorDegradedProbability = 0.25f;
        [Range(0f, 1f)] public float roeFlipProbability = 0.4f;

        public const string ExtremeName = "Extreme";

        // The Extreme level, built in code with the same numbers as Data/Difficulty/Difficulty_Extreme.asset. The trainer lists are
        // serialized in the scenes (which stay untouched), so Extreme is added to them at run time when they have no such entry.
        public static DifficultyProfile Extreme()
        {
            var p = CreateInstance<DifficultyProfile>();
            p.name = "Difficulty_Extreme";
            p.displayName = ExtremeName;
            p.contactCountMultiplier = 1.5f;
            p.speedMultiplier = 1.25f;
            p.blendInPercent = 50f;
            p.extraHazeLevels = 1;
            p.minVisibleSizeMultiplier = 1.25f;
            p.aiErrorRateOk = 0.30f;
            p.aiErrorRateDegraded = 0.50f;
            p.sensorDegradedProbability = 0.6f;
            p.roeFlipProbability = 0.8f;
            return p;
        }

        // Appends Extreme to a trainer's list unless it already has a level of that name. Returns true when it was added.
        public static bool EnsureExtreme(System.Collections.Generic.List<DifficultyProfile> levels)
        {
            if (levels == null) return false;
            foreach (var d in levels) if (d != null && d.displayName == ExtremeName) return false;
            DifficultyProfile asset = null;
#if UNITY_EDITOR
            asset = UnityEditor.AssetDatabase.LoadAssetAtPath<DifficultyProfile>("Assets/Chaukanna/Data/Difficulty/Difficulty_Extreme.asset");
#endif
            levels.Add(asset != null ? asset : Extreme());
            return true;
        }

        // Returns a new config; the one passed in is left alone so levels never stack.
        public ScenarioConfig Apply(ScenarioConfig source)
        {
            var c = source.Clone();
            c.difficulty = displayName;
            c.hostileCount = Scale(c.hostileCount, contactCountMultiplier);
            c.friendlyCount = Scale(c.friendlyCount, contactCountMultiplier * decoyShare);
            c.civilianCount = Scale(c.civilianCount, contactCountMultiplier * decoyShare);
            c.birdCount = Scale(c.birdCount, contactCountMultiplier * decoyShare);
            c.speedMultiplier *= speedMultiplier;
            if (c.blendInPercent < 0f) c.blendInPercent = blendInPercent;      // the trainer's own number wins
            c.visibility = EnvironmentMath.Worsen(c.visibility, extraHazeLevels);
            c.detectionSizeMultiplier *= minVisibleSizeMultiplier;
            c.aiErrorOverride = c.sensorsDegraded ? aiErrorRateDegraded : aiErrorRateOk;
            return c.Validate();
        }

        // A class that was present stays present: 1 of something never rounds down to 0.
        static int Scale(int count, float multiplier)
        {
            return count <= 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(count * multiplier));
        }
    }

    // The one auto-adjust rule: it only ever suggests; the trainer applies it.
    public static class DifficultyAdvisor
    {
        // scorePercent = total points / best possible points for the scored contacts (8 each), clamped to 0-100.
        // Strictly above harderAbove = one level harder; strictly below easierBelow = one easier; otherwise the same.
        public static int Recommend(int currentLevel, int levelCount, float scorePercent, float harderAbove = 80f, float easierBelow = 50f)
        {
            if (levelCount <= 0) return 0;
            int next = currentLevel;
            if (scorePercent > harderAbove) next++;
            else if (scorePercent < easierBelow) next--;
            return Mathf.Clamp(next, 0, levelCount - 1);
        }
    }
}
