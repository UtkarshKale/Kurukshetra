using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // How a swarm (or a bird flock) flies together. Boids: keep apart, match heading, stay together, head for the target.
    [CreateAssetMenu(menuName = "Chaukanna/Swarm Profile", fileName = "SwarmProfile")]
    public class SwarmProfile : ScriptableObject
    {
        [Header("Boids")]
        [Tooltip("Members closer than this push apart, metres.")]
        public float separationRadius = 12f;
        [Tooltip("Members within this distance count as neighbours for heading and cohesion, metres.")]
        public float neighbourRadius = 45f;
        public float separationWeight = 2.5f;
        public float alignmentWeight = 0.5f;
        [Tooltip("Pull towards the neighbours' centre, per metre of offset.")]
        public float cohesionWeight = 0.06f;
        public float targetWeight = 1f;
        [Tooltip("Most a member can change its velocity per second, m/s².")]
        public float maxAcceleration = 10f;

        [Header("Shape at spawn")]
        [Tooltip("Distance between members when the swarm appears, metres.")]
        public float spawnSpacing = 14f;
        [Tooltip("Spread in height, metres (members sit within half of this above and below).")]
        public float altitudeSpread = 12f;
        [Tooltip("All members appear within this many seconds of each other.")]
        public float spawnStagger = 1.5f;

        [Header("Arrival and looks")]
        [Tooltip("A member this close to the target has arrived.")]
        public float arriveDistance = 15f;
        [Tooltip("Wing flaps per second relative to a lone bird (birds in a flock flap slower).")]
        public float flapRate = 1f;

        public static SwarmProfile Drones()
        {
            return CreateInstance<SwarmProfile>();
        }

        public static SwarmProfile Birds()
        {
            var p = CreateInstance<SwarmProfile>();
            p.separationRadius = 6f; p.neighbourRadius = 30f; p.alignmentWeight = 0.8f; p.cohesionWeight = 0.1f;
            p.spawnSpacing = 7f; p.altitudeSpread = 8f; p.flapRate = 0.55f; p.maxAcceleration = 6f;
            return p;
        }
    }

    // The boids step as pure maths, so tests can run it with a fixed time step and get the same answer every time.
    // All members' new velocities are worked out from the old state first, then everyone moves: the order of members
    // never changes the result.
    public static class Boids
    {
        public static void Step(Vector3[] pos, Vector3[] vel, int count, Vector3 target, float speed, SwarmProfile p, float dt, Vector3[] scratch)
        {
            float sepR = p.separationRadius, nbR = p.neighbourRadius;
            for (int i = 0; i < count; i++)
            {
                Vector3 sep = Vector3.zero, ali = Vector3.zero, coh = Vector3.zero;
                int n = 0;
                for (int j = 0; j < count; j++)
                {
                    if (j == i) continue;
                    Vector3 d = pos[j] - pos[i];
                    float dist = d.magnitude;
                    if (dist < nbR) { ali += vel[j]; coh += pos[j]; n++; }
                    if (dist < sepR)
                    {
                        // Stronger the closer they are; a direction is always defined, even when exactly on top of each other.
                        Vector3 away = dist > 1e-4f ? -d / dist : new Vector3(((i * 7 + j * 3) % 5) - 2f, 0.5f, ((i * 3 + j * 5) % 5) - 2f).normalized;
                        float push = (sepR - dist) / sepR;
                        sep += away * push * push * 4f + away * push;
                    }
                }
                Vector3 toTarget = target - pos[i];
                Vector3 desired = toTarget.sqrMagnitude > 1e-6f ? toTarget.normalized * speed : Vector3.zero;
                // Priority: keeping apart gets the acceleration first; heading, cohesion and the target share what is left.
                Vector3 sepAcc = sep * p.separationWeight * speed;
                if (sepAcc.magnitude > p.maxAcceleration) sepAcc = sepAcc.normalized * p.maxAcceleration;
                Vector3 rest = (desired - vel[i]) * p.targetWeight;
                if (n > 0) rest += (ali / n - vel[i]) * p.alignmentWeight + (coh / n - pos[i]) * p.cohesionWeight;
                float budget = Mathf.Max(0f, p.maxAcceleration - sepAcc.magnitude);
                if (rest.magnitude > budget) rest = rest.normalized * budget;
                Vector3 steer = sepAcc + rest;
                Vector3 v = vel[i] + steer * dt;
                float s = v.magnitude;
                if (s > speed * 1.3f) v = v / s * speed * 1.3f;
                else if (s < speed * 0.4f) v = (s > 1e-4f ? v / s : (desired.sqrMagnitude > 0f ? desired.normalized : Vector3.forward)) * speed * 0.4f;
                scratch[i] = v;
            }
            for (int i = 0; i < count; i++)
            {
                vel[i] = scratch[i];
                pos[i] += vel[i] * dt;
            }
        }

        public static float MinSeparation(Vector3[] pos, int count)
        {
            float min = float.MaxValue;
            for (int i = 0; i < count; i++)
                for (int j = i + 1; j < count; j++)
                    min = Mathf.Min(min, Vector3.Distance(pos[i], pos[j]));
            return min;
        }
    }

    // Where each member starts relative to the swarm's centre: a sunflower spiral (evenly spaced, no two on top of each
    // other) turned and jittered by the seed, in a vertical disc facing the direction of travel.
    public static class SwarmPlanner
    {
        public static Vector3[] Layout(int count, int seed, SwarmProfile p, Vector3 travel)
        {
            var rng = new System.Random(seed);
            var offsets = new Vector3[count];
            Vector3 fwd = travel.sqrMagnitude > 1e-6f ? travel.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;
            if (right.sqrMagnitude < 1e-6f) right = Vector3.right;
            float turn = (float)rng.NextDouble() * 360f;
            for (int i = 0; i < count; i++)
            {
                float a = (i * 137.508f + turn) * Mathf.Deg2Rad;
                float r = p.spawnSpacing * 0.62f * Mathf.Sqrt(i + 0.5f);
                float jitterX = ((float)rng.NextDouble() - 0.5f) * p.spawnSpacing * 0.2f;
                float jitterY = ((float)rng.NextDouble() - 0.5f) * p.spawnSpacing * 0.2f;
                float depth = ((float)rng.NextDouble() - 0.5f) * p.spawnSpacing * 0.5f;
                // A flat spiral in the horizontal plane (side and depth), with a little height spread.
                float x = Mathf.Cos(a) * r + jitterX;
                float z = Mathf.Sin(a) * r + depth * 0.2f;
                float y = ((float)rng.NextDouble() - 0.5f) * p.altitudeSpread + jitterY * 0.1f;
                offsets[i] = right * x + Vector3.up * y + fwd * z;
            }
            return offsets;
        }
    }

    // One swarm or flock in the air: moves its members together every frame. Members that are jammed or shot leave it.
    public class SwarmGroup
    {
        public readonly int id;
        public readonly SwarmProfile profile;
        public Vector3 target;
        public float speed;
        public string arrivalReason;           // why a member that reaches the target leaves ("ReachedDefendedPoint", "ExitedSector")
        public readonly List<ContactMotion> members = new List<ContactMotion>();
        public readonly List<Vector3> velocities = new List<Vector3>();
        Vector3[] pos = new Vector3[0], vel = new Vector3[0], scratch = new Vector3[0];

        public SwarmGroup(int id, SwarmProfile profile, Vector3 target, float speed, string arrivalReason)
        {
            this.id = id; this.profile = profile; this.target = target; this.speed = speed; this.arrivalReason = arrivalReason;
        }

        public void Add(ContactMotion m)
        {
            members.Add(m);
            Vector3 dir = target - m.transform.position;
            velocities.Add(dir.sqrMagnitude > 1e-6f ? dir.normalized * speed : Vector3.zero);
            m.Group = this;
        }

        public int Alive { get; private set; }

        // How many living members are nearer the target than this one (0 = the leader, nearest the target).
        public int RankByDistance(ContactMotion m)
        {
            if (m == null) return -1;
            float mine = Vector3.Distance(m.transform.position, target);
            int rank = 0;
            foreach (var o in members) if (o != null && o != m && Vector3.Distance(o.transform.position, target) < mine) rank++;
            return rank;
        }

        public void Step(float dt)
        {
            // Drop members that were destroyed or jammed (a jammed drone sinks on its own).
            for (int i = members.Count - 1; i >= 0; i--)
                if (members[i] == null || members[i].Jammed) { if (members[i] != null) members[i].Group = null; members.RemoveAt(i); velocities.RemoveAt(i); }
            int n = members.Count;
            Alive = n;
            if (n == 0 || dt <= 0f) return;
            if (pos.Length < n) { pos = new Vector3[n * 2]; vel = new Vector3[n * 2]; scratch = new Vector3[n * 2]; }
            for (int i = 0; i < n; i++) { pos[i] = members[i].transform.position; vel[i] = velocities[i]; }
            Boids.Step(pos, vel, n, target, speed, profile, dt, scratch);
            for (int i = n - 1; i >= 0; i--)
            {
                var m = members[i];
                velocities[i] = vel[i];
                m.FlyGroupStep(pos[i], vel[i], profile.flapRate);
                if (Vector3.Distance(pos[i], target) <= profile.arriveDistance)
                {
                    members.RemoveAt(i);
                    velocities.RemoveAt(i);
                    m.Group = null;
                    m.ArriveFromGroup(arrivalReason);
                }
            }
        }
    }
}
