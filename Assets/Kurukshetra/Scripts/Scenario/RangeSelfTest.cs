using System.Collections.Generic;
using Kurukshetra.Player;
using UnityEngine;

namespace Kurukshetra.Scenario
{
    // Checks for modules 1 and 6: the Range drill's choices (interleaving, focus, tiers, fluency) and the true-size display
    // maths against the doc's table (§1.7a). CLI: Kurukshetra.Scenario.RangeSelfTest.Run()
    public static class RangeSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();

            // Interleaving: never three of a kind in a row, every class shows up, the focus class more often.
            var rng = new System.Random(7);
            var seq = new List<ContactClass>();
            for (int i = 0; i < 600; i++) seq.Add(RangeDrill.PickClass(rng, seq, ""));
            bool noTriple = true;
            for (int i = 2; i < seq.Count; i++) if (seq[i] == seq[i - 1] && seq[i] == seq[i - 2]) noTriple = false;
            var counts = new Dictionary<ContactClass, int>();
            foreach (var c in seq) { counts.TryGetValue(c, out int n); counts[c] = n + 1; }
            Check("interleaved: never three of a kind in a row, all five classes appear", noTriple && counts.Count == 5);
            var focused = new List<ContactClass>();
            var rng2 = new System.Random(7);
            for (int i = 0; i < 600; i++) focused.Add(RangeDrill.PickClass(rng2, focused, "Bird"));
            int birds = focused.FindAll(c => c == ContactClass.Bird).Count;
            Check($"the learner's focus class comes up more often ({birds} of 600 birds, about 1 in 5 without focus)", birds > 600 * 0.3f);
            var a = new List<ContactClass>(); var b = new List<ContactClass>();
            var ra = new System.Random(42); var rb = new System.Random(42);
            for (int i = 0; i < 30; i++) { a.Add(RangeDrill.PickClass(ra, a, "")); b.Add(RangeDrill.PickClass(rb, b, "")); }
            Check("same seed, same item order", string.Join(",", a) == string.Join(",", b));

            // Tiers get farther.
            var rr = new System.Random(1);
            bool bands = true;
            for (int i = 0; i < 200; i++)
            {
                float t1 = RangeDrill.RangeFor(rr, 1), t3 = RangeDrill.RangeFor(rr, 3);
                if (t1 < 150f || t1 > 350f || t3 < 550f || t3 > 1000f) bands = false;
            }
            Check("tier 1 items at 150-350 m, tier 3 at 550-1000 m", bands);

            // Fluency: 10 of the last 12 right within 3 s.
            var answers = new List<DrillItem>();
            for (int i = 0; i < 12; i++) answers.Add(new DrillItem { correct = i != 3 && i != 7, seconds = 2f });
            Check("10 of 12 right within 3 s is fluent", RangeDrill.Fluent(answers));
            answers[5].seconds = 4f;
            Check("...one slow answer makes it 9 of 12: not fluent", !RangeDrill.Fluent(answers));
            answers[5].seconds = 2f;
            Check("after a tier change only the new answers count (fewer than 12: not fluent yet)", !RangeDrill.Fluent(answers, 4));
            var none = new List<DrillItem>();
            for (int i = 0; i < 12; i++) none.Add(new DrillItem { correct = false, seconds = -1f });
            Check("no answers in time is never fluent", !RangeDrill.Fluent(none));

            // True-size display, against the doc's table.
            var s24 = new DisplaySettings { diagonalInches = 24f, aspectW = 16f, aspectH = 9f, distanceCm = 60f };
            var s156 = new DisplaySettings { diagonalInches = 15.6f, aspectW = 16f, aspectH = 9f, distanceCm = 55f };
            float v24 = DisplayCalibration.VerticalFov(s24), a24 = DisplayCalibration.ArcminPerPixel(s24, 1080);
            float v156 = DisplayCalibration.VerticalFov(s156), a156 = DisplayCalibration.ArcminPerPixel(s156, 1080), a156lo = DisplayCalibration.ArcminPerPixel(s156, 768);
            Check($"24\" 1080p at 60 cm: vFOV {v24:F1} deg (doc 28.0), {a24:F2} arcmin/px (doc 1.59)", Mathf.Abs(v24 - 28.0f) < 0.3f && Mathf.Abs(a24 - 1.59f) < 0.02f);
            Check($"15.6\" at 55 cm: vFOV {v156:F1} deg (doc 20.0), 1080p {a156:F2} (doc 1.12), 768 rows {a156lo:F2} (doc 1.58)", Mathf.Abs(v156 - 20.0f) < 0.3f && Mathf.Abs(a156 - 1.12f) < 0.02f && Mathf.Abs(a156lo - 1.58f) < 0.03f);
            Check("comparable band 1.0-2.0: 1.59 yes, 3.7 (Unity's default 60 deg view) no", DisplayCalibration.Comparable(a24) && !DisplayCalibration.Comparable(60f * 60f / 1080f * 1.11f));
            float quadArcmin = 2f * Mathf.Atan(0.35f / 2f / 300f) * Mathf.Rad2Deg * 60f;
            Check($"a 0.35 m quad at 300 m is {quadArcmin:F1} arcmin (doc 4.0), so {quadArcmin / a24:F1} px on the 24\" screen (doc 2.5)", Mathf.Abs(quadArcmin - 4.0f) < 0.1f && Mathf.Abs(quadArcmin / a24 - 2.5f) < 0.1f);

            string summary = $"[RangeSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
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
