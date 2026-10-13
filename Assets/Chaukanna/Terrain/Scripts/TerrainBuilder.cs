using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
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
        public static TerrainBuildResult Build(TerrainGenConfig cfg, ElevationGrid dem, GeneratedTerrainRoot existingRoot = null, Func<string, TerrainData, TerrainData> makeData = null)
        {
            var errors = cfg.Validate();
            if (errors.Count > 0) throw new DemException(string.Join("\n", errors));

            var result = new TerrainBuildResult();
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
            var layers = new List<TerrainLayer>();
            foreach (var rule in cfg.surfaceRules) if (rule.layer != null && !layers.Contains(rule.layer)) layers.Add(rule.layer);

            var grid = new Terrain[tiles, tiles];
            float[,] heights = null;
            for (int tz = 0; tz < tiles; tz++)
            for (int tx = 0; tx < tiles; tx++)
            {
                string name = $"Tile_{tx}_{tz}";
                var data = new TerrainData { name = name };
                data.heightmapResolution = res;                       // set before size: changing it resets the size
                data.size = new Vector3(tileSize, range, tileSize);
                // Save as an asset first (editor): surface weights painted before that are not kept in the saved file.
                if (makeData != null) data = makeData(name, data);
                heights = TileHeights(field, tx, tz, res, min, range, heights);
                data.SetHeights(0, 0, heights);                       // one call per tile
                if (layers.Count > 0) Paint(cfg, field, data, layers, tx, tz, res, min);

                var go = Terrain.CreateTerrainGameObject(data);
                go.name = name;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(-half + tx * tileSize, yOffset, -half + tz * tileSize);
                var terrain = go.GetComponent<Terrain>();
                terrain.allowAutoConnect = true;
                grid[tx, tz] = terrain;
                result.tiles.Add(terrain);
            }
            for (int tz = 0; tz < tiles; tz++)
            for (int tx = 0; tx < tiles; tx++)
                grid[tx, tz].SetNeighbors(tx > 0 ? grid[tx - 1, tz] : null, tz < tiles - 1 ? grid[tx, tz + 1] : null, tx < tiles - 1 ? grid[tx + 1, tz] : null, tz > 0 ? grid[tx, tz - 1] : null);

            result.report = Report(cfg, dem, field, min, range, yOffset, result.warnings);
            existingRoot.report = result.report;
            return result;
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        // Surface weights from elevation (metres above the area's lowest point) and slope (degrees).
        static void Paint(TerrainGenConfig cfg, Heightfield field, TerrainData data, List<TerrainLayer> layers, int tileX, int tileZ, int res, float min)
        {
            data.terrainLayers = layers.ToArray();
            int a = Mathf.Min(res - 1, 1024);
            data.alphamapResolution = a;
            var map = new float[a, a, layers.Count];
            int ox = tileX * (res - 1), oz = tileZ * (res - 1);
            float toSample = (res - 1) / (float)a;
            for (int z = 0; z < a; z++)
            for (int x = 0; x < a; x++)
            {
                int sx = Mathf.Min(field.resolution - 1, ox + Mathf.RoundToInt((x + 0.5f) * toSample));
                int sz = Mathf.Min(field.resolution - 1, oz + Mathf.RoundToInt((z + 0.5f) * toSample));
                float height = field.At(sx, sz) - field.min;
                float slope = HeightfieldGenerator.SlopeDegrees(field, sx, sz);
                float total = 0f;
                foreach (var rule in cfg.surfaceRules)
                {
                    if (rule.layer == null) continue;
                    float w = RuleWeight(rule, height, slope);
                    map[z, x, layers.IndexOf(rule.layer)] += w;
                    total += w;
                }
                if (total <= 0.0001f) map[z, x, 0] = 1f;       // nothing matched: fall back to the first layer
                else for (int l = 0; l < layers.Count; l++) map[z, x, l] /= total;
            }
            data.SetAlphamaps(0, 0, map);
        }

        // 1 inside the rule's height and slope band, fading to 0 over its blend distances.
        public static float RuleWeight(SurfaceRule r, float height, float slopeDegrees)
        {
            float h = Band(height, r.minHeight, r.maxHeight, Mathf.Max(0.01f, r.heightBlend));
            float s = Band(slopeDegrees, r.minSlope, r.maxSlope, Mathf.Max(0.01f, r.slopeBlend));
            return h * s * Mathf.Max(0f, r.weight);
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
            var terrains = Terrain.activeTerrains;
            for (int i = 0; i < terrains.Length; i++)
            {
                var t = terrains[i];
                if (t == null || t.terrainData == null) continue;
                Vector3 local = position - t.transform.position;
                Vector3 size = t.terrainData.size;
                if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
                return t.SampleHeight(position) + t.transform.position.y;
            }
            return float.NaN;
        }
    }
}
