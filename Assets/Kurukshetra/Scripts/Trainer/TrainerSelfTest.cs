using System.Collections.Generic;
using Kurukshetra.Scenario;
using UnityEngine;

namespace Kurukshetra.Trainer
{
    // Checks for Randomize and config validation. Run via the CLI: Kurukshetra.Trainer.TrainerSelfTest.Run()
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
            Check("validate fixes impossible values and has no upper limit on counts",
                bad.duration == 5f && bad.hostileCount == 0 && bad.birdCount == 9999 && bad.spawnRingRadius == 10f
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

            // No caps: whatever the trainer types is used; large values only raise a warning.
            var big = new ScenarioConfig { hostileCount = 400, friendlyCount = 250, civilianCount = 120, birdCount = 5000, duration = 20000f, spawnRingRadius = 9000f, spawnWindowEnd = 20000f, moveRadius = 50000f, walkSpeed = 40f, runSpeed = 90f, speedMultiplier = 12f }.Validate();
            Check("huge values pass through unchanged",
                big.hostileCount == 400 && big.friendlyCount == 250 && big.civilianCount == 120 && big.birdCount == 5000 && big.duration == 20000f
                && big.spawnRingRadius == 9000f && big.moveRadius == 50000f && big.walkSpeed == 40f && big.runSpeed == 90f && big.speedMultiplier == 12f);
            Check("a very large run warns instead of clamping", big.Warning().Length > 0);
            Check("a normal run has no warning", new ScenarioConfig().Validate().Warning().Length == 0);
            Check("a stepper with no maximum accepts a million", NumberEntry.Parse("1000000", 6, 0, int.MaxValue) == 1000000);

            // Typed numbers: valid text applies, out-of-range text clamps, anything else keeps the old value.
            Check("typed '120' applies", NumberEntry.Parse("120", 300, 30, 1800) == 120);
            Check("typed ' 45 ' is trimmed", NumberEntry.Parse(" 45 ", 300, 30, 1800) == 45);
            Check("typed '5' clamps up to the minimum", NumberEntry.Parse("5", 300, 30, 1800) == 30);
            Check("typed '99999' clamps down to the maximum", NumberEntry.Parse("99999", 300, 30, 1800) == 1800);
            Check("typed '-3' clamps to 0 for a count", NumberEntry.Parse("-3", 6, 0, 50) == 0);
            Check("typed 'abc' reverts to the old value", NumberEntry.Parse("abc", 300, 30, 1800) == 300);
            Check("empty text reverts to the old value", NumberEntry.Parse("", 300, 30, 1800) == 300 && NumberEntry.Parse("   ", 300, 30, 1800) == 300);
            Check("typed '120.7' takes the whole part", NumberEntry.Parse("120.7", 300, 30, 1800) == 120);
            Check("a negative seed can be typed", NumberEntry.Parse("-42", 1, int.MinValue, int.MaxValue) == -42);
            Check("a huge number does not overflow", NumberEntry.Parse("99999999999999", 1, 0, 1000) == 1000);

            // Soldier movement: default is fixed at the post; the walk radius is a circle around it.
            var move = new ScenarioConfig();
            Check("movement is off by default with a 50 m radius", !move.allowMovement && move.moveRadius == 50f);
            var post = new Vector3(100f, 5f, -200f);
            Check("inside the radius the position is unchanged", Kurukshetra.Player.ObserverMove.ClampToRadius(post, new Vector3(120f, 9f, -200f), 50f) == new Vector3(120f, 9f, -200f));
            Vector3 pulled = Kurukshetra.Player.ObserverMove.ClampToRadius(post, new Vector3(400f, 9f, -200f), 50f);
            Check("outside the radius it is pulled back to the edge, height untouched", Mathf.Approximately(pulled.x, 150f) && pulled.y == 9f && pulled.z == -200f);
            Check("radius 0 means no limit", Kurukshetra.Player.ObserverMove.ClampToRadius(post, new Vector3(900f, 0f, 900f), 0f) == new Vector3(900f, 0f, 900f));
            var bad2 = new ScenarioConfig { moveRadius = -5f, walkSpeed = 0f, runSpeed = 1f }.Validate();
            Check("movement values are clamped (radius >= 0, run >= walk)", bad2.moveRadius == 0f && bad2.walkSpeed == 0.5f && bad2.runSpeed >= bad2.walkSpeed);

            // Map picker: radius to pixels and back, at different zooms and latitudes; snapping; labels; tile URLs.
            float px13 = MapPicker.RadiusToPixels(0.0, 13, 2500f), px14 = MapPicker.RadiusToPixels(0.0, 14, 2500f), px60 = MapPicker.RadiusToPixels(60.0, 13, 2500f);
            Check("radius: one zoom level doubles the circle on screen", Mathf.Abs(px14 / px13 - 2f) < 1e-4f);
            Check("radius: at 60 degrees north the same metres cover twice the pixels", Mathf.Abs(px60 / px13 - 2f) < 1e-3f);
            Check("radius: pixels back to metres", Mathf.Abs(MapPicker.PixelsToRadius(32.69, 12, MapPicker.RadiusToPixels(32.69, 12, 3137f)) - 3137f) < 0.01f);
            Check("radius: drag snaps to 50 m steps, never below 50", MapPicker.SnapRadius(2524f) == 2500f && MapPicker.SnapRadius(2526f) == 2550f && MapPicker.SnapRadius(3f) == 50f);
            Check("radius labels", MapPicker.FormatMetres(2500f) == "2,500 m" && MapPicker.FormatMetres(750f) == "750 m" && MapPicker.FormatMetres(12500f) == "12.5 km");
            Check("tile URL: OpenStreetMap is z/x/y", MapPicker.TileUrl(MapPicker.OsmUrl, 12, 2899, 1660) == "https://tile.openstreetmap.org/12/2899/1660.png");
            Check("tile URL: Sentinel-2 (EOX) is z/y/x", MapPicker.TileUrl(MapPicker.Sentinel2Url, 14, 11598, 6643).EndsWith("/g/14/6643/11598.jpg"));
            Check("tile URL: Esri is z/y/x and takes the key only when given", MapPicker.TileUrl(MapPicker.EsriImageryUrl, 14, 11598, 6643).EndsWith("/tile/14/6643/11598")
                && MapPicker.TileUrl(MapPicker.EsriImageryUrl, 14, 11598, 6643, "abc").EndsWith("/tile/14/6643/11598?token=abc")
                && !MapPicker.TileUrl(MapPicker.OsmUrl, 1, 0, 0, "abc").Contains("token"));

            // Typed or pasted positions.
            Check("coordinates: decimal with comma", GeoParse.TryParseLatLon("34.5735, 76.0974", out double la1, out double lo1) && Mathf.Approximately((float)la1, 34.5735f) && Mathf.Approximately((float)lo1, 76.0974f));
            Check("coordinates: Google Earth style DMS", GeoParse.TryParseLatLon("34°34'24.79\"N 76°05'50.63\"E", out double la2, out double lo2) && System.Math.Abs(la2 - 34.573553) < 1e-5 && System.Math.Abs(lo2 - 76.097397) < 1e-5);
            Check("coordinates: south and west are negative", GeoParse.TryParseLatLon("33°52'S 151°12'E", out double la3, out double lo3) && la3 < -33.8 && lo3 > 151.1
                && GeoParse.TryParseLatLon("40 42 46 N 74 0 21 W", out double la4, out double lo4) && la4 > 40.7 && lo4 < -74.0);
            Check("coordinates: a place name is not coordinates", !GeoParse.TryParseLatLon("Jammu Airport", out _, out _) && !GeoParse.TryParseLatLon("95, 10", out _, out _));
            Check("tile URL: CARTO labels take the key as ?key=", MapPicker.TileUrl(MapPicker.CartoLabelsUrl, 4, 11, 6, "k").EndsWith("/4/11/6@2x.png?key=k"));

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
