using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // Acceptance checks for the species rules, the bush layer and the building palette. Run with the CLI:
    // Chaukanna.TerrainGen.VegetationSelfTest.Run()
    // Synthetic ground only (flat and a ramp); the kit check reads the shipped kit asset, which Chaukanna > Terrain > Wire real art into kit fills.
    public static class VegetationSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var cleanup = new List<UnityEngine.Object>();
            try
            {
                KitChecks();
                SpeciesChecks();
                BushChecks();
                PaletteChecks(cleanup);
                ManagerChecks(cleanup);
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
            string summary = $"[VegetationSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }

        static bool AllUsable(List<GameObject> l, int min)
        {
            if (l == null || l.Count < min) return false;
            foreach (var g in l) if (g == null) return false;
            return true;
        }

        // --- 1. The shipped kit ---

        static void KitChecks()
        {
            var kit = TerrainLayerKit.Default;
            Check($"kit: all four tree species are filled ({kit.broadleafTrees.Count}/{kit.pineTrees.Count}/{kit.snowTrees.Count}/{kit.deadTrees.Count})",
                AllUsable(kit.broadleafTrees, 3) && AllUsable(kit.pineTrees, 2) && AllUsable(kit.snowTrees, 1) && AllUsable(kit.deadTrees, 1) && kit.HasSpeciesGroups);
            var table = kit.BuildTreeTable();
            int total = 0; foreach (var l in table.byClass) total += l.Count;
            Check("kit: the tree table lists every species once and every entry has a trunk collider and a far impostor",
                table.classed && table.prefabs.Count == total && AllHave<CapsuleCollider>(table.prefabs) && AllLod(table.prefabs, 2));
            bool bushesOk = AllUsable(kit.bushPrefabs, 2) && AllUsable(kit.bushSnowPrefabs, 1);
            bool instanced = true, meshes = true;
            if (bushesOk)
                foreach (var list in new[] { kit.bushPrefabs, kit.bushSnowPrefabs })
                    foreach (var g in list)
                    {
                        var m = BushModel.From(g);
                        if (m == null) { meshes = false; continue; }
                        foreach (var mat in m.materials) if (mat == null || !mat.enableInstancing) instanced = false;
                    }
            Check($"kit: {kit.bushPrefabs.Count} bushes and {kit.bushSnowPrefabs.Count} snow bushes, each with a mesh and GPU-instanced materials", bushesOk && meshes && instanced);
            Check("kit: procedural villages have Kenney prefabs for houses, sheds, apartments, commercial and industrial; real footprints stay extruded",
                kit.HasBuildingPrefab(BuildingType.House) && kit.HasBuildingPrefab(BuildingType.Shed) && kit.HasBuildingPrefab(BuildingType.Apartments)
                && kit.HasBuildingPrefab(BuildingType.Commercial) && kit.HasBuildingPrefab(BuildingType.Industrial) && !kit.realisticUsesPrefabs
                && AllHave<BoxCollider>(kit.buildingPrefabs.house) && AllHave<BoxCollider>(kit.buildingPrefabs.apartments));
            Check("kit: extruded buildings use the palette (no fixed wall or roof material)", kit.UsesPalette);
        }

        static bool AllHave<T>(List<GameObject> l) where T : Component
        {
            foreach (var g in l) if (g == null || g.GetComponent<T>() == null) return false;
            return l.Count > 0;
        }

        static bool AllLod(List<GameObject> l, int levels)
        {
            foreach (var g in l) { var lod = g != null ? g.GetComponent<LODGroup>() : null; if (lod == null || lod.lodCount < levels) return false; }
            return l.Count > 0;
        }

        // --- 2. Species by place ---

        static float Share(float elevation, float slope, SurfaceBiome biome, TreeClass c, int n = 4000)
        {
            int k = 0;
            for (int i = 0; i < n; i++) if (Vegetation.TreeClassAt(elevation, slope, biome, i * 7 + 1) == c) k++;
            return k / (float)n;
        }

        static void SpeciesChecks()
        {
            Check("species: on the plains (300 m, gentle) trees are broadleaf, with no snow trees",
                Share(300f, 4f, SurfaceBiome.Temperate, TreeClass.Broadleaf) > 0.8f && Share(300f, 4f, SurfaceBiome.Temperate, TreeClass.Snow) == 0f);
            Check("species: above 1500 m pine takes over", Share(1800f, 8f, SurfaceBiome.Temperate, TreeClass.Pine) > 0.8f);
            Check("species: steep ground (28 degrees) at 300 m is pine too", Share(300f, 28f, SurfaceBiome.Temperate, TreeClass.Pine) > 0.8f);
            float snow = Share(3000f, 8f, SurfaceBiome.Temperate, TreeClass.Snow), dead = Share(3000f, 8f, SurfaceBiome.Temperate, TreeClass.Dead);
            Check($"species: near the snow ({snow * 100f:F0} % snow trees, {dead * 100f:F0} % dead at 3000 m) there are snow-covered and a few dead trees", snow > 0.3f && dead > 0.01f && dead < 0.2f);
            Check("species: at the tree line (3600 m) dead and snow trees dominate", Share(3600f, 8f, SurfaceBiome.Temperate, TreeClass.Dead) + Share(3600f, 8f, SurfaceBiome.Temperate, TreeClass.Snow) > 0.8f);
            Check("species: dry country has more dead trees than green country",
                Share(300f, 4f, SurfaceBiome.Desert, TreeClass.Dead) > Share(300f, 4f, SurfaceBiome.Temperate, TreeClass.Dead) * 2f);
            bool same = true;
            for (int i = 0; i < 200; i++) if (Vegetation.TreeClassAt(2100f, 12f, SurfaceBiome.Temperate, i) != Vegetation.TreeClassAt(2100f, 12f, SurfaceBiome.Temperate, i)) same = false;
            Check("species: the same tree number gives the same species every time", same);

            var table = new TerrainLayerKit.TreeTable();
            var a = new GameObject("a"); var b = new GameObject("b");
            try
            {
                table.prefabs.Add(a); table.prefabs.Add(b);
                table.byClass[(int)TreeClass.Broadleaf].Add(0); table.byClass[(int)TreeClass.Pine].Add(1);
                Check("species: a class with no tree falls back to broadleaf, then pine",
                    table.Pick(TreeClass.Snow, 5) == 0 && table.Pick(TreeClass.Dead, 9) == 0 && table.Pick(TreeClass.Pine, 3) == 1);
                table.byClass[(int)TreeClass.Broadleaf].Clear();
                Check("species: with no broadleaf either, pine is used", table.Pick(TreeClass.Dead, 2) == 1);
            }
            finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
        }

        // --- 3. Bushes ---

        static List<TreeSpot> Grid(int n, float step)
        {
            var l = new List<TreeSpot>();
            for (int i = 0; i < n; i++) l.Add(new TreeSpot { x = (i % 100) * step - 500f, z = (i / 100) * step - 500f, height = 10f, width = 1f });
            return l;
        }

        static void BushChecks()
        {
            var trees = Grid(6000, 30f);
            var ring = new List<BushSpot>();
            Vegetation.RoundTrees(trees, 1f, 11, null, ring);
            // distance of each bush to its tree, and trees with bushes
            var perTree = new Dictionary<int, List<BushSpot>>();
            bool rangeOk = true;
            foreach (var b in ring)
            {
                // The tree is the nearest one: the grid is 30 m apart and a ring reaches 8 m, so the nearest tree is its own.
                int ix = Mathf.RoundToInt((b.x + 500f) / 30f), iz = Mathf.RoundToInt((b.z + 500f) / 30f);
                int idx = iz * 100 + ix;
                if (idx < 0 || idx >= trees.Count) { rangeOk = false; continue; }
                var t = trees[idx];
                float d = Mathf.Sqrt((b.x - t.x) * (b.x - t.x) + (b.z - t.z) * (b.z - t.z));
                if (d < Vegetation.RingMinMetres - 0.01f || d > Vegetation.RingMaxMetres + 0.01f) rangeOk = false;
                if (!perTree.TryGetValue(idx, out var l)) perTree[idx] = l = new List<BushSpot>();
                l.Add(b);
            }
            float share = perTree.Count / (float)trees.Count, mean = ring.Count / (float)Mathf.Max(1, perTree.Count);
            Check($"bushes: each ring bush stands 3 to 8 m from its tree; {share * 100f:F0} % of trees get a ring of {mean:F1} on average (2 to 5)",
                rangeOk && ring.Count > 0 && share > 0.30f && share < 0.40f && mean > 2.8f && mean < 4.2f);

            float minGap = float.MaxValue;
            foreach (var kv in perTree)
                for (int i = 0; i < kv.Value.Count; i++)
                for (int j = i + 1; j < kv.Value.Count; j++)
                {
                    var p = kv.Value[i]; var q = kv.Value[j];
                    minGap = Mathf.Min(minGap, Mathf.Sqrt((p.x - q.x) * (p.x - q.x) + (p.z - q.z) * (p.z - q.z)));
                }
            Check($"bushes: two bushes of one ring never touch (closest pair {minGap:F1} m)", minGap > 1.2f);

            var none = new List<BushSpot>(); Vegetation.RoundTrees(trees, 0f, 11, null, none);
            var triple = new List<BushSpot>(); Vegetation.RoundTrees(trees, 3f, 11, null, triple);
            var again = new List<BushSpot>(); Vegetation.RoundTrees(trees, 1f, 11, null, again);
            bool deterministic = again.Count == ring.Count;
            for (int i = 0; deterministic && i < ring.Count; i++) if (ring[i].x != again[i].x || ring[i].z != again[i].z || ring[i].hash != again[i].hash) deterministic = false;
            Check($"bushes: density 0 gives none, density 3 gives about three times as many ({triple.Count} vs {ring.Count}), the same seed gives the same bushes",
                none.Count == 0 && triple.Count > ring.Count * 2.5f && triple.Count < ring.Count * 3.6f && deterministic);

            var blocked = new List<BushSpot>(); Vegetation.RoundTrees(trees, 1f, 11, (x, z) => x < 0f, blocked);
            bool clear = true; foreach (var b in blocked) if (b.x < 0f) clear = false;
            Check("bushes: ground the caller blocks (the post, the site) gets none", blocked.Count > 0 && blocked.Count < ring.Count && clear);

            // The open scatter, over a flat synthetic ground, with a budget.
            var ground = LayerGround.Synthetic(2000f, 41, (x, z) => 300f);
            var scatter = new List<BushSpot>();
            for (int r = 0; r < Vegetation.ScatterRows(ground); r++) Vegetation.ScatterRow(ground, r, 1f, 5, 9, SurfaceBiome.Temperate, Vector2.zero, 1500f, null, scatter);
            var scatter2 = new List<BushSpot>();
            for (int r = 0; r < Vegetation.ScatterRows(ground); r++) Vegetation.ScatterRow(ground, r, 1f, 5, 9, SurfaceBiome.Temperate, Vector2.zero, 1500f, null, scatter2);
            bool inside = true; foreach (var b in scatter) if (!ground.Contains(b.x, b.z)) inside = false;
            Check($"bushes: a scatter of {scatter.Count} bushes along wood edges and in the open, inside the ground, the same every time", scatter.Count > 100 && inside && scatter2.Count == scatter.Count);
            var dense = new List<BushSpot>();
            for (int r = 0; r < Vegetation.ScatterRows(ground); r++) Vegetation.ScatterRow(ground, r, 4f, 5, 9, SurfaceBiome.Temperate, Vector2.zero, 1500f, null, dense);
            Check($"bushes: the scatter density is typeable ({dense.Count} at 4 against {scatter.Count} at 1)", dense.Count > scatter.Count * 3f);

            var budget = new List<BushSpot>(scatter);
            string note = Vegetation.ApplyBudget(budget, 50, Vector2.zero);
            float farthestKept = 0f; foreach (var b in budget) farthestKept = Mathf.Max(farthestKept, Mathf.Sqrt(b.x * b.x + b.z * b.z));
            int closerCut = 0; foreach (var b in scatter) if (Mathf.Sqrt(b.x * b.x + b.z * b.z) < farthestKept - 0.01f) closerCut++;
            var unlimited = new List<BushSpot>(scatter);
            string noteNone = Vegetation.ApplyBudget(unlimited, 0, Vector2.zero);
            var huge = new List<BushSpot>(scatter);
            Vegetation.ApplyBudget(huge, int.MaxValue, Vector2.zero);
            Check("bushes: a budget keeps the nearest ones and says so; 0 and a huge number cut nothing",
                budget.Count == 50 && note.Contains("thinned") && closerCut <= 50 && unlimited.Count == scatter.Count && noteNone.Length == 0 && huge.Count == scatter.Count);
        }

        // --- 4. Building palette ---

        static void PaletteChecks(List<UnityEngine.Object> cleanup)
        {
            var wallSeen = new HashSet<int>(); var roofSeen = new HashSet<int>();
            bool stable = true, factoryGrey = true;
            for (int h = 0; h < 3000; h++)
            {
                wallSeen.Add(BuildingPalette.WallIndex(h, BuildingType.House)); roofSeen.Add(BuildingPalette.RoofIndex(h, BuildingType.House));
                if (BuildingPalette.WallIndex(h, BuildingType.House) != BuildingPalette.WallIndex(h, BuildingType.House) || BuildingPalette.RoofIndex(h, BuildingType.House) != BuildingPalette.RoofIndex(h, BuildingType.House)) stable = false;
                int fw = BuildingPalette.WallIndex(h, BuildingType.Industrial);
                if (fw != 9 && fw != 10 && fw != 11 && fw != 7) factoryGrey = false;
            }
            Check($"palette: houses get {wallSeen.Count} wall colours and {roofSeen.Count} roof colours, a building always gets the same ones", wallSeen.Count >= 8 && roofSeen.Count >= 5 && stable);
            Check("palette: factories stay grey and sandy", factoryGrey);
            Check("palette: the swatch coordinates point at the matching texture pixel",
                Mathf.Approximately(BuildingPalette.WallUv(3).x * BuildingPalette.Columns, 3.5f) && BuildingPalette.WallUv(0).y < 0.5f && BuildingPalette.RoofUv(0).y > 0.5f
                && BuildingPalette.Texture().GetPixel(3, 0) == (Color)BuildingPalette.Walls[3] && BuildingPalette.Texture().GetPixel(2, 1) == (Color)BuildingPalette.Roofs[2]);
            Check("palette: one shared material for all extruded buildings", BuildingPalette.SharedMaterial() == BuildingPalette.SharedMaterial() && BuildingPalette.SharedMaterial().enableInstancing);

            // Two buildings with different hashes on one mesh keep their own swatches.
            var pm = new PrismMesh();
            var ring = new[] { 0f, 0f, 10f, 0f, 10f, 10f, 0f, 10f };
            var w0 = BuildingPalette.WallUv(2); var r0 = BuildingPalette.RoofUv(5);
            pm.AddPrism(ring, null, 0f, 5f, 0f, 0f, w0, r0);
            pm.AddPrism(ring, null, 0f, 5f, 20f, 0f, BuildingPalette.WallUv(7), BuildingPalette.RoofUv(1));
            var mesh = pm.ToMesh("palette test");
            cleanup.Add(mesh);
            var uvs = new List<Vector2>(); mesh.GetUVs(0, uvs);
            bool wallsOk = true, roofsOk = true;
            foreach (int i in mesh.GetTriangles(0)) { if (uvs[i] != w0 && uvs[i] != BuildingPalette.WallUv(7)) wallsOk = false; }
            foreach (int i in mesh.GetTriangles(1)) { if (uvs[i] != r0 && uvs[i] != BuildingPalette.RoofUv(1)) roofsOk = false; }
            Check("palette: the wall and roof triangles of a prism carry that building's swatch", wallsOk && roofsOk && mesh.GetTriangles(0).Length > 0 && mesh.GetTriangles(1).Length > 0);

            // A prefab building is tinted through a property block, not a new material.
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cleanup.Add(cube);
            var mat0 = cube.GetComponent<Renderer>().sharedMaterial;
            BuildingPalette.TintPrefab(cube, 4, BuildingType.House);
            var mpb = new MaterialPropertyBlock();
            cube.GetComponent<Renderer>().GetPropertyBlock(mpb);
            var tint = mpb.GetColor("_BaseColor");
            Check("palette: a prefab building gets its wall tint from a property block and keeps its material", cube.GetComponent<Renderer>().sharedMaterial == mat0 && Mathf.Approximately(tint.r, BuildingPalette.PrefabTint(4, BuildingType.House).r) && tint.r > 0.4f);
        }

        // --- 5. The manager with the shipped kit ---

        static void ManagerChecks(List<UnityEngine.Object> cleanup)
        {
            var kit = TerrainLayerKit.Default;
            var ctx = LayersSelfTest.SmallWorld(cleanup, kit, false);
            ctx.Settings.treeDensity = 120f;
            ctx.Settings.bushDensity = 1f;
            var manager = TerrainLayerManager.Create(ctx);
            cleanup.Add(manager.gameObject);
            manager.BuildAllNow();
            var trees = manager.ReportOf(LayerKind.Trees);
            int expectedProtos = kit.BuildTreeTable().prefabs.Count;
            Check($"manager: terrain trees use the species table ({ctx.playTiles[0].terrainData.treePrototypes.Length} prototypes), the report lists the species: {trees.detail}",
                ctx.playTiles[0].terrainData.treePrototypes.Length == expectedProtos && trees.detail.Contains("species:") && ctx.playTiles[0].terrainData.treeInstanceCount > 100);

            var bushes = manager.ReportOf(LayerKind.Bushes);
            var field = manager.transform.Find("Bushes") != null ? manager.transform.Find("Bushes").GetComponent<BushField>() : null;
            Check($"manager: the bush layer builds ({bushes.count} bushes: {bushes.count2} round trees, {bushes.count3} scattered), source '{bushes.source}'",
                bushes.mode == LayerMode.Procedural && bushes.source == "procedural" && bushes.count > 200 && bushes.count2 > 50 && field != null && field.Total == bushes.count);

            // Draw from a camera at the post: instanced, culled, counted.
            var camGo = new GameObject("VegetationSelfTest camera");
            cleanup.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 90f; cam.farClipPlane = 600f;
            camGo.transform.position = new Vector3(0f, ctx.play.Height(0f, 0f) + 2f, 0f);
            int best = 0;
            for (int yaw = 0; yaw < 360; yaw += 90)
            {
                camGo.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                field.Draw(cam);
                best = Mathf.Max(best, field.Drawn);
            }
            int near = field.BushesNear(Vector2.zero, field.radius);
            Check($"manager: the field draws {best} instanced bushes toward one side of a camera (of {near} within {field.radius:F0} m), and only some cells are kept in memory ({field.CachedCells})",
                best > 0 && best <= field.Total && near > 0 && field.CachedCells < 400);

            // Budget, Off, density.
            ctx.Settings.bushBudget = 40;
            manager.RebuildNow(LayerKind.Bushes);
            Check("manager: a bush budget of 40 leaves 40 and the report says so", manager.ReportOf(LayerKind.Bushes).count == 40 && manager.ReportOf(LayerKind.Bushes).detail.Contains("thinned"));
            ctx.Settings.bushBudget = 0;
            ctx.Settings.bushDensity = 2f;
            manager.RebuildNow(LayerKind.Bushes);
            int dense = manager.ReportOf(LayerKind.Bushes).count;
            ctx.Settings.bushDensity = 0f;
            manager.RebuildNow(LayerKind.Bushes);
            int zero = manager.ReportOf(LayerKind.Bushes).count;
            Check($"manager: bush density is typeable ({dense} at 2, {zero} at 0), no upper limit", dense > bushes.count && zero == 0 && manager.transform.Find("Bushes") == null);
            ctx.Settings.bushDensity = 1f;
            ctx.Settings.bushes = LayerMode.Off;
            manager.RebuildNow(LayerKind.Bushes);
            Check("manager: bushes Off removes the layer and leaves trees, grass and buildings", manager.transform.Find("Bushes") == null && manager.ReportOf(LayerKind.Bushes).source == "off" && ctx.playTiles[0].terrainData.treeInstanceCount > 100 && manager.transform.Find("Buildings") != null);
            ctx.Settings.bushes = LayerMode.Procedural;
            manager.RebuildNow(LayerKind.Bushes);
            Check("manager: bushes back on gives the same count again", manager.ReportOf(LayerKind.Bushes).count == bushes.count);

            // Villages with the shipped kit use Kenney prefabs, boxes are the fallback.
            var buildings = manager.ReportOf(LayerKind.Buildings);
            var cell = manager.transform.Find("Buildings");
            int prefabs = 0;
            if (cell != null) foreach (Transform c in cell) foreach (Transform t in c) if (t.name.StartsWith("Suburban_") || t.name.StartsWith("Commercial_")) prefabs++;
            Check($"manager: procedural villages place Kenney prefabs ({prefabs} of {buildings.count} buildings)", buildings.count > 0 && buildings.count2 > 0 && prefabs == buildings.count2);
        }
    }
}
