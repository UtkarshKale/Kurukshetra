using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    public struct BushSpot
    {
        public float x, z, yaw, scale;
        public byte source;                           // 0 round a tree, 1 scatter in the open / along a wood edge, 2 from the cover estimate
        public int hash;
    }

    // Which tree species stands where, and where the bushes go. Plain functions of position and seed (no Unity objects),
    // so a test, the Editor and every trainee PC get the same answer.
    public static class Vegetation
    {
        // Elevation bands (metres above sea level). The tree line itself is LayerScatter.TreeLineMetres.
        public const float PineLineMetres = 1500f, SnowTreesMetres = 2600f, DeadTreesMetres = 3400f, SteepPineSlope = 20f;
        public const float RingMinMetres = 3f, RingMaxMetres = 8f, TreeShareWithBushes = 0.35f;
        public const float OpenBushesPerHectare = 4f, EdgeBushesPerHectare = 10f;
        public const byte FromRing = 0, FromScatter = 1, FromCover = 2;

        // Broadleaf on the plains, pine high up and on steep ground, snow-covered trees above ~2600 m, bare dead trees near the
        // tree line (and a few more in dry country). `hash` is the tree's own number, so the choice never changes between builds.
        public static TreeClass TreeClassAt(float elevation, float slopeDegrees, SurfaceBiome biome, int hash)
        {
            float r = (LayerScatter.Hash(hash, 0x51, 0x7C1A) & 0xFFFF) / 65536f;
            float dead = biome == SurfaceBiome.Desert || biome == SurfaceBiome.HighDesert ? 0.2f : 0.03f;
            if (elevation >= DeadTreesMetres) return r < 0.4f ? TreeClass.Dead : r < 0.85f ? TreeClass.Snow : TreeClass.Pine;
            if (elevation >= SnowTreesMetres) return r < dead * 0.5f + 0.04f ? TreeClass.Dead : r < 0.5f ? TreeClass.Snow : TreeClass.Pine;
            if (elevation >= PineLineMetres || slopeDegrees > SteepPineSlope) return r < dead ? TreeClass.Dead : r < 0.92f ? TreeClass.Pine : TreeClass.Broadleaf;
            return r < dead ? TreeClass.Dead : r < 0.1f ? TreeClass.Pine : TreeClass.Broadleaf;
        }

        // 0..1: where bushes grow in the open. Strongest along the edge of a wood (the wood mask between wood and clearing),
        // a thin scatter in clearings, none on steep ground, above the tree line or in dry country beyond a trace.
        public static float OpenBushFactor(LayerGround ground, float x, float z, int treeSeed, SurfaceBiome biome)
        {
            if (ground.SlopeDegrees(x, z) > LayerScatter.TreeSlopeLimit || ground.Elevation(x, z) > LayerScatter.TreeLineMetres) return 0f;
            float mask = LayerScatter.WoodMask(x, z, treeSeed);
            float edge = 1f - Mathf.Clamp01(Mathf.Abs(mask - 0.35f) / 0.35f);
            float open = mask < 0.05f ? OpenBushesPerHectare / EdgeBushesPerHectare : 0f;
            float f = Mathf.Max(edge, open);
            return f * Mathf.Max(0.15f, LayerScatter.ClimateTreeFactor(biome));
        }

        // The bushes round the trees: about a third of the trees get a ring of 2 to 5 (times `density`), 3 to 8 m out, spread
        // round the tree in angular sectors so two bushes of one ring never touch.
        public static void RoundTrees(IList<TreeSpot> trees, float density, int seed, Func<float, float, bool> blocked, List<BushSpot> into)
        {
            if (density <= 0f) return;
            for (int i = 0; i < trees.Count; i++)
            {
                if (LayerScatter.Rand(i, 7, seed ^ 0xB052) >= TreeShareWithBushes) continue;
                float expected = (2f + 3f * LayerScatter.Rand(i, 8, seed ^ 0xB053)) * density;
                int n = Mathf.FloorToInt(expected);
                if (LayerScatter.Rand(i, 9, seed ^ 0xB054) < expected - n) n++;
                if (n <= 0) continue;
                float a0 = LayerScatter.Rand(i, 10, seed ^ 0xB055) * Mathf.PI * 2f;
                var t = trees[i];
                for (int k = 0; k < n; k++)
                {
                    float a = a0 + (k + (LayerScatter.Rand(i, 20 + k, seed ^ 0xB056) - 0.5f) * 0.5f) / n * Mathf.PI * 2f;
                    float r = Mathf.Lerp(RingMinMetres, RingMaxMetres, LayerScatter.Rand(i, 40 + k, seed ^ 0xB057));
                    float x = t.x + Mathf.Cos(a) * r, z = t.z + Mathf.Sin(a) * r;
                    if (blocked != null && blocked(x, z)) continue;
                    into.Add(MakeSpot(x, z, i * 8 + k, seed, FromRing));
                }
            }
        }

        static BushSpot MakeSpot(float x, float z, int id, int seed, byte source)
        {
            return new BushSpot { x = x, z = z, source = source, hash = (int)LayerScatter.Hash(id, source, seed ^ 0xB058), yaw = LayerScatter.Rand(id, 3, seed ^ 0xB059) * 360f, scale = LayerScatter.Rand(id, 4, seed ^ 0xB05A) };
        }

        // Cell size and row count of the open scatter over a ground.
        public static float ScatterCell(LayerGround ground) => Mathf.Max(40f, ground.Half * 2f / 500f);
        public static int ScatterRows(LayerGround ground) => Mathf.CeilToInt(ground.Half * 2f / ScatterCell(ground));

        // One row of the open scatter (the manager does a row per slice). `factor` may be replaced for the cover-estimate mode.
        public static void ScatterRow(LayerGround ground, int row, float perHectare, int treeSeed, int seed, SurfaceBiome biome, Vector2 falloffCentre, float falloffStart,
            Func<float, float, bool> blocked, List<BushSpot> into, Func<float, float, float> factorOverride = null)
        {
            float cell = ScatterCell(ground), half = ground.Half;
            int n = ScatterRows(ground);
            float cz = -half + (row + 0.5f) * cell;
            for (int ix = 0; ix < n; ix++)
            {
                float cx = -half + (ix + 0.5f) * cell;
                float f0 = factorOverride != null ? factorOverride(cx, cz) : OpenBushFactor(ground, cx, cz, treeSeed, biome);
                if (f0 <= 0f) continue;
                float fall = LayerFalloff.Of(Mathf.Sqrt((cx - falloffCentre.x) * (cx - falloffCentre.x) + (cz - falloffCentre.y) * (cz - falloffCentre.y)), falloffStart);
                float expected = perHectare * EdgeBushesPerHectare * f0 * fall * cell * cell / 10000f;
                int count = Mathf.FloorToInt(expected);
                if (LayerScatter.Rand(ix, row, seed ^ 0xB05B) < expected - count) count++;
                for (int k = 0; k < count; k++)
                {
                    float x = cx + (LayerScatter.Rand(ix * 61 + k, row, seed ^ 0xB05C) - 0.5f) * cell, z = cz + (LayerScatter.Rand(ix, row * 61 + k, seed ^ 0xB05D) - 0.5f) * cell;
                    if (!ground.Contains(x, z)) continue;
                    if (blocked != null && blocked(x, z)) continue;
                    into.Add(MakeSpot(x, z, (ix * 7919 + row) * 8 + k, seed, FromScatter));
                }
            }
        }

        // The nearest `budget` bushes to the post (0 = all). Deterministic: ties break on position. Returns a note ("" when nothing was cut).
        public static string ApplyBudget(List<BushSpot> spots, int budget, Vector2 centre)
        {
            if (budget <= 0 || spots.Count <= budget) return "";
            int before = spots.Count;
            spots.Sort((a, b) =>
            {
                float da = (a.x - centre.x) * (a.x - centre.x) + (a.z - centre.y) * (a.z - centre.y), db = (b.x - centre.x) * (b.x - centre.x) + (b.z - centre.y) * (b.z - centre.y);
                int c = da.CompareTo(db);
                return c != 0 ? c : a.hash.CompareTo(b.hash);
            });
            spots.RemoveRange(budget, spots.Count - budget);
            return $"thinned from {before} to {spots.Count} bushes (budget {budget}): the ones nearest the post kept";
        }
    }
}
