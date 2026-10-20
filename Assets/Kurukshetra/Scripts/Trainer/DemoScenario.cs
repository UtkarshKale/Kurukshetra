using System;
using System.Collections.Generic;
using System.IO;
using Kurukshetra.Scenario;
using Kurukshetra.TerrainGen;

namespace Kurukshetra.Trainer
{
    // The one-click DEMO (TrainerMenu > DEMO): a curated 3.5-minute run that shows every part of the trainer in order, and the choice of
    // the best terrain already on this PC. Nothing here downloads anything.
    public static class DemoScenario
    {
        public const string Name = "DEMO - Kurukshetra counter-drone";

        // Timeline (seconds): contacts from 8 s to 120 s, bird flock about 35 s, swarm of 200 at 55 s, ROE card C to A at 90 s, end 210 s.
        public const float Duration = 210f, SwarmAt = 55f, RoeFlipAt = 90f;

        // realTerrain: a real area is in use (leave the site alone: the real buildings and trees are the site). Otherwise a rural site is built.
        public static ScenarioConfig Build(bool realTerrain)
        {
            var c = new ScenarioConfig
            {
                presetName = Name,
                seed = 2026,
                duration = Duration,
                // Hostile quads and fixed-wing, own ISR, civilian drones and single birds.
                hostileCount = 4, friendlyCount = 2, civilianCount = 2, birdCount = 3,
                blendInPercent = 25f,
                spawnWindowStart = 8f, spawnWindowEnd = 120f,
                // A swarm of about 200 and one flock of 40 birds.
                swarmSize = 200, swarmTime = SwarmAt, swarmTactic = SwarmTactic.Saturate,
                birdFlocks = 1, flockSize = 40,
                crowdCount = 0,
                iffAid = true,
                timeOfDay = TimeOfDay.Day, visibility = Visibility.Clear,
                // The card change the demo points at: C (report and track) to A (attack in progress) at 1:30.
                roeAtStart = RoeCard.CardC, roeFlip = true, roeFlipRandom = false, roeFlipTime = RoeFlipAt, roeFlipTo = RoeCard.CardA,
                site = realTerrain ? SiteType.None : SiteType.Rural,
                patternHostileQuad = FlightPattern.CustomPath,
            };
            // Two paths: hostile quads come in low over the left flank, the swarm over the right. Post at 0, 0, the spawn ring is 1 km.
            c.paths.Add(new FlightPath
            {
                name = "Quad run-in (left)", use = PathUse.Drone, cls = (int)ContactClass.HostileQuad, mode = PathMode.OneWay,
                points = new List<Waypoint>
                {
                    new Waypoint { x = -650f, z = 700f, alt = 90f },
                    new Waypoint { x = -250f, z = 420f, alt = 70f },
                    new Waypoint { x = -60f, z = 160f, alt = 40f },
                    new Waypoint { x = 0f, z = 30f, alt = 20f },
                },
            });
            c.paths.Add(new FlightPath
            {
                name = "Swarm approach (right)", use = PathUse.Swarm, mode = PathMode.OneWay,
                points = new List<Waypoint>
                {
                    new Waypoint { x = 500f, z = 850f, alt = 120f },
                    new Waypoint { x = 220f, z = 500f, alt = 90f },
                    new Waypoint { x = 60f, z = 180f, alt = 50f },
                    new Waypoint { x = 0f, z = 20f, alt = 30f },
                },
            });
            return c.Validate();
        }

        // The best real area already on this PC (project RealData folder, the build's KurukshetraData, or this PC's downloads), or null.
        // Score: map features (buildings, trees) matter most, then the satellite picture and the horizon surround; the Jammu air force
        // station is the project's main demo site; scratch areas ("Area", "test") come last. Returns the .raw path.
        public static string PickArea(out string why)
        {
            why = "";
            string best = null; int bestScore = int.MinValue;
            foreach (var raw in ElevationDownloader.DownloadedAreas())
            {
                string dir = Path.GetDirectoryName(raw), stem = Path.GetFileNameWithoutExtension(raw);
                int score = 0;
                if (File.Exists(Path.Combine(dir, stem + "_osm.json"))) score += 4;
                if (File.Exists(Path.Combine(dir, stem + "_satellite.jpg"))) score += 2;
                if (File.Exists(Path.Combine(dir, stem + "_surround.raw"))) score += 1;
                if (stem.StartsWith("Jammu", StringComparison.OrdinalIgnoreCase)) score += 3;
                if (stem.Equals("Area", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("test", StringComparison.OrdinalIgnoreCase)) score -= 10;
                if (score > bestScore) { bestScore = score; best = raw; why = $"{stem} (score {score})"; }
            }
            return best;
        }
    }
}
