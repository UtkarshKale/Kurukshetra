using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Kurukshetra.TerrainGen
{
    // What the terrain layers (trees, grass, buildings) read for a real place: OpenStreetMap features around the area,
    // already in the terrain's own metres (east / north of the area centre). Saved next to the area as <name>_layers.json.
    [Serializable] public class LayerRing { public float[] p; }

    [Serializable]
    public class LayerPoly
    {
        public float[] p;                                   // x, z pairs of the outer ring, counter-clockwise seen from above
        public List<LayerRing> holes = new List<LayerRing>();   // clockwise
        public string kind = "";                            // forest, wood, orchard, grass, meadow, recreation_ground, village_green, park, farmland
    }

    [Serializable]
    public class LayerBuilding
    {
        public float[] p;
        public List<LayerRing> holes = new List<LayerRing>();
        public float height = 6f;
        public float minHeight;
        public string type = "generic";                     // house, apartments, commercial, industrial, religious, shed, generic
    }

    [Serializable] public class LayerPoint { public float x, z; }
    [Serializable] public class LayerLine { public float[] p; }
    [Serializable] public class LayerFarBuilding { public float x, z, height = 6f; public string type = "generic"; }

    [Serializable]
    public class LayerFile
    {
        public const string OsmSource = "OpenStreetMap contributors, ODbL 1.0";
        public const string OsmCredit = "Map features © OpenStreetMap contributors (ODbL)";

        public string source = OsmSource;
        public string fetched = "";
        public double centreLatitude, centreLongitude;
        public float playHalfMetres, surroundHalfMetres;
        public bool surroundOk;
        public string note = "";                            // failures and anything else worth saying
        public int buildingCount, treeCount, treeRowCount, forestCount, grassCount, farBuildingCount;
        public List<LayerBuilding> buildings = new List<LayerBuilding>();
        public List<LayerPoint> trees = new List<LayerPoint>();
        public List<LayerLine> treeRows = new List<LayerLine>();
        public List<LayerPoly> forests = new List<LayerPoly>();
        public List<LayerPoly> grass = new List<LayerPoly>();
        public List<LayerFarBuilding> farBuildings = new List<LayerFarBuilding>();

        public void UpdateCounts()
        {
            buildingCount = buildings.Count; treeCount = trees.Count; treeRowCount = treeRows.Count;
            forestCount = forests.Count; grassCount = grass.Count; farBuildingCount = farBuildings.Count;
        }

        public string CountsLine()
        {
            return $"{buildings.Count} buildings, {trees.Count} trees, {treeRows.Count} tree rows, {forests.Count} forest / wood polygons, {grass.Count} grass / farmland polygons, {farBuildings.Count} far buildings";
        }

        public static string PathFor(string rawPath) => Path.Combine(Path.GetDirectoryName(rawPath), Path.GetFileNameWithoutExtension(rawPath) + "_layers.json");
        public static string RawPathFor(string rawPath) => Path.Combine(Path.GetDirectoryName(rawPath), Path.GetFileNameWithoutExtension(rawPath) + "_osm.json");
        public static string RawSurroundPathFor(string rawPath) => Path.Combine(Path.GetDirectoryName(rawPath), Path.GetFileNameWithoutExtension(rawPath) + "_osm_surround.json");

        // The layers file of an area, or null when the area has none (or it cannot be read).
        public static LayerFile Load(string rawPath)
        {
            try
            {
                string path = PathFor(ElevationDownloader.Resolve(rawPath));
                if (!File.Exists(path)) return null;
                var file = JsonUtility.FromJson<LayerFile>(File.ReadAllText(path));
                return file;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Layers] Could not read the layers file for " + rawPath + ": " + e.Message);
                return null;
            }
        }

        // One line about an area's layers file, read from its first few hundred characters (cheap, whatever the file's size).
        // Empty when the area has no layers file.
        public static string Describe(string rawPath)
        {
            try
            {
                string path = PathFor(ElevationDownloader.Resolve(rawPath));
                if (!File.Exists(path)) return "";
                char[] buffer = new char[1400];
                int n;
                using (var r = new StreamReader(path)) n = r.Read(buffer, 0, buffer.Length);
                string head = new string(buffer, 0, n);
                string Num(string key) { var m = System.Text.RegularExpressions.Regex.Match(head, "\"" + key + "\":(\\d+)"); return m.Success ? m.Groups[1].Value : "?"; }
                var f = System.Text.RegularExpressions.Regex.Match(head, "\"fetched\":\"([^\"]*)\"");
                return $"OpenStreetMap data fetched {(f.Success ? f.Groups[1].Value : "?")}: {Num("buildingCount")} buildings, {Num("treeCount")} trees, {Num("forestCount")} forest polygons, {Num("grassCount")} grass / farmland polygons, {Num("farBuildingCount")} far buildings.";
            }
            catch (Exception) { return ""; }
        }

        public static bool Exists(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return false;
            try { return File.Exists(PathFor(ElevationDownloader.Resolve(rawPath))); } catch (Exception) { return false; }
        }
    }

    // Geographic degrees to the terrain's own metres: the same local metric the terrain and its picture use.
    public class GeoFrame
    {
        public readonly double lat0, lon0, mLat, mLon;
        public GeoFrame(double centreLatitude, double centreLongitude)
        {
            lat0 = centreLatitude; lon0 = centreLongitude;
            mLat = GeoMath.MetresPerDegreeLatitude(lat0);
            mLon = GeoMath.MetresPerDegreeLongitude(lat0);
        }
        public float X(double lon) => (float)((lon - lon0) * mLon);
        public float Z(double lat) => (float)((lat - lat0) * mLat);
        public void Bounds(float halfMetres, out double south, out double west, out double north, out double east)
        {
            GeoMath.LocalToGeo(lat0, lon0, -halfMetres, -halfMetres, out south, out west);
            GeoMath.LocalToGeo(lat0, lon0, halfMetres, halfMetres, out north, out east);
        }
    }

    // A small, forgiving JSON reader. Overpass answers mix shapes (tags are free-form keys), which JsonUtility cannot read.
    // Objects become Dictionary<string, object>, arrays List<object>, numbers double, plus string, bool and null.
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            var reader = new Reader { s = text };
            reader.SkipSpace();
            var v = reader.Value();
            return v;
        }

        public static Dictionary<string, object> Obj(object o) => o as Dictionary<string, object>;
        public static List<object> Arr(object o) => o as List<object>;
        public static string Str(object o) => o as string;
        public static double Num(object o, double fallback = double.NaN) => o is double d ? d : fallback;
        public static object Get(Dictionary<string, object> d, string key) => d != null && d.TryGetValue(key, out var v) ? v : null;

        sealed class Reader
        {
            public string s;
            int i;

            public void SkipSpace()
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++;
            }

            public object Value()
            {
                SkipSpace();
                if (i >= s.Length) throw new FormatException("JSON ends early");
                char c = s[i];
                if (c == '{') return Object();
                if (c == '[') return Array();
                if (c == '"') return String();
                if (c == 't') { i += 4; return true; }
                if (c == 'f') { i += 5; return false; }
                if (c == 'n') { i += 4; return null; }
                return Number();
            }

            Dictionary<string, object> Object()
            {
                var d = new Dictionary<string, object>();
                i++;
                SkipSpace();
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipSpace();
                    string key = String();
                    SkipSpace();
                    if (s[i] != ':') throw new FormatException("JSON: expected ':' at " + i);
                    i++;
                    d[key] = Value();
                    SkipSpace();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("JSON: expected ',' or '}' at " + i);
                }
            }

            List<object> Array()
            {
                var list = new List<object>();
                i++;
                SkipSpace();
                if (s[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Add(Value());
                    SkipSpace();
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return list; }
                    throw new FormatException("JSON: expected ',' or ']' at " + i);
                }
            }

            string String()
            {
                if (s[i] != '"') throw new FormatException("JSON: expected a string at " + i);
                i++;
                int start = i;
                while (i < s.Length && s[i] != '"' && s[i] != '\\') i++;
                if (s[i] == '"') { string plain = s.Substring(start, i - start); i++; return plain; }
                var sb = new StringBuilder();
                sb.Append(s, start, i - start);
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u': sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                throw new FormatException("JSON: unterminated string");
            }

            double Number()
            {
                int start = i;
                bool negative = false;
                if (s[i] == '-') { negative = true; i++; }
                double value = 0.0;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') { value = value * 10.0 + (s[i] - '0'); i++; }
                if (i < s.Length && s[i] == '.')
                {
                    i++;
                    double scale = 0.1;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') { value += (s[i] - '0') * scale; scale *= 0.1; i++; }
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    // Rare in OSM: let the library do the exponent form exactly.
                    i = start;
                    while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                    return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
                }
                if (i == start) throw new FormatException("JSON: unexpected character '" + s[i] + "' at " + i);
                return negative ? -value : value;
            }
        }
    }
}
