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
                ImageryChecks(dir);
                UnityTerrainChecks();
                WebTileChecks(dir);
                SurfaceChecks();
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

        // --- Ground imagery (synthetic picture: west half red, east half blue) ---
        static void ImageryChecks(string dir)
        {
            var pic = new Texture2D(64, 32, TextureFormat.RGB24, false);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 64; x++) pic.SetPixel(x, y, x < 32 ? Color.red : Color.blue);
            string png = Path.Combine(dir, "synthetic_image.png");
            File.WriteAllBytes(png, pic.EncodeToPNG());
            Free(pic);

            // No metadata: loads, not georeferenced, stretched over the whole area.
            var plain = ImageryReader.Load(png);
            var cfg = Config(TerrainMode.PureProcedural, 33, 2000f);
            Rect whole = ImageryReader.Extent(cfg, null, plain);
            Check("picture without bounds loads and is stretched over the whole area", plain.texture.width == 64 && !plain.georeferenced && whole.xMin == -1000f && whole.width == 2000f && Mathf.Approximately(ImageryReader.Coverage(cfg, whole), 1f));

            // With bounds and a real-world mode it is placed by coordinates: here, the eastern half of a 0.02 degree square at 28 N 77 E.
            File.WriteAllText(Path.ChangeExtension(png, ".json"), "{\"west\":77.01,\"south\":28.0,\"east\":77.02,\"north\":28.02,\"source\":\"synthetic test picture\",\"licence\":\"none\"}");
            var placed = ImageryReader.Load(png);
            var dem = new ElevationGrid { width = 3, height = 3, metres = new float[9], west = 77.0, east = 77.02, south = 28.0, north = 28.02, pixelIsArea = false };
            dem.FillNoDataAndMeasure(null);
            cfg.mode = TerrainMode.RealWorldAccurate; cfg.demPath = "synthetic";
            Rect half = ImageryReader.Extent(cfg, dem, placed);
            float lonMetres = (float)(0.01 * GeoMath.MetresPerDegreeLongitude(28.01)), latMetres = (float)(0.02 * GeoMath.MetresPerDegreeLatitude(28.01));
            Check($"picture with bounds is placed by coordinates ({half.width:F0} m x {half.height:F0} m, starting at the centre line)",
                placed.georeferenced && Mathf.Abs(half.xMin) < 0.5f && Mathf.Abs(half.width - lonMetres) < 0.5f && Mathf.Abs(half.height - latMetres) < 0.5f && Mathf.Abs(half.yMin + latMetres * 0.5f) < 0.5f);
            Check("coverage is about half when the picture covers the eastern half", Mathf.Abs(ImageryReader.Coverage(cfg, half) - 0.5f * lonMetres / 1000f) < 0.02f);

            // Built into a terrain: one extra layer per tile, sized to the picture, weighted by the blend only where it covers.
            cfg.mode = TerrainMode.PureProcedural; cfg.tilesPerSide = 2; cfg.imageryBlend = 0.75f;
            var layer = new TerrainLayer();
            cfg.surfaceRules.Add(new SurfaceRule { label = "Everything", layer = layer });
            var eastHalf = new Imagery { texture = plain.texture, georeferenced = false };
            var result = TerrainBuilder.Build(cfg, null, null, null, eastHalf);
            try
            {
                var d0 = result.tiles[0].terrainData; var d1 = result.tiles[1].terrainData;
                var l0 = d0.terrainLayers[d0.terrainLayers.Length - 1]; var l1 = d1.terrainLayers[d1.terrainLayers.Length - 1];
                Check("each tile gets an imagery layer spanning the whole picture, offset by the tile's position",
                    d0.terrainLayers.Length == 2 && l0.diffuseTexture == plain.texture && l0.tileSize == new Vector2(2000f, 2000f) && l0.tileOffset == Vector2.zero && l1.tileOffset == new Vector2(1000f, 0f));
                float[,,] a = d0.GetAlphamaps(0, 0, 4, 4);
                Check("imagery takes its blend share and the rules keep the rest", Mathf.Abs(a[1, 1, 1] - 0.75f) < 0.02f && Mathf.Abs(a[1, 1, 0] - 0.25f) < 0.02f);
                float before = result.field.At(5, 5);
                cfg.imageryBlend = 0f;
                var noPicture = HeightfieldGenerator.Generate(cfg, null);
                Check("imagery never changes heights", noPicture.At(5, 5) == before);
            }
            finally
            {
                foreach (var t in result.tiles) { foreach (var l in t.terrainData.terrainLayers) if (l != layer) Free(l); Free(t.terrainData); }
                Free(result.root); Free(layer); Free(plain.texture); Free(placed.texture); Free(cfg);
            }

            File.WriteAllText(Path.Combine(dir, "scene.tif"), "x");
            Check("GeoTIFF imagery is refused with the conversion command", Throws(() => ImageryReader.Load(Path.Combine(dir, "scene.tif")), "gdal_translate"));
            File.WriteAllText(Path.Combine(dir, "broken.png"), "not a picture");
            Check("a file that is not a picture is reported", Throws(() => ImageryReader.Load(Path.Combine(dir, "broken.png")), "could not be read"));
            Check("missing imagery file is reported", Throws(() => ImageryReader.Load(Path.Combine(dir, "nope.png")), "not found"));
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

        // Real-place download: tile maths, the Terrarium decode, and the whole pipeline from cached tiles (no network).
        static void WebTileChecks(string dir)
        {
            // Terrarium: (R * 256 + G + B / 256) - 32768.
            Check("terrarium decode: sea level", ElevationDownloader.Decode(128, 0, 0) == 0f);
            Check("terrarium decode: 300.5 m", Mathf.Approximately(ElevationDownloader.Decode(129, 44, 128), 300.5f));
            Check("terrarium decode: below sea level", ElevationDownloader.Decode(127, 255, 0) == -1f);

            // Web Mercator round trip, and the well-known tile for a place.
            WebMercator.ToPixel(32.6875, 74.8375, 13, out double px, out double py);
            WebMercator.FromPixel(px, py, 13, out double lat, out double lon);
            Check("mercator round trip", Math.Abs(lat - 32.6875) < 1e-9 && Math.Abs(lon - 74.8375) < 1e-9);
            Check("tile of a known place (0,0 at zoom 1 is the north-west quarter)", (int)(px / 256) == (int)((74.8375 + 180) / 360 * 8192) && WebMercator.WorldPixels(1) == 512);
            Check("metres per pixel at the equator, zoom 0", Math.Abs(WebMercator.MetresPerPixel(0, 0) - 156543.03) < 0.1);

            // Zoom choice: finest zoom up to 13 that keeps within the tile budget.
            var small = new AreaRequest { latitude = 32.6875, longitude = 74.8375, radiusMetres = 2500f };
            int zs = small.Zoom(out int smallTiles);
            var big = new AreaRequest { latitude = 32.6875, longitude = 74.8375, radiusMetres = 60000f };
            int zb = big.Zoom(out int bigTiles);
            Check($"zoom: 5 km area at zoom 13 ({smallTiles} tiles); 120 km area drops to {zb} within 64 tiles", zs == 13 && smallTiles <= 64 && zb < 13 && bigTiles <= 64);
            Check("grid size: power of two plus one, about two samples per source pixel", ElevationDownloader.GridSize(5000f, 16f) == 1025 && ElevationDownloader.GridSize(5000f, 40f) == 257 && ElevationDownloader.GridSize(500000f, 16f) == 2049);

            // One synthetic tile through PNG and back.
            var tex = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var px32 = new Color32[256 * 256];
            for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++) px32[y * 256 + x] = new Color32(129, (byte)(255 - y), 0, 255);   // top row (texture row 255) = 256 m
            tex.SetPixels32(px32);
            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            var mosaic = new float[256 * 256];
            Check("tile decodes with the top row first", ElevationDownloader.DecodeTile(png, mosaic, 256, 0, 0) && mosaic[0] == 256f && mosaic[255 * 256] == 511f);

            // Whole pipeline offline: every tile already in a (temporary) cache, so no request is made.
            string cache = Path.Combine(dir, "tiles"), areas = Path.Combine(dir, "areas");
            ElevationDownloader.CacheFolderOverride = cache;
            ElevationDownloader.AreasFolderOverride = areas;
            try
            {
                var flat = new Texture2D(256, 256, TextureFormat.RGBA32, false);
                var fill = new Color32[256 * 256];
                for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(129, 144, 0, 255);   // 400 m everywhere
                flat.SetPixels32(fill);
                byte[] flatPng = flat.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(flat);
                var req = new AreaRequest { name = "Self test/area", latitude = 32.6875, longitude = 74.8375, radiusMetres = 1500f };
                int z = req.Zoom(out _);
                req.Bounds(out double s, out double w, out double n, out double e, 200.0);
                WebMercator.TileRange(s, w, n, e, z, out int x0, out int y0, out int x1, out int y1);
                for (int ty = y0; ty <= y1; ty++)
                for (int tx = x0; tx <= x1; tx++)
                {
                    string p = ElevationDownloader.TileCachePath("terrarium", z, tx, ty);
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllBytes(p, flatPng);
                }
                AreaDownloadResult got = null;
                var run = ElevationDownloader.Download(req, null, r => got = r);
                int steps = 0;
                while (run.MoveNext() && steps++ < 1000) { }
                Check("offline download uses only cached tiles", got != null && got.ok && got.tilesFetched == 0 && got.tilesFromCache == (x1 - x0 + 1) * (y1 - y0 + 1));
                Check("area name is made safe for a file name", got != null && Path.GetFileName(got.rawPath) == "Self_test_area.raw");
                var grid = got != null && got.ok ? DemReader.Load(got.rawPath) : null;
                Check("the written file reads back: 400 m, centred, 3 km across", grid != null && Math.Abs(grid.minElevation - 400f) < 0.3f && Math.Abs(grid.maxElevation - 400f) < 0.3f
                    && Math.Abs(grid.CentreLatitude - 32.6875) < 1e-6 && Math.Abs(grid.CentreLongitude - 74.8375) < 1e-6 && Math.Abs(grid.WidthMetres - 3000.0) < 1.0);
                Check("it is listed as downloaded", ElevationDownloader.DownloadedAreas().Count == 1);
            }
            finally
            {
                ElevationDownloader.CacheFolderOverride = null;
                ElevationDownloader.AreasFolderOverride = null;
            }
        }

        // Ground materials: tileable noise, terrain analysis on a known shape, and the extra rule conditions.
        static void SurfaceChecks()
        {
            bool wraps = true;
            for (int i = 0; i < 16; i++)
            {
                float v = i / 16f;
                if (Mathf.Abs(TileNoise.Fbm(0f, v, 4, 5, 3) - TileNoise.Fbm(1f, v, 4, 5, 3)) > 1e-4f) wraps = false;
                if (Mathf.Abs(TileNoise.Ridged(v, 0f, 3, 4, 3) - TileNoise.Ridged(v, 1f, 3, 4, 3)) > 1e-4f) wraps = false;
                TileNoise.Cells(0f, v, 9, 5, out float a1, out _, out _);
                TileNoise.Cells(1f, v, 9, 5, out float b1, out _, out _);
                if (Mathf.Abs(a1 - b1) > 1e-4f) wraps = false;
            }
            Check("material noise wraps at the texture edges (tiles without seams)", wraps);

            // A V-shaped valley running north-south: floor at x = middle, sides rising east and west, falling to the north.
            var f = new Heightfield { resolution = 129, sizeMetres = 2560f, metres = new float[129 * 129] };
            for (int z = 0; z < 129; z++)
            for (int x = 0; x < 129; x++)
                f.metres[z * 129 + x] = 1000f + Mathf.Abs(x - 64) * 8f + z * 2f;
            f.min = 1000f; f.max = 1000f + 64 * 8f + 128 * 2f;
            var an = TerrainAnalysis.Compute(f);
            an.At(64, 40, out float floorWet, out float floorCurv, out _);
            an.At(10, 40, out float sideWet, out float sideCurv, out _);
            Check($"wetness: valley floor {floorWet:F2} is wetter than the hillside {sideWet:F2}", floorWet > 0.5f && sideWet < floorWet);
            Check("curvature: the valley floor is a hollow (positive), the open slope is not", floorCurv > 0.01f && Mathf.Abs(sideCurv) < 0.01f);
            an.At(64, 40, out _, out _, out float floorNorth);
            Check("facing: this valley rises to the north, so it faces south (negative)", floorNorth < 0f);
            var ridge = new Heightfield { resolution = 65, sizeMetres = 640f, metres = new float[65 * 65] };
            for (int z = 0; z < 65; z++) for (int x = 0; x < 65; x++) ridge.metres[z * 65 + x] = 500f - Mathf.Abs(x - 32) * 5f;
            TerrainAnalysis.Compute(ridge).At(32, 32, out _, out float crest, out _);
            Check("curvature: a crest is negative", crest < -0.01f);

            var snow = new SurfaceRule { absoluteHeight = true, minHeight = 4300f, heightBlend = 100f, northBias = 1f };
            Check("snow line is absolute height", TerrainBuilder.RuleWeight(snow, 50f, 4600f, 10f, 0.5f, 0f, 0f, 0.5f) > 0.9f && TerrainBuilder.RuleWeight(snow, 3000f, 3600f, 10f, 0.5f, 0f, 0f, 0.5f) == 0f);
            Check("north bias: more on north faces, none on south faces at full bias",
                TerrainBuilder.RuleWeight(snow, 0f, 4600f, 10f, 0.5f, 0f, 1f, 0.5f) > TerrainBuilder.RuleWeight(snow, 0f, 4600f, 10f, 0.5f, 0f, 0f, 0.5f) && TerrainBuilder.RuleWeight(snow, 0f, 4600f, 10f, 0.5f, 0f, -1f, 0.5f) == 0f);
            var green = new SurfaceRule { minWetness = 0.55f, wetnessBlend = 0.1f };
            Check("valley green only where it is wet", TerrainBuilder.RuleWeight(green, 0f, 0f, 5f, 0.9f, 0f, 0f, 0.5f) > 0.9f && TerrainBuilder.RuleWeight(green, 0f, 0f, 5f, 0.2f, 0f, 0f, 0.5f) == 0f);
            var neutral = new SurfaceRule();
            Check("a rule with only the old settings behaves as before", Mathf.Approximately(TerrainBuilder.RuleWeight(neutral, 100f, 30f), 1f));
            var kit = ScriptableObject.CreateInstance<TerrainSurfaceKit>();
            kit.rock = new TerrainLayer(); kit.scree = new TerrainLayer(); kit.soil = new TerrainLayer(); kit.grass = new TerrainLayer(); kit.snow = new TerrainLayer(); kit.sand = new TerrainLayer();
            bool allBiomes = true;
            foreach (SurfaceBiome b in Enum.GetValues(typeof(SurfaceBiome))) if (b != SurfaceBiome.Auto && SurfaceBiomes.Rules(kit, b).Count < 4) allBiomes = false;
            Check("every biome has at least four placement rules", allBiomes);
            var high = new Heightfield { min = 2600f, max = 4500f }; var flat = new Heightfield { min = 260f, max = 320f };
            Check("auto biome: Kargil heights = high desert, Jammu = plains", SurfaceBiomes.Resolve(SurfaceBiome.Auto, high) == SurfaceBiome.HighDesert && SurfaceBiomes.Resolve(SurfaceBiome.Auto, flat) == SurfaceBiome.Plains);
            UnityEngine.Object.DestroyImmediate(kit);

            Check("satellite picture size: 5 km at 4 m pixels = 2048, never above 4096", ImageryDownloader.PictureSize(5000f, 3.9) == 2048 && ImageryDownloader.PictureSize(50000f, 3.9) == 4096 && ImageryDownloader.PictureSize(1000f, 20) == 512);
            Check("surround and picture files sit beside the area", ElevationDownloader.SurroundPath("C:/a/Kargil/Kargil.raw").Replace('\\', '/') == "C:/a/Kargil/Kargil_surround.raw" && ImageryDownloader.PathFor("C:/a/Kargil/Kargil.raw").Replace('\\', '/') == "C:/a/Kargil/Kargil_satellite.jpg");
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
