using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kurukshetra.TerrainGen
{
    // Everything the layers need to know about the terrain they stand on.
    public class LayerContext
    {
        public TerrainGenConfig cfg;
        public TerrainLayerKit kit;
        public LayerGround play, surround;                  // surround may be null
        public List<Terrain> playTiles = new List<Terrain>(), surroundTiles = new List<Terrain>();
        public string demPath = "";                         // the area's elevation file: its layers file sits beside it
        public Imagery playImagery, surroundImagery;        // satellite pictures, if the area has them
        public List<Vector2> posts = new List<Vector2>();
        public SurfaceBiome biome = SurfaceBiome.Plains;
        public double centreLatitude, centreLongitude;      // the area centre, for placing the satellite pictures

        public bool RealTerrain => cfg != null && cfg.UsesDem;
        public TerrainLayerSettings Settings => cfg.terrainLayers;
        public int Seed => cfg.seed;
        public Vector2 FalloffCentre => posts.Count > 0 ? posts[0] : Vector2.zero;

        // OpenStreetMap features of this area, or null when it has none. Loaded on first use.
        LayerFile osm; bool osmLoaded;
        public LayerFile Osm
        {
            get
            {
                if (!osmLoaded)
                {
                    osmLoaded = true;
                    if (RealTerrain && !string.IsNullOrEmpty(demPath)) osm = LayerFile.Load(demPath);
                }
                return osm;
            }
            set { osm = value; osmLoaded = true; }
        }

        public CoverGrid coverPlay, coverSurround;          // built once per session
    }

    // Builds, shows and rebuilds the three terrain layers: trees and grass as Unity terrain features, buildings as merged
    // meshes. All of it hangs under one root, so switching a layer only touches that layer.
    public class TerrainLayerManager : MonoBehaviour
    {
        public const string RootName = "Terrain layers (generated)";
        public static TerrainLayerManager Current { get; private set; }

        public class Report
        {
            public LayerMode mode = LayerMode.Off;
            public string source = "off";                   // OSM, satellite estimate, procedural, mixes of these, off
            public string detail = "";
            public int count;                               // trees, detail cells with grass, buildings
            public int count2, count3;                      // trees: mapped / estimated; buildings: prefab / far
            public float seconds;                           // CPU time spent building (not counting frames waited)
            public bool usedOsm;
            public override string ToString() => $"{mode}: {source}" + (detail.Length > 0 ? " (" + detail + ")" : "");
        }

        public LayerContext Context { get; private set; }
        public bool Busy { get; private set; }
        public string BusyText { get; private set; } = "";
        public readonly Report[] Reports = { new Report(), new Report(), new Report(), new Report() };
        public Report ReportOf(LayerKind k) => Reports[(int)k];

        Transform root;
        readonly Transform[] layerRoots = new Transform[4];
        int seenClearance = -1;
        bool built;
        readonly Queue<LayerKind> queue = new Queue<LayerKind>();
        const float SliceMs = 7f;

        public static TerrainLayerManager Create(LayerContext ctx)
        {
            if (Current != null) Kill(Current.gameObject);
            var go = new GameObject(RootName);
            var m = go.AddComponent<TerrainLayerManager>();
            m.Context = ctx;
            m.root = go.transform;
            Current = m;
            return m;
        }

        void OnDestroy() { if (Current == this) Current = null; }

        static void Kill(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
        }

        // --- Driving ---

        // Builds all three layers (a coroutine, so a loading screen can show progress).
        public IEnumerator BuildAll(Action<float, string> progress = null)
        {
            Busy = true;
            var kinds = new[] { LayerKind.Buildings, LayerKind.Trees, LayerKind.Grass, LayerKind.Bushes };
            for (int i = 0; i < kinds.Length; i++)
            {
                var kind = kinds[i];
                var inner = BuildLayer(kind, (f, t) => progress?.Invoke((i + f) / kinds.Length, t));
                while (inner.MoveNext()) yield return inner.Current;
            }
            seenClearance = SiteClearance.Version;
            built = true;
            Busy = false;
            BusyText = "";
        }

        // Rebuilds one layer over the next frames.
        public void Rebuild(LayerKind kind)
        {
            if (!queue.Contains(kind)) queue.Enqueue(kind);
            // The bushes stand round the trees: new trees, new bushes.
            if (kind == LayerKind.Trees && !queue.Contains(LayerKind.Bushes)) queue.Enqueue(LayerKind.Bushes);
        }

        // Rebuilds one layer right now (tests, tools).
        public void RebuildNow(LayerKind kind)
        {
            var it = BuildLayer(kind, null);
            while (it.MoveNext()) { }
            if (kind == LayerKind.Trees) RebuildNow(LayerKind.Bushes);
        }

        public void BuildAllNow()
        {
            var it = BuildAll();
            while (it.MoveNext()) { }
        }

        void Update()
        {
            if (Busy || Context == null) return;
            if (queue.Count == 0 && built && SiteClearance.Version != seenClearance)
            {
                // The site (watchtower, cantonment) was built or moved: buildings and trees keep clear of it.
                seenClearance = SiteClearance.Version;
                Rebuild(LayerKind.Buildings);
                Rebuild(LayerKind.Trees);
            }
            if (queue.Count > 0) StartCoroutine(RunQueue());
        }

        IEnumerator RunQueue()
        {
            Busy = true;
            while (queue.Count > 0)
            {
                var kind = queue.Dequeue();
                var inner = BuildLayer(kind, null);
                while (inner.MoveNext()) yield return inner.Current;
            }
            seenClearance = SiteClearance.Version;
            Busy = false;
            BusyText = "";
        }

        public void SetMode(LayerKind kind, LayerMode mode)
        {
            if (Context == null || Context.Settings.Get(kind) == mode) return;
            Context.Settings.Set(kind, mode);
            Rebuild(kind);
        }

        // --- One layer ---

        public IEnumerator BuildLayer(LayerKind kind, Action<float, string> progress)
        {
            Context.Settings.Sanitise();

            var fresh = new Report { mode = Context.Settings.Effective(kind, Context.RealTerrain) };
            Reports[(int)kind] = fresh;
            BusyText = (fresh.mode == LayerMode.Off ? "Removing " : "Rebuilding ") + kind.ToString().ToLowerInvariant();
            progress?.Invoke(0f, BusyText);

            yield return null;
            IEnumerator it;
            switch (kind)
            {
                case LayerKind.Trees: it = BuildTrees(fresh, progress); break;
                case LayerKind.Grass: it = BuildGrass(fresh, progress); break;
                case LayerKind.Bushes: it = BuildBushes(fresh, progress); break;
                default: it = BuildBuildings(fresh, progress); break;
            }
            var slice = Stopwatch.StartNew();
            double cpu = 0.0;
            while (true)
            {
                slice.Restart();
                bool more = it.MoveNext();
                cpu += slice.Elapsed.TotalSeconds;
                if (!more) break;
                yield return null;
            }
            fresh.seconds = (float)cpu;
            progress?.Invoke(1f, BusyText);
        }

        // Yields when a slice of work has used up its frame budget. Use inside builders: `if (t.Due) { yield return null; t.Restart(); }`.
        struct Timer
        {
            readonly Stopwatch sw;
            public Timer(bool start) { sw = Stopwatch.StartNew(); }
            public bool Due => sw.Elapsed.TotalMilliseconds >= SliceMs;
            public void Restart() { sw.Restart(); }
        }

        Transform LayerRoot(LayerKind kind)
        {
            if (layerRoots[(int)kind] == null)
            {
                var go = new GameObject(kind.ToString());
                go.transform.SetParent(root, false);
                layerRoots[(int)kind] = go.transform;
            }
            return layerRoots[(int)kind];
        }

        // Shows or hides one layer without touching its data (the 3D replay hides the buildings on request). False when the layer is not built.
        public bool SetLayerVisible(LayerKind kind, bool visible)
        {
            var t = layerRoots[(int)kind];
            if (t == null) return false;
            t.gameObject.SetActive(visible);
            return true;
        }

        void ClearRoot(LayerKind kind)
        {
            var t = layerRoots[(int)kind];
            if (t == null) return;
            // Destroy() waits for the end of the frame: take it out of sight and out of the hierarchy now.
            t.gameObject.name += " (removed)";
            t.SetParent(null);
            Kill(t.gameObject);
            layerRoots[(int)kind] = null;
        }

        bool NearPost(float x, float z)
        {
            foreach (var p in Context.posts)
                if ((x - p.x) * (x - p.x) + (z - p.y) * (z - p.y) < SiteClearance.PostPadMetres * SiteClearance.PostPadMetres) return true;
            return false;
        }

        // ================= Trees =================

        List<TreeSpot> lastTreeSpots;                       // the trees as placed (after the budget), for the bushes round them

        IEnumerator BuildTrees(Report rep, Action<float, string> progress)
        {
            ClearTrees();
            lastTreeSpots = null;
            ClearRoot(LayerKind.Trees);
            var s = Context.Settings;
            if (rep.mode == LayerMode.Off) { rep.source = "off"; yield break; }

            var kit = Context.kit;
            var spots = new List<TreeSpot>();
            float start = s.falloffStartKm * 1000f;
            var centre = Context.FalloffCentre;
            var heights = kit.treeHeight;
            int seed = Context.Seed ^ 0x7EE5;
            Func<float, float, bool> blocked = (x, z) => NearPost(x, z) || SiteClearance.Blocked(x, z, true);
            var timer = new Timer(true);

            if (rep.mode == LayerMode.Procedural)
            {
                rep.source = "procedural";
                ProceduralTreesInto(spots, seed, heights, centre, start, blocked);
                yield return null;
            }
            else
            {
                var osm = Context.Osm;
                string note = "";
                int mapped = 0, estimated = 0;
                if (osm != null)
                {
                    // (counted below, once we know OSM really contributed)
                    int before = spots.Count;
                    LayerScatter.MappedTrees(osm.trees, seed, heights, centre, start, blocked, spots);
                    LayerScatter.TreeRows(osm.treeRows, seed, heights, centre, start, blocked, spots);
                    mapped = spots.Count - before;
                    Func<float, float, bool> inside = (x, z) => Context.play.Contains(x, z) || (Context.surround != null && Context.surround.Contains(x, z));
                    for (int i = 0; i < osm.forests.Count; i++)
                    {
                        var poly = osm.forests[i];
                        if (poly.p == null || poly.p.Length < 6) continue;
                        var index = new PolyIndex(poly.p, poly.holes);
                        // Orchards are rows of smaller trees; thinner than a forest.
                        float density = poly.kind == "orchard" ? s.treeDensity * 0.5f : s.treeDensity;
                        LayerScatter.TreesInPolygon(poly, index, i, density, seed, heights, centre, start, blocked, inside, spots);
                        if (timer.Due) { progress?.Invoke(0.1f + 0.4f * i / osm.forests.Count, "Trees: mapped woods"); yield return null; timer.Restart(); }
                    }
                    mapped = spots.Count;
                }
                // Satellite estimate where the map has no landcover.
                int beforeSat = spots.Count;
                foreach (var domain in new[] { 0, 1 })
                {
                    if (domain == 1 && Context.surround == null) continue;
                    var grid = default(CoverGrid);
                    var build = EnsureCover(domain, progress);
                    while (build.MoveNext()) yield return build.Current;
                    grid = domain == 0 ? Context.coverPlay : Context.coverSurround;
                    if (grid == null) continue;
                    float skip = domain == 1 ? Context.play.Half : 0f;
                    Func<float, float, bool> within = (x, z) => skip <= 0f || Mathf.Abs(x) >= skip || Mathf.Abs(z) >= skip;
                    LayerScatter.TreesFromSatellite(grid, s.treeDensity, seed, heights, centre, start, within, blocked, spots);
                    yield return null;
                }
                estimated = spots.Count - beforeSat;
                bool osmUsed = mapped > 0, sat = estimated > 0;
                rep.usedOsm = osmUsed;
                rep.count2 = mapped; rep.count3 = estimated;
                rep.source = osmUsed ? (sat ? "OSM + satellite estimate" : "OSM") : (sat ? "satellite estimate" : "none");
                if (osm == null) note = "no OSM data for this area"; else if (!osmUsed) note = "the map has no trees or woods here";
                if (!sat && !osmUsed) note = (osm == null ? "no OSM data and " : "") + (Context.playImagery == null ? "no satellite picture" : "no woodland in the picture") + ": no trees";
                rep.detail = note;
            }

            string thinned = ApplyBudget(spots, s.treeBudget, centre, start, seed);
            if (thinned.Length > 0) rep.detail = (rep.detail.Length > 0 ? rep.detail + "; " : "") + thinned;
            lastTreeSpots = spots;
            progress?.Invoke(0.7f, "Trees: placing " + spots.Count);
            var place = PlaceTrees(spots, rep);
            while (place.MoveNext()) yield return place.Current;
            rep.count = spots.Count;
            if (spots.Count >= LayerScatter.HardTreeCeiling) rep.detail += (rep.detail.Length > 0 ? "; " : "") + "stopped at the " + LayerScatter.HardTreeCeiling + " tree limit";
        }

        void ProceduralTreesInto(List<TreeSpot> spots, int seed, Vector2 heights, Vector2 centre, float start, Func<float, float, bool> blocked)
        {
            float density = Context.Settings.treeDensity;
            LayerScatter.ProceduralTrees(Context.play, seed, density, Context.biome, heights, centre, start, 0f, blocked, spots);
            if (Context.surround != null)
                LayerScatter.ProceduralTrees(Context.surround, seed ^ 0x5151, density, Context.biome, heights, centre, start, Context.play.Half, blocked, spots);
        }

        // The same tree without its colliders, for "trees block sight" off (the terrain has no per-tree switch in its API).
        static readonly Dictionary<GameObject, GameObject> softTrees = new Dictionary<GameObject, GameObject>();
        static GameObject WithoutColliders(GameObject prefab)
        {
            if (softTrees.TryGetValue(prefab, out var soft) && soft != null) return soft;
            soft = UnityEngine.Object.Instantiate(prefab);
            soft.name = prefab.name + " (no colliders)";
            soft.hideFlags = HideFlags.HideAndDontSave;
            soft.transform.SetPositionAndRotation(new Vector3(0f, -20000f, 0f), Quaternion.identity);
            foreach (var c in soft.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);      // at once: the terrain reads the prefab as soon as it is assigned
            softTrees[prefab] = soft;
            return soft;
        }

        // Keeps the tree count within the budget: mapped single trees and tree rows always stay, trees within the fall-off
        // distance are kept first, the far ones are thinned and the survivors made bigger so the woodland still reads as woodland.
        // Returns a note (empty when nothing was thinned).
        public static string ApplyBudget(List<TreeSpot> spots, int budget, Vector2 centre, float nearMetres, int seed)
        {
            if (budget <= 0 || spots.Count <= budget) return "";
            int mapped = 0, near = 0, far = 0;
            float nearSq = nearMetres * nearMetres;
            foreach (var s in spots)
            {
                if (s.source == LayerScatter.FromNode || s.source == LayerScatter.FromRow) mapped++;
                else if ((s.x - centre.x) * (s.x - centre.x) + (s.z - centre.y) * (s.z - centre.y) <= nearSq) near++;
                else far++;
            }
            float keepNear = near == 0 ? 1f : Mathf.Clamp01((budget - mapped) / (float)near);
            float keepFar = far == 0 ? 1f : near > 0 && keepNear < 1f ? 0f : Mathf.Clamp01((budget - mapped - near) / (float)far);
            var kept = new List<TreeSpot>(Mathf.Min(spots.Count, budget + 16));
            foreach (var s in spots)
            {
                if (s.source == LayerScatter.FromNode || s.source == LayerScatter.FromRow) { kept.Add(s); continue; }
                bool isNear = (s.x - centre.x) * (s.x - centre.x) + (s.z - centre.y) * (s.z - centre.y) <= nearSq;
                float p = isNear ? keepNear : keepFar;
                if (p <= 0f || LayerScatter.Rand(Mathf.RoundToInt(s.x * 10f), Mathf.RoundToInt(s.z * 10f), seed ^ 0x5B) >= p) continue;
                var t = s;
                if (p < 1f) t.height *= Mathf.Min(isNear ? 1.6f : 2.2f, 1f / Mathf.Sqrt(p));      // the same canopy from fewer trees
                kept.Add(t);
            }
            int before = spots.Count;
            spots.Clear();
            spots.AddRange(kept);
            return $"thinned from {before} to {spots.Count} trees (budget {budget}): {keepNear * 100f:F0} % of the trees within {nearMetres / 1000f:F1} km kept, {keepFar * 100f:F0} % of the far ones, kept ones enlarged";
        }

        static float PrefabHeight(GameObject prefab)
        {
            var clone = UnityEngine.Object.Instantiate(prefab);
            clone.hideFlags = HideFlags.HideAndDontSave;
            clone.transform.position = new Vector3(0f, -30000f, 0f);
            var rs = clone.GetComponentsInChildren<Renderer>(true);
            float h = 0f;
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
                h = b.size.y;
            }
            Kill(clone);
            return h > 0.1f ? h : 8f;
        }

        IEnumerator PlaceTrees(List<TreeSpot> spots, Report rep)
        {
            var table = Context.kit.BuildTreeTable();
            var protoPrefabs = table.prefabs;
            var protos = new TreePrototype[protoPrefabs.Count];
            var baseHeights = new float[protoPrefabs.Count];
            for (int i = 0; i < protos.Length; i++)
            {
                var source = Context.Settings.treesBlockSight ? protoPrefabs[i] : WithoutColliders(protoPrefabs[i]);
                protos[i] = new TreePrototype { prefab = source, bendFactor = 0f };
                baseHeights[i] = PrefabHeight(source);
            }

            var tiles = new List<Terrain>();
            tiles.AddRange(Context.playTiles);
            tiles.AddRange(Context.surroundTiles);
            var perTile = new Dictionary<Terrain, List<TreeInstance>>();
            foreach (var t in tiles) if (t != null) perTile[t] = new List<TreeInstance>();
            float playHalf = Context.play.Half;
            var treeClassCounts = new int[4];

            var timer = new Timer(true);
            for (int i = 0; i < spots.Count; i++)
            {
                var sp = spots[i];
                bool inPlay = Mathf.Abs(sp.x) <= playHalf && Mathf.Abs(sp.z) <= playHalf;
                var tile = FindTile(inPlay ? Context.playTiles : Context.surroundTiles, sp.x, sp.z);
                if (tile == null) continue;
                var size = tile.terrainData.size;
                var pos = tile.transform.position;
                int treeHash = (int)LayerScatter.Hash(i, 3, Context.Seed);
                int proto;
                if (table.classed)
                {
                    // Species by place: pine high up and on steep ground, snow-covered and dead trees near the tree line, broadleaf low.
                    var g = inPlay || Context.surround == null ? Context.play : Context.surround;
                    var cls = Vegetation.TreeClassAt(g.Elevation(sp.x, sp.z), g.SlopeDegrees(sp.x, sp.z), Context.biome, treeHash);
                    proto = table.Pick(cls, treeHash >> 3);
                    treeClassCounts[(int)cls]++;
                }
                else proto = (int)((uint)treeHash % (uint)protos.Length);
                float hs = Mathf.Max(0.05f, sp.height / baseHeights[proto]);
                perTile[tile].Add(new TreeInstance
                {
                    position = new Vector3(Mathf.Clamp01((sp.x - pos.x) / size.x), 0f, Mathf.Clamp01((sp.z - pos.z) / size.z)),
                    widthScale = hs * sp.width, heightScale = hs, rotation = sp.yaw, prototypeIndex = proto,
                    color = Color.white, lightmapColor = Color.white,
                });
                if (timer.Due) { yield return null; timer.Restart(); }
            }

            if (table.classed) rep.detail += (rep.detail.Length > 0 ? "; " : "") + $"species: {treeClassCounts[0]} broadleaf, {treeClassCounts[1]} pine, {treeClassCounts[2]} snow, {treeClassCounts[3]} dead";
            foreach (var kv in perTile)
            {
                var t = kv.Key; var td = t.terrainData;
                td.treePrototypes = protos;
                td.SetTreeInstances(kv.Value.ToArray(), true);
                ViewDistance.RegisterTerrain(t, 6000f, 250f);              // tree / billboard distances times the player's view distance, billboard cross-fade 20 %
                t.treeMaximumFullLODCount = 200;
                t.treeLODBiasMultiplier = Mathf.Max(0.01f, Context.Settings.treeLodBias);      // trees cost far more per frame than their meshes suggest: see TerrainLayerSettings.treeLodBias
                t.drawTreesAndFoliage = true;
                var col = t.GetComponent<TerrainCollider>();
                if (col != null)
                {
                    // The collider only picks up new tree colliders when it is switched off and on again (assigning the data again is not enough).
                    col.enabled = false;
                    col.enabled = true;
                }
                yield return null;
            }
        }

        static Terrain FindTile(List<Terrain> tiles, float x, float z)
        {
            foreach (var t in tiles)
            {
                if (t == null || t.terrainData == null) continue;
                var p = t.transform.position; var s = t.terrainData.size;
                if (x >= p.x && x <= p.x + s.x && z >= p.z && z <= p.z + s.z) return t;
            }
            return null;
        }

        void ClearTrees()
        {
            foreach (var list in new[] { Context.playTiles, Context.surroundTiles })
                foreach (var t in list)
                {
                    if (t == null || t.terrainData == null) continue;
                    t.terrainData.SetTreeInstances(new TreeInstance[0], false);
                    t.terrainData.treePrototypes = new TreePrototype[0];
                }
        }

        // The cover grid of one domain (0 = play area, 1 = surround): mapped landcover, then the satellite estimate for the rest.
        IEnumerator EnsureCover(int domain, Action<float, string> progress)
        {
            if (domain == 0 ? Context.coverPlay != null : Context.coverSurround != null) yield break;
            var ground = domain == 0 ? Context.play : Context.surround;
            if (ground == null) yield break;
            var osm = Context.Osm;
            var img = domain == 0 ? Context.playImagery : Context.surroundImagery;
            if (osm == null && img == null) yield break;

            float half = ground.Half;
            var grid = new CoverGrid(half, Mathf.Max(10f, 2f * half / 3000f));
            if (osm != null)
            {
                // Grass and farmland first, then orchards, then forest on top.
                var timer = new Timer(true);
                for (int pass = 0; pass < 3; pass++)
                {
                    var list = pass == 0 ? osm.grass : osm.forests;
                    foreach (var poly in list)
                    {
                        if (poly.p == null || poly.p.Length < 6) continue;
                        bool orchard = poly.kind == "orchard";
                        if (pass == 1 && !orchard) continue;
                        if (pass == 2 && orchard) continue;
                        byte value = pass == 0 ? (poly.kind == "farmland" ? CoverGrid.OsmFarm : CoverGrid.OsmGrass) : pass == 1 ? CoverGrid.OsmOrchard : CoverGrid.OsmForest;
                        grid.Rasterize(poly, value);
                        if (timer.Due) { yield return null; timer.Restart(); }
                    }
                }
            }
            if (img != null && img.texture != null && img.georeferenced)
            {
                Color32[] px = null;
                try { px = img.texture.GetPixels32(); } catch (Exception e) { Debug.LogWarning("[Layers] The satellite picture cannot be read for the colour estimate: " + e.Message); }
                if (px != null)
                {
                    var frame = new GeoFrame(Context.centreLatitude, Context.centreLongitude);
                    var fill = SatelliteCover.Fill(grid, px, img.texture.width, img.texture.height, img.west, img.south, img.east, img.north, frame);
                    while (fill.MoveNext()) yield return fill.Current;
                }
            }
            if (domain == 0) Context.coverPlay = grid; else Context.coverSurround = grid;
        }

        // ================= Grass =================

        IEnumerator BuildGrass(Report rep, Action<float, string> progress)
        {
            ClearGrass();
            ClearRoot(LayerKind.Grass);
            var s = Context.Settings;
            if (rep.mode == LayerMode.Off) { rep.source = "off"; yield break; }

            var kit = Context.kit;
            // What to draw: the kit's first grass prefab (mesh + material), else a mesh with the kit's first grass texture, else a plain placeholder.
            Mesh clumpMesh = null; Material clumpMaterial = null;
            foreach (var g in kit.grassPrefabs)
            {
                if (g == null) continue;
                var mf = g.GetComponentInChildren<MeshFilter>(true); var mr = g.GetComponentInChildren<MeshRenderer>(true);
                if (mf != null && mr != null && mf.sharedMesh != null && mr.sharedMaterial != null) { clumpMesh = mf.sharedMesh; clumpMaterial = mr.sharedMaterial; break; }
            }
            if (clumpMesh == null)
            {
                Texture2D tex = null;
                foreach (var t in kit.grassTextures) if (t != null) { tex = t; break; }
                var placeholder = TerrainLayerKit.PlaceholderGrass();
                clumpMesh = placeholder.GetComponent<MeshFilter>().sharedMesh;
                clumpMaterial = placeholder.GetComponent<MeshRenderer>().sharedMaterial;
                if (tex != null) { clumpMaterial = new Material(clumpMaterial) { name = "Grass from kit texture" }; if (clumpMaterial.HasProperty("_BaseMap")) clumpMaterial.SetTexture("_BaseMap", tex); else clumpMaterial.mainTexture = tex; }
            }

            bool realistic = rep.mode == LayerMode.Realistic;
            CoverGrid cover = null;
            if (realistic)
            {
                var build = EnsureCover(0, progress);
                while (build.MoveNext()) yield return build.Current;
                cover = Context.coverPlay;
                int mappedCells = 0, estimatedCells = 0;
                if (cover != null)
                    for (byte c = CoverGrid.OsmForest; c <= CoverGrid.SatGrass; c++) { int n = cover.Count(c); if (c <= CoverGrid.OsmOrchard) mappedCells += n; else estimatedCells += n; }
                rep.usedOsm = mappedCells > 0;
                rep.source = mappedCells > 0 ? (estimatedCells > 0 ? "OSM + satellite estimate" : "OSM") : (estimatedCells > 0 ? "satellite estimate" : "none");
                if (cover == null) rep.detail = "no OSM data and no satellite picture: no grass";
            }
            else rep.source = "procedural";

            // Cover on a coarse grid (8 m or coarser); the grass field places the clumps from it when the camera comes near.
            var ground = Context.play;
            float half = ground.Half;
            float coarse = Mathf.Max(8f, 2f * half / 1024f);
            int cn = Mathf.CeilToInt(2f * half / coarse);
            var field = new float[cn * cn];
            var timer = new Timer(true);
            int coveredCells = 0;
            for (int iz = 0; iz < cn; iz++)
            {
                for (int ix = 0; ix < cn; ix++)
                {
                    float x = -half + (ix + 0.5f) * coarse, z = -half + (iz + 0.5f) * coarse;
                    float c;
                    if (realistic) c = cover == null ? 0f : GrassOfClass(cover.At(x, z));
                    else c = LayerScatter.GrassCover(ground, x, z, Context.Seed ^ 0x6A55, Context.biome);
                    field[iz * cn + ix] = c * s.grassDensity;
                    if (field[iz * cn + ix] > 0.001f) coveredCells++;
                }
                if (timer.Due) { progress?.Invoke(0.1f + 0.8f * iz / cn, "Grass: cover"); yield return null; timer.Restart(); }
            }

            var grassRoot = LayerRoot(LayerKind.Grass);
            var gf = grassRoot.gameObject.AddComponent<GrassField>();
            gf.Setup(ground, field, cn, coarse, half, Context.Seed ^ 0x6A55, clumpMesh, clumpMaterial, kit.grassWidth, kit.grassHeight);
            rep.count = coveredCells;
            rep.count2 = gf.ClumpsNear(Context.FalloffCentre, GrassField.DefaultRadius);
            rep.detail = (rep.detail.Length > 0 ? rep.detail + "; " : "") + $"{coveredCells} cover cells of {coarse:F0} m, about {rep.count2} clumps within {GrassField.DefaultRadius:F0} m of the post";
            yield return null;
        }

        static float GrassOfClass(byte c)
        {
            switch (c)
            {
                case CoverGrid.OsmGrass: return 1f;
                case CoverGrid.OsmFarm: return 0.6f;
                case CoverGrid.OsmOrchard: return 0.5f;
                case CoverGrid.OsmForest: return 0.15f;
                case CoverGrid.SatGrass: return 0.8f;
                case CoverGrid.SatTree: return 0.15f;
                default: return 0f;
            }
        }

        // Grass is a component under its own root, which ClearRoot removes: nothing is stored in the terrain data.
        void ClearGrass() { }

        // ================= Bushes =================

        // Instanced drawing needs GPU instancing on the material; a copy is made for any that lack it (the kit's own are left alone).
        static BushModel WithInstancing(BushModel m)
        {
            if (m == null) return null;
            for (int i = 0; i < m.materials.Length; i++)
                if (m.materials[i] != null && !m.materials[i].enableInstancing)
                    m.materials[i] = new Material(m.materials[i]) { name = m.materials[i].name + " (instanced)", enableInstancing = true };
            return m;
        }

        // Realistic bushes are an estimate: they stand along wood edges and on grassland the map or the picture shows, never in crops.
        static float CoverBushFactor(CoverGrid g, LayerGround ground, float x, float z)
        {
            if (ground.SlopeDegrees(x, z) > LayerScatter.TreeSlopeLimit || ground.Elevation(x, z) > LayerScatter.TreeLineMetres) return 0f;
            byte c = g.At(x, z);
            float d = Mathf.Max(g.cell, 24f);
            int wood = 0;
            if (IsWood(g.At(x + d, z))) wood++;
            if (IsWood(g.At(x - d, z))) wood++;
            if (IsWood(g.At(x, z + d))) wood++;
            if (IsWood(g.At(x, z - d))) wood++;
            if (IsWood(c)) return wood < 4 ? 1f : 0.08f;
            float f = c == CoverGrid.SatGrass ? 0.35f : c == CoverGrid.OsmGrass ? 0.2f : c == CoverGrid.OsmOrchard ? 0.1f : 0f;
            if (wood > 0 && c != CoverGrid.OsmFarm) f = Mathf.Max(f, 0.8f);
            return f;
        }

        static bool IsWood(byte c) => c == CoverGrid.OsmForest || c == CoverGrid.SatTree;

        IEnumerator BuildBushes(Report rep, Action<float, string> progress)
        {
            ClearRoot(LayerKind.Bushes);
            var s = Context.Settings;
            if (rep.mode == LayerMode.Off) { rep.source = "off"; yield break; }

            var kit = Context.kit;
            var normal = new List<BushModel>(); var snow = new List<BushModel>();
            foreach (var g in kit.bushPrefabs) { var m = WithInstancing(BushModel.From(g)); if (m != null) normal.Add(m); }
            foreach (var g in kit.bushSnowPrefabs) { var m = WithInstancing(BushModel.From(g)); if (m != null) snow.Add(m); }
            if (normal.Count == 0 && snow.Count == 0) { var m = WithInstancing(BushModel.From(TerrainLayerKit.PlaceholderBush())); if (m != null) normal.Add(m); }

            var ground = Context.play;
            var centre = Context.FalloffCentre;
            float start = s.falloffStartKm * 1000f;
            int treeSeed = Context.Seed ^ 0x7EE5, seed = Context.Seed ^ 0xB05E;
            Func<float, float, bool> blocked = (x, z) => !ground.Contains(x, z) || NearPost(x, z) || SiteClearance.Blocked(x, z, true);
            bool realistic = rep.mode == LayerMode.Realistic;
            var spots = new List<BushSpot>();
            var timer = new Timer(true);

            // Round the trees as they were placed (so the rings follow the tree budget), then a scatter along wood edges and in the open.
            int ring = 0;
            if (lastTreeSpots != null && lastTreeSpots.Count > 0)
            {
                var near = new List<TreeSpot>();
                foreach (var t in lastTreeSpots) if (ground.Contains(t.x, t.z)) near.Add(t);
                Vegetation.RoundTrees(near, s.bushDensity, seed, blocked, spots);
                ring = spots.Count;
            }
            yield return null;

            Func<float, float, float> factor = null;
            CoverGrid cover = null;
            if (realistic)
            {
                var build = EnsureCover(0, progress);
                while (build.MoveNext()) yield return build.Current;
                cover = Context.coverPlay;
                var c = cover;
                factor = c == null ? (Func<float, float, float>)((x, z) => 0f) : (x, z) => CoverBushFactor(c, ground, x, z);
            }
            int rows = Vegetation.ScatterRows(ground);
            for (int row = 0; row < rows; row++)
            {
                Vegetation.ScatterRow(ground, row, s.bushDensity, treeSeed, seed, Context.biome, centre, start, blocked, spots, factor);
                if (timer.Due) { progress?.Invoke(0.1f + 0.7f * row / rows, "Bushes: scatter"); yield return null; timer.Restart(); }
            }
            int scattered = spots.Count - ring;

            string thinned = Vegetation.ApplyBudget(spots, s.bushBudget, centre);
            rep.source = realistic ? (spots.Count == 0 ? "none" : cover != null ? "round trees + cover estimate" : "round trees") : "procedural";
            rep.count = spots.Count; rep.count2 = ring; rep.count3 = scattered;
            rep.detail = $"{ring} round trees, {scattered} along wood edges and in the open";
            if (realistic && spots.Count == 0) rep.detail = "no trees and no map or picture to estimate from: no bushes";
            if (thinned.Length > 0) rep.detail += "; " + thinned;
            yield return null;

            if (spots.Count == 0) yield break;
            var bf = LayerRoot(LayerKind.Bushes).gameObject.AddComponent<BushField>();
            bf.Setup(ground, spots, normal.ToArray(), snow.ToArray(), kit.bushScale, s.bushDrawMetres);
            yield return null;
        }


        // ================= Buildings =================

        class BuildItem
        {
            public float[] p; public List<LayerRing> holes; public float height, minHeight; public BuildingType type; public string osmType;
            public bool procedural, filled; public int hash; public Vector2 centre;
        }

        class CellData
        {
            public int cx, cz;
            public readonly List<BuildItem> extrude = new List<BuildItem>();
            public readonly List<BuildItem> prefabs = new List<BuildItem>();
        }

        public const int PrefabBudget = 3000;
        public const float PrefabFarMetres = 1500f;
        const float CellMetres = 250f, FarCellMetres = 1000f;
        const int FarBuildingCeiling = 80000;

        IEnumerator BuildBuildings(Report rep, Action<float, string> progress)
        {
            ClearRoot(LayerKind.Buildings);
            var s = Context.Settings;
            if (rep.mode == LayerMode.Off) { rep.source = "off"; yield break; }

            var kit = Context.kit;
            var items = new List<BuildItem>();
            var far = new List<LayerFarBuilding>();
            bool realistic = rep.mode == LayerMode.Realistic;
            Func<float, float, bool> blocked = (x, z) => NearPost(x, z) || SiteClearance.Blocked(x, z, false);
            int mapped = 0, procedural = 0;
            var timer = new Timer(true);

            if (realistic)
            {
                var osm = Context.Osm;
                if (osm != null)
                {

                    for (int i = 0; i < osm.buildings.Count; i++)
                    {
                        var b = osm.buildings[i];
                        if (b.p == null || b.p.Length < 6) continue;
                        var c = LayerGeometry.Centroid(b.p);
                        if (!Context.play.Contains(c.x, c.y) || blocked(c.x, c.y)) continue;
                        items.Add(new BuildItem { p = b.p, holes = b.holes, height = b.height, minHeight = b.minHeight, type = TerrainLayerKit.TypeOf(b.type), osmType = b.type, centre = c, hash = i });
                        mapped++;
                    }
                    if (Context.surround != null)
                    {
                        int stride = osm.farBuildings.Count > FarBuildingCeiling ? Mathf.CeilToInt(osm.farBuildings.Count / (float)FarBuildingCeiling) : 1;
                        for (int i = 0; i < osm.farBuildings.Count; i += stride)
                        {
                            var f = osm.farBuildings[i];
                            if (Context.play.Contains(f.x, f.z) || !Context.surround.Contains(f.x, f.z)) continue;
                            far.Add(f);
                        }
                        if (stride > 1) rep.detail = $"far buildings thinned to every {stride}th";
                    }
                    rep.usedOsm = mapped > 0 || far.Count > 0;
                    rep.source = rep.usedOsm ? "OSM" : "none";
                    if (!rep.usedOsm) rep.detail = "the map has no buildings in this area";
                }
                else { rep.source = "none"; rep.detail = "no OSM data for this area: Realistic has no buildings"; }

                if (s.fillGapsProcedurally)
                {
                    // Procedural buildings only in 500 m cells the map left empty.
                    var occupied = new HashSet<long>();
                    foreach (var it in items) occupied.Add(CellKey(Mathf.FloorToInt(it.centre.x / 500f), Mathf.FloorToInt(it.centre.y / 500f)));
                    var spots = new List<BuildingSpot>();
                    LayerScatter.ProceduralBuildings(Context.play, Context.Seed ^ 0x0B1D, s.buildingDensity, blocked, spots);
                    foreach (var sp in spots)
                    {
                        if (occupied.Contains(CellKey(Mathf.FloorToInt(sp.x / 500f), Mathf.FloorToInt(sp.z / 500f)))) continue;
                        items.Add(ItemOf(sp, true));
                        procedural++;
                    }
                    if (procedural > 0) { rep.source = (mapped > 0 ? "OSM + " : "") + "filled procedurally"; }
                }
            }
            else
            {
                rep.source = "procedural";
                var spots = new List<BuildingSpot>();
                LayerScatter.ProceduralBuildings(Context.play, Context.Seed ^ 0x0B1D, s.buildingDensity, blocked, spots);
                foreach (var sp in spots) items.Add(ItemOf(sp, false));
                procedural = items.Count;
            }
            yield return null;

            // Sort into 250 m cells; decide which become prefab instances.
            var cells = new Dictionary<long, CellData>();
            int prefabEligible = 0;
            foreach (var it in items)
            {
                bool usePrefab = kit.HasBuildingPrefab(it.type) && (it.procedural || kit.realisticUsesPrefabs);
                if (usePrefab) prefabEligible++;
            }
            bool overBudget = prefabEligible > PrefabBudget;
            int prefabCount = 0, extrudedCount = 0;
            foreach (var it in items)
            {
                int cx = Mathf.FloorToInt(it.centre.x / CellMetres), cz = Mathf.FloorToInt(it.centre.y / CellMetres);
                long key = CellKey(cx, cz);
                if (!cells.TryGetValue(key, out var cell)) cells[key] = cell = new CellData { cx = cx, cz = cz };
                bool usePrefab = kit.HasBuildingPrefab(it.type) && (it.procedural || kit.realisticUsesPrefabs);
                if (usePrefab && overBudget && Vector2.Distance(it.centre, Context.FalloffCentre) > PrefabFarMetres) usePrefab = false;
                if (usePrefab) { cell.prefabs.Add(it); prefabCount++; } else { cell.extrude.Add(it); extrudedCount++; }
            }

            var parent = LayerRoot(LayerKind.Buildings);
            var wall = kit.WallMaterial(); var roof = kit.RoofMaterial();
            var colliders = new List<MeshCollider>(); var colliderMeshes = new List<Mesh>();
            int done = 0, cellCount = cells.Count;
            foreach (var cell in cells.Values)
            {
                BuildCell(parent, cell, wall, roof, kit, colliders, colliderMeshes);
                done++;
                if (timer.Due) { progress?.Invoke(0.1f + 0.7f * done / Mathf.Max(1, cellCount), "Buildings: " + done + " of " + cellCount + " cells"); yield return null; timer.Restart(); }
            }

            // Cook the colliders on worker threads, then attach them.
            if (colliderMeshes.Count > 0)
            {
                var ids = new UnityEngine.EntityId[colliderMeshes.Count];
                for (int i = 0; i < ids.Length; i++) ids[i] = colliderMeshes[i].GetEntityId();
                System.Threading.Tasks.Task bake = null;
                try { bake = System.Threading.Tasks.Task.Run(() => System.Threading.Tasks.Parallel.For(0, ids.Length, i => Physics.BakeMesh(ids[i], false))); }
                catch (Exception e) { Debug.LogWarning("[Layers] Collider baking on worker threads failed, cooking on the main thread: " + e.Message); }
                while (bake != null && !bake.IsCompleted) yield return null;
                for (int i = 0; i < colliders.Count; i++)
                {
                    colliders[i].sharedMesh = colliderMeshes[i];
                    if (timer.Due) { yield return null; timer.Restart(); }
                }
            }

            if (far.Count > 0)
            {
                var farBuild = BuildFar(parent, far, wall, roof, progress);
                while (farBuild.MoveNext()) yield return farBuild.Current;
            }

            rep.count = extrudedCount + prefabCount;
            rep.count2 = prefabCount;
            rep.count3 = far.Count;
            if (realistic && Context.Osm == null && procedural == 0) rep.count = 0;
            string detail = $"{extrudedCount} extruded, {prefabCount} prefab, {far.Count} far";
            rep.detail = rep.detail.Length > 0 ? detail + "; " + rep.detail : detail;
        }

        static long CellKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        static BuildItem ItemOf(BuildingSpot sp, bool filled)
        {
            var axis = new Vector2(Mathf.Cos(sp.yaw), Mathf.Sin(sp.yaw));
            return new BuildItem
            {
                p = LayerGeometry.BoxRing(new Vector2(sp.x, sp.z), axis, Mathf.Max(sp.width, sp.depth) * 0.5f, Mathf.Min(sp.width, sp.depth) * 0.5f),
                height = sp.height, type = sp.type, procedural = true, filled = filled, hash = sp.hash, centre = new Vector2(sp.x, sp.z),
            };
        }

        void BuildCell(Transform parent, CellData cell, Material wall, Material roof, TerrainLayerKit kit, List<MeshCollider> colliders, List<Mesh> colliderMeshes)
        {
            float ox = (cell.cx + 0.5f) * CellMetres, oz = (cell.cz + 0.5f) * CellMetres;
            var go = new GameObject($"Cell {cell.cx}_{cell.cz}");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(ox, 0f, oz);
            var ground = Context.play;

            if (cell.extrude.Count > 0)
            {
                var full = new PrismMesh(); var boxes = new PrismMesh();
                foreach (var it in cell.extrude)
                {
                    GroundSpan(ground, it, out float low, out float mid);
                    float bottom = it.minHeight > 0f ? mid + it.minHeight : low - 0.6f, top = Mathf.Max(bottom + 1f, mid + it.height);
                    var holes = new List<float[]>();
                    if (it.holes != null) foreach (var h in it.holes) if (h != null && h.p != null) holes.Add(h.p);
                    Vector2? wallUv = null, roofUv = null;
                    if (kit.UsesPalette) { wallUv = BuildingPalette.WallUv(BuildingPalette.WallIndex(it.hash, it.type)); roofUv = BuildingPalette.RoofUv(BuildingPalette.RoofIndex(it.hash, it.type)); }
                    full.AddPrism(it.p, holes, bottom, top, ox, oz, wallUv, roofUv);
                    LayerGeometry.OrientedBox(it.p, out var c, out var axis, out float hl, out float hs);
                    boxes.AddPrism(LayerGeometry.BoxRing(c, axis, Mathf.Max(0.5f, hl), Mathf.Max(0.5f, hs)), null, bottom, top, ox, oz, wallUv, roofUv);
                }
                var mesh0 = full.ToMesh("Buildings " + go.name);
                var mesh1 = boxes.ToMesh("Building boxes " + go.name);
                var r0 = MakeRenderer(go.transform, "Detail", mesh0, wall, roof);
                var r1 = MakeRenderer(go.transform, "Boxes", mesh1, wall, roof);
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.08f, new[] { r0 }) { fadeTransitionWidth = 0.4f }, new LOD(0.005f, new[] { r1 }) { fadeTransitionWidth = 0.5f } });
                lod.fadeMode = LODFadeMode.CrossFade;                     // dithered cross-fade between the detailed block, the box and nothing, not a swap
                lod.animateCrossFading = true;
                lod.RecalculateBounds();
                var col = r0.gameObject.AddComponent<MeshCollider>();
                colliders.Add(col); colliderMeshes.Add(mesh0);
            }

            foreach (var it in cell.prefabs)
            {
                var prefab = kit.PickBuildingPrefab(it.type, it.hash);
                if (prefab == null) continue;
                PlacePrefab(go.transform, prefab, it, ground);
            }
        }

        // Lowest ground under a footprint and the ground at its centre.
        static void GroundSpan(LayerGround ground, BuildItem it, out float low, out float mid)
        {
            mid = ground.Height(it.centre.x, it.centre.y);
            low = mid;
            int n = it.p.Length / 2, stride = Mathf.Max(1, n / 12);
            for (int i = 0; i < n; i += stride) low = Mathf.Min(low, ground.Height(it.p[i * 2], it.p[i * 2 + 1]));
        }

        static MeshRenderer MakeRenderer(Transform parent, string name, Mesh mesh, Material wall, Material roof)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = new[] { wall, roof };
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return r;
        }

        static readonly Dictionary<GameObject, Vector3> prefabSizes = new Dictionary<GameObject, Vector3>();

        static void PlacePrefab(Transform parent, GameObject prefab, BuildItem it, LayerGround ground)
        {
            if (!prefabSizes.TryGetValue(prefab, out var size) || size.y <= 0f)
            {
                var probe = UnityEngine.Object.Instantiate(prefab);
                probe.hideFlags = HideFlags.HideAndDontSave;
                probe.transform.SetPositionAndRotation(new Vector3(0f, -30000f, 0f), Quaternion.identity);
                probe.transform.localScale = Vector3.one;
                var rs = probe.GetComponentsInChildren<Renderer>(true);
                var b = new Bounds(probe.transform.position, Vector3.zero);
                if (rs.Length > 0) { b = rs[0].bounds; for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds); }
                size = b.size.y > 0.01f ? b.size : new Vector3(1f, 1f, 1f);
                prefabSizes[prefab] = size;
                Kill(probe);
            }
            LayerGeometry.OrientedBox(it.p, out var c, out var axis, out float hl, out float hs);
            float yaw = Mathf.Atan2(axis.x, axis.y) * Mathf.Rad2Deg;          // prefab +Z along the long axis
            float width = Mathf.Max(1f, hs * 2f), length = Mathf.Max(1f, hl * 2f);
            // A model longer in X than in Z is turned a quarter so its long side lies along the footprint's long side (less stretching).
            float sx, sz;
            if (size.x > size.z * 1.05f) { yaw -= 90f; sx = length / Mathf.Max(0.01f, size.x); sz = width / Mathf.Max(0.01f, size.z); }
            else { sx = width / Mathf.Max(0.01f, size.x); sz = length / Mathf.Max(0.01f, size.z); }
            GroundSpan(ground, it, out float low, out float mid);
            var go = UnityEngine.Object.Instantiate(prefab, parent);
            go.name = prefab.name;
            go.transform.SetPositionAndRotation(new Vector3(c.x, low - 0.3f, c.y), Quaternion.Euler(0f, yaw, 0f));
            go.transform.localScale = new Vector3(sx, (it.height + 0.3f) / Mathf.Max(0.01f, size.y), sz);
            BuildingPalette.TintPrefab(go, it.hash, it.type);                  // wall tint from the building's hash, through a property block
        }

        IEnumerator BuildFar(Transform parent, List<LayerFarBuilding> far, Material wall, Material roof, Action<float, string> progress)
        {
            var farRoot = new GameObject("Far buildings").transform;
            farRoot.SetParent(parent, false);
            var groups = new Dictionary<long, List<LayerFarBuilding>>();
            foreach (var f in far)
            {
                long key = CellKey(Mathf.FloorToInt(f.x / FarCellMetres), Mathf.FloorToInt(f.z / FarCellMetres));
                if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<LayerFarBuilding>();
                list.Add(f);
            }
            var timer = new Timer(true);
            foreach (var g in groups)
            {
                var list = g.Value;
                float ox = Mathf.Floor(list[0].x / FarCellMetres) * FarCellMetres + FarCellMetres * 0.5f, oz = Mathf.Floor(list[0].z / FarCellMetres) * FarCellMetres + FarCellMetres * 0.5f;
                var pm = new PrismMesh();
                foreach (var f in list)
                {
                    FarSize(f.type, out float len, out float wid);
                    float yaw = LayerScatter.Rand(Mathf.RoundToInt(f.x), Mathf.RoundToInt(f.z), 0x4A) * Mathf.PI;
                    var axis = new Vector2(Mathf.Cos(yaw), Mathf.Sin(yaw));
                    float gy = Context.surround.Height(f.x, f.z);
                    Vector2? wallUv = null, roofUv = null;
                    if (Context.kit.UsesPalette) { int fh = (int)LayerScatter.Hash(Mathf.RoundToInt(f.x), Mathf.RoundToInt(f.z), 0x4B); var ft = TerrainLayerKit.TypeOf(f.type); wallUv = BuildingPalette.WallUv(BuildingPalette.WallIndex(fh, ft)); roofUv = BuildingPalette.RoofUv(BuildingPalette.RoofIndex(fh, ft)); }
                    pm.AddPrism(LayerGeometry.BoxRing(new Vector2(f.x, f.z), axis, len * 0.5f, wid * 0.5f), null, gy - 0.5f, gy + Mathf.Max(2.5f, f.height), ox, oz, wallUv, roofUv);
                }
                var go = new GameObject($"Far {list.Count}");
                go.transform.SetParent(farRoot, false);
                go.transform.position = new Vector3(ox, 0f, oz);
                var r = MakeRenderer(go.transform, "Boxes", pm.ToMesh("Far buildings"), wall, roof);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (timer.Due) { yield return null; timer.Restart(); }
            }
        }

        static void FarSize(string type, out float length, out float width)
        {
            switch (type)
            {
                case "apartments": length = 18f; width = 14f; break;
                case "commercial": length = 20f; width = 14f; break;
                case "industrial": length = 26f; width = 16f; break;
                case "religious": length = 14f; width = 12f; break;
                case "shed": length = 6f; width = 5f; break;
                case "house": length = 10f; width = 8f; break;
                default: length = 11f; width = 9f; break;
            }
        }

        // ================= Reporting =================

        // One line per layer, e.g. for the session file and the HTML report.
        public string Summary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (LayerKind k in Enum.GetValues(typeof(LayerKind)))
            {
                var r = Reports[(int)k];
                if (sb.Length > 0) sb.Append("; ");
                sb.Append(k).Append(' ').Append(r.mode == LayerMode.Off ? "off" : r.mode + " [" + r.source + "]");
                if (r.mode != LayerMode.Off) sb.Append(' ').Append(r.count);
            }
            return sb.ToString();
        }

        // A short form for the trainer panel: "Trees OSM + sat est 120k | Grass proc | Buildings OSM 4.5k".
        public string ShortSummary()
        {
            var sb = new System.Text.StringBuilder();
            foreach (LayerKind k in Enum.GetValues(typeof(LayerKind)))
            {
                var r = Reports[(int)k];
                if (sb.Length > 0) sb.Append(" | ");
                sb.Append(k).Append(' ');
                if (r.mode == LayerMode.Off) { sb.Append("off"); continue; }
                sb.Append(r.source.Replace("satellite estimate", "sat est").Replace("procedural", "proc").Replace("filled procedurally", "filled proc"));
                if (k != LayerKind.Grass) sb.Append(' ').Append(r.count >= 10000 ? (r.count / 1000) + "k" : r.count.ToString());
            }
            return sb.ToString();
        }

        public bool UsedOsm
        {
            get { foreach (var r in Reports) if (r.usedOsm && r.mode == LayerMode.Realistic) return true; return false; }
        }

        public string Credit => UsedOsm ? LayerFile.OsmCredit : "";

        public static string CurrentSummary() => Current != null ? Current.Summary() : "";
        public static string CurrentCredit() => Current != null ? Current.Credit : "";
    }
}
