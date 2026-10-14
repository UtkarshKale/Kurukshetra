using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // The ground materials a terrain can be painted with. Built by Chaukanna > Terrain > Build Detail Materials.
    [CreateAssetMenu(menuName = "Chaukanna/Terrain Surface Kit", fileName = "SurfaceKit")]
    public class TerrainSurfaceKit : ScriptableObject
    {
        public TerrainLayer rock, scree, soil, grass, snow, sand;
    }

    public enum SurfaceBiome
    {
        Auto,
        HighDesert,     // Ladakh / Kargil: bare rock and scree, snow up high and on north faces, green only along streams
        Plains,         // Jammu / Punjab: grass and fields, dry soil, little rock
        Desert,         // Rajasthan: sand and dry soil, rock outcrops
        Temperate,      // forest-edge hills: grass, soil, rock on steep ground
    }

    // Placement rules for each biome. Heights marked absolute are metres above sea level.
    public static class SurfaceBiomes
    {
        public static SurfaceBiome Resolve(SurfaceBiome biome, Heightfield f)
        {
            if (biome != SurfaceBiome.Auto) return biome;
            if (f.max > 2500f) return SurfaceBiome.HighDesert;
            if (f.max - f.min < 150f && f.max < 800f) return SurfaceBiome.Plains;
            return SurfaceBiome.Temperate;
        }

        public static List<SurfaceRule> Rules(TerrainSurfaceKit kit, SurfaceBiome biome)
        {
            var r = new List<SurfaceRule>();
            switch (biome)
            {
                case SurfaceBiome.HighDesert:
                    Add(r, "Scree", kit.scree, w: 1f, breakup: 0.5f);                                                                  // the default: loose stone everywhere
                    Add(r, "Rock", kit.rock, minSlope: 30f, slopeBlend: 8f, w: 1.6f, maxCurv: 0.02f, breakup: 0.35f);                   // steep faces and crests
                    Add(r, "Rock (ridges)", kit.rock, maxCurv: -0.04f, curvBlend: 0.03f, w: 1.2f, breakup: 0.4f);
                    Add(r, "Dry soil", kit.soil, maxSlope: 14f, slopeBlend: 6f, w: 0.9f, breakup: 0.6f);                                // fans and flats
                    Add(r, "Valley green", kit.grass, maxSlope: 12f, minWet: 0.55f, wetBlend: 0.2f, w: 2.2f, breakup: 0.5f, absolute: true, maxH: 4200f);
                    Add(r, "Snow", kit.snow, absolute: true, minH: 4700f, hBlend: 250f, maxSlope: 42f, w: 3f, north: 0.6f, breakup: 0.45f);
                    Add(r, "Snow (north faces)", kit.snow, absolute: true, minH: 4300f, hBlend: 200f, maxSlope: 38f, w: 2.2f, north: 1f, breakup: 0.5f);
                    break;
                case SurfaceBiome.Plains:
                    Add(r, "Grass", kit.grass, maxSlope: 18f, w: 1.4f, breakup: 0.55f);
                    Add(r, "Dry soil", kit.soil, w: 0.8f, breakup: 0.6f, breakupScale: 25f);
                    Add(r, "Wet grass", kit.grass, minWet: 0.45f, w: 1.5f);
                    Add(r, "Banks", kit.scree, minSlope: 20f, w: 1.2f, breakup: 0.3f);
                    break;
                case SurfaceBiome.Desert:
                    Add(r, "Sand", kit.sand, maxSlope: 20f, w: 1.5f, breakup: 0.4f);
                    Add(r, "Dry soil", kit.soil, w: 0.7f, breakup: 0.6f);
                    Add(r, "Rock", kit.rock, minSlope: 25f, w: 1.6f, breakup: 0.3f);
                    Add(r, "Scrub", kit.grass, minWet: 0.6f, w: 1.2f, breakup: 0.6f);
                    break;
                default:
                    Add(r, "Grass", kit.grass, maxSlope: 24f, w: 1.4f, breakup: 0.5f);
                    Add(r, "Soil", kit.soil, w: 0.7f, breakup: 0.6f);
                    Add(r, "Rock", kit.rock, minSlope: 32f, w: 1.8f, breakup: 0.35f);
                    Add(r, "Scree", kit.scree, minSlope: 22f, maxSlope: 36f, maxCurv: -0.02f, w: 1f, breakup: 0.5f);
                    Add(r, "Snow", kit.snow, absolute: true, minH: 3600f, hBlend: 200f, maxSlope: 40f, w: 3f, north: 0.6f);
                    break;
            }
            r.RemoveAll(x => x.layer == null);
            return r;
        }

        static void Add(List<SurfaceRule> list, string label, TerrainLayer layer, float w = 1f, float minSlope = 0f, float maxSlope = 90f, float slopeBlend = 6f,
            float minH = -100000f, float maxH = 100000f, float hBlend = 15f, bool absolute = false, float minWet = 0f, float maxWet = 1f, float wetBlend = 0.15f,
            float minCurv = -100f, float maxCurv = 100f, float curvBlend = 0.02f, float north = 0f, float breakup = 0f, float breakupScale = 40f)
        {
            list.Add(new SurfaceRule
            {
                label = label, layer = layer, weight = w, minSlope = minSlope, maxSlope = maxSlope, slopeBlend = slopeBlend,
                minHeight = minH, maxHeight = maxH, heightBlend = hBlend, absoluteHeight = absolute,
                minWetness = minWet, maxWetness = maxWet, wetnessBlend = wetBlend, minCurvature = minCurv, maxCurvature = maxCurv, curvatureBlend = curvBlend,
                northBias = north, breakup = breakup, breakupScaleMetres = breakupScale,
            });
        }
    }

    // What the ground is like at each sample, for placing materials: where water gathers, ridges and hollows, and which
    // way slopes face. Worked out once per build on a grid of at most 513 x 513 over the whole heightfield.
    public class TerrainAnalysis
    {
        public int size;               // samples per side
        public float spacing;          // metres between samples
        public float[] wetness;        // 0 dry .. 1 valley floor
        public float[] curvature;      // negative = ridge, positive = hollow (metres of height per metre, averaged)
        public float[] northness;      // -1 south-facing .. 1 north-facing, 0 on flat ground
        Heightfield field;

        public static TerrainAnalysis Compute(Heightfield f, int maxSize = 513)
        {
            int n = Mathf.Min(f.resolution, maxSize);
            var a = new TerrainAnalysis { size = n, spacing = f.sizeMetres / (n - 1), field = f, wetness = new float[n * n], curvature = new float[n * n], northness = new float[n * n] };
            var h = new float[n * n];
            float step = (f.resolution - 1) / (float)(n - 1);
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                h[z * n + x] = f.At(Mathf.Min(f.resolution - 1, Mathf.RoundToInt(x * step)), Mathf.Min(f.resolution - 1, Mathf.RoundToInt(z * step)));

            // Shape and facing: differences over about 15 m so single-sample noise does not count.
            int k = Mathf.Max(1, Mathf.RoundToInt(15f / a.spacing));
            float d = k * a.spacing;
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float c = h[z * n + x];
                float e = h[z * n + Mathf.Min(n - 1, x + k)], w = h[z * n + Mathf.Max(0, x - k)];
                float no = h[Mathf.Min(n - 1, z + k) * n + x], so = h[Mathf.Max(0, z - k) * n + x];
                a.curvature[z * n + x] = ((e + w + no + so) * 0.25f - c) / d;           // average of neighbours above the centre = hollow
                float gx = (e - w) / (2f * d), gz = (no - so) / (2f * d);
                float g = Mathf.Sqrt(gx * gx + gz * gz);
                // A slope faces north when the ground falls towards the north (+z): height drops as z grows.
                a.northness[z * n + x] = g < 1e-4f ? 0f : Mathf.Clamp(-gz / g, -1f, 1f) * Mathf.Clamp01(g * 4f);
            }

            // Wetness: D8 flow accumulation. Every cell drains to its lowest neighbour; process from the highest down.
            var order = new int[n * n];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            Array.Sort(order, (p, q) => h[q].CompareTo(h[p]));
            var acc = new float[n * n];
            for (int i = 0; i < acc.Length; i++) acc[i] = 1f;
            foreach (int i in order)
            {
                int x = i % n, z = i / n, best = -1;
                float drop = 0f;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int xx = x + dx, zz = z + dz;
                    if (xx < 0 || zz < 0 || xx >= n || zz >= n) continue;
                    float s = (h[i] - h[zz * n + xx]) / ((dx != 0 && dz != 0) ? 1.4142f : 1f);
                    if (s > drop) { drop = s; best = zz * n + xx; }
                }
                if (best >= 0) acc[best] += acc[i];
            }
            float cellArea = a.spacing * a.spacing;
            for (int i = 0; i < acc.Length; i++)
            {
                // 1 hectare of catchment = dry, 1 square kilometre = a stream bed.
                float catchment = acc[i] * cellArea;
                a.wetness[i] = Mathf.Clamp01((Mathf.Log10(Mathf.Max(1f, catchment)) - 4f) / 2f);
            }
            return a;
        }

        // Values at a heightfield sample (field coordinates).
        public void At(int fieldX, int fieldZ, out float wet, out float curv, out float north)
        {
            float s = (size - 1) / (float)(field.resolution - 1);
            int i = Mathf.Min(size - 1, Mathf.RoundToInt(fieldZ * s)) * size + Mathf.Min(size - 1, Mathf.RoundToInt(fieldX * s));
            wet = wetness[i]; curv = curvature[i]; north = northness[i];
        }
    }
}