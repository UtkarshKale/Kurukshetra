using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Chaukanna.TerrainGen
{
    // OpenStreetMap -> LayerFile. Reads an Overpass JSON answer (already parsed by MiniJson) and keeps what the terrain
    // layers draw: building footprints with heights, single trees, tree rows, forest / wood / orchard and grass / park /
    // farmland polygons. Everything is converted to the terrain's own metres around the area centre.
    public static class OsmParser
    {
        // Heights when the map has neither a height nor a number of levels.
        public static float DefaultHeight(string type)
        {
            switch (type)
            {
                case "house": return 6f;
                case "apartments": return 12f;
                case "commercial": return 10f;
                case "industrial": return 8f;
                case "religious": return 10f;
                case "shed": return 3f;
                default: return 6f;
            }
        }

        // OSM building=* value to the kit's building type.
        public static string BuildingType(string value)
        {
            switch ((value ?? "").ToLowerInvariant())
            {
                case "house": case "detached": case "semidetached_house": case "terrace": case "residential": case "bungalow": case "farm": case "dormitory": case "static_caravan": return "house";
                case "apartments": return "apartments";
                case "commercial": case "retail": case "office": case "hotel": case "supermarket": case "kiosk": case "mall": return "commercial";
                case "industrial": case "warehouse": case "factory": case "manufacture": return "industrial";
                case "church": case "mosque": case "temple": case "cathedral": case "chapel": case "synagogue": case "shrine": case "religious": case "gurdwara": return "religious";
                case "shed": case "garage": case "garages": case "carport": case "roof": case "hut": case "cabin": case "barn": case "stable": case "greenhouse": case "service": case "farm_auxiliary": return "shed";
                default: return "generic";
            }
        }

        // "12", "12.5 m", "12 metres": metres, or NaN when it is not a plain length.
        public static float ParseMetres(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return float.NaN;
            text = text.Trim().ToLowerInvariant().Replace(',', '.');
            int i = 0;
            while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.' || (i == 0 && text[i] == '-'))) i++;
            if (i == 0 || !double.TryParse(text.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)) return float.NaN;
            string unit = text.Substring(i).Trim();
            if (unit.StartsWith("ft") || unit.StartsWith("'")) v *= 0.3048;
            else if (unit.Length > 0 && !unit.StartsWith("m")) return float.NaN;
            return v >= 0.0 && v < 1000.0 ? (float)v : float.NaN;
        }

        public static float HeightOf(Dictionary<string, object> tags, string type)
        {
            float h = ParseMetres(MiniJson.Str(MiniJson.Get(tags, "height")));
            if (!float.IsNaN(h) && h > 0.5f) return h;
            float levels = ParseMetres(MiniJson.Str(MiniJson.Get(tags, "building:levels")));
            if (!float.IsNaN(levels) && levels > 0f) return levels * 3.2f;
            return DefaultHeight(type);
        }

        // Parses one Overpass answer into `into`. `playHalf` is the half side of the play area; with `farOnly` the answer is
        // the surround query: buildings arrive as centres (far buildings outside the play area) and landcover as geometry.
        // `seen` carries element ids between the two answers, so a polygon in both is kept once.
        public static void Parse(object root, GeoFrame frame, LayerFile into, float playHalf, bool surroundAnswer, HashSet<string> seen)
        {
            var elements = MiniJson.Arr(MiniJson.Get(MiniJson.Obj(root), "elements"));
            if (elements == null) throw new FormatException("The answer has no 'elements' list.");
            var parts = new List<LayerBuilding>();
            var outlines = new List<LayerBuilding>();

            foreach (var o in elements)
            {
                var el = MiniJson.Obj(o);
                if (el == null) continue;
                string type = MiniJson.Str(MiniJson.Get(el, "type")) ?? "";
                string id = type + "/" + ((long)MiniJson.Num(MiniJson.Get(el, "id"), 0.0));
                var tags = MiniJson.Obj(MiniJson.Get(el, "tags")) ?? new Dictionary<string, object>();
                if (!seen.Add(id)) continue;

                if (type == "node")
                {
                    if (Tag(tags, "natural") == "tree")
                        into.trees.Add(new LayerPoint { x = R(frame.X(MiniJson.Num(MiniJson.Get(el, "lon")))), z = R(frame.Z(MiniJson.Num(MiniJson.Get(el, "lat")))) });
                    continue;
                }

                bool isBuilding = tags.ContainsKey("building") && Tag(tags, "building") != "no";
                bool isPart = tags.ContainsKey("building:part") && Tag(tags, "building:part") != "no";
                if (isBuilding || isPart)
                {
                    string btype = BuildingType(isBuilding ? Tag(tags, "building") : "yes");
                    var centre = MiniJson.Obj(MiniJson.Get(el, "center"));
                    if (surroundAnswer && centre != null)
                    {
                        float cx = frame.X(MiniJson.Num(MiniJson.Get(centre, "lon"))), cz = frame.Z(MiniJson.Num(MiniJson.Get(centre, "lat")));
                        if (Mathf.Abs(cx) > playHalf || Mathf.Abs(cz) > playHalf)
                            into.farBuildings.Add(new LayerFarBuilding { x = R(cx), z = R(cz), height = HeightOf(tags, btype), type = btype });
                        continue;
                    }
                    foreach (var poly in Polygons(el, type, frame))
                    {
                        var b = new LayerBuilding { p = poly.p, holes = poly.holes, type = btype, height = HeightOf(tags, btype) };
                        float mh = ParseMetres(MiniJson.Str(MiniJson.Get(tags, "min_height")));
                        if (!float.IsNaN(mh) && mh > 0f && mh < b.height) b.minHeight = mh;
                        if (Mathf.Abs(LayerGeometry.SignedArea(b.p)) < 3f) continue;
                        (isBuilding ? outlines : parts).Add(b);
                    }
                    continue;
                }

                if (type == "way" && Tag(tags, "natural") == "tree_row")
                {
                    var line = Points(MiniJson.Arr(MiniJson.Get(el, "geometry")), frame);
                    if (line != null && line.Length >= 4) into.treeRows.Add(new LayerLine { p = line });
                    continue;
                }

                string kind = LandcoverKind(tags);
                if (kind == null) continue;
                foreach (var poly in Polygons(el, type, frame))
                {
                    if (Mathf.Abs(LayerGeometry.SignedArea(poly.p)) < 25f) continue;
                    poly.kind = kind;
                    (kind == "forest" || kind == "wood" || kind == "orchard" ? into.forests : into.grass).Add(poly);
                }
            }

            // A building:part replaces the outline it sits in (the outline would stand inside it).
            if (parts.Count > 0)
            {
                var indexes = new List<PolyIndex>(outlines.Count);
                foreach (var b in outlines) indexes.Add(new PolyIndex(b.p, b.holes));
                var drop = new bool[outlines.Count];
                foreach (var part in parts)
                {
                    var c = LayerGeometry.Centroid(part.p);
                    for (int i = 0; i < outlines.Count; i++)
                        if (!drop[i] && indexes[i].Contains(c.x, c.y)) { drop[i] = true; }
                }
                for (int i = 0; i < outlines.Count; i++) if (!drop[i]) into.buildings.Add(outlines[i]);
                into.buildings.AddRange(parts);
            }
            else into.buildings.AddRange(outlines);
        }

        static string Tag(Dictionary<string, object> tags, string key) => MiniJson.Str(MiniJson.Get(tags, key)) ?? "";

        static string LandcoverKind(Dictionary<string, object> tags)
        {
            string landuse = Tag(tags, "landuse"), natural = Tag(tags, "natural"), leisure = Tag(tags, "leisure");
            if (landuse == "forest") return "forest";
            if (natural == "wood") return "wood";
            if (landuse == "orchard") return "orchard";
            if (landuse == "grass" || landuse == "meadow" || landuse == "recreation_ground" || landuse == "village_green" || landuse == "farmland") return landuse;
            if (leisure == "park") return "park";
            return null;
        }

        static float R(float v) => Mathf.Round(v * 10f) / 10f;

        static float[] Points(List<object> geometry, GeoFrame frame)
        {
            if (geometry == null) return null;
            var p = new float[geometry.Count * 2];
            for (int i = 0; i < geometry.Count; i++)
            {
                var g = MiniJson.Obj(geometry[i]);
                p[i * 2] = R(frame.X(MiniJson.Num(MiniJson.Get(g, "lon")))); p[i * 2 + 1] = R(frame.Z(MiniJson.Num(MiniJson.Get(g, "lat"))));
            }
            return p;
        }

        // Rings of a way (closed) or a multipolygon relation, as outer rings counter-clockwise with their holes clockwise.
        static List<LayerPoly> Polygons(Dictionary<string, object> el, string type, GeoFrame frame)
        {
            var result = new List<LayerPoly>();
            if (type == "way")
            {
                var g = MiniJson.Arr(MiniJson.Get(el, "geometry"));
                if (g == null || g.Count < 4) return result;
                var f = MiniJson.Obj(g[0]); var l = MiniJson.Obj(g[g.Count - 1]);
                if (MiniJson.Num(MiniJson.Get(f, "lat")) != MiniJson.Num(MiniJson.Get(l, "lat")) || MiniJson.Num(MiniJson.Get(f, "lon")) != MiniJson.Num(MiniJson.Get(l, "lon"))) return result;
                g.RemoveAt(g.Count - 1);
                var p = Points(g, frame);
                if (p.Length >= 6) result.Add(new LayerPoly { p = LayerGeometry.Ccw(p) });
                return result;
            }
            if (type != "relation") return result;

            var outers = new List<List<double[]>>();
            var inners = new List<List<double[]>>();
            var members = MiniJson.Arr(MiniJson.Get(el, "members"));
            if (members == null) return result;
            foreach (var mo in members)
            {
                var m = MiniJson.Obj(mo);
                if (MiniJson.Str(MiniJson.Get(m, "type")) != "way") continue;
                var g = MiniJson.Arr(MiniJson.Get(m, "geometry"));
                if (g == null || g.Count < 2) continue;
                var line = new List<double[]>(g.Count);
                foreach (var go in g) { var gp = MiniJson.Obj(go); if (gp != null) line.Add(new[] { MiniJson.Num(MiniJson.Get(gp, "lat")), MiniJson.Num(MiniJson.Get(gp, "lon")) }); }
                string role = MiniJson.Str(MiniJson.Get(m, "role")) ?? "";
                (role == "inner" ? inners : outers).Add(line);
            }
            var outerRings = Join(outers); var innerRings = Join(inners);
            var polys = new List<LayerPoly>();
            foreach (var ring in outerRings) { var p = Points(ring, frame); if (p.Length >= 6) polys.Add(new LayerPoly { p = LayerGeometry.Ccw(p) }); }
            var indexes = new List<PolyIndex>();
            foreach (var poly in polys) indexes.Add(new PolyIndex(poly.p, null));
            foreach (var ring in innerRings)
            {
                var p = Points(ring, frame);
                if (p.Length < 6) continue;
                int owner = -1; float ownerArea = float.MaxValue;
                for (int i = 0; i < polys.Count; i++)
                    if (indexes[i].Contains(p[0], p[1])) { float a = Mathf.Abs(LayerGeometry.SignedArea(polys[i].p)); if (a < ownerArea) { ownerArea = a; owner = i; } }
                if (owner >= 0) polys[owner].holes.Add(new LayerRing { p = LayerGeometry.Cw(p) });
            }
            return polys;
        }

        // Chains member ways end to end into closed rings; pieces that never close are dropped.
        static List<List<object>> Join(List<List<double[]>> lines)
        {
            var rings = new List<List<object>>();
            var left = new List<List<double[]>>(lines);
            while (left.Count > 0)
            {
                var ring = new List<double[]>(left[0]);
                left.RemoveAt(0);
                bool progress = true;
                while (progress && !Same(ring[0], ring[ring.Count - 1]))
                {
                    progress = false;
                    for (int i = 0; i < left.Count; i++)
                    {
                        var l = left[i];
                        var tail = ring[ring.Count - 1];
                        if (Same(tail, l[0])) { for (int k = 1; k < l.Count; k++) ring.Add(l[k]); }
                        else if (Same(tail, l[l.Count - 1])) { for (int k = l.Count - 2; k >= 0; k--) ring.Add(l[k]); }
                        else if (Same(ring[0], l[l.Count - 1])) { var joined = new List<double[]>(l); for (int k = 1; k < ring.Count; k++) joined.Add(ring[k]); ring = joined; }
                        else if (Same(ring[0], l[0])) { var joined = new List<double[]>(); for (int k = l.Count - 1; k >= 0; k--) joined.Add(l[k]); for (int k = 1; k < ring.Count; k++) joined.Add(ring[k]); ring = joined; }
                        else continue;
                        left.RemoveAt(i);
                        progress = true;
                        break;
                    }
                }
                if (ring.Count >= 4 && Same(ring[0], ring[ring.Count - 1]))
                {
                    ring.RemoveAt(ring.Count - 1);
                    var objs = new List<object>(ring.Count);
                    foreach (var pt in ring) objs.Add(new Dictionary<string, object> { { "lat", pt[0] }, { "lon", pt[1] } });
                    rings.Add(objs);
                }
            }
            return rings;
        }

        static bool Same(double[] a, double[] b) => Math.Abs(a[0] - b[0]) < 1e-8 && Math.Abs(a[1] - b[1]) < 1e-8;
    }

    public class OsmFetchResult
    {
        public bool ok;
        public bool surroundOk;
        public string error = "", note = "";
        public LayerFile file;
        public string layersPath = "";
    }

    // Waits real time, in a coroutine run by Unity or by the Editor tool below.
    public class RealtimeWait : CustomYieldInstruction
    {
        readonly double end;
        public RealtimeWait(double seconds) { end = Time.realtimeSinceStartupAsDouble + seconds; }
        public override bool keepWaiting => Time.realtimeSinceStartupAsDouble < end;
    }

    // Downloads the OpenStreetMap features for a downloaded area from the Overpass API, once, and stores them beside it.
    // Polite: one area at a time, two queries, 2 s between them, the project's User-Agent, at most 3 tries per query 10 s apart.
    // Bounded: each try waits at most TimeoutSeconds, and the whole fetch gives up after TotalBudgetSeconds (a slow or busy
    // Overpass server then means "no map features, fetch later", never a download that hangs).
    public static class OsmFetcher
    {
        public const string Endpoint = "https://overpass-api.de/api/interpreter";
        public const float PauseBetweenQueries = 2f, RetryAfter = 10f, TimeoutSeconds = 60f, TotalBudgetSeconds = 200f;
        public const int TriesPerQuery = 3;
        const string Landcover = "[\"landuse\"~\"^(forest|orchard|grass|meadow|recreation_ground|village_green|farmland)$\"]";

        // Tests point the endpoint at nothing: null = the real one.
        public static string EndpointOverride;

        static string Box(double s, double w, double n, double e) => string.Format(CultureInfo.InvariantCulture, "({0:F6},{1:F6},{2:F6},{3:F6})", s, w, n, e);

        public static string PlayAreaQuery(double s, double w, double n, double e)
        {
            string b = Box(s, w, n, e);
            return "[out:json][timeout:60][maxsize:1073741824];(" +
                "way[\"building\"]" + b + ";relation[\"building\"]" + b + ";way[\"building:part\"]" + b + ";" +
                "node[\"natural\"=\"tree\"]" + b + ";way[\"natural\"=\"tree_row\"]" + b + ";" +
                "way" + Landcover + b + ";relation" + Landcover + b + ";" +
                "way[\"natural\"=\"wood\"]" + b + ";relation[\"natural\"=\"wood\"]" + b + ";" +
                "way[\"leisure\"=\"park\"]" + b + ";relation[\"leisure\"=\"park\"]" + b + ";);out geom;";
        }

        public static string SurroundQuery(double s, double w, double n, double e)
        {
            string b = Box(s, w, n, e);
            return "[out:json][timeout:60][maxsize:1073741824];(" +
                "way" + Landcover + b + ";relation" + Landcover + b + ";" +
                "way[\"natural\"=\"wood\"]" + b + ";relation[\"natural\"=\"wood\"]" + b + ";" +
                "way[\"leisure\"=\"park\"]" + b + ";relation[\"leisure\"=\"park\"]" + b + ";)->.lc;.lc out geom;" +
                "(way[\"building\"]" + b + ";relation[\"building\"]" + b + ";)->.bl;.bl out center;";
        }

        // Reads the centre and the half side of an area (or its surround) from its elevation metadata.
        public static bool Extent(string rawPath, out double lat, out double lon, out float half)
        {
            lat = lon = 0.0; half = 0f;
            string meta = Path.ChangeExtension(rawPath, ".json");
            if (!File.Exists(meta)) return false;
            var m = JsonUtility.FromJson<DemReader.RawMetadata>(File.ReadAllText(meta));
            if (m == null || m.width <= 1 || m.cellSizeMetres <= 0.0) return false;
            lat = m.centreLatitude; lon = m.centreLongitude;
            half = (float)((m.width - 1) * m.cellSizeMetres * 0.5);
            return true;
        }

        // Fetches both queries for the area whose elevation file is `rawPath` (absolute) and writes the three files.
        // progress(fraction, text) is optional. Always calls done, with ok false and an error text when it failed.
        public static IEnumerator Fetch(string rawPath, Action<float, string> progress, Action<OsmFetchResult> done, DownloadControl control = null)
        {
            var result = new OsmFetchResult();
            double deadline = Time.realtimeSinceStartupAsDouble + TotalBudgetSeconds;
            if (!Extent(rawPath, out double lat, out double lon, out float playHalf))
            {
                result.error = "No usable area metadata beside " + Path.GetFileName(rawPath) + ".";
                done?.Invoke(result);
                yield break;
            }
            var frame = new GeoFrame(lat, lon);
            frame.Bounds(playHalf, out double s, out double w, out double n, out double e);
            var file = new LayerFile { centreLatitude = lat, centreLongitude = lon, playHalfMetres = playHalf, fetched = DateTime.Now.ToString("yyyy-MM-dd") };
            var seen = new HashSet<string>();

            // Query 1: the play area. Without it there is nothing to store.
            progress?.Invoke(0.02f, "play area query");
            string answer1 = null;
            yield return Query(PlayAreaQuery(s, w, n, e), a => answer1 = a, err => result.error = err, control, deadline, t => progress?.Invoke(0.02f, "play area query: " + t));
            if (answer1 == null) { done?.Invoke(result); yield break; }
            File.WriteAllText(LayerFile.RawPathFor(rawPath), answer1);
            progress?.Invoke(0.45f, "reading the play area answer");
            bool parsed = false;
            string parseError = null;
            var task = System.Threading.Tasks.Task.Run(() =>
            {
                try { OsmParser.Parse(MiniJson.Parse(answer1), frame, file, playHalf, false, seen); parsed = true; }
                catch (Exception ex) { parseError = ex.Message; }
            });
            while (!task.IsCompleted) yield return null;
            answer1 = null;
            if (!parsed) { result.error = "Could not read the OpenStreetMap answer: " + parseError; done?.Invoke(result); yield break; }

            // Query 2: the surround (landcover, and far buildings as points). A failure keeps query 1.
            string surroundPath = ElevationDownloader.SurroundPath(rawPath);
            if (Extent(surroundPath, out double slat, out double slon, out float surroundHalf))
            {
                file.surroundHalfMetres = surroundHalf;
                var sframe = new GeoFrame(slat, slon);
                sframe.Bounds(surroundHalf, out double ss, out double sw, out double sn, out double se);
                yield return new RealtimeWait(PauseBetweenQueries);
                progress?.Invoke(0.55f, "surround query");
                string answer2 = null;
                string surroundError = "";
                yield return Query(SurroundQuery(ss, sw, sn, se), a => answer2 = a, err => surroundError = err, control, deadline, t => progress?.Invoke(0.55f, "surround query: " + t));
                if (answer2 != null)
                {
                    File.WriteAllText(LayerFile.RawSurroundPathFor(rawPath), answer2);
                    progress?.Invoke(0.85f, "reading the surround answer");
                    bool parsed2 = false; string parseError2 = null;
                    var task2 = System.Threading.Tasks.Task.Run(() =>
                    {
                        try { OsmParser.Parse(MiniJson.Parse(answer2), sframe, file, playHalf, true, seen); parsed2 = true; }
                        catch (Exception ex) { parseError2 = ex.Message; }
                    });
                    while (!task2.IsCompleted) yield return null;
                    answer2 = null;
                    file.surroundOk = parsed2;
                    if (!parsed2) file.note += "Surround answer could not be read: " + parseError2 + ". ";
                }
                else file.note += "Surround query failed: " + surroundError + " The play area was kept. ";
            }
            else file.note += "This area has no surround, so no surround query was made. ";

            file.UpdateCounts();
            string layersPath = LayerFile.PathFor(rawPath);
            File.WriteAllText(layersPath, Compact(JsonUtility.ToJson(file)));
            result.ok = true;
            result.file = file;
            result.surroundOk = file.surroundOk;
            result.note = file.note;
            result.layersPath = layersPath;
            progress?.Invoke(1f, "done");
            done?.Invoke(result);
        }

        // Smaller file: coordinates are kept to 0.1 m, but JsonUtility writes every float with all its digits. Only the
        // lists (after the header, which holds the centre in degrees) are rounded.
        public static string Compact(string json)
        {
            int at = json.IndexOf("\"buildings\":", StringComparison.Ordinal);
            if (at < 0) return json;
            string tail = System.Text.RegularExpressions.Regex.Replace(json.Substring(at), @"-?\d+\.\d{3,}", m =>
                double.Parse(m.Value, CultureInfo.InvariantCulture).ToString("0.#", CultureInfo.InvariantCulture));
            return json.Substring(0, at) + tail;
        }

        // Builds the layers file again from the saved raw answers, with no network. Returns null when there is no raw answer.
        public static OsmFetchResult RebuildFromSaved(string rawPath)
        {
            var result = new OsmFetchResult();
            string path1 = LayerFile.RawPathFor(rawPath), path2 = LayerFile.RawSurroundPathFor(rawPath);
            if (!File.Exists(path1)) { result.error = "No saved OpenStreetMap answer for this area."; return result; }
            if (!Extent(rawPath, out double lat, out double lon, out float playHalf)) { result.error = "No usable area metadata."; return result; }
            var frame = new GeoFrame(lat, lon);
            string existing = LayerFile.PathFor(rawPath);
            string fetched = File.GetLastWriteTime(path1).ToString("yyyy-MM-dd");
            var file = new LayerFile { centreLatitude = lat, centreLongitude = lon, playHalfMetres = playHalf, fetched = fetched };
            var seen = new HashSet<string>();
            OsmParser.Parse(MiniJson.Parse(File.ReadAllText(path1)), frame, file, playHalf, false, seen);
            if (Extent(ElevationDownloader.SurroundPath(rawPath), out double slat, out double slon, out float surroundHalf))
            {
                file.surroundHalfMetres = surroundHalf;
                if (File.Exists(path2))
                {
                    OsmParser.Parse(MiniJson.Parse(File.ReadAllText(path2)), new GeoFrame(slat, slon), file, playHalf, true, seen);
                    file.surroundOk = true;
                }
                else file.note += "Surround query failed earlier: the play area was kept. ";
            }
            file.UpdateCounts();
            File.WriteAllText(existing, Compact(JsonUtility.ToJson(file)));
            result.ok = true; result.file = file; result.layersPath = existing; result.surroundOk = file.surroundOk; result.note = file.note;
            return result;
        }

        // Unity's own TLS (UnityTLS) cannot complete the handshake with overpass-api.de (it only offers TLS 1.3), so after one
        // SSL failure the queries go through the system's curl (Windows 10 and later ship curl.exe). Same server, same request.
        static bool preferCurl;

        // One Overpass query, at most TriesPerQuery times with RetryAfter seconds between, every try limited to TimeoutSeconds
        // and the whole fetch to `deadline` (realtime). status(text) says what is happening; skip / cancel end it at once.
        static IEnumerator Query(string query, Action<string> ok, Action<string> failed, DownloadControl control, double deadline, Action<string> status)
        {
            string last = "";
            double begun = Time.realtimeSinceStartupAsDouble;
            for (int attempt = 1; attempt <= TriesPerQuery; attempt++)
            {
                double left = deadline - Time.realtimeSinceStartupAsDouble;
                if (left < 10.0) { last = (last.Length > 0 ? last + "; " : "") + $"gave up after {Time.realtimeSinceStartupAsDouble - begun:F0} s (time budget used up)"; break; }
                float timeout = (float)Math.Min(TimeoutSeconds, left);
                string text = null;
                if (!preferCurl)
                {
                    var form = new WWWForm();
                    form.AddField("data", query);
                    using (var request = UnityWebRequest.Post(EndpointOverride ?? Endpoint, form))
                    {
                        request.SetRequestHeader("User-Agent", ElevationDownloader.UserAgent);
                        request.timeout = Mathf.Max(1, Mathf.RoundToInt(timeout));
                        double started = Time.realtimeSinceStartupAsDouble;
                        int shown = -1;
                        var op = request.SendWebRequest();
                        while (!op.isDone)
                        {
                            if (control != null && control.Stop) { request.Abort(); break; }
                            int sec = (int)(Time.realtimeSinceStartupAsDouble - started);
                            if (sec != shown) { shown = sec; status?.Invoke($"waiting for server, try {attempt}/{TriesPerQuery}, {sec} s"); }
                            yield return null;
                        }
                        if (control != null && control.Stop) { failed(control.StopWord); yield break; }
                        if (request.result == UnityWebRequest.Result.Success) text = request.downloadHandler.text;
                        else
                        {
                            last = $"HTTP {request.responseCode} {request.error}";
                            if (request.result == UnityWebRequest.Result.ConnectionError && IsTlsProblem(request.error)) preferCurl = true;
                        }
                    }
                }
                if (text == null && preferCurl)
                {
                    int tryNo = attempt;
                    var curl = Curl(query, t => text = t, e => last = e, control, timeout, sec => status?.Invoke($"waiting for server (curl), try {tryNo}/{TriesPerQuery}, {sec} s"));
                    while (curl.MoveNext()) yield return curl.Current;
                    if (control != null && control.Stop) { failed(control.StopWord); yield break; }
                }
                if (text != null)
                {
                    if (text.TrimStart().StartsWith("{")) { ok(text); yield break; }
                    last = "the server answered with something that is not JSON";
                }
                if (attempt < TriesPerQuery)
                {
                    double until = Time.realtimeSinceStartupAsDouble + RetryAfter;
                    while (Time.realtimeSinceStartupAsDouble < until)
                    {
                        if (control != null && control.Stop) { failed(control.StopWord); yield break; }
                        status?.Invoke($"failed ({last}), next try {attempt + 1}/{TriesPerQuery} in {Mathf.CeilToInt((float)(until - Time.realtimeSinceStartupAsDouble))} s");
                        yield return null;
                    }
                }
            }
            failed(last);
        }

        // UnityTLS fails the handshake in different words: "SSL ...", "Cert verify failed" (Curl error 35, UnityTls error code 7), "TLS ...".
        static bool IsTlsProblem(string error)
        {
            if (string.IsNullOrEmpty(error)) return false;
            foreach (var word in new[] { "SSL", "TLS", "Cert", "handshake" })
                if (error.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        static IEnumerator Curl(string query, Action<string> ok, Action<string> failed, DownloadControl control, float timeoutSeconds, Action<int> elapsedSeconds)
        {
            string dir = Path.Combine(Application.temporaryCachePath, "osm");
            string queryFile = Path.Combine(dir, "query.txt"), answerFile = Path.Combine(dir, "answer.json");
            System.Diagnostics.Process process = null;
            string startError = null;
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(queryFile, query);
                if (File.Exists(answerFile)) File.Delete(answerFile);
                string exe = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows), "System32", "curl.exe");
                var info = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = File.Exists(exe) ? exe : "curl",
                    Arguments = $"-sS --connect-timeout 20 -m {Mathf.Max(5, (int)timeoutSeconds)} -A \"{ElevationDownloader.UserAgent}\" --data-urlencode \"data@{queryFile}\" -o \"{answerFile}\" -w \"%{{http_code}}\" \"{EndpointOverride ?? Endpoint}\"",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                };
                process = System.Diagnostics.Process.Start(info);
            }
            catch (Exception e) { startError = e.Message; }
            if (process == null) { failed("could not start curl: " + startError); yield break; }
            double started = Time.realtimeSinceStartupAsDouble;
            int shown = -1;
            bool killed = false;
            // curl stops itself after -m seconds; the check here is a safety net, and the Skip / Cancel buttons.
            while (!process.HasExited)
            {
                double elapsed = Time.realtimeSinceStartupAsDouble - started;
                if ((control != null && control.Stop) || elapsed > timeoutSeconds + 10.0)
                {
                    try { process.Kill(); } catch (Exception) { }
                    killed = true;
                    break;
                }
                if ((int)elapsed != shown) { shown = (int)elapsed; elapsedSeconds?.Invoke(shown); }
                yield return null;
            }
            string code = "", error = "";
            if (!killed) { code = process.StandardOutput.ReadToEnd().Trim(); error = process.StandardError.ReadToEnd().Trim(); }
            process.Dispose();
            if (killed)
            {
                try { if (File.Exists(answerFile)) File.Delete(answerFile); } catch (Exception) { }
                failed(control != null && control.Stop ? control.StopWord : "curl did not answer in time");
                yield break;
            }
            if (code == "200" && File.Exists(answerFile)) { string text = File.ReadAllText(answerFile); try { File.Delete(answerFile); } catch (Exception) { } ok(text); yield break; }
            failed($"HTTP {code} {error}".Trim());
        }
    }
}
