using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // One thing in the palette of the 3D editor: a prefab of the terrain layer kit, named by (kind, group, variant).
    public class KitEntry
    {
        public int kind, group, variant;
        public string label;
        public GameObject prefab;
    }

    // The layer kit as a palette for hand-placed objects. Trees come in the kit's species groups (or one group when the kit has none),
    // bushes in normal / snow, buildings by type, rocks in one group. A (kind, group, variant) from a saved scenario is resolved here, so
    // a kit with fewer or other prefabs still gives something (variant is taken modulo the list length).
    public static class ManualKit
    {
        public static readonly string[] KindNames = { "Trees", "Bushes", "Buildings", "Rocks" };

        public static string[] GroupNames(TerrainLayerKit kit, int kind)
        {
            switch ((ManualKind)kind)
            {
                case ManualKind.Tree: return kit.HasSpeciesGroups ? new[] { "Broadleaf", "Pine", "Snow", "Dead" } : new[] { "Trees" };
                case ManualKind.Bush: return new[] { "Bushes", "Snow bushes" };
                case ManualKind.Building: return System.Enum.GetNames(typeof(BuildingType));
                default: return new[] { "Rocks" };
            }
        }

        // The usable prefabs of one group, in kit order.
        public static List<GameObject> Prefabs(TerrainLayerKit kit, int kind, int group)
        {
            List<GameObject> src = null;
            switch ((ManualKind)kind)
            {
                case ManualKind.Tree:
                    if (kit.HasSpeciesGroups) src = group == 0 ? kit.broadleafTrees : group == 1 ? kit.pineTrees : group == 2 ? kit.snowTrees : kit.deadTrees;
                    else src = group == 0 ? kit.TreePrefabsOrPlaceholders() : null;
                    break;
                case ManualKind.Bush: src = group == 0 ? kit.bushPrefabs : kit.bushSnowPrefabs; break;
                case ManualKind.Building: src = kit.BuildingList((BuildingType)Mathf.Clamp(group, 0, 6)); break;
                default: src = group == 0 ? kit.rockPrefabs : null; break;
            }
            var list = new List<GameObject>();
            if (src != null) foreach (var g in src) if (g != null) list.Add(g);
            return list;
        }

        // The whole palette of one kind.
        public static List<KitEntry> Entries(TerrainLayerKit kit, int kind)
        {
            var all = new List<KitEntry>();
            int groups = GroupNames(kit, kind).Length;
            for (int g = 0; g < groups; g++)
            {
                var list = Prefabs(kit, kind, g);
                for (int v = 0; v < list.Count; v++) all.Add(new KitEntry { kind = kind, group = g, variant = v, label = list[v].name, prefab = list[v] });
            }
            return all;
        }

        // Null when the kit has nothing for it. variant is wrapped into the list.
        public static GameObject Resolve(TerrainLayerKit kit, int kind, int group, int variant, out int wrapped)
        {
            wrapped = 0;
            var list = Prefabs(kit, kind, group);
            if (list.Count == 0) return null;
            wrapped = ((variant % list.Count) + list.Count) % list.Count;
            return list[wrapped];
        }
    }

    // The hand-placed trees, bushes, rocks and buildings of a scenario, drawn GPU-instanced: one batch call per mesh part and 1000
    // instances, no GameObject and no Update per object (a thousand or a hundred thousand cost the same script time). Used by the 3D editor
    // (where it is edited through Add / RemoveAt / Insert / Set) and by the run (where it only draws). Trees and buildings can get colliders
    // when a run starts, so they stop the line of sight like the generated ones do.
    public class ManualField : MonoBehaviour
    {
        public static ManualField Current { get; private set; }
        public const string RootName = "Manual objects (scenario)";

        public Camera cameraOverride;       // the editor camera; null = Camera.main
        public int Drawn { get; private set; }
        public int ModelCount => models.Count;
        public List<ManualObject> Objects { get; private set; } = new List<ManualObject>();

        class Part { public Mesh mesh; public Material[] mats; public Matrix4x4 local; }

        class Model
        {
            public int kind;
            public List<Part> parts = new List<Part>();
            public float height = 1f, baseScale = 1f;
            public Bounds bounds;           // in prefab space
            public bool shadows;
            public readonly List<int> objs = new List<int>();
            public Matrix4x4[][] matrices = new Matrix4x4[0][];     // per part, one per instance
            public int count;
        }

        const int Batch = 1023;
        readonly Dictionary<long, Model> models = new Dictionary<long, Model>();
        readonly Dictionary<Material, Material> instanced = new Dictionary<Material, Material>();
        readonly List<float> groundY = new List<float>();
        TerrainLayerKit kit;
        bool dirty = true;
        float nextRebuild;
        GameObject colliderRoot;

        // The field of this scene, made on first use, drawing the given list (the scenario's own list: edits go through the methods below).
        public static ManualField Ensure(List<ManualObject> objects)
        {
            if (Current == null)
            {
                var go = new GameObject(RootName);
                Current = go.AddComponent<ManualField>();
            }
            Current.Bind(objects);
            return Current;
        }

        public static void Remove()
        {
            if (Current != null) Destroy(Current.gameObject);
            Current = null;
        }

        void Bind(List<ManualObject> objects)
        {
            kit = TerrainLayerKit.Default;
            Objects = objects ?? new List<ManualObject>();
            RemoveColliders();
            ResyncAll();
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            foreach (var m in instanced.Values) if (m != null) Destroy(m);
        }

        // --- Editing (keeps the cached ground heights in step with the list) ---

        public void ResyncAll()
        {
            groundY.Clear();
            for (int i = 0; i < Objects.Count; i++) groundY.Add(float.NaN);
            dirty = true;
        }

        public void Add(ManualObject o) { Objects.Add(o); groundY.Add(float.NaN); dirty = true; }
        public void Insert(int index, ManualObject o) { index = Mathf.Clamp(index, 0, Objects.Count); Objects.Insert(index, o); groundY.Insert(index, float.NaN); dirty = true; }
        public void RemoveAt(int index) { if (index < 0 || index >= Objects.Count) return; Objects.RemoveAt(index); groundY.RemoveAt(index); dirty = true; }
        public void Set(int index, ManualObject o) { if (index < 0 || index >= Objects.Count) return; Objects[index] = o; groundY[index] = float.NaN; dirty = true; }
        public void MarkDirty() { dirty = true; }

        // The ground height under object i (cached).
        public float GroundOf(int i)
        {
            if (groundY.Count != Objects.Count) ResyncAll();
            if (float.IsNaN(groundY[i]))
            {
                var o = Objects[i];
                float g = TerrainBuilder.GroundHeight(new Vector3(o.x, 0f, o.z));
                groundY[i] = float.IsNaN(g) ? 0f : g;
            }
            return groundY[i];
        }

        // --- Queries (linear; used on a click or a brush step, never per frame) ---

        public int Nearest(Vector2 xz, float within)
        {
            int best = -1;
            float bd = within * within;
            for (int i = 0; i < Objects.Count; i++)
            {
                float dx = Objects[i].x - xz.x, dz = Objects[i].z - xz.y, d = dx * dx + dz * dz;
                if (d <= bd) { bd = d; best = i; }
            }
            return best;
        }

        public void InCircle(Vector2 centre, float radius, List<int> into, int kind = -1)
        {
            float r2 = radius * radius;
            for (int i = 0; i < Objects.Count; i++)
            {
                if (kind >= 0 && Objects[i].kind != kind) continue;
                float dx = Objects[i].x - centre.x, dz = Objects[i].z - centre.y;
                if (dx * dx + dz * dz <= r2) into.Add(i);
            }
        }

        // World height of an object's model (metres), for framing and the selection ring.
        public float HeightOf(ManualObject o)
        {
            var m = GetModel(o);
            return m != null ? m.height * o.scale * m.baseScale : 2f;
        }

        public float RadiusOf(ManualObject o)
        {
            var m = GetModel(o);
            return m != null ? Mathf.Max(1f, Mathf.Max(m.bounds.extents.x, m.bounds.extents.z) * o.scale * m.baseScale) : 1.5f;
        }

        // --- Models ---

        static long Key(int kind, int group, int variant) => ((long)kind << 40) | ((long)group << 32) | (uint)variant;

        Model GetModel(ManualObject o)
        {
            var prefab = ManualKit.Resolve(kit, o.kind, o.group, o.variant, out int wrapped);
            if (prefab == null) return null;
            long key = Key(o.kind, o.group, wrapped);
            if (models.TryGetValue(key, out var m)) return m;
            m = MakeModel(prefab, o.kind);
            models[key] = m;
            return m;
        }

        Model MakeModel(GameObject prefab, int kind)
        {
            var m = new Model { kind = kind, shadows = kind == (int)ManualKind.Tree || kind == (int)ManualKind.Building };
            HashSet<Renderer> allowed = null;
            var lod = prefab.GetComponent<LODGroup>();
            if (lod != null) { var lods = lod.GetLODs(); if (lods.Length > 0) allowed = new HashSet<Renderer>(lods[0].renderers); }
            bool any = false;
            var b = new Bounds();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null || mf.sharedMesh == null) continue;
                if (allowed != null && !allowed.Contains(mr)) continue;
                var mats = mr.sharedMaterials;
                if (mats == null || mats.Length == 0) continue;
                var part = new Part { mesh = mf.sharedMesh, mats = mats, local = prefab.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix };
                m.parts.Add(part);
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = part.local.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            m.bounds = b;
            m.height = Mathf.Max(0.1f, b.max.y);
            // A tree is drawn at the kit's tree height (like the generated ones); everything else at the model's own size.
            if (kind == (int)ManualKind.Tree)
            {
                float target = 0.5f * (kit.treeHeight.x + kit.treeHeight.y);
                if (target > 0.5f) m.baseScale = target / m.height;
            }
            return m;
        }

        Material Instanced(Material mat)
        {
            if (mat == null || mat.enableInstancing) return mat;
            if (!instanced.TryGetValue(mat, out var copy) || copy == null)
            {
                copy = new Material(mat) { name = mat.name + " (instanced)" };
                copy.enableInstancing = true;
                instanced[mat] = copy;
            }
            return copy;
        }

        // --- Drawing ---

        void Rebuild()
        {
            dirty = false;
            if (groundY.Count != Objects.Count) ResyncAll();
            dirty = false;
            foreach (var m in models.Values) m.objs.Clear();
            for (int i = 0; i < Objects.Count; i++)
            {
                var m = GetModel(Objects[i]);
                if (m != null) m.objs.Add(i);
            }
            foreach (var m in models.Values)
            {
                m.count = m.objs.Count;
                if (m.matrices.Length != m.parts.Count) m.matrices = new Matrix4x4[m.parts.Count][];
                for (int p = 0; p < m.parts.Count; p++)
                {
                    if (m.matrices[p] == null || m.matrices[p].Length < m.count || m.matrices[p].Length > m.count * 2 + 64) m.matrices[p] = new Matrix4x4[m.count + m.count / 4 + 16];
                }
                for (int k = 0; k < m.count; k++)
                {
                    int idx = m.objs[k];
                    var o = Objects[idx];
                    var world = Matrix4x4.TRS(new Vector3(o.x, GroundOf(idx), o.z), Quaternion.Euler(0f, o.yaw, 0f), Vector3.one * (o.scale * m.baseScale));
                    for (int p = 0; p < m.parts.Count; p++) m.matrices[p][k] = world * m.parts[p].local;
                }
            }
        }

        void LateUpdate()
        {
            var cam = cameraOverride != null ? cameraOverride : Camera.main;
            if (cam == null) return;
            if (dirty && Time.unscaledTime >= nextRebuild) { Rebuild(); nextRebuild = Time.unscaledTime + 0.12f; }
            int drawn = 0;
            foreach (var m in models.Values)
            {
                if (m.count == 0) continue;
                for (int p = 0; p < m.parts.Count; p++)
                {
                    var part = m.parts[p];
                    for (int sub = 0; sub < part.mats.Length && sub < part.mesh.subMeshCount; sub++)
                    {
                        var mat = Instanced(part.mats[sub]);
                        if (mat == null) continue;
                        var rp = new RenderParams(mat) { camera = cam, shadowCastingMode = m.shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = m.shadows };
                        for (int start = 0; start < m.count; start += Batch)
                            Graphics.RenderMeshInstanced(rp, part.mesh, sub, m.matrices[p], Mathf.Min(Batch, m.count - start), start);
                    }
                }
                drawn += m.count;
            }
            Drawn = drawn;
        }

        // --- Colliders (a run only) ---

        public const int ColliderCap = 6000;

        // Trunks (a capsule each, grouped in 32 m cells) and building boxes, so trees and buildings put by hand block the line of sight like the generated ones.
        // Returns how many were made. The 3D editor has none, so a click on the ground goes through them.
        public int AddColliders()
        {
            RemoveColliders();
            colliderRoot = new GameObject("Manual colliders");
            colliderRoot.transform.SetParent(transform, false);
            var cells = new Dictionary<long, GameObject>();
            int made = 0;
            for (int i = 0; i < Objects.Count && made < ColliderCap; i++)
            {
                var o = Objects[i];
                if (o.kind != (int)ManualKind.Tree && o.kind != (int)ManualKind.Building) continue;
                var m = GetModel(o);
                if (m == null) continue;
                float y = GroundOf(i), s = o.scale * m.baseScale;
                if (o.kind == (int)ManualKind.Tree)
                {
                    int cx = Mathf.FloorToInt(o.x / 32f), cz = Mathf.FloorToInt(o.z / 32f);
                    long key = ((long)cx << 32) ^ (uint)cz;
                    if (!cells.TryGetValue(key, out var cell))
                    {
                        cell = new GameObject($"Trunks {cx}_{cz}");
                        cell.transform.SetParent(colliderRoot.transform, false);
                        cells[key] = cell;
                    }
                    var cap = cell.AddComponent<CapsuleCollider>();
                    float h = Mathf.Max(2f, m.height * s * 0.8f);
                    cap.radius = Mathf.Clamp(0.04f * m.height * s, 0.25f, 1.2f);
                    cap.height = h;
                    cap.center = new Vector3(o.x, y + h * 0.5f, o.z);
                }
                else
                {
                    var go = new GameObject("Building");
                    go.transform.SetParent(colliderRoot.transform, false);
                    go.transform.SetPositionAndRotation(new Vector3(o.x, y, o.z), Quaternion.Euler(0f, o.yaw, 0f));
                    var box = go.AddComponent<BoxCollider>();
                    box.center = m.bounds.center * s;
                    box.size = m.bounds.size * s;
                }
                made++;
            }
            return made;
        }

        public void RemoveColliders()
        {
            if (colliderRoot != null) Destroy(colliderRoot);
            colliderRoot = null;
        }
    }
}
