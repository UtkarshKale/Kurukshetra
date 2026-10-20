using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Chaukanna.Scenario
{
    // How a big swarm attacks. Direct = fly at the post. Saturate = approach as one body, then spread to several bearings at a
    // set range and rush the post together. Split = approach, then divide into two bodies that come in from the two flanks.
    public enum SwarmTactic { Direct = 0, Saturate = 1, Split = 2 }

    // Numbers of the data-oriented swarm (SwarmSim). Blittable so a Burst job can carry it. Defaults are the drone profile.
    //   Reynolds 1987: separation (kSep), alignment (kAlign), cohesion (the attraction half of the pair force).
    //   Olfati-Saber 2006: one smooth pair force that is zero at the desired spacing, repulsive inside it, attractive outside.
    //   Vasarhelyi 2018: speed and acceleration limits, damped velocity alignment (friction), a floor above the ground.
    public struct SwarmParams
    {
        public float spacing, neighbourRadius, kPair, attractFactor, kSep, kAlign, kGoal;
        public float aMax, vCruise, vMax, vMin, arriveDist, minAbove, dt;
        public int maxNeighbours;
        // Bird profile (StarDisplay-like, Hildenbrandt 2010): bird != 0 turns on banked-turn limits and a small random perturbation.
        // aLat = most sideways acceleration (g * tan(max bank)), aLong = most acceleration along the flight direction, noise = m/s^2.
        public int bird;
        public float aLat, aLong, noise;

        // A flock: topological nearest 7 neighbours (Ballerini 2008), cohesion to them, alignment, a cruise speed to return to,
        // banked turns, a roost to circle (set with SwarmSim.EnableRoost), a little noise. spacing = metres between neighbours.
        public static SwarmParams Birds(float cruise = 10f, float spacing = 4f)
        {
            return new SwarmParams
            {
                spacing = spacing, neighbourRadius = math.max(12f, spacing * 3.5f), maxNeighbours = 7,
                kPair = 5f, attractFactor = 0.8f, kSep = 25f, kAlign = 1.2f, kGoal = 0.5f,
                aMax = 14f, vCruise = cruise, vMax = cruise * 1.35f, vMin = cruise * 0.65f,
                arriveDist = 0f, minAbove = 20f, dt = 0.02f,
                bird = 1, aLat = 12f, aLong = 4f, noise = 0.6f,
            };
        }

        public static SwarmParams Drones(float cruise = 20f)
        {
            return new SwarmParams
            {
                spacing = 14f, neighbourRadius = 31f, maxNeighbours = 7,
                kPair = 6f, attractFactor = 0.5f, kSep = 30f, kAlign = 0.8f, kGoal = 0.8f,
                aMax = 10f, vCruise = cruise, vMax = cruise * 1.3f, vMin = cruise * 0.4f,
                arriveDist = 15f, minAbove = 15f, dt = 0.02f,
            };
        }
    }

    public static class SwarmMath
    {
        public const int MaxGroups = 8;
        public const int HeightN = 128;

        public static uint Hash(int ix, int iy, int iz, uint mask)
        {
            unchecked { return ((uint)(ix * 73856093) ^ (uint)(iy * 19349663) ^ (uint)(iz * 83492791)) & mask; }
        }

        public static float Ground(NativeArray<float> h, float2 origin, float invCell, float x, float z)
        {
            float fx = math.clamp((x - origin.x) * invCell, 0f, HeightN - 1.001f);
            float fz = math.clamp((z - origin.y) * invCell, 0f, HeightN - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            float a = math.lerp(h[iz * HeightN + ix], h[iz * HeightN + ix + 1], tx);
            float b = math.lerp(h[(iz + 1) * HeightN + ix], h[(iz + 1) * HeightN + ix + 1], tx);
            return math.lerp(a, b, tz);
        }
    }

    // Uniform spatial hash grid, rebuilt every step by a counting sort in agent order (so the result never depends on thread timing).
    // Only agents with state 0 (flying) or 2 (promoted, still flying) are in it.
    [BurstCompile]
    public struct SwarmGridJob : IJob
    {
        [ReadOnly] public NativeArray<float3> pos;
        [ReadOnly] public NativeArray<byte> state;
        public float invCell;
        public uint mask;
        public NativeArray<int> cellStart;     // size table + 1
        public NativeArray<int> cursor;        // size table
        public NativeArray<int> cellOf;        // per agent, -1 = not in the grid
        public NativeArray<int> order;         // agent indices grouped by cell
        public int count;

        public void Execute()
        {
            int table = (int)mask + 1;
            for (int c = 0; c <= table; c++) cellStart[c] = 0;
            for (int i = 0; i < count; i++)
            {
                byte s = state[i];
                if (s != 0 && s != 2) { cellOf[i] = -1; continue; }
                float3 p = pos[i];
                int h = (int)SwarmMath.Hash((int)math.floor(p.x * invCell), (int)math.floor(p.y * invCell), (int)math.floor(p.z * invCell), mask);
                cellOf[i] = h;
                cellStart[h + 1]++;
            }
            for (int c = 0; c < table; c++) { cellStart[c + 1] += cellStart[c]; cursor[c] = cellStart[c]; }
            for (int i = 0; i < count; i++)
            {
                int h = cellOf[i];
                if (h < 0) continue;
                order[cursor[h]++] = i;
            }
        }
    }

    // One 50 Hz step for every agent in parallel. Reads the old arrays, writes the new ones: the order agents are processed in
    // cannot change the result.
    [BurstCompile]
    public struct SwarmStepJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> posIn, velIn;
        [ReadOnly] public NativeArray<byte> group;
        [ReadOnly] public NativeArray<float3> goals;
        [ReadOnly] public NativeArray<byte> goalFinal;
        [ReadOnly] public NativeArray<float> goalSpeed;
        [ReadOnly] public NativeArray<int> cellStart, order;
        [ReadOnly] public NativeArray<float> heights;
        public NativeArray<byte> state;                    // each agent writes only its own entry
        [WriteOnly] public NativeArray<float3> posOut, velOut;
        public SwarmParams p;
        public float invCell;
        public uint mask;
        public float2 hOrigin;
        public float hInvCell;
        public int stepIndex;
        public uint seed;

        public void Execute(int i)
        {
            byte st = state[i];
            float3 pos = posIn[i], vel = velIn[i];
            if (st != 0 && st != 2) { posOut[i] = pos; velOut[i] = vel; return; }

            int K = math.clamp(p.maxNeighbours, 1, 12);
            float r2max = p.neighbourRadius * p.neighbourRadius;
            FixedList64Bytes<float> bd = default;
            FixedList64Bytes<int> bi = default;
            for (int k = 0; k < K; k++) { bd.Add(float.MaxValue); bi.Add(-1); }
            FixedList128Bytes<int> seen = default;

            int cx = (int)math.floor(pos.x * invCell), cy = (int)math.floor(pos.y * invCell), cz = (int)math.floor(pos.z * invCell);
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int h = (int)SwarmMath.Hash(cx + dx, cy + dy, cz + dz, mask);
                        bool dup = false;
                        for (int s = 0; s < seen.Length; s++) if (seen[s] == h) { dup = true; break; }
                        if (dup) continue;
                        seen.Add(h);
                        int end = cellStart[h + 1];
                        for (int q = cellStart[h]; q < end; q++)
                        {
                            int j = order[q];
                            if (j == i) continue;
                            float3 d = posIn[j] - pos;
                            float d2 = math.lengthsq(d);
                            if (d2 >= r2max || d2 >= bd[K - 1]) continue;
                            int at = K - 1;
                            while (at > 0 && bd[at - 1] > d2) { bd[at] = bd[at - 1]; bi[at] = bi[at - 1]; at--; }
                            bd[at] = d2; bi[at] = j;
                        }
                    }

            float3 pair = float3.zero, sep = float3.zero, ali = float3.zero;
            int n = 0;
            float half = 0.5f * p.spacing;
            for (int k = 0; k < K; k++)
            {
                int j = bi[k];
                if (j < 0) break;
                float3 d = posIn[j] - pos;
                float r = math.sqrt(bd[k]);
                float3 dir = r > 1e-4f ? d / r : math.normalize(new float3(((i * 7 + j * 3) % 5) - 2f, 0.5f, ((i * 3 + j * 5) % 5) - 2f));
                float z = (r - p.spacing) / p.spacing;
                float f = z / math.sqrt(1f + z * z);                 // Olfati-Saber bump: 0 at the spacing, bounded
                if (f > 0f) f *= p.attractFactor;
                pair += dir * f * p.kPair;
                if (r < half) { float push = (half - r) / half; sep -= dir * push * push * p.kSep; }
                ali += velIn[j] - vel;
                n++;
            }

            int g = group[i];
            float3 goal = goals[g];
            float3 toGoal = goal - pos;
            float dist = math.length(toGoal);
            float cruise = goalSpeed[g];
            float3 desired = dist > 1e-3f ? toGoal / dist * cruise : float3.zero;
            float3 rest = pair + (desired - vel) * p.kGoal;
            if (n > 0) rest += ali / n * p.kAlign;
            // Priority: keeping apart gets the acceleration first; the rest shares what is left.
            float sl = math.length(sep);
            if (sl > p.aMax) sep = sep / sl * p.aMax;
            float budget = math.max(0f, p.aMax - math.length(sep));
            float rl = math.length(rest);
            if (rl > budget) rest = rest / math.max(rl, 1e-6f) * budget;

            float3 acc = sep + rest;
            if (p.bird != 0)
            {
                // A bird turns by banking: sideways acceleration is limited (g * tan(bank)), and so is the push along its heading.
                float sp0 = math.length(vel);
                float3 vd = sp0 > 1e-3f ? vel / sp0 : new float3(0f, 0f, 1f);
                var rn = new Unity.Mathematics.Random(math.hash(new uint2((uint)i + 1u, (uint)stepIndex * 2654435761u + seed)) | 1u);
                acc += rn.NextFloat3Direction() * p.noise;
                float along = math.clamp(math.dot(acc, vd), -p.aLong, p.aLong);
                float3 perp = acc - math.dot(acc, vd) * vd;
                float pl = math.length(perp);
                if (pl > p.aLat) perp *= p.aLat / pl;
                acc = along * vd + perp;
            }
            vel += acc * p.dt;
            float s2 = math.length(vel);
            if (s2 > p.vMax) vel = vel / s2 * p.vMax;
            else if (s2 < math.min(p.vMin, 0.8f * cruise)) vel = (s2 > 1e-4f ? vel / s2 : (dist > 1e-3f ? toGoal / dist : new float3(0f, 0f, 1f))) * math.min(p.vMin, 0.8f * cruise);
            pos += vel * p.dt;

            float floor = SwarmMath.Ground(heights, hOrigin, hInvCell, pos.x, pos.z) + p.minAbove;
            if (pos.y < floor) { pos.y = floor; vel.y = math.max(vel.y, 0f); }

            if (goalFinal[g] != 0 && math.distance(pos, goal) <= p.arriveDist) state[i] = 1;
            posOut[i] = pos;
            velOut[i] = vel;
        }
    }

    // Centroid and live count per group, plus totals: stats[0] flying (state 0), [1] promoted and flying, [2] arrived.
    [BurstCompile]
    public struct SwarmCentroidJob : IJob
    {
        [ReadOnly] public NativeArray<float3> pos;
        [ReadOnly] public NativeArray<byte> state, group;
        public NativeArray<float3> centroid;
        public NativeArray<int> count, stats;
        public int agents;

        public void Execute()
        {
            for (int g = 0; g < SwarmMath.MaxGroups; g++) { centroid[g] = float3.zero; count[g] = 0; }
            int a = 0, b = 0, c = 0;
            for (int i = 0; i < agents; i++)
            {
                byte s = state[i];
                if (s == 1) { c++; continue; }
                if (s != 0 && s != 2) continue;
                if (s == 0) a++; else b++;
                int g = group[i];
                centroid[g] += pos[i];
                count[g]++;
            }
            for (int g = 0; g < SwarmMath.MaxGroups; g++) if (count[g] > 0) centroid[g] /= count[g];
            stats[0] = a; stats[1] = b; stats[2] = c;
        }
    }

    // A swarm of any size as plain data: positions and velocities in NativeArrays, no GameObject, no per-step allocation. Stepped at a
    // fixed 50 Hz, so the same seed and the same calls give the same flight at any frame rate.
    // Agent state: 0 flying, 1 arrived at the target (gone), 2 flying and promoted to a real Contact (not drawn), 3 destroyed / jammed (gone).
    public sealed class SwarmSim : IDisposable
    {
        public const byte Flying = 0, Arrived = 1, Promoted = 2, Gone = 3;

        public readonly int Count;
        public SwarmParams P;
        public readonly SwarmTactic Tactic;
        public readonly float3 Target;
        public int Groups { get; private set; }
        public int StepCount { get; private set; }
        public int ArrivedCount { get; private set; }
        public int FlyingCount { get; private set; }         // flying and not promoted
        public int PromotedFlying { get; private set; }
        public float Radius { get; private set; }            // radius of the starting disc, m
        public int Phase(int g) => phase[g];
        public float3 Centroid(int g) => centroid[g];
        public int GroupCount(int g) => groupCount[g];
        public float3 Goal(int g) => goals[g];
        public float TacticRange;                            // m from the target where Saturate spreads / Split divides; 0 = from the size
        public float SpreadDegrees = 140f;
        public uint Seed;                                    // bird noise stream

        NativeArray<float3>[] pos = new NativeArray<float3>[2], vel = new NativeArray<float3>[2];
        int cur;
        NativeArray<byte> state, group, goalFinal;
        NativeArray<float> goalSpeed;
        NativeArray<float3> goals, centroid;
        NativeArray<int> groupCount, stats, cellStart, cursor, cellOf, order;
        NativeArray<float> heights;
        float2 hOrigin;
        float hInvCell;
        uint mask;
        int[] phase = new int[SwarmMath.MaxGroups];
        readonly float[] lenScratch = new float[SwarmMath.MaxGroups];
        float2 axis, right;                                  // approach direction on the ground (towards the target) and its right
        float3 start;
        float waypointSwitch;
        bool disposed;
        bool roostMode;
        float3 roost;
        float roostR;

        public NativeArray<float3> Pos => pos[cur];
        public NativeArray<float3> PrevPos => pos[1 - cur];
        public NativeArray<float3> Vel => vel[cur];
        public NativeArray<float3> PrevVel => vel[1 - cur];
        public bool RoostMode => roostMode;
        public NativeArray<byte> State => state;
        public float Time => StepCount * P.dt;

        // offsets: where each agent starts relative to centre (SwarmPlanner.Layout).
        public SwarmSim(float3 centre, float3 target, float3[] offsets, SwarmParams p, SwarmTactic tactic)
        {
            Count = offsets.Length;
            P = p;
            Tactic = tactic;
            Target = target;
            start = centre;
            int n = Count;
            int cap = math.max(n, 1);
            for (int k = 0; k < 2; k++) { pos[k] = new NativeArray<float3>(cap, Allocator.Persistent); vel[k] = new NativeArray<float3>(cap, Allocator.Persistent); }
            state = new NativeArray<byte>(cap, Allocator.Persistent);
            group = new NativeArray<byte>(cap, Allocator.Persistent);
            goalFinal = new NativeArray<byte>(SwarmMath.MaxGroups, Allocator.Persistent);
            goals = new NativeArray<float3>(SwarmMath.MaxGroups, Allocator.Persistent);
            goalSpeed = new NativeArray<float>(SwarmMath.MaxGroups, Allocator.Persistent);
            centroid = new NativeArray<float3>(SwarmMath.MaxGroups, Allocator.Persistent);
            groupCount = new NativeArray<int>(SwarmMath.MaxGroups, Allocator.Persistent);
            stats = new NativeArray<int>(4, Allocator.Persistent);
            uint table = 1024;
            while (table < 2u * (uint)cap) table <<= 1;
            mask = table - 1;
            cellStart = new NativeArray<int>((int)table + 1, Allocator.Persistent);
            cursor = new NativeArray<int>((int)table, Allocator.Persistent);
            cellOf = new NativeArray<int>(cap, Allocator.Persistent);
            order = new NativeArray<int>(cap, Allocator.Persistent);
            heights = new NativeArray<float>(SwarmMath.HeightN * SwarmMath.HeightN, Allocator.Persistent);
            hOrigin = new float2(-1e6f, -1e6f);
            hInvCell = (SwarmMath.HeightN - 1) / 2e6f;       // flat ground at 0 until SetHeights

            float2 flat = target.xz - centre.xz;
            axis = math.lengthsq(flat) > 1e-6f ? math.normalize(flat) : new float2(0f, 1f);
            right = new float2(axis.y, -axis.x);
            Groups = tactic == SwarmTactic.Direct ? 1 : tactic == SwarmTactic.Split ? 2 : math.clamp(n / 8, 1, 4);
            float rad = 0f;
            for (int i = 0; i < n; i++)
            {
                float3 o = offsets[i];
                pos[0][i] = centre + o;
                rad = math.max(rad, math.length(o.xz));
                int g = 0;
                float side = o.x * right.x + o.z * right.y, fwd = o.x * axis.x + o.z * axis.y;
                if (tactic == SwarmTactic.Split) g = side >= 0f ? 0 : 1;
                else if (tactic == SwarmTactic.Saturate)
                {
                    float a = math.atan2(side, fwd);                  // -pi..pi
                    g = math.clamp((int)((a + math.PI) / (2f * math.PI) * Groups), 0, Groups - 1);
                }
                group[i] = (byte)g;
            }
            Radius = rad;
            for (int g = 0; g < Groups; g++) { phase[g] = 0; SetGoalForPhase(g); }
            for (int i = 0; i < n; i++)
            {
                float3 d = goals[group[i]] - pos[0][i];
                float l = math.length(d);
                vel[0][i] = l > 1e-3f ? d / l * goalSpeed[group[i]] : new float3(0f, 0f, p.vCruise);
            }
            waypointSwitch = math.max(60f, 0.5f * rad);
            RefreshCentroids(pos[0]);
        }

        // Ground heights over a square of `side` metres centred on centreXz. fill(x, z) returns the ground height there.
        public void SetHeights(float2 centreXz, float side, Func<float, float, float> fill)
        {
            float cellSize = side / (SwarmMath.HeightN - 1);
            hOrigin = centreXz - side * 0.5f;
            hInvCell = 1f / cellSize;
            for (int j = 0; j < SwarmMath.HeightN; j++)
                for (int i = 0; i < SwarmMath.HeightN; i++)
                    heights[j * SwarmMath.HeightN + i] = fill(hOrigin.x + i * cellSize, hOrigin.y + j * cellSize);
        }

        float Range => TacticRange > 0f ? TacticRange : math.max(600f, 1.5f * Radius);

        // The aim point of group g in phase ph (0 = approach, 1 = spread / flank, 2 = the target itself).
        float3 GoalOf(int g, int ph)
        {
            float3 t = Target;
            float2 back = -axis;
            float rs = Range;
            if (Tactic == SwarmTactic.Direct || ph >= 2) return t;
            if (Tactic == SwarmTactic.Saturate)
            {
                if (ph == 0) { float2 p0 = t.xz + back * (rs * 1.5f); return new float3(p0.x, t.y + 30f, p0.y); }
                float frac = Groups > 1 ? (float)g / (Groups - 1) - 0.5f : 0f;
                float ang = math.radians(SpreadDegrees) * frac;
                float2 dir = new float2(back.x * math.cos(ang) - back.y * math.sin(ang), back.x * math.sin(ang) + back.y * math.cos(ang));
                float2 p1 = t.xz + dir * rs;
                return new float3(p1.x, t.y + 30f + 10f * (g % 3), p1.y);
            }
            float side = g == 0 ? 1f : -1f;
            if (ph == 0) { float2 q0 = t.xz + back * rs; return new float3(q0.x, t.y + 30f, q0.y); }
            float2 q1 = t.xz + back * (rs * 0.4f) + right * (side * math.max(250f, rs * 0.35f));
            return new float3(q1.x, t.y + 30f, q1.y);
        }

        // Sets goal, final flag and speed of group g for its current phase, then re-times every group so that all of them arrive together
        // (time on target): the longest remaining path flies at cruise speed, shorter ones slower.
        void SetGoalForPhase(int g)
        {
            if (Tactic != SwarmTactic.Direct && phase[g] == 0 && math.distance(start.xz, Target.xz) <= Range * (Tactic == SwarmTactic.Saturate ? 1.5f : 1f)) phase[g] = 1;
            goals[g] = GoalOf(g, phase[g]);
            goalFinal[g] = (byte)(phase[g] >= 2 || Tactic == SwarmTactic.Direct ? 1 : 0);
            RetimeGroups();
        }

        void RetimeGroups()
        {
            float longest = 1f;
            var len = lenScratch;
            for (int g = 0; g < Groups; g++)
            {
                float3 from = groupCount[g] > 0 ? centroid[g] : start;
                float l = 0f;
                for (int ph = phase[g]; ph <= (Tactic == SwarmTactic.Direct ? 0 : 2); ph++)
                {
                    float3 to = GoalOf(g, ph);
                    l += math.distance(from, to);
                    from = to;
                }
                len[g] = l;
                longest = math.max(longest, l);
            }
            for (int g = 0; g < Groups; g++) goalSpeed[g] = P.vCruise * math.clamp(len[g] / longest, 0.5f, 1f);
        }

        // Bird flocks do not seek a target: each group heads for a roost and then circles it (the aim point runs ahead of the group on a
        // circle round the roost, which itself drifts slowly), so the flock wanders over the sky and never arrives anywhere.
        public void EnableRoost(float3 centre, float radius)
        {
            roostMode = true; roost = centre; roostR = math.max(radius, 30f);
            UpdateRoost();
        }

        void UpdateRoost()
        {
            float t = Time;
            for (int g = 0; g < Groups; g++)
            {
                float3 c = groupCount[g] > 0 ? centroid[g] : start;
                float2 rc = roost.xz + new float2(math.sin(t * 0.021f + g), math.cos(t * 0.017f + 2f * g)) * roostR * 0.4f;
                float R = roostR * (1f + 0.25f * math.sin(t * 0.03f + g));
                float2 d = c.xz - rc;
                float len = math.length(d);
                float2 aim;
                if (len > 1.6f * R) aim = rc;
                else { float ang = math.atan2(d.y, d.x) + 0.6f; aim = rc + R * new float2(math.cos(ang), math.sin(ang)); }
                goals[g] = new float3(aim.x, roost.y + 10f * math.sin(t * 0.05f + g), aim.y);
                goalFinal[g] = 0;
                goalSpeed[g] = P.vCruise;
            }
        }

        // --- A custom path from the 2D editor: the (single) group's aim point runs from waypoint to waypoint. The aim point and the speed
        // are plain per-group numbers the Burst step job already reads, so this costs one comparison per step however many agents fly. ---
        float[] pathX, pathZ, pathAlt, pathSpeed;
        PathMode pathMode;
        PathCursor pathCur;
        bool pathActive, pathFinal;
        float pathScale = 1f;

        public bool HasPath => pathActive;

        // Call after SetHeights (the aim height is the ground under the waypoint plus its height above ground).
        public void SetPath(FlightPath p, float speedScale)
        {
            if (p == null || !p.Usable) return;
            int n = p.points.Count;
            pathX = new float[n]; pathZ = new float[n]; pathAlt = new float[n]; pathSpeed = new float[n];
            float lo = P.vCruise, hi = P.vCruise;
            for (int i = 0; i < n; i++)
            {
                var w = p.points[i];
                pathX[i] = w.x; pathZ[i] = w.z; pathAlt[i] = w.alt; pathSpeed[i] = w.speed;
                if (w.speed > 0f) { lo = math.min(lo, w.speed * speedScale); hi = math.max(hi, w.speed * speedScale); }
            }
            // No cap on typed speeds: the limits widen to fit the fastest and slowest leg.
            P.vMax = math.max(P.vMax, hi * (P.bird != 0 ? 1.35f : 1.3f));
            P.vMin = math.min(P.vMin, lo * 0.4f);
            pathMode = p.mode; pathCur = PathCursor.Start(); pathFinal = false; pathActive = true; pathScale = speedScale; roostMode = false;
            ApplyPathGoal();
        }

        void ApplyPathGoal()
        {
            int i = pathCur.index;
            float gy = SwarmMath.Ground(heights, hOrigin, hInvCell, pathX[i], pathZ[i]) + pathAlt[i];
            float sp = pathSpeed[i] > 0f ? pathSpeed[i] * pathScale : P.vCruise;
            for (int g = 0; g < Groups; g++)
            {
                goals[g] = new float3(pathX[i], gy, pathZ[i]);
                goalSpeed[g] = sp;
                goalFinal[g] = (byte)(pathFinal && P.bird == 0 ? 1 : 0);      // a flock never arrives: it settles into a roost
            }
        }

        void UpdatePath()
        {
            if (pathFinal) return;
            if (groupCount[0] > 0)
            {
                int i = pathCur.index;
                float3 c = centroid[0];
                if (math.distance(c.xz, new float2(pathX[i], pathZ[i])) < waypointSwitch)
                {
                    pathCur.Arrive(pathX.Length, pathMode);
                    if (pathCur.done)
                    {
                        pathFinal = true;
                        if (P.bird != 0) { pathActive = false; EnableRoost(new float3(pathX[i], goals[0].y, pathZ[i]), 150f); return; }
                    }
                }
            }
            ApplyPathGoal();
        }

        // Moves a group on to its next phase when its centre has reached the current waypoint.
        void UpdateTactics()
        {
            if (pathActive) { UpdatePath(); return; }
            if (roostMode) { UpdateRoost(); return; }
            if (Tactic == SwarmTactic.Direct) return;
            for (int g = 0; g < Groups; g++)
            {
                if (goalFinal[g] != 0 || groupCount[g] == 0) continue;
                float3 c = centroid[g];
                if (math.distance(c.xz, goals[g].xz) < waypointSwitch) { phase[g]++; SetGoalForPhase(g); }
            }
        }

        void RefreshCentroids(NativeArray<float3> p)
        {
            new SwarmCentroidJob { pos = p, state = state, group = group, centroid = centroid, count = groupCount, stats = stats, agents = Count }.Run();
            FlyingCount = stats[0]; PromotedFlying = stats[1]; ArrivedCount = stats[2];
        }

        // One 50 Hz step.
        public void Step()
        {
            if (disposed) return;
            int n = Count;
            var read = pos[cur];
            var write = pos[1 - cur];
            float invCell = 1f / math.max(P.neighbourRadius, 1f);
            var grid = new SwarmGridJob { pos = read, state = state, invCell = invCell, mask = mask, cellStart = cellStart, cursor = cursor, cellOf = cellOf, order = order, count = n };
            var step = new SwarmStepJob
            {
                posIn = read, velIn = vel[cur], group = group, goals = goals, goalFinal = goalFinal, goalSpeed = goalSpeed, cellStart = cellStart, order = order,
                heights = heights, state = state, posOut = write, velOut = vel[1 - cur], p = P, invCell = invCell, mask = mask,
                hOrigin = hOrigin, hInvCell = hInvCell, stepIndex = StepCount, seed = Seed,
            };
            var cen = new SwarmCentroidJob { pos = write, state = state, group = group, centroid = centroid, count = groupCount, stats = stats, agents = n };
            JobHandle h = grid.Schedule();
            h = step.Schedule(n, 64, h);
            h = cen.Schedule(h);
            h.Complete();
            cur = 1 - cur;
            StepCount++;
            FlyingCount = stats[0]; PromotedFlying = stats[1]; ArrivedCount = stats[2];
            UpdateTactics();
        }

        // Runs whole steps until the swarm's own clock reaches t (seconds since it appeared), at most maxSteps of them.
        public int AdvanceTo(float t, int maxSteps = 10)
        {
            int done = 0;
            while (done < maxSteps && (StepCount + 1) * P.dt <= t) { Step(); done++; }
            return done;
        }

        public void SetState(int i, byte s) { if (i >= 0 && i < Count) state[i] = s; }

        // How many flying members are nearer the target than agent i (0 = the leader).
        public int RankByDistance(int i)
        {
            var p = pos[cur];
            float mine = math.distancesq(p[i], Target);
            int rank = 0;
            for (int k = 0; k < Count; k++)
                if (k != i && (state[k] == Flying || state[k] == Promoted) && math.distancesq(p[k], Target) < mine) rank++;
            return rank;
        }

        // Closest pair of flying agents, found with a managed hash grid (for tests at thousands of agents; allocates). `within`
        // is the search radius: anything further apart than that is ignored.
        public float MinSeparation(float within)
        {
            var p = pos[cur];
            var cells = new Dictionary<long, List<int>>();
            float inv = 1f / within;
            for (int i = 0; i < Count; i++)
            {
                if (state[i] != Flying && state[i] != Promoted) continue;
                var q = p[i];
                long k = CellKey((int)math.floor(q.x * inv), (int)math.floor(q.y * inv), (int)math.floor(q.z * inv));
                if (!cells.TryGetValue(k, out var l)) cells[k] = l = new List<int>();
                l.Add(i);
            }
            float min = float.MaxValue;
            foreach (var kv in cells)
                foreach (int i in kv.Value)
                {
                    var q = p[i];
                    int cx = (int)math.floor(q.x * inv), cy = (int)math.floor(q.y * inv), cz = (int)math.floor(q.z * inv);
                    for (int dz = -1; dz <= 1; dz++)
                        for (int dy = -1; dy <= 1; dy++)
                            for (int dx = -1; dx <= 1; dx++)
                                if (cells.TryGetValue(CellKey(cx + dx, cy + dy, cz + dz), out var l))
                                    foreach (int j in l) if (j > i) min = math.min(min, math.distancesq(q, p[j]));
                }
            return min == float.MaxValue ? float.MaxValue : math.sqrt(min);
        }

        static long CellKey(int x, int y, int z) => ((long)(x + 100000) << 40) ^ ((long)(y + 100000) << 20) ^ (long)(z + 100000);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            for (int k = 0; k < 2; k++) { if (pos[k].IsCreated) pos[k].Dispose(); if (vel[k].IsCreated) vel[k].Dispose(); }
            if (state.IsCreated) state.Dispose();
            if (group.IsCreated) group.Dispose();
            if (goalFinal.IsCreated) goalFinal.Dispose();
            if (goals.IsCreated) goals.Dispose();
            if (goalSpeed.IsCreated) goalSpeed.Dispose();
            if (centroid.IsCreated) centroid.Dispose();
            if (groupCount.IsCreated) groupCount.Dispose();
            if (stats.IsCreated) stats.Dispose();
            if (cellStart.IsCreated) cellStart.Dispose();
            if (cursor.IsCreated) cursor.Dispose();
            if (cellOf.IsCreated) cellOf.Dispose();
            if (order.IsCreated) order.Dispose();
            if (heights.IsCreated) heights.Dispose();
        }
    }
}
