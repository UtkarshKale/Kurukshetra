using System;
using System.Collections.Generic;
using System.IO;
using Chaukanna.TerrainGen;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // CRIT-1 (second pass): a build copied to another PC showed an empty map, because the build only carries the map tiles
    // that happened to be cached on the PC that made it (on the new dev PC: 9 tiles at one zoom). Every downloaded area
    // already ships its own satellite pictures with their bounds (<area>_satellite.jpg + .json, and a 20 km
    // <area>_surround_satellite.jpg), so the map draws those wherever it has no tile: the areas shipped with the build are
    // always visible and pickable with no network. Pictures are loaded only when they come on screen.
    public static class OfflineAreas
    {
        [Serializable] class Bounds { public double west, south, east, north; }

        public class Picture
        {
            public string area, path;
            public double west, south, east, north;
            public bool surround;
            public Texture2D texture;
            public bool failed;
            public double CentreLat => (north + south) * 0.5;
            public double CentreLon => (east + west) * 0.5;
        }

        static List<Picture> pictures;
        static float scannedAt = -100f;

        // Every georeferenced area picture on this PC: this PC's downloads, the build's bundled copy, areas received over the
        // LAN. Surrounds first, so the sharper play-area picture is drawn on top. Rescanned at most every 30 s.
        public static List<Picture> All
        {
            get
            {
                if (pictures != null && Time.realtimeSinceStartup - scannedAt < 30f) return pictures;
                scannedAt = Time.realtimeSinceStartup;
                var old = pictures;
                pictures = Scan(new[] { ElevationDownloader.AreasFolder, ElevationDownloader.BundledAreasFolder, ElevationDownloader.ReceivedAreasFolder });
                if (old != null)
                    foreach (var p in pictures)
                    {
                        var same = old.Find(o => o.path == p.path);
                        if (same != null) { p.texture = same.texture; p.failed = same.failed; }
                    }
                return pictures;
            }
        }

        public static List<Picture> Scan(IEnumerable<string> folders)
        {
            var list = new List<Picture>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                string[] metas;
                try { metas = Directory.GetFiles(folder, "*_satellite.json", SearchOption.AllDirectories); }
                catch (Exception) { continue; }
                foreach (var meta in metas)
                {
                    string image = FindImage(meta);
                    if (image == null) continue;
                    string key = Path.GetFileName(image);           // the same area found in two folders: keep the first
                    if (!seen.Add(key)) continue;
                    Bounds b;
                    try { b = JsonUtility.FromJson<Bounds>(File.ReadAllText(meta)); }
                    catch (Exception) { continue; }
                    if (b == null || !(b.east > b.west) || !(b.north > b.south)) continue;
                    string name = Path.GetFileNameWithoutExtension(meta).Replace("_satellite", "");
                    bool surround = name.EndsWith(ElevationDownloader.SurroundSuffix, StringComparison.OrdinalIgnoreCase);
                    if (surround) name = name.Substring(0, name.Length - ElevationDownloader.SurroundSuffix.Length);
                    list.Add(new Picture { area = name, path = image, west = b.west, south = b.south, east = b.east, north = b.north, surround = surround });
                }
            }
            list.Sort((a, b) => a.surround == b.surround ? string.Compare(a.area, b.area, StringComparison.OrdinalIgnoreCase) : a.surround ? -1 : 1);
            return list;
        }

        static string FindImage(string meta)
        {
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
            {
                string p = Path.ChangeExtension(meta, ext);
                if (File.Exists(p)) return p;
            }
            return null;
        }

        public static int AreaCount
        {
            get
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in All) names.Add(p.area);
                return names.Count;
            }
        }

        // The picture's rectangle in world pixels at this zoom (x, y = west and north edges).
        public static Rect PixelRect(Picture p, int zoom)
        {
            WebMercator.ToPixel(p.north, p.west, zoom, out double x0, out double y0);
            WebMercator.ToPixel(p.south, p.east, zoom, out double x1, out double y1);
            return new Rect((float)x0, (float)y0, (float)(x1 - x0), (float)(y1 - y0));
        }

        public static Texture2D Texture(Picture p)
        {
            if (p.texture != null || p.failed) return p.texture;
            try
            {
                var tex = new Texture2D(2, 2, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, name = "Offline " + p.area };
                if (tex.LoadImage(File.ReadAllBytes(p.path))) p.texture = tex;
                else { UnityEngine.Object.Destroy(tex); p.failed = true; }
            }
            catch (Exception) { p.failed = true; }
            return p.texture;
        }

        // Draws the parts of the area pictures that fall inside `cell` (a map cell in group coordinates; `cellWorld` is the
        // same cell in world pixels). True if anything was drawn.
        public static bool DrawInto(Rect cell, Rect cellWorld, int zoom, bool paint)
        {
            bool any = false;
            foreach (var p in All)
            {
                Rect pr = PixelRect(p, zoom);
                // The world wraps east-west; the shipped areas never straddle the date line, so one copy is enough.
                float x0 = Mathf.Max(pr.xMin, cellWorld.xMin), x1 = Mathf.Min(pr.xMax, cellWorld.xMax);
                float y0 = Mathf.Max(pr.yMin, cellWorld.yMin), y1 = Mathf.Min(pr.yMax, cellWorld.yMax);
                if (x1 <= x0 || y1 <= y0) continue;
                any = true;
                if (!paint) continue;
                var tex = Texture(p);
                if (tex == null) continue;
                var dest = new Rect(cell.x + (x0 - cellWorld.x), cell.y + (y0 - cellWorld.y), x1 - x0, y1 - y0);
                // Texture v runs bottom-up; the picture is north-up, so the north edge is v = 1.
                float u0 = (x0 - pr.xMin) / pr.width, u1 = (x1 - pr.xMin) / pr.width;
                float v1 = 1f - (y0 - pr.yMin) / pr.height, v0 = 1f - (y1 - pr.yMin) / pr.height;
                GUI.DrawTextureWithTexCoords(dest, tex, new Rect(u0, v0, u1 - u0, v1 - v0), false);
            }
            return any;
        }

        public static void Unload()
        {
            if (pictures == null) return;
            foreach (var p in pictures) if (p.texture != null) { UnityEngine.Object.Destroy(p.texture); p.texture = null; }
        }
    }
}
