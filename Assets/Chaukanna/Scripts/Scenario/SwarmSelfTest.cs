using System.Collections.Generic;
using Chaukanna.Scoring;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // Checks the swarm and flock code without a scene: the layout and the flight are the same for the same seed, no two
    // members ever come within a collision distance, the swarm reaches its target, and triage scoring counts right.
    // CLI: Chaukanna.Scenario.SwarmSelfTest.Run()
    public static class SwarmSelfTest
    {
        public const float CollisionDistance = 3f;
        static int failures;
        static readonly List<string> lines = new List<string>();

        // Flies `count` members from a layout to a target `distance` metres ahead with a fixed time step.
        // Returns the closest any two members came, and the final positions.
        public static float Fly(int count, int seed, SwarmProfile p, float distance, float speed, out Vector3[] final, out int arrived, out int steps)
        {
            var target = new Vector3(0f, 40f, distance);
            var start = new Vector3(0f, 60f, 0f);
            var offsets = SwarmPlanner.Layout(count, seed, p, target - start);
            var pos = new Vector3[count];
            var vel = new Vector3[count];
            var scratch = new Vector3[count];
            for (int i = 0; i < count; i++) { pos[i] = start + offsets[i]; vel[i] = (target - pos[i]).normalized * speed; }
            float min = Boids.MinSeparation(pos, count);
            arrived = 0;
            steps = 0;
            int alive = count;
            const float dt = 0.02f;
            while (alive > 0 && steps < 20000)
            {
                Boids.Step(pos, vel, alive, target, speed, p, dt, scratch);
                steps++;
                if (alive > 1) min = Mathf.Min(min, Boids.MinSeparation(pos, alive));
                // Members that arrive leave the group (as in the game), keeping the arrays packed.
                for (int i = alive - 1; i >= 0; i--)
                    if (Vector3.Distance(pos[i], target) <= p.arriveDistance)
                    {
                        arrived++;
                        alive--;
                        pos[i] = pos[alive]; vel[i] = vel[alive];
                    }
            }
            final = pos;
            return min;
        }

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var drones = SwarmProfile.Drones();
            var birds = SwarmProfile.Birds();

            var a = SwarmPlanner.Layout(24, 77, drones, Vector3.forward);
            var b = SwarmPlanner.Layout(24, 77, drones, Vector3.forward);
            var c = SwarmPlanner.Layout(24, 78, drones, Vector3.forward);
            bool same = true, differs = false;
            for (int i = 0; i < 24; i++) { if (a[i] != b[i]) same = false; if (a[i] != c[i]) differs = true; }
            Check("layout: same seed gives the same formation, another seed a different one", same && differs);
            float spawnMin = float.MaxValue;
            for (int i = 0; i < 24; i++) for (int j = i + 1; j < 24; j++) spawnMin = Mathf.Min(spawnMin, Vector3.Distance(a[i], a[j]));
            Check($"layout: no two members start closer than {CollisionDistance} m (closest {spawnMin:F1} m)", spawnMin > CollisionDistance);

            float min24 = Fly(24, 77, drones, 1000f, 14f, out var end1, out int arrived24, out int steps24);
            Fly(24, 77, drones, 1000f, 14f, out var end2, out _, out int steps24b);
            bool deterministic = steps24 == steps24b;
            for (int i = 0; i < end1.Length && deterministic; i++) if (end1[i] != end2[i]) deterministic = false;
            Check("flight: the same seed flies exactly the same way", deterministic);
            Check($"flight, 24 drones over 1 km: closest approach {min24:F1} m (> {CollisionDistance} m), {arrived24} of 24 arrive", min24 > CollisionDistance && arrived24 == 24);
            float min8 = Fly(8, 5, drones, 800f, 12f, out _, out int arrived8, out _);
            Check($"flight, 8 drones: closest approach {min8:F1} m, all arrive", min8 > CollisionDistance && arrived8 == 8);
            float min48 = Fly(48, 9, drones, 1000f, 14f, out _, out int arrived48, out _);
            Check($"flight, 48 drones: closest approach {min48:F1} m, all arrive", min48 > CollisionDistance && arrived48 == 48);
            float minBirds = Fly(12, 3, birds, 600f, 9f, out _, out int arrivedBirds, out _);
            Check($"flock of 12 birds (same code): closest approach {minBirds:F1} m, all arrive", minBirds > 1.5f && arrivedBirds == 12);
            Check("birds flap slower in a flock", birds.flapRate < drones.flapRate);

            // Triage: 4 swarm members; the player acted on the nearest twice and a third one; one leaked.
            var rules = RuleSet.Defaults();
            var recs = new List<ContactRecord>();
            for (int i = 0; i < 4; i++) recs.Add(new ContactRecord { id = i + 1, truthClass = ContactClass.HostileQuad, swarmId = 0, firstInViewTime = 10f + i });
            recs[0].swarmRank = 0; recs[0].action = PlayerAction.Engage; recs[0].actionTime = 15f;
            recs[1].swarmRank = 1; recs[1].action = PlayerAction.Jam; recs[1].actionTime = 18f;
            recs[2].swarmRank = 3; recs[2].action = PlayerAction.Engage; recs[2].actionTime = 20f;
            recs[3].despawnReason = "ReachedDefendedPoint"; recs[3].despawnTime = 40f;
            var t = ScoreCalculator.Triage(recs, rules);
            Check("triage: 4 in the swarm, 3 acted on, 2 of them nearest-first, 1 leaked", t.happened && t.size == 4 && t.actedOn == 3 && t.nearestFirst == 2 && t.leakers == 1);
            Check("triage: first action 5 s after the first was seen, 1 of 3 points", Mathf.Approximately(t.firstActionSeconds, 5f) && t.points == 1 && t.maxPoints == 3);
            Check("no swarm, no triage node", !ScoreCalculator.Triage(new[] { new ContactRecord { truthClass = ContactClass.Bird } }, rules).happened);

            var cfg = new ScenarioConfig { swarmSize = 24, birdFlocks = 2, flockSize = 8 };
            Check("a 24 swarm and 2 flocks of 8 count in the total contacts", cfg.TotalContacts == new ScenarioConfig().TotalContacts + 24 + 16);

            Object.DestroyImmediate(drones);
            Object.DestroyImmediate(birds);
            string summary = $"[SwarmSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
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
