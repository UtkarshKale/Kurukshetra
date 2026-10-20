using System;
using UnityEngine;

namespace Kurukshetra.TerrainGen
{
    // The elevation field for a whole terrain area, in metres, with what was done to make it.
    public class Heightfield
    {
        public int resolution;          // samples per side
        public float sizeMetres;        // width and length covered
        public float[] metres;          // row-major; row 0 = southern edge, column 0 = western edge (Unity's +Z north, +X east)
        public float min, max;
        public float centreElevation;   // ground height at the middle, before any vertical offset
        public int outsideSourceSamples;        // samples that fell outside the DEM and were clamped to its edge
        public double sourceSpacingMetres;      // DEM sample spacing, 0 for pure procedural
        public float Spacing => sizeMetres / (resolution - 1);
        public float At(int x, int z) { return metres[z * resolution + x]; }
    }

    // Layer A + Layer B: finalElevation = sourceElevation + proceduralDisplacement, evaluated in world metres.
    // Pure functions of the config, the (never modified) source grid and the position, so results are reproducible
    // and any sub-rectangle of the area gives the same numbers as the whole.
    public static class HeightfieldGenerator
    {
        // Elevation at one point. east/north are metres from the centre of the area.
        public static float Elevation(TerrainGenConfig cfg, ElevationGrid dem, double centreLat, double centreLon, double east, double north, float sourceMean, float maskValue, ref int outside)
        {
            switch (cfg.mode)
            {
                case TerrainMode.RealWorldAccurate:
                    // The DEM and nothing else: no displacement, warp, power shaping or terracing.
                    return Source(dem, centreLat, centreLon, east, north, ref outside);

                case TerrainMode.RealWorldSubtleDetail:
                    return Source(dem, centreLat, centreLon, east, north, ref outside)
                        + Displacement(cfg, east, north) * cfg.displacementMultiplier;

                case TerrainMode.RealWorldMajorVariations:
                {
                    float original = Source(dem, centreLat, centreLon, east, north, ref outside);
                    float blend = cfg.modificationBlend * maskValue;
                    if (blend <= 0f) return original;
                    Warp(cfg, ref east, ref north);
                    int ignored = 0;
                    float reshaped = sourceMean + (Source(dem, centreLat, centreLon, east, north, ref ignored) - sourceMean) * cfg.sourceReliefScale;
                    float modified = Terrace(cfg, reshaped + Displacement(cfg, east, north) * cfg.displacementMultiplier);
                    return Mathf.Lerp(original, modified, blend);
                }

                case TerrainMode.CustomHybrid:
                {
                    // sourceReliefScale is the source's weight; displacementMultiplier is the procedural weight.
                    Warp(cfg, ref east, ref north);
                    float source = sourceMean + (Source(dem, centreLat, centreLon, east, north, ref outside) - sourceMean) * cfg.sourceReliefScale;
                    return Terrace(cfg, source + Displacement(cfg, east, north) * cfg.displacementMultiplier);
                }

                default:
                    Warp(cfg, ref east, ref north);
                    return Terrace(cfg, cfg.baseElevation + Displacement(cfg, east, north) * cfg.displacementMultiplier);
            }
        }

        static float Source(ElevationGrid dem, double centreLat, double centreLon, double east, double north, ref int outside)
        {
            GeoMath.LocalToGeo(centreLat, centreLon, east, north, out double lat, out double lon);
            if (!dem.Contains(lat, lon)) outside++;
            return dem.Sample(lat, lon);
        }

        // Sum of the enabled noise layers at a point, in metres.
        public static float Displacement(TerrainGenConfig cfg, double east, double north)
        {
            double total = 0.0;
            for (int i = 0; i < cfg.layers.Count; i++)
            {
                var l = cfg.layers[i];
                if (!l.enabled || l.amplitudeMetres == 0f || l.featureSizeMetres <= 0f) continue;
                total += Layer(l, cfg.seed, east, north);
            }
            return (float)total;
        }

