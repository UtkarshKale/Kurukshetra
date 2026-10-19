using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chaukanna.Scenario
{
    // Everything the crowd needs to draw: the tiny silhouettes, the class models taken apart into parts (mesh, submesh,
    // material, local matrix), the bucket tables the bucket job fills, and the instanced draw. Meshes are used as they are
    // (never combined: model meshes may not be readable in a build). Materials are copies with instancing switched on; a
    // material with no usable shader becomes a copy of RuntimeMaterials.Lit with its colour, so a build never draws pink.
    public sealed class CrowdRenderer : IDisposable
    {
        public const int DotBuckets = 3;                   // quad, fixed-wing, bird
        public int BucketCount { get; private set; }
        public readonly int[] ModelCount = new int[5];     // looks per class that have parts

        Mesh[] meshes;
        int[] subs;
        Material[] mats;
        Mesh[] dotMeshes = new Mesh[DotBuckets];
        Material dotMaterial;

        public NativeArray<int> modelFirst, modelParts, dotBucket, counts, starts, fill, stats;
        public NativeArray<float4x4> partLocal, matrices;
        public int MaxParts { get; private set; } = 1;
        public int Capacity { get; private set; }
        bool disposed;

        // defs / models are indexed by ContactClass value (0..4); only FriendlyISR, CivilianQuad and Bird are used.
        public CrowdRenderer(ContactDefinition[] defs, List<GameObject>[] models, int agentCapacity)
        {
            var meshList = new List<Mesh>();
            var subList = new List<int>();
            var matList = new List<Material>();
            var local = new List<float4x4>();

            // Silhouette buckets first.
            dotMeshes[0] = SilhouetteMesh(Silhouette.Quad);
            dotMeshes[1] = SilhouetteMesh(Silhouette.FixedWing);
            dotMeshes[2] = SilhouetteMesh(Silhouette.Bird);
            var lit = RuntimeMaterials.Lit;
            dotMaterial = lit != null ? new Material(lit) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            SetColour(dotMaterial, new Color(0.13f, 0.13f, 0.14f));
            dotMaterial.enableInstancing = true;
            for (int i = 0; i < DotBuckets; i++) { meshList.Add(dotMeshes[i]); subList.Add(0); matList.Add(dotMaterial); local.Add(float4x4.identity); }

            dotBucket = new NativeArray<int>(5, Allocator.Persistent);
            modelFirst = new NativeArray<int>(5 * CrowdGenerator.MaxVariants, Allocator.Persistent);
            modelParts = new NativeArray<int>(5 * CrowdGenerator.MaxVariants, Allocator.Persistent);
            for (int i = 0; i < modelFirst.Length; i++) { modelFirst[i] = -1; modelParts[i] = 0; }
            for (int c = 0; c < 5; c++)
            {
                var def = c < defs.Length ? defs[c] : null;
                var sil = def != null ? def.silhouette : (c == (int)ContactClass.Bird ? Silhouette.Bird : c == (int)ContactClass.FriendlyISR ? Silhouette.FixedWing : Silhouette.Quad);
                dotBucket[c] = sil == Silhouette.Quad ? 0 : sil == Silhouette.FixedWing ? 1 : 2;
            }

            // Model parts of the three crowd classes, one set per look.
            foreach (int c in new[] { (int)ContactClass.FriendlyISR, (int)ContactClass.CivilianQuad, (int)ContactClass.Bird })
            {
                var def = c < defs.Length ? defs[c] : null;
                if (def == null) continue;
                var list = models != null && c < models.Length ? models[c] : null;
                int looks = Math.Min(ContactVisuals.ChoiceCount(def, list), CrowdGenerator.MaxVariants);
                for (int v = 0; v < looks; v++)
                {
                    int first = meshList.Count;
                    ExtractParts(def, v, list, meshList, subList, matList, local);
                    int parts = meshList.Count - first;
                    if (parts > 0)
                    {
                        modelFirst[c * CrowdGenerator.MaxVariants + v] = first;
                        modelParts[c * CrowdGenerator.MaxVariants + v] = parts;
                        ModelCount[c]++;
                        MaxParts = Math.Max(MaxParts, parts);
                    }
                }
            }

            BucketCount = meshList.Count;
            meshes = meshList.ToArray(); subs = subList.ToArray(); mats = matList.ToArray();
            partLocal = new NativeArray<float4x4>(BucketCount, Allocator.Persistent);
            for (int i = 0; i < BucketCount; i++) partLocal[i] = local[i];
            counts = new NativeArray<int>(BucketCount, Allocator.Persistent);
            starts = new NativeArray<int>(BucketCount, Allocator.Persistent);
            fill = new NativeArray<int>(BucketCount, Allocator.Persistent);
            stats = new NativeArray<int>(4, Allocator.Persistent);
            Capacity = Math.Max(1, agentCapacity) * MaxParts;
            matrices = new NativeArray<float4x4>(Capacity, Allocator.Persistent);
        }

        // Builds the look exactly as a real contact would wear it, reads its renderers, then throws the object away.
        // Part matrices are relative to the root with the definition scale left out (the agent matrix carries the size).
        void ExtractParts(ContactDefinition def, int variant, List<GameObject> models, List<Mesh> meshList, List<int> subList, List<Material> matList, List<float4x4> local)
        {
            var root = ContactVisuals.Build(def, out var materials, out _, out _, variant, models);
            root.SetActive(false);                          // never drawn, not even for a frame
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
                    if (!copies.TryGetValue(src ?? dotMaterial, out var mat)) copies[src ?? dotMaterial] = mat = MakeMaterial(src);
                    meshList.Add(mf.sharedMesh); subList.Add(s); matList.Add(mat); local.Add(m);
                }
            }
            // The contact copies made by Build are no longer needed (the crowd keeps its own).
            foreach (var mat in materials) if (mat != null) Kill(mat);
            Kill(root);
        }

        // Always a copy of the built-in Resources Lit material (instancing on in the asset), carrying the model material's
        // colour and texture. A copy of the model's own material would need that shader's instancing variant in the build,
        // which Unity strips unless some built material has instancing on: the crowd would then draw nothing in a build.
        static Material MakeMaterial(Material src)
        {
            var lit = RuntimeMaterials.Lit;
            var m = lit != null ? new Material(lit) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color c = new Color(0.4f, 0.4f, 0.42f);
            Texture tex = null;
            if (src != null)
            {
                try
                {
                    if (src.HasProperty("_BaseColor")) c = src.GetColor("_BaseColor"); else if (src.HasProperty("_Color")) c = src.GetColor("_Color");
                    if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap"); else if (src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
                }
                catch { }
            }
            SetColour(m, c);
            if (tex != null && m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.enableInstancing = true;
            return m;
        }

        static void SetColour(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        public Material MaterialOfBucket(int b) => mats[b];

        // Destroy in Play mode, DestroyImmediate otherwise (the self-tests run in the Editor).
        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        // Draws every bucket for one camera. Counts / starts were filled by the bucket job (already completed).
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
            if (modelFirst.IsCreated) modelFirst.Dispose();
            if (modelParts.IsCreated) modelParts.Dispose();
            if (dotBucket.IsCreated) dotBucket.Dispose();
            if (counts.IsCreated) counts.Dispose();
            if (starts.IsCreated) starts.Dispose();
            if (fill.IsCreated) fill.Dispose();
            if (stats.IsCreated) stats.Dispose();
            if (partLocal.IsCreated) partLocal.Dispose();
            if (matrices.IsCreated) matrices.Dispose();
            if (mats != null) { var seen = new HashSet<Material>(); foreach (var m in mats) if (m != null && seen.Add(m)) Kill(m); }
            foreach (var m in dotMeshes) if (m != null) Kill(m);
        }

        // --- Tiny silhouettes: about 12 triangles each, double sided, unit span. ---

        static Mesh SilhouetteMesh(Silhouette s)
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            switch (s)
            {
                case Silhouette.Quad:
                    Rect(v, n, t, new Vector3(-0.5f, 0, -0.5f), new Vector3(0.5f, 0, 0.5f), 0.07f);       // X cross, two diagonals
                    Rect(v, n, t, new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f), 0.07f);
                    Rect(v, n, t, new Vector3(-0.1f, 0.02f, 0), new Vector3(0.1f, 0.02f, 0), 0.2f);      // body
                    break;
                case Silhouette.FixedWing:
                    Rect(v, n, t, new Vector3(0, 0, -0.5f), new Vector3(0, 0, 0.5f), 0.1f);              // fuselage
                    Rect(v, n, t, new Vector3(-0.5f, 0, 0.1f), new Vector3(0.5f, 0, 0.1f), 0.2f);        // wing
                    Rect(v, n, t, new Vector3(-0.18f, 0, -0.45f), new Vector3(0.18f, 0, -0.45f), 0.1f);  // tailplane
                    break;
                default:
                    Rect(v, n, t, new Vector3(0, 0, 0), new Vector3(-0.5f, 0.12f, 0), 0.25f);            // shallow V
                    Rect(v, n, t, new Vector3(0, 0, 0), new Vector3(0.5f, 0.12f, 0), 0.25f);
                    Rect(v, n, t, new Vector3(0, 0, -0.15f), new Vector3(0, 0, 0.2f), 0.1f);             // body
                    break;
            }
            var mesh = new Mesh { name = "CrowdDot_" + s, hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(v); mesh.SetNormals(n); mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // A strip from a to b of the given width, as two quads (front and back) = 4 triangles.
        static void Rect(List<Vector3> v, List<Vector3> n, List<int> t, Vector3 a, Vector3 b, float width)
        {
            Vector3 d = (b - a).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, d);
            if (side.sqrMagnitude < 1e-6f) side = Vector3.right;
            side = side.normalized * width * 0.5f;
            Vector3 p0 = a - side, p1 = a + side, p2 = b + side, p3 = b - side;
            Vector3 nor = Vector3.Cross(p1 - p0, p3 - p0).normalized;
            if (nor.y < 0f) nor = -nor;
            int i = v.Count;
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3); for (int k = 0; k < 4; k++) n.Add(nor);
            t.Add(i); t.Add(i + 2); t.Add(i + 1); t.Add(i); t.Add(i + 3); t.Add(i + 2);
            // back face: own vertices, opposite normal, reversed winding
            v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3); for (int k = 0; k < 4; k++) n.Add(-nor);
            t.Add(i + 4); t.Add(i + 5); t.Add(i + 6); t.Add(i + 4); t.Add(i + 6); t.Add(i + 7);
        }
    }
}
