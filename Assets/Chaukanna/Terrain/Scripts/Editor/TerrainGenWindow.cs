using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Networking;

namespace Chaukanna.TerrainGen.EditorTools
{
    // Chaukanna > Terrain Generator. Every setting lives on a TerrainGenConfig asset; this window only edits it and runs actions.
    public class TerrainGenWindow : EditorWindow
    {
        const string GeneratedFolder = "Assets/Chaukanna/Terrain/Generated";
        const string LayerFolder = "Assets/Chaukanna/Terrain/Layers";
        const string ConfigFolder = "Assets/Chaukanna/Terrain/Configs";

        TerrainGenConfig config;
        SerializedObject serialized;
        ElevationGrid dem;              // Layer A in memory; reloaded from the file, never written back
        string demLoadedFrom = "";
        Texture2D preview;
        string status = "";
        MessageType statusType = MessageType.None;
        Vector2 scroll;
        ProceduralPreset preset = ProceduralPreset.RollingHills;
        bool showSource = true, showTerrain = true, showProcedural = true, showAppearance, showDownload;
        UnityWebRequest download;
        string downloadTarget = "";
        double downloadExtentKm = 6.0;
        string demType = "SRTMGL1";

        [MenuItem("Chaukanna/Terrain Generator")]
        static void Open()
        {
            GetWindow<TerrainGenWindow>("Terrain Generator").minSize = new Vector2(420f, 500f);
        }

        void OnDisable()
        {
            if (download != null) { download.Abort(); download.Dispose(); download = null; }
            if (preview != null) DestroyImmediate(preview);
        }

