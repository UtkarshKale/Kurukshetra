using System.Collections.Generic;
using Chaukanna.Scoring;
using Unity.Mathematics;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // Checks the flapping birds without a scene: the code-built mesh, the flap poses and rates, a 3000-bird flock's step time and
    // flight (airborne, cohesive, deterministic), the group call and its scoring, and that no model asset is used for birds.
    // CLI: Chaukanna.Scenario.BirdSelfTest.Run()   (log in BirdSelfTest.Log, numbers in BirdSelfTest.Extra)
    public static class BirdSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();
        public static string Log => string.Join("\n", lines);
        public static string Extra = "";
        public static float Ms3000Bird, Ms3000Drone;

        static void Check(string name, bool ok, string detail = "")
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? "  [" + detail + "]" : ""));
        }

        static SwarmSim BuildFlock(int n, int seed, int species, out float3 roost)
        {
            var p = SwarmParams.Birds(BirdSpeciesInfo.Cruise(species), BirdSpeciesInfo.Spacing(species));
            var prof = SwarmProfile.Birds();
            prof.spawnSpacing = p.spacing; prof.altitudeSpread = Mathf.Max(20f, p.spacing * 6f);
            var offsets = SwarmPlanner.Layout(n, seed, prof, new Vector3(0f, 0f, 1f));
            var arr = new float3[n];
            for (int i = 0; i < n; i++) arr[i] = new float3(offsets[i].x, offsets[i].y, offsets[i].z);
            var centre = new float3(0f, 100f, -600f);
            roost = new float3(0f, 110f, 0f);
            var sim = new SwarmSim(centre, roost, arr, p, SwarmTactic.Direct) { Seed = (uint)seed };
            sim.EnableRoost(roost, 220f);
            return sim;
        }

        // Mean distance to the nearest neighbour (brute force, for a few hundred birds).
        static float MeanNearest(SwarmSim sim)
        {
            var p = sim.Pos; float sum = 0f;
            for (int i = 0; i < sim.Count; i++)
            {
                float best = float.MaxValue;
                for (int j = 0; j < sim.Count; j++) if (j != i) best = Mathf.Min(best, math.distancesq(p[i], p[j]));
                sum += Mathf.Sqrt(best);
            }
            return sum / sim.Count;
        }

        public static bool Run(bool perf = true)
        {
            failures = 0;
            lines.Clear();
            Extra = "";

            // 1. The mesh: expected vertex count for every species and pose, wing joints move, nothing degenerate.
            bool counts = true, valid = true;
            for (int s = 0; s < BirdSpeciesInfo.Count; s++)
                for (int po = 0; po < BirdSpeciesInfo.Poses; po++)
                {
                    var m = BirdMesh.Get(s, po);
                    if (m.vertexCount != BirdMesh.VertexCount(s)) counts = false;
                    if (m.triangles.Length / 3 < 100 || m.bounds.size.x < 0.3f) valid = false;
                }
            Check("mesh vertex count is as built (kite 114, pigeon and small bird 106, every pose)", counts && BirdMesh.Get(0, 0).vertexCount == 114 && BirdMesh.Get(1, 0).vertexCount == 106);
            Check("mesh has at least 100 triangles and about one unit of span", valid, $"{BirdMesh.Get(1, 0).triangles.Length / 3} triangles (pigeon)");
            Check("mesh is cached (same object on the second ask)", ReferenceEquals(BirdMesh.Get(1, 3), BirdMesh.Get(1, 3)));

            // 2. Flap poses differ: wings up (pose 0) is higher than wings down (pose 4); glide differs from both.
            var up = BirdMesh.Get(1, 0); var down = BirdMesh.Get(1, 4); var glide = BirdMesh.Get(1, BirdSpeciesInfo.GlidePose);
            Check("flap poses differ (wings up higher than wings down)", up.bounds.max.y > down.bounds.max.y + 0.1f, $"top {up.bounds.max.y:F2} vs {down.bounds.max.y:F2}");
            Check("glide pose differs from up and down", Mathf.Abs(glide.bounds.max.y - up.bounds.max.y) > 0.02f && Mathf.Abs(glide.bounds.max.y - down.bounds.max.y) > 0.02f);
            bool allDistinct = true;
            for (int a = 0; a < BirdSpeciesInfo.Poses; a++)
                for (int b = a + 1; b < BirdSpeciesInfo.Poses; b++)
                    if (Mathf.Abs(BirdMesh.Get(1, a).bounds.max.y - BirdMesh.Get(1, b).bounds.max.y) < 1e-4f && Mathf.Abs(BirdMesh.Get(1, a).bounds.size.x - BirdMesh.Get(1, b).bounds.size.x) < 1e-4f) allDistinct = false;
            Check("all 9 poses have a different wing shape", allDistinct);

            // 3. Flap rate falls as the bird gets bigger, and stays in 2..9 Hz.
            float kite = BirdFlight.FlapHz(0, BirdSpeciesInfo.Span(0), 0.5f), pig = BirdFlight.FlapHz(1, BirdSpeciesInfo.Span(1), 0.5f), spa = BirdFlight.FlapHz(2, BirdSpeciesInfo.Span(2), 0.5f);
            Check("flap rate scales with size (kite < pigeon < small bird)", kite < pig && pig < spa, $"{kite:F1} / {pig:F1} / {spa:F1} Hz");
            Check("flap rates are 2 to 9 Hz", kite >= 2f && spa <= 9f);
            Check("a bigger bird of one species beats slower", BirdFlight.FlapHz(1, 0.8f, 0.5f) < BirdFlight.FlapHz(1, 0.5f, 0.5f));
            int glideKite = 0, glidePigeon = 0, flapPoses = 0;
            var seen = new HashSet<int>();
            for (int k = 0; k < 2000; k++)
            {
                float t = k * 0.05f;
                int pk = BirdFlight.Pose(0, 1.5f, t, 77u), pp = BirdFlight.Pose(1, 0.65f, t, 77u);
                if (pk == BirdSpeciesInfo.GlidePose) glideKite++;
                if (pp == BirdSpeciesInfo.GlidePose) glidePigeon++;
                if (pp != BirdSpeciesInfo.GlidePose) seen.Add(pp);
                flapPoses += pp != BirdSpeciesInfo.GlidePose ? 1 : 0;
            }
            Check("flap-and-glide: a kite glides more of the time than a pigeon", glideKite > glidePigeon && glidePigeon > 0 && flapPoses > 0, $"kite glides {glideKite / 20}%, pigeon {glidePigeon / 20}%");
            Check("a beating bird goes through all 8 wing poses", seen.Count == 8, seen.Count + " poses seen");
            float other = 0f; for (int k = 0; k < 400; k++) if (BirdFlight.Pose(1, 0.65f, k * 0.05f, 1u) != BirdFlight.Pose(1, 0.65f, k * 0.05f, 2u)) other++;
            Check("two birds are out of step (own phase)", other > 100f, other + " of 400 samples differ");
            Check("a right turn banks right, a left turn banks left", BirdFlight.Bank(10f, 0.6f) > 0.3f && BirdFlight.Bank(10f, -0.6f) < -0.3f, $"{BirdFlight.Bank(10f, 0.6f) * Mathf.Rad2Deg:F0} deg");

            // 4. The 3000-bird flock: step time against 3000 drones.
            if (perf)
            {
                using (var sim = BuildFlock(3000, 4321, BirdSpeciesInfo.Pigeon, out _))
                {
                    for (int i = 0; i < 5; i++) sim.Step();
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    for (int i = 0; i < 50; i++) sim.Step();
                    Ms3000Bird = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency / 50f;
                }
                Ms3000Drone = SwarmStress.Run(3000);
                Check("3000-bird step costs about what 3000 drones cost", Ms3000Bird < Ms3000Drone * 3f + 1f && Ms3000Bird < 12f, $"birds {Ms3000Bird:F2} ms, drones {Ms3000Drone:F2} ms per 50 Hz step");
                Extra += $"3000 birds {Ms3000Bird:F2} ms/step, 3000 drones {Ms3000Drone:F2} ms/step. ";
            }

            // 5. Flight: airborne, cohesive, near the roost, at about cruise speed, never on top of each other.
            using (var sim = BuildFlock(400, 99, BirdSpeciesInfo.Pigeon, out float3 roost))
            {
                float nn0 = MeanNearest(sim);
                for (int i = 0; i < 2000; i++) sim.Step();       // 40 s
                var pos = sim.Pos; var vel = sim.Vel;
                float minY = float.MaxValue, maxR = 0f, spd = 0f;
                float3 c = float3.zero;
                for (int i = 0; i < sim.Count; i++) { c += pos[i]; minY = Mathf.Min(minY, pos[i].y); spd += math.length(vel[i]); }
                c /= sim.Count; spd /= sim.Count;
                for (int i = 0; i < sim.Count; i++) maxR = Mathf.Max(maxR, math.distance(pos[i], c));
                float nn = MeanNearest(sim), sep = sim.MinSeparation(60f), spacing = BirdSpeciesInfo.Spacing(BirdSpeciesInfo.Pigeon);
                Check("birds stay airborne", minY >= sim.P.minAbove - 0.5f && !float.IsNaN(c.x), $"lowest {minY:F1} m");
                Check("flock stays cohesive (nearest-neighbour distance in a band)", nn > 0.4f * spacing && nn < 3f * spacing, $"mean nearest {nn:F1} m at start {nn0:F1} m, spacing {spacing:F1} m");
                Check("flock is one body (no bird far from the centre)", maxR < 500f, $"furthest {maxR:F0} m from the centre");
                Check("flock stays over its roost", math.distance(c.xz, roost.xz) < 600f, $"centre {math.distance(c.xz, roost.xz):F0} m from the roost");
                Check("birds hold about cruise speed", spd > 0.6f * sim.P.vCruise && spd < 1.4f * sim.P.vCruise, $"{spd:F1} m/s, cruise {sim.P.vCruise:F1}");
                Check("no two birds on top of each other", sep > 0.2f, $"closest pair {sep:F2} m");
                float turn = 0f;
                for (int i = 0; i < sim.Count; i++) { float a = math.length(vel[i] - sim.PrevVel[i]) / sim.P.dt; turn = Mathf.Max(turn, a); }
                Check("turns are banked (acceleration within the bird limits)", turn < sim.P.aLat * 1.2f + sim.P.aLong + sim.P.noise + 6f, $"max {turn:F1} m/s^2");
            }

            // 6. Deterministic from the seed.
            using (var a = BuildFlock(300, 5, BirdSpeciesInfo.Sparrow, out _))
            using (var b = BuildFlock(300, 5, BirdSpeciesInfo.Sparrow, out _))
            using (var d = BuildFlock(300, 6, BirdSpeciesInfo.Sparrow, out _))
            {
                for (int i = 0; i < 150; i++) { a.Step(); b.Step(); d.Step(); }
                bool same = true, diff = false;
                for (int i = 0; i < 300; i++) { if (!a.Pos[i].Equals(b.Pos[i])) same = false; if (!a.Pos[i].Equals(d.Pos[i])) diff = true; }
                Check("same seed, same flight; another seed, another flight", same && diff);
            }

            // 7. Group call and its scoring.
            ContactRecord Rec(int id, ContactClass c, int gid) => new ContactRecord { id = id, truthClass = c, groupId = gid, spawnTime = 5f, firstInViewTime = 10f };
            var calls = new List<GroupCallRecord>();
            var flock = new List<ContactRecord> { Rec(1, ContactClass.Bird, 4), Rec(2, ContactClass.Bird, 4), Rec(3, ContactClass.Bird, 4) };
            var swarm = new List<ContactRecord> { Rec(11, ContactClass.HostileQuad, 7), Rec(12, ContactClass.HostileQuad, 7), Rec(13, ContactClass.HostileQuad, 7) };
            var mix = new List<ContactRecord> { Rec(21, ContactClass.Bird, -1), Rec(22, ContactClass.HostileQuad, -1), Rec(23, ContactClass.FriendlyISR, -1) };
            var r1 = GroupCalls.Declare(calls, flock, GroupCall.BirdFlock, 40f, out _);
            var r2 = GroupCalls.Declare(calls, swarm, GroupCall.BirdFlock, 50f, out _);          // wrong: it is a drone swarm
            var again = GroupCalls.Declare(calls, flock, GroupCall.DroneSwarm, 60f, out var existing);
            var r3 = GroupCalls.Declare(calls, mix, GroupCall.Mixed, 70f, out _);
            Check("right group call is recorded as right with its time", r1 != null && r1.correct && Mathf.Abs(r1.seconds - 30f) < 0.01f && r1.truth == "BirdFlock", r1 != null ? $"{r1.seconds:F0} s" : "null");
            Check("wrong group call is recorded as wrong", r2 != null && !r2.correct && r2.truth == "DroneSwarm");
            Check("a group is scored once (second call on it refused)", again == null && existing == r1 && calls.Count == 3);
            Check("a mix of birds and drones is truth Mixed", r3 != null && r3.correct && r3.truth == "Mixed");
            Check("a single contact is not a group", GroupCalls.Declare(calls, new List<ContactRecord> { Rec(31, ContactClass.Bird, -1) }, GroupCall.BirdFlock, 80f, out _) == null);
            var res = GroupCalls.Evaluate(calls);
            Check("group scoring counts right and wrong", res.calls == 3 && res.right == 2 && res.wrong == 1 && res.points == 2 * GroupCalls.RightPoints && res.maxPoints == 3 * GroupCalls.RightPoints, res.summary);
            var session = ScoreCalculator.Calculate(new List<ContactRecord>(), RuleSet.Defaults(), 120f, 1, null, RoeCard.WeaponsHold, Chaukanna.Net.SeatRole.Any, calls);
            var none = ScoreCalculator.Calculate(new List<ContactRecord>(), RuleSet.Defaults(), 120f, 1);
            Check("the session result carries the group node and its points", session.groupCall.happened && session.totalPoints - none.totalPoints == res.points && session.maxPoints - none.maxPoints == res.maxPoints);
            Check("no group calls, no node", !none.groupCall.happened);

            // 8. Old bird art is not on the bird spawn path.
            var def = ScriptableObject.CreateInstance<ContactDefinition>();
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            def.contactClass = ContactClass.Bird; def.size = 1.8f; def.prefab = cube; def.variants = new List<GameObject> { cube };
            var spawnerGo = new GameObject("BirdTestSpawner");
            var spawner = spawnerGo.AddComponent<ScenarioSpawner>();
            spawner.birdModels.Add(cube);
            Check("ScenarioSpawner ignores its bird models list", spawner.ModelsFor(ContactClass.Bird).Count == 0);
            Check("a Bird definition's prefab and variants are not used", ContactVisuals.PickPrefab(def, 3, spawner.birdModels) == null && ContactVisuals.ChoiceCount(def) == BirdSpeciesInfo.Count);
            var root = ContactVisuals.Build(def, out var mats, out _, out _, 5, spawner.birdModels);
            bool built = root.GetComponent<BirdBody>() != null && root.GetComponentInChildren<SkinnedMeshRenderer>() == null && root.GetComponent<MeshFilter>().sharedMesh.name.StartsWith("Bird_")
                && root.GetComponentsInChildren<MeshRenderer>().Length == 1;
            Check("a Bird contact wears the code-built bird (one mesh, no model, no skinned renderer)", built);
            var body = root.GetComponent<BirdBody>();
            Check("it has its own material copy (instancing on)", mats.Length == 1 && mats[0].enableInstancing);
            var crowdDefs = new ContactDefinition[5]; crowdDefs[(int)ContactClass.Bird] = def;
            using (var cr = new CrowdRenderer(crowdDefs, null, 10))
                Check("crowd birds are flap-pose buckets, not model parts", cr.BirdFirst >= CrowdRenderer.DotBuckets && cr.BucketCount == cr.BirdFirst + BirdSpeciesInfo.Count * BirdSpeciesInfo.Tints * BirdSpeciesInfo.Poses, $"{cr.BucketCount} buckets");
            using (var sr = new SwarmRenderer(BirdSpeciesInfo.Pigeon, 10))
                Check("a bird flock renderer has a silhouette plus 27 pose buckets", sr.BucketCount == 1 + BirdSpeciesInfo.Tints * BirdSpeciesInfo.Poses && sr.IsBird);
#if UNITY_EDITOR
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ContactDefinition>("Assets/Chaukanna/Data/Contact_Bird.asset");
            if (asset != null) Check("Contact_Bird.asset no longer links the old pigeon / jay", asset.prefab == null && (asset.variants == null || asset.variants.Count == 0), "prefab " + (asset.prefab != null ? asset.prefab.name : "none"));
#endif
            Object.DestroyImmediate(root);
            foreach (var m in mats) Object.DestroyImmediate(m);
            Object.DestroyImmediate(spawnerGo);
            Object.DestroyImmediate(cube);
            Object.DestroyImmediate(def);

            Extra += failures == 0 ? "all birds checks passed" : failures + " bird checks FAILED";
            return failures == 0;
        }
    }
}
