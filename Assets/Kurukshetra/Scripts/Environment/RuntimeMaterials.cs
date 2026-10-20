using UnityEngine;

namespace Kurukshetra
{
    // Materials and shaders for things built at runtime (the generated terrain, the site, the VR hands, effects).
    // In the Editor Shader.Find always works, but a built game only contains shaders that some scene or asset in the build
    // references; the generated terrain is in no scene, so its URP Terrain/Lit shader was left out and it drew pink.
    // The materials in Assets/Kurukshetra/Resources/Kurukshetra/ are always built in, and carry their shaders with them.
    public static class RuntimeMaterials
    {
        public const string Folder = "Kurukshetra/";
        static Material terrain, lit, unlit;
        static TerrainLayer neutralGround;
        static Texture2D neutralGroundTexture;
        static bool terrainWarned;

        // A dry-earth colour for the ground when nothing better exists (no ground materials, no satellite picture).
        public static readonly Color GroundColour = new Color(0.42f, 0.40f, 0.30f, 1f);

        // The terrain material. Normally Resources/Kurukshetra/TerrainLit (always in a build, with its shader). If that is missing or its
        // shader is unusable it logs an error ONCE and falls back, so the ground is never the pink error shader: first the URP terrain
        // shader found by name, then the Lit material in the ground colour.
        public static Material Terrain
        {
            get
            {
                if (terrain != null) return terrain;
                var m = Resources.Load<Material>(Folder + "TerrainLit");
                if (m != null && Usable(m.shader)) return terrain = m;
                if (!terrainWarned)
                {
                    terrainWarned = true;
                    Debug.LogError("[RuntimeMaterials] Resources/" + Folder + "TerrainLit is missing or its shader is unusable (" + (m != null && m.shader != null ? m.shader.name : "no material") + "). Using a fallback ground material instead of the pink error shader.");
                }
                var shader = Shader.Find("Universal Render Pipeline/Terrain/Lit");
                if (Usable(shader)) return terrain = new Material(shader) { name = "TerrainLit (fallback)" };
                var plain = Lit;
                if (plain != null) { terrain = new Material(plain) { name = "Ground (fallback)", color = GroundColour }; return terrain; }
                var lite = Shader.Find("Universal Render Pipeline/Lit");
                if (Usable(lite)) return terrain = new Material(lite) { name = "Ground (fallback)", color = GroundColour };
                return null;
            }
        }

        static bool Usable(Shader s) => s != null && s.isSupported && !s.name.StartsWith("Hidden/InternalError", System.StringComparison.OrdinalIgnoreCase);

        // One plain ground-coloured terrain layer, for a terrain tile that would otherwise have no layer at all (no ground materials and
        // no satellite picture, for instance a half-downloaded area): such a tile must show neutral earth, never pink or white.
        public static TerrainLayer NeutralGround
        {
            get
            {
                if (neutralGround != null) return neutralGround;
                if (neutralGroundTexture == null)
                {
                    neutralGroundTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "Neutral ground", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Repeat };
                    var px = new Color[16];
                    for (int i = 0; i < px.Length; i++) { float v = 1f + ((i * 7) % 5 - 2) * 0.02f; px[i] = new Color(GroundColour.r * v, GroundColour.g * v, GroundColour.b * v, 1f); }
                    neutralGroundTexture.SetPixels(px);
                    neutralGroundTexture.Apply(false, true);
                }
                neutralGround = new TerrainLayer { name = "Neutral ground", diffuseTexture = neutralGroundTexture, tileSize = new Vector2(8f, 8f), smoothness = 0f, metallic = 0f };
                neutralGround.hideFlags = HideFlags.HideAndDontSave;
                return neutralGround;
            }
        }

        public static Material Lit => lit != null ? lit : (lit = Resources.Load<Material>(Folder + "Lit"));
        public static Material Unlit => unlit != null ? unlit : (unlit = Resources.Load<Material>(Folder + "Unlit"));

        // Shader.Find, falling back to the shader of a built-in material, so a built game never draws pink.
        public static Shader Find(string name)
        {
            var s = Shader.Find(name);
            if (s != null && s.isSupported) return s;
            Material m = name.Contains("Terrain") ? Terrain : name.Contains("Unlit") || name.StartsWith("Sprites") ? Unlit : Lit;
            return m != null ? m.shader : s;
        }
    }
}
