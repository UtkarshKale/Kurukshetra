using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    public struct TreeSpot
    {
        public float x, z, height, yaw, width;       // width: relative girth, around 1
        public byte source;                          // 0 mapped tree, 1 mapped row, 2 mapped forest, 3 satellite estimate, 4 procedural
    }

    public struct BuildingSpot
    {
        public float x, z, yaw, width, depth, height;
        public BuildingType type;
        public int hash;
    }

    // Where things go, deterministically from a seed. Nothing here touches Unity objects, so the same call gives the same
    // answer in a test, in the Editor and on every trainee PC.
    public static class LayerScatter
    {
        public const byte FromNode = 0, FromRow = 1, FromForest = 2, FromSatellite = 3, FromProcedural = 4;
        public const float TreeLineMetres = 3800f, TreeSlopeLimit = 35f, GrassSlopeLimit = 30f, SnowLineMetres = 4300f, VillageSlopeLimit = 6f;
        public const int HardTreeCeiling = 2000000;

        // --- Hash randomness (stable across machines) ---
        public static uint Hash(int a, int b, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u ^ (uint)a * 0x85EBCA6Bu;
                h = (h << 13) | (h >> 19);
                h ^= (uint)b * 0xC2B2AE35u;
                h *= 0x27D4EB2Fu;
                h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12;
                return h;
            }
        }

        public static float Rand(int a, int b, int seed) => (Hash(a, b, seed) & 0xFFFFFF) / (float)0x1000000;

        static Vector2 HeightRange(Vector2 r) => r.x <= r.y ? r : new Vector2(r.y, r.x);

        static TreeSpot Spot(float x, float z, int a, int b, int seed, Vector2 heights, byte source)
        {
            var hr = HeightRange(heights);
            return new TreeSpot { x = x, z = z, height = Mathf.Lerp(hr.x, hr.y, Rand(a, b, seed ^ 0x11)), yaw = Rand(a, b, seed ^ 0x22) * Mathf.PI * 2f, width = 0.85f + 0.3f * Rand(a, b, seed ^ 0x33), source = source };
        }

        // Keeps an individual tree with the fall-off probability (deterministic per tree).
        static bool Keep(float x, float z, Vector2 centre, float startMetres, int seed)
        {
            float f = LayerFalloff.Of(Mathf.Sqrt((x - centre.x) * (x - centre.x) + (z - centre.y) * (z - centre.y)), startMetres);
            return f >= 1f || Rand(Mathf.RoundToInt(x * 10f), Mathf.RoundToInt(z * 10f), seed ^ 0x77) < f;
        }

        // Mapped trees: each one is kept (thinned only far from the post).
        public static void MappedTrees(IList<LayerPoint> points, int seed, Vector2 heights, Vector2 falloffCentre, float falloffStart, Func<float, float, bool> blocked, List<TreeSpot> into)
        {
            for (int i = 0; i < points.Count; i++)
            {
                var p = points[i];
                if (blocked != null && blocked(p.x, p.z)) continue;
                if (!Keep(p.x, p.z, falloffCentre, falloffStart, seed)) continue;
                into.Add(Spot(p.x, p.z, i, 1, seed, heights, FromNode));
            }
        }

        // A tree every ~6 m along each mapped tree row.
        public static void TreeRows(IList<LayerLine> rows, int seed, Vector2 heights, Vector2 falloffCentre, float falloffStart, Func<float, float, bool> blocked, List<TreeSpot> into, float spacing = 6f)
        {
            for (int r = 0; r < rows.Count; r++)
            {
                var p = rows[r].p;
                int n = p.Length / 2, k = 0;
                for (int i = 0; i + 1 < n; i++)
                {
                    float ax = p[i * 2], az = p[i * 2 + 1], bx = p[i * 2 + 2], bz = p[i * 2 + 3], len = Mathf.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                    int steps = Mathf.Max(1, Mathf.FloorToInt(len / spacing));
                    for (int s = 0; s < steps; s++, k++)
                    {
                        float t = (s + 0.5f) / steps, x = Mathf.Lerp(ax, bx, t), z = Mathf.Lerp(az, bz, t);
                        if (blocked != null && blocked(x, z)) continue;
                        if (!Keep(x, z, falloffCentre, falloffStart, seed)) continue;
                        into.Add(Spot(x, z, r, k + 100003, seed, heights, FromRow));
                    }
                }
            }
        }

        // Trees inside a forest polygon at `perHectare` (thinned by distance from the post). Every tree lies inside the
        // polygon and outside its holes: each candidate is tested against the polygon.
        public static void TreesInPolygon(LayerPoly poly, PolyIndex index, int polyId, float perHectare, int seed, Vector2 heights, Vector2 falloffCentre, float falloffStart,
            Func<float, float, bool> blocked, Func<float, float, bool> allowed, List<TreeSpot> into, byte source = FromForest)
        {
            if (perHectare <= 0f) return;
            const float Block = 128f;
            int bx0 = Mathf.FloorToInt(index.MinX / Block), bx1 = Mathf.FloorToInt(index.MaxX / Block), bz0 = Mathf.FloorToInt(index.MinZ / Block), bz1 = Mathf.FloorToInt(index.MaxZ / Block);
            for (int bz = bz0; bz <= bz1; bz++)
            for (int bx = bx0; bx <= bx1; bx++)
            {
                float cx = (bx + 0.5f) * Block, cz = (bz + 0.5f) * Block;
                float f = LayerFalloff.Of(Mathf.Sqrt((cx - falloffCentre.x) * (cx - falloffCentre.x) + (cz - falloffCentre.y) * (cz - falloffCentre.y)), falloffStart);
                float density = perHectare * f;
                float s = Mathf.Sqrt(10000f / Mathf.Max(0.01f, density));          // metres between trees on a jittered grid
                int steps = Mathf.Max(1, Mathf.CeilToInt(Block / s));
                float step = Block / steps;
                for (int gz = 0; gz < steps; gz++)
                for (int gx = 0; gx < steps; gx++)
                {
                    int ia = bx * 977 + gx, ib = bz * 983 + gz;
                    float x = bx * Block + (gx + Rand(ia, ib, seed ^ polyId)) * step, z = bz * Block + (gz + Rand(ia, ib, seed ^ polyId ^ 0x5A5A)) * step;
                    if (!index.Contains(x, z)) continue;
                    if (blocked != null && blocked(x, z)) continue;
                    if (allowed != null && !allowed(x, z)) continue;
                    if (into.Count >= HardTreeCeiling) return;
                    into.Add(Spot(x, z, ia, ib, seed, heights, source));
                }
            }
        }

        // Trees for the cells of a cover grid that the satellite says are woodland: `perHectare` x estimate share, per cell.
        public static void TreesFromSatellite(CoverGrid grid, float perHectare, int seed, Vector2 heights, Vector2 falloffCentre, float falloffStart, Func<float, float, bool> inside, Func<float, float, bool> blocked, List<TreeSpot> into)
        {
            for (int iz = 0; iz < grid.n; iz++)
            for (int ix = 0; ix < grid.n; ix++)
            {
                if (grid.cls[iz * grid.n + ix] != CoverGrid.SatTree) continue;
                var c = grid.CellCentre(ix, iz);
                if (inside != null && !inside(c.x, c.y)) continue;
                float f = LayerFalloff.Of(Vector2.Distance(c, falloffCentre), falloffStart);
                float expected = perHectare * 0.8f * f * grid.cell * grid.cell / 10000f;
                int count = Mathf.FloorToInt(expected);
                if (Rand(ix, iz, seed ^ 0x3C3C) < expected - count) count++;
                for (int k = 0; k < count; k++)
                {
                    float x = c.x + (Rand(ix * 31 + k, iz, seed ^ 0x41) - 0.5f) * grid.cell, z = c.y + (Rand(ix, iz * 31 + k, seed ^ 0x42) - 0.5f) * grid.cell;
                    if (blocked != null && blocked(x, z)) continue;
                    if (into.Count >= HardTreeCeiling) return;
                    into.Add(Spot(x, z, ix * 131 + k, iz, seed, heights, FromSatellite));
                }
            }
        }

        // --- Procedural ---

        // How much of the climate's trees a biome keeps (the high desert has trees only along the valleys, which a
        // plain noise mask cannot find, so it simply has few).
        public static float ClimateTreeFactor(SurfaceBiome biome)
        {
            switch (biome)
            {
                case SurfaceBiome.HighDesert: return 0.25f;
                case SurfaceBiome.Desert: return 0.1f;
                case SurfaceBiome.Plains: return 0.7f;
                default: return 1f;
            }
        }

        public static float ClimateGrassFactor(SurfaceBiome biome)
        {
            switch (biome)
            {
                case SurfaceBiome.HighDesert: return 0.25f;
                case SurfaceBiome.Desert: return 0.12f;
                default: return 1f;
            }
        }

        // 0..1 woodland mask: clumps from low-frequency noise plus narrow tree lines.
        public static float WoodMask(float x, float z, int seed)
        {
            double n = Noise.Fbm(x / 480.0, z / 480.0, seed ^ 0x7001, 3, 2.0, 0.5);
            float clump = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.22f, (float)n));
            double line = 1.0 - Math.Abs(Noise.Gradient(x / 230.0, z / 230.0, seed ^ 0x7002));
            float tl = line > 0.972 ? 0.75f : 0f;
            return Mathf.Max(clump, tl);
        }

        // 0..1 for the ground: no trees on steep slopes or above the tree line.
        public static float TreeGroundFactor(float slopeDegrees, float elevation)
        {
            float slope = 1f - Mathf.InverseLerp(TreeSlopeLimit - 7f, TreeSlopeLimit, slopeDegrees);
            float high = 1f - Mathf.InverseLerp(TreeLineMetres - 400f, TreeLineMetres, elevation);
            return Mathf.Clamp01(slope * high);
        }

        // Procedural trees over the ground in clumps and lines. `outsideSquare` > 0 skips the play square (for the surround).
        public static void ProceduralTrees(LayerGround ground, int seed, float perHectare, SurfaceBiome biome, Vector2 heights, Vector2 falloffCentre, float falloffStart,
            float skipHalfSquare, Func<float, float, bool> blocked, List<TreeSpot> into)
        {
            if (perHectare <= 0f) return;
            float half = ground.Half, side = half * 2f;
            float cell = Mathf.Max(40f, side / 700f);
            int n = Mathf.CeilToInt(side / cell);
            float climate = ClimateTreeFactor(biome);
            for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                float cx = -half + (ix + 0.5f) * cell, cz = -half + (iz + 0.5f) * cell;
                if (skipHalfSquare > 0f && Mathf.Abs(cx) < skipHalfSquare - cell * 0.5f && Mathf.Abs(cz) < skipHalfSquare - cell * 0.5f) continue;
                float f = LayerFalloff.Of(Mathf.Sqrt((cx - falloffCentre.x) * (cx - falloffCentre.x) + (cz - falloffCentre.y) * (cz - falloffCentre.y)), falloffStart);
                float expected = perHectare * climate * f * cell * cell / 10000f;
                if (expected <= 0f) continue;
                // Cheap skip: the mask is smooth over 100s of metres, so a cell far from any wood costs one noise sample.
                if (WoodMask(cx, cz, seed) <= 0f && WoodMask(cx - cell, cz, seed) <= 0f && WoodMask(cx + cell, cz, seed) <= 0f && WoodMask(cx, cz - cell, seed) <= 0f && WoodMask(cx, cz + cell, seed) <= 0f) continue;
                int count = Mathf.CeilToInt(expected);
                for (int k = 0; k < count; k++)
                {
                    float x = cx + (Rand(ix * 53 + k, iz, seed ^ 0x61) - 0.5f) * cell, z = cz + (Rand(ix, iz * 53 + k, seed ^ 0x62) - 0.5f) * cell;
                    if (!ground.Contains(x, z)) continue;
                    if (skipHalfSquare > 0f && Mathf.Abs(x) < skipHalfSquare && Mathf.Abs(z) < skipHalfSquare) continue;
                    float p = WoodMask(x, z, seed) * TreeGroundFactor(ground.SlopeDegrees(x, z), ground.Elevation(x, z));
                    if (Rand(ix * 53 + k, iz * 59 + k, seed ^ 0x63) >= p * expected / count) continue;
                    if (blocked != null && blocked(x, z)) continue;
                    if (into.Count >= HardTreeCeiling) return;
                    into.Add(Spot(x, z, ix * 53 + k, iz, seed, heights, FromProcedural));
                }
            }
        }

        // Grass cover 0..1 for the ground and the seed: noise clumps, none on steep ground or near the snow line.
        public static float GrassCover(LayerGround ground, float x, float z, int seed, SurfaceBiome biome)
        {
            float slope = ground.SlopeDegrees(x, z);
            if (slope >= GrassSlopeLimit) return 0f;
            float elevation = ground.Elevation(x, z);
            if (elevation >= SnowLineMetres) return 0f;
            float s = 1f - Mathf.InverseLerp(GrassSlopeLimit - 8f, GrassSlopeLimit, slope);
            float snow = 1f - Mathf.InverseLerp(SnowLineMetres - 600f, SnowLineMetres, elevation);
            double n = Noise.Fbm(x / 70.0, z / 70.0, seed ^ 0x9001, 3, 2.0, 0.5);
            float clump = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(-0.4f, 0.4f, (float)n));
            return Mathf.Clamp01(s * snow * clump * ClimateGrassFactor(biome));
        }

        // Villages on flat ground: a few centres, houses on a jittered grid round each. Keeps clear of `blocked` and of steep ground.
        public static void ProceduralBuildings(LayerGround ground, int seed, float villagesPerKm2, Func<float, float, bool> blocked, List<BuildingSpot> into)
        {
            if (villagesPerKm2 <= 0f) return;
            float half = ground.Half;
            const float Cell = 1000f;
            int n = Mathf.CeilToInt(half * 2f / Cell);
            for (int cz = 0; cz < n; cz++)
            for (int cx = 0; cx < n; cx++)
            {
                float x0 = -half + cx * Cell, z0 = -half + cz * Cell;
                float w = Mathf.Min(Cell, half - x0), d = Mathf.Min(Cell, half - z0);
                float expected = villagesPerKm2 * (w * d) / 1000000f;
                int villages = Mathf.FloorToInt(expected);
                if (Rand(cx, cz, seed ^ 0x8101) < expected - villages) villages++;
                for (int v = 0; v < villages; v++)
                {
                    float radius = Mathf.Lerp(70f, 170f, Rand(cx * 17 + v, cz, seed ^ 0x8102));
                    // Try a few places in the cell until one is flat and free.
                    for (int attempt = 0; attempt < 12; attempt++)
                    {
                        float vx = x0 + (0.1f + 0.8f * Rand(cx * 17 + v, cz * 13 + attempt, seed ^ 0x8103)) * w, vz = z0 + (0.1f + 0.8f * Rand(cx * 13 + attempt, cz * 17 + v, seed ^ 0x8104)) * d;
                        if (!ground.Contains(vx, vz) || ground.SlopeDegrees(vx, vz) > VillageSlopeLimit) continue;
                        if (blocked != null && blocked(vx, vz)) continue;
                        VillageAt(ground, vx, vz, radius, cx * 31 + v, cz, seed, blocked, into);
                        break;
                    }
                }
            }
        }

        static void VillageAt(LayerGround ground, float vx, float vz, float radius, int a, int b, int seed, Func<float, float, bool> blocked, List<BuildingSpot> into)
        {
            float yaw = Rand(a, b, seed ^ 0x8201) * Mathf.PI;
            float spacing = Mathf.Lerp(22f, 30f, Rand(a, b, seed ^ 0x8202));
            int steps = Mathf.CeilToInt(radius / spacing);
            float cosY = Mathf.Cos(yaw), sinY = Mathf.Sin(yaw);
            bool temple = false;
            for (int gz = -steps; gz <= steps; gz++)
            for (int gx = -steps; gx <= steps; gx++)
            {
                int ia = a * 4099 + gx, ib = b * 4111 + gz;
                float lx = (gx + (Rand(ia, ib, seed ^ 0x8203) - 0.5f) * 0.55f) * spacing, lz = (gz + (Rand(ia, ib, seed ^ 0x8204) - 0.5f) * 0.55f) * spacing;
                float dist = Mathf.Sqrt(lx * lx + lz * lz);
                if (dist > radius * (0.65f + 0.35f * Rand(ia, ib, seed ^ 0x8205))) continue;       // ragged edge
                if (Rand(ia, ib, seed ^ 0x8206) > 0.78f) continue;
                float x = vx + lx * cosY - lz * sinY, z = vz + lx * sinY + lz * cosY;
                if (!ground.Contains(x, z) || ground.SlopeDegrees(x, z) > VillageSlopeLimit) continue;
                if (blocked != null && blocked(x, z)) continue;
                float roll = Rand(ia, ib, seed ^ 0x8207);
                var spot = new BuildingSpot { x = x, z = z, hash = (int)Hash(ia, ib, seed), yaw = yaw + (Rand(ia, ib, seed ^ 0x8208) < 0.5f ? 0f : Mathf.PI * 0.5f) + (Rand(ia, ib, seed ^ 0x8209) - 0.5f) * 0.25f };
                if (!temple && dist < radius * 0.35f && roll < 0.2f) { temple = true; spot.type = BuildingType.Religious; spot.width = 9f; spot.depth = 9f; spot.height = 9f; }
                else if (roll < 0.14f) { spot.type = BuildingType.Apartments; spot.width = Mathf.Lerp(12f, 16f, Rand(ia, ib, seed ^ 0x820A)); spot.depth = Mathf.Lerp(10f, 13f, Rand(ia, ib, seed ^ 0x820B)); spot.height = Mathf.Lerp(9f, 13f, Rand(ia, ib, seed ^ 0x820C)); }
                else if (roll < 0.22f) { spot.type = BuildingType.Shed; spot.width = Mathf.Lerp(4f, 6f, Rand(ia, ib, seed ^ 0x820A)); spot.depth = Mathf.Lerp(3.5f, 5f, Rand(ia, ib, seed ^ 0x820B)); spot.height = 2.8f; }
                else { spot.type = BuildingType.House; spot.width = Mathf.Lerp(7f, 11f, Rand(ia, ib, seed ^ 0x820A)); spot.depth = Mathf.Lerp(5.5f, 9f, Rand(ia, ib, seed ^ 0x820B)); spot.height = Mathf.Lerp(3f, 5.5f, Rand(ia, ib, seed ^ 0x820C)); }
                into.Add(spot);
            }
        }
    }
}