        void Say(string message, MessageType type)
        {
            status = message;
            statusType = type;
            Repaint();
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            var picked = (TerrainGenConfig)EditorGUILayout.ObjectField(new GUIContent("Config", "The settings asset. Everything below is saved in it."), config, typeof(TerrainGenConfig), false);
            if (picked != config) { config = picked; serialized = null; dem = null; demLoadedFrom = ""; }
            if (GUILayout.Button("New", GUILayout.Width(50f))) CreateConfig();
            EditorGUILayout.EndHorizontal();
            if (config == null)
            {
                EditorGUILayout.HelpBox("Pick a Terrain Gen Config, or press New to make one.", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }
            if (serialized == null || serialized.targetObject != config) serialized = new SerializedObject(config);
            serialized.Update();

            Prop("mode", "Mode");
            Prop("seed", "Seed");
            EditorGUILayout.HelpBox(ModeHelp(config.mode), MessageType.None);

            // --- Source and geography ---
            showSource = EditorGUILayout.BeginFoldoutHeaderGroup(showSource, "Source and geography (layer A)");
            if (showSource)
            {
                using (new EditorGUI.DisabledScope(!config.UsesDem))
                {
                    EditorGUILayout.BeginHorizontal();
                    Prop("demPath", "Elevation file");
                    if (GUILayout.Button("Browse", GUILayout.Width(64f))) EditorApplication.delayCall += Browse;
                    EditorGUILayout.EndHorizontal();
                    Prop("useFileCentre", "Centre on the file");
                    using (new EditorGUI.DisabledScope(config.useFileCentre))
                    {
                        Prop("centreLatitude", "Centre latitude");
                        Prop("centreLongitude", "Centre longitude");
                    }
                    if (GUILayout.Button("Load / reload elevation file")) LoadDem();
                    if (dem != null) EditorGUILayout.HelpBox(dem.Describe(), MessageType.None);
                    Prop("provenance", "Provenance");
                    Prop("licenceAndAttribution", "Licence and attribution");
                }
                if (!config.UsesDem) EditorGUILayout.LabelField("Not used in Pure Procedural.", EditorStyles.miniLabel);
                DownloadGui();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // --- Terrain ---
            showTerrain = EditorGUILayout.BeginFoldoutHeaderGroup(showTerrain, "Terrain settings");
            if (showTerrain)
            {
                Prop("sizeMetres", "Area size (m)");
                EditorGUILayout.BeginHorizontal();
                Prop("heightmapResolution", "Heightmap resolution");
                foreach (int r in new[] { 257, 513, 1025 })
                    if (GUILayout.Button(r.ToString(), GUILayout.Width(46f))) serialized.FindProperty("heightmapResolution").intValue = r;
                EditorGUILayout.EndHorizontal();
                Prop("tilesPerSide", "Tiles per side");
                EditorGUILayout.LabelField(" ", $"{config.tilesPerSide * config.tilesPerSide} tile(s) of {config.TileSize:F0} m, one sample every {config.SampleSpacing:F2} m", EditorStyles.miniLabel);
                Prop("groundPostAtZero", "Post ground at height 0");
                Prop("flattenPostPad", "Flatten post pad");
                using (new EditorGUI.DisabledScope(!config.flattenPostPad))
                {
                    Prop("postPadRadius", "Pad radius (m)");
                    Prop("postPadFalloff", "Pad falloff (m)");
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // --- Procedural ---
            showProcedural = EditorGUILayout.BeginFoldoutHeaderGroup(showProcedural, "Procedural settings (layer B)");
            EditorGUILayout.EndFoldoutHeaderGroup();
            if (showProcedural)
            {
                using (new EditorGUI.DisabledScope(config.mode == TerrainMode.RealWorldAccurate))
                {
                    EditorGUILayout.BeginHorizontal();
                    preset = (ProceduralPreset)EditorGUILayout.EnumPopup(new GUIContent("Preset", "Replaces the layers, warp and terrace below."), preset);
                    if (GUILayout.Button("Apply", GUILayout.Width(60f)))
                    {
                        Undo.RecordObject(config, "Apply terrain preset");
                        if (config.mode == TerrainMode.RealWorldSubtleDetail) config.ApplySubtleDetailDefaults();
                        else if (config.mode == TerrainMode.RealWorldMajorVariations || config.mode == TerrainMode.CustomHybrid) config.ApplyMajorVariationDefaults();
                        else config.ApplyPreset(preset);
                        EditorUtility.SetDirty(config);
                        serialized.Update();
                    }
                    EditorGUILayout.EndHorizontal();
                    if (config.mode != TerrainMode.PureProcedural) EditorGUILayout.LabelField(" ", "In real-world modes Apply loads the defaults for that mode.", EditorStyles.miniLabel);
                    if (config.mode == TerrainMode.PureProcedural) Prop("baseElevation", "Base elevation (m)");
                    Prop("displacementMultiplier", config.mode == TerrainMode.CustomHybrid ? "Procedural weight" : "Displacement multiplier");
                    if (config.mode == TerrainMode.RealWorldMajorVariations || config.mode == TerrainMode.CustomHybrid)
                        Prop("sourceReliefScale", config.mode == TerrainMode.CustomHybrid ? "Source weight" : "Source relief scale");
                    if (config.mode == TerrainMode.RealWorldMajorVariations)
                    {
                        Prop("modificationBlend", "Blend with untouched DEM");
                        Prop("modificationMask", "Modification mask");
                    }
                    Prop("layers", "Noise layers");
                    if (config.mode != TerrainMode.RealWorldSubtleDetail)
                    {
                        Prop("warp", "Domain warp");
                        Prop("terrace", "Terrace / mesa");
                    }
                }
                if (config.mode == TerrainMode.RealWorldAccurate) EditorGUILayout.LabelField("Ignored in Real World Accurate.", EditorStyles.miniLabel);
            }

            // --- Appearance ---
            showAppearance = EditorGUILayout.BeginFoldoutHeaderGroup(showAppearance, "Appearance (layer C)");
            EditorGUILayout.EndFoldoutHeaderGroup();
            if (showAppearance)
            {
                Prop("surfaceRules", "Surface rules");
                if (GUILayout.Button(new GUIContent("Create placeholder layers and default rules", "Flat-colour grass, dirt, rock, sand and snow layers in " + LayerFolder + ". Existing ones are reused, never overwritten."))) CreatePlaceholderLayers();
            }

            serialized.ApplyModifiedProperties();

            // --- Actions ---
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);
            var errors = config.Validate();
            foreach (string e in errors) EditorGUILayout.HelpBox(e, MessageType.Error);
            using (new EditorGUI.DisabledScope(errors.Count > 0))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Preview heightfield")) EditorApplication.delayCall += Preview;
                if (GUILayout.Button(new GUIContent("Generate terrain", "Builds a GeneratedTerrain object in the open scene. Only ever replaces a terrain this tool made from this config."), GUILayout.Height(26f))) EditorApplication.delayCall += Generate;
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(!config.UsesDem))
                    if (GUILayout.Button(new GUIContent("Restore unmodified DEM", "Switches to Real World Accurate and regenerates. Your procedural settings are kept for later."))) EditorApplication.delayCall += RestoreDem;
                if (GUILayout.Button(new GUIContent("Export heightmap (.raw)", "16-bit little-endian RAW of the whole area with a .json describing it."))) EditorApplication.delayCall += Export;
                EditorGUILayout.EndHorizontal();
            }
            if (preview != null)
            {
                var r = GUILayoutUtility.GetAspectRect(1f, GUILayout.MaxWidth(400f));
                EditorGUI.DrawPreviewTexture(r, preview);
                EditorGUILayout.LabelField("Preview: shaded relief, north up. Lighter = higher.", EditorStyles.miniLabel);
            }            // The generated terrain can be switched on and off without regenerating.
            var made = FindRoot();
            if (made != null)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(made.gameObject.activeSelf ? "Generated terrain is shown." : "Generated terrain is hidden (disabled).", EditorStyles.miniLabel);
                if (GUILayout.Button(made.gameObject.activeSelf ? "Hide terrain" : "Show terrain", GUILayout.Width(110f)))
                {
                    bool show = !made.gameObject.activeSelf;
                    EditorApplication.delayCall += () => { var r = FindRoot(); if (r != null) ShowTerrain(r.gameObject, show); };
                }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Run terrain self-test")) Say(TerrainGenSelfTest.Run() ? "Self-test passed. Details are in the Console." : "Self-test FAILED. Details are in the Console.", MessageType.Info);

            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);

            EditorGUILayout.EndScrollView();
        }

