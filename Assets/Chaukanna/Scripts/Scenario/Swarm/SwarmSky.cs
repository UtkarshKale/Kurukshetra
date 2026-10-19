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
            sim = new SwarmSim(centre, target, arr, p, tactic);
            float side = Mathf.Max(3f * Vector3.Distance(centre, target), 2f * sim.Radius + 2000f);
            sim.SetHeights(new float2((centre.x + target.x) * 0.5f, (centre.z + target.z) * 0.5f), side, (x, z) =>
            {
                float g = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(new Vector3(x, 0f, z));
                return float.IsNaN(g) ? 0f : g;
            });
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
            float side = Mathf.Max(3f * Vector3.Distance(centre, target), 2f * sim.Radius + 2000f);
            sim.SetHeights(new float2((centre.x + target.x) * 0.5f, (centre.z + target.z) * 0.5f), side, (x, z) =>
            {
                float g = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(new Vector3(x, 0f, z));
                return float.IsNaN(g) ? 0f : g;
            });
            sim.EnableRoost(new float3(target.x, target.y, target.z), 220f);
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
            if (RunConditions.NetTeamClient) return;
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
            var cam = PlayerCamera();
            if (cam == null || !cam.isActiveAndEnabled) return;
            Refresh(cam, sensor != null ? sensor.EffectiveMaxRange : 2000f);
            var bounds = new Bounds((centre + target) * 0.5f, Vector3.one * (Vector3.Distance(centre, target) + 4f * sim.Radius + 4000f));
            DrawnInstances = rend.Draw(cam, bounds);
            LastCpuMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency;
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
                    time = sim.Time + alpha * sim.P.dt, span = rend.Size, maxRange = maxRange, minPx = 0.3f, modelPx = 6f,
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
