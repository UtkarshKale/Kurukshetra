using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kurukshetra.TerrainGen.EditorTools
{
    // Kurukshetra > Terrain > Wire real art into kit. Safe to run again (it rebuilds its own prefabs and refills the lists).
    // Wraps art that is already in the project (Quaternius Ultimate Nature Pack, Kenney suburban and commercial kits, all CC0)
    // in prefabs under Assets/Kurukshetra/Prefabs/LayerKit/ and fills Resources/Kurukshetra/TerrainLayerKit_Default.asset:
    //   trees   by species (broadleaf / pine / snow / dead), each with a trunk collider and a card impostor for far LODs;
    //   bushes  as plain mesh prefabs whose material copies have GPU instancing on (they are drawn by BushField);
    //   houses  and blocks for procedural villages, each with a box collider and a distance cull.
    // The third-party files are never edited. Materials stay URP/Lit (their import already is), so a build does not draw pink.
    public static class WireRealArt
    {
        const string Nature = "Assets/these assets/Ultimate Nature Pack by Quaternius/FBX/";
        const string Suburban = "Assets/these assets/kenney_city-kit-suburban_20/Models/FBX format/";
        const string Commercial = "Assets/these assets/kenney_city-kit-commercial_2.1/Models/FBX format/";
        const string PrefabFolder = "Assets/Kurukshetra/Prefabs/LayerKit";
        const string MaterialFolder = "Assets/Kurukshetra/Materials/Layers";
        const string PlaceholderMeshes = "Assets/Kurukshetra/Prefabs/LayerPlaceholders/Meshes/Crossed quads 8.asset";
        const string KitPath = "Assets/Kurukshetra/Resources/Kurukshetra/TerrainLayerKit_Default.asset";

        static readonly string[] Broadleaf = { "CommonTree_1", "CommonTree_2", "CommonTree_3", "BirchTree_1", "BirchTree_2", "Willow_1" };
        static readonly string[] Pine = { "PineTree_1", "PineTree_2", "PineTree_3", "PineTree_4" };
        static readonly string[] Snow = { "PineTree_Snow_1", "PineTree_Snow_2", "CommonTree_Snow_1", "CommonTree_Dead_Snow_1" };
        static readonly string[] Dead = { "CommonTree_Dead_1", "CommonTree_Dead_2", "BirchTree_Dead_1" };
        static readonly string[] Bushes = { "Bush_1", "Bush_2", "BushBerries_1", "BushBerries_2" };
        static readonly string[] SnowBushes = { "Bush_Snow_1", "Bush_Snow_2" };
        static readonly string[] Rocks = { "Rock_1", "Rock_2", "Rock_3", "Rock_Moss_1", "Rock_Moss_2", "Rock_Snow_1", "Rock_Snow_2" };
        static readonly string[] Houses = { "building-type-a", "building-type-b", "building-type-c", "building-type-d", "building-type-e", "building-type-f", "building-type-g", "building-type-h", "building-type-i", "building-type-j" };
        static readonly string[] Sheds = { "building-type-h", "building-type-i", "building-type-m" };
        static readonly string[] Blocks = { "building-a", "building-b", "building-d", "building-f", "building-g", "building-h", "building-i" };
        static readonly string[] Shops = { "building-c", "building-e", "building-j", "building-k", "building-n" };
        static readonly string[] Works = { "low-detail-building-wide-a", "low-detail-building-wide-b" };

        [MenuItem("Kurukshetra/Terrain/Wire real art into kit")]
        public static void Wire()
        {
            Directory.CreateDirectory(PrefabFolder);
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();

            if (AssetDatabase.LoadAssetAtPath<Mesh>(PlaceholderMeshes) == null || AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Layer_ConiferCard.mat") == null)
                BuildLayerPlaceholders.Build();       // the card mesh and card materials come from the placeholders
            var cards = AssetDatabase.LoadAssetAtPath<Mesh>(PlaceholderMeshes);
            var coniferCard = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Layer_ConiferCard.mat");
            var broadCard = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Layer_BroadleafCard.mat");
            var snowCard = CardCopy("Veg_Card_Snow", coniferCard, new Color(0.9f, 0.95f, 1f));
            var deadCard = CardCopy("Veg_Card_Dead", broadCard, new Color(1.2f, 0.9f, 0.7f));

            var kit = AssetDatabase.LoadAssetAtPath<TerrainLayerKit>(KitPath);
            if (kit == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KitPath));
                kit = ScriptableObject.CreateInstance<TerrainLayerKit>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }

            int missing = 0;
            kit.broadleafTrees = Trees(Broadleaf, broadCard, cards, ref missing);
            kit.pineTrees = Trees(Pine, coniferCard, cards, ref missing);
            kit.snowTrees = Trees(Snow, snowCard, cards, ref missing);
            kit.deadTrees = Trees(Dead, deadCard, cards, ref missing);
            kit.bushPrefabs = BushModels(Bushes, ref missing);
            kit.bushSnowPrefabs = BushModels(SnowBushes, ref missing);
            kit.rockPrefabs = Raw(Rocks, ref missing);
            kit.bushScale = new Vector2(0.9f, 1.7f);
            kit.buildingPrefabs = new BuildingPrefabs
            {
                house = Buildings(Suburban, Houses, ref missing), shed = Buildings(Suburban, Sheds, ref missing),
                apartments = Buildings(Commercial, Blocks, ref missing), commercial = Buildings(Commercial, Shops, ref missing),
                industrial = Buildings(Commercial, Works, ref missing), religious = new List<GameObject>(), generic = Buildings(Suburban, Houses, ref missing),
            };
            kit.realisticUsesPrefabs = false;           // real footprints stay exact
            kit.buildingMaterial = null;                // extruded buildings take wall and roof colours from the palette (BuildingPalette)
            kit.roofMaterial = null;
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Layers] Real art wired into {KitPath}: {kit.broadleafTrees.Count} broadleaf, {kit.pineTrees.Count} pine, {kit.snowTrees.Count} snow, {kit.deadTrees.Count} dead trees; {kit.bushPrefabs.Count}+{kit.bushSnowPrefabs.Count} bushes; {kit.rockPrefabs.Count} rocks; {kit.buildingPrefabs.house.Count} houses, {kit.buildingPrefabs.apartments.Count} blocks. Missing source files: {missing}.");
        }

        // --- Trees ---

        static List<GameObject> Trees(string[] names, Material card, Mesh cards, ref int missing)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Nature + n + ".fbx");
                if (fbx == null) { missing++; continue; }
                var root = new GameObject("Tree_" + n);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
                model.name = "Model";
                var rs = model.GetComponentsInChildren<Renderer>(true);
                var b = Bounds(rs);
                float h = Mathf.Max(0.5f, b.max.y), w = Mathf.Max(b.size.x, b.size.z);
                var impostor = new GameObject("Impostor");
                impostor.transform.SetParent(root.transform, false);
                impostor.transform.localScale = new Vector3(w * 1.1f, h, w * 1.1f);
                impostor.AddComponent<MeshFilter>().sharedMesh = cards;
                var ir = impostor.AddComponent<MeshRenderer>();
                ir.sharedMaterial = card; ir.shadowCastingMode = ShadowCastingMode.Off; ir.receiveShadows = false;
                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.center = new Vector3(0f, h * 0.25f, 0f); capsule.radius = Mathf.Max(0.03f, h * 0.03f); capsule.height = h * 0.5f;
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.04f, rs), new LOD(0.003f, new Renderer[] { ir }) });     // 0.04: the terrain scales sizes by treeLodBias (0.2), so the full model shows out to about 50 m; 0.01 (about 250 m) was being tested when this PC GPU reset (cause not isolated)
                lod.RecalculateBounds();
                list.Add(Save(root, "Tree_" + n));
            }
            return list;
        }

        static Material CardCopy(string name, Material source, Color tint)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(source); AssetDatabase.CreateAsset(m, path); }
            else EditorUtility.CopySerialized(source, m);
            m.name = name;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
            m.color = tint;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // --- Bushes and rocks ---

        // A prefab with one child holding the model's mesh and its transform from the FBX; the material copies are saved assets with instancing on.
        static List<GameObject> BushModels(string[] names, ref int missing)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Nature + n + ".fbx");
                var mf = fbx != null ? fbx.GetComponentInChildren<MeshFilter>(true) : null;
                var mr = fbx != null ? fbx.GetComponentInChildren<MeshRenderer>(true) : null;
                if (mf == null || mr == null) { missing++; continue; }
                var root = new GameObject("Bush_" + n);
                var child = new GameObject("Model");
                child.transform.SetParent(root.transform, false);
                var local = fbx.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                child.transform.localPosition = local.GetColumn(3);
                child.transform.localRotation = local.rotation;
                child.transform.localScale = local.lossyScale;
                child.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = child.AddComponent<MeshRenderer>();
                var src = mr.sharedMaterials;
                var copies = new Material[src.Length];
                for (int i = 0; i < src.Length; i++) copies[i] = InstancedCopy(src[i], "Veg_" + n + "_" + i);
                r.sharedMaterials = copies;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                list.Add(Save(root, "Bush_" + n));
            }
            return list;
        }

        static Material InstancedCopy(Material source, string name)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(source); AssetDatabase.CreateAsset(m, path); }
            else EditorUtility.CopySerialized(source, m);
            m.name = name;
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static List<GameObject> Raw(string[] names, ref int missing)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(Nature + n + ".fbx");
                if (fbx == null) { missing++; continue; }
                list.Add(fbx);
            }
            return list;
        }

        // --- Buildings ---

        static List<GameObject> Buildings(string folder, string[] names, ref int missing)
        {
            var list = new List<GameObject>();
            foreach (var n in names)
            {
                var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(folder + n + ".fbx");
                if (fbx == null) { missing++; continue; }
                string prefabName = (folder == Suburban ? "Suburban_" : "Commercial_") + n;
                var root = new GameObject(prefabName);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(fbx, root.transform);
                model.name = "Model";
                var rs = model.GetComponentsInChildren<Renderer>(true);
                var b = Bounds(rs);
                var box = root.AddComponent<BoxCollider>();
                box.center = b.center; box.size = b.size;
                var lod = root.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.012f, rs) });     // distance cull: a house under ~1 % of the screen height is dropped
                lod.RecalculateBounds();
                list.Add(Save(root, prefabName));
            }
            return list;
        }

        // --- Helpers ---

        static Bounds Bounds(Renderer[] rs)
        {
            if (rs.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static GameObject Save(GameObject go, string name)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }
    }
}
