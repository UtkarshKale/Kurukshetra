using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // Acceptance checks for the terrain layers (trees, grass, buildings). Run with the CLI:
    // Chaukanna.TerrainGen.LayersSelfTest.Run()
    // Everything here is SYNTHETIC: a hand-written Overpass sample, drawn pictures and a small generated terrain. No network.
    public static class LayersSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();
        const double Lat0 = 32.0, Lon0 = 75.0;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var cleanup = new List<UnityEngine.Object>();
            try
            {
                ParseChecks();
                FrameChecks();
                GeometryChecks();
                ScatterChecks();
                SatelliteChecks();
                ProceduralChecks();
                FalloffChecks();
                KitAndToggleChecks(cleanup);
            }
            catch (Exception e)
            {
                failures++;
                lines.Add("FAIL  unexpected exception: " + e);
            }
            finally
            {
                foreach (var o in cleanup) if (o != null) UnityEngine.Object.DestroyImmediate(o);
                SiteClearance.Clear();
            }
            string summary = $"[LayersSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }

        // --- 1. Overpass JSON ---

        static string Pt(GeoFrame f, double x, double z)
        {
            GeoMath.LocalToGeo(Lat0, Lon0, x, z, out double lat, out double lon);
            return "{\"lat\":" + lat.ToString("F8", Inv) + ",\"lon\":" + lon.ToString("F8", Inv) + "}";
        }

        static string Ring(GeoFrame f, params double[] xz)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < xz.Length; i += 2) sb.Append(i > 0 ? "," : "").Append(Pt(f, xz[i], xz[i + 1]));
            return sb.Append("]").ToString();
        }

        static string Sample(GeoFrame f)
        {
            var sb = new StringBuilder("{\"version\":0.6,\"elements\":[");
            // a tree
            GeoMath.LocalToGeo(Lat0, Lon0, 30.0, 40.0, out double tl, out double tn);
            sb.Append("{\"type\":\"node\",\"id\":1,\"lat\":" + tl.ToString("F8", Inv) + ",\"lon\":" + tn.ToString("F8", Inv) + ",\"tags\":{\"natural\":\"tree\"}},");
            // a house with 3 levels (9.6 m), clockwise on purpose
            sb.Append("{\"type\":\"way\",\"id\":2,\"tags\":{\"building\":\"house\",\"building:levels\":\"3\"},\"geometry\":" + Ring(f, 100, 100, 100, 110, 110, 110, 110, 100, 100, 100) + "},");
            // an apartment block with a height tag
            sb.Append("{\"type\":\"way\",\"id\":3,\"tags\":{\"building\":\"apartments\",\"height\":\"12.5 m\"},\"geometry\":" + Ring(f, 200, 100, 220, 100, 220, 115, 200, 115, 200, 100) + "},");
            // an outline with a part inside it: the outline must be dropped
            sb.Append("{\"type\":\"way\",\"id\":4,\"tags\":{\"building\":\"yes\"},\"geometry\":" + Ring(f, 300, 300, 330, 300, 330, 330, 300, 330, 300, 300) + "},");
            sb.Append("{\"type\":\"way\",\"id\":5,\"tags\":{\"building:part\":\"yes\",\"height\":\"20\"},\"geometry\":" + Ring(f, 305, 305, 325, 305, 325, 325, 305, 325, 305, 305) + "},");
            // a forest relation: outer ring made of two member ways, one inner hole
            sb.Append("{\"type\":\"relation\",\"id\":6,\"tags\":{\"type\":\"multipolygon\",\"landuse\":\"forest\"},\"members\":["
                + "{\"type\":\"way\",\"ref\":61,\"role\":\"outer\",\"geometry\":" + Ring(f, -400, -400, 400, -400, 400, 400) + "},"
                + "{\"type\":\"way\",\"ref\":62,\"role\":\"outer\",\"geometry\":" + Ring(f, 400, 400, -400, 400, -400, -400) + "},"
                + "{\"type\":\"way\",\"ref\":63,\"role\":\"inner\",\"geometry\":" + Ring(f, -100, -100, -100, 100, 100, 100, 100, -100, -100, -100) + "}]},");
            // grass, a tree row, and a way that is not ours
            sb.Append("{\"type\":\"way\",\"id\":7,\"tags\":{\"landuse\":\"meadow\"},\"geometry\":" + Ring(f, 500, 500, 600, 500, 600, 600, 500, 600, 500, 500) + "},");
            sb.Append("{\"type\":\"way\",\"id\":8,\"tags\":{\"natural\":\"tree_row\"},\"geometry\":" + Ring(f, 0, 700, 60, 700) + "},");
            sb.Append("{\"type\":\"way\",\"id\":9,\"tags\":{\"highway\":\"track\"},\"geometry\":" + Ring(f, 0, 0, 10, 0) + "}");
            return sb.Append("]}").ToString();
        }

        static void ParseChecks()
        {
            var j = MiniJson.Obj(MiniJson.Parse("{\"a\":[1,2.5,-3e2,\"x\\n\\u0041\"],\"b\":{\"c\":true,\"d\":null}}"));
            var a = MiniJson.Arr(MiniJson.Get(j, "a"));
            Check("MiniJson reads numbers, exponents, escapes, nesting", a.Count == 4 && (double)a[1] == 2.5 && (double)a[2] == -300.0 && (string)a[3] == "x\nA" && (bool)MiniJson.Get(MiniJson.Obj(MiniJson.Get(j, "b")), "c"));

            var f = new GeoFrame(Lat0, Lon0);
            var file = new LayerFile();
            OsmParser.Parse(MiniJson.Parse(Sample(f)), f, file, 1000f, false, new HashSet<string>());
            Check("sample: 1 tree, 1 tree row, 1 forest, 1 grass, 3 buildings (one outline replaced by its part)", file.trees.Count == 1 && file.treeRows.Count == 1 && file.forests.Count == 1 && file.grass.Count == 1 && file.buildings.Count == 3);
            var house = file.buildings.Find(b => b.type == "house");
            var flats = file.buildings.Find(b => b.type == "apartments");
            Check("levels give height: 3 levels = 9.6 m; height tag '12.5 m' = 12.5 m; none = type default",
                house != null && Mathf.Abs(house.height - 9.6f) < 0.01f && flats != null && Mathf.Abs(flats.height - 12.5f) < 0.01f && OsmParser.DefaultHeight("house") == 6f);
            Check("a building part replaces the outline around it (kept the 20 m part)", file.buildings.Exists(b => Mathf.Abs(b.height - 20f) < 0.01f) && !file.buildings.Exists(b => b.type == "generic" && Mathf.Abs(b.height - 6f) < 0.01f));
            Check("footprints are counter-clockwise whatever the map said", house != null && LayerGeometry.SignedArea(house.p) > 0f && Mathf.Abs(LayerGeometry.SignedArea(house.p) - 100f) < 0.5f);
            var forest = file.forests[0];
            Check("forest relation: two member ways joined into one ring, with the inner ring as a hole", forest.p.Length == 8 && forest.holes.Count == 1 && LayerGeometry.SignedArea(forest.holes[0].p) < 0f);
            var idx = new PolyIndex(forest.p, forest.holes);
            Check("forest: inside the ring yes, inside the hole no", idx.Contains(200f, 200f) && !idx.Contains(0f, 0f));
            Check("the mapped tree lands at its place (30, 40 m)", Mathf.Abs(file.trees[0].x - 30f) < 0.3f && Mathf.Abs(file.trees[0].z - 40f) < 0.3f);

            // Surround answer: a far building outside the play area is kept as a point, one inside is not.
            GeoMath.LocalToGeo(Lat0, Lon0, 3000.0, 100.0, out double fl, out double fo);
            GeoMath.LocalToGeo(Lat0, Lon0, 100.0, 100.0, out double nl, out double no);
            string far = "{\"elements\":[{\"type\":\"way\",\"id\":70,\"center\":{\"lat\":" + fl.ToString("F8", Inv) + ",\"lon\":" + fo.ToString("F8", Inv) + "},\"tags\":{\"building\":\"apartments\",\"building:levels\":\"5\"}},"
                + "{\"type\":\"way\",\"id\":71,\"center\":{\"lat\":" + nl.ToString("F8", Inv) + ",\"lon\":" + no.ToString("F8", Inv) + "},\"tags\":{\"building\":\"house\"}}]}";
            var file2 = new LayerFile();
            OsmParser.Parse(MiniJson.Parse(far), f, file2, 1000f, true, new HashSet<string>());
            Check("surround: far buildings are points outside the play area only, with height from levels", file2.farBuildings.Count == 1 && file2.farBuildings[0].type == "apartments" && Mathf.Abs(file2.farBuildings[0].height - 16f) < 0.01f);

            string compact = OsmFetcher.Compact("{\"centreLatitude\":32.123456789,\"buildings\":[{\"p\":[12.300000190734863,-4.699999809326172]}]}");
            Check("saved file is compact: metres to 0.1, degrees untouched", compact.Contains("32.123456789") && compact.Contains("12.3,-4.7") && !compact.Contains("12.30000"));
            Check("queries name the area's own box and the project's tags", OsmFetcher.PlayAreaQuery(1, 2, 3, 4).Contains("(1.000000,2.000000,3.000000,4.000000)") && OsmFetcher.PlayAreaQuery(1, 2, 3, 4).Contains("out geom") && OsmFetcher.SurroundQuery(1, 2, 3, 4).Contains("out center"));
        }

        // --- 2. Geographic frame ---

        static void FrameChecks()
        {
            var f = new GeoFrame(Lat0, Lon0);
            double worst = 0.0;
            foreach (var c in new[] { new Vector2(0f, 0f), new Vector2(-2500f, -2500f), new Vector2(2500f, 2500f), new Vector2(-2500f, 2500f), new Vector2(2500f, -2500f) })
            {
                GeoMath.LocalToGeo(Lat0, Lon0, c.x, c.y, out double lat, out double lon);
                worst = Math.Max(worst, Math.Max(Math.Abs(f.X(lon) - c.x), Math.Abs(f.Z(lat) - c.y)));
            }
            Check($"lat/lon -> metres matches the terrain's own conversion at the centre and corners (worst {worst:F3} m)", worst < 0.5);

            // The terrain's imagery placement uses the same maths: a picture whose bounds are the area's corners.
            var cfg = ScriptableObject.CreateInstance<TerrainGenConfig>();
            cfg.sizeMetres = 5000f; cfg.mode = TerrainMode.RealWorldAccurate; cfg.useFileCentre = false; cfg.centreLatitude = Lat0; cfg.centreLongitude = Lon0;
            GeoMath.LocalToGeo(Lat0, Lon0, -2500.0, -2500.0, out double s, out double w);
            GeoMath.LocalToGeo(Lat0, Lon0, 2500.0, 2500.0, out double n, out double e);
            var dem = new ElevationGrid { width = 2, height = 2, west = w, east = e, south = s, north = n, metres = new float[4] };
            var rect = ImageryReader.Extent(cfg, dem, new Imagery { georeferenced = true, west = w, east = e, south = s, north = n });
            Check("the picture's placement agrees with the layer frame to under 0.5 m", Mathf.Abs(rect.xMin - f.X(w)) < 0.5f && Mathf.Abs(rect.yMin - f.Z(s)) < 0.5f && Mathf.Abs(rect.xMax - f.X(e)) < 0.5f && Mathf.Abs(rect.yMax - f.Z(n)) < 0.5f);
            UnityEngine.Object.DestroyImmediate(cfg);
        }

        // --- 3. Footprint extrusion ---

        static void GeometryChecks()
        {
            // An L shape: 6 corners, concave. Counter-clockwise.
            var L = new float[] { 0, 0, 20, 0, 20, 10, 10, 10, 10, 20, 0, 20 };
            Check("L shape is counter-clockwise, area 300", Mathf.Abs(LayerGeometry.SignedArea(L) - 300f) < 0.01f);
            var pm = new PrismMesh();
            Check("L shape extrudes", pm.AddPrism(L, null, 0f, 9f, 0f, 0f));
            Check("L shape: 4 roof triangles + 12 wall triangles", pm.roofs.Count / 3 == 4 && pm.walls.Count / 3 == 12);

            bool upward = true;
            for (int i = 0; i < pm.roofs.Count; i += 3) if (Vector3.Cross(pm.vertices[pm.roofs[i + 1]] - pm.vertices[pm.roofs[i]], pm.vertices[pm.roofs[i + 2]] - pm.vertices[pm.roofs[i]]).y <= 0f) upward = false;
            Check("roof triangles face up (front faces seen from above)", upward);

            // Outward walls: the face normal (from winding) points away from the footprint centre of every wall.
            var centre = LayerGeometry.Centroid(L);
            bool outward = true;
            for (int i = 0; i < pm.walls.Count; i += 3)
            {
                Vector3 a = pm.vertices[pm.walls[i]], b = pm.vertices[pm.walls[i + 1]], c = pm.vertices[pm.walls[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized, mid = (a + b + c) / 3f;
                if (Vector3.Dot(n, pm.normals[pm.walls[i]]) < 0.99f) outward = false;                   // winding agrees with the stored normal
                // For a concave footprint "away from the centroid" is not a test; test against the wall itself: a point just outside is outside the polygon.
                var index = new PolyIndex(L, null);
                Vector3 probe = mid + n * 0.05f;
                if (index.Contains(probe.x, probe.z)) outward = false;
            }
            Check("walls face outward: winding matches the normal and a step along it leaves the footprint", outward);

            // Closed: the walls' top edge and the roof's rim are the same points (no gap), and the walls reach the ground.
            var wallTops = new HashSet<string>(); var roofRim = new HashSet<string>();
            foreach (int i in pm.walls) if (Mathf.Approximately(pm.vertices[i].y, 9f)) wallTops.Add(pm.vertices[i].x.ToString("F2") + "," + pm.vertices[i].z.ToString("F2"));
            for (int i = 0; i < pm.vertices.Count; i++) if (Mathf.Approximately(pm.vertices[i].y, 9f) && pm.normals[i] == Vector3.up) roofRim.Add(pm.vertices[i].x.ToString("F2") + "," + pm.vertices[i].z.ToString("F2"));
            bool touchesGround = pm.vertices.Exists(v => Mathf.Approximately(v.y, 0f));
            Check("the roof's rim and the walls' top edge are the same 6 corners, and the walls reach the ground", wallTops.SetEquals(roofRim) && wallTops.Count == 6 && touchesGround);

            // Holes: a 10 x 10 square with a 4 x 4 courtyard.
            var outer = new float[] { 0, 0, 10, 0, 10, 10, 0, 10 };
            var hole = new float[] { 3, 3, 3, 7, 7, 7, 7, 3 };       // clockwise
            Check("triangulation with a hole covers outer minus hole (area 84)", LayerGeometry.Triangulate(outer, new List<float[]> { hole }, out var verts, out var tris) && Mathf.Abs(TriArea(verts, tris) - 84f) < 0.01f);
            bool none = true;
            for (int i = 0; i < tris.Count; i += 3)
            {
                var c = (verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f;
                if (c.x > 3.01f && c.x < 6.99f && c.y > 3.01f && c.y < 6.99f) none = false;
            }
            Check("no roof triangle sits over the courtyard", none);
            var pm2 = new PrismMesh();
            pm2.AddPrism(outer, new List<float[]> { hole }, 0f, 5f, 0f, 0f);
            Check("a courtyard gets inner walls too (8 wall quads)", pm2.walls.Count / 6 == 8);

            // Oriented box of a rotated rectangle.
            var rot = new float[] { 0, 0, 10, 10, 8, 12, -2, 2 };
            LayerGeometry.OrientedBox(rot, out var bc, out var axis, out float hl, out float hs);
            Check("oriented box follows the longest edge (half sizes 7.07 x 1.41)", Mathf.Abs(hl - 7.07f) < 0.05f && Mathf.Abs(hs - 1.41f) < 0.05f && Mathf.Abs(Mathf.Abs(axis.x) - 0.707f) < 0.01f);
        }

        static float TriArea(List<Vector2> v, List<int> t)
        {
            float a = 0f;
            for (int i = 0; i < t.Count; i += 3) a += Mathf.Abs(((v[t[i + 1]].x - v[t[i]].x) * (v[t[i + 2]].y - v[t[i]].y) - (v[t[i + 1]].y - v[t[i]].y) * (v[t[i + 2]].x - v[t[i]].x)) * 0.5f);
            return a;
        }

        // --- 4. Scatter inside polygons ---

        static void ScatterChecks()
        {
            var poly = new LayerPoly { p = new float[] { -400, -400, 400, -400, 400, 400, -400, 400 }, kind = "forest" };
            poly.holes.Add(new LayerRing { p = new float[] { -100, -100, -100, 100, 100, 100, 100, -100 } });
            var index = new PolyIndex(poly.p, poly.holes);
            var spots = new List<TreeSpot>();
            LayerScatter.TreesInPolygon(poly, index, 1, 100f, 42, new Vector2(8f, 16f), Vector2.zero, 100000f, null, null, spots);
            bool allInside = true, anyInHole = false;
            foreach (var s in spots)
            {
                if (!index.Contains(s.x, s.z)) allInside = false;
                if (Mathf.Abs(s.x) < 100f && Mathf.Abs(s.z) < 100f) anyInHole = true;
                if (Mathf.Abs(s.x) > 400f || Mathf.Abs(s.z) > 400f) allInside = false;
            }
            float expected = (800f * 800f - 200f * 200f) / 10000f * 100f;
            Check($"no tree outside its forest or in its hole ({spots.Count} trees)", allInside && !anyInHole && spots.Count > 0);
            Check($"tree count follows the density (expected about {expected:F0})", Mathf.Abs(spots.Count - expected) / expected < 0.2f);
            var again = new List<TreeSpot>();
            LayerScatter.TreesInPolygon(poly, index, 1, 100f, 42, new Vector2(8f, 16f), Vector2.zero, 100000f, null, null, again);
            Check("the same seed gives the same trees", again.Count == spots.Count && again[0].x == spots[0].x && again[again.Count - 1].z == spots[spots.Count - 1].z);
            bool heightsOk = true;
            foreach (var s in spots) if (s.height < 8f - 0.01f || s.height > 16f + 0.01f) heightsOk = false;
            Check("tree heights stay in the kit's range", heightsOk);
            var thinned = new List<TreeSpot>();
            LayerScatter.TreesInPolygon(poly, index, 1, 100f, 42, new Vector2(8f, 16f), Vector2.zero, 50f, null, null, thinned);
            Check("far from the post the forest thins out (fall-off)", thinned.Count < spots.Count * 0.7f);
        }

        // --- 5. Satellite colour ---

        static void SatelliteChecks()
        {
            Check("classify: dark green = tree, light smooth green = grass, light textured green = tree, grey = none, black = none",
                SatelliteCover.Classify(0.12f, 0.27f, 0.12f, 0f) == CoverGrid.SatTree
                && SatelliteCover.Classify(0.50f, 0.70f, 0.28f, 0.01f) == CoverGrid.SatGrass
                && SatelliteCover.Classify(0.50f, 0.70f, 0.28f, 0.12f) == CoverGrid.SatTree
                && SatelliteCover.Classify(0.5f, 0.5f, 0.5f, 0f) == CoverGrid.None
                && SatelliteCover.Classify(0f, 0f, 0f, 0f) == CoverGrid.None);

            // A picture 90 x 30 pixels over +-45 m x +-15 m: left third dark green, middle light green, right grey.
            var f = new GeoFrame(Lat0, Lon0);
            GeoMath.LocalToGeo(Lat0, Lon0, -45.0, -45.0, out double s, out double w);
            GeoMath.LocalToGeo(Lat0, Lon0, 45.0, 45.0, out double n, out double e);
            var px = new Color32[90 * 90];
            for (int y = 0; y < 90; y++)
            for (int x = 0; x < 90; x++)
                px[y * 90 + x] = x < 30 ? new Color32(30, 70, 30, 255) : x < 60 ? new Color32(130, 180, 70, 255) : new Color32(128, 128, 128, 255);
            var grid = new CoverGrid(45f, 15f);
            var fill = SatelliteCover.Fill(grid, px, 90, 90, w, s, e, n, f);
            while (fill.MoveNext()) { }
            Check("synthetic picture: dark green cells are tree, light green grass, grey none", grid.At(-30f, 0f) == CoverGrid.SatTree && grid.At(0f, 0f) == CoverGrid.SatGrass && grid.At(30f, 0f) == CoverGrid.None);

            // Mapped landcover wins over the estimate.
            var grid2 = new CoverGrid(45f, 15f);
            grid2.Rasterize(new LayerPoly { p = new float[] { -45, -45, 0, -45, 0, 45, -45, 45 }, kind = "grass" }, CoverGrid.OsmGrass);
            var fill2 = SatelliteCover.Fill(grid2, px, 90, 90, w, s, e, n, f);
            while (fill2.MoveNext()) { }
            Check("a cell the map covers keeps the map's class; the estimate fills only the rest", grid2.At(-30f, 0f) == CoverGrid.OsmGrass && grid2.At(7f, 0f) == CoverGrid.SatGrass);
        }

        // --- 6. Procedural scatter ---

        static void ProceduralChecks()
        {
            // 2 km square, x from -1000 to 1000. Flat on the west half; a 15 deg slope on the east half.
            var ground = LayerGround.Synthetic(2000f, 201, (x, z) => x < 0f ? 100f : 100f + x * 0.27f);
            var a = new List<BuildingSpot>(); var b = new List<BuildingSpot>(); var c = new List<BuildingSpot>();
            LayerScatter.ProceduralBuildings(ground, 1234, 12f, null, a);
            LayerScatter.ProceduralBuildings(ground, 1234, 12f, null, b);
            LayerScatter.ProceduralBuildings(ground, 4321, 12f, null, c);
            bool same = a.Count == b.Count && a.Count > 0;
            for (int i = 0; same && i < a.Count; i++) same = a[i].x == b[i].x && a[i].z == b[i].z && a[i].type == b[i].type;
            bool differs = c.Count != a.Count;
            for (int i = 0; !differs && i < Mathf.Min(a.Count, c.Count); i++) differs = a[i].x != c[i].x || a[i].z != c[i].z;
            Check($"procedural buildings: same seed same result ({a.Count} houses), different seed differs", same && differs);

            bool flatOnly = true;
            foreach (var s in a) if (ground.SlopeDegrees(s.x, s.z) > LayerScatter.VillageSlopeLimit + 0.01f) flatOnly = false;
            Check("no building on steep ground (slope under 6 deg)", flatOnly);

            // Keep clear of the site: a rectangle across the flat ground.
            var zones = SiteBuilderLikeZones();
            SiteClearance.Set(zones);
            var d = new List<BuildingSpot>();
            LayerScatter.ProceduralBuildings(ground, 1234, 12f, (x, z) => SiteClearance.Blocked(x, z, false), d);
            bool clear = true;
            foreach (var s in d) if (SiteClearance.Blocked(s.x, s.z, false)) clear = false;
            Check($"no building inside the Site's footprint ({d.Count} left outside it)", clear && d.Count > 0 && d.Count < a.Count);
            SiteClearance.Clear();

            // Trees: gentle tilt from 3000 m to 4000 m across the area (slope 14 deg); none above the 3800 m tree line.
            var mountain = LayerGround.Synthetic(2000f, 201, (x, z) => 3500f + x * 0.5f);
            var trees = new List<TreeSpot>();
            LayerScatter.ProceduralTrees(mountain, 99, 400f, SurfaceBiome.Temperate, new Vector2(8f, 16f), Vector2.zero, 100000f, 0f, null, trees);
            bool belowLine = true; int low = 0;
            foreach (var t in trees) { float el = mountain.Elevation(t.x, t.z); if (el > LayerScatter.TreeLineMetres + 1f) belowLine = false; if (el < 3500f) low++; }
            Check($"no tree above the tree line ({trees.Count} trees, {low} on the lower half)", belowLine && trees.Count > 0 && low > 0);

            var steep = LayerGround.Synthetic(2000f, 201, (x, z) => 100f + x * 0.8f);        // 38.7 deg everywhere
            var none = new List<TreeSpot>();
            LayerScatter.ProceduralTrees(steep, 99, 400f, SurfaceBiome.Temperate, new Vector2(8f, 16f), Vector2.zero, 100000f, 0f, null, none);
            int inland = 0; foreach (var t in none) if (Mathf.Abs(t.x) < 980f && Mathf.Abs(t.z) < 980f) inland++;      // the last 10 m at the edge sees a clamped slope
            Check("no tree on slopes over 35 degrees", inland == 0);

            var t1 = new List<TreeSpot>(); var t2 = new List<TreeSpot>();
            LayerScatter.ProceduralTrees(mountain, 99, 400f, SurfaceBiome.Temperate, new Vector2(8f, 16f), Vector2.zero, 100000f, 0f, null, t1);
            LayerScatter.ProceduralTrees(mountain, 7, 400f, SurfaceBiome.Temperate, new Vector2(8f, 16f), Vector2.zero, 100000f, 0f, null, t2);
            Check("procedural trees: same seed, same trees; another seed, other trees", t1.Count == trees.Count && t1[0].x == trees[0].x && (t2.Count != trees.Count || t2[0].x != trees[0].x));

            Check("grass is zero on steep ground and above the snow line, positive on gentle low ground",
                LayerScatter.GrassCover(steep, 0f, 0f, 5, SurfaceBiome.Plains) == 0f && LayerScatter.GrassCover(LayerGround.Synthetic(2000f, 51, (x, z) => 4600f), 0f, 0f, 5, SurfaceBiome.Plains) == 0f
                && LayerScatter.GrassCover(LayerGround.Synthetic(2000f, 51, (x, z) => 300f), 0f, 0f, 5, SurfaceBiome.Plains) > 0.3f);
        }

        static List<SiteClearance.Zone> SiteBuilderLikeZones()
        {
            // A cantonment 600 m deep and 400 m either side, in front of a post at (-500, 0) looking east.
            return SiteClearance.ForSite(new Vector2(-500f, 0f), Vector2.right, 1f, 12f, 600f, 380f, 700f);
        }

        // --- 7. Fall-off ---

        static void FalloffChecks()
        {
            bool monotonic = true; float last = 2f;
            for (float d = 0f; d <= 20000f; d += 100f)
            {
                float f = LayerFalloff.Of(d, 1500f);
                if (f > last + 1e-6f) monotonic = false;
                last = f;
            }
            Check("density fall-off is full inside the start distance, never rises with distance, never below 15 %",
                monotonic && LayerFalloff.Of(0f, 1500f) == 1f && LayerFalloff.Of(1500f, 1500f) == 1f && Mathf.Approximately(LayerFalloff.Of(3000f, 1500f), 0.5f) && LayerFalloff.Of(100000f, 1500f) == LayerFalloff.Floor);
        }

        // --- 8, 9, 10. Kit, toggles, honesty about the data ---

        internal static LayerContext SmallWorld(List<UnityEngine.Object> cleanup, TerrainLayerKit kit, bool realTerrain)
        {
            var cfg = ScriptableObject.CreateInstance<TerrainGenConfig>();
            cleanup.Add(cfg);
            cfg.mode = TerrainMode.PureProcedural;
            cfg.seed = 5;
            cfg.sizeMetres = 1000f; cfg.heightmapResolution = 129; cfg.tilesPerSide = 1;
            cfg.baseElevation = 300f; cfg.flattenPostPad = false;
            cfg.layers = new List<NoiseLayer> { new NoiseLayer { label = "Swell", amplitudeMetres = 4f, featureSizeMetres = 900f, octaves = 2, persistence = 0.4f } };
            var built = TerrainBuilder.Build(cfg, null);
            cleanup.Add(built.root);

            var run = ScriptableObject.CreateInstance<TerrainGenConfig>();
            cleanup.Add(run);
            run.mode = realTerrain ? TerrainMode.RealWorldAccurate : TerrainMode.PureProcedural;
            run.seed = 5;
            run.terrainLayers = new TerrainLayerSettings { treeDensity = 120f, buildingDensity = 30f, grassDensity = 1f };
            var ctx = new LayerContext { cfg = run, kit = kit, centreLatitude = Lat0, centreLongitude = Lon0, biome = SurfaceBiome.Temperate };
            ctx.play = new LayerGround(built.field, -built.field.centreElevation);
            ctx.playTiles.AddRange(built.tiles);
            ctx.posts.Add(Vector2.zero);
            return ctx;
        }

        // Clumps standing within 150 m of the centre (the grass is placed around the camera, so this is what a post sees).
        static int GrassCells(TerrainLayerManager m)
        {
            var root = m.transform.Find("Grass");
            var field = root != null ? root.GetComponent<GrassField>() : null;
            return field == null ? 0 : field.ClumpsNear(Vector2.zero, 150f);
        }

        static void KitAndToggleChecks(List<UnityEngine.Object> cleanup)
        {
            // Kit rules.
            var kit = ScriptableObject.CreateInstance<TerrainLayerKit>();
            cleanup.Add(kit);
            Check("kit: an empty list means the footprint is extruded; a filled one means the prefab", !kit.HasBuildingPrefab(BuildingType.House));
            var dummyHouse = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cleanup.Add(dummyHouse);
            kit.buildingPrefabs.house.Add(dummyHouse);
            Check("kit: a filled list gives the prefab", kit.HasBuildingPrefab(BuildingType.House) && kit.PickBuildingPrefab(BuildingType.House, 7) == dummyHouse && !kit.HasBuildingPrefab(BuildingType.Shed));
            kit.buildingPrefabs.house.Clear();
            var placeholder = kit.TreePrefabsOrPlaceholders();
            Check("kit: a missing tree prefab is replaced by a placeholder tree", placeholder.Count == 1 && placeholder[0] != null && placeholder[0].GetComponentsInChildren<Renderer>().Length >= 2);
            cleanup.Add(placeholder[0]);

            // The manager on a small generated terrain, everything procedural.
            var ctx = SmallWorld(cleanup, kit, false);
            var manager = TerrainLayerManager.Create(ctx);
            cleanup.Add(manager.gameObject);
            manager.BuildAllNow();
            int trees = ctx.playTiles[0].terrainData.treeInstanceCount, grass = GrassCells(manager);
            var buildings = manager.ReportOf(LayerKind.Buildings);
            Check($"procedural layers build: {trees} trees, {grass} grass clumps within 150 m, {buildings.count} buildings (none from prefabs: the kit has none)", trees > 50 && grass > 100 && buildings.count > 0 && buildings.count2 == 0);
            Check("sources are labelled 'procedural'", manager.ReportOf(LayerKind.Trees).source == "procedural" && manager.ReportOf(LayerKind.Grass).source == "procedural" && buildings.source == "procedural");
            Check("no map credit when no OpenStreetMap data is shown", manager.Credit.Length == 0);
            Check("no tree stands within 60 m of the post", !AnyWithin(manager, SiteClearance.PostPadMetres));

            // Toggle: Off removes that layer only; back on rebuilds the same counts.
            int buildingChildren = BuildingCells(manager);
            ctx.Settings.trees = LayerMode.Off;
            manager.RebuildNow(LayerKind.Trees);
            Check("trees Off: no trees left, buildings and grass untouched", ctx.playTiles[0].terrainData.treeInstanceCount == 0 && BuildingCells(manager) == buildingChildren && GrassCells(manager) == grass && manager.ReportOf(LayerKind.Trees).source == "off");
            ctx.Settings.trees = LayerMode.Procedural;
            manager.RebuildNow(LayerKind.Trees);
            Check("trees back on: the same count as before", ctx.playTiles[0].terrainData.treeInstanceCount == trees);

            // Tree budget: a dense wood is thinned to the budget and the kept trees are made bigger.
            float meanBefore = 0f; int nBefore = 0;
            foreach (var t in ctx.playTiles[0].terrainData.treeInstances) { meanBefore += t.heightScale; nBefore++; }
            meanBefore /= Mathf.Max(1, nBefore);
            ctx.Settings.treeBudget = 400;
            manager.RebuildNow(LayerKind.Trees);
            float meanAfter = 0f; int nAfter = ctx.playTiles[0].terrainData.treeInstanceCount;
            foreach (var t in ctx.playTiles[0].terrainData.treeInstances) meanAfter += t.heightScale;
            meanAfter /= Mathf.Max(1, nAfter);
            Check($"tree budget: {nBefore} trees thinned to {nAfter} (budget 400), kept trees bigger ({meanBefore:F2} -> {meanAfter:F2}), the report says so",
                nBefore > 800 && nAfter > 100 && nAfter <= 400 && meanAfter > meanBefore * 1.3f && manager.ReportOf(LayerKind.Trees).detail.Contains("thinned"));
            ctx.Settings.treeBudget = 0;
            manager.RebuildNow(LayerKind.Trees);
            Check("budget 0 means no limit: the full count is back", ctx.playTiles[0].terrainData.treeInstanceCount == nBefore);
            ctx.Settings.treeBudget = 60000;
            ctx.Settings.buildings = LayerMode.Off;
            manager.RebuildNow(LayerKind.Buildings);
            Check("buildings Off: their root is gone, trees and grass stay", BuildingCells(manager) == 0 && ctx.playTiles[0].terrainData.treeInstanceCount == trees && GrassCells(manager) == grass);
            ctx.Settings.buildings = LayerMode.Procedural;
            manager.RebuildNow(LayerKind.Buildings);
            Check("buildings back on: the same count", manager.ReportOf(LayerKind.Buildings).count == buildings.count);

            // With a house prefab in the kit, procedural houses use it.
            var kit2 = ScriptableObject.CreateInstance<TerrainLayerKit>();
            cleanup.Add(kit2);
            kit2.buildingPrefabs.house.Add(dummyHouse);
            kit2.buildingPrefabs.apartments.Add(dummyHouse); kit2.buildingPrefabs.shed.Add(dummyHouse); kit2.buildingPrefabs.religious.Add(dummyHouse);
            ctx.kit = kit2;
            manager.RebuildNow(LayerKind.Buildings);
            Check("kit prefab used for procedural buildings when the list is filled", manager.ReportOf(LayerKind.Buildings).count2 > 0);
            ctx.kit = kit;

            // Honesty: Realistic with no OSM file.
            var real = SmallWorld(cleanup, kit, true);
            real.Settings.trees = LayerMode.Realistic; real.Settings.grass = LayerMode.Realistic; real.Settings.buildings = LayerMode.Realistic;
            var m2 = TerrainLayerManager.Create(real);
            cleanup.Add(m2.gameObject);
            m2.BuildAllNow();
            Check("Realistic without map data and without a picture: no buildings, no trees, source 'none'", m2.ReportOf(LayerKind.Buildings).count == 0 && m2.ReportOf(LayerKind.Buildings).source == "none" && m2.ReportOf(LayerKind.Trees).source == "none" && real.playTiles[0].terrainData.treeInstanceCount == 0);

            // ... with a satellite picture: trees from colour, labelled as an estimate, still no buildings.
            GeoMath.LocalToGeo(Lat0, Lon0, -500.0, -500.0, out double s, out double w);
            GeoMath.LocalToGeo(Lat0, Lon0, 500.0, 500.0, out double n, out double e);
            var tex = new Texture2D(64, 64, TextureFormat.RGB24, false);
            var pixels = new Color32[64 * 64];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (i % 64) < 32 ? new Color32(25, 65, 28, 255) : new Color32(140, 135, 120, 255);
            tex.SetPixels32(pixels); tex.Apply();
            cleanup.Add(tex);
            real.playImagery = new Imagery { texture = tex, georeferenced = true, west = w, east = e, south = s, north = n };
            m2.RebuildNow(LayerKind.Trees);
            m2.RebuildNow(LayerKind.Grass);
            Check("Realistic with only a picture: trees 'satellite estimate' (woodland half), no OpenStreetMap credit",
                m2.ReportOf(LayerKind.Trees).source == "satellite estimate" && real.playTiles[0].terrainData.treeInstanceCount > 20 && m2.ReportOf(LayerKind.Grass).source == "satellite estimate" && m2.Credit.Length == 0);
            bool west = true;
            foreach (var t in real.playTiles[0].terrainData.treeInstances) if (t.position.x > 0.52f) west = false;      // only the green (west) half
            Check("estimated trees stand only on the green half of the picture", west);

            // ... with map data: trees inside the mapped forest, buildings from the footprints, credit shown.
            var file = new LayerFile();
            OsmParser.Parse(MiniJson.Parse(Sample(new GeoFrame(Lat0, Lon0))), new GeoFrame(Lat0, Lon0), file, 1000f, false, new HashSet<string>());
            real.Osm = file;
            real.coverPlay = null;
            m2.RebuildNow(LayerKind.Trees);
            m2.RebuildNow(LayerKind.Buildings);
            var treeReport = m2.ReportOf(LayerKind.Trees);
            Check("Realistic with map data: trees 'OSM + satellite estimate', buildings 'OSM' = the 3 footprints", treeReport.source == "OSM + satellite estimate" && m2.ReportOf(LayerKind.Buildings).source == "OSM" && m2.ReportOf(LayerKind.Buildings).count == 3);
            Check("the credit line appears once OpenStreetMap data is shown", m2.Credit == LayerFile.OsmCredit && m2.Summary().Contains("Buildings Realistic [OSM]"));
        }

        static bool AnyWithin(TerrainLayerManager m, float radius)
        {
            var ctx = m.Context;
            foreach (var t in ctx.playTiles)
            {
                var size = t.terrainData.size; var pos = t.transform.position;
                foreach (var inst in t.terrainData.treeInstances)
                    if (new Vector2(pos.x + inst.position.x * size.x, pos.z + inst.position.z * size.z).magnitude < radius - 0.5f) return true;
            }
            return false;
        }

        static int BuildingCells(TerrainLayerManager m)
        {
            var b = m.transform.Find("Buildings");
            return b == null ? 0 : b.childCount;
        }
    }
}
