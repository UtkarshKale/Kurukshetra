using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Sensors
{
    // Checks for the sensor maths and AI hint selection. Run from the SensorController context menu,
    // or via the CLI: Chaukanna.Sensors.SensorSelfTest.Run()
    public static class SensorSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var profile = ScriptableObject.CreateInstance<SensorProfile>();
            profile.ApplyDefaults();

            // Same seed and id: same hint. Different seed: the set of hints changes.
            bool stable = true;
            int differs = 0;
            for (int id = 1; id <= 200; id++)
            {
                var a = SensorMath.PickHint(profile, 1001, id, ContactClass.HostileQuad, 0.15f);
                var b = SensorMath.PickHint(profile, 1001, id, ContactClass.HostileQuad, 0.15f);
                var other = SensorMath.PickHint(profile, 2002, id, ContactClass.HostileQuad, 0.15f);
                if (a.call != b.call || a.confidence != b.confidence || a.wrong != b.wrong) stable = false;
                if (a.call != other.call || a.confidence != other.confidence) differs++;
            }
            Check("same seed and id give the same hint", stable);
            Check("a different seed changes the hints", differs > 50);

            // Error rate over many contacts, healthy and degraded.
            float ok = WrongRate(profile, profile.aiErrorRate.ok), degraded = WrongRate(profile, profile.aiErrorRate.degraded);
            Check($"healthy error rate near 15 % (got {ok * 100f:F1} %)", Mathf.Abs(ok - 0.15f) < 0.02f);
            Check($"degraded error rate near 35 % (got {degraded * 100f:F1} %)", Mathf.Abs(degraded - 0.35f) < 0.02f);

            // Wrong hints are plausible and never the right class; right hints are the right class; confidence stays in band.
            bool plausible = true, rightIsRight = true, inBand = true, wrongCanBeMoreConfident = false;
            int maxRight = 0;
            foreach (ContactClass truth in System.Enum.GetValues(typeof(ContactClass)))
            {
                var options = profile.Confusion(truth).wrongOptions;
                for (int id = 1; id <= 400; id++)
                {
                    var h = SensorMath.PickHint(profile, 7, id, truth, 0.5f);
                    Vector2 band = h.wrong ? profile.aiWrongConfidence : profile.aiRightConfidence;
                    if (h.confidence < band.x || h.confidence > band.y) inBand = false;
                    if (h.wrong) { if (!options.Contains(h.call) || h.call == SensorMath.RightCall(truth)) plausible = false; }
                    else
                    {
                        if (h.call != SensorMath.RightCall(truth)) rightIsRight = false;
                        if (h.confidence > maxRight) maxRight = h.confidence;
                    }
                    if (h.wrong && h.confidence > profile.aiRightConfidence.y) wrongCanBeMoreConfident = true;
                }
            }
            Check("wrong hints come from the confusion table", plausible);
            Check("right hints name the true class", rightIsRight);
            Check("confidence stays inside its configured range", inBand);
            Check("some wrong hints are more confident than any right one", wrongCanBeMoreConfident);

            // RF: nearer is stronger, nothing beyond sensor range, hostile and civilian quads overlap.
            Check("RF rises as an emitter closes", SensorMath.RfLevel(0.7f, 300f, 1200f) > SensorMath.RfLevel(0.7f, 900f, 1200f));
            Check("RF is zero beyond sensor range", SensorMath.RfLevel(0.7f, 1300f, 1200f) == 0f);
            Check("RF at 600 m of 1200 m is half strength", Mathf.Approximately(SensorMath.RfLevel(0.6f, 600f, 1200f), 0.3f));
            float hostileLow = 0.7f * (1f - profile.Signature(ContactClass.HostileQuad).rfVariation);
            float civilianHigh = 0.5f * (1f + profile.Signature(ContactClass.CivilianQuad).rfVariation);
            Check("a strong civilian quad can equal a hostile quad at closer range", SensorMath.RfLevel(civilianHigh, 400f, 1200f) > SensorMath.RfLevel(hostileLow, 600f, 1200f));
            Check("birds emit no RF", !profile.Signature(ContactClass.Bird).rfEmitter);
            Check("per-contact RF variation is fixed and bounded",
                SensorMath.Variation(1001, 5, 0.15f) == SensorMath.Variation(1001, 5, 0.15f) && Mathf.Abs(SensorMath.Variation(1001, 5, 0.15f) - 1f) <= 0.15f);

            // Sound: half loudness at the half range; a fixed-wing is heard over a quad at the same range.
            Check("sound is half at the half range", Mathf.Approximately(SensorMath.HeardLevel(0.8f, 400f, 400f), 0.4f));
            Check("fixed-wing is louder than a quad at equal range",
                SensorMath.HeardLevel(profile.Signature(ContactClass.HostileFixedWing).loudness, 500f, 400f) > SensorMath.HeardLevel(profile.Signature(ContactClass.HostileQuad).loudness, 500f, 400f));

            // Bearings.
            Check("world bearing: +X is 90", Mathf.Approximately(SensorMath.WorldBearing(Vector3.right), 90f));
            Check("world bearing: -X is -90", Mathf.Approximately(SensorMath.WorldBearing(Vector3.left), -90f));
            Check("relative bearing: facing 0, target 90 = 90 right", Mathf.Approximately(SensorMath.RelativeBearing(0f, 90f), 90f));
            Check("relative bearing: facing 90, target 0 = 90 left", Mathf.Approximately(SensorMath.RelativeBearing(90f, 0f), -90f));
            Check("relative bearing wraps: facing 170, target -170 = 20 right", Mathf.Approximately(SensorMath.RelativeBearing(170f, -170f), 20f));

            if (Application.isPlaying) Object.Destroy(profile); else Object.DestroyImmediate(profile);
            string summary = $"[SensorSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static float WrongRate(SensorProfile profile, float errorRate)
        {
            int wrong = 0;
            const int n = 20000;
            for (int id = 1; id <= n; id++)
                if (SensorMath.PickHint(profile, 1001, id, ContactClass.HostileQuad, errorRate).wrong) wrong++;
            return wrong / (float)n;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
