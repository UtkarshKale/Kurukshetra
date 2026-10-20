using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Environment
{
    // Checks for the visibility maths. Run via the CLI: Chaukanna.Environment.EnvironmentSelfTest.Run()
    public static class EnvironmentSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var p = ScriptableObject.CreateInstance<EnvironmentProfile>();

            // Minimum visible size by time of day.
            Check("day leaves the size threshold alone", EnvironmentMath.SizeMultiplier(p, TimeOfDay.Day, false) == 1f);
            Check("night doubles the size threshold", Mathf.Approximately(EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, false), 2f));
            Check("dusk sits between day and night", EnvironmentMath.SizeMultiplier(p, TimeOfDay.Dusk, false) > 1f && EnvironmentMath.SizeMultiplier(p, TimeOfDay.Dusk, false) < 2f);

            // Night vision recovers 60 % of the penalty: 2.0 -> 1.4.
            Check("night vision at night: 2.0 becomes 1.4", Mathf.Approximately(EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, true), 1.4f));
            Check("night vision never beats daylight", EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, true) > 1f && EnvironmentMath.SizeMultiplier(p, TimeOfDay.Dusk, true) > 1f);
            Check("night vision does nothing by day", EnvironmentMath.SizeMultiplier(p, TimeOfDay.Day, true) == 1f);
            p.nightVisionRecovery = 1f;
            Check("full recovery brings night back to 1.0", Mathf.Approximately(EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, true), 1f));
            p.nightVisionRecovery = 0f;
            Check("no recovery leaves night at 2.0", Mathf.Approximately(EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, true), 2f));
            p.nightVisionRecovery = 0.6f;

            // The threshold follows the configured number.
            p.night.minVisibleSizeMultiplier = 3f;
            Check("threshold scales as configured (3x)", Mathf.Approximately(EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, false), 3f) && Mathf.Approximately(EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, true), 1.8f));
            p.night.minVisibleSizeMultiplier = 2f;

            // Range by visibility.
            Check("clear keeps full range", EnvironmentMath.RangeMultiplier(p, Visibility.Clear) == 1f);
            Check("hazy leaves 60 % of range", Mathf.Approximately(EnvironmentMath.RangeMultiplier(p, Visibility.Hazy), 0.6f));
            Check("foggy leaves 35 % of range", Mathf.Approximately(EnvironmentMath.RangeMultiplier(p, Visibility.Foggy), 0.35f));

            // A worked case: a 2.2 m quad at 700 m is 0.18 deg. Threshold 0.15 by day (seen), 0.30 at night (not), 0.21 with night vision (not), and seen through 8-degree binoculars.
            float quad = 2f * Mathf.Atan2(1.1f, 700f) * Mathf.Rad2Deg;
            Check("quad at 700 m: seen by day, not at night, not with NV alone",
                quad >= 0.15f && quad < 0.15f * EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, false) && quad < 0.15f * EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, true));
            Check("quad at 700 m at night: seen through binoculars", quad >= 0.15f * EnvironmentMath.SizeMultiplier(p, TimeOfDay.Night, false) * 8f / 60f);

            // Fog density and haze steps.
            Check("fog density is 2 / visibility", Mathf.Approximately(EnvironmentMath.FogDensity(1000f), 0.002f) && EnvironmentMath.FogDensity(0f) == 0f);
            Check("thicker fog for shorter visibility", EnvironmentMath.FogDensity(700f) > EnvironmentMath.FogDensity(1200f));
            Check("worsen steps and clamps", EnvironmentMath.Worsen(Visibility.Clear, 1) == Visibility.Hazy && EnvironmentMath.Worsen(Visibility.Hazy, 5) == Visibility.Foggy && EnvironmentMath.Worsen(Visibility.Foggy, 0) == Visibility.Foggy);
            Check("conditions text", EnvironmentMath.Describe(TimeOfDay.Night, Visibility.Foggy, 40f) == "Night, Foggy, NV used 40%" && EnvironmentMath.Describe(TimeOfDay.Day, Visibility.Clear, 0f) == "Day, Clear");

            if (Application.isPlaying) Object.Destroy(p); else Object.DestroyImmediate(p);
            string summary = $"[EnvironmentSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
