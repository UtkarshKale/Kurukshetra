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
