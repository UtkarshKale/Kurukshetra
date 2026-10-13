using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // Acceptance checks for the terrain generator. Run from the Terrain Generator window, or via the CLI:
    // Chaukanna.TerrainGen.TerrainGenSelfTest.Run()
    // The elevation files used here are SYNTHETIC: they are written by this test from a known formula so the result can
    // be checked exactly. They are not real GIS data and are deleted afterwards.
    public static class TerrainGenSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        // Known "ground truth" for the synthetic DEM: a tilted plane with a broad hill, in metres.
        static float SyntheticElevation(double lat, double lon)
        {
            double x = (lon - 77.0) * 100.0, y = (lat - 28.0) * 100.0;        // 0..4 across the test tile
            return (float)(200.0 + 60.0 * x + 25.0 * y + 150.0 * Math.Exp(-((x - 2.0) * (x - 2.0) + (y - 2.0) * (y - 2.0))));
        }

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            string dir = Path.Combine(Application.temporaryCachePath, "terrain-selftest");
            Directory.CreateDirectory(dir);
            try
            {
                NoiseChecks();
                ProceduralChecks();
                DemChecks(dir);
                InvalidInputChecks(dir);
                UnityTerrainChecks();
            }
            catch (Exception e)
            {
                failures++;
                lines.Add("FAIL  unexpected exception: " + e);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
            string summary = $"[TerrainGenSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static TerrainGenConfig Config(TerrainMode mode, int resolution = 65, float size = 2000f)
        {
            var c = ScriptableObject.CreateInstance<TerrainGenConfig>();
            c.mode = mode;
            c.heightmapResolution = resolution;
            c.sizeMetres = size;
            c.flattenPostPad = false;
            c.ApplyPreset(ProceduralPreset.RollingHills);
            return c;
        }

        static void Free(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o); else UnityEngine.Object.DestroyImmediate(o);
        }

        static float MaxDifference(Heightfield a, Heightfield b)
        {
            float max = 0f;
            for (int i = 0; i < a.metres.Length; i++) max = Mathf.Max(max, Mathf.Abs(a.metres[i] - b.metres[i]));
            return max;
        }

        // --- Noise ---
        static void NoiseChecks()
        {
            bool inRange = true, same = true;
            double biggestStep = 0.0, min = 1.0, max = -1.0;
            for (int i = 0; i < 4000; i++)
            {
                double x = i * 0.0137 + 3.1, y = i * 0.0071 - 8.4;
                double a = Noise.Gradient(x, y, 42);
                if (a < -1.0 || a > 1.0) inRange = false;
                if (a != Noise.Gradient(x, y, 42)) same = false;
                biggestStep = Math.Max(biggestStep, Math.Abs(a - Noise.Gradient(x + 0.01, y, 42)));
                min = Math.Min(min, a); max = Math.Max(max, a);
            }
            Check("gradient noise stays in [-1, 1] and repeats exactly", inRange && same);
            Check($"gradient noise is coherent: a 1 % step moves it by at most {biggestStep:F3}", biggestStep < 0.08);
            Check($"gradient noise uses its range ({min:F2} to {max:F2})", min < -0.5 && max > 0.5);
            Check("a different seed gives different noise", Math.Abs(Noise.Gradient(1.3, 2.7, 1) - Noise.Gradient(1.3, 2.7, 2)) > 1e-6);

            bool fbmRange = true, ridgeRange = true;
            for (int i = 0; i < 2000; i++)
            {
                double f = Noise.Fbm(i * 0.021, i * 0.013, 7, 6, 2.0, 0.5), r = Noise.Ridged(i * 0.021, i * 0.013, 7, 6, 2.0, 0.5, 2.0);
                if (f < -1.0 || f > 1.0) fbmRange = false;
                if (r < 0.0 || r > 1.0) ridgeRange = false;
            }
            Check("fBm stays in [-1, 1] and ridged in [0, 1] with 6 octaves", fbmRange && ridgeRange);
            Noise.Cellular(0.3, 0.7, 5, out double f1, out double f2);
            Check("cellular noise: nearest distance is not beyond the second nearest", f1 >= 0.0 && f1 <= f2);
        }

        // --- Pure procedural: landforms, seed, shaping stages, tiling ---
        static void ProceduralChecks()
        {
            var cfg = Config(TerrainMode.PureProcedural, 129, 4000f);
            var a = HeightfieldGenerator.Generate(cfg, null);
            var b = HeightfieldGenerator.Generate(cfg, null);
            Check("same seed and settings give an identical terrain", MaxDifference(a, b) == 0f);

            // Smooth, recognisable landforms: real relief, but neighbouring samples differ by far less than the relief.
            float relief = a.max - a.min, roughness = 0f;
            for (int z = 0; z < a.resolution; z++)
            for (int x = 1; x < a.resolution; x++)
                roughness = Mathf.Max(roughness, Mathf.Abs(a.At(x, z) - a.At(x - 1, z)));
            Check($"rolling hills: relief {relief:F0} m, steepest step between samples {roughness:F1} m (smooth, not vertex noise)", relief > 30f && roughness < relief * 0.25f);

            cfg.seed = 2002;
            var c = HeightfieldGenerator.Generate(cfg, null);
            Check($"a different seed changes the terrain (up to {MaxDifference(a, c):F0} m)", MaxDifference(a, c) > 5f);
            cfg.seed = 1001;

            // Each shaping stage on its own.
            cfg.ApplyPreset(ProceduralPreset.RollingHills);
            cfg.layers[0].kind = NoiseKind.Ridged;
            Check("ridged noise alone changes the terrain", MaxDifference(a, HeightfieldGenerator.Generate(cfg, null)) > 5f);

            cfg.ApplyPreset(ProceduralPreset.RollingHills);
            cfg.warp.enabled = true;
            var warped = HeightfieldGenerator.Generate(cfg, null);
            Check("domain warp alone changes the terrain but keeps its height range", MaxDifference(a, warped) > 2f && Mathf.Abs((warped.max - warped.min) - relief) < relief * 0.5f);
            cfg.warp.blend = 0f;
            Check("warp blend 0 returns the unwarped terrain exactly", MaxDifference(a, HeightfieldGenerator.Generate(cfg, null)) == 0f);

            cfg.ApplyPreset(ProceduralPreset.RollingHills);
            cfg.terrace.enabled = true; cfg.terrace.intervalMetres = 20f; cfg.terrace.blend = 1f;
            var stepped = HeightfieldGenerator.Generate(cfg, null);
            int onTread = 0;
            foreach (float v in stepped.metres) if (Mathf.Abs(v / 20f - Mathf.Round(v / 20f)) < 0.02f) onTread++;
            Check($"terracing alone puts most samples on 20 m steps ({100f * onTread / stepped.metres.Length:F0} %)", onTread > stepped.metres.Length * 0.6f);

            cfg.ApplyPreset(ProceduralPreset.RollingHills);
            cfg.layers[0].power = 3f;
            var shaped = HeightfieldGenerator.Generate(cfg, null);
            float meanA = 0f, meanS = 0f;
            for (int i = 0; i < a.metres.Length; i++) { meanA += a.metres[i]; meanS += shaped.metres[i]; }
            Check("a power curve above 1 lowers the plains (mean drops) without raising the peaks", meanS < meanA && shaped.max <= a.max + 0.01f);

            // Every preset produces finite terrain with relief.
            bool presetsOk = true;
            foreach (ProceduralPreset preset in Enum.GetValues(typeof(ProceduralPreset)))
            {
                cfg.ApplyPreset(preset);
                var f = HeightfieldGenerator.Generate(cfg, null);
                if (float.IsNaN(f.min) || float.IsNaN(f.max) || f.max - f.min < 5f || cfg.Validate().Count > 0) presetsOk = false;
            }
            Check("all nine presets validate and generate terrain with relief", presetsOk);

            // Tiling: two neighbouring tiles generated separately agree exactly along their shared edge.
            cfg.ApplyPreset(ProceduralPreset.MountainRanges);
            int res = 65;
            var west = HeightfieldGenerator.GenerateRegion(cfg, null, -2000.0, -1000.0, 2000f, res, false);
            var east = HeightfieldGenerator.GenerateRegion(cfg, null, 0.0, -1000.0, 2000f, res, false);
            float seam = 0f;
            for (int z = 0; z < res; z++) seam = Mathf.Max(seam, Mathf.Abs(west.At(res - 1, z) - east.At(0, z)));
            Check($"neighbouring tiles match along their shared edge (largest gap {seam} m)", seam == 0f);

            // The tiled build slices one global field, so its tiles share samples too.
            cfg.tilesPerSide = 2; cfg.heightmapResolution = 33;
            var whole = HeightfieldGenerator.Generate(cfg, null);
            float[,] t0 = TerrainBuilder.TileHeights(whole, 0, 0, 33, whole.min, whole.max - whole.min), t1 = TerrainBuilder.TileHeights(whole, 1, 0, 33, whole.min, whole.max - whole.min);
            bool tileEdge = whole.resolution == 65;
            for (int z = 0; z < 33; z++) if (t0[z, 32] != t1[z, 0]) tileEdge = false;
            Check("a 2 x 2 tile build has 65 global samples and identical tile borders", tileEdge);
            Free(cfg);
        }

        // --- Real-world modes against a synthetic DEM ---
        static void DemChecks(string dir)
        {
            // Synthetic 0.04 x 0.04 degree grid at 28 N 77 E, 161 x 161 samples, with a block of NoData, as an Esri ASCII grid.
            const int n = 161;
            const double cell = 0.04 / n;
            var sb = new StringBuilder();
            sb.Append("ncols ").Append(n).Append("\nnrows ").Append(n).Append("\nxllcorner 77.0\nyllcorner 28.0\ncellsize ").Append(cell.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append("\nNODATA_value -9999\n");
            for (int row = 0; row < n; row++)
            {
                double lat = 28.0 + 0.04 - (row + 0.5) * cell;       // first row is the northern edge
                for (int col = 0; col < n; col++)
                {
                    double lon = 77.0 + (col + 0.5) * cell;
                    bool hole = row >= 40 && row < 46 && col >= 100 && col < 106;
                    sb.Append(hole ? "-9999" : SyntheticElevation(lat, lon).ToString("F3", System.Globalization.CultureInfo.InvariantCulture)).Append(col == n - 1 ? '\n' : ' ');
                }
            }
            string asc = Path.Combine(dir, "synthetic.asc");
            File.WriteAllText(asc, sb.ToString());
            var dem = DemReader.Load(asc);
            Check($"synthetic ASCII grid loads: {dem.width} x {dem.height}, about {dem.CellMetresX:F1} m x {dem.CellMetresY:F1} m per sample", dem.width == n && dem.height == n && dem.noDataCount == 36);

            // Longitude metres shrink with latitude; they are not a constant.
            Check($"a degree of longitude is {GeoMath.MetresPerDegreeLongitude(28.0) / 1000.0:F2} km at 28 N and {GeoMath.MetresPerDegreeLongitude(60.0) / 1000.0:F2} km at 60 N",
                Math.Abs(GeoMath.MetresPerDegreeLongitude(28.0) - 98364.0) < 150.0 && GeoMath.MetresPerDegreeLongitude(60.0) < 56000.0 && Math.Abs(GeoMath.MetresPerDegreeLatitude(28.0) - 110810.0) < 150.0);

            // Orientation: north is up and east is right (the formula rises to the east and to the north).
            Check("orientation: higher to the east and to the north, as in the source",
                dem.Sample(28.02, 77.035) > dem.Sample(28.02, 77.005) && dem.Sample(28.035, 77.005) > dem.Sample(28.005, 77.005));
            // NoData block filled smoothly: no spike or pit, close to the true surface.
            float holeTruth = SyntheticElevation(28.04 - 43.0 * cell, 77.0 + 103.0 * cell), holeValue = dem.Sample(28.04 - 43.0 * cell, 77.0 + 103.0 * cell);
            Check($"NoData is filled from neighbours without a spike (filled {holeValue:F1} m, true {holeTruth:F1} m)", Mathf.Abs(holeValue - holeTruth) < 6f && dem.minElevation > 150f);

            var cfg = Config(TerrainMode.RealWorldAccurate, 129, 3000f);
            cfg.demPath = asc;
            var accurate = HeightfieldGenerator.Generate(cfg, dem);
            // Compare every output sample with the formula the file was made from (away from the filled hole).
            float worst = 0f;
            for (int z = 0; z < accurate.resolution; z++)
            for (int x = 0; x < accurate.resolution; x++)
            {
                double east = -1500.0 + x * accurate.Spacing, north = -1500.0 + z * accurate.Spacing;
                GeoMath.LocalToGeo(dem.CentreLatitude, dem.CentreLongitude, east, north, out double lat, out double lon);
                if (Math.Abs(lat - (28.04 - 43.0 * cell)) < 5 * cell && Math.Abs(lon - (77.0 + 103.0 * cell)) < 5 * cell) continue;
                worst = Mathf.Max(worst, Mathf.Abs(accurate.At(x, z) - SyntheticElevation(lat, lon)));
            }
            Check($"accurate mode follows the source profile (worst error {worst:F2} m over {accurate.max - accurate.min:F0} m of relief)", worst < 1.0f);
            Check("accurate mode keeps the metre scale (no normalising or exaggeration)", accurate.max <= dem.maxElevation + 0.01f && accurate.min >= dem.minElevation - 0.01f && accurate.max - accurate.min > 200f && accurate.outsideSourceSamples == 0);

            // Accurate mode ignores every procedural setting.
            cfg.ApplyPreset(ProceduralPreset.WarpedAlien);
            cfg.terrace.enabled = true; cfg.flattenPostPad = true;
            Check("accurate mode ignores noise, warp, terrace and the post pad", MaxDifference(accurate, HeightfieldGenerator.Generate(cfg, dem)) == 0f);
            cfg.flattenPostPad = false;

            // Subtle detail: zero restores the baseline exactly; amplitude scales the deviation in metres.
            cfg.mode = TerrainMode.RealWorldSubtleDetail;
            cfg.ApplySubtleDetailDefaults();
            cfg.displacementMultiplier = 0f;
            Check("subtle detail with multiplier 0 equals the accurate baseline exactly", MaxDifference(accurate, HeightfieldGenerator.Generate(cfg, dem)) == 0f);
            cfg.displacementMultiplier = 1f; cfg.layers[0].amplitudeMetres = 0f;
            Check("subtle detail with amplitude 0 equals the accurate baseline exactly", MaxDifference(accurate, HeightfieldGenerator.Generate(cfg, dem)) == 0f);
            cfg.layers[0].amplitudeMetres = 6f;
            float d6 = MaxDifference(accurate, HeightfieldGenerator.Generate(cfg, dem));
            cfg.layers[0].amplitudeMetres = 24f;
            float d24 = MaxDifference(accurate, HeightfieldGenerator.Generate(cfg, dem));
            Check($"detail amplitude is in metres: 6 m gives up to {d6:F2} m, 24 m gives up to {d24:F2} m (4x)", d6 > 0.5f && d6 <= 3.01f && Mathf.Abs(d24 / d6 - 4f) < 0.01f);

            // Major variations: blend 0 is the DEM; blend 1 differs; relief scale stretches about the mean.
            cfg.mode = TerrainMode.RealWorldMajorVariations;
            cfg.ApplyMajorVariationDefaults();
            cfg.modificationBlend = 0f;
            Check("major variations with blend 0 equals the accurate baseline exactly", MaxDifference(accurate, HeightfieldGenerator.Generate(cfg, dem)) == 0f);
            cfg.modificationBlend = 1f;
            var major = HeightfieldGenerator.Generate(cfg, dem);
            Check($"major variations with blend 1 reshapes the terrain (up to {MaxDifference(accurate, major):F0} m)", MaxDifference(accurate, major) > 20f);
            cfg.layers.Clear(); cfg.sourceReliefScale = 2f;
            var stretched = HeightfieldGenerator.Generate(cfg, dem);
            Check("relief scale 2 doubles the height range about the mean", Mathf.Abs((stretched.max - stretched.min) - 2f * (accurate.max - accurate.min)) < 0.5f);

            // Custom hybrid: source weight 0 removes the real landform; weight 1 with no displacement is the DEM.
            cfg.mode = TerrainMode.CustomHybrid;
            cfg.sourceReliefScale = 1f; cfg.displacementMultiplier = 0f;
            Check("custom hybrid with source 1 and displacement 0 equals the baseline", MaxDifference(accurate, HeightfieldGenerator.Generate(cfg, dem)) < 0.001f);
            cfg.sourceReliefScale = 0f;
            var flat = HeightfieldGenerator.Generate(cfg, dem);
            Check("custom hybrid with source 0 is flat at the source's mean", flat.max - flat.min < 0.001f && Mathf.Abs(flat.min - dem.meanElevation) < 0.001f);

            // The source grid itself is never changed by generating.
            var again = DemReader.Load(asc);
            bool untouched = true;
            for (int i = 0; i < dem.metres.Length; i++) if (dem.metres[i] != again.metres[i]) { untouched = false; break; }
            Check("the source elevation grid is unchanged after every mode has run", untouched);

            // Downsampling and coverage are reported.
            cfg.mode = TerrainMode.RealWorldAccurate;
            cfg.heightmapResolution = 33; cfg.sizeMetres = 3000f;
            var coarse = HeightfieldGenerator.Generate(cfg, dem);
            Check($"downsampling is detectable: source {coarse.sourceSpacingMetres:F1} m, output {coarse.Spacing:F1} m", coarse.sourceSpacingMetres < coarse.Spacing);
            cfg.sizeMetres = 20000f;
            Check("an area larger than the file is reported, not silently invented", HeightfieldGenerator.Generate(cfg, dem).outsideSourceSamples > 0);

            // SRTM .hgt (3 arc-second): big-endian, north row first, position from the file name. Synthetic values again.
            var hgt = new byte[1201 * 1201 * 2];
            for (int row = 0; row < 1201; row++)
            for (int col = 0; col < 1201; col++)
            {
                short v = (short)(row == 600 && col == 600 ? short.MinValue : 100 + col / 4 + (1200 - row) / 8);   // rises east and north; one void
                int i = (row * 1201 + col) * 2;
                hgt[i] = (byte)((v >> 8) & 0xFF); hgt[i + 1] = (byte)(v & 0xFF);
            }
            string hgtPath = Path.Combine(dir, "N28E077.hgt");
            File.WriteAllBytes(hgtPath, hgt);
            var tile = DemReader.Load(hgtPath);
            Check("synthetic .hgt: position from the file name, big-endian values, void filled",
                tile.south == 28 && tile.west == 77 && tile.north == 29 && tile.east == 78 && tile.noDataCount == 1
                && Mathf.Approximately(tile.Sample(28.0, 77.0), 100f) && Mathf.Approximately(tile.Sample(29.0, 78.0), 100 + 300 + 150) && Mathf.Abs(tile.Sample(28.5, 77.5) - 325f) < 1f);

            // 16-bit RAW with a metadata sidecar, stored south-up with scale and offset.
            const int rw = 64;
            var raw = new byte[rw * rw * 2];
            for (int row = 0; row < rw; row++)
            for (int col = 0; col < rw; col++)
            {
                ushort v = (ushort)(1000 + col * 10 + row * 5);      // row 0 = south in this file
                raw[(row * rw + col) * 2] = (byte)(v & 0xFF); raw[(row * rw + col) * 2 + 1] = (byte)(v >> 8);
            }
            string rawPath = Path.Combine(dir, "synthetic.raw");
            File.WriteAllBytes(rawPath, raw);
            File.WriteAllText(Path.ChangeExtension(rawPath, ".json"), "{\"width\":64,\"height\":64,\"littleEndian\":true,\"signed\":false,\"centreLatitude\":28.5,\"centreLongitude\":77.5,\"cellSizeMetres\":30,\"elevationScale\":0.1,\"elevationOffset\":50,\"rowOrder\":\"south-up\",\"source\":\"synthetic test data\",\"licence\":\"none\"}");
            var rawGrid = DemReader.Load(rawPath);
            Check("synthetic 16-bit RAW: scale, offset, south-up rows and metre cell size are honoured",
                Mathf.Abs(rawGrid.minElevation - 150f) < 0.01f && Mathf.Abs(rawGrid.maxElevation - (150f + 63f + 31.5f)) < 0.01f
                && rawGrid.Sample(rawGrid.north, rawGrid.west) > rawGrid.Sample(rawGrid.south, rawGrid.west) && Math.Abs(rawGrid.CellMetresX - 30.0) < 0.05 && Math.Abs(rawGrid.CellMetresY - 30.0) < 0.05);
            Free(cfg);
        }

        // --- Bad inputs give clear errors, not terrain ---
        static void InvalidInputChecks(string dir)
        {
            Check("missing file is reported", Throws(() => DemReader.Load(Path.Combine(dir, "nope.asc")), "not found"));
            File.WriteAllText(Path.Combine(dir, "photo.jpg"), "x");
            Check("unsupported format is reported", Throws(() => DemReader.Load(Path.Combine(dir, "photo.jpg")), "Unsupported"));
            File.WriteAllText(Path.Combine(dir, "area.tif"), "x");
            Check("GeoTIFF is refused with the conversion command, not half-read", Throws(() => DemReader.Load(Path.Combine(dir, "area.tif")), "gdal_translate"));
            File.WriteAllBytes(Path.Combine(dir, "N28E077.hgt"), new byte[1000]);
            Check("truncated .hgt is reported", Throws(() => DemReader.Load(Path.Combine(dir, "N28E077.hgt")), "truncated"));
            File.WriteAllBytes(Path.Combine(dir, "tile.hgt"), new byte[1201 * 1201 * 2]);
            Check(".hgt without a position in its name is reported", Throws(() => DemReader.Load(Path.Combine(dir, "tile.hgt")), "south-west corner"));
            File.WriteAllText(Path.Combine(dir, "short.asc"), "ncols 4\nnrows 4\nxllcorner 77\nyllcorner 28\ncellsize 0.001\n1 2 3 4\n5 6 7 8\n");
            Check(".asc with too few rows is reported", Throws(() => DemReader.Load(Path.Combine(dir, "short.asc")), "promises"));
            File.WriteAllText(Path.Combine(dir, "bad.asc"), "ncols 2\nnrows 2\nxllcorner 77\nyllcorner 28\ncellsize 0.001\n1 2\n3 banana\n");
            Check(".asc with a non-numeric value is reported", Throws(() => DemReader.Load(Path.Combine(dir, "bad.asc")), "not a number"));
            File.WriteAllText(Path.Combine(dir, "metres.asc"), "ncols 2\nnrows 2\nxllcorner 500000\nyllcorner 3100000\ncellsize 30\n1 2\n3 4\n");
            Check("a projected (metre) .asc grid is refused with the reprojection command", Throws(() => DemReader.Load(Path.Combine(dir, "metres.asc")), "EPSG:4326"));
            File.WriteAllText(Path.Combine(dir, "wild.asc"), "ncols 2\nnrows 2\nxllcorner 77\nyllcorner 28\ncellsize 0.001\n10 20\n30 99999\n");
            Check("impossible elevations are refused instead of becoming a spike", Throws(() => DemReader.Load(Path.Combine(dir, "wild.asc")), "not possible on Earth"));
            File.WriteAllText(Path.Combine(dir, "void.asc"), "ncols 2\nnrows 2\nxllcorner 77\nyllcorner 28\ncellsize 0.001\nNODATA_value -9999\n-9999 -9999\n-9999 -9999\n");
            Check("a file that is all NoData is reported", Throws(() => DemReader.Load(Path.Combine(dir, "void.asc")), "NoData"));
            File.WriteAllBytes(Path.Combine(dir, "lonely.raw"), new byte[8]);
            Check(".raw without its metadata file is reported", Throws(() => DemReader.Load(Path.Combine(dir, "lonely.raw")), "metadata"));

            var cfg = Config(TerrainMode.RealWorldAccurate);
            Check("a real-world mode without a file fails validation", cfg.Validate().Count > 0 && Throws(() => HeightfieldGenerator.Generate(cfg, null), "elevation data"));
            cfg.mode = TerrainMode.PureProcedural;
            cfg.heightmapResolution = 500;
            Check("heightmap resolution 500 is rejected; 513 and 1025 are accepted",
                cfg.Validate().Count > 0 && TerrainGenConfig.IsValidResolution(513) && TerrainGenConfig.IsValidResolution(1025) && !TerrainGenConfig.IsValidResolution(512));
            Free(cfg);
        }

        // --- Unity Terrain objects ---
        static void UnityTerrainChecks()
        {
            var cfg = Config(TerrainMode.PureProcedural, 65, 2000f);
            cfg.ApplyPreset(ProceduralPreset.MountainRanges);
            cfg.tilesPerSide = 2;
            cfg.flattenPostPad = true;
            var layerA = new TerrainLayer();
            var layerB = new TerrainLayer();
            cfg.surfaceRules.Add(new SurfaceRule { label = "Flat", layer = layerA, maxSlope = 20f });
            cfg.surfaceRules.Add(new SurfaceRule { label = "Steep", layer = layerB, minSlope = 20f });

            var result = TerrainBuilder.Build(cfg, null);
            try
            {
                var first = result.tiles[0];
                Vector3 size = first.terrainData.size;
                Check("build makes 4 tiles of 1000 m with a 65-sample heightmap each", result.tiles.Count == 4 && first.terrainData.heightmapResolution == 65 && Mathf.Approximately(size.x, 1000f) && Mathf.Approximately(size.z, 1000f));
                Check($"terrain height range {size.y:F0} m covers the data {result.field.max - result.field.min:F0} m without clipping", size.y >= result.field.max - result.field.min);

                // Unity's own samples along the border between tile (0,0) and tile (1,0).
                float[,] left = result.tiles[0].terrainData.GetHeights(64, 0, 1, 65), right = result.tiles[1].terrainData.GetHeights(0, 0, 1, 65);
                float gap = 0f;
                for (int z = 0; z < 65; z++) gap = Mathf.Max(gap, Mathf.Abs(left[z, 0] - right[z, 0]));
                Check($"Unity tile borders match (largest gap {gap * size.y:F4} m)", gap == 0f);

                // World height from Unity equals the generated elevation (to the 16-bit heightmap's precision).
                float worst = 0f;
                for (int i = 0; i < 40; i++)
                {
                    int gx = (i * 37) % result.field.resolution, gz = (i * 53) % result.field.resolution;
                    var p = new Vector3(-1000f + gx * result.field.Spacing, 0f, -1000f + gz * result.field.Spacing);
                    float expected = result.field.At(gx, gz) - result.field.centreElevation;
                    float got = HeightOn(result, p);
                    if (!float.IsNaN(got)) worst = Mathf.Max(worst, Mathf.Abs(got - expected));
                }
                Check($"Unity ground height matches the generated elevation (worst {worst:F3} m; 16-bit step is {size.y / 65535f:F3} m)", worst < size.y / 65535f * 2f + 0.01f);
                Check($"the ground under the post is at world height 0 (got {HeightOn(result, Vector3.zero):F3} m) and the pad is level", Mathf.Abs(HeightOn(result, Vector3.zero)) < 0.05f && Mathf.Abs(HeightOn(result, new Vector3(25f, 0f, -20f))) < 0.05f);

                float[,,] alpha = first.terrainData.GetAlphamaps(0, 0, first.terrainData.alphamapWidth, first.terrainData.alphamapHeight);
                bool normalised = true;
                float flatShare = 0f;
                for (int z = 0; z < alpha.GetLength(0); z++)
                for (int x = 0; x < alpha.GetLength(1); x++)
                {
                    if (Mathf.Abs(alpha[z, x, 0] + alpha[z, x, 1] - 1f) > 0.01f) normalised = false;
                    flatShare += alpha[z, x, 0];
                }
                flatShare /= alpha.GetLength(0) * alpha.GetLength(1);
                Check($"surface weights sum to 1 everywhere; slope rule splits the tile ({flatShare * 100f:F0} % flat layer)", normalised && flatShare > 0.02f && flatShare < 0.98f);

                // Slope: a plane rising 1 m per metre is 45 degrees.
                var ramp = new Heightfield { resolution = 5, sizeMetres = 40f, metres = new float[25] };
                for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++) ramp.metres[z * 5 + x] = x * 10f;
                Check("slope of a 1-in-1 ramp is 45 degrees, centre and edge", Mathf.Abs(HeightfieldGenerator.SlopeDegrees(ramp, 2, 2) - 45f) < 0.01f && Mathf.Abs(HeightfieldGenerator.SlopeDegrees(ramp, 0, 4) - 45f) < 0.01f);
                Check("surface rule fades at its limits", TerrainBuilder.RuleWeight(cfg.surfaceRules[0], 10f, 10f) == 1f && TerrainBuilder.RuleWeight(cfg.surfaceRules[0], 10f, 40f) == 0f && TerrainBuilder.RuleWeight(cfg.surfaceRules[0], 10f, 23f) > 0f);
            }
            finally
            {
                foreach (var t in result.tiles) Free(t.terrainData);
                Free(result.root);
                Free(layerA); Free(layerB);
                Free(cfg);
            }
        }

        // World ground height on the test's own tiles only, so another terrain already in the scene cannot interfere.
        static float HeightOn(TerrainBuildResult result, Vector3 position)
        {
            foreach (var t in result.tiles)
            {
                Vector3 local = position - t.transform.position, size = t.terrainData.size;
                if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
                return t.SampleHeight(position) + t.transform.position.y;
            }
            return float.NaN;
        }

        static bool Throws(Action action, string messageContains)
        {
            try { action(); }
            catch (DemException e) { return e.Message.IndexOf(messageContains, StringComparison.OrdinalIgnoreCase) >= 0; }
            catch (Exception) { return false; }
            return false;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
