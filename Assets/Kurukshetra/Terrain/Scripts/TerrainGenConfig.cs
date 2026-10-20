using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kurukshetra.TerrainGen
{
    public enum TerrainMode
    {
        RealWorldAccurate,          // A: the DEM, untouched
        RealWorldSubtleDetail,      // B: the DEM plus low-amplitude detail
        RealWorldMajorVariations,   // C: the DEM reshaped, blended with the original
        PureProcedural,             // D: no DEM
        CustomHybrid,               // E: independent weights for source and displacement
    }

    public enum NoiseKind
    {
        Gradient,   // smooth hills (fBm)
        Ridged,     // sharp crests
        Cellular,   // Voronoi basins and plates
    }

    public enum ProceduralPreset
    {
        RollingHills,
        PlainsWithPeaks,
        MountainRanges,
        RidgedRugged,
        Voronoi,
        Mesas,
        Terraced,
        WarpedAlien,
        Custom,
    }

    // One noise stage. Every size is in metres.
    // Pipeline: sample the noise > normalise to 0..1 > optional power > optional curve > multiply by amplitude.
    // Normalisation: Gradient (n + 1) / 2; Ridged already 0..1; Cellular min(f1, 1) (0 at feature points).
    // "Centred" subtracts 0.5 first, so the layer adds and removes height equally (right for displacement).
    [Serializable]
    public class NoiseLayer
    {
        public bool enabled = true;
        public string label = "Layer";
        public NoiseKind kind = NoiseKind.Gradient;
        [Tooltip("Height range this layer can contribute, metres.")]
        public float amplitudeMetres = 100f;
        [Tooltip("Width of the largest features, metres.")]
        public float featureSizeMetres = 1500f;
        [Range(1, 10)] public int octaves = 5;
        [Tooltip("Each octave is this many times finer.")]
        [Range(1.5f, 3f)] public float lacunarity = 2f;
        [Tooltip("Each octave is this many times weaker. Lower = smoother.")]
        [Range(0.2f, 0.7f)] public float persistence = 0.5f;
        [Tooltip("Added to the terrain seed, so layers differ from each other.")]
        public int seedOffset;
        [Tooltip("Ridged only: higher = sharper, narrower crests.")]
        [Range(0.5f, 4f)] public float ridgeExponent = 2f;
        [Tooltip("Applied to the 0..1 value. Above 1 flattens the lowlands and keeps the peaks; 1 = off.")]
        [Range(0.25f, 6f)] public float power = 1f;
        public bool useCurve;
        [Tooltip("Remaps the 0..1 value when Use Curve is on.")]
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [Tooltip("On: the layer is centred on zero (adds and removes height). Off: it only adds height.")]
        public bool centred;
    }

    // Offsets where the terrain is sampled, which bends ridges and valleys. A separate stage from the height layers.
    [Serializable]
    public class WarpSettings
    {
        public bool enabled;
        [Tooltip("How far a point can be pushed sideways, metres.")]
        public float strengthMetres = 300f;
        [Tooltip("Size of the bends, metres.")]
        public float featureSizeMetres = 1200f;
        public int seedOffset = 9001;
        [Range(0f, 1f)] public float blend = 1f;
    }

    [Serializable]
    public class TerraceSettings
    {
        public bool enabled;
        [Tooltip("Height of each step, metres.")]
        public float intervalMetres = 40f;
        [Tooltip("0 = smooth slope, 1 = flat steps with sharp risers.")]
        [Range(0f, 1f)] public float blend = 0.8f;
    }

    // Where a surface layer appears. Elevation is in metres above the terrain's lowest point; slope in degrees.
    [Serializable]
    public class SurfaceRule
    {
        public string label = "Surface";
        public TerrainLayer layer;
        public float minHeight = 0f;
        public float maxHeight = 100000f;
        [Range(0f, 90f)] public float minSlope = 0f;
        [Range(0f, 90f)] public float maxSlope = 90f;
        [Tooltip("Softness of the height limits, metres.")]
        public float heightBlend = 15f;
        [Tooltip("Softness of the slope limits, degrees.")]
        public float slopeBlend = 6f;
        [Tooltip("Relative strength where several rules overlap.")]
        public float weight = 1f;

        [Header("Extra conditions (neutral by default)")]
        [Tooltip("On: the height limits are metres above sea level (e.g. a snow line). Off: above the area's lowest point.")]
        public bool absoluteHeight;
        [Tooltip("Where water would gather, 0 = dry ridge or open slope, 1 = valley floor or stream bed (from flow accumulation).")]
        public float minWetness = 0f, maxWetness = 1f;
        public float wetnessBlend = 0.15f;
        [Tooltip("Shape: negative = ridge or crest, positive = gully or hollow.")]
        public float minCurvature = -100f, maxCurvature = 100f;
        public float curvatureBlend = 0.02f;
        [Tooltip("Positive favours north-facing slopes (snow lies longer), negative favours south-facing.")]
        [Range(-1f, 1f)] public float northBias;
        [Tooltip("Breaks up hard edges and repetition with noise, 0 = none.")]
        [Range(0f, 1f)] public float breakup;
        public float breakupScaleMetres = 40f;
    }

    // Everything needed to rebuild a terrain. Nothing essential lives in the editor window.
    [CreateAssetMenu(menuName = "Kurukshetra/Terrain Gen Config", fileName = "TerrainGenConfig")]
    public class TerrainGenConfig : ScriptableObject
    {
        [Header("Mode")]
        public TerrainMode mode = TerrainMode.PureProcedural;
        public int seed = 1001;

        [Header("Layer A: source elevation (real-world modes)")]
        [Tooltip("Elevation file: .hgt (SRTM), .asc (Esri ASCII grid) or .raw with a .json sidecar. Absolute, or relative to the project folder.")]
        public string demPath = "";
        [Tooltip("Centre of the area to build, decimal degrees.")]
        public double centreLatitude;
        public double centreLongitude;
        [Tooltip("Tick to centre on the file's own middle instead of the coordinates above.")]
        public bool useFileCentre = true;
        [Tooltip("Where the data came from. Filled in on import; keep it with the terrain.")]
        [TextArea(2, 5)] public string provenance = "";
        [TextArea(1, 3)] public string licenceAndAttribution = "";

        [Header("Terrain")]
        [Tooltip("Width and length of the whole area, metres.")]
        public float sizeMetres = 5000f;
        [Tooltip("Height samples along one tile edge. Must be 2^n + 1.")]
        public int heightmapResolution = 513;
        [Tooltip("Tiles along each side. 1 = a single terrain.")]
        [Range(1, 8)] public int tilesPerSide = 1;
        [Tooltip("Lowers or raises the whole terrain so the ground under the observation post (the centre) is at world height 0. Shape is unchanged.")]
        public bool groundPostAtZero = true;
        [Tooltip("Flatten a pad around the centre for the observation post. This changes the terrain, so it is ignored in Real World Accurate.")]
        public bool flattenPostPad = true;
        public float postPadRadius = 40f;
        public float postPadFalloff = 160f;
        // Set at runtime by the trainer's setup: pad centres in metres east/north of the area centre, one per player post.
        // Empty = one pad at the centre.
        [System.NonSerialized] public List<Vector2> padCentres = new List<Vector2>();

        [Header("Layer B: procedural (base in Pure Procedural; displacement in the other modes)")]
        [Tooltip("Pure Procedural only: height before any noise, metres.")]
        public float baseElevation = 200f;
        public List<NoiseLayer> layers = new List<NoiseLayer>();
        public WarpSettings warp = new WarpSettings();
        public TerraceSettings terrace = new TerraceSettings();
        [Tooltip("Scales every displacement layer. 0 = no procedural change at all.")]
        public float displacementMultiplier = 1f;

        [Header("Real-world reshaping (Major Variations and Custom Hybrid)")]
        [Tooltip("Stretches the real relief about its mean. 1 = as measured, 1.5 = higher peaks and deeper valleys, 0 = flat.")]
        public float sourceReliefScale = 1f;
        [Tooltip("Major Variations: 0 = untouched DEM, 1 = fully modified.")]
        [Range(0f, 1f)] public float modificationBlend = 1f;
        [Tooltip("Optional. White = modify, black = leave the DEM alone. Must be readable. Stretched over the whole area, north up.")]
        public Texture2D modificationMask;

        [Header("Layer C: surface")]
        [Tooltip("Ground materials (rock, scree, soil, grass, snow, sand). When set, the surface is painted from the biome below and the rules list is not used.")]
        public TerrainSurfaceKit surfaceKit;
        [Tooltip("How the kit's materials are placed. Auto picks from the elevation (high desert above ~2500 m, plains when low and flat).")]
        public SurfaceBiome biome = SurfaceBiome.Auto;
        public List<SurfaceRule> surfaceRules = new List<SurfaceRule>();
        [Tooltip("Optional true-colour picture of the ground (PNG or JPG, north up) with a .json giving its bounds in degrees. It only colours the terrain; it never changes heights.")]
        public string imageryPath = "";
        [Tooltip("0 = surface rules only, 1 = the picture only (where it covers the terrain).")]
        [Range(0f, 1f)] public float imageryBlend = 1f;

        [Header("Trees, grass and buildings")]
        public TerrainLayerSettings terrainLayers = new TerrainLayerSettings();

        public bool UsesDem => mode != TerrainMode.PureProcedural;
        public float TileSize => sizeMetres / Mathf.Max(1, tilesPerSide);
        // Samples across the whole area; neighbouring tiles share their edge row.
        public int GlobalResolution => Mathf.Max(1, tilesPerSide) * (heightmapResolution - 1) + 1;
        public float SampleSpacing => sizeMetres / (GlobalResolution - 1);

        public static bool IsValidResolution(int r)
        {
            int n = r - 1;
            return r >= 33 && r <= 4097 && (n & (n - 1)) == 0;
        }

        // Problems that stop generation. Empty = good to go.
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (!IsValidResolution(heightmapResolution)) errors.Add($"Heightmap resolution {heightmapResolution} is not 2^n + 1 between 33 and 4097 (for example 257, 513, 1025).");
            if (sizeMetres < 100f) errors.Add("Area size must be at least 100 m.");
            if (tilesPerSide < 1) errors.Add("Tiles per side must be at least 1.");
            if (UsesDem && string.IsNullOrWhiteSpace(demPath)) errors.Add("This mode needs an elevation file. Pick one, or switch to Pure Procedural.");
            if (mode == TerrainMode.PureProcedural && layers.TrueForAll(l => !l.enabled)) errors.Add("Pure Procedural needs at least one enabled noise layer. Apply a preset.");
            foreach (var l in layers)
                if (l.enabled && l.featureSizeMetres <= 0f) errors.Add($"Layer '{l.label}': feature size must be above 0 m.");
            if (terrace.enabled && terrace.intervalMetres <= 0f) errors.Add("Terrace interval must be above 0 m.");
            if (modificationMask != null && !modificationMask.isReadable) errors.Add("The modification mask texture must have Read/Write enabled in its import settings.");
            return errors;
        }

        // Replaces the procedural settings with a named landscape. Mode and source are left alone.
        public void ApplyPreset(ProceduralPreset preset)
        {
            layers = new List<NoiseLayer>();
            warp = new WarpSettings();
            terrace = new TerraceSettings();
            displacementMultiplier = 1f;
            switch (preset)
            {
                case ProceduralPreset.RollingHills:
                    baseElevation = 150f;
                    layers.Add(new NoiseLayer { label = "Hills", amplitudeMetres = 90f, featureSizeMetres = 1400f, octaves = 4, persistence = 0.42f });
                    break;
                case ProceduralPreset.PlainsWithPeaks:
                    baseElevation = 120f;
                    layers.Add(new NoiseLayer { label = "Plain", amplitudeMetres = 25f, featureSizeMetres = 1800f, octaves = 3, persistence = 0.4f });
                    layers.Add(new NoiseLayer { label = "Scattered peaks", amplitudeMetres = 520f, featureSizeMetres = 2200f, octaves = 5, power = 4.5f, seedOffset = 17 });
                    break;
                case ProceduralPreset.MountainRanges:
                    baseElevation = 300f;
                    layers.Add(new NoiseLayer { label = "Massif", amplitudeMetres = 700f, featureSizeMetres = 3200f, octaves = 6, power = 1.6f });
                    layers.Add(new NoiseLayer { label = "Ridges", kind = NoiseKind.Ridged, amplitudeMetres = 320f, featureSizeMetres = 1500f, octaves = 5, ridgeExponent = 2f, seedOffset = 31 });
                    warp = new WarpSettings { enabled = true, strengthMetres = 260f, featureSizeMetres = 1800f };
                    break;
                case ProceduralPreset.RidgedRugged:
                    baseElevation = 250f;
                    layers.Add(new NoiseLayer { label = "Ridges", kind = NoiseKind.Ridged, amplitudeMetres = 650f, featureSizeMetres = 1700f, octaves = 7, ridgeExponent = 2.4f, persistence = 0.55f });
                    layers.Add(new NoiseLayer { label = "Roughness", amplitudeMetres = 45f, featureSizeMetres = 180f, octaves = 4, centred = true, seedOffset = 53 });
                    warp = new WarpSettings { enabled = true, strengthMetres = 180f, featureSizeMetres = 900f };
                    break;
                case ProceduralPreset.Voronoi:
                    baseElevation = 150f;
                    layers.Add(new NoiseLayer { label = "Cells", kind = NoiseKind.Cellular, amplitudeMetres = 320f, featureSizeMetres = 900f, octaves = 1 });
                    layers.Add(new NoiseLayer { label = "Undulation", amplitudeMetres = 50f, featureSizeMetres = 1300f, octaves = 3, centred = true, seedOffset = 71 });
                    break;
                case ProceduralPreset.Mesas:
                    baseElevation = 180f;
                    layers.Add(new NoiseLayer { label = "Plateaus", amplitudeMetres = 360f, featureSizeMetres = 1900f, octaves = 4, power = 1.8f });
                    terrace = new TerraceSettings { enabled = true, intervalMetres = 120f, blend = 0.92f };
                    warp = new WarpSettings { enabled = true, strengthMetres = 150f, featureSizeMetres = 1100f };
                    break;
                case ProceduralPreset.Terraced:
                    baseElevation = 160f;
                    layers.Add(new NoiseLayer { label = "Hills", amplitudeMetres = 300f, featureSizeMetres = 1600f, octaves = 5 });
                    terrace = new TerraceSettings { enabled = true, intervalMetres = 30f, blend = 0.75f };
                    break;
                case ProceduralPreset.WarpedAlien:
                    baseElevation = 200f;
                    layers.Add(new NoiseLayer { label = "Spines", kind = NoiseKind.Ridged, amplitudeMetres = 520f, featureSizeMetres = 1300f, octaves = 6, ridgeExponent = 3f });
                    layers.Add(new NoiseLayer { label = "Pits", kind = NoiseKind.Cellular, amplitudeMetres = 260f, featureSizeMetres = 700f, octaves = 1, seedOffset = 97 });
                    warp = new WarpSettings { enabled = true, strengthMetres = 900f, featureSizeMetres = 1000f };
                    break;
                default:
                    baseElevation = 150f;
                    layers.Add(new NoiseLayer { label = "Layer 1", amplitudeMetres = 150f, featureSizeMetres = 1500f });
                    break;
            }
        }

        // Low-amplitude roughness for Real World + Subtle Detail.
        public void ApplySubtleDetailDefaults()
        {
            layers = new List<NoiseLayer> { new NoiseLayer { label = "Detail", amplitudeMetres = 6f, featureSizeMetres = 120f, octaves = 4, centred = true } };
            warp = new WarpSettings();
            terrace = new TerraceSettings();
            displacementMultiplier = 1f;
            sourceReliefScale = 1f;
        }

        // Stronger reshaping for Real World + Major Variations and Custom Hybrid.
        public void ApplyMajorVariationDefaults()
        {
            layers = new List<NoiseLayer>
            {
                new NoiseLayer { label = "Broad hills", amplitudeMetres = 180f, featureSizeMetres = 2200f, octaves = 4, centred = true },
                new NoiseLayer { label = "Ridges", kind = NoiseKind.Ridged, amplitudeMetres = 120f, featureSizeMetres = 900f, octaves = 5, centred = true, seedOffset = 31 },
            };
            warp = new WarpSettings();
            terrace = new TerraceSettings();
            displacementMultiplier = 1f;
            sourceReliefScale = 1.3f;
            modificationBlend = 1f;
        }
    }
}
