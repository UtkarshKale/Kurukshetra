using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Chaukanna.TerrainGen.EditorTools
{
    // Chaukanna > Terrain > Build layer placeholders. Run once.
    // Makes simple stand-ins for the art of the terrain layers, all from primitives and generated meshes, with every material
    // saved as a .mat asset (so it ships in a build): two trees (conifer, broadleaf) with a cheap distant impostor and a trunk
    // collider, a grass clump, and six buildings. Then fills Resources/Chaukanna/TerrainLayerKit_Default.asset with them.
    // Replace them any time by dragging your own prefabs into that asset (nose / front along +Z, pivot at the ground, 1 unit = 1 metre).
    public static class BuildLayerPlaceholders
    {
        const string PrefabFolder = "Assets/Chaukanna/Prefabs/LayerPlaceholders";
        const string MaterialFolder = "Assets/Chaukanna/Materials/Layers";
        const string KitPath = "Assets/Chaukanna/Resources/Chaukanna/TerrainLayerKit_Default.asset";

        [MenuItem("Chaukanna/Terrain/Build layer placeholders")]
        public static void Build()
        {
            Directory.CreateDirectory(PrefabFolder + "/Meshes");
            Directory.CreateDirectory(MaterialFolder);
            AssetDatabase.Refresh();

            var trunk = Mat("Layer_Trunk", new Color(0.30f, 0.21f, 0.14f), null);
            var wall = Mat("Layer_Wall", new Color(0.72f, 0.68f, 0.60f), null);
            var roof = Mat("Layer_Roof", new Color(0.50f, 0.28f, 0.22f), null);
            var concrete = Mat("Layer_Concrete", new Color(0.58f, 0.58f, 0.58f), null);
            var white = Mat("Layer_Religious", new Color(0.88f, 0.86f, 0.80f), null);
            var dome = Mat("Layer_Dome", new Color(0.72f, 0.60f, 0.25f), null);
            var coniferLeaves = Mat("Layer_Conifer", new Color(0.12f, 0.27f, 0.15f), null);
            var broadLeaves = Mat("Layer_Broadleaf", new Color(0.22f, 0.36f, 0.14f), null);
            var coniferCard = Mat("Layer_ConiferCard", Color.white, Texture("Layer_ConiferCard", true), true);
            var broadCard = Mat("Layer_BroadleafCard", Color.white, Texture("Layer_BroadleafCard", false), true);
            var grass = Mat("Layer_Grass", Color.white, SaveTexture("Layer_Blades", TerrainLayerKit.BladeTexture(64)), true);

            var coneMesh = SaveMesh("Cone", Cone(12));
            var wedgeMesh = SaveMesh("Roof wedge", Wedge());
            var cards = SaveMesh("Crossed quads 8", Cards(1f, 1f));
            var grassMesh = SaveMesh("Grass quads", TerrainLayerKit.CrossedQuads(1f, 0.7f));

            // Trees: LOD0 trunk + crown, LOD1 crossed quads, culled below ~1 % of the screen height.
            var conifer = Tree("Tree_Conifer", trunk, coniferLeaves, coneMesh, 14f, 4.4f, 11f, coniferCard, cards, true);
            var broadleaf = Tree("Tree_Broadleaf", trunk, broadLeaves, null, 12f, 5f, 8f, broadCard, cards, false);

            // Grass clump: crossed quads.
            var clump = new GameObject("Grass_Clump");
            clump.AddComponent<MeshFilter>().sharedMesh = grassMesh;
            var gr = clump.AddComponent<MeshRenderer>();
            gr.sharedMaterial = grass; gr.shadowCastingMode = ShadowCastingMode.Off;
            var clumpPrefab = Save(clump, "Grass_Clump");

            // Buildings.
            var house = Building("Building_House", wall, roof, wedgeMesh, 8f, 6f, 3.2f, 1.3f, null);
            var apartments = Building("Building_Apartments", concrete, concrete, null, 14f, 11f, 12f, 0f, null);
            var commercial = Building("Building_Commercial", wall, concrete, null, 18f, 12f, 8f, 0f, null);
            var industrial = Building("Building_Industrial", concrete, roof, wedgeMesh, 24f, 14f, 7f, 1.2f, null);
            var religious = Building("Building_Religious", white, dome, null, 8f, 8f, 6f, 0f, dome);
            var shed = Building("Building_Shed", wall, roof, wedgeMesh, 5f, 4f, 2.5f, 0.5f, null);

            var kit = AssetDatabase.LoadAssetAtPath<TerrainLayerKit>(KitPath);
            if (kit == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KitPath));
                kit = ScriptableObject.CreateInstance<TerrainLayerKit>();
                AssetDatabase.CreateAsset(kit, KitPath);
            }
            kit.treePrefabs = new List<GameObject> { conifer, broadleaf };
            kit.treeHeight = new Vector2(8f, 17f);
            kit.grassPrefabs = new List<GameObject> { clumpPrefab };
            kit.grassTextures = new List<Texture2D>();
            kit.grassWidth = new Vector2(0.6f, 1.0f); kit.grassHeight = new Vector2(0.35f, 0.6f);
            kit.buildingPrefabs = new BuildingPrefabs
            {
                house = new List<GameObject> { house }, apartments = new List<GameObject> { apartments }, commercial = new List<GameObject> { commercial },
                industrial = new List<GameObject> { industrial }, religious = new List<GameObject> { religious }, shed = new List<GameObject> { shed }, generic = new List<GameObject> { house },
            };
            kit.realisticUsesPrefabs = false;
            kit.buildingMaterial = wall;
            kit.roofMaterial = roof;
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Layers] Placeholders built in " + PrefabFolder + " and the kit at " + KitPath + ".");
        }

        // --- Materials and textures ---

        static Material Mat(string name, Color colour, Texture2D map, bool alphaClip = false)
        {
            string path = MaterialFolder + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Chaukanna.RuntimeMaterials.Lit);
                AssetDatabase.CreateAsset(m, path);
            }
            m.name = name;
            m.color = colour;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colour);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            if (map != null) { if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", map); else m.mainTexture = map; }
            if (alphaClip)
            {
                if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
                if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
                m.renderQueue = 2450;
            }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Texture2D SaveTexture(string name, Texture2D tex)
        {
            string path = PrefabFolder + "/Meshes/" + name + ".png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // A tree silhouette for the distant card: a cone or a round crown on a trunk.
        static Texture2D Texture(string name, bool conifer)
        {
            const int W = 64, H = 128;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float u = (x + 0.5f) / W - 0.5f, v = (y + 0.5f) / H;
                bool on;
                if (conifer) on = (v > 0.22f && Mathf.Abs(u) < (1f - v) * 0.5f * 1.05f) || (v <= 0.22f && Mathf.Abs(u) < 0.04f);
                else
                {
                    float cy = 0.66f, r = 0.34f;
                    on = (u * u / (0.45f * 0.45f) + (v - cy) * (v - cy) / (r * r) < 1f) || (v <= 0.42f && Mathf.Abs(u) < 0.045f);
                }
                float shade = 0.6f + 0.4f * Mathf.PerlinNoise(x * 0.25f, y * 0.25f);
                px[y * W + x] = on ? (conifer ? new Color32((byte)(25 * shade + 15), (byte)(70 * shade + 20), (byte)(35 * shade + 15), 255) : new Color32((byte)(60 * shade + 20), (byte)(95 * shade + 25), (byte)(35 * shade + 10), 255)) : new Color32(0, 0, 0, 0);
            }
            t.SetPixels32(px);
            t.Apply();
            return SaveTexture(name, t);
        }

        // --- Meshes ---

        static Mesh SaveMesh(string name, Mesh mesh)
        {
            mesh.name = name;
            string path = PrefabFolder + "/Meshes/" + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old != null) { EditorUtility.CopySerialized(mesh, old); Object.DestroyImmediate(mesh); return old; }
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        static Mesh Cone(int sides)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            v.Add(new Vector3(0f, 1f, 0f));                    // apex
            for (int i = 0; i < sides; i++) { float a = i * Mathf.PI * 2f / sides; v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f)); }
            v.Add(new Vector3(0f, 0f, 0f));                    // base centre
            for (int i = 0; i < sides; i++)
            {
                int a = 1 + i, b = 1 + (i + 1) % sides;
                t.Add(0); t.Add(b); t.Add(a);                  // side (clockwise from outside)
                t.Add(sides + 1); t.Add(a); t.Add(b);          // base
            }
            var m = new Mesh { vertices = v.ToArray(), triangles = t.ToArray() };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // A gable roof: a triangular prism one metre wide and long, ridge 1 high along Z, sitting on y = 0.
        static Mesh Wedge()
        {
            var v = new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f), new Vector3(0f, 1f, -0.5f),
                new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0f, 1f, 0.5f),
            };
            var verts = new List<Vector3>(); var tris = new List<int>();
            void Quad(int a, int b, int c, int d) { int s = verts.Count; verts.Add(v[a]); verts.Add(v[b]); verts.Add(v[c]); verts.Add(v[d]); tris.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 }); }
            void Tri(int a, int b, int c) { int s = verts.Count; verts.Add(v[a]); verts.Add(v[b]); verts.Add(v[c]); tris.AddRange(new[] { s, s + 1, s + 2 }); }
            Quad(0, 2, 5, 3);      // left slope
            Quad(1, 4, 5, 2);      // right slope
            Tri(0, 1, 2);          // front gable
            Tri(3, 5, 4);          // back gable
            var m = new Mesh { vertices = verts.ToArray(), triangles = tris.ToArray() };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        // Two crossed quads, each double-sided: 4 quads, 8 triangles.
        static Mesh Cards(float width, float height)
        {
            float w = width * 0.5f;
            var corners = new[] { new Vector3(-w, 0f, 0f), new Vector3(w, 0f, 0f), new Vector3(w, height, 0f), new Vector3(-w, height, 0f) };
            var corners2 = new[] { new Vector3(0f, 0f, -w), new Vector3(0f, 0f, w), new Vector3(0f, height, w), new Vector3(0f, height, -w) };
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>(); var n = new List<Vector3>();
            void Add(Vector3[] c, Vector3 normal, bool flip)
            {
                int s = v.Count;
                for (int i = 0; i < 4; i++) { v.Add(c[i]); n.Add(flip ? -normal : normal); }
                uv.AddRange(flip ? new[] { new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1) } : new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) });
                if (flip) t.AddRange(new[] { s, s + 1, s + 2, s, s + 2, s + 3 }); else t.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });
            }
            Add(corners, Vector3.back, false); Add(corners, Vector3.forward, true);
            Add(corners2, Vector3.left, false); Add(corners2, Vector3.right, true);
            var m = new Mesh { vertices = v.ToArray(), uv = uv.ToArray(), triangles = t.ToArray(), normals = n.ToArray() };
            m.RecalculateBounds();
            return m;
        }

        // --- Prefabs ---

        static GameObject Save(GameObject go, string name)
        {
            string path = PrefabFolder + "/" + name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject Child(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject MeshChild(Transform parent, string name, Mesh mesh, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        static GameObject Tree(string name, Material trunkMat, Material leaves, Mesh cone, float height, float trunkHeight, float crownHeight, Material cardMat, Mesh cards, bool conifer)
        {
            var root = new GameObject(name);
            var trunk = Child(root.transform, "Trunk", PrimitiveType.Cylinder, new Vector3(0f, trunkHeight * 0.5f, 0f), new Vector3(0.6f, trunkHeight * 0.5f, 0.6f), trunkMat);
            GameObject crown;
            if (conifer) crown = MeshChild(root.transform, "Crown", cone, new Vector3(0f, height - crownHeight - 0.2f + 0.2f, 0f), new Vector3(crownHeight * 0.55f, crownHeight, crownHeight * 0.55f), leaves);
            else crown = Child(root.transform, "Crown", PrimitiveType.Sphere, new Vector3(0f, trunkHeight + crownHeight * 0.4f, 0f), new Vector3(crownHeight * 0.95f, crownHeight * 0.8f, crownHeight * 0.95f), leaves);
            // Distant impostor: two crossed cards as tall and wide as the tree.
            var card = MeshChild(root.transform, "Impostor", cards, Vector3.zero, new Vector3(height * (conifer ? 0.42f : 0.62f), height, height * (conifer ? 0.42f : 0.62f)), cardMat);
            foreach (var r in new[] { trunk, crown, card }) r.GetComponent<MeshRenderer>().shadowCastingMode = r == card ? ShadowCastingMode.Off : ShadowCastingMode.On;
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, trunkHeight * 0.5f + 0.5f, 0f); capsule.radius = 0.35f; capsule.height = trunkHeight + 1f;
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[]
            {
                new LOD(0.1f, new Renderer[] { trunk.GetComponent<MeshRenderer>(), crown.GetComponent<MeshRenderer>() }),
                new LOD(0.006f, new Renderer[] { card.GetComponent<MeshRenderer>() }),
            });
            lod.RecalculateBounds();
            return Save(root, name);
        }

        // A box with an optional roof wedge, a single-box LOD1 and a box collider. Footprint width x length, wall height; roof adds roofHeight.
        static GameObject Building(string name, Material wallMat, Material roofMat, Mesh roofMesh, float width, float length, float wallHeight, float roofHeight, Material domeMat)
        {
            var root = new GameObject(name);
            var walls = Child(root.transform, "Walls", PrimitiveType.Cube, new Vector3(0f, wallHeight * 0.5f, 0f), new Vector3(width, wallHeight, length), wallMat);
            var near = new List<Renderer> { walls.GetComponent<Renderer>() };
            if (roofMesh != null && roofHeight > 0f) near.Add(MeshChild(root.transform, "Roof", roofMesh, new Vector3(0f, wallHeight, 0f), new Vector3(width * 1.06f, roofHeight, length * 1.06f), roofMat).GetComponent<Renderer>());
            else { var slab = Child(root.transform, "Roof", PrimitiveType.Cube, new Vector3(0f, wallHeight + 0.15f, 0f), new Vector3(width * 1.02f, 0.3f, length * 1.02f), roofMat); near.Add(slab.GetComponent<Renderer>()); }
            if (domeMat != null) near.Add(Child(root.transform, "Dome", PrimitiveType.Sphere, new Vector3(0f, wallHeight + 0.4f, 0f), new Vector3(width * 0.6f, width * 0.6f, width * 0.6f), domeMat).GetComponent<Renderer>());
            var box = Child(root.transform, "Far", PrimitiveType.Cube, new Vector3(0f, (wallHeight + roofHeight) * 0.5f, 0f), new Vector3(width, wallHeight + roofHeight, length), wallMat);
            foreach (var r in near) r.shadowCastingMode = ShadowCastingMode.On;
            box.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            var col = root.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, (wallHeight + roofHeight) * 0.5f, 0f); col.size = new Vector3(width, wallHeight + roofHeight, length);
            var lod = root.AddComponent<LODGroup>();
            lod.SetLODs(new[] { new LOD(0.06f, near.ToArray()), new LOD(0.01f, new[] { box.GetComponent<Renderer>() }) });
            lod.RecalculateBounds();
            return Save(root, name);
        }
    }
}
