using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // Shared rules for the distance cut-offs (GraphicsPrefs.ViewDistanceScale) so nothing looks like a glitch: every range is
    // scaled by the player's view distance, and anything that ends at a range fades out over its last 20 % instead of popping.
    public static class ViewDistance
    {
        public const float FadeBand = 0.2f;                 // fraction of a range, at its far end, over which things fade out
        public const float GrassFloor = 100f, BushFloor = 100f, TreeBillboardFloor = 150f, TreeDistanceFloor = 2000f;

        public static float Scale => Mathf.Max(0.05f, GraphicsPrefs.ViewDistanceScale);

        // A base range times the view-distance scale, never below `floor` (so nothing starts appearing close to the camera on Low).
        public static float Range(float baseMetres, float floor) => Mathf.Max(Mathf.Min(baseMetres, floor), baseMetres * Scale);

        // 1 inside the range's first 80 %, easing to 0 at the range.
        public static float FadeOut(float d, float range)
        {
            float t = Mathf.Clamp01((range - d) / Mathf.Max(0.01f, range * FadeBand));
            return t * t * (3f - 2f * t);
        }

        // Fraction of instances kept at distance d: full to 40 % of the range, down to 25 % at 80 %, a continuous ramp (no steps).
        public static float Density(float d, float range)
        {
            float t = Mathf.Clamp01((d / range - 0.4f) / 0.4f);
            return Mathf.Lerp(1f, 0.25f, t * t * (3f - 2f * t));
        }

        // A stable 0..1 number for an instance, from where it stands (the same bush gets the same number every frame).
        public static float Hash01(float x, float z)
        {
            unchecked
            {
                uint h = (uint)((int)(x * 37f) * 73856093 ^ (int)(z * 37f) * 19349663);
                h ^= h >> 13; h *= 0x5bd1e995u; h ^= h >> 15;
                return (h & 0xFFFF) / 65535f;
            }
        }

        // How big (0..1) an instance is drawn at distance d: 0 when its hash is past the density threshold or it is past the range, with
        // a short ramp in between so it grows in and out instead of popping. Includes the end-of-range fade.
        public static float InstanceScale(float d, float range, float x, float z)
        {
            float keep = Density(d, range);
            float s = Mathf.Clamp01((keep - Hash01(x, z)) * 6f);
            return s * FadeOut(d, range);
        }

        // Uniformly scales a TRS matrix about its own position.
        public static void ScaleAboutPosition(ref Matrix4x4 m, float s)
        {
            m.m00 *= s; m.m10 *= s; m.m20 *= s;
            m.m01 *= s; m.m11 *= s; m.m21 *= s;
            m.m02 *= s; m.m12 *= s; m.m22 *= s;
        }

        // --- Terrain trees: Unity draws these, so the distances are set on the terrain tiles. ---

        struct TreeBase { public float treeDistance, billboard; }
        static readonly Dictionary<Terrain, TreeBase> terrains = new Dictionary<Terrain, TreeBase>();
        static readonly List<Terrain> dead = new List<Terrain>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            terrains.Clear();
            GraphicsPrefs.ViewDistanceChanged -= ApplyAll;
            GraphicsPrefs.ViewDistanceChanged += ApplyAll;
        }

        // Remembers a tile's unscaled tree distances and applies the current scale. The billboard cross-fade is 20 % of the billboard distance.
        public static void RegisterTerrain(Terrain t, float treeDistance, float billboardDistance)
        {
            if (t == null) return;
            terrains[t] = new TreeBase { treeDistance = treeDistance, billboard = billboardDistance };
            ApplyTo(t, terrains[t]);
        }

        static void ApplyTo(Terrain t, TreeBase b)
        {
            float bb = Range(b.billboard, TreeBillboardFloor);
            t.treeDistance = Range(b.treeDistance, TreeDistanceFloor);
            t.treeBillboardDistance = bb;
            t.treeCrossFadeLength = Mathf.Max(5f, bb * 0.2f);                 // far trees blend into their cards over the last 20 % instead of swapping
        }

        static void ApplyAll()
        {
            dead.Clear();
            foreach (var kv in terrains)
            {
                if (kv.Key == null) { dead.Add(kv.Key); continue; }
                ApplyTo(kv.Key, kv.Value);
            }
            foreach (var t in dead) terrains.Remove(t);
        }
    }
}
