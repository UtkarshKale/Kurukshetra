using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Kurukshetra.TerrainGen
{
    // Web Mercator ("slippy map") tile maths, shared by the map picker and the elevation downloader.
    // Tile x, y and pixel coordinates are at a zoom level z: 2^z tiles across, 256 pixels per tile.
    public static class WebMercator
    {
        public const int TileSize = 256;
        public const double MaxLatitude = 85.05112878;

        public static double WorldPixels(int zoom) => TileSize * Math.Pow(2.0, zoom);

        public static void ToPixel(double lat, double lon, int zoom, out double px, out double py)
        {
            lat = Math.Max(-MaxLatitude, Math.Min(MaxLatitude, lat));
            double n = WorldPixels(zoom), phi = lat * Math.PI / 180.0;
            px = (lon + 180.0) / 360.0 * n;
            py = (1.0 - Math.Log(Math.Tan(phi) + 1.0 / Math.Cos(phi)) / Math.PI) * 0.5 * n;
        }

        public static void FromPixel(double px, double py, int zoom, out double lat, out double lon)
        {
            double n = WorldPixels(zoom);
            lon = px / n * 360.0 - 180.0;
            lat = Math.Atan(Math.Sinh(Math.PI * (1.0 - 2.0 * py / n))) * 180.0 / Math.PI;
        }

        // Ground metres covered by one pixel at this latitude and zoom.
        public static double MetresPerPixel(double lat, int zoom)
        {
            return 156543.03392804097 * Math.Cos(lat * Math.PI / 180.0) / Math.Pow(2.0, zoom);
        }

        // Tiles covering a lat/lon box: inclusive ranges.
        public static void TileRange(double south, double west, double north, double east, int zoom, out int x0, out int y0, out int x1, out int y1)
        {
            ToPixel(north, west, zoom, out double ax, out double ay);
            ToPixel(south, east, zoom, out double bx, out double by);
            int max = (1 << zoom) - 1;
            x0 = Math.Max(0, (int)Math.Floor(ax / TileSize)); y0 = Math.Max(0, (int)Math.Floor(ay / TileSize));
            x1 = Math.Min(max, (int)Math.Floor(bx / TileSize)); y1 = Math.Min(max, (int)Math.Floor(by / TileSize));
        }
    }

    // A square area on the ground: a centre and a "radius", which is half the side. The terrain is the square that
    // encloses the circle the trainer drew.
    [Serializable]
    public class AreaRequest
    {
        public string name = "Area";
        public string folder;               // folder under the areas folder; empty = the name
        public double latitude, longitude;
        public float radiusMetres = 2500f;
        public int maxTiles = 64;           // politeness: never fetch more than this many tiles for one area
        public int maxZoom = 13;            // ~15-19 m pixels at mid latitudes; the data itself is ~30 m in most places

        public float SideMetres => radiusMetres * 2f;

        public void Bounds(out double south, out double west, out double north, out double east, double marginMetres = 0.0)
        {
            double r = radiusMetres + marginMetres;
            GeoMath.LocalToGeo(latitude, longitude, -r, -r, out south, out west);
            GeoMath.LocalToGeo(latitude, longitude, r, r, out north, out east);
        }

        // The finest zoom (up to maxZoom) whose tiles for this area stay within maxTiles.
        public int Zoom(out int tileCount)
        {
            Bounds(out double s, out double w, out double n, out double e, 200.0);
            for (int z = Math.Max(1, maxZoom); z >= 1; z--)
            {
                WebMercator.TileRange(s, w, n, e, z, out int x0, out int y0, out int x1, out int y1);
                tileCount = (x1 - x0 + 1) * (y1 - y0 + 1);
                if (tileCount <= maxTiles) return z;
            }
            tileCount = 1;
            return 1;
        }
    }

    // Set by the Skip / Cancel buttons while an area downloads. skip ends only the current step (it is cleared when the next
    // step starts); cancel ends the whole download.
    public class DownloadControl
    {
        public volatile bool skip, cancel;
        public bool Stop => skip || cancel;
        public string StopWord => cancel ? "cancelled" : "skipped";
    }

    // One web request with a timeout, a bounded number of tries and a status text ("waiting for server, try 2/3, 35 s").
    // Never waits longer than tries * timeoutSeconds + the pauses between tries, and gives up at once on skip / cancel.
    public static class WebFetch
    {
        public static IEnumerator GetBytes(string url, string userAgent, int tries, float timeoutSeconds, float retryWaitSeconds,
            DownloadControl control, Action<string> status, Action<byte[]> ok, Action<string> failed)
        {
            string last = "no answer";
            for (int attempt = 1; attempt <= tries; attempt++)
            {
                using (var request = UnityWebRequest.Get(url))
                {
                    request.SetRequestHeader("User-Agent", userAgent);
                    request.timeout = Mathf.Max(1, Mathf.RoundToInt(timeoutSeconds));
                    double started = Time.realtimeSinceStartupAsDouble;
                    int shown = -1;
                    var op = request.SendWebRequest();
                    while (!op.isDone)
                    {
                        if (control != null && control.Stop) { request.Abort(); break; }
                        int sec = (int)(Time.realtimeSinceStartupAsDouble - started);
                        if (sec != shown) { shown = sec; status?.Invoke($"waiting for server, try {attempt}/{tries}, {sec} s"); }
                        yield return null;
                    }
                    if (control != null && control.Stop) { failed(control.StopWord); yield break; }
                    if (request.result == UnityWebRequest.Result.Success) { ok(request.downloadHandler.data); yield break; }
                    last = (request.responseCode > 0 ? "HTTP " + request.responseCode + " " : "") + request.error;
                }
                if (attempt < tries)
                {
                    double until = Time.realtimeSinceStartupAsDouble + retryWaitSeconds;
                    while (Time.realtimeSinceStartupAsDouble < until)
                    {
                        if (control != null && control.Stop) { failed(control.StopWord); yield break; }
                        status?.Invoke($"failed ({last}), next try {attempt + 1}/{tries} in {Mathf.CeilToInt((float)(until - Time.realtimeSinceStartupAsDouble))} s");
                        yield return null;
                    }
                }
            }
            failed(last);
        }
    }

    public class AreaDownloadResult
    {
        public bool ok;
        public string error = "";
        public string rawPath = "";             // absolute
        public string projectRelativePath = ""; // what goes in TerrainGenConfig.demPath when the file is inside the project
        public int zoom, tilesFetched, tilesFromCache, gridSize;
        public double sourceMetresPerPixel, gridMetresPerSample;
        public float minElevation, maxElevation;
        public string summary = "";
    }

    // Downloads real elevation for an area from the AWS Open Data "Terrain Tiles" (Mapzen Terrarium PNG, no key),
    // and writes it as a 16-bit RAW + JSON that DemReader already reads, so it works offline afterwards.
    // Tiles are cached on disk and never fetched twice.
    public static class ElevationDownloader
    {
        public const string TileUrl = "https://s3.amazonaws.com/elevation-tiles-prod/terrarium/{z}/{x}/{y}.png";
        public const string UserAgent = "Kurukshetra-SIH-trainer/0.1 (counter-drone training simulator)";
        public const string Attribution = "Terrain Tiles (Mapzen / AWS Open Data). Elevation sources include SRTM, GMTED2010, ETOPO1, NED and others: https://github.com/tilezen/joerd/blob/master/docs/attribution.md";
        public const string Licence = "Open data; credit the sources listed at https://github.com/tilezen/joerd/blob/master/docs/attribution.md";

        // Stored heights: metres = raw * Scale + Offset, so 0.2 m steps from -1000 m to about 12 100 m.
        public const double Scale = 0.2, Offset = -1000.0;

        // Terrarium: height in metres from one pixel's red, green and blue.
        public static float Decode(byte r, byte g, byte b) => r * 256f + g + b / 256f - 32768f;

        // Tests point these somewhere temporary; null = the real folders.
        public static string CacheFolderOverride, AreasFolderOverride;

        public static string CacheFolder => CacheFolderOverride ??
            Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".kurukshetra", "tiles");

        public static string TileCachePath(string set, int z, int x, int y) => Path.Combine(CacheFolder, set, z.ToString(), x.ToString(), y + ".png");

        // Where downloaded areas are written: inside the project in the Editor (so a team can share them), next to the
        // player's own data in a build.
        public static string AreasFolder
        {
            get
            {
                if (AreasFolderOverride != null) return AreasFolderOverride;
#if UNITY_EDITOR
                return Path.GetFullPath(Path.Combine(Application.dataPath, "Kurukshetra", "Terrain", "RealData"));
#else
                return Path.Combine(Application.persistentDataPath, "RealData");
#endif
            }
        }

        // --- Data shipped with a build (CRIT-1: "Map offline" on another PC). A post-build step copies the map tile cache and
        // the downloaded areas into <build>/KurukshetraData/, so a copied build has them with no network and no install.
        public const string BundleFolderName = "KurukshetraData";
        public static string BundledFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", BundleFolderName));
        public static string BundledTilePath(string set, int z, int x, int y) => Path.Combine(BundledFolder, "tiles", set, z.ToString(), x.ToString(), y + ".png");
        public static string BundledAreasFolder => Path.Combine(BundledFolder, "RealData");
        public const string ProjectAreasPrefix = "Assets/Kurukshetra/Terrain/RealData/";
        // Areas a trainee PC received from the trainer over the LAN (kept out of the project even in the Editor).
        public static string ReceivedAreasFolder => Path.Combine(Application.persistentDataPath, "ReceivedAreas");

        // Finds a data file wherever it is on this PC: as given, relative to the project (Editor) or the build, and for an
        // area recorded on the trainer's PC (project path, or an absolute path from another PC), in the bundled copy.
        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            string full = Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));
            if (File.Exists(full)) return full;
            string norm = path.Replace("\\", "/");
            int at = norm.IndexOf(ProjectAreasPrefix, StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                string rest = norm.Substring(at + ProjectAreasPrefix.Length);
                foreach (var root in new[] { BundledAreasFolder, AreasFolder, ReceivedAreasFolder })
                {
                    string candidate = Path.GetFullPath(Path.Combine(root, rest));
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return full;
        }

        // The coarse surround of an area is <name>_surround.raw beside it, covering SurroundFactor times the side.
        public const string SurroundSuffix = "_surround";
        public const float SurroundFactor = 4f;
        public static string SurroundPath(string rawPath) => Path.Combine(Path.GetDirectoryName(rawPath), Path.GetFileNameWithoutExtension(rawPath) + SurroundSuffix + ".raw");

        public static string SafeName(string name)
        {
            var chars = (string.IsNullOrWhiteSpace(name) ? "Area" : name.Trim()).ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            return new string(chars);
        }

        // Every area downloaded so far (works with no network).
        public static List<string> DownloadedAreas()
        {
            var list = new List<string>();
            // This PC's own downloads, plus the areas shipped with the build.
            foreach (var folder in new[] { AreasFolder, BundledAreasFolder, ReceivedAreasFolder })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var json in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
                    if (File.Exists(Path.ChangeExtension(json, ".raw")) && !json.EndsWith(SurroundSuffix + ".json", StringComparison.OrdinalIgnoreCase) && !list.Contains(Path.ChangeExtension(json, ".raw"))) list.Add(Path.ChangeExtension(json, ".raw"));
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        // --- Deleting downloaded data. Only paths inside the areas folders or the tile cache are ever touched. ---
        static readonly string[] AreaSuffixes = { "", "_surround", "_satellite", "_surround_satellite", "_osm", "_layers" };

        static bool Inside(string root, string path)
        {
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(path)) return false;
            string r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, '/') + Path.DirectorySeparatorChar;
            string p = Path.GetFullPath(path);
            return p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
        }

        // True when this area may be deleted (it lives in this PC's areas folder or the received folder, not the read-only bundled one).
        public static bool CanDeleteArea(string rawPath) => Inside(AreasFolder, rawPath) || Inside(ReceivedAreasFolder, rawPath);

        // Deletes every file of one area (raw, json, surround, satellite picture, map features, and the .meta files), then its folder if
        // that is left empty. Returns the bytes freed; error is set (and nothing deleted) when the path is not allowed.
        public static long DeleteArea(string rawPath, out string error)
        {
            error = null;
            if (!CanDeleteArea(rawPath)) { error = "Not deleted: this area is not in the downloads folders (bundled areas are read-only)."; return 0; }
            string dir = Path.GetDirectoryName(Path.GetFullPath(rawPath));
            string stem = Path.GetFileNameWithoutExtension(rawPath);
            long freed = 0;
            try
            {
                foreach (var file in Directory.GetFiles(dir))
                {
                    string name = Path.GetFileName(file);
                    string baseName = name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 5) : name;
                    string noExt = Path.GetFileNameWithoutExtension(baseName);
                    bool mine = false;
                    foreach (var suffix in AreaSuffixes) if (string.Equals(noExt, stem + suffix, StringComparison.OrdinalIgnoreCase)) mine = true;
                    if (!mine || !Inside(dir, file)) continue;
                    freed += new FileInfo(file).Length;
                    File.Delete(file);
                }
                bool root = string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, '/'), Path.GetFullPath(AreasFolder).TrimEnd(Path.DirectorySeparatorChar, '/'), StringComparison.OrdinalIgnoreCase)
                         || string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar, '/'), Path.GetFullPath(ReceivedAreasFolder).TrimEnd(Path.DirectorySeparatorChar, '/'), StringComparison.OrdinalIgnoreCase);
                if (!root && Directory.GetFileSystemEntries(dir).Length == 0)
                {
                    Directory.Delete(dir);
                    string folderMeta = dir + ".meta";
                    if (File.Exists(folderMeta)) File.Delete(folderMeta);
                }
            }
            catch (Exception e) { error = "Delete stopped: " + e.Message; }
