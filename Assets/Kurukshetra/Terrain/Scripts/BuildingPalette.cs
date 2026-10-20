using UnityEngine;

namespace Kurukshetra.TerrainGen
{
    // Wall tint and roof colour of a building, picked from a palette by the building's own hash (an OSM index or a village
    // slot), so the same building has the same colours on every PC and in every build. Extruded buildings share ONE material
    // whose texture is the palette (the mesh UVs point at a swatch), so a thousand buildings in many colours cost no extra
    // materials and still batch. Prefab buildings get the wall tint through a MaterialPropertyBlock.
    public static class BuildingPalette
    {
        // 0 whitewash, 1 cream, 2 ochre, 3 pale yellow, 4 pink, 5 sky blue, 6 mint, 7 sand, 8 saffron, 9 light grey, 10 concrete, 11 off-white
        public static readonly Color32[] Walls =
        {
            new Color32(236, 232, 222, 255), new Color32(226, 210, 172, 255), new Color32(204, 160, 98, 255), new Color32(232, 214, 140, 255),
            new Color32(214, 160, 152, 255), new Color32(150, 186, 206, 255), new Color32(158, 198, 170, 255), new Color32(206, 188, 150, 255),
            new Color32(232, 156, 76, 255), new Color32(186, 186, 182, 255), new Color32(150, 150, 148, 255), new Color32(222, 222, 216, 255),
        };

        // 0 terracotta, 1 brick red, 2 grey tin, 3 blue tin, 4 green tin, 5 concrete, 6 brown, 7 slate
        public static readonly Color32[] Roofs =
        {
            new Color32(176, 92, 62, 255), new Color32(150, 66, 52, 255), new Color32(128, 132, 136, 255), new Color32(70, 98, 140, 255),
            new Color32(70, 118, 84, 255), new Color32(140, 140, 138, 255), new Color32(104, 76, 56, 255), new Color32(76, 80, 88, 255),
        };

        static readonly int[] HouseWalls = { 0, 1, 2, 3, 4, 5, 6, 7, 8 }, BlockWalls = { 0, 1, 9, 10, 11, 7, 5 }, FactoryWalls = { 9, 10, 11, 7 }, ShrineWalls = { 0, 11, 1, 8 };
        static readonly int[] HouseRoofs = { 0, 0, 1, 2, 3, 4, 6, 5 }, BlockRoofs = { 5, 5, 2, 7 }, FactoryRoofs = { 2, 3, 7, 2 }, ShrineRoofs = { 1, 0, 5 };

        static int[] WallSet(BuildingType t)
        {
            switch (t)
            {
                case BuildingType.Apartments: case BuildingType.Commercial: return BlockWalls;
                case BuildingType.Industrial: return FactoryWalls;
                case BuildingType.Religious: return ShrineWalls;
                default: return HouseWalls;
            }
        }

        static int[] RoofSet(BuildingType t)
        {
            switch (t)
            {
                case BuildingType.Apartments: case BuildingType.Commercial: return BlockRoofs;
                case BuildingType.Industrial: return FactoryRoofs;
                case BuildingType.Religious: return ShrineRoofs;
                default: return HouseRoofs;
            }
        }

        public static int WallIndex(int hash, BuildingType t)
        {
            var set = WallSet(t);
            return set[(int)(LayerScatter.Hash(hash, 0x3A11, 0x5EED) % (uint)set.Length)];
        }

        public static int RoofIndex(int hash, BuildingType t)
        {
            var set = RoofSet(t);
            return set[(int)(LayerScatter.Hash(hash, 0x3A12, 0x5EEE) % (uint)set.Length)];
        }

        public static Color Wall(int hash, BuildingType t) => Walls[WallIndex(hash, t)];
        public static Color Roof(int hash, BuildingType t) => Roofs[RoofIndex(hash, t)];

        // The palette texture: row 0 the wall swatches, row 1 the roof swatches, one pixel each (point filtered, no mips).
        public const int Columns = 16, Rows = 2;

        public static Vector2 WallUv(int index) => new Vector2((index + 0.5f) / Columns, 0.25f);
        public static Vector2 RoofUv(int index) => new Vector2((index + 0.5f) / Columns, 0.75f);

        static Texture2D texture;
        static Material shared;

        public static Texture2D Texture()
        {
            if (texture != null) return texture;
            var t = new Texture2D(Columns, Rows, TextureFormat.RGBA32, false, false) { name = "Building palette (runtime)", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[Columns * Rows];
            for (int i = 0; i < Columns; i++)
            {
                px[i] = i < Walls.Length ? Walls[i] : (Color32)Color.white;
                px[Columns + i] = i < Roofs.Length ? Roofs[i] : (Color32)Color.white;
            }
            t.SetPixels32(px);
            t.Apply(false, false);
            texture = t;
            return t;
        }

        // One lit material for every palette-coloured building. Built from the shared lit material in Resources, so a built game has its shader.
        public static Material SharedMaterial()
        {
            if (shared != null) return shared;
            var lit = Kurukshetra.RuntimeMaterials.Lit;
            var m = lit != null ? new Material(lit) : new Material(Kurukshetra.RuntimeMaterials.Find("Universal Render Pipeline/Lit"));
            m.name = "Building palette material (runtime)";
            m.color = Color.white;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", Texture()); else m.mainTexture = Texture();
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            m.enableInstancing = true;
            shared = m;
            return m;
        }

        static MaterialPropertyBlock block;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), ColorId = Shader.PropertyToID("_Color");

        // The wall tint of a prefab building: the palette colour pulled halfway to white, so the model keeps its own detail
        // and roof and the houses still differ from each other. A per-renderer block, not a material per building.
        public static Color PrefabTint(int hash, BuildingType t) => Color.Lerp(Color.white, Wall(hash, t), 0.55f);

        public static void TintPrefab(GameObject instance, int hash, BuildingType t)
        {
            if (block == null) block = new MaterialPropertyBlock();
            var tint = PrefabTint(hash, t);
            foreach (var r in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColor, tint);
                block.SetColor(ColorId, tint);
                r.SetPropertyBlock(block);
                block.Clear();
            }
        }
    }
}
