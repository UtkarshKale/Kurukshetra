using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chaukanna.Scenario
{
    // Visibility and instance matrices of every flying swarm agent, in one Burst job. Position is interpolated between the last two
    // 50 Hz steps. Bucket 0 = the far silhouette; buckets 1..P = the parts of the near model (matrix = agent TRS * part local).
    // A counting sort packs the matrices bucket by bucket, so nothing is allocated per frame.
    [BurstCompile]
    public struct SwarmDrawJob : IJob
    {
        [ReadOnly] public NativeArray<float3> prevPos, curPos, vel;
        [ReadOnly] public NativeArray<byte> state;
        [ReadOnly] public NativeArray<float4x4> partLocal;     // per bucket (identity for bucket 0)
        public int agents, bucketCount;
        public float alpha, size, maxRange, minPx, modelPx, pxPerMetreAtOne;
        public float3 camPos;
        public float4x4 viewProj;
        public NativeArray<int> counts, starts, fill, stats;   // stats: 0 drawn as model, 1 drawn as silhouette
        public NativeArray<byte> vis;              // 0 not drawn, 1 silhouette, 2 model
        public NativeArray<float3> drawPos;
        [WriteOnly] public NativeArray<float4x4> matrices;

        public void Execute()
        {
            for (int b = 0; b < bucketCount; b++) { counts[b] = 0; fill[b] = 0; }
            int nModel = 0, nDot = 0;
            bool hasModel = bucketCount > 1;
            for (int i = 0; i < agents; i++)
            {
                if (state[i] != 0) { vis[i] = 0; continue; }
                float3 p = math.lerp(prevPos[i], curPos[i], alpha);
                drawPos[i] = p;
                float dist = math.distance(p, camPos);
                float px = size * pxPerMetreAtOne / math.max(dist, 0.01f);
                byte l = 0;
                if (dist <= maxRange && px >= minPx)
                {
                    float4 clip = math.mul(viewProj, new float4(p, 1f));
                    float m = clip.w * 1.05f;
                    if (clip.w > 0f && math.abs(clip.x) <= m && math.abs(clip.y) <= m) l = (px > modelPx && hasModel) ? (byte)2 : (byte)1;
                }
                vis[i] = l;
                if (l == 2) { nModel++; for (int b = 1; b < bucketCount; b++) counts[b]++; }
                else if (l == 1) { nDot++; counts[0]++; }
            }
            int run = 0;
            for (int b = 0; b < bucketCount; b++) { starts[b] = run; run += counts[b]; }
            for (int i = 0; i < agents; i++)
            {
                byte l = vis[i];
                if (l == 0) continue;
                float3 v = vel[i];
                float yaw = math.atan2(v.x, v.z);
                float4x4 world = float4x4.TRS(drawPos[i], quaternion.RotateY(yaw), new float3(size));
                if (l == 2)
                    for (int b = 1; b < bucketCount; b++) matrices[starts[b] + fill[b]++] = math.mul(world, partLocal[b]);
                else matrices[starts[0] + fill[0]++] = world;
            }
            stats[0] = nModel; stats[1] = nDot;
        }
    }

    // Draws a big swarm: the far silhouette and the near model (taken apart into parts), instanced. Same approach as the crowd:
    // materials are copies of the built-in Resources Lit material with instancing on, so a build never draws pink.
    public sealed class SwarmRenderer : IDisposable
    {
        public int BucketCount { get; private set; }
        public NativeArray<float4x4> partLocal, matrices;
        public NativeArray<int> counts, starts, fill, stats;
        public NativeArray<byte> vis;
        public NativeArray<float3> drawPos;
        public readonly float Size;
        Mesh[] meshes;
        int[] subs;
        Material[] mats;
        Mesh dotMesh;
        bool disposed;

        public SwarmRenderer(ContactDefinition def, List<GameObject> models, int agents)
        {
            Size = def != null ? def.size : 1f;
            var meshList = new List<Mesh>();
            var subList = new List<int>();
            var matList = new List<Material>();
            var local = new List<float4x4>();
            dotMesh = CrowdRenderer.SilhouetteMesh(def != null ? def.silhouette : Silhouette.Quad);
            var dotMat = CrowdRenderer.MakeMaterial(null);
            CrowdRenderer.SetColour(dotMat, new Color(0.13f, 0.13f, 0.14f));
            meshList.Add(dotMesh); subList.Add(0); matList.Add(dotMat); local.Add(float4x4.identity);
            if (def != null) ExtractParts(def, models, meshList, subList, matList, local);
            BucketCount = meshList.Count;
            meshes = meshList.ToArray(); subs = subList.ToArray(); mats = matList.ToArray();
            partLocal = new NativeArray<float4x4>(BucketCount, Allocator.Persistent);
            for (int i = 0; i < BucketCount; i++) partLocal[i] = local[i];
            counts = new NativeArray<int>(BucketCount, Allocator.Persistent);
            starts = new NativeArray<int>(BucketCount, Allocator.Persistent);
            fill = new NativeArray<int>(BucketCount, Allocator.Persistent);
            stats = new NativeArray<int>(4, Allocator.Persistent);
            int cap = Math.Max(1, agents);
            matrices = new NativeArray<float4x4>(cap * Math.Max(1, BucketCount - 1), Allocator.Persistent);
            vis = new NativeArray<byte>(cap, Allocator.Persistent);
            drawPos = new NativeArray<float3>(cap, Allocator.Persistent);
        }

        // Builds the look as a real contact would wear it, reads its renderers, then throws it away (as the crowd does).
        void ExtractParts(ContactDefinition def, List<GameObject> models, List<Mesh> meshList, List<int> subList, List<Material> matList, List<float4x4> local)
        {
            var root = ContactVisuals.Build(def, out var materials, out _, out _, 0, models);
            root.SetActive(false);
            root.transform.localScale = Vector3.one;
            var copies = new Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                Matrix4x4 m = root.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                var shared = r.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    var src = shared.Length == 0 ? null : shared[Math.Min(s, shared.Length - 1)];
                    var key = src != null ? src : matList[0];
                    if (!copies.TryGetValue(key, out var mat)) copies[key] = mat = CrowdRenderer.MakeMaterial(src);
                    meshList.Add(mf.sharedMesh); subList.Add(s); matList.Add(mat); local.Add(m);
                }
            }
            foreach (var mat in materials) if (mat != null) Kill(mat);
            Kill(root);
        }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        public int Draw(Camera cam, Bounds bounds)
        {
            if (disposed || cam == null) return 0;
            var all = matrices.Reinterpret<Matrix4x4>();
            int drawn = 0;
            for (int b = 0; b < BucketCount; b++)
            {
                int n = counts[b];
                if (n <= 0) continue;
                var rp = new RenderParams(mats[b])
                {
                    camera = cam,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off,
                    motionVectorMode = MotionVectorGenerationMode.ForceNoMotion,
                    worldBounds = bounds,
                };
                int start = starts[b];
                while (n > 0)
                {
                    int chunk = Math.Min(n, 1023);
                    Graphics.RenderMeshInstanced(rp, meshes[b], subs[b], all, chunk, start);
                    start += chunk; n -= chunk; drawn += chunk;
                }
            }
            return drawn;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (partLocal.IsCreated) partLocal.Dispose();
            if (matrices.IsCreated) matrices.Dispose();
            if (counts.IsCreated) counts.Dispose();
            if (starts.IsCreated) starts.Dispose();
            if (fill.IsCreated) fill.Dispose();
            if (stats.IsCreated) stats.Dispose();
            if (vis.IsCreated) vis.Dispose();
            if (drawPos.IsCreated) drawPos.Dispose();
            if (mats != null) { var seen = new HashSet<Material>(); foreach (var m in mats) if (m != null && seen.Add(m)) Kill(m); }
            if (dotMesh != null) Kill(dotMesh);
        }
    }
}