        // One layer: sample > normalise to 0..1 > power > curve > centre > amplitude.
        public static double Layer(NoiseLayer l, int seed, double east, double north)
        {
            double x = east / l.featureSizeMetres, y = north / l.featureSizeMetres;
            int s = seed + l.seedOffset * 7919;
            double v;
            switch (l.kind)
            {
                case NoiseKind.Ridged:
                    v = Noise.Ridged(x, y, s, l.octaves, l.lacunarity, l.persistence, l.ridgeExponent);
                    break;
                case NoiseKind.Cellular:
                    Noise.Cellular(x, y, s, out double f1, out double _);
                    v = f1 > 1.0 ? 1.0 : f1;
                    break;
                default:
                    v = (Noise.Fbm(x, y, s, l.octaves, l.lacunarity, l.persistence) + 1.0) * 0.5;
                    break;
            }
            if (v < 0.0) v = 0.0; else if (v > 1.0) v = 1.0;
            if (l.power != 1f) v = Math.Pow(v, l.power);
            if (l.useCurve && l.curve != null) v = Mathf.Clamp01(l.curve.Evaluate((float)v));
            if (l.centred) v -= 0.5;
            return v * l.amplitudeMetres;
        }

        // Domain warp: move the sampling position by two independent noise fields.
        public static void Warp(TerrainGenConfig cfg, ref double east, ref double north)
        {
            var w = cfg.warp;
            if (!w.enabled || w.blend <= 0f || w.strengthMetres == 0f || w.featureSizeMetres <= 0f) return;
            double x = east / w.featureSizeMetres, y = north / w.featureSizeMetres;
            int s = cfg.seed + w.seedOffset;
            double dx = Noise.Fbm(x, y, s, 3, 2.0, 0.5), dy = Noise.Fbm(x + 37.2, y - 11.8, s + 4447, 3, 2.0, 0.5);
            east += dx * w.strengthMetres * w.blend;
            north += dy * w.strengthMetres * w.blend;
        }