        void Prop(string name, string label)
        {
            var p = serialized.FindProperty(name);
            if (p != null) EditorGUILayout.PropertyField(p, new GUIContent(label, p.tooltip), true);
        }

        GeneratedTerrainRoot FindRoot()
        {
            if (config == null) return null;
            foreach (var root in FindObjectsByType<GeneratedTerrainRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (root.config == config) return root;
            return null;
        }

        // Enables or disables the generated terrain. When showing it, offers to hide a flat "Ground" object that would
        // otherwise slice through the hills; that object is only disabled, never changed or deleted, and only if you agree.
        void ShowTerrain(GameObject root, bool show)
        {
            if (root == null) return;
            Undo.RecordObject(root, show ? "Show generated terrain" : "Hide generated terrain");
            root.SetActive(show);
            var ground = FindGround(root);
            if (show && ground != null && ground.activeSelf
                && EditorUtility.DisplayDialog("Hide the flat ground?", $"'{ground.name}' is a flat ground object at the same place and will cut through the generated terrain.\n\nHide it while the terrain is shown? It is only disabled; nothing is deleted, and Hide terrain turns it back on.", "Hide it", "Leave it"))
            {
                Undo.RecordObject(ground, "Hide flat ground");
                ground.SetActive(false);
            }
            else if (!show && ground != null && !ground.activeSelf)
            {
                // Going back: the flat ground comes back on, so the scene is never left with no ground at all.
                Undo.RecordObject(ground, "Show flat ground");
                ground.SetActive(true);
            }
            EditorSceneManager.MarkSceneDirty(root.scene);
            if (show)
            {
                Selection.activeGameObject = root;
                if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();
            }
            Repaint();
        }

        // A root-level object called "Ground" that is not part of the generated terrain.
        static GameObject FindGround(GameObject generatedRoot)
        {
            foreach (var go in generatedRoot.scene.GetRootGameObjects())
                if (go != generatedRoot && go.name == "Ground") return go;
            return null;
        }

        void Browse()
        {
            if (config == null) return;
            string chosen = EditorUtility.OpenFilePanelWithFilters("Elevation file", Directory.Exists(CacheFolder) ? CacheFolder : "", new[] { "Elevation", "hgt,asc,raw,r16", "All files", "*" });
            if (string.IsNullOrEmpty(chosen)) return;
            Undo.RecordObject(config, "Pick elevation file");
            config.demPath = chosen;
            EditorUtility.SetDirty(config);
            LoadDem();
        }

        void RestoreDem()
        {
            if (config == null) return;
            Undo.RecordObject(config, "Restore DEM");
            config.mode = TerrainMode.RealWorldAccurate;
            EditorUtility.SetDirty(config);
            Generate();
        }

        static string ModeHelp(TerrainMode m)
        {
            switch (m)
            {
                case TerrainMode.RealWorldAccurate: return "The elevation file, untouched. No noise, warp, shaping, terracing or post pad.";
                case TerrainMode.RealWorldSubtleDetail: return "The elevation file plus low-amplitude detail in metres. Multiplier 0 gives the accurate terrain back exactly (with the post pad off).";
                case TerrainMode.RealWorldMajorVariations: return "The real landform reshaped: relief scale, noise, warp and terraces, blended with the untouched DEM and optionally masked.";
                case TerrainMode.CustomHybrid: return "Independent weights for the real landform and the procedural displacement.";
                default: return "No elevation file. Terrain = base elevation + noise layers, warped and terraced as set.";
            }
        }

        void CreateConfig()
        {
            Directory.CreateDirectory(ConfigFolder);
            string path = AssetDatabase.GenerateUniqueAssetPath(ConfigFolder + "/TerrainGenConfig.asset");
            var c = CreateInstance<TerrainGenConfig>();
            c.ApplyPreset(ProceduralPreset.RollingHills);
            AssetDatabase.CreateAsset(c, path);
            AssetDatabase.SaveAssets();
            config = c;
            serialized = null;
            Say("Created " + path, MessageType.Info);
        }

        bool LoadDem()
        {
            try
            {
                dem = DemReader.Load(config.demPath);
                demLoadedFrom = config.demPath;
                if (string.IsNullOrWhiteSpace(config.provenance))
                {
                    Undo.RecordObject(config, "Record DEM provenance");
                    config.provenance = dem.Describe();
                    EditorUtility.SetDirty(config);
                }
                Say("Loaded " + dem.sourceName, MessageType.Info);
                return true;
            }
            catch (DemException e)
            {
                dem = null;
                Say(e.Message, MessageType.Error);
                return false;
            }
        }

        bool EnsureDem()
        {
            if (!config.UsesDem) return true;
            if (dem != null && demLoadedFrom == config.demPath) return true;
            return LoadDem();
        }

        void Preview()
        {
            if (config == null || !EnsureDem()) return;
            try
            {
                // A quarter-resolution pass is plenty for a preview.
                int res = Mathf.Min(257, config.GlobalResolution);
                var f = HeightfieldGenerator.GenerateRegion(config, dem, -config.sizeMetres * 0.5, -config.sizeMetres * 0.5, config.sizeMetres, res, true);
                if (preview == null || preview.width != res) { if (preview != null) DestroyImmediate(preview); preview = new Texture2D(res, res, TextureFormat.RGB24, false); }
                var px = new Color32[res * res];
                float range = Mathf.Max(1f, f.max - f.min);
                for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    float h = (f.At(x, z) - f.min) / range;
                    // Simple hill shading from the north-west.
                    float sx = f.At(Mathf.Min(res - 1, x + 1), z) - f.At(Mathf.Max(0, x - 1), z), sz = f.At(x, Mathf.Min(res - 1, z + 1)) - f.At(x, Mathf.Max(0, z - 1));
                    float shade = Mathf.Clamp01(0.75f + (sz - sx) / (4f * f.Spacing));
                    byte v = (byte)(255f * Mathf.Clamp01((0.25f + 0.75f * h) * shade));
                    px[z * res + x] = new Color32(v, v, v, 255);
                }
                preview.SetPixels32(px);
                preview.Apply();
                Say($"Preview: {f.min:F0} m to {f.max:F0} m, ground at the centre {f.centreElevation:F0} m."
                    + (f.outsideSourceSamples > 0 ? $"\n{f.outsideSourceSamples} preview samples lie outside the elevation file." : ""), f.outsideSourceSamples > 0 ? MessageType.Warning : MessageType.Info);
            }
            catch (DemException e) { Say(e.Message, MessageType.Error); }
        }

