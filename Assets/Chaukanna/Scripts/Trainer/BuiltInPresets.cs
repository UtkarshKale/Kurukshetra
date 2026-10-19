using System.Collections.Generic;
using Chaukanna.Scenario;

namespace Chaukanna.Trainer
{
    // Scenarios defined in code (no scene or asset edits), offered after the asset presets in the in-field trainer panel and
    // the TrainerMenu. The dense-sky pair: a crowd with a few hostiles hiding in it, and the extreme mixed-sky swarm.
    public static class BuiltInPresets
    {
        public const string CrowdedSky = "Crowded Sky - Find the Foe";
        public const string ExtremeSwarm = "Extreme - Mixed Sky Swarm";

        public static List<ScenarioConfig> All()
        {
            return new List<ScenarioConfig> { Crowded(), Extreme() };
        }

        // Day, clear, 1500 cooperative tracks; 3 hostiles (quad and fixed-wing) hide in them. Card C, flipping to B at a seeded time.
        public static ScenarioConfig Crowded()
        {
            return new ScenarioConfig
            {
                presetName = CrowdedSky,
                seed = 4242,
                duration = 420f,
                hostileCount = 3, friendlyCount = 0, civilianCount = 0, birdCount = 0,      // the crowd supplies the decoys
                spawnWindowStart = 30f, spawnWindowEnd = 300f,
                crowdCount = 1500,
                iffAid = true, friendlyNoIffPercent = 5f, hostileSpoofPercent = 0f,
                timeOfDay = TimeOfDay.Day, visibility = Visibility.Clear,
                roeAtStart = RoeCard.CardC, roeFlip = true, roeFlipRandom = true, roeFlipTo = RoeCard.CardB,
                site = SiteType.Rural,
            }.Validate();
        }

        // Dusk, hazy, sensors degraded, 4000 tracks, 4 hostiles half of them posing as own drones, a 48-drone swarm at 150 s,
        // 4 bird flocks of 20. Card C to A at a random time. Plays on the Extreme difficulty level.
        public static ScenarioConfig Extreme()
        {
            return new ScenarioConfig
            {
                presetName = ExtremeSwarm,
                seed = 9001,
                duration = 600f,
                hostileCount = 4, friendlyCount = 0, civilianCount = 0, birdCount = 0,
                blendInPercent = 50f,
                spawnWindowStart = 30f, spawnWindowEnd = 420f,
                swarmSize = 48, swarmTime = 150f,
                birdFlocks = 4, flockSize = 20,
                crowdCount = 4000,
                iffAid = true, friendlyNoIffPercent = 12f, hostileSpoofPercent = 30f,
                timeOfDay = TimeOfDay.Dusk, visibility = Visibility.Hazy, sensorsDegraded = true,
                roeAtStart = RoeCard.CardC, roeFlip = true, roeFlipRandom = true, roeFlipTo = RoeCard.CardA,
                site = SiteType.Urban,
                difficulty = DifficultyProfile.ExtremeName,
            }.Validate();
        }

        // Index of the difficulty level a built-in preset wants, in a trainer's list; -1 = leave the level alone.
        public static int DifficultyIndexFor(ScenarioConfig c, IList<DifficultyProfile> levels)
        {
            if (c == null || string.IsNullOrEmpty(c.difficulty) || levels == null) return -1;
            for (int i = 0; i < levels.Count; i++) if (levels[i] != null && levels[i].displayName == c.difficulty) return i;
            return -1;
        }
    }
}