#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
#endif
            return freed;
        }

        // Deletes the cached map tile pictures (*.png) under CacheFolder. Never .key files, never anything outside that folder.
        public static long ClearTileCache(out int files)
        {
            files = 0;
            long freed = 0;
            if (!Directory.Exists(CacheFolder)) return 0;
            foreach (var file in Directory.GetFiles(CacheFolder, "*.png", SearchOption.AllDirectories))
            {
                if (!Inside(CacheFolder, file)) continue;
                try { freed += new FileInfo(file).Length; File.Delete(file); files++; } catch { }
            }
            return freed;
        }

        public static string ProjectRelative(string absolute)
        {
            string project = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            string p = Path.GetFullPath(absolute).Replace('\\', '/');
            return p.StartsWith(project, StringComparison.OrdinalIgnoreCase) ? p.Substring(project.Length) : p;
        }

        public static IEnumerator Download(AreaRequest req, Action<float, string> progress, Action<AreaDownloadResult> done, DownloadControl control = null)
        {
            var result = new AreaDownloadResult();
            int zoom = req.Zoom(out int count);
            result.zoom = zoom;
            req.Bounds(out double s, out double w, out double n, out double e, 200.0);
            WebMercator.TileRange(s, w, n, e, zoom, out int x0, out int y0, out int x1, out int y1);
            int across = x1 - x0 + 1, down = y1 - y0 + 1;
            var heights = new float[across * WebMercator.TileSize * down * WebMercator.TileSize];
            int stride = across * WebMercator.TileSize;

            int index = 0;
            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++, index++)
            {
                progress?.Invoke(0.9f * index / count, $"Elevation tile {index + 1} of {count} (zoom {zoom})");
                string cache = TileCachePath("terrarium", zoom, tx, ty);
                byte[] png = null;
                if (File.Exists(cache)) { png = File.ReadAllBytes(cache); result.tilesFromCache++; }
                else
                {
                    string url = TileUrl.Replace("{z}", zoom.ToString()).Replace("{x}", tx.ToString()).Replace("{y}", ty.ToString());
                    byte[] got = null; string failure = null;
                    yield return WebFetch.GetBytes(url, UserAgent, 3, 30f, 3f, control,
                        t => progress?.Invoke(0.9f * index / count, $"Elevation tile {index + 1} of {count} (zoom {zoom}): {t}"), b => got = b, f => failure = f);
                    if (got == null)
                    {
                        result.error = control != null && control.Stop ? "Elevation download " + control.StopWord + "."
                            : $"Could not download elevation tile {zoom}/{tx}/{ty}: {failure}. Check the internet connection; tiles already downloaded are kept.";
                        done?.Invoke(result);
                        yield break;
                    }
                    png = got;
                    Directory.CreateDirectory(Path.GetDirectoryName(cache));
                    File.WriteAllBytes(cache, png);
                    result.tilesFetched++;
                }
                if (!DecodeTile(png, heights, stride, (tx - x0) * WebMercator.TileSize, (ty - y0) * WebMercator.TileSize))
                {
                    try { File.Delete(cache); } catch (Exception) { }
                    result.error = $"Elevation tile {zoom}/{tx}/{ty} is not a valid picture; it was removed from the cache. Try again.";
                    done?.Invoke(result);
                    yield break;
                }
            }

            progress?.Invoke(0.92f, "Resampling to a metric grid");
            yield return null;
            try { WriteArea(req, heights, across * WebMercator.TileSize, down * WebMercator.TileSize, x0 * WebMercator.TileSize, y0 * WebMercator.TileSize, zoom, result); }
            catch (Exception ex) { result.error = "Could not write the elevation file: " + ex.Message; }
            progress?.Invoke(1f, result.ok ? "Done" : "Failed");
            done?.Invoke(result);
        }

        // Decodes one 256 x 256 Terrarium PNG into the mosaic at (ox, oy), rows top-down.
        public static bool DecodeTile(byte[] png, float[] mosaic, int stride, int ox, int oy)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (png == null || !tex.LoadImage(png) || tex.width != WebMercator.TileSize || tex.height != WebMercator.TileSize) return false;
                var px = tex.GetPixels32();
                for (int y = 0; y < WebMercator.TileSize; y++)
                {
                    int texRow = WebMercator.TileSize - 1 - y;      // textures are stored bottom row first
                    for (int x = 0; x < WebMercator.TileSize; x++)
                    {
                        var c = px[texRow * WebMercator.TileSize + x];
                        mosaic[(oy + y) * stride + ox + x] = Decode(c.r, c.g, c.b);
                    }
                }
                return true;
            }
            finally { if (Application.isPlaying) UnityEngine.Object.Destroy(tex); else UnityEngine.Object.DestroyImmediate(tex); }
        }

        // Grid size for an area: about two samples per source pixel, a power of two plus one, 257 to 2049.
        public static int GridSize(float sideMetres, double sourceMetresPerPixel)
        {
            double wanted = sideMetres / Math.Max(1.0, sourceMetresPerPixel * 0.5);
            int n = 256;
            while (n < wanted && n < 2048) n *= 2;
            return n + 1;
        }

        // Samples the mosaic on a square local metric grid centred on the request, and writes <name>.raw + <name>.json.
        static void WriteArea(AreaRequest req, float[] mosaic, int width, int height, double originPx, double originPy, int zoom, AreaDownloadResult result)
        {
            result.sourceMetresPerPixel = WebMercator.MetresPerPixel(req.latitude, zoom);
            int size = GridSize(req.SideMetres, result.sourceMetresPerPixel);
            double cell = req.SideMetres / (size - 1);
            result.gridSize = size;
            result.gridMetresPerSample = cell;

            var raw = new byte[size * size * 2];
            float min = float.MaxValue, max = float.MinValue;
            double half = req.SideMetres * 0.5;
            for (int row = 0; row < size; row++)            // north-up
            for (int col = 0; col < size; col++)
            {
                double east = -half + col * cell, north = half - row * cell;
                GeoMath.LocalToGeo(req.latitude, req.longitude, east, north, out double lat, out double lon);
                WebMercator.ToPixel(lat, lon, zoom, out double px, out double py);
                float h = Bilinear(mosaic, width, height, px - originPx - 0.5, py - originPy - 0.5);
                min = Mathf.Min(min, h); max = Mathf.Max(max, h);
                int v = (int)Math.Round((h - Offset) / Scale);
                v = Math.Max(0, Math.Min(65535, v));
                int b = (row * size + col) * 2;
                raw[b] = (byte)(v & 0xFF);
                raw[b + 1] = (byte)(v >> 8);
            }
            result.minElevation = min;
            result.maxElevation = max;

            string name = SafeName(req.name);
            string folder = Path.Combine(AreasFolder, SafeName(string.IsNullOrEmpty(req.folder) ? req.name : req.folder));
            Directory.CreateDirectory(folder);
            string rawPath = Path.Combine(folder, name + ".raw");
            File.WriteAllBytes(rawPath, raw);
            var meta = new DemReader.RawMetadata
            {
                width = size, height = size, littleEndian = true, signed = false,
                centreLatitude = req.latitude, centreLongitude = req.longitude, cellSizeMetres = cell,
                elevationScale = Scale, elevationOffset = Offset, rowOrder = "north-up", crs = "EPSG:4326 (local metric grid around the centre)",
                source = $"{Attribution} Zoom {zoom}, about {result.sourceMetresPerPixel:F0} m per source pixel. Downloaded {DateTime.Now:yyyy-MM-dd}. Centre {req.latitude:F5}, {req.longitude:F5}, radius {req.radiusMetres:F0} m.",
                licence = Licence,
            };
            File.WriteAllText(Path.ChangeExtension(rawPath, ".json"), JsonUtility.ToJson(meta, true));

            result.ok = true;
            result.rawPath = rawPath;
            result.projectRelativePath = ProjectRelative(rawPath);
            result.summary = $"Downloaded {name}: {req.SideMetres / 1000f:F1} km square, {size} x {size} samples every {cell:F1} m, {min:F0} m to {max:F0} m. "
                + $"Source tiles at zoom {zoom}, about {result.sourceMetresPerPixel:F0} m per pixel ({result.tilesFetched} fetched, {result.tilesFromCache} from cache). "
                + "The underlying data is about 30 m in most of the world (SRTM), so finer detail is interpolated.";
        }

        static float Bilinear(float[] m, int w, int h, double x, double y)
        {
            x = Math.Max(0.0, Math.Min(w - 1.0, x));
            y = Math.Max(0.0, Math.Min(h - 1.0, y));
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float tx = (float)(x - x0), ty = (float)(y - y0);
            float top = Mathf.Lerp(m[y0 * w + x0], m[y0 * w + x1], tx);
            float bottom = Mathf.Lerp(m[y1 * w + x0], m[y1 * w + x1], tx);
            return Mathf.Lerp(top, bottom, ty);
        }
    }

    public class ImageryDownloadResult
    {
        public bool ok;
        public string error = "", path = "", summary = "";
        public int zoom, size, tilesFetched, tilesFromCache;
        public double metresPerPixel;
    }

    // A true-colour picture of the area for the terrain surface, from Sentinel-2 cloudless 2016 by EOX (CC BY 4.0,
    // about 10 m). Written as <name>_satellite.jpg + .json (bounds in degrees) next to the elevation, so the terrain
    // generator's existing imagery reader uses it and it works offline afterwards. Tiles are cached like the elevation.
    public static class ImageryDownloader
    {
        public const string TileUrl = "https://tiles.maps.eox.at/wmts/1.0.0/s2cloudless_3857/default/g/{z}/{y}/{x}.jpg";
        public const string Attribution = "Sentinel-2 cloudless 2016 - https://s2maps.eu by EOX IT Services GmbH (contains modified Copernicus Sentinel data 2016 & 2017)";
        public const string Licence = "CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/)";
        const int MaxZoom = 15, MaxTiles = 100;

        public static string PathFor(string rawPath) => Path.Combine(Path.GetDirectoryName(rawPath), Path.GetFileNameWithoutExtension(rawPath) + "_satellite.jpg");

        // Picture size: the source detail (about 4-5 m per pixel at zoom 15 in India) rounded up to a power of two, 512 to 4096.
        public static int PictureSize(float sideMetres, double metresPerPixel)
        {
            int n = 512;
            while (n < sideMetres / metresPerPixel && n < 4096) n *= 2;
            return n;
        }

        public static IEnumerator Download(AreaRequest req, string outPath, Action<float, string> progress, Action<ImageryDownloadResult> done, DownloadControl control = null)
        {
            var result = new ImageryDownloadResult();
            var tileReq = new AreaRequest { name = req.name, latitude = req.latitude, longitude = req.longitude, radiusMetres = req.radiusMetres, maxTiles = MaxTiles, maxZoom = MaxZoom };
            int zoom = tileReq.Zoom(out int count);
            result.zoom = zoom;
            tileReq.Bounds(out double s, out double w, out double n, out double e, 200.0);
            WebMercator.TileRange(s, w, n, e, zoom, out int x0, out int y0, out int x1, out int y1);
            int across = x1 - x0 + 1, down = y1 - y0 + 1, stride = across * WebMercator.TileSize;
            var mosaic = new Color32[stride * down * WebMercator.TileSize];

            int index = 0;
            for (int ty = y0; ty <= y1; ty++)
            for (int tx = x0; tx <= x1; tx++, index++)
            {
                progress?.Invoke(0.9f * index / count, $"Satellite tile {index + 1} of {count}");
                string cache = ElevationDownloader.TileCachePath("s2cloudless-2016", zoom, tx, ty).Replace(".png", ".jpg");
                byte[] jpg = null;
                if (File.Exists(cache)) { jpg = File.ReadAllBytes(cache); result.tilesFromCache++; }
                else
                {
                    string url = TileUrl.Replace("{z}", zoom.ToString()).Replace("{x}", tx.ToString()).Replace("{y}", ty.ToString());
                    byte[] got = null; string failure = null;
                    yield return WebFetch.GetBytes(url, ElevationDownloader.UserAgent, 3, 30f, 3f, control,
                        t => progress?.Invoke(0.9f * index / count, $"Satellite tile {index + 1} of {count}: {t}"), b => got = b, f => failure = f);
                    if (got == null)
                    {
                        result.error = control != null && control.Stop ? "Satellite picture " + control.StopWord + ". The terrain is still built, coloured by its materials."
                            : $"Could not download satellite tile {zoom}/{tx}/{ty}: {failure}. The terrain is still built, coloured by its materials.";
                        done?.Invoke(result);
                        yield break;
                    }
                    jpg = got;
                    Directory.CreateDirectory(Path.GetDirectoryName(cache));
                    File.WriteAllBytes(cache, jpg);
                    result.tilesFetched++;
                }
                if (!PasteTile(jpg, mosaic, stride, (tx - x0) * WebMercator.TileSize, (ty - y0) * WebMercator.TileSize))
                {
                    try { File.Delete(cache); } catch (Exception) { }
                    result.error = $"Satellite tile {zoom}/{tx}/{ty} is not a valid picture; removed from the cache.";
                    done?.Invoke(result);
                    yield break;
                }
            }

            progress?.Invoke(0.92f, "Fitting the picture to the terrain");
            yield return null;
            // Up to 4096 x 4096 samples with map-projection maths each: done on a worker thread so the window keeps painting
            // (on the main thread this froze the menu for many seconds at the end of the satellite steps).
            Color32[] fitted = null;
            string fitError = null;
            int fitWidth = stride, fitHeight = down * WebMercator.TileSize;
            double fitOx = x0 * WebMercator.TileSize, fitOy = y0 * WebMercator.TileSize;
            var fitTask = System.Threading.Tasks.Task.Run(() =>
            {
                try { fitted = Sample(req, mosaic, fitWidth, fitHeight, fitOx, fitOy, zoom, result); }
                catch (Exception ex) { fitError = ex.Message; }
            });
            while (!fitTask.IsCompleted) yield return null;
            if (fitted == null) result.error = "Could not fit the satellite picture: " + fitError;
            else
            {
                try { Write(req, fitted, zoom, outPath, result); }
                catch (Exception ex) { result.error = "Could not write the satellite picture: " + ex.Message; }
            }
            progress?.Invoke(1f, result.ok ? "Done" : "Failed");
            done?.Invoke(result);
        }

        public static bool PasteTile(byte[] image, Color32[] mosaic, int stride, int ox, int oy)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
            try
            {
                if (image == null || !tex.LoadImage(image) || tex.width != WebMercator.TileSize || tex.height != WebMercator.TileSize) return false;
                var px = tex.GetPixels32();
                for (int y = 0; y < WebMercator.TileSize; y++)
                {
                    int row = WebMercator.TileSize - 1 - y;         // textures are stored bottom row first
                    Array.Copy(px, row * WebMercator.TileSize, mosaic, (oy + y) * stride + ox, WebMercator.TileSize);
                }
                return true;
            }
            finally { if (Application.isPlaying) UnityEngine.Object.Destroy(tex); else UnityEngine.Object.DestroyImmediate(tex); }
        }

        // Pure maths (no Unity objects): safe on a worker thread.
        static Color32[] Sample(AreaRequest req, Color32[] mosaic, int width, int height, double originPx, double originPy, int zoom, ImageryDownloadResult result)
        {
            result.metresPerPixel = WebMercator.MetresPerPixel(req.latitude, zoom);
            int size = PictureSize(req.SideMetres, result.metresPerPixel);
            result.size = size;
            var pixels = new Color32[size * size];
            double half = req.SideMetres * 0.5, cell = req.SideMetres / size;
            for (int row = 0; row < size; row++)                 // texture row 0 = south edge
            for (int col = 0; col < size; col++)
            {
                double east = -half + (col + 0.5) * cell, north = -half + (row + 0.5) * cell;
                GeoMath.LocalToGeo(req.latitude, req.longitude, east, north, out double lat, out double lon);
                WebMercator.ToPixel(lat, lon, zoom, out double px, out double py);
                pixels[row * size + col] = Bilinear(mosaic, width, height, px - originPx - 0.5, py - originPy - 0.5);
            }
            return pixels;
        }

        static void Write(AreaRequest req, Color32[] pixels, int zoom, string outPath, ImageryDownloadResult result)
        {
            int size = result.size;
            double half = req.SideMetres * 0.5;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            try
            {
                tex.SetPixels32(pixels);
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, tex.EncodeToJPG(92));
            }
            finally { if (Application.isPlaying) UnityEngine.Object.Destroy(tex); else UnityEngine.Object.DestroyImmediate(tex); }

            GeoMath.LocalToGeo(req.latitude, req.longitude, -half, -half, out double south, out double west);
            GeoMath.LocalToGeo(req.latitude, req.longitude, half, half, out double northEdge, out double east2);
            var meta = new ImageryReader.Metadata { west = west, south = south, east = east2, north = northEdge, source = $"{Attribution}. Zoom {zoom}, about {result.metresPerPixel:F1} m per source pixel; the data itself is 10 m. Downloaded {DateTime.Now:yyyy-MM-dd}.", licence = Licence };
            File.WriteAllText(Path.ChangeExtension(outPath, ".json"), JsonUtility.ToJson(meta, true));
            result.ok = true;
            result.path = outPath;
            result.summary = $"Satellite picture {size} x {size} ({req.SideMetres / size:F1} m per pixel; Sentinel-2 data is 10 m) from zoom {zoom} ({result.tilesFetched} tiles fetched, {result.tilesFromCache} from cache).";
        }

        static Color32 Bilinear(Color32[] m, int w, int h, double x, double y)
        {
            x = Math.Max(0.0, Math.Min(w - 1.0, x));
            y = Math.Max(0.0, Math.Min(h - 1.0, y));
            int x0 = (int)x, y0 = (int)y, x1 = Math.Min(x0 + 1, w - 1), y1 = Math.Min(y0 + 1, h - 1);
            float tx = (float)(x - x0), ty = (float)(y - y0);
            Color a = Color32.Lerp(m[y0 * w + x0], m[y0 * w + x1], tx), b = Color32.Lerp(m[y1 * w + x0], m[y1 * w + x1], tx);
            return Color.Lerp(a, b, ty);
        }
    }
}
