using System.Collections.Generic;
using System.IO;
using Kurukshetra.TerrainGen;
using Kurukshetra.Trainer;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Kurukshetra.TerrainGen.EditorTools
{
    // CRIT-1 (second pass): a build only ships the map tiles cached on the PC that made it. This menu item fills the tile
    // cache around every downloaded area (map and 10 m satellite), plus a coarse overview of India, so the next build's
    // KurukshetraData/tiles/ gives a usable map offline. It downloads only when clicked (never at build time), one tile at a
    // time with the project's User-Agent, at most MaxTiles per click, skipping tiles already cached.
    public static class PrefetchMapTiles
    {
        const int MaxTiles = 1500;
        static readonly Queue<(string set, string url, int z, int x, int y)> queue = new Queue<(string, string, int, int, int)>();
        static UnityWebRequest current;
        static (string set, string url, int z, int x, int y) currentTile;
        static int done, failed, total;
        static double nextAt;

        [MenuItem("Kurukshetra/Offline map/Prefetch map tiles around downloaded areas")]
        static void Prefetch()
        {
            if (queue.Count > 0 || current != null) { Debug.Log("[Map] A prefetch is already running."); return; }
            var areas = OfflineAreas.Scan(new[] { ElevationDownloader.AreasFolder });
            var plan = Plan(areas);
            if (plan.Count == 0) { Debug.Log("[Map] Every tile for the downloaded areas is already cached."); return; }
            if (!EditorUtility.DisplayDialog("Prefetch map tiles",
                $"Download {plan.Count} map tiles (OpenStreetMap and Sentinel-2 cloudless) around {areas.Count} area pictures into {ElevationDownloader.CacheFolder}?\n\nThey are copied into every Windows build so the map works offline.", "Download", "Cancel")) return;
            foreach (var t in plan) queue.Enqueue(t);
            done = failed = 0;
            total = queue.Count;
            EditorApplication.update += Step;
        }

        public static List<(string set, string url, int z, int x, int y)> Plan(List<OfflineAreas.Picture> areas)
        {
            var list = new List<(string, string, int, int, int)>();
            var seen = new HashSet<string>();
            void Cover(double south, double west, double north, double east, int z0, int z1)
            {
                for (int z = z0; z <= z1 && list.Count < MaxTiles; z++)
                {
                    WebMercator.ToPixel(north, west, z, out double px0, out double py0);
                    WebMercator.ToPixel(south, east, z, out double px1, out double py1);
                    int x0 = (int)(px0 / WebMercator.TileSize), x1 = (int)(px1 / WebMercator.TileSize);
                    int y0 = (int)(py0 / WebMercator.TileSize), y1 = (int)(py1 / WebMercator.TileSize);
                    for (int x = x0; x <= x1; x++)
                    for (int y = y0; y <= y1; y++)
                    {
                        Add("osm", MapPicker.OsmUrl, z, x, y);
                        if (z <= 15) Add("s2cloudless-2016", MapPicker.Sentinel2Url, z, x, y);
                    }
                }
            }
            void Add(string set, string url, int z, int x, int y)
            {
                if (list.Count >= MaxTiles || !seen.Add(set + z + "/" + x + "/" + y)) return;
                if (File.Exists(ElevationDownloader.TileCachePath(set, z, x, y))) return;
                list.Add((set, url, z, x, y));
            }
            Cover(6.0, 68.0, 37.0, 98.0, 3, 6);             // India overview
            foreach (var a in areas)
            {
                if (a.surround) Cover(a.south, a.west, a.north, a.east, 7, 13);
                else Cover(a.south, a.west, a.north, a.east, 12, 16);
            }
            return list;
        }

        static void Step()
        {
            if (current != null)
            {
                if (!current.isDone) return;
                if (current.result == UnityWebRequest.Result.Success)
                {
                    string path = ElevationDownloader.TileCachePath(currentTile.set, currentTile.z, currentTile.x, currentTile.y);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, current.downloadHandler.data);
                    done++;
                }
                else
                {
                    failed++;
                    if (current.result == UnityWebRequest.Result.ConnectionError) { Debug.LogWarning("[Map] Prefetch stopped: no connection (" + current.error + ")."); queue.Clear(); }
                }
                current.Dispose();
                current = null;
                nextAt = EditorApplication.timeSinceStartup + 0.15;     // polite: about 6 tiles a second, one at a time
            }
            if (queue.Count == 0)
            {
                EditorApplication.update -= Step;
                EditorUtility.ClearProgressBar();
                Debug.Log($"[Map] Prefetch finished: {done} tiles saved, {failed} failed. Make a new build to ship them.");
                return;
            }
            if (EditorUtility.DisplayCancelableProgressBar("Prefetch map tiles", $"{done + failed} of {total}", (done + failed) / (float)Mathf.Max(1, total)))
            {
                queue.Clear();
                return;
            }
            if (EditorApplication.timeSinceStartup < nextAt) return;
            currentTile = queue.Dequeue();
            current = UnityWebRequest.Get(MapPicker.TileUrl(currentTile.url, currentTile.z, currentTile.x, currentTile.y));
            current.SetRequestHeader("User-Agent", ElevationDownloader.UserAgent);
            current.timeout = 20;
            current.SendWebRequest();
        }
    }
}
