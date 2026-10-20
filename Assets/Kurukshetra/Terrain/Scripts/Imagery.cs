using System;
using System.IO;
using UnityEngine;

namespace Kurukshetra.TerrainGen
{
    // Layer C input: a picture of the ground (satellite or aerial true colour), kept apart from elevation.
    // It only ever colours the terrain. Its brightness is never read as height.
    public class Imagery
    {
        public Texture2D texture;
        public bool georeferenced;                  // false = no usable bounds; the picture is stretched over the whole area
        public double west, south, east, north;     // degrees, outer edges of the picture
        public string sourceName = "", source = "", licence = "";

        public string Describe()
        {
            return $"{sourceName}: {texture.width} x {texture.height} px. "
                + (georeferenced ? $"Bounds {south:F5} to {north:F5} N, {west:F5} to {east:F5} E." : "No bounds given: stretched over the whole terrain.")
                + (string.IsNullOrEmpty(source) ? "" : " Source: " + source + ".") + (string.IsNullOrEmpty(licence) ? "" : " Licence: " + licence + ".");
        }
    }

    public static class ImageryReader
    {
        [Serializable]
        public class Metadata
        {
            public double west, south, east, north;     // degrees (EPSG:4326), outer edges, north up
            public string source = "", licence = "";
        }

        // PNG or JPG, north up, plus an optional <name>.json with its bounds in degrees.
        public static Imagery Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new DemException("No imagery file was chosen.");
            path = ElevationDownloader.Resolve(path);
            if (!File.Exists(path)) throw new DemException("Imagery file not found: " + path);
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".tif" || ext == ".tiff" || ext == ".jp2")
                throw new DemException("Imagery must be an 8-bit PNG or JPG. Convert it first, clipped to the same area as the elevation:\n"
                    + "  gdal_translate -of PNG -ot Byte -scale input.tif imagery.png\nSee the terrain README.");
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg") throw new DemException($"Unsupported imagery format '{ext}'. Use PNG or JPG.");

            var tex = new Texture2D(2, 2, TextureFormat.RGB24, true) { wrapMode = TextureWrapMode.Clamp, name = Path.GetFileNameWithoutExtension(path) };
            if (!tex.LoadImage(File.ReadAllBytes(path)) || tex.width < 4 || tex.height < 4)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(tex); else UnityEngine.Object.DestroyImmediate(tex);
                throw new DemException("This file could not be read as a PNG or JPG picture: " + Path.GetFileName(path));
            }

            var imagery = new Imagery { texture = tex, sourceName = Path.GetFileName(path) };
            string metaPath = Path.ChangeExtension(path, ".json");
            if (File.Exists(metaPath))
            {
                Metadata m;
                try { m = JsonUtility.FromJson<Metadata>(File.ReadAllText(metaPath)); }
                catch (Exception e) { throw new DemException("Cannot read " + Path.GetFileName(metaPath) + ": " + e.Message); }
                if (m != null && m.east > m.west && m.north > m.south && Math.Abs(m.north) <= 90.0 && Math.Abs(m.south) <= 90.0)
                {
                    imagery.georeferenced = true;
                    imagery.west = m.west; imagery.south = m.south; imagery.east = m.east; imagery.north = m.north;
                }
                else throw new DemException(Path.GetFileName(metaPath) + " must give west, south, east and north in degrees, with east > west and north > south.");
                imagery.source = m.source ?? "";
                imagery.licence = m.licence ?? "";
            }
            return imagery;
        }

        // Where the picture sits on the terrain, in metres east/north of the area centre (x, y = west and south edges).
        // With a georeferenced picture and a real-world mode it is placed by its coordinates; otherwise it covers the whole area.
        public static Rect Extent(TerrainGenConfig cfg, ElevationGrid dem, Imagery imagery)
        {
            float size = cfg.sizeMetres;
            if (imagery == null || !imagery.georeferenced || !cfg.UsesDem || dem == null) return new Rect(-size * 0.5f, -size * 0.5f, size, size);
            double lat = cfg.useFileCentre ? dem.CentreLatitude : cfg.centreLatitude, lon = cfg.useFileCentre ? dem.CentreLongitude : cfg.centreLongitude;
            double mLat = GeoMath.MetresPerDegreeLatitude(lat), mLon = GeoMath.MetresPerDegreeLongitude(lat);
            float w = (float)((imagery.west - lon) * mLon), e = (float)((imagery.east - lon) * mLon);
            float s = (float)((imagery.south - lat) * mLat), n = (float)((imagery.north - lat) * mLat);
            return new Rect(w, s, e - w, n - s);
        }

        // Share of the terrain area the picture covers, 0 to 1.
        public static float Coverage(TerrainGenConfig cfg, Rect extent)
        {
            float half = cfg.sizeMetres * 0.5f;
            float w = Mathf.Max(0f, Mathf.Min(half, extent.xMax) - Mathf.Max(-half, extent.xMin));
            float h = Mathf.Max(0f, Mathf.Min(half, extent.yMax) - Mathf.Max(-half, extent.yMin));
            return w * h / (cfg.sizeMetres * cfg.sizeMetres);
        }
    }
}