        void Generate()
        {
            if (config == null || config.Validate().Count > 0 || !EnsureDem()) return;
            GeneratedTerrainRoot existing = null;
            foreach (var root in FindObjectsByType<GeneratedTerrainRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (root.config == config) { existing = root; break; }
            if (existing != null && !EditorUtility.DisplayDialog("Regenerate terrain", $"Replace the terrain previously generated from '{config.name}' in this scene?\nNo other object is touched.", "Replace", "Cancel")) return;

            try
            {
                string folder = GeneratedFolder + "/" + config.name;
                Directory.CreateDirectory(folder);
                EditorUtility.DisplayProgressBar("Terrain Generator", "Generating heightfield...", 0.3f);
                var result = TerrainGenEditor.Generate(config, dem, existing);
                if (existing == null) Undo.RegisterCreatedObjectUndo(result.root, "Generate terrain");
                ShowTerrain(result.root, true);     // you pressed Generate, so you want to see it
                Selection.activeGameObject = result.root;
                Say(result.report + "\nSaved tile data in " + folder + ". Save the scene (Ctrl+S) to keep the terrain objects.", result.warnings.Count > 0 ? MessageType.Warning : MessageType.Info);
            }
            catch (DemException e) { Say(e.Message, MessageType.Error); }
            finally { EditorUtility.ClearProgressBar(); }
        }

        void Export()
        {
            if (config == null || !EnsureDem()) return;
            string path = EditorUtility.SaveFilePanel("Export heightmap", "", config.name + "_heightmap", "raw");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                var f = HeightfieldGenerator.Generate(config, dem);
                // 0.1 m per unit above the lowest point keeps full physical scale in 16 bits up to 6553 m of relief.
                double scale = Math.Max(0.1, (f.max - f.min) / 65535.0);
                var bytes = new byte[f.metres.Length * 2];
                for (int row = 0; row < f.resolution; row++)          // written north-up
                for (int x = 0; x < f.resolution; x++)
                {
                    int v = Mathf.Clamp((int)Math.Round((f.At(x, f.resolution - 1 - row) - f.min) / scale), 0, 65535);
                    int i = (row * f.resolution + x) * 2;
                    bytes[i] = (byte)(v & 0xFF); bytes[i + 1] = (byte)(v >> 8);
                }
                File.WriteAllBytes(path, bytes);
                double lat = 0, lon = 0;
                if (config.UsesDem && dem != null) { lat = config.useFileCentre ? dem.CentreLatitude : config.centreLatitude; lon = config.useFileCentre ? dem.CentreLongitude : config.centreLongitude; }
                var meta = new DemReader.RawMetadata { width = f.resolution, height = f.resolution, littleEndian = true, signed = false, centreLatitude = lat, centreLongitude = lon, cellSizeMetres = f.Spacing, elevationScale = scale, elevationOffset = f.min, rowOrder = "north-up", crs = config.UsesDem ? "EPSG:4326 centre, local metric grid" : "none (procedural)", source = "Generated by Chaukanna Terrain Generator, mode " + config.mode + ", seed " + config.seed, licence = config.licenceAndAttribution };
                File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonUtility.ToJson(meta, true));
                Say($"Exported {f.resolution} x {f.resolution} samples to {path} with its .json.", MessageType.Info);
            }
            catch (Exception e) { Say("Export failed: " + e.Message, MessageType.Error); }
        }

