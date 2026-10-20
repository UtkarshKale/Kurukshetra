using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kurukshetra.TerrainGen.EditorTools
{
    // Kurukshetra > Terrain > Fetch OSM layers for downloaded areas.
    // For every area in Assets/Kurukshetra/Terrain/RealData/ that has no <name>_layers.json: fetch the OpenStreetMap features
    // (see OsmFetcher: one area at a time, 2 s between queries, at most 2 tries per query 30 s apart) and write the files
    // beside the area. A progress bar with Cancel. Safe to run again: areas that have their file are skipped.
    public static class FetchOsmLayers
    {
        public static bool Running { get; private set; }
        public static string Status { get; private set; } = "idle";
        public static readonly List<string> Results = new List<string>();
        static bool cancel;
        static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
        static string current = "";

        [MenuItem("Kurukshetra/Terrain/Fetch OSM layers for downloaded areas")]
        static void Menu() => Start(false, null);

        // force = also refetch areas that already have a layers file. only = a single area folder name.
        public static void Start(bool force, string only)
        {
            if (Running) return;
            Results.Clear();
            cancel = false;
            Running = true;
            Status = "starting";
            stack.Clear();
            stack.Push(Run(force, only));
            EditorApplication.update += Tick;
        }

        public static void Cancel() { cancel = true; }

        static void Finish(string status)
        {
            Status = status;
            Running = false;
            EditorApplication.update -= Tick;
            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
            Debug.Log("[Layers] OSM fetch " + status + "\n" + string.Join("\n", Results));
        }

        // A small coroutine driver: understands null, AsyncOperation, CustomYieldInstruction and nested IEnumerators.
        static object waiting;
        static void Tick()
        {
            try
            {
                for (int guard = 0; guard < 64 && stack.Count > 0; guard++)
                {
                    if (waiting is AsyncOperation op) { if (!op.isDone) return; waiting = null; }
                    else if (waiting is CustomYieldInstruction cy) { if (cy.keepWaiting) return; waiting = null; }
                    var top = stack.Peek();
                    if (!top.MoveNext()) { stack.Pop(); continue; }
                    var y = top.Current;
                    if (y is IEnumerator nested) { stack.Push(nested); continue; }
                    waiting = y;
                    if (y == null) return;       // one frame
                }
                if (stack.Count == 0) Finish(cancel ? "cancelled" : "done");
            }
            catch (Exception e)
            {
                Results.Add("EXCEPTION " + e);
                Finish("failed");
            }
        }

        static IEnumerator Run(bool force, string only)
        {
            string root = ElevationDownloader.AreasFolder;
            var todo = new List<string>();
            if (Directory.Exists(root))
                foreach (var dir in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(dir);
                    if (only != null && !string.Equals(only, name, StringComparison.OrdinalIgnoreCase)) continue;
                    string raw = Path.Combine(dir, name + ".raw");
                    if (!File.Exists(raw)) continue;
                    if (!force && File.Exists(LayerFile.PathFor(raw))) { Results.Add($"{name}: skipped, already has a layers file"); continue; }
                    todo.Add(raw);
                }
            for (int i = 0; i < todo.Count && !cancel; i++)
            {
                string raw = todo[i];
                string name = Path.GetFileNameWithoutExtension(raw);
                current = name;
                OsmFetchResult result = null;
                var started = DateTime.Now;
                var fetch = OsmFetcher.Fetch(raw, (f, text) =>
                {
                    Status = $"{name}: {text}";
                    if (EditorUtility.DisplayCancelableProgressBar("Fetching OpenStreetMap features", $"{name}: {text}", (i + f) / todo.Count)) cancel = true;
                }, r => result = r);
                while (fetch.MoveNext() && !cancel) yield return fetch.Current;
                string took = $"{(DateTime.Now - started).TotalSeconds:F0} s";
                if (result == null) Results.Add($"{name}: cancelled");
                else if (!result.ok) Results.Add($"{name}: FAILED after {took}: {result.error}");
                else Results.Add($"{name}: {result.file.CountsLine()} ({took}){(result.file.note.Length > 0 ? " NOTE: " + result.file.note : "")}");
                if (i < todo.Count - 1 && !cancel) yield return new RealtimeWait(OsmFetcher.PauseBetweenQueries);
            }
        }
    }
}
