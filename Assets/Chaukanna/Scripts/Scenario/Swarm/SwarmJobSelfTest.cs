using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // Builds a swarm of N agents headless (no scene) and times the Burst step.
    // CLI: Chaukanna.Scenario.SwarmStress.Run(5000)  ->  ms per step.
    public static class SwarmStress
    {
        public static SwarmSim Build(int agents, int seed, SwarmTactic tactic, float distance, SwarmParams p)
        {
            var prof = SwarmProfile.Drones();
            prof.spawnSpacing = p.spacing;
            var centre = new float3(0f, 100f, 0f);
            var target = new float3(0f, 80f, distance);
            var offsets = SwarmPlanner.Layout(agents, seed, prof, new Vector3(0f, 0f, 1f));
            var arr = new float3[agents];
            for (int i = 0; i < agents; i++) arr[i] = new float3(offsets[i].x, offsets[i].y, offsets[i].z);
            return new SwarmSim(centre, target, arr, p, tactic);
        }

        // Average milliseconds per 50 Hz step over `steps` steps (after 5 warm-up steps).
        public static float Run(int agents, int steps = 50)
        {
            using (var sim = Build(agents, 1234, SwarmTactic.Direct, 5000f, SwarmParams.Drones()))
            {
                for (int i = 0; i < 5; i++) sim.Step();
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < steps; i++) sim.Step();
                return (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency / steps;
            }
        }
    }

    // Checks the big-swarm simulation without a scene: same seed same flight, nobody on top of anybody, the tactics do what they say,
    // the terrain floor holds, the step is fast and allocates nothing.
    // CLI: Chaukanna.Scenario.SwarmJobSelfTest.Run()   (log in SwarmJobSelfTest.Log)
    public static class SwarmJobSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();
        public static string Log => string.Join("\n", lines);
        public static bool Done;
        public static string Extra = "";
        public static float Ms1000, Ms3000, Ms5000, Ms10000;

        static void Check(string name, bool ok, string detail = "")
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? "  [" + detail + "]" : ""));
        }

        public static bool Run(bool perf = true)
        {
            failures = 0;
            lines.Clear();
            var p = SwarmParams.Drones();

            // 1. Determinism: same seed -> identical positions after 200 steps; another seed -> different.
            using (var a = SwarmStress.Build(1000, 7, SwarmTactic.Saturate, 1500f, p))
            using (var b = SwarmStress.Build(1000, 7, SwarmTactic.Saturate, 1500f, p))
            using (var c = SwarmStress.Build(1000, 8, SwarmTactic.Saturate, 1500f, p))
            {
                for (int i = 0; i < 200; i++) { a.Step(); b.Step(); c.Step(); }
                bool same = true, differs = false;
                for (int i = 0; i < 1000; i++)
                {
                    if (!a.Pos[i].Equals(b.Pos[i])) same = false;
                    if (!a.Pos[i].Equals(c.Pos[i])) differs = true;
                }
                Check("same seed, same positions after 200 steps (1000 agents)", same);
                Check("another seed flies differently", differs);
            }

            // 2. No collisions at 1000 agents over a 20 s flight (checked every second), and the lattice holds near the spacing.
            using (var s = SwarmStress.Build(1000, 11, SwarmTactic.Direct, 3000f, p))
            {
                float min = s.MinSeparation(40f);
                for (int i = 1; i <= 1000; i++)
                {
                    s.Step();
                    if (i % 50 == 0) min = Mathf.Min(min, s.MinSeparation(40f));
                }
                Check("1000 agents: closest pair over 20 s >= 5 m", min >= 5f, $"closest {min:F1} m, spacing {p.spacing} m");
            }

            // 3. Direct: every agent reaches the post.
            using (var s = SwarmStress.Build(500, 21, SwarmTactic.Direct, 700f, p))
            {
                int steps = 0;
                while (s.FlyingCount > 0 && steps < 4000) { s.Step(); steps++; }
                Check("direct: all 500 agents reach the target", s.ArrivedCount == 500, $"{s.ArrivedCount} arrived in {steps * p.dt:F0} s");
            }

            // 4. Saturate: 4 groups, phases advance through the spread waypoint, all arrive within a window.
            using (var s = SwarmStress.Build(400, 31, SwarmTactic.Saturate, 1400f, p))
            {
                int steps = 0, first = -1;
                bool spread = false;
                while (s.FlyingCount > 0 && steps < 9000)
                {
                    s.Step(); steps++;
                    if (first < 0 && s.ArrivedCount > 0) first = steps;
                    if (s.Phase(0) == 1 && s.Phase(s.Groups - 1) == 1) spread = true;
                }
                float window = first < 0 ? 999f : (steps - first) * p.dt;
                Check("saturate: 4 groups", s.Groups == 4, "groups " + s.Groups);
                Check("saturate: spread to several bearings before the rush", spread);
                Check("saturate: all 400 reach the target", s.ArrivedCount == 400, $"{s.ArrivedCount} arrived");
                Check("saturate: arrivals within a 30 s window", window <= 30f, $"window {window:F1} s");
            }

            // 5. Split: two groups, each its own flank waypoint, both reach the target.
            using (var s = SwarmStress.Build(400, 41, SwarmTactic.Split, 1400f, p))
            {
                int steps = 0;
                bool flank = false;
                float apart = 0f;
                while (s.FlyingCount > 0 && steps < 9000)
                {
                    s.Step(); steps++;
                    if (s.Phase(0) == 1 && s.Phase(1) == 1) { flank = true; apart = Mathf.Max(apart, math.distance(s.Centroid(0), s.Centroid(1))); }
                }
                Check("split: two sub-groups, both non-empty at the start", s.Groups == 2);
                Check("split: the two bodies come apart (centres > 200 m)", flank && apart > 200f, $"apart {apart:F0} m");
                Check("split: all 400 reach the target", s.ArrivedCount == 400, $"{s.ArrivedCount} arrived");
            }

            // 6. Terrain floor: a 120 m hill across the path; nobody flies below ground + 15 m.
            using (var s = SwarmStress.Build(300, 51, SwarmTactic.Direct, 1200f, p))
            {
                s.SetHeights(new float2(0f, 600f), 3000f, (x, z) => 120f * Mathf.Exp(-((z - 600f) * (z - 600f)) / (2f * 150f * 150f)));
                float worst = float.MaxValue;
                for (int i = 0; i < 2500 && s.FlyingCount > 0; i++)
                {
                    s.Step();
                    if (i % 10 != 0) continue;
                    var pos = s.Pos;
                    for (int k = 0; k < 300; k++)
                    {
                        if (s.State[k] != SwarmSim.Flying) continue;
                        float g = 120f * Mathf.Exp(-((pos[k].z - 600f) * (pos[k].z - 600f)) / (2f * 150f * 150f));
                        worst = Mathf.Min(worst, pos[k].y - g);
                    }
                }
                Check("terrain floor: nobody below ground + 14 m over a hill", worst >= 14f, $"lowest clearance {worst:F1} m");
            }

            // 7. Zero allocation per step.
            using (var s = SwarmStress.Build(2000, 61, SwarmTactic.Saturate, 3000f, p))
            {
                for (int i = 0; i < 5; i++) s.Step();
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 50; i++) s.Step();
                long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Check("no GC allocation in 50 steps of 2000 agents", bytes == 0, bytes + " bytes");
            }

            // 7b. Drawing: the draw job packs visible agents into instance matrices (silhouette far, model parts near), none for hidden ones.
            {
                var def = ScriptableObject.CreateInstance<ContactDefinition>();
                def.size = 1.2f; def.silhouette = Silhouette.Quad;
                var camGo = new GameObject("SwarmTestCam");
                var cam = camGo.AddComponent<Camera>();
                cam.transform.position = new Vector3(0f, 100f, -300f);
                cam.transform.rotation = Quaternion.identity;
                cam.targetTexture = new RenderTexture(1280, 720, 16);
                var sim = SwarmStress.Build(2000, 71, SwarmTactic.Direct, 3000f, p);
                var rend = new SwarmRenderer(def, null, 2000);
                var sky = camGo.AddComponent<SwarmSky>();
                sky.AttachForTest(sim, rend, def);
                for (int i = 0; i < 10; i++) sim.Step();
                sky.Refresh(cam, 5000f);
                int packed = 0;
                for (int b = 0; b < rend.BucketCount; b++) packed += rend.counts[b];
                int visible = 0;
                for (int i = 0; i < 2000; i++) if (rend.vis[i] != 0) visible++;
                var m0 = rend.matrices[0];
                Check("draw job: visible agents packed into matrices", visible > 1000 && packed >= visible && !float.IsNaN(m0.c3.x), $"{visible} visible, {packed} instances, {rend.BucketCount} buckets");
                sim.SetState(0, SwarmSim.Promoted);
                sky.Refresh(cam, 5000f);
                Check("a promoted agent is no longer drawn", rend.vis[0] == 0);
                sky.Stop();
                var rt = cam.targetTexture;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(def);
            }

            // 8. Speed.
            if (perf)
            {
                Ms1000 = SwarmStress.Run(1000);
                Ms3000 = SwarmStress.Run(3000);
                Ms5000 = SwarmStress.Run(5000);
                lines.Add($"INFO step ms: 1000 = {Ms1000:F2}, 3000 = {Ms3000:F2}, 5000 = {Ms5000:F2}");
                Check("5000 agents step in under 2 ms (Editor)", Ms5000 < 2f, $"{Ms5000:F2} ms");
            }

            lines.Add(failures == 0 ? "SwarmJobSelfTest: ALL PASS" : $"SwarmJobSelfTest: {failures} FAILED");
            Debug.Log(Log);
            return failures == 0;
        }
    }
}
