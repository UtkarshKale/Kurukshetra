using System.Collections.Generic;
using Kurukshetra.Player;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Kurukshetra.Scenario
{
    // Checks the dense-sky crowd without a scene: the closed-form motion, the generator, the ground grid, the Burst jobs against
    // the managed maths, the bucketing, the track numbers, the IFF replies and the promotion bookkeeping.
    // CLI: Kurukshetra.Scenario.CrowdSelfTest.Run()
    public static class CrowdSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();
        public static string Timing = "";       // the numbers of the 10 000 agent check, for the report

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }

        static ContactDefinition Def(ContactClass cls, Silhouette sil, float size)
        {
            var d = ScriptableObject.CreateInstance<ContactDefinition>();
            d.contactClass = cls; d.silhouette = sil; d.size = size; d.displayName = cls.ToString();
            return d;
        }

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var spec = CrowdGenerator.DefaultSpecs();
            var post = new float2(0f, 0f);

            // 1. Deterministic.
            var a1 = CrowdGenerator.Generate(77, 1000, post, 1500f, 600f, 5f, spec);
            var a2 = CrowdGenerator.Generate(77, 1000, post, 1500f, 600f, 5f, spec);
            var a3 = CrowdGenerator.Generate(78, 1000, post, 1500f, 600f, 5f, spec);
            bool same = true, differs = false;
            for (int i = 0; i < a1.Length; i++)
            {
                same &= math.distance(CrowdMath.Pos(a1[i], 123.4f), CrowdMath.Pos(a2[i], 123.4f)) == 0f && a1[i].pattern == a2[i].pattern && a1[i].iff == a2[i].iff;
                differs |= math.distance(CrowdMath.Pos(a1[i], 123.4f), CrowdMath.Pos(a3[i], 123.4f)) > 0.01f;
            }
            Check("same seed gives the same crowd and positions", same && a1.Length == 1000);
            Check("a different seed gives a different crowd", differs);

            // 2. Every pattern stays inside its bounds for 600 s.
            bool bounds = true;
            var seen = new HashSet<int>();
            foreach (var a in a1)
            {
                seen.Add(a.pattern);
                float2 c = a.centre.xz, perp = new float2(-a.axis.y, a.axis.x);
                for (float t = 0f; t <= 600f; t += 5f)
                {
                    float3 p = CrowdMath.Pos(a, t);
                    if (!math.all(math.isfinite(p))) { bounds = false; break; }
                    float2 d = p.xz - c;
                    switch ((CrowdPattern)a.pattern)
                    {
                        case CrowdPattern.Orbit:
                            bounds &= math.abs(math.length(d) - a.radius) < 0.05f && math.abs(p.y - a.centre.y) < 1e-3f; break;
                        case CrowdPattern.Corridor:
                            bounds &= math.abs(math.dot(d, a.axis)) <= a.length * 0.5f + 0.05f && math.abs(math.dot(d, perp) - a.offset.x) < 0.05f && p.y >= a.centre.y - 1e-3f && p.y <= a.centre.y + CrowdMath.LevelGap + 1e-3f; break;
                        case CrowdPattern.Grid:
                            bounds &= math.abs(math.dot(d, a.axis)) <= a.length * 0.5f + 0.05f && math.abs(math.dot(d, perp)) <= (a.lanes - 1) * a.radius * 0.5f + 0.05f; break;
                        case CrowdPattern.Station:
                            bounds &= math.length(d) <= a.radius * 1.3f + 0.05f && math.abs(p.y - a.centre.y) <= 2.01f; break;
                        case CrowdPattern.Cluster:
                            bounds &= math.length(d) <= a.radius + math.length(a.offset.xz) + 0.1f && math.length(d) >= a.radius - math.length(a.offset.xz) - 0.1f; break;
                        default:
                            bounds &= math.length(d) <= a.radius * 1.2807f + math.length(a.offset.xz) + 5f; break;
                    }
                    if (!bounds) break;
                }
                if (!bounds) break;
            }
            Check("all six patterns stay inside their bounds over 600 s", bounds && seen.Count == 6);

            // 3. Counts match the mix shares (+-1) for 1000 and 10 000.
            bool counts = true;
            foreach (int total in new[] { 1000, 10000 })
            {
                var list = total == 1000 ? a1 : CrowdGenerator.Generate(5, total, post, 1500f, 600f, 5f, spec);
                var hist = new int[6];
                foreach (var a in list) hist[a.pattern]++;
                var expect = CrowdGenerator.Counts(total);
                int histSum = 0;
                for (int i = 0; i < 6; i++)
                {
                    counts &= hist[i] == expect[i] && Mathf.Abs(hist[i] - total * CrowdGenerator.Shares[i]) <= 1f;
                    histSum += hist[i];
                }
                counts &= histSum == total && list.Length == total;
            }
            Check("generation counts match the mix shares (+-1) for 1000 and 10 000", counts);

            // 3b. Cooperative classes only, none hostile; birds have no IFF; time windows.
            bool classes = true, early = true;
            int windowed = 0;
            foreach (var a in a1)
            {
                classes &= a.cls == (byte)ContactClass.FriendlyISR || a.cls == (byte)ContactClass.CivilianQuad || a.cls == (byte)ContactClass.Bird;
                if (a.cls == (byte)ContactClass.Bird) classes &= a.iff == 1;
                if (a.spawnT > 0f || a.endT < CrowdMath.Never) windowed++;
                early &= a.spawnT < a.endT;
            }
            Check("crowd is own ISR / civil / birds only, birds never answer, windows are sane", classes && early && windowed > 100 && windowed < 320);

            // 4. Height grid: bilinear between corners, the 15 m floor, clamping at the edge.
            var h = new NativeArray<float>(4, Allocator.Temp);
            h[0] = 0f; h[1] = 10f; h[2] = 20f; h[3] = 30f;      // row-major: (0,0) (1,0) / (0,1) (1,1)
            float mid = CrowdMath.Ground(h, new float2(0f, 0f), 0.1f, 2, 5f, 5f);
            float edge = CrowdMath.Ground(h, new float2(0f, 0f), 0.1f, 2, 5f, 0f);
            float outside = CrowdMath.Ground(h, new float2(0f, 0f), 0.1f, 2, -500f, 5f);
            h.Dispose();
            Check("height grid is bilinear between the corners", Mathf.Abs(mid - 15f) < 1e-4f && Mathf.Abs(edge - 5f) < 1e-4f);
            Check("height grid clamps at its edge", Mathf.Abs(outside - 10f) < 1e-4f);
            Check("above-ground clamp: never lower than 15 m over the ground", Mathf.Abs(CrowdMath.WorldY(5f, 100f) - 115f) < 1e-4f && Mathf.Abs(CrowdMath.WorldY(20f, 100f) - 120f) < 1e-4f);

            // Camera for the job checks: 1920 x 1080 render texture, at the post, looking along +Z, ignores all colliders.
            var rt = new RenderTexture(1920, 1080, 16);
            var camGo = new GameObject("CrowdTestCamera") { hideFlags = HideFlags.HideAndDontSave };
            camGo.transform.position = new Vector3(0f, 1.7f, 0f);
            camGo.transform.rotation = Quaternion.identity;
            var cam = camGo.AddComponent<Camera>();
            cam.targetTexture = rt; cam.fieldOfView = 60f; cam.enabled = false;
            var sensor = camGo.AddComponent<ContactSensor>();
            sensor.terrainMask = 0;
            var skyGo = new GameObject("CrowdTestSky") { hideFlags = HideFlags.HideAndDontSave };
            var sky = skyGo.AddComponent<CrowdSky>();
            sky.enabled = false;
            var defs = new ContactDefinition[5];
            defs[(int)ContactClass.FriendlyISR] = Def(ContactClass.FriendlyISR, Silhouette.FixedWing, 4f);
            defs[(int)ContactClass.CivilianQuad] = Def(ContactClass.CivilianQuad, Silhouette.Quad, 2.2f);
            defs[(int)ContactClass.Bird] = Def(ContactClass.Bird, Silhouette.Bird, 1.8f);
            CrowdRenderer renderer = null;
            try
            {
                // 5. Job output equals the managed maths for 1000 agents.
                sky.InitData(a1, post, 4500f, false);
                for (int j = 0; j < CrowdSky.HeightGrid; j++)
                    for (int i = 0; i < CrowdSky.HeightGrid; i++) sky.SetHeight(i, j, 60f + 40f * Mathf.Sin(i * 0.07f) * Mathf.Cos(j * 0.05f));
                sky.Refresh(cam, 250f, 100000f, false);
                float worst = 0f;
                int aliveCount = 0;
                for (int i = 0; i < a1.Length; i++)
                {
                    bool alive = CrowdMath.Alive(a1[i], 250f);
                    if (alive != sky.IsAlive(i)) { worst = 999f; break; }
                    if (!alive) continue;
                    aliveCount++;
                    float3 w = CrowdMath.World(a1[i], 250f, sky.Heights, sky.GridOrigin, sky.InvCell, CrowdSky.HeightGrid);
                    worst = Mathf.Max(worst, Vector3.Distance(sky.PositionOf(i), new Vector3(w.x, w.y, w.z)));
                    float yd = sky.YawOf(i) - CrowdMath.Yaw(a1[i], 250f); float yawDiff = Mathf.Abs(Mathf.Atan2(Mathf.Sin(yd), Mathf.Cos(yd)));
                    if (yawDiff > 0.02f) worst = 999f;      // heading is a finite difference of two positions: ~1 degree of float noise is fine
                    if (sky.PositionOf(i).y < CrowdMath.MinAbove) worst = 999f;
                }
                Check($"job output equals the managed maths for 1000 agents (max error {worst * 1000f:F3} mm, {aliveCount} alive)", worst < 0.001f && aliveCount > 700);

                // 6 and 7. 10 000 agents: both jobs together under 2 ms; the buckets add up and nothing behind the camera is in one.
                renderer = new CrowdRenderer(defs, new List<GameObject>[5], 10000);
                var big = CrowdGenerator.Generate(31, 10000, post, 1500f, 600f, 5f, spec);
                sky.InitData(big, post, 4500f, false);
                for (int j = 0; j < CrowdSky.HeightGrid; j++)
                    for (int i = 0; i < CrowdSky.HeightGrid; i++) sky.SetHeight(i, j, 60f + 40f * Mathf.Sin(i * 0.07f) * Mathf.Cos(j * 0.05f));
                sky.AttachRendererForTest(renderer);
                sky.Refresh(cam, 100f, 2000f, true);            // warm-up (Burst compiles on first use)
                sky.Refresh(cam, 100f, 2000f, true);
                var times = new List<float>();
                var sw = new System.Diagnostics.Stopwatch();
                for (int k = 0; k < 15; k++)
                {
                    sw.Restart();
                    sky.Refresh(cam, 100f + k, 2000f, true);
                    sw.Stop();
                    times.Add((float)sw.Elapsed.TotalMilliseconds);
                }
                times.Sort();
                float median = times[times.Count / 2];
                Timing = $"10000 agents: update + bucket jobs median {median:F2} ms, min {times[0]:F2}, max {times[times.Count - 1]:F2}";
                Check($"10 000 agents: update + bucket jobs under 2 ms ({median:F2} ms median)", median < 2f);

                sky.Refresh(cam, 150f, 2000f, true);
                int visible = 0, behind = 0, models = 0, dots = 0, partsExpected = 0;
                Vector3 camPos = cam.transform.position, fwd = cam.transform.forward;
                for (int i = 0; i < big.Length; i++)
                {
                    byte l = sky.LodOf(i);
                    if (l == CrowdLod.None) continue;
                    visible++;
                    if (Vector3.Dot(sky.PositionOf(i) - camPos, fwd) <= 0f) behind++;
                    int key = big[i].cls * CrowdGenerator.MaxVariants + big[i].variant;
                    if (l == CrowdLod.Model && renderer.modelParts[key] > 0) { models++; partsExpected += renderer.modelParts[key]; } else dots++;
                }
                int matrixTotal = 0, behindMatrices = 0;
                for (int b = 0; b < renderer.BucketCount; b++)
                {
                    matrixTotal += renderer.counts[b];
                    for (int m = 0; m < renderer.counts[b]; m++)
                    {
                        float4x4 mat = renderer.matrices[renderer.starts[b] + m];
                        if (Vector3.Dot(new Vector3(mat.c3.x, mat.c3.y, mat.c3.z) - camPos, fwd) < -5f) behindMatrices++;
                    }
                }
                Check($"buckets add up to the visible count ({visible} visible: {models} models, {dots} dots, {matrixTotal} matrices)",
                    visible > 0 && models == sky.ModelAgents && dots == sky.DotAgents && matrixTotal == dots + partsExpected && models + dots == visible);
                Check("nothing behind the camera is in any bucket", behind == 0 && behindMatrices == 0);

                // 10. Promotion bookkeeping and picking, with two hand-made agents ahead of the camera.
                var pair = new CrowdAgent[2];
                for (int i = 0; i < 2; i++)
                    pair[i] = new CrowdAgent
                    {
                        pattern = (byte)CrowdPattern.Station, cls = (byte)ContactClass.CivilianQuad, size = 2.2f, radius = 5f, speed = 0.2f,
                        centre = new float3(i * 100f, 40f, 300f), axis = new float2(1f, 0f), spawnT = -1f, endT = CrowdMath.Never, dir = 1f, lanes = 1f, length = 1f,
                    };
                sky.InitData(pair, post, 4500f, false);
                sky.Refresh(cam, 0f, 2000f, false);
                Vector3 aim = sky.PositionOf(0) - camPos;
                int hit = sky.Pick(cam, aim, 2f, sensor, out float angle);
                Check($"Pick finds the drone under the aim ({angle:F2} deg off)", hit == 0 && angle < 0.01f);
                sky.MarkRemoved(0);
                sky.Refresh(cam, 0f, 2000f, false);
                Check("a promoted drone is marked removed, not alive, not drawn", sky.IsRemoved(0) && !sky.IsAlive(0) && sky.LodOf(0) == CrowdLod.None && sky.Promoted == 1 && sky.IsAlive(1));
                Check("a promoted drone is never picked again", sky.Pick(cam, aim, 2f, sensor) == -1);
                sky.MarkRemoved(0);
                Check("removing twice counts once", sky.Promoted == 1);
            }
            finally
            {
                sky.StopForTest();
                Object.DestroyImmediate(skyGo);
                Object.DestroyImmediate(camGo);
                cam = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                foreach (var d in defs) if (d != null) Object.DestroyImmediate(d);
            }

            // 8. Track numbers.
            var perm = CrowdIds.Permutation(5500, 4242);
            var set = new HashSet<int>(perm);
            bool inRange = true;
            foreach (int v in perm) inRange &= v >= 1 && v <= 5500;
            bool shuffled = false;
            for (int i = 0; i < 20; i++) shuffled |= perm[i] != i + 1;
            Check("track numbers are a permutation of 1..N with no duplicates", perm.Length == 5500 && set.Count == 5500 && inRange && shuffled);
            var perm2 = CrowdIds.Permutation(5500, 4242);
            bool repeat = true;
            for (int i = 0; i < perm.Length; i++) repeat &= perm[i] == perm2[i];
            Check("the shuffle repeats for the same seed", repeat);
            bool plain = true;
            for (int i = 0; i < 50; i++) plain &= ScenarioSpawner.PlannedTrackId(null, i) == i + 1;
            Check("track numbers are unchanged (1, 2, 3...) when crowdCount == 0", plain);

            // 9. IFF reply table and shares.
            Check("own drone: valid reply, or no reply when its transponder is off",
                Iff.ReplyText(ContactClass.FriendlyISR, false, false, false, false, 1, 1) == "IFF: VALID (own)" && Iff.ReplyText(ContactClass.FriendlyISR, false, false, true, false, 1, 1) == "IFF: NO REPLY");
            string civ = Iff.ReplyText(ContactClass.CivilianQuad, false, false, false, false, 1, 9);
            Check("civil drone: Remote ID with operator and filed route, or no Remote ID",
                civ.StartsWith("REMOTE ID: civil, operator ") && civ.EndsWith("route filed") && Iff.ReplyText(ContactClass.CivilianQuad, false, false, true, false, 1, 9) == "NO REMOTE ID");
            Check("a bird never answers", Iff.ReplyText(ContactClass.Bird, false, false, false, false, 1, 1) == "IFF: NO REPLY");
            Check("a hostile does not answer, unless it spoofs",
                Iff.ReplyText(ContactClass.HostileQuad, false, false, false, false, 1, 1) == "IFF: NO REPLY" && Iff.ReplyText(ContactClass.HostileQuad, false, false, false, true, 1, 1) == "IFF: VALID (own)");
            Check("a blend-in hostile spoofs until its anomaly, then gives no reply",
                Iff.ReplyText(ContactClass.HostileFixedWing, true, false, false, false, 1, 1) == "IFF: VALID (own)" && Iff.ReplyText(ContactClass.HostileFixedWing, true, true, false, false, 1, 1) == "IFF: NO REPLY");
            int noIff = 0, spoof = 0, early2 = 0;
            float minDelay = 9f, maxDelay = 0f;
            const int N = 20000;
            for (int id = 1; id <= N; id++)
            {
                if (Iff.Roll(7, id, 12f, 1)) noIff++;
                if (Iff.Roll(7, id, 30f, 2)) spoof++;
                float d = Iff.Delay(7, id);
                minDelay = Mathf.Min(minDelay, d); maxDelay = Mathf.Max(maxDelay, d);
                if (Iff.Roll(7, id, 0f, 1)) early2++;
            }
            Check($"no-IFF share is about 12 % and spoof share about 30 % ({100f * noIff / N:F1} / {100f * spoof / N:F1})",
                Mathf.Abs(100f * noIff / N - 12f) < 1.2f && Mathf.Abs(100f * spoof / N - 30f) < 1.5f && early2 == 0);
            Check($"IFF replies come after 0.6 to 1.4 s ({minDelay:F2} to {maxDelay:F2})", minDelay >= 0.6f && maxDelay <= 1.4f && maxDelay > 1.3f && minDelay < 0.7f);

            // Summary maths.
            var recs = new List<ContactRecord>
            {
                new ContactRecord { fromCrowd = true, action = PlayerAction.Engage, iffChecked = true },
                new ContactRecord { fromCrowd = true, action = PlayerAction.Report },
                new ContactRecord { fromCrowd = true, action = PlayerAction.Jam },
                new ContactRecord { iffChecked = true, action = PlayerAction.Engage },
            };
            var sum = CrowdSky.Summarize(recs, 1500, 1320);
            Check("crowd summary counts checked, challenges and friendlies engaged or jammed",
                sum.tracks == 1500 && sum.peak == 1320 && sum.checkedCount == 3 && sum.iffChallenges == 2 && sum.friendliesHit == 2);

            string summary = $"[CrowdSelfTest] {lines.Count - failures}/{lines.Count} passed\n{Timing}\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }
    }
}
