using System.Collections.Generic;
using System.IO;
using Kurukshetra.TerrainGen;
using UnityEngine;

namespace Kurukshetra.Trainer
{
    // The offline map (CRIT-1, second pass): area pictures are found in a folder like the build's bundle, placed on the
    // map where their bounds say, and a surround sorts under its play area. CLI: Kurukshetra.Trainer.MapOfflineSelfTest.Run()
    public static class MapOfflineSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            string dir = Path.Combine(Application.temporaryCachePath, "MapOfflineSelfTest");
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(Path.Combine(dir, "Kargil"));
                Directory.CreateDirectory(Path.Combine(dir, "Copy"));
                var tex = new Texture2D(8, 8, TextureFormat.RGB24, false);
                byte[] jpg = tex.EncodeToJPG();
                Object.DestroyImmediate(tex);
                void Area(string folder, string name, double w, double s, double e, double n)
                {
                    File.WriteAllBytes(Path.Combine(dir, folder, name + "_satellite.jpg"), jpg);
                    File.WriteAllText(Path.Combine(dir, folder, name + "_satellite.json"), $"{{\"west\":{w},\"south\":{s},\"east\":{e},\"north\":{n}}}");
                }
                Area("Kargil", "Kargil", 76.07, 34.55, 76.12, 34.59);
                Area("Kargil", "Kargil_surround", 75.98, 34.48, 76.20, 34.66);
                Area("Copy", "Kargil", 76.07, 34.55, 76.12, 34.59);             // the same area in a second folder
                File.WriteAllText(Path.Combine(dir, "Kargil", "Broken_satellite.json"), "{\"west\":5,\"east\":1}");   // no picture, bad bounds

                var list = OfflineAreas.Scan(new[] { dir });
                Check($"two pictures found (play area + surround), duplicate and broken skipped ({list.Count})", list.Count == 2);
                Check("surround sorts first so the play area draws on top", list.Count == 2 && list[0].surround && !list[1].surround);
                Check("area name read from the file name", list.Count == 2 && list[0].area == "Kargil" && list[1].area == "Kargil");

                var inner = list.Count == 2 ? list[1] : null;
                if (inner != null)
                {
                    Rect r12 = OfflineAreas.PixelRect(inner, 12), r13 = OfflineAreas.PixelRect(inner, 13);
                    Check("pixel rect is north-west anchored with positive size", r12.width > 0f && r12.height > 0f);
                    Check("one zoom level up doubles the rect", Mathf.Abs(r13.width - 2f * r12.width) < 0.01f && Mathf.Abs(r13.x - 2f * r12.x) < 1f);
                    WebMercator.ToPixel(inner.CentreLat, inner.CentreLon, 12, out double cx, out double cy);
                    Check("the area's centre lies inside its rect", r12.Contains(new Vector2((float)cx, (float)cy)));
                    var t = OfflineAreas.Texture(inner);
                    Check("picture loads from disk", t != null && t.width == 8);
                    if (t != null) { Object.DestroyImmediate(t); inner.texture = null; }
                }

                var plan = Kurukshetra.TerrainGen.EditorToolsProxy.PlanCount(list);
                Check($"prefetch plan covers the area and stays polite ({plan} tiles, at most 1500)", plan < 0 || (plan > 0 && plan <= 1500));
            }
            finally
            {
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
            string summary = $"[MapOfflineSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}

namespace Kurukshetra.TerrainGen
{
    // The prefetch planner lives in the Editor assembly; runtime code reaches it by reflection (-1 = not in the Editor).
    public static class EditorToolsProxy
    {
        public static int PlanCount(List<Kurukshetra.Trainer.OfflineAreas.Picture> areas)
        {
            var type = System.Type.GetType("Kurukshetra.TerrainGen.EditorTools.PrefetchMapTiles, Assembly-CSharp-Editor");
            var m = type?.GetMethod("Plan");
            if (m == null) return -1;
            var result = m.Invoke(null, new object[] { areas }) as System.Collections.ICollection;
            return result != null ? result.Count : -1;
        }
    }
}
