using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kurukshetra.TerrainGen
{
    // Marks the coarse surround built around a real-world play area (see TerrainBuilder.BuildSurround).
    public class SurroundTerrain : MonoBehaviour { }

    // Marks the root object this tool made, so regenerating only ever replaces its own output.
    public class GeneratedTerrainRoot : MonoBehaviour
    {
        public TerrainGenConfig config;
        [TextArea(3, 10)] public string report = "";
    }

    // What a build did, for the editor window and the README-style report.
    public class TerrainBuildResult
    {
        public GameObject root;
        public List<Terrain> tiles = new List<Terrain>();
        public Heightfield field;
        public string report = "";
        public List<string> warnings = new List<string>();
    }

    // Layer C and Unity integration: turns a heightfield into Unity Terrain tiles and paints the surface rules.
    public static class TerrainBuilder
    {
        // Normalised heights for one tile, in Unity's [z, x] order. `range` is the vertical span the tile is sized to.
        public static float[,] TileHeights(Heightfield field, int tileX, int tileZ, int tileResolution, float min, float range, float[,] buffer = null)
        {
            if (buffer == null || buffer.GetLength(0) != tileResolution || buffer.GetLength(1) != tileResolution) buffer = new float[tileResolution, tileResolution];
            int ox = tileX * (tileResolution - 1), oz = tileZ * (tileResolution - 1);
            for (int z = 0; z < tileResolution; z++)
            for (int x = 0; x < tileResolution; x++)
                buffer[z, x] = Mathf.Clamp01((field.At(ox + x, oz + z) - min) / range);
            return buffer;
        }

        // Builds or rebuilds the tiles under `existingRoot` (or a new root). makeData lets the editor save each
        // TerrainData as an asset; at runtime pass null and the data stays in memory.
        // imagery (optional) colours the terrain; saveAsset lets the editor keep the picture and its layers as assets.
        public static TerrainBuildResult Build(TerrainGenConfig cfg, ElevationGrid dem, GeneratedTerrainRoot existingRoot = null, Func<string, TerrainData, TerrainData> makeData = null, Imagery imagery = null, Action<string, UnityEngine.Object> saveAsset = null)
        {
            var result = new TerrainBuildResult();
            var steps = BuildInSteps(result, cfg, dem, existingRoot, makeData, imagery, saveAsset);
            while (steps.MoveNext()) { }
            return result;
        }

        // The same build, one step per frame when run as a coroutine: the heightfield first, then one tile at a time.
        // progress(fraction 0-1, what is being done) is called before each step, so a loading screen can show it.
        public static System.Collections.IEnumerator BuildInSteps(TerrainBuildResult result, TerrainGenConfig cfg, ElevationGrid dem, GeneratedTerrainRoot existingRoot = null,
            Func<string, TerrainData, TerrainData> makeData = null, Imagery imagery = null, Action<string, UnityEngine.Object> saveAsset = null, Action<float, string> progress = null)
        {
            var errors = cfg.Validate();
            if (errors.Count > 0) throw new DemException(string.Join("\n", errors));

            progress?.Invoke(0f, "Shaping the ground");
            yield return null;
            var field = result.field = HeightfieldGenerator.Generate(cfg, dem);

            // Vertical range: the real span of the data plus a little headroom, so nothing is clipped and nothing is rescaled.
            float min = Mathf.Floor(field.min - 1f), range = Mathf.Max(10f, Mathf.Ceil(field.max + 1f) - min);
            float yOffset = cfg.groundPostAtZero ? min - field.centreElevation : min;

            GameObject root;
            if (existingRoot != null)
            {
                root = existingRoot.gameObject;
                for (int i = root.transform.childCount - 1; i >= 0; i--) Destroy(root.transform.GetChild(i).gameObject);
            }
            else
            {
                root = new GameObject("GeneratedTerrain");
                existingRoot = root.AddComponent<GeneratedTerrainRoot>();
            }
            existingRoot.config = cfg;
            result.root = root;

            int tiles = Mathf.Max(1, cfg.tilesPerSide), res = cfg.heightmapResolution;
            float tileSize = cfg.TileSize, half = cfg.sizeMetres * 0.5f;
            // Surface: from the kit and biome when there is a kit, otherwise the config's own rules.
            var rules = cfg.surfaceKit != null ? SurfaceBiomes.Rules(cfg.surfaceKit, SurfaceBiomes.Resolve(cfg.biome, field)) : cfg.surfaceRules.FindAll(x => x.layer != null);
            var analysis = rules.Count > 0 ? TerrainAnalysis.Compute(field) : null;
            if (cfg.surfaceKit != null) result.warnings.Add($"Surface: {SurfaceBiomes.Resolve(cfg.biome, field)} materials.");

            // Imagery: where the picture sits, and how much of the terrain it covers.
            bool useImagery = imagery != null && imagery.texture != null && cfg.imageryBlend > 0f;
            Rect imageryExtent = default;
            if (useImagery)
            {
                imageryExtent = ImageryReader.Extent(cfg, dem, imagery);
                float coverage = ImageryReader.Coverage(cfg, imageryExtent);
                if (coverage <= 0f) { useImagery = false; result.warnings.Add("The imagery does not overlap the terrain area at all, so it was not used. Check its bounds against the elevation file."); }
                else
                {
                    if (coverage < 0.99f) result.warnings.Add($"The imagery covers only {coverage * 100f:F0} % of the terrain; the rest uses the surface rules.");
                    if (!imagery.georeferenced || !cfg.UsesDem) result.warnings.Add("The imagery has no usable position here, so it is stretched over the whole terrain.");
                    if (saveAsset != null) saveAsset("Imagery", imagery.texture);
                }
            }

            var grid = new Terrain[tiles, tiles];
            float[,] heights = null;
            for (int tz = 0; tz < tiles; tz++)
            for (int tx = 0; tx < tiles; tx++)
            {
                progress?.Invoke(0.35f + 0.6f * (tz * tiles + tx) / (tiles * tiles), tiles > 1 ? $"Painting tile {tz * tiles + tx + 1} of {tiles * tiles}" : "Painting the ground");
                yield return null;
                string name = $"Tile_{tx}_{tz}";
                var data = new TerrainData { name = name };
                data.heightmapResolution = res;                       // set before size: changing it resets the size
                data.size = new Vector3(tileSize, range, tileSize);
                // Save as an asset first (editor): surface weights painted before that are not kept in the saved file.
                if (makeData != null) data = makeData(name, data);
                heights = TileHeights(field, tx, tz, res, min, range, heights);
                data.SetHeights(0, 0, heights);                       // one call per tile
                TerrainLayer imageryLayer = null;
                if (useImagery)
                {
                    // One layer per tile, because each tile needs its own offset into the picture.
                    // Unity maps texture u = (local x + tileOffset.x) / tileSize.x, so the offset is the tile's west / south edge measured from the picture's.
                    imageryLayer = new TerrainLayer { name = name + "_Imagery", diffuseTexture = imagery.texture, tileSize = new Vector2(imageryExtent.width, imageryExtent.height), tileOffset = new Vector2(-half + tx * tileSize - imageryExtent.xMin, -half + tz * tileSize - imageryExtent.yMin), smoothness = 0f, metallic = 0f };
                    if (saveAsset != null) saveAsset(imageryLayer.name, imageryLayer);
                }
                if (rules.Count > 0 || imageryLayer != null)
                    Paint(cfg, field, analysis, rules, data, imageryLayer, tx, tz, res, imageryExtent, new Vector2(-half + tx * tileSize, -half + tz * tileSize), tileSize);
                // A tile with no layer at all (no ground materials, no picture: e.g. a half-downloaded area) must not render as the error
                // colour: give it one neutral earth layer (runtime only; a tile saved as an editor asset keeps whatever it has).
                if (makeData == null && (data.terrainLayers == null || data.terrainLayers.Length == 0) && Kurukshetra.RuntimeMaterials.NeutralGround != null)
                {
                    data.terrainLayers = new[] { Kurukshetra.RuntimeMaterials.NeutralGround };
                    if (!result.warnings.Contains(NoLayersWarning)) result.warnings.Add(NoLayersWarning);
                }

                var go = Terrain.CreateTerrainGameObject(data);
                go.name = name;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(-half + tx * tileSize, yOffset, -half + tz * tileSize);
                var terrain = go.GetComponent<Terrain>();
                terrain.allowAutoConnect = true;
                // A material that is always in the build (the default terrain shader is left out of a built game: pink).
                var groundMaterial = Kurukshetra.RuntimeMaterials.Terrain;      // never null in practice: it falls back (and logs an error) when the Resources material is missing
                if (groundMaterial != null) terrain.materialTemplate = groundMaterial;
                else Debug.LogError("[Terrain] No terrain material at all: this tile will use the pipeline's default terrain shader, which a build leaves out (pink).");
                grid[tx, tz] = terrain;
                result.tiles.Add(terrain);
            }
            for (int tz = 0; tz < tiles; tz++)
            for (int tx = 0; tx < tiles; tx++)
                grid[tx, tz].SetNeighbors(tx > 0 ? grid[tx - 1, tz] : null, tz < tiles - 1 ? grid[tx, tz + 1] : null, tx < tiles - 1 ? grid[tx + 1, tz] : null, tz > 0 ? grid[tx, tz - 1] : null);

            result.report = Report(cfg, dem, field, min, range, yOffset, result.warnings);
            existingRoot.report = result.report;
            progress?.Invoke(1f, "Ground ready");
        }

        const string NoLayersWarning = "No ground materials and no satellite picture for this terrain: it is drawn in one neutral earth colour.";

        static void Destroy(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        // At most this many surface materials per tile: with the satellite picture that is 3 + 1 = 4, one rendering pass.
        public const int MaxDetailLayersWithImagery = 3, MaxDetailLayers = 4;

        // Surface weights from the rules (height, slope, wetness, shape, facing, noise), then the satellite picture on top.
        // Each tile keeps only the materials that matter most on it, so it renders in one pass.
        static void Paint(TerrainGenConfig cfg, Heightfield field, TerrainAnalysis analysis, List<SurfaceRule> rules, TerrainData data, TerrainLayer imageryLayer,
            int tileX, int tileZ, int res, Rect imageryExtent, Vector2 tileOrigin, float tileSize)
        {
            int a = Mathf.Min(res - 1, 1024);
            int ox = tileX * (res - 1), oz = tileZ * (res - 1);
            float toSample = (res - 1) / (float)a;

            // Distinct materials named by the rules, and every rule's weight at every alphamap sample.
            var materials = new List<TerrainLayer>();
            var ruleMaterial = new int[rules.Count];
            for (int i = 0; i < rules.Count; i++)
            {
                int m = materials.IndexOf(rules[i].layer);
                if (m < 0) { materials.Add(rules[i].layer); m = materials.Count - 1; }
                ruleMaterial[i] = m;
            }
            var weights = new float[a * a * Math.Max(1, materials.Count)];
            var totals = new double[materials.Count];
            for (int z = 0; z < a; z++)
            for (int x = 0; x < a; x++)
            {
                int sx = Mathf.Min(field.resolution - 1, ox + Mathf.RoundToInt((x + 0.5f) * toSample));
                int sz = Mathf.Min(field.resolution - 1, oz + Mathf.RoundToInt((z + 0.5f) * toSample));
                float absolute = field.At(sx, sz), height = absolute - field.min;
                float slope = HeightfieldGenerator.SlopeDegrees(field, sx, sz);
                float wet = 0.5f, curv = 0f, north = 0f;
                analysis?.At(sx, sz, out wet, out curv, out north);
                float east = tileOrigin.x + (x + 0.5f) / a * tileSize, northM = tileOrigin.y + (z + 0.5f) / a * tileSize;
                for (int i = 0; i < rules.Count; i++)
                {
                    var r = rules[i];
                    float noise = r.breakup > 0f ? (float)(0.5 + 0.5 * Noise.Fbm(east / Math.Max(1f, r.breakupScaleMetres), northM / Math.Max(1f, r.breakupScaleMetres), cfg.seed + 7919 * (i + 1), 3, 2.0, 0.5)) : 0.5f;
                    float w = RuleWeight(r, height, absolute, slope, wet, curv, north, noise);
                    weights[(z * a + x) * materials.Count + ruleMaterial[i]] += w;
                    totals[ruleMaterial[i]] += w;
                }
            }

            // Keep the strongest materials for this tile.
            int keep = Mathf.Min(materials.Count, imageryLayer != null ? MaxDetailLayersWithImagery : MaxDetailLayers);
            var chosen = new List<int>();
            for (int m = 0; m < materials.Count; m++) chosen.Add(m);
            chosen.Sort((p, q) => totals[q].CompareTo(totals[p]));
            if (chosen.Count > keep) chosen.RemoveRange(keep, chosen.Count - keep);

            var tileLayers = new List<TerrainLayer>();
            foreach (int m in chosen) tileLayers.Add(materials[m]);
            int imageryIndex = -1;
            if (imageryLayer != null) { tileLayers.Add(imageryLayer); imageryIndex = tileLayers.Count - 1; }
            if (tileLayers.Count == 0) return;
            data.terrainLayers = tileLayers.ToArray();
            data.alphamapResolution = a;
            var map = new float[a, a, tileLayers.Count];
            int detail = chosen.Count;
            for (int z = 0; z < a; z++)
            for (int x = 0; x < a; x++)
            {
                float total = 0f;
                for (int c = 0; c < detail; c++) { float w = weights[(z * a + x) * materials.Count + chosen[c]]; map[z, x, c] = w; total += w; }
                if (total <= 0.0001f) { if (detail > 0) map[z, x, 0] = 1f; }       // nothing matched: the tile's main material
                else for (int c = 0; c < detail; c++) map[z, x, c] /= total;

                if (imageryIndex >= 0)
                {
                    // The picture takes its share only where it actually covers the ground; the detail materials keep the rest,
                    // so close up there is texture and from a distance the real colours.
                    var world = new Vector2(tileOrigin.x + (x + 0.5f) / a * tileSize, tileOrigin.y + (z + 0.5f) / a * tileSize);
                    float share = imageryExtent.Contains(world) || detail == 0 ? (detail == 0 ? 1f : cfg.imageryBlend) : 0f;
                    for (int c = 0; c < detail; c++) map[z, x, c] *= 1f - share;
                    map[z, x, imageryIndex] = share;
                }
            }
            data.SetAlphamaps(0, 0, map);
        }

        // 1 inside the rule's height and slope band, fading to 0 over its blend distances.
        public static float RuleWeight(SurfaceRule r, float height, float slopeDegrees)
        {
            return RuleWeight(r, height, height, slopeDegrees, Mathf.Clamp(0.5f, r.minWetness, r.maxWetness), Mathf.Clamp(0f, r.minCurvature, r.maxCurvature), 0f, 0.5f);
        }

        // The full rule: every condition is a soft band, multiplied together.
        public static float RuleWeight(SurfaceRule r, float heightAboveLowest, float elevation, float slopeDegrees, float wetness, float curvature, float northness, float noise01)
        {
            float h = Band(r.absoluteHeight ? elevation : heightAboveLowest, r.minHeight, r.maxHeight, Mathf.Max(0.01f, r.heightBlend));
            float s = Band(slopeDegrees, r.minSlope, r.maxSlope, Mathf.Max(0.01f, r.slopeBlend));
            float w = Band(wetness, r.minWetness, r.maxWetness, Mathf.Max(0.001f, r.wetnessBlend));
            float c = Band(curvature, r.minCurvature, r.maxCurvature, Mathf.Max(0.0001f, r.curvatureBlend));
            float facing = Mathf.Max(0f, 1f + r.northBias * northness);
            float noise = Mathf.Lerp(1f, Mathf.Clamp01(noise01) * 2f, Mathf.Clamp01(r.breakup));
            return h * s * w * c * facing * noise * Mathf.Max(0f, r.weight);
        }
        static float Band(float v, float lo, float hi, float blend)
        {
            return Mathf.Clamp01((v - lo) / blend + 1f) * Mathf.Clamp01((hi - v) / blend + 1f);
        }

        static string Report(TerrainGenConfig cfg, ElevationGrid dem, Heightfield f, float min, float range, float yOffset, List<string> warnings)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Mode: {cfg.mode}   Seed: {cfg.seed}");
            sb.AppendLine($"Area: {cfg.sizeMetres:F0} m x {cfg.sizeMetres:F0} m in {cfg.tilesPerSide} x {cfg.tilesPerSide} tile(s), {cfg.heightmapResolution} samples per tile edge");
            sb.AppendLine($"Output spacing: {f.Spacing:F2} m per sample");
            sb.AppendLine($"Elevation: {f.min:F1} m to {f.max:F1} m. Terrain height range {range:F0} m from {min:F0} m (no vertical exaggeration).");
            sb.AppendLine($"Ground at the centre: {f.centreElevation:F1} m, placed at world height {(cfg.groundPostAtZero ? 0f : f.centreElevation):F1}.");
            if (cfg.UsesDem && dem != null)
            {
                sb.AppendLine("Source: " + dem.Describe().Replace("\n", "\n  "));
                if (f.sourceSpacingMetres < f.Spacing * 0.95)
                    warnings.Add($"Detail reduced: the source has a sample every {f.sourceSpacingMetres:F1} m but the terrain only every {f.Spacing:F1} m. Raise the heightmap resolution, add tiles or shrink the area to keep it.");
                else if (f.sourceSpacingMetres > f.Spacing * 1.05)
                    warnings.Add($"The terrain samples every {f.Spacing:F1} m but the source only every {f.sourceSpacingMetres:F1} m, so the extra samples are interpolated; the effective resolution is {f.sourceSpacingMetres:F1} m.");
                if (f.outsideSourceSamples > 0)
                    warnings.Add($"{f.outsideSourceSamples} of {f.metres.Length} samples lie outside the elevation file and repeat its edge values. Use a smaller area or a file that covers it.");
            }
            if (cfg.mode == TerrainMode.RealWorldAccurate && cfg.flattenPostPad) sb.AppendLine("Post pad is ignored in Real World Accurate.");
            foreach (string w in warnings) sb.AppendLine("WARNING: " + w);
            return sb.ToString();
        }

        // Ground height in world space under a point, from whichever generated or other Terrain is there. NaN if none.
        public static float GroundHeight(Vector3 position)
        {
            // The play area wins over the surround (which lies just under it); the surround answers only outside it.
            var terrains = Terrain.activeTerrains;
            float fallback = float.NaN;
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i];
                if (t == null || t.terrainData == null) continue;
                Vector3 local = position - t.transform.position;
                Vector3 size = t.terrainData.size;
                if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
                float h = t.SampleHeight(position) + t.transform.position.y;
                if (t.GetComponentInParent<SurroundTerrain>() != null) { if (float.IsNaN(fallback)) fallback = h; continue; }
                return h;
            }
            return fallback;
        }

        // Builds the coarse surround: a bigger area of the same place around the play area, so the horizon is real
        // ground instead of empty sky. It lies a few metres under the play area, has no collider and is drawn coarser.
        public static TerrainBuildResult BuildSurround(TerrainGenConfig cfg, ElevationGrid dem, Imagery imagery, float playAreaCentreElevation)
        {
            var c = UnityEngine.Object.Instantiate(cfg);
            c.name = cfg.name + " (surround)";
            c.mode = TerrainMode.RealWorldAccurate;
            c.useFileCentre = true;
            c.sizeMetres = Mathf.Floor((float)Math.Min(dem.WidthMetres, dem.HeightMetres));
            c.tilesPerSide = 1;
            c.heightmapResolution = 513;
            c.flattenPostPad = false;
            c.groundPostAtZero = false;
            var result = Build(c, dem, null, null, imagery);
            result.root.name = "GeneratedTerrain (surround)";
            result.root.AddComponent<SurroundTerrain>();
            result.root.transform.position = new Vector3(0f, -playAreaCentreElevation - SurroundSink, 0f);
            foreach (var t in result.tiles)
            {
                t.heightmapPixelError = 10f;
                t.basemapDistance = 20000f;
                var col = t.GetComponent<TerrainCollider>();
                if (col != null) UnityEngine.Object.Destroy(col);
            }
            UnityEngine.Object.Destroy(c);
            return result;
        }

        public const float SurroundSink = 4f;
    }
}
