using System.Collections;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Kurukshetra.TerrainGen.EditorTools
{
    // Builds one downloaded area's terrain in the Editor and then all three layers on it, and reports the time each layer took
    // and what it made. Stepped from EditorApplication.update so a long build does not freeze the Editor.
    // Kurukshetra > Terrain > Measure layer build (Jammu_AFS); or LayerMeasure.Start("<area folder name>") from a script.
    public static class LayerMeasure
    {
        public static bool Running { get; private set; }
        public static string Status { get; private set; } = "idle";
        public static string Result { get; private set; } = "";
        static IEnumerator job;
        static GameObject keepRoot;

        [MenuItem("Kurukshetra/Terrain/Measure layer build (Jammu_AFS)")]
        static void Menu() => Start("Jammu_AFS");

        public static void Start(string area)
        {
            if (Running) return;
            Running = true;
            Result = "";
            job = Job(area);
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            var clock = Stopwatch.StartNew();
            try
            {
                while (clock.ElapsedMilliseconds < 60)
                    if (!job.MoveNext()) { Running = false; EditorApplication.update -= Tick; return; }
            }
            catch (System.Exception e)
            {
                Result = "EXCEPTION " + e;
                Running = false;
                EditorApplication.update -= Tick;
            }
        }

        static IEnumerator Job(string area)
        {
            string raw = Path.GetFullPath("Assets/Kurukshetra/Terrain/RealData/" + area + "/" + area + ".raw");
            var total = Stopwatch.StartNew();
            Status = "loading " + area;
            yield return null;
            var dem = DemReader.Load(raw);
            Imagery img = null, simg = null; ElevationGrid sdem = null;
            string sat = ImageryDownloader.PathFor(raw);
            if (File.Exists(sat)) img = ImageryReader.Load(sat);
            string sraw = ElevationDownloader.SurroundPath(raw);
            if (File.Exists(sraw))
            {
                sdem = DemReader.Load(sraw);
                string sp = ImageryDownloader.PathFor(sraw);
                if (File.Exists(sp)) simg = ImageryReader.Load(sp);
            }
            var cfg = ScriptableObject.CreateInstance<TerrainGenConfig>();
            cfg.mode = TerrainMode.RealWorldAccurate; cfg.demPath = raw; cfg.useFileCentre = true; cfg.heightmapResolution = 513; cfg.tilesPerSide = 1;
            cfg.sizeMetres = Mathf.Floor((float)System.Math.Min(dem.WidthMetres, dem.HeightMetres));
            cfg.centreLatitude = dem.CentreLatitude; cfg.centreLongitude = dem.CentreLongitude;

            Status = "building the terrain";
            yield return null;
            var clock = Stopwatch.StartNew();
            var built = TerrainBuilder.Build(cfg, dem, null, null, img);
            TerrainBuildResult around = sdem != null ? TerrainBuilder.BuildSurround(cfg, sdem, simg, built.field.centreElevation) : null;
            double terrainSec = clock.Elapsed.TotalSeconds;

            var ctx = new LayerContext
            {
                cfg = cfg, kit = TerrainLayerKit.Default, demPath = raw, playImagery = img, surroundImagery = simg,
                biome = SurfaceBiomes.Resolve(cfg.biome, built.field), centreLatitude = dem.CentreLatitude, centreLongitude = dem.CentreLongitude,
            };
            ctx.play = new LayerGround(built.field, -built.field.centreElevation);
            ctx.playTiles.AddRange(built.tiles);
            if (around != null)
            {
                ctx.surround = new LayerGround(around.field, -built.field.centreElevation - TerrainBuilder.SurroundSink);
                ctx.surroundTiles.AddRange(around.tiles);
            }
            ctx.posts.Add(Vector2.zero);
            var manager = TerrainLayerManager.Create(ctx);

            Status = "building the layers";
            clock.Restart();
            var run = manager.BuildAll((f, text) => Status = text);
            while (run.MoveNext()) yield return null;
            double layersWall = clock.Elapsed.TotalSeconds;

            int trees = 0, surroundTrees = 0, grassTiles = 0;
            foreach (var t in ctx.playTiles) trees += t.terrainData.treeInstanceCount;
            foreach (var t in ctx.surroundTiles) surroundTrees += t.terrainData.treeInstanceCount;
            var r = manager.Reports;
            Result = $"{area}: terrain + surround {terrainSec:F1} s; layers wall {layersWall:F1} s (CPU: trees {r[0].seconds:F1} s, grass {r[1].seconds:F1} s, buildings {r[2].seconds:F1} s) | "
                + $"trees [{r[0].source}] {trees} play + {surroundTrees} surround {(string.IsNullOrEmpty(r[0].detail) ? "" : "(" + r[0].detail + ")")} | grass [{r[1].source}] {r[1].count} grass cover cells | "
                + $"buildings [{r[2].source}] {r[2].count} ({r[2].detail}) | osm {(ctx.Osm == null ? "none" : ctx.Osm.CountsLine())} | picture {(img == null ? "none" : img.texture.width + " px")}";
            Debug.Log("[Layers] " + Result);
            Status = "done";
            yield return null;
            Object.DestroyImmediate(manager.gameObject);
            Object.DestroyImmediate(built.root);
            if (around != null) Object.DestroyImmediate(around.root);
            Object.DestroyImmediate(cfg);
            _ = grassTiles;
            _ = total;
        }
    }
}
