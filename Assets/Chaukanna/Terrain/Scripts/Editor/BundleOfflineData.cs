using System.IO;
using Chaukanna.TerrainGen;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Chaukanna.TerrainGen.EditorTools
{
    // CRIT-1: a build copied to another PC showed "Map offline" and had none of the downloaded areas. After every build,
    // this copies the map tiles cached on this PC and the downloaded real-terrain areas next to the game, in
    // <build folder>/ChaukannaData/, so the copied build works with no internet. Keys (carto.key, arcgis.key) are never copied.
    public class BundleOfflineData : IPostprocessBuildWithReport
    {
        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platformGroup != BuildTargetGroup.Standalone) return;
            string buildDir = Path.GetDirectoryName(report.summary.outputPath);
            if (string.IsNullOrEmpty(buildDir)) return;
            string bundle = Path.Combine(buildDir, ElevationDownloader.BundleFolderName);
            long tiles = Copy(ElevationDownloader.CacheFolder, Path.Combine(bundle, "tiles"), false);
            long areas = Copy(Path.GetFullPath(Path.Combine(Application.dataPath, "Chaukanna", "Terrain", "RealData")), Path.Combine(bundle, "RealData"), true);
            Debug.Log($"[Build] Offline data copied to {bundle}: map tiles {tiles / 1024} KB, downloaded areas {areas / 1024} KB. Copy the whole build folder to the other PC.");
        }

        static long Copy(string from, string to, bool skipMeta)
        {
            if (!Directory.Exists(from)) return 0;
            long bytes = 0;
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(file);
                if (skipMeta && name.EndsWith(".meta")) continue;
                if (name.EndsWith(".key")) continue;
                if (skipMeta && (name.EndsWith("_osm.json") || name.EndsWith("_osm_surround.json"))) continue;     // raw OpenStreetMap answers: large, and the build only needs <area>_layers.json
                string target = Path.Combine(to, file.Substring(from.Length).TrimStart('\\', '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
                bytes += new FileInfo(file).Length;
            }
            return bytes;
        }
    }
}
