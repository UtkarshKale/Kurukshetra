using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kurukshetra.TerrainGen
{
    public enum LayerMode { Off, Realistic, Procedural }
    public enum LayerKind { Trees, Grass, Buildings, Bushes }   // Bushes last: the others keep their numbers
    public enum TreeClass { Broadleaf, Pine, Snow, Dead }
    public enum BuildingType { House, Apartments, Commercial, Industrial, Religious, Shed, Generic }

    // The trainer's choices for trees, grass and buildings. Part of the terrain config, so it travels in the LAN start
    // packet with the rest of the terrain. Realistic = the real place (OpenStreetMap, with a satellite-colour estimate
    // for trees and grass where the map has nothing); Procedural = generated from the terrain seed.
    [Serializable]
    public class TerrainLayerSettings
    {
        public LayerMode trees = LayerMode.Realistic, grass = LayerMode.Procedural, buildings = LayerMode.Realistic, bushes = LayerMode.Procedural;
        [Tooltip("Trees per hectare inside forest and wood (and in procedural woods). Single mapped trees are always kept.")]
        public float treeDensity = 60f;
        [Tooltip("The most terrain trees drawn at once (0 = no limit). Every tree costs a little time each frame (about 0.1 ms per 1000 on the development PC), so a dense forest is thinned beyond the fall-off distance and the remaining far trees are made bigger.")]
        public int treeBudget = 60000;
        [Tooltip("How far trees are drawn: Unity's terrain tree LOD bias multiplier. Higher draws trees farther (with the placeholder trees, a 20 m tree is drawn out to about 1.2 km at 0.2 and 2.9 km at 0.5) and costs frame time. A distant tree is a flat card from the prefab's last LOD.")]
        public float treeLodBias = 0.2f;
        [Tooltip("Share of full grass cover, 0 to 1.")]
        [Range(0f, 1f)] public float grassDensity = 0.7f;
        [Tooltip("Procedural mode: villages per square kilometre.")]
        public float buildingDensity = 1.5f;
        [Tooltip("Bushes: 1 = a ring of 2 to 5 bushes (3 to 8 m out) round about a third of the trees, plus a light scatter along wood edges and in the open. 0.5 = half as many, 3 = three times as many. No upper limit.")]
        public float bushDensity = 1f;
        [Tooltip("The most bushes in the world (0 = no limit). Bushes nearest the post are kept first. Drawing is instanced and limited by the draw distance below, so this mostly bounds memory and build time.")]
        public int bushBudget = 40000;
        [Tooltip("Bushes are drawn this far round the camera, metres (fewer of them the farther out). Costs frame time when raised.")]
        public float bushDrawMetres = 200f;
        [Tooltip("Trees stop the line of sight like hills and buildings do.")]
        public bool treesBlockSight = true;
        [Tooltip("Full density inside this distance from the post (km); beyond it trees thin out as distance grows.")]
        public float falloffStartKm = 1.5f;
        [Tooltip("Realistic buildings: also add procedural buildings in 500 m cells the map left empty. Labelled as filled procedurally.")]
        public bool fillGapsProcedurally;

        public LayerMode Get(LayerKind kind) => kind == LayerKind.Trees ? trees : kind == LayerKind.Grass ? grass : kind == LayerKind.Bushes ? bushes : buildings;

        public void Set(LayerKind kind, LayerMode mode)
        {
            if (kind == LayerKind.Trees) trees = mode; else if (kind == LayerKind.Grass) grass = mode; else if (kind == LayerKind.Bushes) bushes = mode; else buildings = mode;
        }

        // Realistic needs a real place; on a procedural terrain it means Procedural.
        public LayerMode Effective(LayerKind kind, bool realTerrain)
        {
            var m = Get(kind);
            return m == LayerMode.Realistic && !realTerrain ? LayerMode.Procedural : m;
        }

        public void Sanitise()
        {
            if (treeDensity < 0f || float.IsNaN(treeDensity)) treeDensity = 0f;
            if (treeBudget < 0) treeBudget = 0;
            if (treeLodBias < 0.01f || float.IsNaN(treeLodBias)) treeLodBias = 0.01f;
            grassDensity = Mathf.Clamp01(float.IsNaN(grassDensity) ? 0f : grassDensity);   // a share: 0 to 1 is physical
            if (buildingDensity < 0f || float.IsNaN(buildingDensity)) buildingDensity = 0f;
            if (bushDensity < 0f || float.IsNaN(bushDensity)) bushDensity = 0f;
            if (bushBudget < 0) bushBudget = 0;
            if (bushDrawMetres < 10f || float.IsNaN(bushDrawMetres)) bushDrawMetres = 10f;
            if (falloffStartKm < 0.05f || float.IsNaN(falloffStartKm)) falloffStartKm = 0.05f;
        }

        public TerrainLayerSettings Clone() => (TerrainLayerSettings)MemberwiseClone();
    }

    [Serializable]
    public class BuildingPrefabs
    {
        public List<GameObject> house = new List<GameObject>(), apartments = new List<GameObject>(), commercial = new List<GameObject>(),
            industrial = new List<GameObject>(), religious = new List<GameObject>(), shed = new List<GameObject>(), generic = new List<GameObject>();

        public List<GameObject> Of(BuildingType t)
        {
            switch (t)
            {
                case BuildingType.House: return house;
                case BuildingType.Apartments: return apartments;
                case BuildingType.Commercial: return commercial;
                case BuildingType.Industrial: return industrial;
                case BuildingType.Religious: return religious;
                case BuildingType.Shed: return shed;
                default: return generic;
            }
        }
    }

    // The art the layers use, replaceable without touching code. Drag your own prefabs into the lists:
    // nose / front along +Z, pivot at the ground, 1 unit = 1 metre. Placeholders made from primitives are in
    // Prefabs/LayerPlaceholders (menu Kurukshetra > Terrain > Build layer placeholders).
    [CreateAssetMenu(menuName = "Kurukshetra/Terrain Layer Kit", fileName = "TerrainLayerKit")]
    public class TerrainLayerKit : ScriptableObject
    {
        [Header("Trees   (drag your own prefabs in; nose / front along +Z, pivot at the ground, 1 unit = 1 metre)")]
        [Tooltip("One is picked per tree from the seed. A prefab with a LODGroup is drawn by distance; a collider on the trunk makes it block sight.")]
        public List<GameObject> treePrefabs = new List<GameObject>();
        [Tooltip("Tree height range, metres. Each tree is scaled to a height in this range.")]
        public Vector2 treeHeight = new Vector2(8f, 17f);

        [Header("Tree species (filled by Kurukshetra > Terrain > Wire real art into kit; when any list here is filled it is used instead of the list above)")]
        [Tooltip("Lowlands and plains. Also the fallback for a missing group.")]
        public List<GameObject> broadleafTrees = new List<GameObject>();
        [Tooltip("High ground and steep slopes.")]
        public List<GameObject> pineTrees = new List<GameObject>();
        [Tooltip("High up, where snow lies: snow-covered trees.")]
        public List<GameObject> snowTrees = new List<GameObject>();
        [Tooltip("Bare dead trees: close to the tree line and in dry country.")]
        public List<GameObject> deadTrees = new List<GameObject>();

        [Header("Bushes and rocks (drawn instanced; no colliders; a MeshFilter + MeshRenderer on the prefab, materials with GPU instancing on)")]
        public List<GameObject> bushPrefabs = new List<GameObject>();
        [Tooltip("Bushes used high up, where snow lies.")]
        public List<GameObject> bushSnowPrefabs = new List<GameObject>();
        [Tooltip("Not drawn yet (reserved).")]
        public List<GameObject> rockPrefabs = new List<GameObject>();
        [Tooltip("Scale range of one bush (1 = the model's own size).")]
        public Vector2 bushScale = new Vector2(0.8f, 1.7f);

        [Header("Grass")]
        [Tooltip("Small meshes (a clump, crossed quads) drawn instanced as terrain details. Material must have GPU instancing on.")]
        public List<GameObject> grassPrefabs = new List<GameObject>();
        [Tooltip("Or textures drawn as billboard grass, used only when there is no grass prefab.")]
        public List<Texture2D> grassTextures = new List<Texture2D>();
        public Color grassHealthy = new Color(0.45f, 0.62f, 0.25f), grassDry = new Color(0.62f, 0.58f, 0.32f);
        [Tooltip("Width and height scale range of one clump.")]
        public Vector2 grassWidth = new Vector2(0.6f, 1.0f), grassHeight = new Vector2(0.35f, 0.6f);

        [Header("Buildings (by OpenStreetMap type; an empty list means the footprint is extruded exactly)")]
        public BuildingPrefabs buildingPrefabs = new BuildingPrefabs();
        [Tooltip("Off: real (OpenStreetMap) buildings are extruded exactly from their footprints, a true 1:1 shape with a flat roof. On: a type that has prefabs uses one of them scaled to the footprint box and height.")]
        public bool realisticUsesPrefabs;
        [Tooltip("Walls and roofs of extruded footprints. Empty = a copy of the shared lit material.")]
        public Material buildingMaterial, roofMaterial;

        public const string ResourcePath = "Kurukshetra/TerrainLayerKit_Default";

        static TerrainLayerKit fallback;

        // The shipped kit (in Resources, so builds have it), or an empty one: then trees and buildings are made from simple shapes.
        public static TerrainLayerKit Default
        {
            get
            {
                var k = Resources.Load<TerrainLayerKit>(ResourcePath);
                if (k != null) return k;
                if (fallback == null) { fallback = CreateInstance<TerrainLayerKit>(); fallback.name = "Terrain layer kit (empty)"; }
                return fallback;
            }
        }

        public List<GameObject> BuildingList(BuildingType t) => buildingPrefabs != null ? buildingPrefabs.Of(t) : null;

        public static BuildingType TypeOf(string osmType)
        {
            switch (osmType)
            {
                case "house": return BuildingType.House;
                case "apartments": return BuildingType.Apartments;
                case "commercial": return BuildingType.Commercial;
                case "industrial": return BuildingType.Industrial;
                case "religious": return BuildingType.Religious;
                case "shed": return BuildingType.Shed;
                default: return BuildingType.Generic;
            }
        }

        public bool HasBuildingPrefab(BuildingType t)
        {
            var list = BuildingList(t);
            if (list == null) return false;
            foreach (var g in list) if (g != null) return true;
            return false;
        }

        public GameObject PickBuildingPrefab(BuildingType t, int hash)
        {
            var list = BuildingList(t);
            if (list == null) return null;
            var usable = new List<GameObject>();
            foreach (var g in list) if (g != null) usable.Add(g);
            return usable.Count == 0 ? null : usable[(hash & 0x7FFFFFFF) % usable.Count];
        }

        public bool HasSpeciesGroups => AnyIn(broadleafTrees) || AnyIn(pineTrees) || AnyIn(snowTrees) || AnyIn(deadTrees);

        static bool AnyIn(List<GameObject> l) { if (l != null) foreach (var g in l) if (g != null) return true; return false; }

        // The trees as a table: prefab list (what the terrain gets as prototypes) and which entries belong to which class.
        public class TreeTable
        {
            public readonly List<GameObject> prefabs = new List<GameObject>();
            public readonly List<int>[] byClass = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            public bool classed;

            // The prototype index for a class and a hash; a class with no trees falls back to broadleaf, then pine, then anything.
            public int Pick(TreeClass c, int hash)
            {
                var l = byClass[(int)c];
                if (l.Count == 0) l = byClass[(int)TreeClass.Broadleaf];
                if (l.Count == 0) l = byClass[(int)TreeClass.Pine];
                if (l.Count == 0) return (int)((uint)hash % (uint)prefabs.Count);
                return l[(int)((uint)hash % (uint)l.Count)];
            }
        }

        public TreeTable BuildTreeTable()
        {
            var t = new TreeTable();
            if (!HasSpeciesGroups)
            {
                t.prefabs.AddRange(TreePrefabsOrPlaceholders());
                for (int c = 0; c < 4; c++) for (int i = 0; i < t.prefabs.Count; i++) t.byClass[c].Add(i);
                return t;
            }
            t.classed = true;
            void Add(List<GameObject> src, TreeClass c) { if (src != null) foreach (var g in src) if (g != null) { t.byClass[(int)c].Add(t.prefabs.Count); t.prefabs.Add(g); } }
            Add(broadleafTrees, TreeClass.Broadleaf); Add(pineTrees, TreeClass.Pine); Add(snowTrees, TreeClass.Snow); Add(deadTrees, TreeClass.Dead);
            return t;
        }

        // The kit's tree prefabs, with a plain shape (trunk and crown) in place of any that is missing.
        public List<GameObject> TreePrefabsOrPlaceholders()
        {
            var list = new List<GameObject>();
            foreach (var g in treePrefabs) if (g != null) list.Add(g);
            if (list.Count == 0) list.Add(PlaceholderTree());
            return list;
        }

        // With no material of its own the kit paints extruded buildings from a palette (BuildingPalette): each building
        // picks its wall and roof colour from its hash. With a material, that material is used as it is.
        public bool UsesPalette => buildingMaterial == null && roofMaterial == null;
        public Material WallMaterial() => buildingMaterial != null ? buildingMaterial : (roofMaterial == null ? BuildingPalette.SharedMaterial() : Tinted(new Color(0.72f, 0.68f, 0.6f), "Layer wall (runtime)"));
        public Material RoofMaterial() => roofMaterial != null ? roofMaterial : (buildingMaterial == null ? BuildingPalette.SharedMaterial() : Tinted(new Color(0.5f, 0.28f, 0.22f), "Layer roof (runtime)"));

        static Material Tinted(Color c, string name)
        {
            var m = new Material(Kurukshetra.RuntimeMaterials.Lit) { name = name, color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
            m.enableInstancing = true;
            return m;
        }

        static GameObject placeholderTree;

        // A trunk and a crown built from primitives, kept off-screen and inactive. Only used when the kit has no tree.
        public static GameObject PlaceholderTree()
        {
            if (placeholderTree != null) return placeholderTree;
            var root = new GameObject("Placeholder tree (runtime)");
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.position = new Vector3(0f, -20000f, 0f);      // far below everything: it is only a source of meshes
            var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.name = "Trunk";
            trunk.transform.SetParent(root.transform, false);
            trunk.transform.localPosition = new Vector3(0f, 2f, 0f);
            trunk.transform.localScale = new Vector3(0.5f, 2f, 0.5f);
            trunk.GetComponent<Renderer>().sharedMaterial = Tinted(new Color(0.3f, 0.22f, 0.15f), "Placeholder trunk");
            var cap = trunk.GetComponent<Collider>();
            if (cap != null) DestroyImmediate(cap);
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 2f, 0f); capsule.radius = 0.3f; capsule.height = 4f;
            var crown = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            crown.name = "Crown";
            crown.transform.SetParent(root.transform, false);
            crown.transform.localPosition = new Vector3(0f, 6.5f, 0f);
            crown.transform.localScale = new Vector3(5.5f, 6f, 5.5f);
            crown.GetComponent<Renderer>().sharedMaterial = Tinted(new Color(0.2f, 0.34f, 0.14f), "Placeholder crown");
            var cc = crown.GetComponent<Collider>();
            if (cc != null) DestroyImmediate(cc);
            root.SetActive(true);
            placeholderTree = root;
            return root;
        }

        static GameObject placeholderBush;

        // A sphere, off-screen. Only used when the kit has no bush.
        public static GameObject PlaceholderBush()
        {
            if (placeholderBush != null) return placeholderBush;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Placeholder bush (runtime)";
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = new Vector3(0f, -20000f, 0f);
            var c = go.GetComponent<Collider>();
            if (c != null) DestroyImmediate(c);
            go.GetComponent<Renderer>().sharedMaterial = Tinted(new Color(0.18f, 0.32f, 0.12f), "Placeholder bush");
            placeholderBush = go;
            return go;
        }

        static GameObject placeholderGrass;

        // Two crossed quads with a drawn blade texture, off-screen. Only used when the kit has no grass.
        public static GameObject PlaceholderGrass()
        {
            if (placeholderGrass != null) return placeholderGrass;
            var go = new GameObject("Placeholder grass (runtime)");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.transform.position = new Vector3(0f, -20000f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = CrossedQuads(1f, 0.7f);
            var r = go.AddComponent<MeshRenderer>();
            var m = new Material(Kurukshetra.RuntimeMaterials.Lit) { name = "Placeholder grass" };
            var tex = BladeTexture();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex); else m.mainTexture = tex;
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
            m.EnableKeyword("_ALPHATEST_ON");
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            m.enableInstancing = true;
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            placeholderGrass = go;
            return go;
        }

        // Two vertical quads crossing at right angles, pivot at the ground.
        public static Mesh CrossedQuads(float width, float height)
        {
            float w = width * 0.5f;
            var m = new Mesh { name = "Crossed quads" };
            m.vertices = new[]
            {
                new Vector3(-w, 0f, 0f), new Vector3(w, 0f, 0f), new Vector3(w, height, 0f), new Vector3(-w, height, 0f),
                new Vector3(0f, 0f, -w), new Vector3(0f, 0f, w), new Vector3(0f, height, w), new Vector3(0f, height, -w),
            };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 };
            m.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.up };   // lit like the ground it grows from
            m.RecalculateBounds();
            return m;
        }

        // Green blades on a transparent background.
        public static Texture2D BladeTexture(int size = 64)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Blades", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1), v = y / (float)(size - 1);
                bool blade = false;
                for (int b = 0; b < 11 && !blade; b++)
                {
                    float cx = (b + 0.5f) / 11f + 0.02f * Mathf.Sin(b * 3.1f), width = Mathf.Lerp(0.04f, 0.0f, v), lean = 0.08f * Mathf.Sin(b * 1.7f) * v;
                    blade = Mathf.Abs(u - cx - lean) < width && v < 0.5f + 0.45f * ((b * 37 % 7) / 7f);
                }
                px[y * size + x] = blade ? new Color32((byte)(80 + 70 * v), (byte)(130 + 60 * v), 50, 255) : new Color32(0, 0, 0, 0);
            }
            t.SetPixels32(px);
            t.Apply(true);
            return t;
        }
    }
}