        // Steps the height. Within each interval the rise is squeezed towards the top, leaving a flat tread below it.
        public static float Terrace(TerrainGenConfig cfg, float height)
        {
            var t = cfg.terrace;
            if (!t.enabled || t.blend <= 0f || t.intervalMetres <= 0f) return height;
            float steps = height / t.intervalMetres;
            float floor = Mathf.Floor(steps), frac = steps - floor;
            float riser = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.75f, 1f, frac));
            return Mathf.Lerp(height, (floor + riser) * t.intervalMetres, t.blend);
        }

        // The whole area at the config's global resolution.
        public static Heightfield Generate(TerrainGenConfig cfg, ElevationGrid dem)
        {
            return GenerateRegion(cfg, dem, -cfg.sizeMetres * 0.5, -cfg.sizeMetres * 0.5, cfg.sizeMetres, cfg.GlobalResolution, true);
        }

        // Any square part of the area. westEdge/southEdge are metres from the centre. Because every sample depends only on
        // its own world position, two regions that share an edge get identical values along it.
        public static Heightfield GenerateRegion(TerrainGenConfig cfg, ElevationGrid dem, double westEdge, double southEdge, float size, int resolution, bool applyPostPad)
        {
            if (cfg.UsesDem && dem == null) throw new DemException("This mode needs elevation data, but none is loaded.");
            double centreLat = 0.0, centreLon = 0.0;
            float sourceMean = 0f;
            if (cfg.UsesDem)
            {
                centreLat = cfg.useFileCentre ? dem.CentreLatitude : cfg.centreLatitude;
                centreLon = cfg.useFileCentre ? dem.CentreLongitude : cfg.centreLongitude;
                sourceMean = dem.meanElevation;
            }

            var field = new Heightfield { resolution = resolution, sizeMetres = size, metres = new float[resolution * resolution], sourceSpacingMetres = cfg.UsesDem ? Math.Max(dem.CellMetresX, dem.CellMetresY) : 0.0 };
            double step = size / (double)(resolution - 1);
            var mask = cfg.mode == TerrainMode.RealWorldMajorVariations ? cfg.modificationMask : null;
            int outside = 0;
            for (int z = 0; z < resolution; z++)
            {
                double north = southEdge + z * step;
                for (int x = 0; x < resolution; x++)
                {
                    double east = westEdge + x * step;
                    float maskValue = 1f;
                    if (mask != null)
                        maskValue = mask.GetPixelBilinear((float)((east + cfg.sizeMetres * 0.5) / cfg.sizeMetres), (float)((north + cfg.sizeMetres * 0.5) / cfg.sizeMetres)).grayscale;
                    field.metres[z * resolution + x] = Elevation(cfg, dem, centreLat, centreLon, east, north, sourceMean, maskValue, ref outside);
                }
            }
            field.outsideSourceSamples = outside;

            // Ground level at the centre of the whole area (the observation post), from the same function.
            int unused = 0;
            field.centreElevation = Elevation(cfg, dem, centreLat, centreLon, 0.0, 0.0, sourceMean, 1f, ref unused);

            // Optional flat pads for the observation posts. They alter the terrain, so Real World Accurate never gets them.
            if (applyPostPad && cfg.flattenPostPad && cfg.mode != TerrainMode.RealWorldAccurate && cfg.postPadRadius > 0f)
            {
                float inner = cfg.postPadRadius, outer = cfg.postPadRadius + Mathf.Max(1f, cfg.postPadFalloff);
                int pads = cfg.padCentres != null && cfg.padCentres.Count > 0 ? cfg.padCentres.Count : 1;
                for (int p = 0; p < pads; p++)
                {
                    Vector2 c = cfg.padCentres != null && cfg.padCentres.Count > 0 ? cfg.padCentres[p] : Vector2.zero;
                    int none = 0;
                    float level = Elevation(cfg, dem, centreLat, centreLon, c.x, c.y, sourceMean, 1f, ref none);
                    for (int z = 0; z < resolution; z++)
                    {
                        double north = southEdge + z * step;
                        for (int x = 0; x < resolution; x++)
                        {
                            double east = westEdge + x * step;
                            double dx = east - c.x, dz = north - c.y;
                            float d = (float)Math.Sqrt(dx * dx + dz * dz);
                            if (d >= outer) continue;
                            float keep = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, d));
                            int i = z * resolution + x;
                            field.metres[i] = Mathf.Lerp(level, field.metres[i], keep);
                        }
                    }
                }
            }
            field.min = float.MaxValue;
            field.max = float.MinValue;
            foreach (float v in field.metres)
            {
                if (v < field.min) field.min = v;
                if (v > field.max) field.max = v;
            }
            return field;
        }

        // Shaded-relief picture of a heightfield, north up, lighter = higher. Reuses `into` when it is the right size.
        public static Texture2D PreviewTexture(Heightfield f, Texture2D into = null)
        {
            int res = f.resolution;
            if (into == null || into.width != res || into.height != res) into = new Texture2D(res, res, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[res * res];
            float range = Mathf.Max(1f, f.max - f.min);
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float h = (f.At(x, z) - f.min) / range;
                float sx = f.At(Math.Min(res - 1, x + 1), z) - f.At(Math.Max(0, x - 1), z), sz = f.At(x, Math.Min(res - 1, z + 1)) - f.At(x, Math.Max(0, z - 1));
                float shade = Mathf.Clamp01(0.75f + (sz - sx) / (4f * f.Spacing));      // lit from the north-west
                float v = Mathf.Clamp01((0.25f + 0.75f * h) * shade);
                // A green-to-brown-to-white tint by height, so it reads as terrain on the trainer's map.
                Color low = new Color(0.35f, 0.5f, 0.3f), mid = new Color(0.55f, 0.45f, 0.32f);
                Color c = Color.Lerp(Color.Lerp(low, mid, Mathf.Clamp01(h * 2f)), Color.white, Mathf.Clamp01(h * 2f - 1f)) * (0.35f + 0.9f * v);
                px[z * res + x] = new Color32((byte)(Mathf.Clamp01(c.r) * 255f), (byte)(Mathf.Clamp01(c.g) * 255f), (byte)(Mathf.Clamp01(c.b) * 255f), 255);
            }
            into.SetPixels32(px);
            into.Apply();
            return into;
        }

        // Slope in degrees by central differences over the physical sample spacing (one-sided at the edges).
        public static float SlopeDegrees(Heightfield f, int x, int z)
        {
            int n = f.resolution;
            int x0 = Math.Max(0, x - 1), x1 = Math.Min(n - 1, x + 1), z0 = Math.Max(0, z - 1), z1 = Math.Min(n - 1, z + 1);
            float dx = (f.At(x1, z) - f.At(x0, z)) / ((x1 - x0) * f.Spacing);
            float dz = (f.At(x, z1) - f.At(x, z0)) / ((z1 - z0) * f.Spacing);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
        }
    }
}
