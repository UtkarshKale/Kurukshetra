using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // The trainer menu's "Preview terrain": a top-down picture of the terrain as it will be built, made on the CPU (no temporary
    // terrain, no offscreen camera): shaded relief from the preview heightfield, the satellite picture draped over it when there is
    // one, and the four layers drawn as overlays from the same placement rules the real layers use (LayerScatter / Vegetation
    // noise masks and ground limits for procedural; the area's OpenStreetMap polygons for realistic). It is a schematic of where
    // trees, bushes, grass and buildings will be, not a render of each one. Built coarse first (128 px), then refined (384 px, the cap),
    // a few milliseconds per frame, so the menu never stalls. Toggling a layer only recomposes the picture.
    public class TerrainPreview
    {
        public const int CoarseRes = 128, FinalRes = 384;
        const double SliceMs = 6.0;

        public Texture2D Texture { get; private set; }
        public bool Busy { get; private set; }
        public float Progress { get; private set; }
        public string Note { get; private set; } = "";
        public string Signature { get; private set; } = "";        // the settings this picture was built from
        public bool ShowHeight = true, ShowTrees = true, ShowBushes = true, ShowGrass = true, ShowBuildings = true;

        int res;
        Color32[] pixels;
        float[] shade;                       // hillshade 0..1.4
        Color32[] ground;                    // height tint or satellite colour
        byte[] trees, bushes, grass;         // coverage 0..255
        List<Vector2Int> buildings = new List<Vector2Int>();
        List<Vector2Int> postDots = new List<Vector2Int>();
        bool satellite;

        // Frees the big arrays and the texture (the menu keeps the object but drops what it holds).
        public void Release()
        {
            pixels = null; shade = null; ground = null; trees = bushes = grass = null;
            buildings.Clear(); postDots.Clear();
            if (Texture != null) { UnityEngine.Object.Destroy(Texture); Texture = null; }
            Signature = "";
        }

        public struct Input
        {
            public TerrainGenConfig cfg;
            public Heightfield field;
            public ElevationGrid dem;
            public Imagery picture;
            public List<Vector2> posts;
            public string signature;
        }

        public IEnumerator Build(Input inp)
        {
            Busy = true;
            Progress = 0f;
            Note = "";
            var failed = default(Exception);
            var run = BuildPasses(inp);
            while (true)
            {
                bool more;
                try { more = run.MoveNext(); }
                catch (Exception e) { failed = e; break; }
                if (!more) break;
                yield return null;
            }
            if (failed != null) Note = "The preview could not be built: " + failed.Message;
            else Signature = inp.signature;
            Busy = false;
        }

        IEnumerator BuildPasses(Input inp)
        {
            foreach (int r in new[] { CoarseRes, FinalRes })
            {
                var pass = Pass(inp, r, r == CoarseRes ? 0f : 0.2f, r == CoarseRes ? 0.2f : 0.8f);
                while (pass.MoveNext()) yield return null;
                Compose();
                yield return null;
            }
        }

        static Func<float, float, bool> NoBlock => null;

        IEnumerator Pass(Input inp, int r, float p0, float pSpan)
        {
            var cfg = inp.cfg;
            var f = inp.field;
            var L = cfg.terrainLayers;
            bool real = cfg.UsesDem;
            var biome = SurfaceBiomes.Resolve(cfg.biome, f);
            var g = new LayerGround(f, 0f);
            float size = cfg.sizeMetres, half = size * 0.5f, cell = size / r;
            int treeSeed = cfg.seed ^ 0x7EE5, grassSeed = cfg.seed ^ 0x6A55;
            var sw = Stopwatch.StartNew();

            var newGround = new Color32[r * r];
            var newShade = new float[r * r];
            var newTrees = new byte[r * r];
            var newBushes = new byte[r * r];
            var newGrass = new byte[r * r];

            // Satellite drape: the picture's place on the terrain, as the real terrain colouring uses it.
            Rect ext = default;
            bool sat = inp.picture != null && inp.picture.texture != null && cfg.imageryBlend > 0f;
            if (sat) ext = ImageryReader.Extent(cfg, inp.dem, inp.picture);
            satellite = sat;
            float range = Mathf.Max(1f, f.max - f.min);

            // Realistic layers need the area's map data (rasterised once per pass at this resolution).
            var mTrees = L.Effective(LayerKind.Trees, real);
            var mBushes = L.Effective(LayerKind.Bushes, real);
            var mGrass = L.Effective(LayerKind.Grass, real);
            var mBuild = L.Effective(LayerKind.Buildings, real);
            LayerFile osm = null;
            bool needOsm = real && (mTrees == LayerMode.Realistic || mBushes == LayerMode.Realistic || mGrass == LayerMode.Realistic || mBuild == LayerMode.Realistic);
            if (needOsm && !string.IsNullOrEmpty(cfg.demPath)) osm = LayerFile.Load(cfg.demPath);
            CoverGrid cover = null;
            var notes = new List<string>();
            if (needOsm && osm == null) notes.Add("no map data for this area: realistic layers show nothing");
            if (osm != null)
            {
                cover = new CoverGrid(half, cell);
                foreach (var poly in osm.grass) { if (poly.p == null || poly.p.Length < 6) continue; cover.Rasterize(poly, poly.kind == "farmland" ? CoverGrid.OsmFarm : CoverGrid.OsmGrass); if (sw.Elapsed.TotalMilliseconds > SliceMs) { yield return null; sw.Restart(); } }
                foreach (var poly in osm.forests) { if (poly.p == null || poly.p.Length < 6) continue; cover.Rasterize(poly, poly.kind == "orchard" ? CoverGrid.OsmOrchard : CoverGrid.OsmForest); if (sw.Elapsed.TotalMilliseconds > SliceMs) { yield return null; sw.Restart(); } }
            }

            float treeScale = Mathf.Clamp01(L.treeDensity / 60f) * LayerScatter.ClimateTreeFactor(biome);
            float treeScaleCap = Mathf.Max(treeScale, 0.15f);
            float bushScale = Mathf.Clamp01(L.bushDensity);
            Vector2 post0 = inp.posts != null && inp.posts.Count > 0 ? inp.posts[0] : Vector2.zero;
            float start = L.falloffStartKm * 1000f;

            for (int iz = 0; iz < r; iz++)
            {
                float z = -half + (iz + 0.5f) * cell;
                for (int ix = 0; ix < r; ix++)
                {
                    float x = -half + (ix + 0.5f) * cell;
                    int i = iz * r + ix;
                    float e = g.Elevation(x, z);
                    float h = Mathf.Clamp01((e - f.min) / range);
                    float sx = g.Elevation(x + cell, z) - g.Elevation(x - cell, z), sz = g.Elevation(x, z + cell) - g.Elevation(x, z - cell);
                    newShade[i] = Mathf.Clamp(0.75f + (sz - sx) / (4f * cell), 0.1f, 1.5f);
                    Color low = new Color(0.35f, 0.5f, 0.3f), mid = new Color(0.55f, 0.45f, 0.32f);
                    Color tint = Color.Lerp(Color.Lerp(low, mid, Mathf.Clamp01(h * 2f)), Color.white, Mathf.Clamp01(h * 2f - 1f));
                    Color col = tint;
                    if (sat)
                    {
                        float u = (x - ext.xMin) / ext.width, v = (z - ext.yMin) / ext.height;
                        if (u >= 0f && u <= 1f && v >= 0f && v <= 1f) col = Color.Lerp(tint, inp.picture.texture.GetPixelBilinear(u, v), Mathf.Clamp01(cfg.imageryBlend));
                    }
                    newGround[i] = col;

                    float slope = 0f;
                    bool needSlope = mTrees == LayerMode.Procedural || mBushes != LayerMode.Off || mGrass == LayerMode.Procedural;
                    if (needSlope) slope = g.SlopeDegrees(x, z);
                    float fall = LayerFalloff.Of(Mathf.Sqrt((x - post0.x) * (x - post0.x) + (z - post0.y) * (z - post0.y)), start);

                    // Trees.
                    float t = 0f;
                    if (mTrees == LayerMode.Procedural)
                        t = LayerScatter.WoodMask(x, z, treeSeed) * LayerScatter.TreeGroundFactor(slope, e) * treeScaleCap;
                    else if (mTrees == LayerMode.Realistic && cover != null)
                    {
                        byte c = cover.At(x, z);
                        t = c == CoverGrid.OsmForest ? Mathf.Clamp01(L.treeDensity / 60f) : c == CoverGrid.OsmOrchard ? 0.5f * Mathf.Clamp01(L.treeDensity / 60f) : 0f;
                    }
                    newTrees[i] = (byte)(Mathf.Clamp01(t * Mathf.Lerp(0.6f, 1f, fall)) * 255f);

                    // Bushes: along wood edges and thinly in the open (procedural); at the edge of mapped woods (realistic).
                    float b = 0f;
                    if (mBushes == LayerMode.Procedural) b = Vegetation.OpenBushFactor(g, x, z, treeSeed, biome) * bushScale;
                    else if (mBushes == LayerMode.Realistic && cover != null)
                    {
                        byte c = cover.At(x, z);
                        bool wood = c == CoverGrid.OsmForest || c == CoverGrid.OsmOrchard;
                        bool edge = wood && (cover.At(x + cell * 1.5f, z) != c || cover.At(x - cell * 1.5f, z) != c || cover.At(x, z + cell * 1.5f) != c || cover.At(x, z - cell * 1.5f) != c);
                        b = edge ? 0.9f * bushScale : c == CoverGrid.OsmGrass ? 0.12f * bushScale : 0f;
                    }
                    newBushes[i] = (byte)(Mathf.Clamp01(b) * 255f);

                    // Grass.
                    float gr = 0f;
                    if (mGrass == LayerMode.Procedural) gr = LayerScatter.GrassCover(g, x, z, grassSeed, biome) * L.grassDensity;
                    else if (mGrass == LayerMode.Realistic && cover != null)
                    {
                        byte c = cover.At(x, z);
                        gr = c == CoverGrid.OsmGrass || c == CoverGrid.OsmFarm ? L.grassDensity : 0f;
                    }
                    newGrass[i] = (byte)(Mathf.Clamp01(gr) * 255f);
                }
                if (sw.Elapsed.TotalMilliseconds > SliceMs)
                {
                    Progress = p0 + pSpan * (0.9f * iz / r);
                    yield return null;
                    sw.Restart();
                }
            }

            // Buildings: dots (a village house is under a pixel at this scale; a dot per house keeps the villages visible).
            var dots = new List<Vector2Int>();
            Vector2Int Px(float x, float z) => new Vector2Int(Mathf.Clamp((int)((x + half) / cell), 0, r - 1), Mathf.Clamp((int)((z + half) / cell), 0, r - 1));
            if (mBuild == LayerMode.Procedural)
            {
                var spots = new List<BuildingSpot>();
                LayerScatter.ProceduralBuildings(g, cfg.seed ^ 0x0B1D, L.buildingDensity, NoBlock, spots);
                foreach (var s in spots) dots.Add(Px(s.x, s.z));
            }
            else if (mBuild == LayerMode.Realistic && osm != null)
            {
                foreach (var bd in osm.buildings) if (bd.p != null && bd.p.Length >= 6) dots.Add(Px(Centroid(bd.p, 0), Centroid(bd.p, 1)));
                foreach (var fb in osm.farBuildings) if (Mathf.Abs(fb.x) < half && Mathf.Abs(fb.z) < half) dots.Add(Px(fb.x, fb.z));
            }
            if (mTrees == LayerMode.Realistic && osm != null)
            {
                // Single mapped trees are real points; give each a pixel of tree.
                foreach (var tp in osm.trees)
                    if (Mathf.Abs(tp.x) < half && Mathf.Abs(tp.z) < half) { var p = Px(tp.x, tp.z); int i = p.y * r + p.x; newTrees[i] = (byte)Mathf.Max(newTrees[i], 200); }
            }
            var posts = new List<Vector2Int>();
            if (inp.posts != null) foreach (var p in inp.posts) posts.Add(Px(p.x, p.y));

            Note = notes.Count > 0 ? string.Join("; ", notes) : "";

            // Swap in the finished pass.
            res = r; ground = newGround; shade = newShade; trees = newTrees; bushes = newBushes; grass = newGrass;
            buildings = dots; postDots = posts;
            if (Texture == null || Texture.width != r) { if (Texture != null) UnityEngine.Object.Destroy(Texture); Texture = new Texture2D(r, r, TextureFormat.RGB24, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "TerrainPreview" }; }
            pixels = new Color32[r * r];
            Progress = p0 + pSpan;
        }

        static float Centroid(float[] p, int axis)
        {
            float s = 0f; int n = p.Length / 2;
            for (int i = 0; i < n; i++) s += p[i * 2 + axis];
            return s / Mathf.Max(1, n);
        }

        // Layer toggles only need this: no placement is recomputed.
        public void Compose()
        {
            if (pixels == null || Texture == null) return;
            int n = res;
            for (int i = 0; i < n * n; i++)
            {
                Color c = ground[i];
                if (ShowHeight) c *= (0.35f + 0.9f * Mathf.Clamp01(shade[i] * 0.85f)); else if (!satellite) c = Color.Lerp(c, new Color(0.45f, 0.5f, 0.42f), 0.8f);
                if (ShowGrass && grass[i] > 0) c = Color.Lerp(c, new Color(0.5f, 0.75f, 0.28f), grass[i] / 255f * 0.5f);
                if (ShowBushes && bushes[i] > 0) c = Color.Lerp(c, new Color(0.45f, 0.5f, 0.12f), bushes[i] / 255f * 0.75f);
                if (ShowTrees && trees[i] > 0) c = Color.Lerp(c, new Color(0.06f, 0.26f, 0.1f), trees[i] / 255f * 0.9f);
                c.a = 1f;
                pixels[i] = c;
            }
            if (ShowBuildings)
                foreach (var d in buildings) Dot(d.x, d.y, new Color32(235, 225, 205, 255), 1);
            foreach (var d in postDots) Dot(d.x, d.y, new Color32(255, 255, 255, 255), 2);
            Texture.SetPixels32(pixels);
            Texture.Apply(false);
        }

        void Dot(int x, int z, Color32 c, int radius)
        {
            for (int dz = -radius; dz <= radius; dz++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int px = x + dx, pz = z + dz;
                if (px < 0 || pz < 0 || px >= res || pz >= res) continue;
                pixels[pz * res + px] = c;
            }
        }
    }
}
