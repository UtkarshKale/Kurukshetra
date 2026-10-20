using System.Collections.Generic;
using Chaukanna.Player;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // What the big swarm did in a run (trainer feed).
    public struct BigSwarmSummary
    {
        public int size, arrived, promoted, flying;
        public bool birds;
        public SwarmTactic tactic;
        public override string ToString() => birds ? $"Bird flock of {size}: {flying} still flying at the end, {promoted} picked out as real contacts" : $"Big swarm of {size} ({tactic}): {arrived} reached the post, {flying} still flying at the end, {promoted} picked out as real contacts";
    }

    // The big swarm of a run (more than SwarmSky.Threshold members): agents are data stepped by Burst jobs at 50 Hz (SwarmSim) and drawn
    // instanced (SwarmRenderer); there is no GameObject per drone. Like the dense-sky crowd, the soldier "finds" one by aiming at it
    // (Pick) or lassoing it, which promotes it to a real Contact that a SwarmFollower keeps on the agent's path. The swarm as a whole is
    // recorded through ContactRecord.swarmId on those contacts. Created by the ScenarioSpawner.
    [DefaultExecutionOrder(-40)]
    public class SwarmSky : MonoBehaviour
    {
        public static readonly List<SwarmSky> All = new List<SwarmSky>();   // the drone swarm and every big bird flock of this run
        public static SwarmSky Active => All.Count > 0 ? All[0] : null;
        public const int Threshold = 64;                    // a swarm of more than this many goes through this system
        public const int TrackBase = 200000;
        const int PickCandidates = 4;

        public SwarmSim Sim => sim;
        public SwarmRenderer Renderer => rend;
        public int GroupId { get; private set; }
        public int Promoted { get; private set; }
        public float LastCpuMs { get; private set; }         // steps + draw job + draw submission, this frame
        public float LastStepMs { get; private set; }
        public int DrawnInstances { get; private set; }
        public bool Started => sim != null;
        public bool IsBirds => birds;
        public int Species => species;
        public int Count => count;

        ScenarioSpawner spawner;
        SwarmSim sim;
        SwarmRenderer rend;
        ContactDefinition def;
        List<GameObject> models;
        int count, seed;
        Vector3 centre, target;
        float speed, startT;
        SwarmTactic tactic;
        FlightPath path;                  // a custom path from the 2D editor (null = the usual straight in / roost)
        float pathScale = 1f;
        Bounds pathBounds;
        bool birds;
        int species;
        ContactSensor sensor;
        float alpha;
        bool noted, hasResults;
        readonly List<int> scratch = new List<int>();
        readonly int[] pickIndex = new int[PickCandidates];
        readonly float[] pickAngle = new float[PickCandidates];

        // Sets up a swarm that will appear at scenario time startTime. Nothing is allocated until then.
        public void Begin(ScenarioSpawner owner, int groupId, ContactDefinition definition, List<GameObject> modelList, int agents, Vector3 start, Vector3 aim,
            float cruiseSpeed, float startTime, int seedValue, SwarmTactic how)
        {
            Stop();
            spawner = owner; GroupId = groupId; def = definition; models = modelList; count = agents; centre = start; target = aim;
            speed = cruiseSpeed; startT = startTime; seed = seedValue; tactic = how;
            Promoted = 0; noted = false; hasResults = false;
            path = null;
            if (!All.Contains(this)) All.Add(this);
            enabled = true;
        }

        // A big bird flock (more than Threshold birds): the same engine with the bird profile. It starts at start, flies to the roost and
        // circles it. Birds are never hostile; a bird picked out becomes a real Bird contact.
        public void BeginBirds(ScenarioSpawner owner, int groupId, ContactDefinition definition, int agents, Vector3 start, Vector3 roostPoint,
            float startTime, int seedValue, int speciesIndex)
        {
            Begin(owner, groupId, definition, null, agents, start, roostPoint, BirdSpeciesInfo.Cruise(speciesIndex), startTime, seedValue, SwarmTactic.Direct);
            birds = true; species = speciesIndex;
        }

        // This swarm / flock flies a path drawn on the map instead of heading straight for the post (or a roost). Call before it appears.
        public void SetPath(FlightPath p, float speedScale)
        {
            path = p != null && p.Usable ? p : null;
            pathScale = speedScale;
        }

        // Where the ground-height grid and the draw bounds sit: round the whole path when there is one.
        void Extent(out float2 mid, out float side, out Bounds bounds)
        {
            float minX = centre.x, maxX = centre.x, minZ = centre.z, maxZ = centre.z, minY = centre.y, maxY = centre.y;
            void Add(Vector3 v) { minX = Mathf.Min(minX, v.x); maxX = Mathf.Max(maxX, v.x); minZ = Mathf.Min(minZ, v.z); maxZ = Mathf.Max(maxZ, v.z); minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
            Add(target);
            if (path != null) for (int i = 0; i < path.points.Count; i++) Add(path.World(i));
            mid = new float2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            float spread = Mathf.Max(maxX - minX, maxZ - minZ);
            side = Mathf.Max(path != null ? spread * 1.25f + 1000f : 3f * Vector3.Distance(centre, target), 2f * sim.Radius + 2000f);
            bounds = new Bounds(new Vector3(mid.x, (minY + maxY) * 0.5f, mid.y), new Vector3(spread, maxY - minY, spread) + Vector3.one * (4f * sim.Radius + 4000f));
        }

        public void Stop()
        {
            if (sim != null) { sim.Dispose(); sim = null; }
            if (rend != null) { rend.Dispose(); rend = null; }
            All.Remove(this);
            hasResults = false;
        }

        void OnDestroy() { Stop(); }

        // The swarm's own clock: seconds since it appeared.
        float Clock => spawner.ScenarioTime - startT;

        void Create()
        {
            if (birds) { CreateBirds(); return; }
            var p = SwarmParams.Drones(Mathf.Max(5f, speed));
            var prof = SwarmProfile.Drones();
            prof.spawnSpacing = p.spacing;
            var offsets = SwarmPlanner.Layout(count, seed, prof, target - centre);
            var arr = new float3[count];
            for (int i = 0; i < count; i++) arr[i] = new float3(offsets[i].x, offsets[i].y, offsets[i].z);
            sim = new SwarmSim(centre, target, arr, p, path != null ? SwarmTactic.Direct : tactic);      // a drawn path replaces the tactic
            Extent(out float2 mid, out float side, out pathBounds);
            sim.SetHeights(mid, side, (x, z) =>
            {
                float g = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(new Vector3(x, 0f, z));
                return float.IsNaN(g) ? 0f : g;
            });
            if (path != null) sim.SetPath(path, pathScale);
            rend = new SwarmRenderer(def, models, count);
            Debug.Log($"[Swarm] {count} drones ({tactic}) appear at {startT:F0} s; {rend.BucketCount - 1} model parts, grid height field {side:F0} m wide.");
        }

        void CreateBirds()
        {
            var p = SwarmParams.Birds(Mathf.Max(5f, speed), BirdSpeciesInfo.Spacing(species));
            var prof = SwarmProfile.Birds();
            prof.spawnSpacing = p.spacing; prof.altitudeSpread = Mathf.Max(20f, p.spacing * 6f);
            var offsets = SwarmPlanner.Layout(count, seed, prof, target - centre);
            var arr = new float3[count];
            for (int i = 0; i < count; i++) arr[i] = new float3(offsets[i].x, offsets[i].y, offsets[i].z);
            sim = new SwarmSim(centre, target, arr, p, SwarmTactic.Direct) { Seed = (uint)seed };
            Extent(out float2 mid, out float side, out pathBounds);
            sim.SetHeights(mid, side, (x, z) =>
            {
                float g = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(new Vector3(x, 0f, z));
                return float.IsNaN(g) ? 0f : g;
            });
            if (path != null) sim.SetPath(path, pathScale);
            else sim.EnableRoost(new float3(target.x, target.y, target.z), 220f);
            rend = new SwarmRenderer(species, count);
            Debug.Log($"[Swarm] {count} {BirdSpeciesInfo.Name(species).ToLower()}s (a bird flock) appear at {startT:F0} s and circle a roost; {rend.BucketCount - 1} flap-pose buckets.");
        }

        Camera PlayerCamera()
        {
            if (sensor == null) sensor = FindAnyObjectByType<ContactSensor>();
            if (sensor != null) return sensor.GetComponent<Camera>();
            return Camera.main;
        }

        void LateUpdate()
        {
            if (spawner == null || spawner.Waiting) return;
            if (RunConditions.NetTeamClient) { RemoteLate(); return; }
            if (spawner.Finished)
            {
                if (!noted && sim != null)
                {
                    noted = true;
                    ContactRegistry.Note(Summary().ToString());
                }
                return;
            }
            float t = Clock;
            if (t < 0f) return;
            if (sim == null) Create();
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            sim.AdvanceTo(t);
            long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            LastStepMs = (t1 - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency;
            alpha = Mathf.Clamp01((t - sim.StepCount * sim.P.dt) / sim.P.dt);
            DrawFrame(t0);
        }

        // Camera, visibility and the instanced draw for this frame (the host's own sim and a trainee's copy of the host's swarm).
        void DrawFrame(long t0)
        {
            var cam = PlayerCamera();
            if (cam == null || !cam.isActiveAndEnabled) return;
            Refresh(cam, sensor != null ? sensor.EffectiveMaxRange : 2000f);
            var bounds = path != null ? pathBounds : new Bounds((centre + target) * 0.5f, Vector3.one * (Vector3.Distance(centre, target) + 4f * sim.Radius + 4000f));
            DrawnInstances = rend.Draw(cam, bounds);
            LastCpuMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency;
        }

        // --- LAN team mode, trainee's PC ---
        // The host owns the swarm. This PC never steps it: it keeps a sim object only as the data the draw job reads, and the host's
        // snapshots (LanSession -> SwarmSnapshot) write every agent's position and state into it. Drawn between the last two snapshots.
        bool[] remoteSeen;
        float remoteHostT = -1f, remoteSnapAt, remoteInterval = 0.5f;
        bool RunningNet => RunConditions.NetTeamClient;
        public bool RemoteReady => sim != null && remoteSeen != null;

        void RemoteLate()
        {
            if (spawner.Finished || sim == null || remoteSeen == null) return;
            alpha = Mathf.Clamp((Time.time - remoteSnapAt) / remoteInterval, 0f, 1.5f);
            DrawFrame(System.Diagnostics.Stopwatch.GetTimestamp());
        }

        // A chunk of the host's snapshot: agents start .. start+n-1.
        public void ApplyRemote(float hostT, int start, int n, Vector3[] pos, byte[] st)
        {
            if (spawner == null || n <= 0) return;
            if (sim == null)
            {
                Create();
                remoteSeen = new bool[sim.Count];
                var s0 = sim.State;
                for (int i = 0; i < sim.Count; i++) s0[i] = SwarmSim.Gone;       // not drawn until the host has said where it is
            }
            if (hostT > remoteHostT + 1e-3f)
            {
                if (remoteHostT >= 0f) remoteInterval = Mathf.Clamp(hostT - remoteHostT, 0.05f, 4f);
                remoteHostT = hostT;
                remoteSnapAt = Time.time;
            }
            else if (hostT < remoteHostT - 1e-3f) return;          // an older snapshot that arrived late
            var P = sim.Pos; var Q = sim.PrevPos; var V = sim.Vel; var PV = sim.PrevVel; var S = sim.State;
            float inv = 1f / remoteInterval;
            float3 aim = new float3(target.x, target.y, target.z);
            for (int k = 0; k < n; k++)
            {
                int i = start + k;
                if (i < 0 || i >= sim.Count) break;
                float3 np = new float3(pos[k].x, pos[k].y, pos[k].z);
                if (!remoteSeen[i])
                {
                    remoteSeen[i] = true;
                    Q[i] = np; P[i] = np;
                    float3 d = aim - np; d.y = 0f;
                    float3 v0 = math.lengthsq(d) > 1f ? math.normalize(d) * Mathf.Max(5f, speed) : new float3(0f, 0f, 1f);
                    V[i] = v0; PV[i] = v0;
                }
                else
                {
                    float3 old = P[i];
                    Q[i] = old; P[i] = np;
                    PV[i] = V[i];
                    float3 dv = np - old;
                    if (math.lengthsq(dv) > 1e-4f) V[i] = dv * inv;
                }
                S[i] = st[k];
            }
        }

        // Visibility and instance matrices for this camera (public so a test can run it without the spawner).
        public void Refresh(Camera cam, float maxRange)
        {
            float vfov = cam.fieldOfView * Mathf.Deg2Rad;
            if (birds)
            {
                var bj = new BirdDrawJob
                {
                    prevPos = sim.PrevPos, curPos = sim.Pos, prevVel = sim.PrevVel, curVel = sim.Vel, state = sim.State,
                    agents = sim.Count, bucketCount = rend.BucketCount, species = species, seed = (uint)seed, alpha = alpha, dt = sim.P.dt,
                    time = RunningNet ? remoteHostT + (Time.time - remoteSnapAt) : sim.Time + alpha * sim.P.dt, span = rend.Size, maxRange = maxRange, minPx = 0.3f, modelPx = BirdView.ModelPx, minSpanPx = BirdView.MinSpanPx,
                    pxPerMetreAtOne = cam.pixelHeight / (2f * Mathf.Tan(vfov * 0.5f)), camPos = cam.transform.position,
                    viewProj = (float4x4)(cam.projectionMatrix * cam.worldToCameraMatrix),
                    counts = rend.counts, starts = rend.starts, fill = rend.fill, stats = rend.stats, bucketOf = rend.bucketOf, vis = rend.vis, drawPos = rend.drawPos, matrices = rend.matrices,
                };
                bj.Run();
                hasResults = true;
                return;
            }
            var job = new SwarmDrawJob
            {
                prevPos = sim.PrevPos, curPos = sim.Pos, vel = sim.Vel, state = sim.State, partLocal = rend.partLocal,
                agents = sim.Count, bucketCount = rend.BucketCount, alpha = alpha, size = rend.Size, maxRange = maxRange, minPx = 0.3f, modelPx = 6f,
                pxPerMetreAtOne = cam.pixelHeight / (2f * Mathf.Tan(vfov * 0.5f)), camPos = cam.transform.position,
                viewProj = (float4x4)(cam.projectionMatrix * cam.worldToCameraMatrix),
                counts = rend.counts, starts = rend.starts, fill = rend.fill, stats = rend.stats, vis = rend.vis, drawPos = rend.drawPos, matrices = rend.matrices,
            };
            job.Run();
            hasResults = true;
        }

        // Self-test hook: a sky with a sim and a renderer made by hand, no spawner.
        public void AttachForTest(SwarmSim s, SwarmRenderer r, ContactDefinition d) { Stop(); sim = s; rend = r; def = d; All.Remove(this); }

        public BigSwarmSummary Summary() => new BigSwarmSummary
        {
            birds = birds,
            size = count, tactic = tactic, promoted = Promoted,
            arrived = sim != null ? sim.ArrivedCount : 0, flying = sim != null ? sim.FlyingCount + sim.PromotedFlying : 0,
        };

        // --- Reading the agents ---

        public Vector3 PositionOf(int i)
        {
            var p = sim.Pos[i]; var q = sim.PrevPos[i];
            var w = math.lerp(q, p, alpha);
            return new Vector3(w.x, w.y, w.z);
        }

        public Vector3 VelocityOf(int i) { var v = sim.Vel[i]; return new Vector3(v.x, v.y, v.z); }
        public bool IsDrawn(int i) => hasResults && rend != null && i >= 0 && i < sim.Count && rend.vis[i] != 0;
        public int TrackId(int index) => TrackBase + index;
        public bool IsFlying(int i) => sim != null && i >= 0 && i < sim.Count && (sim.State[i] == SwarmSim.Flying || sim.State[i] == SwarmSim.Promoted);
        public bool IsArrived(int i) => sim != null && sim.State[i] == SwarmSim.Arrived;
        public void MarkGone(int i) { if (sim != null && sim.State[i] == SwarmSim.Promoted) sim.SetState(i, SwarmSim.Gone); }
        public int RankOf(int i) => sim != null ? sim.RankByDistance(i) : -1;

        // --- Finding one (same tests as the dense-sky crowd) ---

        public int Pick(Camera cam, Vector3 aimDir, float maxAngleDeg, ContactSensor sense, out float angle)
        {
            angle = 999f;
            if (!hasResults || sense == null || sim == null) return -1;
            Vector3 eye = cam.transform.position;
            int found = 0;
            var vis = rend.vis; var dp = rend.drawPos;
            for (int i = 0; i < sim.Count; i++)
            {
                if (vis[i] == 0) continue;
                float3 p = dp[i];
                float ang = Vector3.Angle(aimDir, new Vector3(p.x, p.y, p.z) - eye);
                if (ang > maxAngleDeg) continue;
                int at = found;
                while (at > 0 && pickAngle[at - 1] > ang) at--;
                if (at >= PickCandidates) continue;
                for (int k = Mathf.Min(found, PickCandidates - 1); k > at; k--) { pickIndex[k] = pickIndex[k - 1]; pickAngle[k] = pickAngle[k - 1]; }
                pickIndex[at] = i; pickAngle[at] = ang;
                if (found < PickCandidates) found++;
            }
            for (int k = 0; k < found; k++)
            {
                int i = pickIndex[k];
                if (sense.CanSeePoint(PointOf(i), SizeForSense, out _, out _))
                {
                    angle = pickAngle[k];
                    return i;
                }
            }
            return -1;
        }

        float SizeForSense => birds && rend != null ? rend.Size : def != null ? def.size : 1f;

        Vector3 PointOf(int i) { float3 p = rend.drawPos[i]; return new Vector3(p.x, p.y, p.z); }

        public void InLoop(Camera cam, List<Vector2> loop, float near, float far, ContactSensor sense, List<int> result)
        {
            result.Clear();
            if (!hasResults || loop.Count < 3 || sim == null) return;
            LassoMath.Prepare(loop, out Vector2 min, out Vector2 max);
            int n = loop.Count;
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < sim.Count; i++)
            {
                if (rend.vis[i] == 0) continue;
                Vector3 p = PointOf(i);
                if (!LassoMath.InBand(Vector3.Distance(eye, p), near, far)) continue;
                Vector3 sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f || sp.x < min.x || sp.x > max.x || sp.y < min.y || sp.y > max.y) continue;
                if (!LassoMath.InsidePrepared(sp.x, sp.y, n)) continue;
                if (!sense.CanSeePoint(p, SizeForSense, out _, out _, false)) continue;
                result.Add(i);
            }
            Vector3 tp = target;
            result.Sort((a, b) => (PointOf(a) - tp).sqrMagnitude.CompareTo((PointOf(b) - tp).sqrMagnitude));
        }

        public int PromoteForLasso(Camera cam, List<Vector2> loop, float near, float far, ContactSensor sense, List<Contact> into, out int total)
        {
            InLoop(cam, loop, near, far, sense, scratch);
            total = scratch.Count;
            int made = 0;
            foreach (int i in scratch)
            {
                if (made >= CrowdSky.MaxLassoPromote) break;
                if (!sense.LineOfSight(PointOf(i))) continue;
                var c = Promote(i);
                if (c != null) { into.Add(c); made++; }
            }
            return made;
        }

        // Turns agent `index` into a real Contact on the same flight and marks it promoted: not drawn any more, still simulated (the
        // others keep their spacing round it), and a SwarmFollower moves the contact from the agent's data.
        public Contact Promote(int index)
        {
            // A trainee's PC: the host owns the swarm. Ask it; the new contact arrives like any other (LanSession.SendSwarmPick).
            if (RunConditions.NetTeamClient)
            {
                if (sim != null && index >= 0 && index < sim.Count && sim.State[index] == SwarmSim.Flying) Chaukanna.Net.LanSession.SendSwarmPick(GroupId, index, TrackId(index));
                return null;
            }
            if (spawner == null || sim == null || index < 0 || index >= sim.Count || sim.State[index] != SwarmSim.Flying) return null;
            var contact = spawner.SpawnCrowdContact(def.contactClass, PositionOf(index), 7 * (index % 3) + index % 7, TrackId(index));
            if (contact == null) return null;
            sim.SetState(index, SwarmSim.Promoted);
            Promoted++;
            if (birds) contact.Data.groupId = GroupId; else { contact.Data.swarmId = GroupId; contact.Data.groupId = GroupId; }
            contact.gameObject.AddComponent<SwarmFollower>().Init(this, index);
            return contact;
        }
    }

    // Keeps a promoted swarm drone's Contact on its agent's path. Jammed: ContactMotion sinks it and the agent leaves the swarm. Arrived
    // at the post: the contact despawns as ReachedDefendedPoint (a leaker, as for any swarm member).
    public class SwarmFollower : MonoBehaviour
    {
        SwarmSky sky;
        int index;
        Contact contact;
        ContactMotion motion;
        bool done;

        public void Init(SwarmSky owner, int i)
        {
            sky = owner; index = i;
            contact = GetComponent<Contact>();
            motion = GetComponent<ContactMotion>();
        }

        public int Rank() => sky != null ? sky.RankOf(index) : -1;

        void Update()
        {
            if (contact == null || sky == null || sky.Sim == null || done) return;
            if (motion != null && motion.Jammed) { sky.MarkGone(index); done = true; return; }
            if (sky.IsArrived(index)) { done = true; contact.Despawn("ReachedDefendedPoint"); return; }
            transform.position = sky.PositionOf(index);
            Vector3 v = sky.VelocityOf(index);
            if (v.x * v.x + v.z * v.z > 1e-4f) transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg, 0f);
        }

        void OnDestroy()
        {
            // Shot down or despawned: the agent leaves the swarm.
            if (sky != null && sky.Sim != null && !done) sky.MarkGone(index);
        }
    }
}