        void CreatePlaceholderLayers()
        {
            Undo.RecordObject(config, "Default surface rules");
            TerrainGenEditor.CreatePlaceholderLayers(config);
            serialized.Update();
            Say("Placeholder layers are in " + LayerFolder + " and default rules are set. Heights are metres above the area's lowest point.", MessageType.Info);
        }
        // --- Online elevation: OpenTopography Global DEM API ---

        static string CacheFolder => Path.GetFullPath(Path.Combine(Application.persistentDataPath, "dem-cache"));

        // The key is never stored in the project: environment variable first, then a file in the user's home folder.
        static string ApiKey()
        {
            string key = System.Environment.GetEnvironmentVariable("OPENTOPOGRAPHY_API_KEY");
            if (!string.IsNullOrWhiteSpace(key)) return key.Trim();
            string file = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".chaukanna", "opentopography.key");
            return File.Exists(file) ? File.ReadAllText(file).Trim() : "";
        }

        void DownloadGui()
        {
            showDownload = EditorGUILayout.Foldout(showDownload, "Download from OpenTopography (needs your free API key)", true);
            if (!showDownload) return;
            EditorGUILayout.HelpBox("Uses the OpenTopography Global DEM API and asks for an Esri ASCII grid. An API key is required: set the environment variable OPENTOPOGRAPHY_API_KEY, or put the key in <your user folder>/.chaukanna/opentopography.key. "
                + "This path has not been tested end to end, because no key was available when it was written. Files are cached in:\n" + CacheFolder, MessageType.Info);
            demType = EditorGUILayout.TextField(new GUIContent("Dataset (demtype)", "For example SRTMGL1 (30 m), SRTMGL3 (90 m), COP30, COP90, NASADEM. Check the OpenTopography API page for the current list."), demType);
            downloadExtentKm = EditorGUILayout.DoubleField(new GUIContent("Extent (km)", "Width and height of the square to fetch, centred on the latitude and longitude above."), downloadExtentKm);
            bool busy = download != null;
            using (new EditorGUI.DisabledScope(busy || config.useFileCentre))
                if (GUILayout.Button("Download for the centre latitude / longitude")) StartDownload();
            if (config.useFileCentre) EditorGUILayout.LabelField(" ", "Untick 'Centre on the file' and enter coordinates first.", EditorStyles.miniLabel);
            if (busy)
            {
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(), download.downloadProgress, "Downloading...");
                if (GUILayout.Button("Cancel")) { download.Abort(); FinishDownload(true); }
            }
        }

        void StartDownload()
        {
            string key = ApiKey();
            if (key.Length == 0) { Say("No OpenTopography API key found. Set OPENTOPOGRAPHY_API_KEY or create <user folder>/.chaukanna/opentopography.key. Keys are free from opentopography.org.", MessageType.Error); return; }
            if (Math.Abs(config.centreLatitude) > 85.0 || Math.Abs(config.centreLongitude) > 180.0 || downloadExtentKm < 0.5 || downloadExtentKm > 100.0) { Say("Enter a latitude between -85 and 85, a longitude between -180 and 180, and an extent between 0.5 and 100 km.", MessageType.Error); return; }

            double half = downloadExtentKm * 500.0;
            GeoMath.LocalToGeo(config.centreLatitude, config.centreLongitude, -half, -half, out double south, out double west);
            GeoMath.LocalToGeo(config.centreLatitude, config.centreLongitude, half, half, out double north, out double east);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Directory.CreateDirectory(CacheFolder);
            downloadTarget = Path.Combine(CacheFolder, string.Format(inv, "{0}_{1:F4}_{2:F4}_{3:F1}km.asc", demType, config.centreLatitude, config.centreLongitude, downloadExtentKm));
            if (File.Exists(downloadTarget)) { UseDownloaded("Using the cached file (no download)."); return; }

            string url = string.Format(inv, "https://portal.opentopography.org/API/globaldem?demtype={0}&south={1:F6}&north={2:F6}&west={3:F6}&east={4:F6}&outputFormat=AAIGrid&API_Key={5}",
                UnityWebRequest.EscapeURL(demType), south, north, west, east, UnityWebRequest.EscapeURL(key));
            download = UnityWebRequest.Get(url);
            download.timeout = 120;
            download.SendWebRequest();
            EditorApplication.update += PollDownload;
            Say("Requesting elevation from OpenTopography...", MessageType.Info);
        }

        void PollDownload()
        {
            if (download == null) { EditorApplication.update -= PollDownload; return; }
            Repaint();
            if (!download.isDone) return;
            FinishDownload(false);
        }

        void FinishDownload(bool cancelled)
        {
            EditorApplication.update -= PollDownload;
            var request = download;
            download = null;
            if (request == null) return;
            try
            {
                if (cancelled) { Say("Download cancelled.", MessageType.Info); return; }
                string text = request.downloadHandler != null ? request.downloadHandler.text : "";
                if (request.result != UnityWebRequest.Result.Success)
                {
                    string reason = request.responseCode == 401 || request.responseCode == 403 ? "The API key was rejected."
                        : request.responseCode == 429 ? "Rate limit reached; try again later."
                        : request.responseCode == 400 ? "The request was refused (area too large for this dataset, or outside its coverage)."
                        : request.error;
                    Say($"Download failed (HTTP {request.responseCode}). {reason}\n{(text.Length > 300 ? text.Substring(0, 300) : text)}", MessageType.Error);
                    return;
                }
                if (!text.TrimStart().StartsWith("ncols", StringComparison.OrdinalIgnoreCase))
                {
                    Say("The server replied, but not with an elevation grid:\n" + (text.Length > 300 ? text.Substring(0, 300) : text), MessageType.Error);
                    return;
                }
                File.WriteAllText(downloadTarget, text);
                UseDownloaded("Downloaded and cached.");
            }
            finally { request.Dispose(); }
        }

        void UseDownloaded(string note)
        {
            Undo.RecordObject(config, "Use downloaded DEM");
            config.demPath = downloadTarget;
            config.provenance = $"OpenTopography Global DEM API, dataset {demType}, {downloadExtentKm:F1} km square centred {config.centreLatitude:F5}, {config.centreLongitude:F5}. Fetched {DateTime.Now:yyyy-MM-dd}.";
            config.licenceAndAttribution = "Cite the dataset as OpenTopography requires for " + demType + " (see the dataset page and the terrain README).";
            EditorUtility.SetDirty(config);
            serialized = null;
            if (LoadDem()) Say(note + "\n" + dem.Describe(), MessageType.Info);
        }
    }

    // The same actions the window performs, callable from scripts and the CLI.
    public static class TerrainGenEditor
    {
        const string GeneratedFolder = "Assets/Chaukanna/Terrain/Generated";
        const string LayerFolder = "Assets/Chaukanna/Terrain/Layers";

        // Builds the terrain in the open scene and saves each tile's TerrainData as an asset, so it survives saving and reloading.
        // Only replaces `existing`, which must be a root this tool made.
        public static TerrainBuildResult Generate(TerrainGenConfig config, ElevationGrid dem, GeneratedTerrainRoot existing)
        {
            string folder = GeneratedFolder + "/" + config.name;
            Directory.CreateDirectory(folder);
            var result = TerrainBuilder.Build(config, dem, existing, (name, data) =>
            {
                string path = folder + "/" + name + ".asset";
                AssetDatabase.DeleteAsset(path);        // only files this tool wrote, inside its own folder
                AssetDatabase.CreateAsset(data, path);
                return data;
            });
            AssetDatabase.SaveAssets();
            result.root.name = "GeneratedTerrain (" + config.name + ")";
            EditorSceneManager.MarkSceneDirty(result.root.scene);
            return result;
        }

        // Flat-colour grass, dirt, rock, sand and snow layers plus default height and slope rules. Existing layer assets are reused.
        public static void CreatePlaceholderLayers(TerrainGenConfig config)
        {
            Directory.CreateDirectory(LayerFolder);
            var colours = new (string name, Color colour)[]
            {
                ("Grass", new Color(0.30f, 0.42f, 0.20f)), ("Dirt", new Color(0.42f, 0.34f, 0.24f)), ("Rock", new Color(0.42f, 0.41f, 0.40f)),
                ("Sand", new Color(0.72f, 0.65f, 0.48f)), ("Snow", new Color(0.92f, 0.94f, 0.96f)),
            };
            var layers = new Dictionary<string, TerrainLayer>();
            foreach (var (name, colour) in colours)
            {
                string layerPath = LayerFolder + "/Placeholder_" + name + ".terrainlayer";
                var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath);
                if (layer == null)
                {
                    string texPath = LayerFolder + "/Placeholder_" + name + ".png";
                    if (!File.Exists(texPath))
                    {
                        var tex = new Texture2D(8, 8, TextureFormat.RGB24, false);
                        var px = new Color[64];
                        for (int i = 0; i < 64; i++) px[i] = colour;
                        tex.SetPixels(px);
                        File.WriteAllBytes(texPath, tex.EncodeToPNG());
                        UnityEngine.Object.DestroyImmediate(tex);
                        AssetDatabase.ImportAsset(texPath);
                    }
                    layer = new TerrainLayer { diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath), tileSize = new Vector2(20f, 20f), smoothness = 0f, metallic = 0f };
                    AssetDatabase.CreateAsset(layer, layerPath);
                }
                layers[name] = layer;
            }
            AssetDatabase.SaveAssets();

            config.surfaceRules = new List<SurfaceRule>
            {
                new SurfaceRule { label = "Sand (low, flat)", layer = layers["Sand"], maxHeight = 15f, maxSlope = 12f },
                new SurfaceRule { label = "Grass (gentle)", layer = layers["Grass"], minHeight = 10f, maxHeight = 900f, maxSlope = 24f },
                new SurfaceRule { label = "Dirt (moderate slope)", layer = layers["Dirt"], minSlope = 20f, maxSlope = 38f, maxHeight = 1200f },
                new SurfaceRule { label = "Rock (steep)", layer = layers["Rock"], minSlope = 34f, weight = 1.5f },
                new SurfaceRule { label = "Snow (high)", layer = layers["Snow"], minHeight = 1100f, maxSlope = 50f, heightBlend = 80f },
            };
            EditorUtility.SetDirty(config);
        }

    }
}