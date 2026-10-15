using UnityEngine;

namespace Chaukanna
{
    // Materials and shaders for things built at runtime (the generated terrain, the site, the VR hands, effects).
    // In the Editor Shader.Find always works, but a built game only contains shaders that some scene or asset in the build
    // references; the generated terrain is in no scene, so its URP Terrain/Lit shader was left out and it drew pink.
    // The materials in Assets/Chaukanna/Resources/Chaukanna/ are always built in, and carry their shaders with them.
    public static class RuntimeMaterials
    {
        public const string Folder = "Chaukanna/";
        static Material terrain, lit, unlit;

        public static Material Terrain => terrain != null ? terrain : (terrain = Resources.Load<Material>(Folder + "TerrainLit"));
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
