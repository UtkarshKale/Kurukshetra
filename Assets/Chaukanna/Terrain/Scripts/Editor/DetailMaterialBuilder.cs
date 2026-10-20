using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // Makes the ground materials for the terrain (rock, scree, soil, grass, snow, sand): a tileable colour texture and a
    // normal map for each, drawn from noise in code (no downloaded textures), plus their TerrainLayers and a surface kit.
    // Menu: Chaukanna > Terrain > Build Detail Materials. Running it again rebuilds them in place.
    public static class DetailMaterialBuilder
    {
        public const string Folder = "Assets/Chaukanna/Terrain/Layers/Detail";
        public const string KitPath = Folder + "/SurfaceKit.asset";
        const int Size = 512;

        [MenuItem("Chaukanna/Terrain/Build Detail Materials")]
        public static void BuildMenu()
        {
            var kit = Build();
            EditorGUIUtility.PingObject(kit);
            Debug.Log("[Terrain] Detail materials built in " + Folder);
        }

        public static TerrainSurfaceKit Build()
        {
            Directory.CreateDirectory(Folder);
            var kit = AssetDatabase.LoadAssetAtPath<TerrainSurfaceKit>(KitPath);
            if (kit == null) { kit = ScriptableObject.CreateInstance<TerrainSurfaceKit>(); AssetDatabase.CreateAsset(kit, KitPath); }

            kit.rock = Make("Rock", 14f, 0.08f, 1.2f, Rock);
            kit.scree = Make("Scree", 5f, 0.06f, 1.0f, Scree);
            kit.soil = Make("Soil", 7f, 0.04f, 0.7f, Soil);
            kit.grass = Make("Grass", 5f, 0.03f, 0.8f, Grass);
            kit.snow = Make("Snow", 10f, 0.35f, 0.5f, Snow);
            kit.sand = Make("Sand", 9f, 0.05f, 0.6f, Sand);
            EditorUtility.SetDirty(kit);
            AssetDatabase.SaveAssets();
            return kit;
        }

        // One material: fill(u, v) gives height (0..1) and colour at a texel; u, v in 0..1 and the pattern must wrap.
        delegate void Fill(float u, float v, out float height, out Color colour);

        static TerrainLayer Make(string name, float tileMetres, float smoothness, float normalStrength, Fill fill)
        {
            var height = new float[Size * Size];
            var albedo = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                fill((x + 0.5f) / Size, (y + 0.5f) / Size, out float h, out Color c);
                height[y * Size + x] = h;
                albedo[y * Size + x] = c;
            }
            var normals = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float dx = height[y * Size + (x + 1) % Size] - height[y * Size + (x + Size - 1) % Size];
                float dy = height[((y + 1) % Size) * Size + x] - height[((y + Size - 1) % Size) * Size + x];
                var n = new Vector3(-dx * normalStrength * 8f, -dy * normalStrength * 8f, 1f).normalized;
                normals[y * Size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }

            string albedoPath = $"{Folder}/{name}_albedo.png", normalPath = $"{Folder}/{name}_normal.png";
            WritePng(albedoPath, albedo);
            WritePng(normalPath, normals);
            AssetDatabase.ImportAsset(albedoPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(normalPath, ImportAssetOptions.ForceUpdate);
            var ai = (TextureImporter)AssetImporter.GetAtPath(albedoPath);
            ai.textureType = TextureImporterType.Default; ai.sRGBTexture = true; ai.wrapMode = TextureWrapMode.Repeat; ai.anisoLevel = 4; ai.mipmapEnabled = true;
            ai.SaveAndReimport();
            var ni = (TextureImporter)AssetImporter.GetAtPath(normalPath);
            ni.textureType = TextureImporterType.NormalMap; ni.wrapMode = TextureWrapMode.Repeat; ni.anisoLevel = 4;
            ni.SaveAndReimport();

            string layerPath = $"{Folder}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
            bool create = layer == null;
            if (create) layer = new TerrainLayer();
            layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            layer.normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(tileMetres, tileMetres);
            layer.smoothness = smoothness;
            layer.metallic = 0f;
            if (create) AssetDatabase.CreateAsset(layer, layerPath); else EditorUtility.SetDirty(layer);
            return layer;
        }

        static void WritePng(string path, Color32[] pixels)
        {
            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
        }

        // --- The materials. Colours are typical ground albedos; variation comes from noise that wraps at the edges. ---

        static void Rock(float u, float v, out float h, out Color c)
        {
            // Weathered bare rock: big ridged forms, layered strata, a few warped fractures (not paving).
            float wu = u + 0.08f * (TileNoise.Fbm(u, v, 3, 3, 15) - 0.5f), wv = v + 0.08f * (TileNoise.Fbm(u, v, 3, 3, 16) - 0.5f);
            float ridged = TileNoise.Ridged(u, v, 3, 6, 11);
            float strata = 0.5f + 0.5f * Mathf.Sin((v * 7f + TileNoise.Fbm(u, v, 2, 4, 12) * 2.2f) * Mathf.PI * 2f);
            float fracture = TileNoise.CellEdge(wu, wv, 4, 13);
            float grain = TileNoise.Fbm(u, v, 32, 3, 17);
            h = Mathf.Clamp01(0.55f * ridged + 0.2f * strata + 0.15f * grain + 0.1f * fracture);
            float tone = TileNoise.Fbm(u, v, 2, 4, 14);
            var dark = Color.Lerp(new Color(0.26f, 0.24f, 0.21f), new Color(0.31f, 0.29f, 0.27f), tone);
            var light = Color.Lerp(new Color(0.56f, 0.50f, 0.42f), new Color(0.52f, 0.50f, 0.47f), tone);
            c = Color.Lerp(dark, light, Mathf.Clamp01(0.8f * h + 0.2f * strata)) * Mathf.Lerp(0.82f, 1f, Mathf.Clamp01(fracture * 1.5f)) * Mathf.Lerp(0.92f, 1.05f, grain);
            c.a = 1f;
        }

        static void Scree(float u, float v, out float h, out Color c)
        {
            // Loose angular stones of two sizes lying in gravel.
            float wu = u + 0.05f * (TileNoise.Fbm(u, v, 6, 2, 23) - 0.5f), wv = v + 0.05f * (TileNoise.Fbm(u, v, 6, 2, 24) - 0.5f);
            TileNoise.Cells(wu, wv, 9, 21, out float b1, out float b2, out float bid);
            TileNoise.Cells(wu, wv, 30, 25, out float s1, out float s2, out float sid);
            bool bigStone = bid > 0.45f;
            float big = bigStone ? Mathf.Clamp01((b2 - b1) * 3.5f) : 0f;
            float small = Mathf.Clamp01((s2 - s1) * 4f) * (sid > 0.3f ? 1f : 0.3f);
            float gravel = TileNoise.Fbm(u, v, 64, 2, 22);
            float stone = Mathf.Max(big, small * 0.8f);
            h = Mathf.Clamp01(0.75f * stone + 0.25f * gravel);
            float tint = ((big > small ? bid : sid) - 0.5f) * 0.22f;
            var stoneCol = new Color(0.50f + tint, 0.46f + tint * 0.9f, 0.40f + tint * 0.8f);
            var gravelCol = Color.Lerp(new Color(0.36f, 0.33f, 0.28f), new Color(0.48f, 0.44f, 0.38f), gravel);
            c = Color.Lerp(gravelCol, stoneCol * Mathf.Lerp(0.8f, 1.05f, stone), Mathf.SmoothStep(0.05f, 0.35f, stone));
            c.a = 1f;
        }
        static void Soil(float u, float v, out float h, out Color c)
        {
            float f = TileNoise.Fbm(u, v, 6, 5, 31);
            TileNoise.Cells(u, v, 40, 32, out float f1, out float f2, out float id);
            float pebble = id > 0.82f ? Mathf.Clamp01(1f - f1 * 3f) : 0f;
            h = Mathf.Clamp01(0.75f * f + 0.25f * pebble);
            c = Color.Lerp(new Color(0.34f, 0.26f, 0.18f), new Color(0.50f, 0.40f, 0.29f), f);
            c = Color.Lerp(c, new Color(0.55f, 0.52f, 0.47f), pebble * 0.7f);
            c.a = 1f;
        }

        static void Grass(float u, float v, out float h, out Color c)
        {
            float fine = TileNoise.Fbm(u * 1f, v * 1f, 32, 4, 41);
            float blades = TileNoise.Fbm(u, v * 0.25f, 64, 3, 42);
            float patches = TileNoise.Fbm(u, v, 2, 3, 43);
            h = Mathf.Clamp01(0.6f * fine + 0.4f * blades);
            var green = Color.Lerp(new Color(0.13f, 0.20f, 0.06f), new Color(0.30f, 0.37f, 0.12f), h);
            var dry = Color.Lerp(new Color(0.33f, 0.31f, 0.16f), new Color(0.47f, 0.43f, 0.24f), h);
            c = Color.Lerp(green, dry, Mathf.SmoothStep(0.45f, 0.75f, patches) * 0.7f);
            c.a = 1f;
        }

        static void Snow(float u, float v, out float h, out Color c)
        {
            float f = TileNoise.Fbm(u, v, 4, 5, 51);
            float sparkle = TileNoise.Fbm(u, v, 64, 2, 52);
            h = Mathf.Clamp01(0.85f * f + 0.15f * sparkle);
            c = Color.Lerp(new Color(0.74f, 0.79f, 0.88f), new Color(0.93f, 0.94f, 0.96f), f);
            c.a = 1f;
        }

        static void Sand(float u, float v, out float h, out Color c)
        {
            float warp = TileNoise.Fbm(u, v, 3, 3, 61);
            float ripple = 0.5f + 0.5f * Mathf.Sin((v * 14f + warp * 2.5f) * Mathf.PI * 2f);
            float grain = TileNoise.Fbm(u, v, 64, 2, 62);
            h = Mathf.Clamp01(0.7f * ripple + 0.3f * grain);
            c = Color.Lerp(new Color(0.66f, 0.55f, 0.39f), new Color(0.80f, 0.70f, 0.52f), 0.6f * ripple + 0.4f * grain);
            c.a = 1f;
        }
    }
}
