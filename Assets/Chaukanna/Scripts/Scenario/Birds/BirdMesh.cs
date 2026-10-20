using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // Species of the code-built bird. 0 = black kite (large, long glides, forked tail), 1 = pigeon / crow class (medium),
    // 2 = small passerine (sparrow, myna, starling: small, fast beat, short glides).
    public static class BirdSpeciesInfo
    {
        public const int Count = 3, Poses = 9, GlidePose = 8, Tints = 3;
        public const int Kite = 0, Pigeon = 1, Sparrow = 2;

        public static string Name(int s) => s == Kite ? "Kite" : s == Pigeon ? "Pigeon" : "Sparrow";

        // Typical wingspan, metres.
        public static float Span(int s) => s == Kite ? 1.5f : s == Pigeon ? 0.65f : 0.25f;

        // Wingbeat frequency at the typical span, Hz. Pennycuick 1990: f falls as span grows (about span^-0.9 here).
        public static float BaseHz(int s) => s == Kite ? 2.5f : s == Pigeon ? 5.5f : 8f;

        // Cruise speed of a flock of this species, m/s.
        public static float Cruise(int s) => s == Kite ? 9f : s == Pigeon ? 11f : 10f;

        // Spacing between neighbours in a flock, metres.
        public static float Spacing(int s) => s == Kite ? 9f : s == Pigeon ? 4f : 2.5f;

        // Flap-glide: a burst of beats, then a glide. Kites soar (long glides); small birds bound (short pauses).
        public static void Cycle(int s, out float beatsMin, out float beatsMax, out float glideMin, out float glideMax)
        {
            if (s == Kite) { beatsMin = 2f; beatsMax = 4f; glideMin = 1.0f; glideMax = 3.0f; }
            else if (s == Pigeon) { beatsMin = 5f; beatsMax = 10f; glideMin = 0.3f; glideMax = 0.9f; }
            else { beatsMin = 4f; beatsMax = 8f; glideMin = 0.1f; glideMax = 0.3f; }
        }

        public static Color Tint(int s, int t)
        {
            t = Mathf.Clamp(t, 0, Tints - 1);
            if (s == Kite) return t == 0 ? new Color(0.30f, 0.21f, 0.14f) : t == 1 ? new Color(0.38f, 0.28f, 0.18f) : new Color(0.24f, 0.19f, 0.15f);
            if (s == Pigeon) return t == 0 ? new Color(0.46f, 0.48f, 0.53f) : t == 1 ? new Color(0.30f, 0.32f, 0.37f) : new Color(0.60f, 0.60f, 0.62f);
            return t == 0 ? new Color(0.40f, 0.30f, 0.22f) : t == 1 ? new Color(0.30f, 0.26f, 0.20f) : new Color(0.50f, 0.40f, 0.30f);
        }
    }

    // How one bird beats its wings. Pure functions of (species, size, time, seed): the same on a GameObject bird, in a Burst
    // job and in a replay. Beats come in bursts of a few, with a glide between (Rayner 1985), each bird with its own phase.
    public static class BirdFlight
    {
        public static float Hash01(uint h, uint salt)
        {
            return (math.hash(new uint2(h, salt * 2654435761u + 1u)) & 0xFFFFFFu) * (1f / 16777216f);
        }

        public static uint Mix(int index, uint seed)
        {
            return math.hash(new uint2((uint)index * 747796405u + 1u, seed * 2891336453u + 3u));
        }

        // Wingbeat frequency, Hz. span = this bird's span in metres (bigger birds beat slower), h1 = the bird's own 0..1 draw.
        public static float FlapHz(int species, float span, float h1)
        {
            return BirdSpeciesInfo.BaseHz(species) * math.pow(BirdSpeciesInfo.Span(species) / math.max(span, 0.05f), 0.9f) * (0.95f + 0.1f * h1);
        }

        // Pose index 0..7 = one wingbeat (0 = wings up); 8 = gliding.
        public static int Pose(int species, float span, float t, uint seed)
        {
            float f = FlapHz(species, span, Hash01(seed, 1));
            BirdSpeciesInfo.Cycle(species, out float bmin, out float bmax, out float gmin, out float gmax);
            float beats = math.floor(math.lerp(bmin, bmax, Hash01(seed, 2)) + 0.5f);
            float glide = math.lerp(gmin, gmax, Hash01(seed, 3));
            float burst = beats / f;
            float cycle = burst + glide;
            float u = math.frac(t / cycle + Hash01(seed, 4)) * cycle;
            if (u >= burst) return BirdSpeciesInfo.GlidePose;
            return math.min(7, (int)(8f * math.frac(u * f)));
        }

        // Roll angle (radians, positive = right wing down) for a turn: bank = atan(v * yawRate / g), limited.
        public static float Bank(float speed, float yawRate)
        {
            return math.clamp(math.atan(speed * yawRate / 9.81f), -0.9f, 0.9f);
        }
    }

    // The bird mesh, built once in code: body, head and beak, tail, and two three-segment wings. Unit wingspan, nose along +Z, up +Y.
    // Every face is double sided. One mesh per (species, pose): the same vertex count for every pose of a species, only the wing
    // joints move, so a bird is animated by choosing which pose mesh to draw (instanced, grouped by pose).
    public static class BirdMesh
    {
        const int Ring = 6;
        static readonly Mesh[,] cache = new Mesh[BirdSpeciesInfo.Count, BirdSpeciesInfo.Poses];

        // Wing angle above horizontal, radians, per pose.
        public static float WingAngle(int pose)
        {
            if (pose >= BirdSpeciesInfo.GlidePose) return 6f * Mathf.Deg2Rad;
            return (5f + 40f * Mathf.Cos(2f * Mathf.PI * pose / 8f)) * Mathf.Deg2Rad;
        }

        public static int VertexCount(int species) => 50 + (species == BirdSpeciesInfo.Kite ? 16 : 8) + 48;

        public static Mesh Get(int species, int pose)
        {
            species = Mathf.Clamp(species, 0, BirdSpeciesInfo.Count - 1);
            pose = Mathf.Clamp(pose, 0, BirdSpeciesInfo.Poses - 1);
            var m = cache[species, pose];
            if (m == null) cache[species, pose] = m = Build(species, pose);
            return m;
        }

        public static void Release()
        {
            for (int s = 0; s < BirdSpeciesInfo.Count; s++)
                for (int p = 0; p < BirdSpeciesInfo.Poses; p++)
                {
                    if (cache[s, p] != null) { if (Application.isPlaying) Object.Destroy(cache[s, p]); else Object.DestroyImmediate(cache[s, p]); }
                    cache[s, p] = null;
                }
        }

        struct Shape { public float body, tail, rad, head, tailW; public float[] lead, trail, len; public bool forked; }

        static Shape ShapeOf(int s)
        {
            if (s == BirdSpeciesInfo.Kite)
                return new Shape { body = 0.28f, tail = 0.20f, rad = 0.045f, head = 0.03f, tailW = 0.16f, forked = true,
                    lead = new[] { 0.06f, 0.045f, 0.01f, -0.04f }, trail = new[] { -0.14f, -0.11f, -0.09f, -0.08f }, len = new[] { 0.20f, 0.16f, 0.10f } };
            if (s == BirdSpeciesInfo.Pigeon)
                return new Shape { body = 0.34f, tail = 0.22f, rad = 0.07f, head = 0.045f, tailW = 0.17f, forked = false,
                    lead = new[] { 0.10f, 0.07f, 0.03f, -0.03f }, trail = new[] { -0.18f, -0.17f, -0.15f, -0.13f }, len = new[] { 0.17f, 0.17f, 0.12f } };
            return new Shape { body = 0.36f, tail = 0.20f, rad = 0.075f, head = 0.055f, tailW = 0.14f, forked = false,
                lead = new[] { 0.10f, 0.07f, 0.04f, -0.01f }, trail = new[] { -0.15f, -0.14f, -0.12f, -0.10f }, len = new[] { 0.16f, 0.16f, 0.14f } };
        }

        static Mesh Build(int species, int pose)
        {
            var sh = ShapeOf(species);
            var v = new List<Vector3>(128);
            var t = new List<int>(512);

            // Body, head and beak: one loft of rings along Z (6 sided), closed with a centre vertex at each end.
            float B = sh.body, R = sh.rad, H = sh.head;
            float[] z = { -0.5f * B, -0.25f * B, 0f, 0.25f * B, 0.5f * B, 0.5f * B + 0.5f * H, 0.5f * B + 1.3f * H, 0.5f * B + 2.1f * H };
            float[] r = { 0.30f * R, 0.80f * R, R, 0.90f * R, 0.60f * R, H, 0.75f * H, 0.25f * H };
            int ring0 = v.Count;
            for (int k = 0; k < z.Length; k++)
                for (int a = 0; a < Ring; a++)
                {
                    float ang = 2f * Mathf.PI * a / Ring;
                    v.Add(new Vector3(Mathf.Cos(ang) * r[k], Mathf.Sin(ang) * r[k] * 0.85f, z[k]));
                }
            int tailCap = v.Count; v.Add(new Vector3(0f, 0f, z[0] - 0.01f));
            int noseCap = v.Count; v.Add(new Vector3(0f, 0f, z[z.Length - 1] + 0.5f * H));
            for (int k = 0; k < z.Length - 1; k++)
                for (int a = 0; a < Ring; a++)
                {
                    int a0 = ring0 + k * Ring + a, a1 = ring0 + k * Ring + (a + 1) % Ring, b0 = a0 + Ring, b1 = a1 + Ring;
                    t.Add(a0); t.Add(b0); t.Add(a1); t.Add(a1); t.Add(b0); t.Add(b1);
                }
            for (int a = 0; a < Ring; a++)
            {
                int a0 = ring0 + a, a1 = ring0 + (a + 1) % Ring;
                t.Add(tailCap); t.Add(a1); t.Add(a0);
                int l0 = ring0 + (z.Length - 1) * Ring + a, l1 = ring0 + (z.Length - 1) * Ring + (a + 1) % Ring;
                t.Add(noseCap); t.Add(l0); t.Add(l1);
            }

            // Tail: a flat fan behind the body (forked for the kite), double sided.
            float zt = z[0], ze = z[0] - sh.tail, w0 = 0.3f * R, w1 = sh.tailW * 0.5f;
            if (sh.forked)
            {
                Quad(v, t, new Vector3(-w0, 0, zt), new Vector3(0, 0, zt), new Vector3(0, 0, ze + 0.5f * sh.tail), new Vector3(-w1, 0, ze));
                Quad(v, t, new Vector3(0, 0, zt), new Vector3(w0, 0, zt), new Vector3(w1, 0, ze), new Vector3(0, 0, ze + 0.5f * sh.tail));
            }
            else Quad(v, t, new Vector3(-w0, 0, zt), new Vector3(w0, 0, zt), new Vector3(w1, 0, ze), new Vector3(-w1, 0, ze));

            // Wings: three segments per side. Each joint is carried up or down by the pose angle, increasingly towards the tip;
            // on the upstroke the wing is flexed (shorter), as a real wing is.
            float th = WingAngle(pose);
            float up = Mathf.Max(0f, th) / (45f * Mathf.Deg2Rad);
            bool upstroke = pose >= 5 && pose < BirdSpeciesInfo.GlidePose;   // poses 0-4 are the downstroke (wings out), 5-7 the upstroke (wings flexed in)
            float flex = pose >= BirdSpeciesInfo.GlidePose ? 1f : 1f - 0.3f * up - (upstroke ? 0.25f : 0f);
            for (int side = -1; side <= 1; side += 2)
            {
                var j = new Vector3[4];
                j[0] = new Vector3(side * 0.6f * R, 0.3f * R, 0f);
                float cum = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float ang = th * (1f + 0.35f * k);
                    float len = sh.len[k] * flex;
                    j[k + 1] = j[k] + new Vector3(side * Mathf.Cos(ang) * len, Mathf.Sin(ang) * len, 0f);
                    cum += len;
                }
                for (int k = 0; k < 3; k++)
                {
                    float sweep = (pose >= BirdSpeciesInfo.GlidePose ? 0f : 0.04f * up) * k;
                    Quad(v, t,
                        new Vector3(j[k].x, j[k].y, sh.lead[k] - sweep * (k > 0 ? 1f : 0f)), new Vector3(j[k].x, j[k].y, sh.trail[k]),
                        new Vector3(j[k + 1].x, j[k + 1].y, sh.trail[k + 1] - sweep), new Vector3(j[k + 1].x, j[k + 1].y, sh.lead[k + 1] - sweep));
                }
            }

            var mesh = new Mesh { name = $"Bird_{BirdSpeciesInfo.Name(species)}_{pose}", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // A quad with its own vertices on both faces (front and back), so it is lit from either side.
        static void Quad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3);
            int j = v.Count;
            v.Add(a); v.Add(b); v.Add(c); v.Add(d);
            t.Add(j); t.Add(j + 2); t.Add(j + 1); t.Add(j); t.Add(j + 3); t.Add(j + 2);
        }
    }

    // Materials of the bird: copies of the Resources Lit material (instancing on), one per (species, tint), so a build never draws
    // pink. SharedMaterial is cached for GameObject birds; instanced renderers make their own through CrowdRenderer.MakeMaterial.
    public static class BirdKit
    {
        static readonly Material[,] shared = new Material[BirdSpeciesInfo.Count, BirdSpeciesInfo.Tints];

        public static Material MakeMaterial(int species, int tint)
        {
            var m = CrowdRenderer.MakeMaterial(null);
            CrowdRenderer.SetColour(m, BirdSpeciesInfo.Tint(species, tint));
            return m;
        }

        public static Material SharedMaterial(int species, int tint)
        {
            var m = shared[species, tint];
            if (m == null) { shared[species, tint] = m = MakeMaterial(species, tint); m.hideFlags = HideFlags.HideAndDontSave; }
            return m;
        }

        // Species of a contact's look: kites 25 %, pigeons 45 %, small birds 30 %.
        public static int SpeciesOf(int seed)
        {
            int k = (seed & 0x7FFFFFFF) % 20;
            return k < 5 ? BirdSpeciesInfo.Kite : k < 14 ? BirdSpeciesInfo.Pigeon : BirdSpeciesInfo.Sparrow;
        }

        // A bird as a GameObject (a Contact wears this): a mesh swapped between the flap poses every frame by BirdBody.
        public static GameObject BuildObject(int seed)
        {
            int species = SpeciesOf(seed);
            var go = new GameObject("Bird");
            go.AddComponent<MeshFilter>().sharedMesh = BirdMesh.Get(species, BirdSpeciesInfo.GlidePose);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SharedMaterial(species, ((seed >> 8) & 0x7FFFFFFF) % BirdSpeciesInfo.Tints);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var body = go.AddComponent<BirdBody>();
            body.Init(species, seed);
            return go;
        }
    }

    // The minimum on-screen size of a bird. A kite at 300 m is 0.3 degrees wide and a sparrow at 100 m less. Drawn as the true model
    // its flat wings are seen almost edge on from the ground, so their triangles are under a pixel thick and nothing is rasterised:
    // the birds looked invisible. Under ModelPx pixels a bird is therefore drawn as the flat far silhouette turned to face the
    // camera, and never narrower than MinSpanPx pixels (it is made bigger, never smaller). Detection and scoring use the true size.
    public static class BirdView
    {
        public const float MinSpanPx = 7f, ModelPx = 8f;
        static Camera cam;
        static Mesh dot;
        static Material dotMat;

        public static Camera PlayerCamera()
        {
            if (cam == null || !cam.isActiveAndEnabled)
            {
                var sensor = Object.FindAnyObjectByType<Chaukanna.Player.ContactSensor>();
                cam = sensor != null ? sensor.GetComponent<Camera>() : Camera.main;
            }
            return cam;
        }

        public static float PxPerMetreAtOne(Camera c) => c.pixelHeight / (2f * Mathf.Tan(c.fieldOfView * Mathf.Deg2Rad * 0.5f));

        // Scale factor (1 = true size) that gives a bird of `trueSpan` metres at `pos` at least MinSpanPx pixels across on `c`.
        public static float Boost(Camera c, Vector3 pos, float trueSpan)
        {
            if (c == null || trueSpan <= 1e-4f) return 1f;
            float need = MinSpanPx * Vector3.Distance(c.transform.position, pos) / Mathf.Max(PxPerMetreAtOne(c), 1e-3f);
            return Mathf.Clamp(need / trueSpan, 1f, 60f);
        }

        public static Mesh DotMesh { get { if (dot == null) dot = CrowdRenderer.SilhouetteMesh(Silhouette.Bird); return dot; } }

        public static Material DotMaterial
        {
            get
            {
                if (dotMat == null)
                {
                    dotMat = CrowdRenderer.MakeMaterial(null);
                    CrowdRenderer.SetColour(dotMat, new Color(0.10f, 0.10f, 0.11f));
                    dotMat.hideFlags = HideFlags.HideAndDontSave;
                }
                return dotMat;
            }
        }

        // The flat silhouette's face (+Y) to the camera, its wings level across the screen (so it reads as a V, not a smear).
        public static Quaternion Facing(Vector3 toCam)
        {
            toCam.Normalize();
            Vector3 x = Vector3.Cross(Vector3.up, toCam);
            if (x.sqrMagnitude < 1e-4f) x = Vector3.right;
            x.Normalize();
            return Quaternion.LookRotation(Vector3.Cross(x, toCam), toCam);
        }
    }

    // Animates a GameObject bird: flap-glide pose by phase, pitch along the climb, bank into turns. Runs in LateUpdate so it also
    // works for contacts moved by something else (ContactMotion, a group, a swarm follower, a LAN proxy).
    public class BirdBody : MonoBehaviour
    {
        public int Species { get; private set; }
        public int Pose { get; private set; }
        public float Bank { get; private set; }
        MeshFilter mf;
        uint seed;
        float span;
        Vector3 lastPos, vel, lastVel, baseScale;
        bool started, baseKnown;
        MeshRenderer mr;
        Material trueMat;

        public void Init(int species, int seedValue)
        {
            Species = species;
            seed = (uint)seedValue * 2654435761u;
            span = BirdSpeciesInfo.Span(species) * (0.9f + 0.2f * BirdFlight.Hash01(seed, 9));
            mf = GetComponent<MeshFilter>();
        }

        void LateUpdate()
        {
            if (mf == null) mf = GetComponent<MeshFilter>();
            float dt = Time.deltaTime;
            Vector3 p = transform.position;
            if (!started) { started = true; lastPos = p; return; }
            if (dt > 1e-5f)
            {
                Vector3 v = (p - lastPos) / dt;
                lastPos = p;
                if (v.sqrMagnitude < 80f * 80f)
                {
                    lastVel = vel;
                    vel = Vector3.Lerp(vel, v, 1f - Mathf.Exp(-dt * 6f));
                    float speed = vel.magnitude;
                    if (speed > 0.5f && lastVel.sqrMagnitude > 0.25f)
                    {
                        float yawRate = (lastVel.z * vel.x - lastVel.x * vel.z) / Mathf.Max(0.01f, lastVel.magnitude * speed) / dt;   // + = turning right
                        Bank = Mathf.Lerp(Bank, BirdFlight.Bank(speed, yawRate), 1f - Mathf.Exp(-dt * 8f));
                    }
                }
            }
            if (vel.sqrMagnitude > 0.25f)
                transform.rotation = Quaternion.LookRotation(vel.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, -Bank * Mathf.Rad2Deg);
            Pose = BirdFlight.Pose(Species, span, Time.time, seed);
            if (mf != null) mf.sharedMesh = BirdMesh.Get(Species, Pose);

            // Minimum on-screen size (see BirdView): far away the bird is the flat silhouette facing the camera, made no narrower
            // than MinSpanPx pixels; near it is the true model.
            if (!baseKnown) { baseScale = transform.localScale; baseKnown = true; mr = GetComponent<MeshRenderer>(); trueMat = mr != null ? mr.sharedMaterial : null; }
            var cam = BirdView.PlayerCamera();
            if (cam == null) return;
            float dist = Mathf.Max(Vector3.Distance(cam.transform.position, p), 0.01f), ppm = BirdView.PxPerMetreAtOne(cam);
            bool far = baseScale.x * ppm / dist < BirdView.ModelPx;
            if (far)
            {
                if (mf != null) mf.sharedMesh = BirdView.DotMesh;
                if (mr != null) mr.sharedMaterial = BirdView.DotMaterial;
                transform.localScale = Vector3.one * Mathf.Clamp(BirdView.MinSpanPx * dist / Mathf.Max(ppm, 1e-3f), baseScale.x, baseScale.x * 60f);
                transform.rotation = BirdView.Facing(cam.transform.position - p);
            }
            else
            {
                if (mr != null && trueMat != null && mr.sharedMaterial != trueMat) mr.sharedMaterial = trueMat;
                transform.localScale = baseScale;
            }
        }
    }

    // The instanced bird draw: visibility, pose bucket and matrix of every flying agent, in one Burst job. Bucket 0 = the far
    // silhouette; bucket 1 + tint * Poses + pose = the near bird in that flap pose. Per-bird size, tint and phase come from a
    // hash of the agent index, so they are the same in every run of a seed.
    [BurstCompile]
    public struct BirdDrawJob : IJob
    {
        [ReadOnly] public NativeArray<float3> prevPos, curPos, prevVel, curVel;
        [ReadOnly] public NativeArray<byte> state;
        public int agents, bucketCount, species;
        public uint seed;
        public float alpha, dt, time, span, maxRange, minPx, modelPx, pxPerMetreAtOne, minSpanPx;
        public float3 camPos;
        public float4x4 viewProj;
        public NativeArray<int> counts, starts, fill, stats, bucketOf;
        public NativeArray<byte> vis;
        public NativeArray<float3> drawPos;
        [WriteOnly] public NativeArray<float4x4> matrices;

        public void Execute()
        {
            for (int b = 0; b < bucketCount; b++) { counts[b] = 0; fill[b] = 0; }
            int nModel = 0, nDot = 0;
            for (int i = 0; i < agents; i++)
            {
                if (state[i] != 0) { vis[i] = 0; continue; }
                float3 p = math.lerp(prevPos[i], curPos[i], alpha);
                drawPos[i] = p;
                uint h = BirdFlight.Mix(i, seed);
                float size = span * (0.9f + 0.2f * BirdFlight.Hash01(h, 9));
                float dist = math.distance(p, camPos);
                float px = size * pxPerMetreAtOne / math.max(dist, 0.01f);
                byte l = 0;
                int bucket = 0;
                // Minimum on-screen size: a real wingspan this far away is under a pixel and the triangles vanish, so a bird is never
                // drawn smaller than minSpanPx wide (px >= minPx is only the old cull, kept when minSpanPx is 0).
                if (dist <= maxRange && (px >= minPx || minSpanPx > 0f))
                {
                    float4 clip = math.mul(viewProj, new float4(p, 1f));
                    float m = clip.w * 1.05f;
                    if (clip.w > 0f && math.abs(clip.x) <= m && math.abs(clip.y) <= m)
                    {
                        if (px > modelPx)
                        {
                            l = 2;
                            int tint = math.min((int)(BirdFlight.Hash01(h, 5) * BirdSpeciesInfo.Tints), BirdSpeciesInfo.Tints - 1);
                            bucket = 1 + tint * BirdSpeciesInfo.Poses + BirdFlight.Pose(species, size, time, h);
                        }
                        else l = 1;
                    }
                }
                vis[i] = l;
                bucketOf[i] = bucket;
                if (l != 0) { counts[bucket]++; if (l == 2) nModel++; else nDot++; }
            }
            int run = 0;
            for (int b = 0; b < bucketCount; b++) { starts[b] = run; run += counts[b]; }
            for (int i = 0; i < agents; i++)
            {
                if (vis[i] == 0) continue;
                uint h = BirdFlight.Mix(i, seed);
                float size = span * (0.9f + 0.2f * BirdFlight.Hash01(h, 9));
                float3 v = math.lerp(prevVel[i], curVel[i], alpha);
                float speed = math.length(v);
                float dist = math.max(math.distance(drawPos[i], camPos), 0.01f);
                size = math.max(size, minSpanPx * dist / math.max(pxPerMetreAtOne, 1e-3f));
                quaternion rot = quaternion.RotateY(math.atan2(v.x, v.z));
                if (vis[i] == 1)
                {
                    // The far silhouette is a flat shape: turn its face to the camera, wings level across the screen.
                    float3 tc = (camPos - drawPos[i]) / dist;
                    float3 xw = math.cross(new float3(0f, 1f, 0f), tc);
                    xw = math.lengthsq(xw) < 1e-4f ? new float3(1f, 0f, 0f) : math.normalize(xw);
                    rot = new quaternion(new float3x3(xw, tc, math.cross(xw, tc)));
                }
                if (vis[i] == 2 && speed > 0.5f)
                {
                    float3 a = prevVel[i], b2 = curVel[i];
                    float la = math.length(a), lb = math.length(b2);
                    float yawRate = la > 0.5f && lb > 0.5f ? (a.z * b2.x - a.x * b2.z) / (la * lb) / dt : 0f;
                    float bank = BirdFlight.Bank(speed, yawRate);
                    rot = math.mul(quaternion.LookRotationSafe(v / speed, math.up()), quaternion.RotateZ(-bank));
                }
                matrices[starts[bucketOf[i]] + fill[bucketOf[i]]++] = float4x4.TRS(drawPos[i], rot, new float3(size));
            }
            stats[0] = nModel; stats[1] = nDot;
        }
    }
}
