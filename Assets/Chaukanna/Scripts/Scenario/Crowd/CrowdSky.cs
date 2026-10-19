using System.Collections.Generic;
using Chaukanna.Player;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // What the dense sky did in a run (trainer feed, dashboard, HTML report, session file).
    public struct CrowdSummary
    {
        public int tracks;              // crowd drones planned for the run
        public int peak;                // most airborne at once
        public int checkedCount;        // crowd drones the soldier picked out (promoted to real contacts)
        public int iffChallenges;       // contacts challenged with the I key (crowd or planned)
        public int friendliesHit;       // crowd drones that were engaged or jammed
        public override string ToString() =>
            $"Dense sky: {tracks} tracks (peak {peak} airborne), {checkedCount} checked, {iffChallenges} IFF challenges, {friendliesHit} friendlies engaged or jammed";
    }

    // The crowd layer for cooperative traffic (dense sky). No GameObject per drone: agents are data, moved in closed form by a
    // Burst job and drawn instanced. The soldier "finds" one by aiming at it (Pick), which promotes it to a real Contact.
    // Created by the ScenarioSpawner when a run has crowdCount > 0.
    [DefaultExecutionOrder(-50)]
    public class CrowdSky : MonoBehaviour
    {
        public static CrowdSky Active { get; private set; }
        public const int MaxLassoPromote = 48;
        public const int HeightGrid = 256;
        const int PickCandidates = 4;

        // Run totals, readable after the crowd is gone (the keeper stamps them on the result).
        public static int RunTracks { get; private set; }
        public static int RunPeak { get; private set; }

        // Measured each frame.
        public float LastCpuMs { get; private set; }        // jobs + bucket + draw submission, this frame
        public float LastJobMs { get; private set; }
        public int AliveCount { get; private set; }
        public int ModelAgents { get; private set; }        // drawn as models
        public int DotAgents { get; private set; }          // drawn as tiny silhouettes
        public int DrawnInstances { get; private set; }
        public int Promoted { get; private set; }
        public int[] BucketCounts { get; private set; } = new int[0];
        public int Count => agents.IsCreated ? agents.Length : 0;
        public CrowdRenderer Renderer => renderer;

        NativeArray<CrowdAgent> agents;
        NativeArray<byte> removed, lod, alive;
        NativeArray<float> heights, px, yaw;
        NativeArray<float3> pos;
        float2 gridOrigin;
        float invCell;
        int[] agentIds;
        CrowdRenderer renderer;
        ScenarioSpawner spawner;
        ContactSensor sensor;
        Vector3 post;
        float radius;
        bool noted, hasResults;
        float resultsTime;
        readonly List<int> scratch = new List<int>();

        // --- Setup ---

        // Full set-up for a run: generates the crowd, samples the ground, builds the renderer.
        public void Begin(ScenarioSpawner owner, int seed, int count, float crowdRadius, Vector3 postPosition, float ringRadius, float duration,
            float noIffPercent, ContactDefinition[] defs, List<GameObject>[] models, int[] ids)
        {
            Stop();
            spawner = owner;
            post = postPosition;
            radius = crowdRadius;
            var spec = CrowdGenerator.DefaultSpecs();
            for (int c = 0; c < 5 && defs != null && c < defs.Length; c++)
            {
                if (defs[c] == null) continue;
                var d = defs[c];
                spec[c] = new CrowdClassSpec(d.size, d.speedRange.x, d.speedRange.y, d.altitudeRange.x, d.altitudeRange.y,
                    ContactVisuals.ChoiceCount(d, models != null && c < models.Length ? models[c] : null));
            }
            var list = CrowdGenerator.Generate(seed, count, new float2(post.x, post.z), crowdRadius, duration, noIffPercent, spec);
            InitData(list, new float2(post.x, post.z), Mathf.Max(3f * ringRadius, 2f * crowdRadius), true);
            agentIds = ids;
            renderer = new CrowdRenderer(defs ?? new ContactDefinition[5], models, list.Length);
            BucketCounts = new int[renderer.BucketCount];
            sensor = null;
            RunTracks = list.Length;
            RunPeak = 0;
            noted = false;
            Active = this;
            enabled = true;
        }

        // Data only (no renderer, no spawner): what the self-tests use. gridSide = side of the square height grid round the post.
        public void InitData(CrowdAgent[] list, float2 postXz, float gridSide, bool sampleTerrain)
        {
            Dispose();
            int n = list.Length;
            agents = new NativeArray<CrowdAgent>(math.max(n, 1), Allocator.Persistent);
            for (int i = 0; i < n; i++) agents[i] = list[i];
            if (n == 0) agents.Dispose();
            int m = n;
            if (n > 0)
            {
                removed = new NativeArray<byte>(m, Allocator.Persistent);
                lod = new NativeArray<byte>(m, Allocator.Persistent);
                alive = new NativeArray<byte>(m, Allocator.Persistent);
                px = new NativeArray<float>(m, Allocator.Persistent);
                yaw = new NativeArray<float>(m, Allocator.Persistent);
                pos = new NativeArray<float3>(m, Allocator.Persistent);
            }
            heights = new NativeArray<float>(HeightGrid * HeightGrid, Allocator.Persistent);
            float side = math.max(gridSide, 100f);
            gridOrigin = postXz - side * 0.5f;
            float cell = side / (HeightGrid - 1);
            invCell = 1f / cell;
            if (sampleTerrain)
            {
                for (int j = 0; j < HeightGrid; j++)
                    for (int i = 0; i < HeightGrid; i++)
                    {
                        float g = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(new Vector3(gridOrigin.x + i * cell, 0f, gridOrigin.y + j * cell));
                        heights[j * HeightGrid + i] = float.IsNaN(g) ? 0f : g;
                    }
            }
            Promoted = 0;
            hasResults = false;
        }

        // Self-test only: give a data-only crowd a renderer (its bucket tables) so the bucket job can run; the crowd now owns it.
        public void AttachRendererForTest(CrowdRenderer r)
        {
            renderer = r;
            BucketCounts = new int[r.BucketCount];
        }

        public void StopForTest() { Dispose(); }

        public void SetHeight(int i, int j, float h) { heights[j * HeightGrid + i] = h; }
        public float2 GridOrigin => gridOrigin;
        public float InvCell => invCell;
        public NativeArray<float> Heights => heights;
        public NativeArray<CrowdAgent> Agents => agents;

        public void Stop()
        {
            if (Active == this) Active = null;
            Dispose();
            enabled = false;
        }

        void OnDestroy() { if (Active == this) Active = null; Dispose(); }

        void Dispose()
        {
            if (renderer != null) { renderer.Dispose(); renderer = null; }
            if (agents.IsCreated) agents.Dispose();
            if (removed.IsCreated) removed.Dispose();
            if (lod.IsCreated) lod.Dispose();
            if (alive.IsCreated) alive.Dispose();
            if (px.IsCreated) px.Dispose();
            if (yaw.IsCreated) yaw.Dispose();
            if (pos.IsCreated) pos.Dispose();
            if (heights.IsCreated) heights.Dispose();
            hasResults = false;
        }

        public static void ResetRunStats() { RunTracks = 0; RunPeak = 0; }

        // --- Frame ---

        Camera PlayerCamera()
        {
            if (sensor == null) sensor = FindAnyObjectByType<ContactSensor>();
            if (sensor != null) return sensor.GetComponent<Camera>();
            return Camera.main;
        }

        // The time the crowd runs on: the scenario clock, or on a LAN team client the host's clock (LanSession.HostTime), so every
        // PC sees the same sky without any positions being sent. Not drawn on a team client until the host's first snapshot.
        float Clock => Chaukanna.Net.LanSession.TeamClient ? Chaukanna.Net.LanSession.HostTime : spawner.ScenarioTime;

        void LateUpdate()
        {
            if (!agents.IsCreated || spawner == null || renderer == null) return;
            if (spawner.Waiting) return;
            if (Chaukanna.Net.LanSession.TeamClient && !Chaukanna.Net.LanSession.HostTimeValid) return;
            if (spawner.Finished)
            {
                if (!noted)
                {
                    noted = true;
                    ContactRegistry.Note(Summarize(ContactRegistry.Records, RunTracks, RunPeak).ToString());
                }
                return;
            }
            var cam = PlayerCamera();
            if (cam == null || !cam.isActiveAndEnabled) return;
            float maxRange = sensor != null ? sensor.EffectiveMaxRange : 2000f;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            Refresh(cam, Clock, maxRange, true);
            var bounds = new Bounds(post, Vector3.one * (2f * radius + 4000f));
            DrawnInstances = renderer.Draw(cam, bounds);
            LastCpuMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency;
        }

        // Runs the update job (and, with bucketing, the bucket job) for time t and waits for it. Public for the self-test.
        public void Refresh(Camera cam, float t, float maxRange, bool bucket)
        {
            if (!agents.IsCreated || Count == 0) return;
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            float vfov = cam.fieldOfView * Mathf.Deg2Rad;
            var update = new CrowdUpdateJob
            {
                agents = agents, removed = removed, heights = heights, gridOrigin = gridOrigin, invCell = invCell, gridN = HeightGrid,
                time = t, camPos = cam.transform.position,
                viewProj = (float4x4)(cam.projectionMatrix * cam.worldToCameraMatrix),
                pxPerMetreAtOne = cam.pixelHeight / (2f * Mathf.Tan(vfov * 0.5f)),
                maxRange = maxRange, minPx = 0.3f, modelPx = 6f,
                pos = pos, yaw = yaw, px = px, lod = lod, alive = alive,
            };
            JobHandle h = update.Schedule(agents.Length, 256);
            if (bucket && renderer != null)
            {
                var b = new CrowdBucketJob
                {
                    agents = agents, lod = lod, pos = pos, yaw = yaw, alive = alive,
                    modelFirst = renderer.modelFirst, modelParts = renderer.modelParts, partLocal = renderer.partLocal, dotBucket = renderer.dotBucket,
                    bucketCount = renderer.BucketCount, birdFirst = renderer.BirdFirst, time = t, counts = renderer.counts, starts = renderer.starts, fill = renderer.fill,
                    matrices = renderer.matrices, stats = renderer.stats,
                };
                h = b.Schedule(h);
            }
            h.Complete();
            hasResults = true;
            resultsTime = t;
            LastJobMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency;
            if (bucket && renderer != null)
            {
                AliveCount = renderer.stats[0]; ModelAgents = renderer.stats[1]; DotAgents = renderer.stats[2];
                for (int i = 0; i < BucketCounts.Length; i++) BucketCounts[i] = renderer.counts[i];
                if (AliveCount > RunPeak) RunPeak = AliveCount;
            }
        }

        // --- Reading the agents ---

        public bool IsAlive(int i) => hasResults && i >= 0 && i < Count && alive[i] != 0;
        public byte LodOf(int i) => lod[i];
        public Vector3 PositionOf(int i) => pos[i];
        public float YawOf(int i) => yaw[i];
        public float PixelsOf(int i) => px[i];
        public CrowdAgent AgentOf(int i) => agents[i];
        public bool IsRemoved(int i) => removed[i] != 0;

        // Where agent i is at time t (managed, same maths as the job): what a promoted drone follows.
        public Vector3 WorldPos(in CrowdAgent a, float t)
        {
            float3 w = CrowdMath.World(a, t, heights, gridOrigin, invCell, HeightGrid);
            return new Vector3(w.x, w.y, w.z);
        }

        public int TrackId(int index) => agentIds != null && index < agentIds.Length ? agentIds[index] : 100000 + index;

        // Marks an agent as taken out of the crowd (promoted): it is never drawn or picked again.
        public void MarkRemoved(int i)
        {
            if (i < 0 || i >= Count || removed[i] != 0) return;
            removed[i] = 1;
            alive[i] = 0;
            lod[i] = CrowdLod.None;
            Promoted++;
        }

        // --- Finding one ---

        // The crowd drone nearest the aim, within maxAngleDeg, that passes the same detection test as a contact (range, apparent
        // size scaled by FOV, inside the view, line of sight). The line-of-sight cast is only made for the best few. -1 = none.
        public int Pick(Camera cam, Vector3 aimDir, float maxAngleDeg, ContactSensor sense) => Pick(cam, aimDir, maxAngleDeg, sense, out _);

        public int Pick(Camera cam, Vector3 aimDir, float maxAngleDeg, ContactSensor sense, out float angle)
        {
            angle = 999f;
            if (!hasResults || sense == null) return -1;
            Vector3 eye = cam.transform.position;
            int found = 0;
            for (int i = 0; i < Count; i++)
            {
                if (lod[i] == CrowdLod.None) continue;
                float3 p = pos[i];
                float ang = Vector3.Angle(aimDir, new Vector3(p.x, p.y, p.z) - eye);
                if (ang > maxAngleDeg) continue;
                // keep the PickCandidates smallest angles, sorted
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
                if (sense.CanSeePoint(PositionOf(i), agents[i].size, out _, out _))
                {
                    angle = pickAngle[k];
                    return i;
                }
            }
            return -1;
        }

        readonly int[] pickIndex = new int[PickCandidates];
        readonly float[] pickAngle = new float[PickCandidates];

        // Crowd drones inside a lasso loop (screen pixels, bottom-left origin) and the depth band, that pass the cheap part of the
        // detection test. Sorted nearest the post first. No line-of-sight cast (see PromoteForLasso).
        public void InLoop(Camera cam, List<Vector2> loop, float near, float far, ContactSensor sense, List<int> result)
        {
            result.Clear();
            if (!hasResults || loop.Count < 3) return;
            LassoMath.Prepare(loop, out Vector2 min, out Vector2 max);
            int n = loop.Count;
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < Count; i++)
            {
                if (lod[i] == CrowdLod.None) continue;
                Vector3 p = PositionOf(i);
                if (!LassoMath.InBand(Vector3.Distance(eye, p), near, far)) continue;
                Vector3 sp = cam.WorldToScreenPoint(p);
                if (sp.z <= 0f || sp.x < min.x || sp.x > max.x || sp.y < min.y || sp.y > max.y) continue;
                if (!LassoMath.InsidePrepared(sp.x, sp.y, n)) continue;
                if (!sense.CanSeePoint(p, agents[i].size, out _, out _, false)) continue;
                result.Add(i);
            }
            Vector3 postPos = post;
            result.Sort((a, b) => (PositionOf(a) - postPos).sqrMagnitude.CompareTo((PositionOf(b) - postPos).sqrMagnitude));
        }

        // Promotes the crowd drones in a loop, up to MaxLassoPromote, nearest the post first (each one also has to pass the
        // line-of-sight test). total = how many were in the loop; the rest need a tighter loop.
        public int PromoteForLasso(Camera cam, List<Vector2> loop, float near, float far, ContactSensor sense, List<Contact> into, out int total)
        {
            InLoop(cam, loop, near, far, sense, scratch);
            total = scratch.Count;
            int made = 0;
            foreach (int i in scratch)
            {
                if (made >= MaxLassoPromote) break;
                if (!sense.LineOfSight(PositionOf(i))) continue;
                var c = Promote(i);
                if (c != null) { into.Add(c); made++; }
            }
            return made;
        }

        // A team client does not promote: it asks the host (CrowdPick), which promotes the agent on the shared sky. Its SpawnMsg
        // carries the crowd index, every client removes that agent from its own crowd, and the asking client locks the proxy.
        public static void RemotePromote(int index) { Chaukanna.Net.LanSession.SendCrowdPick(index); }

        // A team client, on the host's SpawnMsg for a promoted crowd drone: take the agent out of this PC's crowd and say where it
        // is on the host's clock. False when there is no such agent here (no crowd, wrong index).
        public bool TakeForRemote(int index, out CrowdAgent agent, out Vector3 position)
        {
            agent = default;
            position = Vector3.zero;
            if (!agents.IsCreated || index < 0 || index >= Count) return false;
            agent = agents[index];
            position = WorldPos(agent, Chaukanna.Net.LanSession.HostTime);
            MarkRemoved(index);
            return true;
        }

        // Turns agent `index` into a real Contact on the same path and marks it removed from the crowd.
        public Contact Promote(int index)
        {
            if (spawner == null || index < 0 || index >= Count || removed[index] != 0) return null;
            if (Chaukanna.Net.LanSession.TeamClient) { RemotePromote(index); return null; }
            CrowdAgent a = agents[index];
            float t = spawner.ScenarioTime;
            if (!CrowdMath.Alive(a, t)) return null;
            Vector3 p = WorldPos(a, t);
            int lookSeed = 7 * a.variant + index % 7;      // the contact picks its model from lookSeed / 7: the same look as the crowd drawing
            var contact = spawner.SpawnCrowdContact((ContactClass)a.cls, p, lookSeed, TrackId(index));
            if (contact == null) return null;
            MarkRemoved(index);
            contact.Data.fromCrowd = true;
            contact.Data.crowdIndex = index;
            contact.Data.crowdPattern = a.pattern;
            contact.Data.iffNoReply = a.iff == 1;
            contact.transform.rotation = Quaternion.Euler(0f, CrowdMath.Yaw(a, t) * Mathf.Rad2Deg, 0f);
            contact.gameObject.AddComponent<CrowdFollower>().Init(this, a);
            return contact;
        }

        // --- Summary ---

        public static CrowdSummary Summarize(IList<ContactRecord> records, int tracks, int peak)
        {
            var s = new CrowdSummary { tracks = tracks, peak = peak };
            foreach (var r in records)
            {
                if (r.iffChecked) s.iffChallenges++;
                if (!r.fromCrowd) continue;
                s.checkedCount++;
                if (r.action == PlayerAction.Engage || r.action == PlayerAction.Jam) s.friendliesHit++;
            }
            return s;
        }
    }
}
