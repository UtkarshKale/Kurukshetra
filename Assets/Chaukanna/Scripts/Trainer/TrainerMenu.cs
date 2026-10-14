using System;
using System.Collections.Generic;
using Chaukanna.Scenario;
using Chaukanna.Session;
using Chaukanna.TerrainGen;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // The trainer's main menu (scene TrainerMenu). Tabs: Terrain, Scenario, Map, Lobby. START carries everything into the
    // field scene through SessionSetup; nothing in the field runs before that.
    public class TrainerMenu : MonoBehaviour
    {
        [Tooltip("Starting terrain settings and the surface layers to paint with. A copy is edited here; the asset is not changed.")]
        public TerrainGenConfig terrainTemplate;
        public List<TrainerPreset> presets = new List<TrainerPreset>();
        public List<DifficultyProfile> difficulties = new List<DifficultyProfile>();
        public int difficultyIndex = 1;
        public RandomizeBounds randomizeBounds = new RandomizeBounds();
        [Tooltip("Scene loaded by START.")]
        public string fieldScenePath = "Assets/Chaukanna/Scenes/MixedSky.unity";
        [Tooltip("This scene, so the field can come back to it.")]
        public string menuScenePath = "Assets/Chaukanna/Scenes/TrainerMenu.unity";
        [Tooltip("Text size at 720p; scales with window height.")]
        public int fontSize = 15;

        // Working state. Public so tests and the CLI can drive the menu without clicking.
        public TerrainGenConfig Terrain { get; private set; }
        public ScenarioConfig Scenario { get; private set; }
        public ElevationGrid Dem { get; private set; }
        public Imagery Picture { get; private set; }
        public Heightfield PreviewField { get; private set; }
        public readonly List<PlayerSlot> Players = new List<PlayerSlot>();
        public readonly List<Vector2> SpawnPoints = new List<Vector2>();
        public string Message { get; private set; } = "";

        enum Tab { Terrain, Scenario, Map, Lobby, Credits }
        enum MapTool { PlacePost, AddSpawn, RemoveSpawn }

        Tab tab;
        MapTool tool;
        int selectedPlayer;
        ProceduralPreset terrainPreset = ProceduralPreset.RollingHills;
        Texture2D preview;
        bool previewStale = true;
        string demPath = "", demLoadedFrom = "", imageryPath = "";
        GUIStyle label, button, title, small, field;
        Vector2 scroll;

        // Real place: picked on the OpenStreetMap map, elevation downloaded from the AWS Terrain Tiles.
        public MapPicker Map { get; private set; }
        bool showMap;
        string searchText = "", areaName = "Area";
        public bool Downloading { get; private set; }
        public bool WithSatellite = true;      // also download a satellite picture to colour the terrain
        float downloadProgress;
        string downloadStep = "";
        List<string> areas = new List<string>();
        float areasCheckedAt = -100f;

        static readonly string[] TabNames = { "1  TERRAIN", "2  SCENARIO", "3  MAP", "4  LOBBY", "CREDITS" };
        static readonly string[] ModeNames = { "Real world\naccurate", "Real world\n+ subtle detail", "Real world\n+ major variations", "Pure\nprocedural", "Custom\nhybrid" };

        void Awake()
        {
            Terrain = terrainTemplate != null ? Instantiate(terrainTemplate) : ScriptableObject.CreateInstance<TerrainGenConfig>();
            Terrain.name = "Session terrain";
            if (terrainTemplate == null) Terrain.ApplyPreset(ProceduralPreset.RollingHills);
            demPath = Terrain.demPath ?? "";
            Scenario = presets.Count > 0 && presets[0] != null ? presets[0].config.Clone().Validate() : new ScenarioConfig();
            Players.Add(new PlayerSlot { name = "Player 1", isLocal = true });
            bool haveCentre = Terrain.centreLatitude != 0.0 || Terrain.centreLongitude != 0.0;
            Map = new MapPicker(this, haveCentre ? Terrain.centreLatitude : 32.6875, haveCentre ? Terrain.centreLongitude : 74.8375, 12);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SessionSetup.Clear();       // back in the menu: the previous session is over
        }

        void OnDestroy()
        {
            if (preview != null) Destroy(preview);
            if (Terrain != null) Destroy(Terrain);
            Map?.Destroy();
        }

        // --- Actions ---

        public bool LoadDem()
        {
            try
            {
                Terrain.demPath = demPath.Trim().Trim('"');
                Dem = DemReader.Load(Terrain.demPath);
                demLoadedFrom = Terrain.demPath;
                previewStale = true;
                Message = "Loaded elevation file.\n" + Dem.Describe();
                return true;
            }
            catch (DemException e)
            {
                Dem = null;
                Message = e.Message;
                return false;
            }
        }

        // Loads the optional ground picture (PNG / JPG, with a .json for its bounds).
        public bool LoadImagery(string path)
        {
            try
            {
                imageryPath = (path ?? "").Trim().Trim('"');
                if (Picture != null && Picture.texture != null) Destroy(Picture.texture);
                Picture = imageryPath.Length == 0 ? null : ImageryReader.Load(imageryPath);
                Terrain.imageryPath = imageryPath;
                Message = Picture == null ? "Imagery cleared." : "Loaded imagery. " + Picture.Describe();
                return true;
            }
            catch (DemException e)
            {
                Picture = null;
                Message = e.Message;
                return false;
            }
        }

        // Builds the heightfield at preview resolution and its picture. Returns false (with Message set) when settings are invalid.
        public bool GeneratePreview()
        {
            var errors = Terrain.Validate();
            if (Terrain.UsesDem && errors.Count == 0 && (Dem == null || demLoadedFrom != Terrain.demPath) && !LoadDem()) return false;
            if (errors.Count > 0) { Message = string.Join("\n", errors); return false; }
            try
            {
                Terrain.padCentres = new List<Vector2>();
                foreach (var p in Players) Terrain.padCentres.Add(p.post);
                int res = Mathf.Min(257, Terrain.GlobalResolution);
                PreviewField = HeightfieldGenerator.GenerateRegion(Terrain, Dem, -Terrain.sizeMetres * 0.5, -Terrain.sizeMetres * 0.5, Terrain.sizeMetres, res, true);
                preview = HeightfieldGenerator.PreviewTexture(PreviewField, preview);
                previewStale = false;
                Message = $"Terrain preview: {PreviewField.min:F0} m to {PreviewField.max:F0} m, {Terrain.sizeMetres / 1000f:F1} km across, a sample every {Terrain.SampleSpacing:F1} m in the field."
                    + (PreviewField.outsideSourceSamples > 0 ? "\nWARNING: part of this area lies outside the elevation file." : "");
                return true;
            }
            catch (DemException e)
            {
                Message = e.Message;
                return false;
            }
        }

        // Downloads the elevation for a square of side 2 x radius around (lat, lon), its satellite picture, and a coarse
        // surround four times as wide for the horizon, then switches the terrain to it.
        public void DownloadArea(string name, double latitude, double longitude, float radiusMetres)
        {
            if (Downloading) return;
            var req = new AreaRequest { name = name, latitude = latitude, longitude = longitude, radiusMetres = Mathf.Max(100f, radiusMetres) };
            StartCoroutine(DownloadAll(req));
        }

        System.Collections.IEnumerator DownloadAll(AreaRequest req)
        {
            Downloading = true;
            downloadProgress = 0f;
            System.Action<float, string> step(string part, float from, float span) => (f, what) => { downloadProgress = from + span * f; downloadStep = part + ": " + what; };

            AreaDownloadResult area = null;
            yield return ElevationDownloader.Download(req, step("Elevation", 0f, 0.4f), r => area = r);
            if (area == null || !area.ok) { Downloading = false; areasCheckedAt = -100f; Message = area?.error ?? "Download failed."; downloadStep = "Failed"; yield break; }
            string notes = area.summary;

            if (WithSatellite)
            {
                ImageryDownloadResult picture = null;
                yield return ImageryDownloader.Download(req, ImageryDownloader.PathFor(area.rawPath), step("Satellite", 0.4f, 0.3f), r => picture = r);
                notes += "\n" + (picture != null && picture.ok ? picture.summary : picture?.error);
            }

            // The surround: the same place, four times as wide, coarser. Only used for the view to the horizon.
            var around = new AreaRequest { name = System.IO.Path.GetFileNameWithoutExtension(area.rawPath) + ElevationDownloader.SurroundSuffix, folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(area.rawPath)),
                latitude = req.latitude, longitude = req.longitude, radiusMetres = req.radiusMetres * ElevationDownloader.SurroundFactor };
            AreaDownloadResult surround = null;
            yield return ElevationDownloader.Download(around, step("Surround", 0.7f, 0.15f), r => surround = r);
            if (surround != null && surround.ok && WithSatellite)
                yield return ImageryDownloader.Download(around, ImageryDownloader.PathFor(surround.rawPath), step("Surround picture", 0.85f, 0.15f), r => { });
            notes += surround != null && surround.ok ? $"\nSurround for the horizon: {around.SideMetres / 1000f:F0} km square." : "\nNo surround (" + surround?.error + ")";

            Downloading = false;
            areasCheckedAt = -100f;
            downloadStep = "Done";
            if (UseArea(area.projectRelativePath.Length > 0 ? area.projectRelativePath : area.rawPath))
            {
                Terrain.provenance = notes;
                Message = notes;
            }
        }

        // The coarse surround and its picture, when the area has one.
        public ElevationGrid SurroundDem { get; private set; }
        public Imagery SurroundPicture { get; private set; }
        // Uses an elevation file downloaded before (works with no network). The terrain area becomes the file's area.
        public bool UseArea(string rawPath)
        {
            demPath = rawPath;
            if (Terrain.mode == TerrainMode.PureProcedural) SetTerrainMode(TerrainMode.RealWorldAccurate);
            Terrain.useFileCentre = true;
            Terrain.licenceAndAttribution = ElevationDownloader.Attribution;
            if (!LoadDem()) return false;
            Terrain.sizeMetres = Mathf.Floor((float)Math.Min(Dem.WidthMetres, Dem.HeightMetres));
            Terrain.centreLatitude = Dem.CentreLatitude;
            Terrain.centreLongitude = Dem.CentreLongitude;
            Map.SetPick(Dem.CentreLatitude, Dem.CentreLongitude);
            // Its surround for the horizon, if there is one.
            SurroundDem = null;
            if (SurroundPicture != null && SurroundPicture.texture != null) Destroy(SurroundPicture.texture);
            SurroundPicture = null;
            string surround = ElevationDownloader.SurroundPath(System.IO.Path.IsPathRooted(rawPath) ? rawPath : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", rawPath)));
            if (System.IO.File.Exists(surround))
            {
                try
                {
                    SurroundDem = DemReader.Load(surround);
                    string sp = ImageryDownloader.PathFor(surround);
                    if (System.IO.File.Exists(sp)) SurroundPicture = ImageryReader.Load(sp);
                }
                catch (DemException e) { Message = "Surround not used: " + e.Message; SurroundDem = null; }
            }

            // Its satellite picture, if one was downloaded with it.
            string picture = ImageryDownloader.PathFor(rawPath);
            if (System.IO.File.Exists(System.IO.Path.IsPathRooted(picture) ? picture : System.IO.Path.Combine(Application.dataPath, "..", picture)))
            {
                LoadImagery(System.IO.Path.IsPathRooted(picture) ? picture : System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", picture)));
                if (Terrain.imageryBlend > 0.9f) Terrain.imageryBlend = 0.6f;    // leave room for the ground materials close up
            }
            previewStale = true;
            return true;
        }

        public void SetTerrainMode(TerrainMode mode)
        {
            if (Terrain.mode == mode) return;
            Terrain.mode = mode;
            if (mode == TerrainMode.RealWorldSubtleDetail) Terrain.ApplySubtleDetailDefaults();
            else if (mode == TerrainMode.RealWorldMajorVariations || mode == TerrainMode.CustomHybrid) Terrain.ApplyMajorVariationDefaults();
            else if (mode == TerrainMode.PureProcedural) Terrain.ApplyPreset(terrainPreset);
            previewStale = true;
        }

        public void SetTerrainPreset(ProceduralPreset p)
        {
            terrainPreset = p;
            if (Terrain.mode == TerrainMode.PureProcedural) Terrain.ApplyPreset(p);
            previewStale = true;
        }

        public void PlacePost(int player, Vector2 metres)
        {
            if (player < 0 || player >= Players.Count) return;
            Players[player].post = ClampToArea(metres, 100f);
            previewStale = true;      // the flat pad moves with the post
        }

        public void AddSpawnPoint(Vector2 metres)
        {
            SpawnPoints.Add(ClampToArea(metres, 20f));
        }

        public void RemoveNearestSpawnPoint(Vector2 metres)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < SpawnPoints.Count; i++)
            {
                float d = Vector2.Distance(SpawnPoints[i], metres);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            if (best >= 0) SpawnPoints.RemoveAt(best);
        }

        Vector2 ClampToArea(Vector2 p, float margin)
        {
            float half = Terrain.sizeMetres * 0.5f - margin;
            return new Vector2(Mathf.Clamp(p.x, -half, half), Mathf.Clamp(p.y, -half, half));
        }

        // Problems that stop START. Empty = ready.
        public List<string> StartProblems()
        {
            var problems = Terrain.Validate();
            if (Terrain.UsesDem && problems.Count == 0 && (Dem == null || demLoadedFrom != Terrain.demPath)) problems.Add("Load the elevation file on the Terrain tab first.");
            if (Players.Count == 0) problems.Add("Add at least one player on the Map tab.");
            return problems;
        }

        // Hands the session to the field scene and loads it.
        public bool StartSession()
        {
            var problems = StartProblems();
            if (problems.Count > 0) { Message = string.Join("\n", problems); return false; }

            SessionSetup.Clear();
            SessionSetup.terrain = Instantiate(Terrain);
            SessionSetup.terrain.name = "Session terrain";
            SessionSetup.dem = Dem;
            SessionSetup.imagery = Picture;
            SessionSetup.surroundDem = Terrain.UsesDem ? SurroundDem : null;
            SessionSetup.surroundImagery = Terrain.UsesDem ? SurroundPicture : null;
            SessionSetup.scenario = Scenario.Clone().Validate();
            SessionSetup.difficultyIndex = difficultyIndex;
            foreach (var p in Players) SessionSetup.players.Add(new PlayerSlot { name = p.name, post = p.post, ready = p.ready, isLocal = p.isLocal });
            SessionSetup.spawnPoints.AddRange(SpawnPoints);
            SessionSetup.menuScenePath = menuScenePath;
            SessionSetup.Ready = true;
            SessionSetup.LoadWithScreen(fieldScenePath);
            return true;
        }

        // --- UI ---

        void OnGUI()
        {
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
                small = new GUIStyle(label);
                title = new GUIStyle(label) { fontStyle = FontStyle.Bold, wordWrap = false };
                button = new GUIStyle(GUI.skin.button) { richText = true };
                field = new GUIStyle(GUI.skin.textField);
            }
            label.fontSize = button.fontSize = field.fontSize = fontSize;
            small.fontSize = fontSize - 2;
            title.fontSize = fontSize + 9;
            label.normal.textColor = title.normal.textColor = Color.white;
            small.normal.textColor = new Color(0.72f, 0.76f, 0.8f);

            float ui = Screen.height / 720f;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(ui, ui, 1f));
            float w = Screen.width / ui, h = 720f;
            Fill(new Rect(0f, 0f, w, h), new Color(0.05f, 0.06f, 0.08f));

            // Top bar: title, tabs, START.
            const float pad = 14f, barH = 54f;
            Fill(new Rect(0f, 0f, w, barH), new Color(0.09f, 0.11f, 0.14f));
            GUI.Label(new Rect(pad, 8f, 300f, 38f), "CHAUKANNA  TRAINER", title);
            tab = (Tab)GUI.Toolbar(new Rect(320f, 10f, Mathf.Min(680f, w - 320f - 200f), 34f), (int)tab, TabNames, button);
            var problems = StartProblems();
            var old = GUI.backgroundColor;
            GUI.backgroundColor = problems.Count == 0 ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
            if (GUI.Button(new Rect(w - 180f, 8f, 166f, 38f), "<b>START</b>", new GUIStyle(button) { fontSize = fontSize + 6 })) StartSession();
            GUI.backgroundColor = old;

            // Message strip along the bottom.
            const float msgH = 62f;
            Fill(new Rect(0f, h - msgH, w, msgH), new Color(0.09f, 0.11f, 0.14f));
            string msg = problems.Count > 0 ? "<color=#FFB040>Before START: " + string.Join("  ", problems) + "</color>\n" + Message : Message;
            GUI.Label(new Rect(pad, h - msgH + 4f, w - pad * 2f, msgH - 8f), msg, small);

            var body = new Rect(pad, barH + pad, w - pad * 2f, h - barH - msgH - pad * 2f);
            switch (tab)
            {
                case Tab.Terrain: TerrainTab(body); break;
                case Tab.Scenario: ScenarioTab(body); break;
                case Tab.Map: MapTab(body); break;
                case Tab.Credits: GUI.Label(new Rect(body.x, body.y, Mathf.Min(980f, body.width), body.height), Credits.Text, label); break;
                default: LobbyTab(body); break;
            }
            GUI.matrix = saved;
        }

        void TerrainTab(Rect body)
        {
            float leftW = Mathf.Min(520f, body.width * 0.5f);
            GUILayout.BeginArea(new Rect(body.x, body.y, leftW, body.height));
            scroll = GUILayout.BeginScrollView(scroll);
            const float row = 26f;

            RealPlace(row);
            GUILayout.Space(6f);
            GUILayout.Label("<b>Mode</b>", label);
            int mode = GUILayout.SelectionGrid((int)Terrain.mode, ModeNames, 3, button, GUILayout.Height(row * 3.2f));
            if (mode != (int)Terrain.mode) SetTerrainMode((TerrainMode)mode);

            if (Terrain.mode == TerrainMode.PureProcedural)
            {
                GUILayout.Space(6f);
                GUILayout.Label("<b>Landscape preset</b>", label);
                string[] names = Enum.GetNames(typeof(ProceduralPreset));
                int p = GUILayout.SelectionGrid((int)terrainPreset, names, 3, button, GUILayout.Height(row * 3f));
                if (p != (int)terrainPreset) SetTerrainPreset((ProceduralPreset)p);
            }
            else
            {
                GUILayout.Space(6f);
                GUILayout.Label("<b>Elevation file</b>  (.hgt, .asc, or .raw with .json; full path)", label);
                GUILayout.BeginHorizontal();
                demPath = GUILayout.TextField(demPath, field, GUILayout.Height(row));
                if (GUILayout.Button("Load", button, GUILayout.Width(70f), GUILayout.Height(row))) LoadDem();
                GUILayout.EndHorizontal();
                GUILayout.Label(Dem != null && demLoadedFrom == Terrain.demPath ? $"Loaded: {Dem.sourceName}, {Dem.width} x {Dem.height}, {Dem.minElevation:F0} to {Dem.maxElevation:F0} m" : "No elevation file loaded.", small);
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            int terrainSeed = Stepper("Terrain seed", Terrain.seed, 1, row, int.MinValue, int.MaxValue);
            if (terrainSeed != Terrain.seed) { Terrain.seed = terrainSeed; previewStale = true; }
            if (GUILayout.Button("Random seed", button, GUILayout.Height(row))) { Terrain.seed = System.Environment.TickCount & 0xFFFFF; previewStale = true; }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            int area = Stepper("Area m", Mathf.RoundToInt(Terrain.sizeMetres), 500, row, 100);
            if (area != Mathf.RoundToInt(Terrain.sizeMetres)) { Terrain.sizeMetres = area; previewStale = true; }
            GUILayout.Label("  Detail", label, GUILayout.Width(60f));
            foreach (int r in new[] { 257, 513, 1025 })
                if (GUILayout.Toggle(Terrain.heightmapResolution == r, r.ToString(), button, GUILayout.Height(row)) && Terrain.heightmapResolution != r) Terrain.heightmapResolution = r;
            GUILayout.EndHorizontal();

            if (Terrain.mode != TerrainMode.RealWorldAccurate)
            {
                Terrain.displacementMultiplier = Slider(Terrain.mode == TerrainMode.CustomHybrid ? "Procedural weight" : "Procedural amount", Terrain.displacementMultiplier, 0f, 3f);
                if (Terrain.mode == TerrainMode.RealWorldMajorVariations || Terrain.mode == TerrainMode.CustomHybrid)
                    Terrain.sourceReliefScale = Slider(Terrain.mode == TerrainMode.CustomHybrid ? "Real terrain weight" : "Real relief scale", Terrain.sourceReliefScale, 0f, 3f);
                if (Terrain.mode == TerrainMode.RealWorldMajorVariations)
                    Terrain.modificationBlend = Slider("Blend with real terrain", Terrain.modificationBlend, 0f, 1f);
                bool pads = GUILayout.Toggle(Terrain.flattenPostPad, "  Flatten a pad under each player post", GUILayout.Height(row));
                if (pads != Terrain.flattenPostPad) { Terrain.flattenPostPad = pads; previewStale = true; }
            }
            else GUILayout.Label("Accurate mode uses the elevation file untouched: no noise, no pads under the posts.", small);

            // Ground materials: which biome's rules place rock, scree, soil, grass, snow and sand.
            if (Terrain.surfaceKit != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("<b>Ground materials</b>  (rock, scree, soil, grass, snow, sand)", label);
                string[] biomes = { "Auto", "High desert\n(Ladakh)", "Plains\n(Jammu)", "Desert\n(Rajasthan)", "Temperate\nhills" };
                int b = GUILayout.SelectionGrid((int)Terrain.biome, biomes, 5, button, GUILayout.Height(row * 2f));
                if (b != (int)Terrain.biome) { Terrain.biome = (SurfaceBiome)b; previewStale = true; }
            }

            GUILayout.Space(6f);
            GUILayout.Label("<b>Ground picture</b>  (optional PNG / JPG, full path; a .json beside it places it by coordinates)", label);
            GUILayout.BeginHorizontal();
            imageryPath = GUILayout.TextField(imageryPath, field, GUILayout.Height(row));
            if (GUILayout.Button("Load", button, GUILayout.Width(70f), GUILayout.Height(row))) LoadImagery(imageryPath);
            GUILayout.EndHorizontal();
            if (Picture != null) Terrain.imageryBlend = Slider("Picture strength", Terrain.imageryBlend, 0f, 1f);
            GUILayout.Label(Picture != null ? Picture.Describe() : "No picture: the terrain is coloured by height and slope.", small);

            GUILayout.Space(8f);
            if (GUILayout.Button(previewStale ? "<b>Generate preview</b>" : "Regenerate preview", button, GUILayout.Height(row * 1.5f))) GeneratePreview();
            GUILayout.Label("The same settings and seed are used to build the real terrain when you press START.", small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Right side: the OpenStreetMap picker or the height preview.
            float size = Mathf.Min(body.height - 36f, body.width - leftW - 20f);
            float rx = body.x + leftW + 20f;
            if (GUI.Toggle(new Rect(rx, body.y, 150f, 30f), showMap, "Map: pick a place", button)) showMap = true;
            if (GUI.Toggle(new Rect(rx + 156f, body.y, 150f, 30f), !showMap, "Height preview", button)) showMap = false;
            var map = new Rect(rx, body.y + 36f, size, size);
            if (showMap)
            {
                Map.Draw(map, small);
                return;
            }
            Fill(map, new Color(0.1f, 0.12f, 0.15f));
            if (preview != null) GUI.DrawTexture(map, preview);
            GUI.Label(new Rect(map.x + 8f, map.y + 4f, map.width - 16f, 40f), preview == null ? "No preview yet. Press Generate preview." : previewStale ? "<color=#FFB040>Settings changed since this preview.</color>" : "North is up.", small);
        }

        // Search, typed position and radius, download, and the areas already downloaded.
        void RealPlace(float row)
        {
            GUILayout.Label("<b>Real place</b>  (search or click on the map on the right, then download it)", label);
            if (!showMap && GUILayout.Button("Open the map to search and pick a place", button, GUILayout.Height(row))) showMap = true;

            GUILayout.BeginHorizontal();
            GUILayout.Label("Lat", label, GUILayout.Width(28f), GUILayout.Height(row));
            double lat = NumberEntry.FieldDouble("menu.lat", Map.PickLatitude, 5, field, GUILayout.Width(92f), GUILayout.Height(row));
            GUILayout.Label(" Lon", label, GUILayout.Width(34f), GUILayout.Height(row));
            double lon = NumberEntry.FieldDouble("menu.lon", Map.PickLongitude, 5, field, GUILayout.Width(92f), GUILayout.Height(row));
            if (lat != Map.PickLatitude || lon != Map.PickLongitude) { Map.SetPick(lat, lon); Map.CentreOn(lat, lon); }
            Map.RadiusMetres = Stepper("Radius m", Mathf.RoundToInt(Map.RadiusMetres), 250, row, 100);
            GUILayout.EndHorizontal();

            // What the download will fetch, and whether the terrain can hold that detail.
            var req = new AreaRequest { name = areaName, latitude = Map.PickLatitude, longitude = Map.PickLongitude, radiusMetres = Map.RadiusMetres };
            int zoom = req.Zoom(out int tileCount);
            double mpp = WebMercator.MetresPerPixel(req.latitude, zoom);
            string note = $"Area {req.SideMetres / 1000f:F1} km square. Source tiles: zoom {zoom}, {tileCount} tiles, about {mpp:F0} m per pixel (the data itself is about 30 m in most places).";
            if (req.SideMetres / (Terrain.GlobalResolution - 1) > 30f) note += $"\n<color=#FFB040>The terrain samples only every {req.SideMetres / (Terrain.GlobalResolution - 1):F0} m at this size: pick Detail 1025 or more tiles to keep the detail.</color>";
            GUILayout.Label(note, small);

            WithSatellite = GUILayout.Toggle(WithSatellite, "  Also download a satellite picture (Sentinel-2, 10 m) to colour the ground", GUILayout.Height(row));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", label, GUILayout.Width(44f), GUILayout.Height(row));
            areaName = GUILayout.TextField(areaName, 40, field, GUILayout.Width(150f), GUILayout.Height(row));
            using (new GUIEnabledScope(!Downloading))
                if (GUILayout.Button(Downloading ? $"Downloading {downloadProgress * 100f:F0} %" : "<b>Download terrain</b>", button, GUILayout.Height(row)))
                    DownloadArea(areaName, Map.PickLatitude, Map.PickLongitude, Map.RadiusMetres);
            GUILayout.EndHorizontal();
            if (Downloading || downloadStep.Length > 0) GUILayout.Label(downloadStep + (Map.Status.Length > 0 ? "   " + Map.Status : ""), small);
            else if (Map.Status.Length > 0) GUILayout.Label(Map.Status, small);

            // Areas already downloaded: these work with no network at all.
            if (Time.realtimeSinceStartup - areasCheckedAt > 5f) { areas = ElevationDownloader.DownloadedAreas(); areasCheckedAt = Time.realtimeSinceStartup; }
            if (areas.Count > 0)
            {
                GUILayout.Label("Downloaded before (no internet needed):", small);
                foreach (var a in areas)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(System.IO.Path.GetFileNameWithoutExtension(a), label, GUILayout.Height(row - 4f));
                    if (GUILayout.Button("Use", button, GUILayout.Width(60f), GUILayout.Height(row - 4f))) UseArea(ElevationDownloader.ProjectRelative(a));
                    GUILayout.EndHorizontal();
                }
            }
        }

        float Slider(string name, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{name}  <b>{value:F2}</b>", label, GUILayout.Width(230f));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Height(22f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(v, value)) previewStale = true;
            return v;
        }

        void ScenarioTab(Rect body)
        {
            GUILayout.BeginArea(new Rect(body.x, body.y, Mathf.Min(760f, body.width), body.height));
            const float row = 28f;
            GUILayout.Label("<b>Scripted scenarios</b>", label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < presets.Count; i++)
                if (presets[i] != null && GUILayout.Button(presets[i].config.presetName, button, GUILayout.Height(row * 1.3f))) Scenario = presets[i].config.Clone().Validate();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            Scenario.seed = Stepper("Seed", Scenario.seed, 1, row, int.MinValue, int.MaxValue);
            if (GUILayout.Button("Randomize (new seed)", button, GUILayout.Height(row))) Scenario = ScenarioRandomizer.Randomize(System.Environment.TickCount & 0xFFFFF, randomizeBounds, Scenario, Difficulty);
            if (GUILayout.Button("Randomize from this seed", button, GUILayout.Height(row))) Scenario = ScenarioRandomizer.Randomize(Scenario.seed, randomizeBounds, Scenario, Difficulty);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Scenario: {Scenario.presetName}", small);

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            Scenario.hostileCount = Stepper("Hostile", Scenario.hostileCount, 1, row);
            Scenario.friendlyCount = Stepper("Own ISR", Scenario.friendlyCount, 1, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.civilianCount = Stepper("Civilian", Scenario.civilianCount, 1, row);
            Scenario.birdCount = Stepper("Birds", Scenario.birdCount, 1, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.duration = Stepper("Duration s", Mathf.RoundToInt(Scenario.duration), 30, row);
            Scenario.spawnRingRadius = Stepper("Ring m", Mathf.RoundToInt(Scenario.spawnRingRadius), 100, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.spawnWindowStart = Stepper("Spawn from s", Mathf.RoundToInt(Scenario.spawnWindowStart), 5, row);
            Scenario.spawnWindowEnd = Stepper("Spawn until s", Mathf.RoundToInt(Scenario.spawnWindowEnd), 30, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.sectorArcDegrees = Stepper("Arc deg", Mathf.RoundToInt(Scenario.sectorArcDegrees), 10, row, 1, 360);
            Scenario.speedMultiplier = Stepper("Speed %", Mathf.RoundToInt(Scenario.speedMultiplier * 100f), 10, row, 1) / 100f;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.swarmSize = Stepper("Swarm size", Scenario.swarmSize, 8, row);
            Scenario.swarmTime = Stepper("Swarm at s", Mathf.RoundToInt(Scenario.swarmTime), 10, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.birdFlocks = Stepper("Bird flocks", Scenario.birdFlocks, 1, row);
            Scenario.flockSize = Stepper("Flock size", Scenario.flockSize, 2, row, 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.blendInPercent = Stepper("Blend-in %", Mathf.RoundToInt(Scenario.blendInPercent), 5, row, -1, 100);
            GUILayout.Label(Scenario.blendInPercent < 0f ? "Hostiles posing as own drones: -1 = set by the difficulty level (none on Recruit, 10 % Trained, 30 % Expert)." : "of the hostiles fly an own-drone patrol to blend in, then break off for the post.", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Time: " + Scenario.timeOfDay.ToString().ToUpper(), button, GUILayout.Height(row))) Scenario.timeOfDay = (TimeOfDay)(((int)Scenario.timeOfDay + 1) % 3);
            if (GUILayout.Button("Visibility: " + Scenario.visibility.ToString().ToUpper(), button, GUILayout.Height(row))) Scenario.visibility = (Visibility)(((int)Scenario.visibility + 1) % 3);
            if (difficulties.Count > 0 && GUILayout.Button("Difficulty: " + (Difficulty != null ? Difficulty.displayName.ToUpper() : "-"), button, GUILayout.Height(row))) difficultyIndex = (difficultyIndex + 1) % difficulties.Count;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Site: " + Scenario.site.ToString().ToUpper(), button, GUILayout.Height(row))) Scenario.site = (SiteType)(((int)Scenario.site + 1) % 3);
            if (GUILayout.Button("Sensors: " + (Scenario.sensorsDegraded ? "DEGRADED" : "OK"), button, GUILayout.Height(row))) Scenario.sensorsDegraded = !Scenario.sensorsDegraded;
            if (GUILayout.Button("ROE at start: " + RoeCards.Short(Scenario.roeAtStart), button, GUILayout.Height(row))) Scenario.roeAtStart = RoeCards.Next(Scenario.roeAtStart);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            // Card change: off, at a set time, or at a time drawn from the seed.
            string flipMode = !Scenario.roeFlip ? "OFF" : Scenario.roeFlipRandom ? "RANDOM TIME" : "AT A SET TIME";
            if (GUILayout.Button("Card change: " + flipMode, button, GUILayout.Height(row)))
            {
                if (!Scenario.roeFlip) { Scenario.roeFlip = true; Scenario.roeFlipRandom = false; }
                else if (!Scenario.roeFlipRandom) Scenario.roeFlipRandom = true;
                else { Scenario.roeFlip = false; Scenario.roeFlipRandom = false; }
            }
            if (Scenario.roeFlip && GUILayout.Button("to " + RoeCards.Short(Scenario.roeFlipTo), button, GUILayout.Height(row))) Scenario.roeFlipTo = RoeCards.Next(Scenario.roeFlipTo);
            if (Scenario.roeFlip && !Scenario.roeFlipRandom) Scenario.roeFlipTime = Stepper("Change at s", Mathf.RoundToInt(Scenario.roeFlipTime), 10, row);
            GUILayout.EndHorizontal();
            // Soldier: fixed at the post, or free to walk within a radius.
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Soldier movement: " + (Scenario.allowMovement ? "ALLOWED (W A S D, Shift to run)" : "FIXED AT POST"), button, GUILayout.Width(330f), GUILayout.Height(row))) Scenario.allowMovement = !Scenario.allowMovement;
            var vrColour = GUI.backgroundColor;
            if (Scenario.vrMode) GUI.backgroundColor = new Color(0.3f, 0.75f, 1f);
            if (GUILayout.Button("<b>VR MODE: " + (Scenario.vrMode ? "ON" : "OFF") + "</b>", button, GUILayout.Width(150f), GUILayout.Height(row))) Scenario.vrMode = !Scenario.vrMode;
            GUI.backgroundColor = vrColour;
            GUI.enabled = Scenario.allowMovement;
            Scenario.moveRadius = Stepper("Radius m", Mathf.RoundToInt(Scenario.moveRadius), 10, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.walkSpeed = Stepper("Walk m/s", Mathf.RoundToInt(Scenario.walkSpeed), 1, row, 1);
            Scenario.runSpeed = Stepper("Run m/s", Mathf.RoundToInt(Scenario.runSpeed), 1, row, 1);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            Scenario.Validate();
            string warning = Scenario.Warning();
            if (warning.Length > 0) GUILayout.Label("<color=#FFB040>" + warning + "</color>", label);
            GUILayout.Space(8f);
            GUILayout.Label("Type a number into any box and press Enter (or click elsewhere), or use - / +. Difficulty is applied on top of these numbers when the run starts. Drone spawn points and player posts are set on the Map tab.", small);
            GUILayout.EndArea();
        }

        DifficultyProfile Difficulty => difficulties.Count > 0 ? difficulties[Mathf.Clamp(difficultyIndex, 0, difficulties.Count - 1)] : null;

        // Label, a box to type the value into, and - / + buttons.
        int Stepper(string name, int value, int step, float row, int min = 0, int max = int.MaxValue)
        {
            return NumberEntry.Stepper("menu." + name, name, value, step, min, max, label, field, button, row, 110f);
        }

        void MapTab(Rect body)
        {
            if (preview == null || previewStale) GeneratePreview();
            float size = Mathf.Min(body.height, body.width - 360f);
            var map = new Rect(body.x, body.y, size, size);
            Fill(map, new Color(0.1f, 0.12f, 0.15f));
            if (preview != null) GUI.DrawTexture(map, preview);

            float area = Terrain.sizeMetres;
            Func<Vector2, Vector2> toMap = m => new Vector2(map.x + (m.x / area + 0.5f) * map.width, map.y + (0.5f - m.y / area) * map.height);

            // A click on the map does what the selected tool says.
            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && map.Contains(e.mousePosition))
            {
                var metres = new Vector2(((e.mousePosition.x - map.x) / map.width - 0.5f) * area, (0.5f - (e.mousePosition.y - map.y) / map.height) * area);
                if (tool == MapTool.PlacePost) PlacePost(selectedPlayer, metres);
                else if (tool == MapTool.AddSpawn) AddSpawnPoint(metres);
                else RemoveNearestSpawnPoint(metres);
                e.Use();
            }

            if (e.type == EventType.Repaint)
            {
                // Default spawn ring around each post when no points are placed.
                if (SpawnPoints.Count == 0)
                    foreach (var p in Players)
                        for (int i = 0; i < 72; i++)
                        {
                            float a = i * 5f * Mathf.Deg2Rad;
                            Dot(toMap(p.post + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * Scenario.spawnRingRadius), 2f, new Color(1f, 0.4f, 0.3f, 0.7f));
                        }
                foreach (var s in SpawnPoints) Dot(toMap(s), 9f, new Color(1f, 0.3f, 0.25f));
                for (int i = 0; i < Players.Count; i++)
                {
                    Vector2 at = toMap(Players[i].post);
                    Dot(at, i == selectedPlayer ? 15f : 12f, Color.black);
                    Dot(at, i == selectedPlayer ? 11f : 8f, i == selectedPlayer ? new Color(0.4f, 0.8f, 1f) : Color.white);
                    GUI.Label(new Rect(at.x + 9f, at.y - 10f, 140f, 20f), Players[i].name, small);
                }
            }

            GUILayout.BeginArea(new Rect(map.xMax + 20f, body.y, body.width - size - 20f, body.height));
            const float row = 28f;
            GUILayout.Label("<b>Click the map to...</b>", label);
            tool = (MapTool)GUILayout.SelectionGrid((int)tool, new[] { "Place the selected player's post", "Add a drone spawn point", "Remove the nearest spawn point" }, 1, button, GUILayout.Height(row * 3.2f));

            GUILayout.Space(8f);
            GUILayout.Label("<b>Players</b>", label);
            for (int i = 0; i < Players.Count; i++)
                if (GUILayout.Toggle(selectedPlayer == i, $"{Players[i].name}{(Players[i].isLocal ? " (this machine)" : "")}   at {Players[i].post.x:F0} E, {Players[i].post.y:F0} N", button, GUILayout.Height(row))) selectedPlayer = i;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add player", button, GUILayout.Height(row)))
            {
                Players.Add(new PlayerSlot { name = "Player " + (Players.Count + 1), post = new Vector2(150f * Players.Count, 0f) });
                selectedPlayer = Players.Count - 1;
                previewStale = true;
            }
            if (GUILayout.Button("Remove last", button, GUILayout.Height(row)) && Players.Count > 1)
            {
                Players.RemoveAt(Players.Count - 1);
                selectedPlayer = Mathf.Min(selectedPlayer, Players.Count - 1);
                previewStale = true;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8f);
            GUILayout.Label($"<b>Drone spawn points: {SpawnPoints.Count}</b>", label);
            GUILayout.Label(SpawnPoints.Count == 0 ? $"None placed, so drones appear on a {Scenario.spawnRingRadius:F0} m ring in front of the post (the dotted circle)." : "Drones appear at these points, spread about 30 m around each.", small);
            if (GUILayout.Button("Clear spawn points (use the ring)", button, GUILayout.Height(row))) SpawnPoints.Clear();

            GUILayout.Space(8f);
            GUILayout.Label("White square = player post, red = drone spawn. Only Player 1 is played on this machine until LAN multiplayer is added; the other posts are placed and flattened now so they are ready for it.", small);
            GUILayout.EndArea();
        }

        void LobbyTab(Rect body)
        {
            GUILayout.BeginArea(new Rect(body.x, body.y, Mathf.Min(760f, body.width), body.height));
            const float row = 30f;
            GUILayout.Label("<b>Lobby</b>", label);
            GUILayout.Label("LAN multiplayer is not built yet: the networking stack has to be chosen first, because it means adding a package. This tab shows how it will work, with the one local player.", small);
            GUILayout.Space(8f);
            GUI.enabled = false;
            GUILayout.BeginHorizontal();
            GUILayout.Button("Create lobby (host)", button, GUILayout.Height(row));
            GUILayout.Button("Join lobby", button, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUILayout.Space(8f);
            GUILayout.Label("<b>Players</b>", label);
            foreach (var p in Players)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{p.name}{(p.isLocal ? "  (this machine)" : "  (waiting for a network player)")}", label, GUILayout.Width(380f), GUILayout.Height(row));
                using (new GUIEnabledScope(p.isLocal))
                    if (GUILayout.Button(p.ready ? "READY" : "Not ready", button, GUILayout.Width(140f), GUILayout.Height(row))) p.ready = !p.ready;
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(8f);
            GUILayout.Label("When networking is in: the trainer hosts, players join over the LAN and press Ready, START sends this terrain, scenario and post list to everyone, and each player is spawned at their post.", small);
            GUILayout.EndArea();
        }

        struct GUIEnabledScope : IDisposable
        {
            readonly bool previous;
            public GUIEnabledScope(bool enabled) { previous = GUI.enabled; GUI.enabled = enabled; }
            public void Dispose() { GUI.enabled = previous; }
        }

        static void Dot(Vector2 p, float size, Color c)
        {
            Fill(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), c);
        }

        static void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
