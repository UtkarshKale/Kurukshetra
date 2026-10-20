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

        enum Tab { Terrain, Scenario, Map, Editor, Board, Lobby }

        Tab tab;
        int selectedPlayer;
        readonly PathEditor pathEd = new PathEditor();       // the 2D path / spawn / post editor on the MAP tab
        ProceduralPreset terrainPreset = ProceduralPreset.RollingHills;
        Texture2D preview;
        bool previewStale = true;
        string demPath = "", demLoadedFrom = "", imageryPath = "";
        GUIStyle label, button, title, small, field;
        Vector2 scroll;
        // One persistent scroll position per area that can grow taller than the window.
        Vector2 scenarioScroll, mapPanelScroll, lobbyScroll, editorListScroll, editorChecksScroll, messageScroll;
        GUIStyle tabStyle;

        // Real place: picked on the OpenStreetMap map, elevation downloaded from the AWS Terrain Tiles.
        public MapPicker Map { get; private set; }
        bool showMap;
        string searchText = "", areaName = "Area";
        public bool Downloading { get; private set; }
        public bool WithSatellite = true;      // also download a satellite picture to colour the terrain
        public bool WithMapFeatures = true;    // also fetch the OpenStreetMap features (buildings, trees, forests, grass) once, for the Realistic layers
        float downloadProgress;
        string downloadStep = "";
        List<string> areas = new List<string>();
        float areasCheckedAt = -100f;
        string deleteArmed;
        bool tilesArmed;

        static readonly string[] TabNames = { "1  TERRAIN", "2  SCENARIO", "3  MAP", "EDITOR", "BOARD", "4  LOBBY" };
        static readonly string[] ModeNames = { "Real world\naccurate", "Real world\n+ subtle detail", "Real world\n+ major variations", "Pure\nprocedural", "Custom\nhybrid" };

        void Awake()
        {
            DifficultyProfile.EnsureExtreme(difficulties);      // the Extreme level lives in code too: the scene lists stay untouched
            Terrain = terrainTemplate != null ? Instantiate(terrainTemplate) : ScriptableObject.CreateInstance<TerrainGenConfig>();
            Terrain.name = "Session terrain";
            if (terrainTemplate == null) Terrain.ApplyPreset(ProceduralPreset.RollingHills);
            demPath = Terrain.demPath ?? "";
            Scenario = presets.Count > 0 && presets[0] != null ? presets[0].config.Clone().Validate() : new ScenarioConfig();
            Players.Add(new PlayerSlot { name = "Player 1", isLocal = true });
            AdoptEditorSession();
            if (Chaukanna.Net.LanSession.Active) tab = Tab.Lobby;
            bool haveCentre = Terrain.centreLatitude != 0.0 || Terrain.centreLongitude != 0.0;
            Map = new MapPicker(this, haveCentre ? Terrain.centreLatitude : 32.6875, haveCentre ? Terrain.centreLongitude : 74.8375, 12);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SessionSetup.Clear();       // back in the menu: the previous session is over
        }

        // A scenario or package that carries player posts takes them, and its spawn points, onto the Map tab.
        void AdoptPosts(ScenarioConfig c)
        {
            if (c == null || c.playerPosts == null || c.playerPosts.Count == 0) return;
            Players.Clear();
            for (int i = 0; i < c.playerPosts.Count; i++) Players.Add(new PlayerSlot { name = c.playerPosts[i].name, post = ClampToArea(c.playerPosts[i].post, 100f), isLocal = i == 0 });
            SpawnPoints.Clear();
            if (c.spawnPoints != null) foreach (var s in c.spawnPoints) SpawnPoints.Add(ClampToArea(s, 20f));
            selectedPlayer = 0;
            previewStale = true;
        }

        // Back from the 3D editor (or from a test run started in it): the terrain, the scenario with its paths, objects and groups, the posts and
        // the spawn points are what the trainer left them, not the defaults.
        void AdoptEditorSession()
        {
            if (!SessionSetup.fromEditor || SessionSetup.terrain == null || SessionSetup.scenario == null) return;
            if (Terrain != null) Destroy(Terrain);
            Terrain = SessionSetup.terrain;
            Terrain.name = "Session terrain";
            Scenario = SessionSetup.scenario.Validate();
            difficultyIndex = SessionSetup.difficultyIndex;
            Dem = SessionSetup.dem; Picture = SessionSetup.imagery;
            SurroundDem = SessionSetup.surroundDem; SurroundPicture = SessionSetup.surroundImagery;
            demPath = Terrain.demPath ?? ""; demLoadedFrom = Dem != null ? Terrain.demPath : ""; imageryPath = Terrain.imageryPath ?? "";
            Players.Clear();
            foreach (var p in SessionSetup.players) Players.Add(new PlayerSlot { name = p.name, post = p.post, ready = p.ready, isLocal = p.isLocal });
            if (Players.Count == 0) Players.Add(new PlayerSlot { name = "Player 1", isLocal = true });
            SpawnPoints.Clear();
            SpawnPoints.AddRange(SessionSetup.spawnPoints);
            previewStale = true;
            tab = Tab.Scenario;
            Message = $"Back from the 3D editor: {Scenario.paths.Count} paths, {Scenario.manualObjects.Count} placed objects, {Scenario.placedGroups.Count} placed groups.";
        }

        // Builds the field like START does, then opens the 3D scenario editor in it instead of starting the run.
        public bool Open3DEditor()
        {
            var problems = Terrain.Validate();
            if (Terrain.UsesDem && problems.Count == 0 && (Dem == null || demLoadedFrom != Terrain.demPath)) problems.Add("Load the elevation file on the Terrain tab first.");
            if (Players.Count == 0) problems.Add("Add at least one player on the Map tab.");
            if (Chaukanna.Net.LanSession.Current != Chaukanna.Net.LanSession.Role.None) problems.Add("Leave the LAN lobby first: the 3D editor is for one PC.");
            if (Downloading) problems.Add("Wait for the download to finish.");
            if (problems.Count > 0) { Message = "3D editor: " + string.Join(" ", problems); return false; }

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
            SessionSetup.fromEditor = true;
            SessionSetup.editorMode = true;
            SessionSetup.Ready = true;
            SessionSetup.LoadWithScreen(fieldScenePath);
            return true;
        }

        void OnDestroy()
        {
            if (preview != null) Destroy(preview);
            tPrev.Release();
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
            yield return ElevationDownloader.Download(req, step("Elevation", 0f, 0.3f), r => area = r);
            if (area == null || !area.ok) { Downloading = false; areasCheckedAt = -100f; Message = area?.error ?? "Download failed."; downloadStep = "Failed"; yield break; }
            string notes = area.summary;

            if (WithSatellite)
            {
                ImageryDownloadResult picture = null;
                yield return ImageryDownloader.Download(req, ImageryDownloader.PathFor(area.rawPath), step("Satellite", 0.3f, 0.2f), r => picture = r);
                notes += "\n" + (picture != null && picture.ok ? picture.summary : picture?.error);
            }

            // The surround: the same place, four times as wide, coarser. Only used for the view to the horizon.
            var around = new AreaRequest { name = System.IO.Path.GetFileNameWithoutExtension(area.rawPath) + ElevationDownloader.SurroundSuffix, folder = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(area.rawPath)),
                latitude = req.latitude, longitude = req.longitude, radiusMetres = req.radiusMetres * ElevationDownloader.SurroundFactor };
            AreaDownloadResult surround = null;
            yield return ElevationDownloader.Download(around, step("Surround", 0.5f, 0.1f), r => surround = r);
            if (surround != null && surround.ok && WithSatellite)
                yield return ImageryDownloader.Download(around, ImageryDownloader.PathFor(surround.rawPath), step("Surround picture", 0.6f, 0.15f), r => { });
            notes += surround != null && surround.ok ? $"\nSurround for the horizon: {around.SideMetres / 1000f:F0} km square." : "\nNo surround (" + surround?.error + ")";

            // Map features (OpenStreetMap) for the Realistic trees, grass and buildings. A failure keeps the terrain.
            if (WithMapFeatures)
            {
                OsmFetchResult features = null;
                yield return OsmFetcher.Fetch(area.rawPath, step("Map features (OpenStreetMap)", 0.75f, 0.25f), r => features = r);
                notes += features != null && features.ok ? "\nMap features (OpenStreetMap): " + features.file.CountsLine() + "." + (features.note.Length > 0 ? " " + features.note : "")
                    : "\nNo map features (" + (features?.error ?? "not fetched") + "). Realistic layers use the satellite colour for trees and grass and have no buildings.";
            }

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
            if (Chaukanna.Net.LanSession.Current == Chaukanna.Net.LanSession.Role.Host && !Chaukanna.Net.LanSession.Instance.roster.AllReady()) problems.Add("Not every trainee on the Lobby tab is READY.");
            if (Chaukanna.Net.LanSession.Current == Chaukanna.Net.LanSession.Role.Client && !startingFromHost) problems.Add("Joined to a trainer: the trainer starts the run.");
            return problems;
        }

        // The DEMO button (and its presenter checklist, shown only in a demo run) is hidden: the owner will record the demo
        // (2026-10-20). The code stays so it can be switched back on.
        const bool ShowDemo = false;

        // DEMO: the best real terrain already on this PC (else a procedural plain), the curated demo scenario (a hostile quad group, a swarm of 200,
        // a bird flock, own ISR, civilians, an ROE card change, two paths), one player at the post, sound on, then START. Nothing is downloaded.
        public bool StartDemo()
        {
            if (Downloading) { Message = "DEMO: wait for the download to finish."; return false; }
            if (Chaukanna.Net.LanSession.Current != Chaukanna.Net.LanSession.Role.None) { Message = "DEMO: leave the LAN lobby first (the demo is a one-PC run)."; return false; }
            string note;
            string raw = DemoScenario.PickArea(out string why);
            bool real = false;
            try { real = raw != null && UseArea(ElevationDownloader.ProjectRelative(raw)); }
            catch (Exception e) { Debug.LogWarning("[Demo] Could not use area: " + e.Message); }
            if (real) note = "real terrain " + why;
            else
            {
                Dem = null; demLoadedFrom = "";
                SetTerrainMode(TerrainMode.PureProcedural);
                SetTerrainPreset(ProceduralPreset.PlainsWithPeaks);
                note = raw == null ? "procedural terrain (Plains with peaks): no real area on this PC" : "procedural terrain (Plains with peaks): the real area could not be loaded";
            }
            Scenario = DemoScenario.Build(real);
            Players.Clear();
            Players.Add(new PlayerSlot { name = "Player 1", isLocal = true, post = Vector2.zero });
            SpawnPoints.Clear();
            Chaukanna.Audio.SoundEngine.Enabled = true;
            if (Chaukanna.Audio.SoundEngine.Volume < 50) Chaukanna.Audio.SoundEngine.Volume = 70;
            if (!StartSession()) return false;
            SessionSetup.demo = true;
            Message = "DEMO starting: " + note + ".";
            return true;
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
            // LAN host: send the same set-up to every trainee's PC before loading the field here.
            if (Chaukanna.Net.LanSession.Current == Chaukanna.Net.LanSession.Role.Host)
            {
                var packet = new Chaukanna.Net.StartPacket { scenarioJson = JsonUtility.ToJson(Scenario), terrainJson = JsonUtility.ToJson(Terrain), difficulty = difficultyIndex, spawnPoints = new List<Vector2>(SpawnPoints) };
                // A real-terrain run: list the area's files, so trainees without it can ask for them.
                string areaFolder = null;
                if (Terrain.UsesDem)
                {
                    string dem = ElevationDownloader.Resolve(Terrain.demPath);
                    areaFolder = System.IO.Path.GetDirectoryName(dem);
                    if (System.IO.Directory.Exists(areaFolder))
                    {
                        packet.areaName = System.IO.Path.GetFileName(areaFolder);
                        packet.demFile = System.IO.Path.GetFileName(dem);
                        foreach (var f in System.IO.Directory.GetFiles(areaFolder))
                            if (!f.EndsWith(".meta") && !f.EndsWith(".key") && !f.EndsWith("_osm.json") && !f.EndsWith("_osm_surround.json")) packet.areaFiles.Add(new Chaukanna.Net.AreaFile { name = System.IO.Path.GetFileName(f), size = new System.IO.FileInfo(f).Length });
                    }
                }
                Chaukanna.Net.LanSession.Instance.HostStart(packet, areaFolder);
            }
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
            // The title takes only the width its text needs; the tab toolbar fills the gap up to START (never under it).
            const string titleText = "KURUKSHETRA  TRAINER";
            float titleW = Mathf.Min(300f, Mathf.Ceil(title.CalcSize(new GUIContent(titleText)).x) + 6f);
            GUI.Label(new Rect(pad, 8f, titleW, 38f), titleText, title);
            float tabsX = pad + titleW + 12f;
            float tabsW = Mathf.Min(900f, w - 290f - 14f - tabsX);       // START begins at w - 180 (DEMO at w - 290 when shown)
            if (tabStyle == null) tabStyle = new GUIStyle(button);
            tabStyle.fontSize = tabsW < 700f ? Mathf.Max(10, fontSize - 3) : fontSize;
            tab = (Tab)GUI.Toolbar(new Rect(tabsX, 10f, Mathf.Max(200f, tabsW), 34f), (int)tab, TabNames, tabStyle);
            var problems = StartProblems();
            var old = GUI.backgroundColor;
            GUI.backgroundColor = problems.Count == 0 ? new Color(0.3f, 0.9f, 0.4f) : new Color(0.6f, 0.6f, 0.6f);
            if (GUI.Button(new Rect(w - 180f, 8f, 166f, 38f), "<b>START</b>", new GUIStyle(button) { fontSize = fontSize + 6 })) StartSession();
            GUI.backgroundColor = new Color(0.95f, 0.75f, 0.25f);
            if (ShowDemo && GUI.Button(new Rect(w - 290f, 8f, 104f, 38f), new GUIContent("<b>DEMO</b>", "One click: the best terrain on this PC, a curated 3.5-minute scenario (hostile quads, a swarm of 200, a bird flock, own ISR, a civilian, an ROE card change, two paths), sound on, and START. The presenter checklist shows in the run (F8 hides it)."), new GUIStyle(button) { fontSize = fontSize + 4 })) StartDemo();
            GUI.backgroundColor = old;
            if (GUI.Button(new Rect(w - 290f, 8f, 104f, 38f), new GUIContent("Settings", "Voice and graphics settings (also F10)."), new GUIStyle(button) { fontSize = fontSize })) Chaukanna.Net.VoiceSettings.OpenWindow();

            // Message strip along the bottom.
            const float msgH = 62f;
            Fill(new Rect(0f, h - msgH, w, msgH), new Color(0.09f, 0.11f, 0.14f));
            string msg = problems.Count > 0 ? "<color=#FFB040>Before START: " + string.Join("  ", problems) + "</color>\n" + Message : Message;
            // A long message scrolls inside the strip instead of running off the bottom of the window.
            GUILayout.BeginArea(new Rect(pad, h - msgH + 4f, w - pad * 2f, msgH - 8f));
            messageScroll = GUILayout.BeginScrollView(messageScroll);
            GUILayout.Label(msg, small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            var body = new Rect(pad, barH + pad, w - pad * 2f, h - barH - msgH - pad * 2f);
            switch (tab)
            {
                case Tab.Terrain: TerrainTab(body); break;
                case Tab.Scenario: ScenarioTab(body); break;
                case Tab.Map: MapTab(body); break;
                case Tab.Editor: EditorTab(body); break;
                case Tab.Board: BoardTab(body); break;
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

            LayersBlock(row);

            GUILayout.Space(8f);
            if (GUILayout.Button(previewStale ? "<b>Generate preview</b>" : "Regenerate preview", button, GUILayout.Height(row * 1.5f))) GeneratePreview();
            GUILayout.Label("The same settings and seed are used to build the real terrain when you press START.", small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Right side: the OpenStreetMap picker or the height preview.
            float size = Mathf.Min(body.height - 36f, body.width - leftW - 20f);
            float rx = body.x + leftW + 20f;
            if (GUI.Toggle(new Rect(rx, body.y, 150f, 30f), showMap, "Map: pick a place", button)) showMap = true;
            if (GUI.Toggle(new Rect(rx + 156f, body.y, 150f, 30f), !showMap, "Preview terrain", button) && showMap)
            {
                showMap = false;
                if (tPrev.Texture == null && !tPrev.Busy) StartTerrainPreview();      // first visit builds it; later visits keep what is shown
            }
            var map = new Rect(rx, body.y + 36f, size, size);
            if (showMap)
            {
                Map.Draw(map, small);
                return;
            }
            TerrainPreviewPanel(new Rect(rx, body.y + 36f, Mathf.Min(body.width - leftW - 20f, body.height - 36f), body.height - 36f));
        }

        // --- Preview terrain: a real top-down picture (relief, satellite drape, layer overlays) built a little each frame. ---

        readonly TerrainPreview tPrev = new TerrainPreview();
        Coroutine tPrevRoutine;

        string TerrainPreviewSignature()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(Terrain.seed).Append('|').Append(Terrain.sizeMetres).Append('|').Append((int)Terrain.biome).Append('|').Append((int)Terrain.mode).Append('|').Append(Terrain.demPath).Append('|').Append(imageryPath)
              .Append('|').Append(Terrain.imageryBlend).Append('|').Append(JsonUtility.ToJson(Terrain.terrainLayers));
            foreach (var p in Players) sb.Append('|').Append(p.post.x).Append(',').Append(p.post.y);
            return sb.ToString();
        }

        void StartTerrainPreview()
        {
            if (!GeneratePreview()) return;      // validates the settings and builds the preview heightfield (Message says what is wrong)
            if (tPrevRoutine != null) StopCoroutine(tPrevRoutine);
            var posts = new List<Vector2>();
            foreach (var p in Players) posts.Add(p.post);
            tPrevRoutine = StartCoroutine(tPrev.Build(new TerrainPreview.Input { cfg = Terrain, field = PreviewField, dem = Dem, picture = Picture, posts = posts, signature = TerrainPreviewSignature() }));
        }

        void TerrainPreviewPanel(Rect r)
        {
            const float row = 28f;
            bool changed = false;
            float x = r.x;
            string[] names = { "Height", "Trees", "Bushes", "Grass", "Buildings" };
            for (int i = 0; i < names.Length; i++)
            {
                bool cur = i == 0 ? tPrev.ShowHeight : i == 1 ? tPrev.ShowTrees : i == 2 ? tPrev.ShowBushes : i == 3 ? tPrev.ShowGrass : tPrev.ShowBuildings;
                float w = i == 4 ? 92f : 74f;
                bool now = GUI.Toggle(new Rect(x, r.y, w, row), cur, names[i], button);
                x += w + 4f;
                if (now == cur) continue;
                changed = true;
                if (i == 0) tPrev.ShowHeight = now; else if (i == 1) tPrev.ShowTrees = now; else if (i == 2) tPrev.ShowBushes = now; else if (i == 3) tPrev.ShowGrass = now; else tPrev.ShowBuildings = now;
            }
            if (changed && !tPrev.Busy) tPrev.Compose();
            if (GUI.Button(new Rect(x + 6f, r.y, 110f, row), tPrev.Busy ? "Working..." : "Regenerate", button) && !tPrev.Busy) StartTerrainPreview();

            float size = Mathf.Min(r.width, r.height - row - 40f);
            var map = new Rect(r.x, r.y + row + 6f, size, size);
            Fill(map, new Color(0.1f, 0.12f, 0.15f));
            if (tPrev.Texture != null) GUI.DrawTexture(map, tPrev.Texture);
            string status;
            if (tPrev.Texture == null) status = tPrev.Busy ? "Building the preview ..." : "No preview yet. Press Regenerate.";
            else if (tPrev.Busy) status = $"Refining ... {Mathf.RoundToInt(tPrev.Progress * 100f)}%";
            else if (tPrev.Signature != TerrainPreviewSignature()) status = "<color=#FFB040>Settings changed since this preview. Press Regenerate.</color>";
            else status = "North is up. White squares are the player posts. Trees, bushes, grass and buildings are where the real layers will put them (a schematic, not each plant).";
            if (tPrev.Note.Length > 0) status += "\n<color=#FFB040>" + tPrev.Note + "</color>";
            GUI.Label(new Rect(r.x, map.yMax + 4f, Mathf.Max(200f, size), 56f), status, small);
        }

        // --- Trees, grass and buildings ---

        string layerInfoFor = "\u0001", layerInfo = "";
        bool fetchingFeatures;
        float featProgress;
        string featStep = "", featFailed = "";
        static readonly string[] LayerModeNames = { "OFF", "REALISTIC", "PROCEDURAL" };

        // Four layers, each Off / Realistic / Procedural, and the numbers behind them. Realistic = the real place
        // (OpenStreetMap, with a satellite-colour estimate for trees and grass where the map has nothing).
        void LayersBlock(float row)
        {
            var L = Terrain.terrainLayers;
            bool real = Terrain.UsesDem;
            GUILayout.Space(8f);
            GUILayout.Label("<b>Trees, bushes, grass and buildings</b>  (near = detailed, far = less)", label);
            foreach (LayerKind kind in Enum.GetValues(typeof(LayerKind)))
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(kind.ToString(), label, GUILayout.Width(86f), GUILayout.Height(row));
                var shown = L.Effective(kind, real);
                for (int i = 0; i < 3; i++)
                    using (new GUIEnabledScope(i != (int)LayerMode.Realistic || real))
                        if (GUILayout.Toggle(shown == (LayerMode)i, LayerModeNames[i], button, GUILayout.Height(row)) && shown != (LayerMode)i) L.Set(kind, (LayerMode)i);
                GUILayout.EndHorizontal();
            }
            if (!real) GUILayout.Label("Realistic needs a real place: on a procedural terrain every layer is procedural.", small);

            GUILayout.BeginHorizontal();
            L.treeDensity = (float)Math.Max(0.0, NumberEntry.FieldDouble("menu.treeDensity", L.treeDensity, 0, field, GUILayout.Width(70f), GUILayout.Height(row)));
            GUILayout.Label(" trees per ha", label, GUILayout.Height(row));
            L.grassDensity = Mathf.Clamp01((float)NumberEntry.FieldDouble("menu.grassDensity", L.grassDensity, 2, field, GUILayout.Width(60f), GUILayout.Height(row)));
            GUILayout.Label(" grass cover 0-1", label, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            L.buildingDensity = (float)Math.Max(0.0, NumberEntry.FieldDouble("menu.buildingDensity", L.buildingDensity, 2, field, GUILayout.Width(70f), GUILayout.Height(row)));
            GUILayout.Label(" villages per km² (procedural)", label, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            L.treeBudget = (int)Math.Min(int.MaxValue, Math.Max(0.0, NumberEntry.FieldDouble("menu.treeBudget", L.treeBudget, 0, field, GUILayout.Width(80f), GUILayout.Height(row))));
            GUILayout.Label(" most trees at once (0 = no limit; each costs frame time)", label, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            L.bushDensity = (float)Math.Max(0.0, NumberEntry.FieldDouble("menu.bushDensity", L.bushDensity, 2, field, GUILayout.Width(60f), GUILayout.Height(row)));
            GUILayout.Label(" bush density (1 = 2 to 5 bushes round a third of the trees, plus a scatter along wood edges)", label, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            L.bushBudget = (int)Math.Min(int.MaxValue, Math.Max(0.0, NumberEntry.FieldDouble("menu.bushBudget", L.bushBudget, 0, field, GUILayout.Width(80f), GUILayout.Height(row))));
            GUILayout.Label(" most bushes in the world (0 = no limit)", label, GUILayout.Height(row));
            L.bushDrawMetres = (float)Math.Max(10.0, NumberEntry.FieldDouble("menu.bushDraw", L.bushDrawMetres, 0, field, GUILayout.Width(60f), GUILayout.Height(row)));
            GUILayout.Label(" m bush draw distance", label, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            L.treeLodBias = (float)Math.Max(0.01, NumberEntry.FieldDouble("menu.treeLodBias", L.treeLodBias, 2, field, GUILayout.Width(60f), GUILayout.Height(row)));
            GUILayout.Label(" tree draw distance (0.2 = about 1.2 km, 0.5 = 2.9 km; farther costs frame time)", label, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            L.falloffStartKm = (float)Math.Max(0.05, NumberEntry.FieldDouble("menu.falloff", L.falloffStartKm, 2, field, GUILayout.Width(60f), GUILayout.Height(row)));
            GUILayout.Label(" km: full density this near the post, thinner beyond", label, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            L.treesBlockSight = GUILayout.Toggle(L.treesBlockSight, "  Trees block the line of sight", GUILayout.Height(row));
            L.fillGapsProcedurally = GUILayout.Toggle(L.fillGapsProcedurally, "  Realistic buildings: fill empty 500 m cells procedurally (labelled as filled)", GUILayout.Height(row));

            // What data the Realistic mode has for the chosen area.
            if (real)
            {
                if (layerInfoFor != Terrain.demPath) { layerInfoFor = Terrain.demPath; layerInfo = LayerFile.Describe(Terrain.demPath); }
                bool needsData = L.trees == LayerMode.Realistic || L.grass == LayerMode.Realistic || L.buildings == LayerMode.Realistic;
                if (layerInfo.Length > 0) GUILayout.Label(layerInfo + " Map features © OpenStreetMap contributors (ODbL).", small);
                else if (needsData) GUILayout.Label("<color=#FFB040>No OSM data for this area yet: Realistic uses satellite colour for trees and grass, no buildings.</color>", small);
                if (layerInfo.Length == 0 && Dem != null && demLoadedFrom == Terrain.demPath)
                {
                    // The progress is drawn on the button itself: a fill bar, the percent and the step, then done / failed.
                    string idle = featFailed.Length > 0 ? "Failed: " + featFailed + "  (click to retry)" : "Fetch map features (OpenStreetMap) for this area";
                    string text = fetchingFeatures ? $"Fetching map features  {Mathf.RoundToInt(featProgress * 100f)} %  {featStep}" : idle;
                    using (new GUIEnabledScope(!fetchingFeatures && !Downloading))
                    {
                        var br = GUILayoutUtility.GetRect(new GUIContent(text), button, GUILayout.Height(row));
                        if (GUI.Button(br, GUIContent.none, button)) { featFailed = ""; StartCoroutine(FetchFeatures()); }
                        if (fetchingFeatures)
                        {
                            var old = GUI.color;
                            GUI.color = new Color(0.25f, 0.6f, 0.9f, 0.55f);
                            GUI.DrawTexture(new Rect(br.x + 2f, br.y + 2f, (br.width - 4f) * Mathf.Clamp01(featProgress), br.height - 4f), Texture2D.whiteTexture);
                            GUI.color = old;
                        }
                        GUI.Label(br, text, button);
                    }
                }
            }
        }

        System.Collections.IEnumerator FetchFeatures()
        {
            fetchingFeatures = true;
            string raw = ElevationDownloader.Resolve(Terrain.demPath);
            OsmFetchResult result = null;
            featProgress = 0f; featStep = ""; featFailed = "";
            yield return OsmFetcher.Fetch(raw, (f, what) => { featProgress = f; featStep = what; Message = what; }, r => result = r);
            fetchingFeatures = false;
            featFailed = result != null && result.ok ? "" : (result?.error ?? "not fetched");
            layerInfoFor = "\u0001";
            Message = result != null && result.ok ? "Map features (OpenStreetMap): " + result.file.CountsLine() + "." + (result.note.Length > 0 ? " " + result.note : "") : "No map features: " + (result?.error ?? "not fetched");
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
            WithMapFeatures = GUILayout.Toggle(WithMapFeatures, "  Also fetch map features (OpenStreetMap: buildings, trees, forests, grass), once", GUILayout.Height(row));
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
                    if (ElevationDownloader.CanDeleteArea(a))
                    {
                        bool armed = deleteArmed == a;
                        if (GUILayout.Button(armed ? "Delete? click again" : "Delete", button, GUILayout.Width(armed ? 150f : 70f), GUILayout.Height(row - 4f)))
                        {
                            if (!armed) deleteArmed = a;
                            else
                            {
                                deleteArmed = null;
                                long freed = ElevationDownloader.DeleteArea(a, out string err);
                                Message = err ?? $"Deleted area {System.IO.Path.GetFileNameWithoutExtension(a)}: {freed / 1048576f:F1} MB freed.";
                                areasCheckedAt = -100f;
                            }
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            if (GUILayout.Button(tilesArmed ? "Clear cached map tiles? click again (areas are not touched)" : "Clear cached map tiles (pictures only, re-downloaded when needed)", button, GUILayout.Height(row - 4f)))
            {
                if (!tilesArmed) tilesArmed = true;
                else
                {
                    tilesArmed = false;
                    long freed = ElevationDownloader.ClearTileCache(out int n);
                    Message = $"Cleared {n} cached map tiles: {freed / 1048576f:F1} MB freed.";
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

        // --- Module 11: scenario editor (form + validator). Module 12: proficiency board with sign-in. ---

        ScenarioSpec spec = new ScenarioSpec();
        List<ScenarioSpec> packages;
        bool specUnlocked;
        Vector2 editorScroll, boardScroll;
        string loginId = "", loginPin = "", loginNote = "", editorNote = "";
        List<Chaukanna.Scoring.BoardCell> boardCells;
        List<string> boardNotYet;
        List<Chaukanna.Scoring.BoardFlag> boardFlags;

        static T Cycle<T>(T value) where T : Enum
        {
            var values = (T[])Enum.GetValues(typeof(T));
            int i = Array.IndexOf(values, value);
            return values[(i + 1) % values.Length];
        }

        void EditorTab(Rect body)
        {
            if (packages == null) packages = PackageStore.LoadAll();
            const float row = 26f;
            // Package list on the left.
            GUILayout.BeginArea(new Rect(body.x, body.y, 230f, body.height));
            editorListScroll = GUILayout.BeginScrollView(editorListScroll);
            GUILayout.Label("<b>Packages</b>", label);
            if (GUILayout.Button("+ New package", button, GUILayout.Height(row))) { spec = new ScenarioSpec(); specUnlocked = true; }
            foreach (var p in packages)
                if (GUILayout.Button((p.reference ? "[REF] " : "") + p.name, button, GUILayout.Height(row))) { spec = p.Clone(); specUnlocked = !p.reference; }
            GUILayout.Label("Saved in " + PackageStore.Folder, small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // The form.
            var formRect = new Rect(body.x + 244f, body.y, Mathf.Max(300f, Mathf.Min(660f, body.width - 244f - 330f)), body.height);
            GUILayout.BeginArea(formRect);
            editorScroll = GUILayout.BeginScrollView(editorScroll);
            bool locked = spec.reference && !specUnlocked;
            if (locked)
            {
                GUILayout.Label("<color=#FFB040>Locked reference package. Unlocking it changes the package every soldier is compared on.</color>", small);
                if (GUILayout.Button("Unlock to edit", button, GUILayout.Height(row))) specUnlocked = true;
            }
            GUI.enabled = !locked;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", label, GUILayout.Width(110f));
            spec.name = GUILayout.TextField(spec.name ?? "", 40, field, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            spec.seed = Stepper("Seed", spec.seed, 1, row, int.MinValue, int.MaxValue);
            spec.durationMinutes = Stepper("Minutes", spec.durationMinutes, 1, row, 1, 120);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Light: " + spec.light, button, GUILayout.Height(row))) spec.light = Cycle(spec.light);
            if (GUILayout.Button("Weather: " + spec.weather, button, GUILayout.Height(row))) spec.weather = Cycle(spec.weather);
            if (GUILayout.Button("Site: " + spec.site, button, GUILayout.Height(row))) spec.site = Cycle(spec.site);
            if (GUILayout.Button("Sensors: " + (spec.sensorsDegraded ? "DEGRADED" : "OK"), button, GUILayout.Height(row))) spec.sensorsDegraded = !spec.sensorsDegraded;
            GUILayout.EndHorizontal();
            if (spec.paths == null) spec.paths = new List<FlightPath>();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Custom paths: {spec.paths.Count}", label, GUILayout.Width(140f), GUILayout.Height(row));
            if (GUILayout.Button("Take the paths drawn on the MAP tab", button, GUILayout.Height(row))) spec.paths = FlightPath.CloneAll(Scenario.paths);
            if (GUILayout.Button("Clear", button, GUILayout.Width(80f), GUILayout.Height(row))) spec.paths.Clear();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Card at start: " + RoeCards.Short(spec.roeStart), button, GUILayout.Height(row))) spec.roeStart = RoeCards.Next(spec.roeStart);
            spec.roeChangeSecond = Stepper("Change at s (-1 none)", spec.roeChangeSecond, 30, row, -1, 7200);
            if (spec.roeChangeSecond >= 0 && GUILayout.Button("to " + RoeCards.Short(spec.roeAfter), button, GUILayout.Height(row))) spec.roeAfter = RoeCards.Next(spec.roeAfter);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Prevalence: " + spec.prevalence, button, GUILayout.Height(row))) spec.prevalence = Cycle(spec.prevalence);
            if (spec.prevalence != Prevalence.Custom) spec.prevalenceTotal = Stepper("Contacts", spec.prevalenceTotal, 1, row, 1, 60);
            GUILayout.EndHorizontal();
            if (spec.prevalence == Prevalence.Custom)
            {
                GUILayout.BeginHorizontal();
                spec.hostiles = Stepper("Hostile", spec.hostiles, 1, row, 0, 99);
                spec.ownDrones = Stepper("Own", spec.ownDrones, 1, row, 0, 99);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                spec.civilDrones = Stepper("Civil", spec.civilDrones, 1, row, 0, 99);
                spec.birds = Stepper("Birds", spec.birds, 1, row, 0, 99);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            spec.swarmSize = Stepper("Swarm", spec.swarmSize, 8, row, 0);
            if (spec.swarmSize > 0) spec.swarmSecond = Stepper("Swarm at s", spec.swarmSecond, 15, row, 0, 7200);
            if (spec.swarmSize > 0 && GUILayout.Button($"Tactic: {spec.swarmTactic}", button, GUILayout.Height(row))) spec.swarmTactic = Cycle(spec.swarmTactic);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            spec.birdFlocks = Stepper("Bird flocks", spec.birdFlocks, 1, row, 0, 20);
            spec.ringMetres = Stepper("Ring m", spec.ringMetres, 100, row, 100, 9000);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Quiet watch: " + (spec.quietWatch ? "ON" : "OFF"), button, GUILayout.Height(row))) spec.quietWatch = !spec.quietWatch;
            if (spec.quietWatch)
            {
                spec.watchMinutes = Stepper("Watch min", spec.watchMinutes, 5, row, 1, 180);
                spec.contactsPerHour = Stepper("Per hour", spec.contactsPerHour, 1, row, 0, 20);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Director: " + (spec.director ? "ON (training only)" : "OFF"), button, GUILayout.Height(row))) spec.director = !spec.director;
            if (GUILayout.Button("Reference package: " + (spec.reference ? "YES" : "NO"), button, GUILayout.Height(row))) spec.reference = !spec.reference;
            GUILayout.EndHorizontal();
            if (spec.reference)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Fairness profile id", label, GUILayout.Width(160f));
                spec.fairnessId = GUILayout.TextField(spec.fairnessId ?? "", 24, field, GUILayout.Height(row));
                GUILayout.EndHorizontal();
            }
            GUI.enabled = true;
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Checks and actions on the right.
            var right = new Rect(formRect.xMax + 14f, body.y, Mathf.Clamp(body.xMax - (formRect.xMax + 14f), 120f, 316f), body.height);
            GUILayout.BeginArea(right);
            editorChecksScroll = GUILayout.BeginScrollView(editorChecksScroll);
            var issues = SpecValidator.Validate(spec);
            bool ok = SpecValidator.Ok(issues);
            GUILayout.Label(ok ? "<b><color=#60E080>Valid</color></b>" : "<b><color=#FF6050>Not valid</color></b>", label);
            foreach (var i in issues)
                GUILayout.Label($"<color=#{(i.blocking ? "FF6050" : "B0B8C0")}>{i.code}</color> {i.message}", small);
            GUILayout.Space(8f);
            GUI.enabled = ok;
            if (GUILayout.Button("Save package", button, GUILayout.Height(row)))
            {
                spec.playerPosts = new List<PostPoint>();
                foreach (var p in Players) spec.playerPosts.Add(new PostPoint { name = p.name, post = p.post });
                spec.spawnPoints = new List<Vector2>(SpawnPoints);
                try { PackageStore.Save(spec); packages = PackageStore.LoadAll(); editorNote = "Saved " + spec.name; specUnlocked = !spec.reference; }
                catch (Exception e) { editorNote = "Could not save: " + e.Message; }
            }
            if (GUILayout.Button("Use for START", button, GUILayout.Height(row))) { Scenario = SpecExpander.Expand(spec); AdoptPosts(Scenario); editorNote = $"'{spec.name}' is set up; press START."; }
            GUI.enabled = true;
            if (GUILayout.Button("Delete package", button, GUILayout.Height(row))) { PackageStore.Delete(spec.name); packages = PackageStore.LoadAll(); editorNote = "Deleted " + spec.name; }
            if (editorNote.Length > 0) GUILayout.Label(editorNote, small);
            GUILayout.Label("The form holds only choices and whole numbers; paths, speeds and timings come from the seed. An offline LLM builder that fills this form from a sentence needs your go-ahead for a local model.", small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void BoardTab(Rect body)
        {
            const float row = 26f;
            GUILayout.BeginArea(new Rect(body.x, body.y, Mathf.Min(1100f, body.width), body.height));
            GUILayout.Label("<b><color=#FF7060>" + Chaukanna.Scoring.ProficiencyBoard.Disclaimer + "</color></b>", label);

            // Sign-in: a pseudonymous ID and a PIN. Names never go in; the instructor keeps the ID-to-name list offline.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Soldier ID", label, GUILayout.Width(90f));
            loginId = GUILayout.TextField(loginId, 16, field, GUILayout.Width(140f), GUILayout.Height(row));
            GUILayout.Label("PIN", label, GUILayout.Width(40f));
            loginPin = GUILayout.PasswordField(loginPin, '*', 8, field, GUILayout.Width(100f), GUILayout.Height(row));
            if (GUILayout.Button("Sign in", button, GUILayout.Width(90f), GUILayout.Height(row))) SignIn(false);
            if (GUILayout.Button("Register", button, GUILayout.Width(90f), GUILayout.Height(row))) SignIn(true);
            if (GUILayout.Button("Sign out", button, GUILayout.Width(90f), GUILayout.Height(row))) { Chaukanna.Scoring.ScoreKeeper.PlayerName = "Player 1"; loginNote = "Signed out."; }
            GUILayout.EndHorizontal();
            GUILayout.Label($"Playing as <b>{Chaukanna.Scoring.ScoreKeeper.PlayerName}</b>.  {loginNote}", small);

            if (GUILayout.Button("Refresh board", button, GUILayout.Width(160f), GUILayout.Height(row)) || boardCells == null) RefreshBoard();
            boardScroll = GUILayout.BeginScrollView(boardScroll);
            GUILayout.Label("<b>Reference packages only, Director off</b>  (n = sessions; interval = 95 % of the mean, shown from n = 2)", small);
            if (boardCells.Count == 0) GUILayout.Label("No reference-package sessions yet. Make a package in EDITOR with 'Reference package: YES', then play it.", small);
            foreach (var c in boardCells)
                GUILayout.Label($"{c.player,-12}  {c.package,-24}  n {c.n,2}   latest {c.latest,3:F0} %   mean {c.mean,3:F0} %" + (c.low >= 0f ? $" ({c.low:F0}-{c.high:F0})" : " (n too small)") + (c.process >= 0f ? $"   process {c.process:F0}" : ""), label);
            GUILayout.Space(6f);
            GUILayout.Label("<b>Not yet trained</b>", label);
            GUILayout.Label(boardNotYet.Count == 0 ? "Everyone registered has played every reference package." : string.Join("   ", boardNotYet), small);
            GUILayout.Space(6f);
            GUILayout.Label("<b>Duplicate-timing check</b> (back-to-back sessions under different IDs with indistinguishable decision timing)", label);
            if (boardFlags.Count == 0) GUILayout.Label("No flags.", small);
            foreach (var f in boardFlags) GUILayout.Label($"<color=#FFB040>{f.a} then {f.b} at {f.when}: KS p = {f.p:F2}. Check who played.</color>", small);
            GUILayout.EndScrollView();
            if (GUILayout.Button("Export board (HTML)", button, GUILayout.Width(200f), GUILayout.Height(row)))
            {
                string path = Chaukanna.Scoring.ProficiencyBoard.SaveHtml(Chaukanna.Scoring.ProficiencyBoard.Html(boardCells, boardNotYet, boardFlags));
                loginNote = path != null ? "Board saved: " + path : "Could not save the board.";
                if (path != null) Application.OpenURL("file:///" + path.Replace('\\', '/'));
            }
            GUILayout.EndArea();
        }

        void SignIn(bool register)
        {
            string why = Chaukanna.Scoring.Login.SignIn(loginId, loginPin, register);
            loginPin = "";
            if (why.Length == 0) { Chaukanna.Scoring.ScoreKeeper.PlayerName = loginId; loginNote = (register ? "Registered and signed in as " : "Signed in as ") + loginId + "."; RefreshBoard(); }
            else loginNote = why;
        }

        void RefreshBoard()
        {
            var sessions = Chaukanna.Scoring.ProficiencyBoard.LoadSessions();
            boardCells = Chaukanna.Scoring.ProficiencyBoard.Cells(sessions);
            var soldiers = new List<string>();
            foreach (var s in Chaukanna.Scoring.Login.Load().soldiers) soldiers.Add(s.id);
            var refs = new List<string>();
            foreach (var p in PackageStore.LoadAll()) if (p.reference) refs.Add(p.name);
            boardNotYet = Chaukanna.Scoring.ProficiencyBoard.NotYetTrained(soldiers, refs, boardCells);
            boardFlags = Chaukanna.Scoring.ProficiencyBoard.DuplicateTiming(sessions);
        }

        static float Next(float[] steps, float value)
        {
            for (int i = 0; i < steps.Length; i++) if (steps[i] > value + 0.01f) return steps[i];
            return steps[0];
        }

        void ScenarioTab(Rect body)
        {
            GUILayout.BeginArea(new Rect(body.x, body.y, Mathf.Min(760f, body.width), body.height));
            scenarioScroll = GUILayout.BeginScrollView(scenarioScroll);
            const float row = 28f;
            GUILayout.Label("<b>Scripted scenarios</b>", label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < presets.Count; i++)
                if (presets[i] != null && GUILayout.Button(presets[i].config.presetName, button, GUILayout.Height(row * 1.3f))) Scenario = presets[i].config.Clone().Validate();
            GUILayout.EndHorizontal();
            // Presets defined in code (dense sky), after the asset presets.
            GUILayout.BeginHorizontal();
            foreach (var built in BuiltInPresets.All())
                if (GUILayout.Button(built.presetName, button, GUILayout.Height(row * 1.3f))) LoadBuiltIn(built);
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            Scenario.seed = Stepper("Seed", Scenario.seed, 1, row, int.MinValue, int.MaxValue);
            if (GUILayout.Button("Randomize (new seed)", button, GUILayout.Height(row))) Scenario = ScenarioRandomizer.Randomize(System.Environment.TickCount & 0xFFFFF, randomizeBounds, Scenario, Difficulty);
            if (GUILayout.Button("Randomize from this seed", button, GUILayout.Height(row))) Scenario = ScenarioRandomizer.Randomize(Scenario.seed, randomizeBounds, Scenario, Difficulty);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Scenario: {Scenario.presetName}", small);
            if (GUILayout.Button("<b>3D editor</b>   fly over the terrain; paths, drone and bird groups, trees, bushes and buildings in 3D   (F6)", button, GUILayout.Height(row))) Open3DEditor();

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
            ScenarioOptionsPanel.Draw(Scenario, "menu.opt", label, small, field, button, row, 130f);
            GUILayout.BeginHorizontal();
            Scenario.swarmSize = Stepper("Swarm size", Scenario.swarmSize, 8, row);
            Scenario.swarmTime = Stepper("Swarm at s", Mathf.RoundToInt(Scenario.swarmTime), 10, row);
            GUILayout.EndHorizontal();
            if (Scenario.swarmSize > 0)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"Swarm tactic: <b>{Scenario.swarmTactic}</b> (click to change)", button, GUILayout.Height(row))) Scenario.swarmTactic = Cycle(Scenario.swarmTactic);
                GUILayout.Label(Scenario.swarmSize > Chaukanna.Scenario.SwarmSky.Threshold ? "A big swarm: simulated as data, thousands are fine. Tactic applies." : $"Tactic applies above {Chaukanna.Scenario.SwarmSky.Threshold} drones.", small);
                GUILayout.EndHorizontal();
            }
            GUILayout.BeginHorizontal();
            Scenario.birdFlocks = Stepper("Bird flocks", Scenario.birdFlocks, 1, row);
            Scenario.flockSize = Stepper("Flock size", Scenario.flockSize, 2, row, 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.blendInPercent = Stepper("Blend-in %", Mathf.RoundToInt(Scenario.blendInPercent), 5, row, -1, 100);
            GUILayout.Label(Scenario.blendInPercent < 0f ? "Hostiles posing as own drones: -1 = set by the difficulty level (none on Recruit, 10 % Trained, 30 % Expert, 50 % Extreme)." : "of the hostiles fly an own-drone patrol to blend in, then break off for the post.", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            // Dense sky: the crowd of cooperative drones and birds, and the IFF / Remote ID aid (I key).
            GUILayout.BeginHorizontal();
            Scenario.crowdCount = Stepper("Crowd tracks", Scenario.crowdCount, 500, row);
            Scenario.crowdRadius = Stepper("Crowd radius m", Mathf.RoundToInt(Scenario.crowdRadius), 500, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("IFF aid: " + (Scenario.iffAid ? "ON" : "OFF"), button, GUILayout.Width(120f), GUILayout.Height(row))) Scenario.iffAid = !Scenario.iffAid;
            Scenario.friendlyNoIffPercent = Stepper("No-IFF %", Mathf.RoundToInt(Scenario.friendlyNoIffPercent), 1, row, 0, 100);
            Scenario.hostileSpoofPercent = Stepper("Spoof %", Mathf.RoundToInt(Scenario.hostileSpoofPercent), 5, row, 0, 100);
            GUILayout.EndHorizontal();
            GUILayout.Label("Crowd tracks: cooperative drones and birds besides the contacts above (0 = none; radius 0 = 1.5 x the ring). Hostiles hide among them; the soldier finds them by aiming (I key = IFF / Remote ID).", small, GUILayout.Height(row));

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

            // Range drill and the true-size display (modules 1 and 6).
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Range drill: " + (Scenario.rangeDrill ? $"ON ({Scenario.drillItems} items)" : "OFF"), button, GUILayout.Height(row))) Scenario.rangeDrill = !Scenario.rangeDrill;
            if (Scenario.rangeDrill && GUILayout.Button("Items", button, GUILayout.Width(70f), GUILayout.Height(row))) Scenario.drillItems = Scenario.drillItems >= 60 ? 12 : Scenario.drillItems + 12;
            var ds = Chaukanna.Player.DisplayCalibration.Settings;
            bool changed = false;
            if (GUILayout.Button("True size: " + (ds.trueSize ? "ON" : "OFF"), button, GUILayout.Height(row))) { ds.trueSize = !ds.trueSize; changed = true; }
            if (ds.trueSize)
            {
                float[] diags = { 14f, 15.6f, 17.3f, 21.5f, 24f, 27f, 32f };
                float[] dists = { 45f, 50f, 55f, 60f, 70f, 80f, 100f };
                if (GUILayout.Button($"{ds.diagonalInches:0.#}\" screen", button, GUILayout.Height(row))) { ds.diagonalInches = Next(diags, ds.diagonalInches); changed = true; }
                if (GUILayout.Button($"{ds.aspectW:F0}:{ds.aspectH:F0}", button, GUILayout.Height(row))) { if (ds.aspectH == 9f) { ds.aspectW = 16f; ds.aspectH = 10f; } else { ds.aspectW = 16f; ds.aspectH = 9f; } changed = true; }
                if (GUILayout.Button($"{ds.distanceCm:F0} cm away", button, GUILayout.Height(row))) { ds.distanceCm = Next(dists, ds.distanceCm); changed = true; }
            }
            GUILayout.EndHorizontal();
            if (changed) Chaukanna.Player.DisplayCalibration.Save();
            GUILayout.Label(Chaukanna.Player.DisplayCalibration.Describe(Camera.main), label, GUILayout.Height(row));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Prevalence: " + (Scenario.prevalence == Prevalence.Custom ? "CUSTOM COUNTS" : Scenario.prevalence.ToString().ToUpper() + $" ({ScenarioConfig.Share(Scenario.prevalence) * 100f:F0} % of {Scenario.prevalenceTotal})"), button, GUILayout.Height(row))) Scenario.prevalence = (Prevalence)(((int)Scenario.prevalence + 1) % 4);
            if (Scenario.prevalence != Prevalence.Custom && !Scenario.quietWatch && GUILayout.Button("Contacts: " + Scenario.prevalenceTotal, button, GUILayout.Height(row))) Scenario.prevalenceTotal = Scenario.prevalenceTotal >= 40 ? 6 : Scenario.prevalenceTotal + 6;
            if (GUILayout.Button("Quiet watch: " + (Scenario.quietWatch ? "ON" : "OFF"), button, GUILayout.Height(row))) Scenario.quietWatch = !Scenario.quietWatch;
            if (Scenario.quietWatch)
            {
                if (GUILayout.Button($"{Scenario.watchMinutes:F0} min", button, GUILayout.Height(row))) Scenario.watchMinutes = Scenario.watchMinutes >= 90f ? 20f : Scenario.watchMinutes >= 45f ? 90f : 45f;
                if (GUILayout.Button($"{Scenario.contactsPerHour:F0} / hour", button, GUILayout.Height(row))) Scenario.contactsPerHour = Scenario.contactsPerHour >= 4f ? 1f : Scenario.contactsPerHour + 1f;
            }
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
            int view = Scenario.EffectiveViewMode;
            if (view != ScenarioConfig.ViewDesktop) GUI.backgroundColor = view == ScenarioConfig.ViewHeadset ? new Color(0.45f, 1f, 0.55f) : new Color(0.3f, 0.75f, 1f);
            // Three-way: desktop > VR-sim (mouse) > headset (OpenXR). With no headset runtime the run falls back to VR-sim.
            if (GUILayout.Button("<b>VIEW: " + ScenarioConfig.ViewModeLabel(view) + "</b>", button, GUILayout.Width(170f), GUILayout.Height(row))) Scenario.SetViewMode((view + 1) % 3);
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
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // A preset defined in code: becomes the working scenario, and selects the difficulty level it wants (Extreme).
        public void LoadBuiltIn(ScenarioConfig built)
        {
            Scenario = built.Clone().Validate();
            int level = BuiltInPresets.DifficultyIndexFor(built, difficulties);
            if (level >= 0) difficultyIndex = level;
        }

        DifficultyProfile Difficulty => difficulties.Count > 0 ? difficulties[Mathf.Clamp(difficultyIndex, 0, difficulties.Count - 1)] : null;

        // Label, a box to type the value into, and - / + buttons.
        int Stepper(string name, int value, int step, float row, int min = 0, int max = int.MaxValue)
        {
            return NumberEntry.Stepper("menu." + name, name, value, step, min, max, label, field, button, row, 110f);
        }

        // Ground height from the preview heightfield (bilinear), for the path editor's altitude profile. Metres east / north of the centre.
        float PreviewGround(Vector2 m)
        {
            var f = PreviewField;
            if (f == null || f.metres == null || f.resolution < 2) return 0f;
            float fx = Mathf.Clamp((m.x / f.sizeMetres + 0.5f) * (f.resolution - 1), 0f, f.resolution - 1.001f);
            float fz = Mathf.Clamp((m.y / f.sizeMetres + 0.5f) * (f.resolution - 1), 0f, f.resolution - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            float a = Mathf.Lerp(f.At(ix, iz), f.At(ix + 1, iz), tx), b = Mathf.Lerp(f.At(ix, iz + 1), f.At(ix + 1, iz + 1), tx);
            return Mathf.Lerp(a, b, tz);
        }

        void MapTab(Rect body)
        {
            if (preview == null || previewStale) GeneratePreview();
            float stripH = PathEditor.StripHeight;
            float size = Mathf.Max(120f, Mathf.Min(body.height - stripH - 8f, body.width - 380f));
            var map = new Rect(body.x, body.y, size, size);
            Fill(map, new Color(0.1f, 0.12f, 0.15f));
            if (preview != null) GUI.DrawTexture(map, preview);

            float area = Terrain.sizeMetres;
            Func<Vector2, Vector2> toMap = m => new Vector2(map.x + (m.x / area + 0.5f) * map.width, map.y + (0.5f - m.y / area) * map.height);

            // The editor: drag waypoints, spawn points and posts; click does what the selected tool says.
            if (Scenario.paths == null) Scenario.paths = new List<FlightPath>();
            pathEd.Bind(label, small, button, field, fontSize);
            pathEd.Paths = Scenario.paths;
            pathEd.SpawnPoints = SpawnPoints;
            pathEd.Players = Players;
            pathEd.Clamp = m => ClampToArea(m, 20f);
            pathEd.Ground = PreviewGround;
            pathEd.PostsChanged = () => { for (int i = 0; i < Players.Count; i++) Players[i].post = ClampToArea(Players[i].post, 100f); previewStale = true; };
            pathEd.SelectedPlayer = selectedPlayer;
            var view = new PathEditor.View
            {
                toScreen = toMap,
                toWorld = sp => new Vector2(((sp.x - map.x) / map.width - 0.5f) * area, (0.5f - (sp.y - map.y) / map.height) * area),
                pxPerMetre = map.width / area,
            };
            pathEd.Input(map, view);
            selectedPlayer = pathEd.SelectedPlayer;
            pathEd.SpawnsChanged = false;

            var e = Event.current;
            if (e.type == EventType.Repaint)
            {
                // Drawn inside a clip group so a post or label near the map edge cannot spill into the panel beside it.
                Func<Vector2, Vector2> toLocal = m => toMap(m) - map.position;
                GUI.BeginGroup(map);
                // Default spawn ring around each post when no points are placed.
                if (SpawnPoints.Count == 0)
                    foreach (var p in Players)
                        for (int i = 0; i < 72; i++)
                        {
                            float a = i * 5f * Mathf.Deg2Rad;
                            Dot(toLocal(p.post + new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * Scenario.spawnRingRadius), 2f, new Color(1f, 0.4f, 0.3f, 0.7f));
                        }
                for (int i = 0; i < Players.Count; i++)
                {
                    Vector2 at = toLocal(Players[i].post);
                    Dot(at, i == selectedPlayer ? 15f : 12f, Color.black);
                    Dot(at, i == selectedPlayer ? 11f : 8f, i == selectedPlayer ? new Color(0.4f, 0.8f, 1f) : Color.white);
                    GUI.Label(new Rect(at.x + 9f, at.y - 10f, 140f, 20f), Players[i].name, small);
                }
                GUI.EndGroup();
            }
            pathEd.Draw(map, view);
            pathEd.Profile(new Rect(map.x, map.yMax + 6f, map.width, stripH));

            GUILayout.BeginArea(new Rect(map.xMax + 20f, body.y, body.width - size - 20f, body.height));
            mapPanelScroll = GUILayout.BeginScrollView(mapPanelScroll);
            const float row = 28f;
            // The 2D map: paths and heights, player posts and drone spawn points (the same data the 3D editor edits).
            pathEd.ToolGrid(row, true, true);

            GUILayout.Space(8f);
            GUILayout.Label("<b>Players</b>   (drag a white square on the map to move it)", label);
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
            GUILayout.Label($"<b>Drone spawn points: {SpawnPoints.Count}</b>   (drag a red square to move it)", label);
            GUILayout.Label(SpawnPoints.Count == 0 ? $"None placed, so drones appear on a {Scenario.spawnRingRadius:F0} m ring in front of the post (the dotted circle)." : "Drones appear at these points, spread about 30 m around each.", small);
            if (GUILayout.Button("Clear spawn points (use the ring)", button, GUILayout.Height(row))) SpawnPoints.Clear();
            GUILayout.Space(8f);
            pathEd.PathPanel(row);
            GUILayout.Space(8f);
            if (Scenario.paths.Count > 0)
                GUILayout.Label("<b>Want more control?</b> Click <b>3D editor</b> (F6) for player posts, spawn points, custom drones and birds, trees and buildings, and full scenario editing.", label);
            if (GUILayout.Button("<b>3D editor</b>   (F6)", button, GUILayout.Height(row))) Open3DEditor();
            GUILayout.Space(8f);
            GUILayout.Label("Coloured lines = flight paths; the arrow shows the direction. Click a waypoint to see and type its height above ground (and speed). The strip under the map shows the selected path's height over the terrain.", small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void LobbyTab(Rect body)
        {
            GUILayout.BeginArea(new Rect(body.x, body.y, Mathf.Min(900f, body.width), body.height));
            lobbyScroll = GUILayout.BeginScrollView(lobbyScroll);
            const float row = 30f;
            var lan = Chaukanna.Net.LanSession.Instance;
            var role = Chaukanna.Net.LanSession.Current;
            GUILayout.Label("<b>LAN lobby</b>  (local network only: no internet, no relay; works air-gapped)", label);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Your name / ID", label, GUILayout.Width(130f), GUILayout.Height(row));
            Chaukanna.Scoring.ScoreKeeper.PlayerName = GUILayout.TextField(Chaukanna.Scoring.ScoreKeeper.PlayerName, 16, field, GUILayout.Width(160f), GUILayout.Height(row));
            GUILayout.Label("Port", label, GUILayout.Width(40f), GUILayout.Height(row));
            lanPort = GUILayout.TextField(lanPort, 5, field, GUILayout.Width(70f), GUILayout.Height(row));
            GUILayout.EndHorizontal();
            ushort.TryParse(lanPort, out ushort port);
            if (port == 0) port = Chaukanna.Net.LanSession.DefaultPort;

            if (role == Chaukanna.Net.LanSession.Role.None)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Host (trainer PC)", button, GUILayout.Width(200f), GUILayout.Height(row))) Chaukanna.Net.LanSession.Ensure().Host(Chaukanna.Scoring.ScoreKeeper.PlayerName, port);
                GUILayout.Label("or join the trainer at IP", label, GUILayout.Width(190f), GUILayout.Height(row));
                lanAddress = GUILayout.TextField(lanAddress, 40, field, GUILayout.Width(150f), GUILayout.Height(row));
                if (GUILayout.Button("Join", button, GUILayout.Width(90f), GUILayout.Height(row))) Chaukanna.Net.LanSession.Ensure().Join(Chaukanna.Scoring.ScoreKeeper.PlayerName, lanAddress, port);
                GUILayout.EndHorizontal();
                // Hosts that answered the UDP broadcast on this network (typing the IP above still works).
                var scan = Chaukanna.Net.LanSession.Ensure();
                scan.RequestScan();
                if (scan.Discovery.Found.Count == 0) GUILayout.Label("Looking for trainer PCs on this network ...  (none found yet: type the IP instead)", small);
                foreach (var h in scan.Discovery.Found)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>{h.name}</b>  {h.address}:{h.port}  {h.mode.ToString().ToUpper()}  {h.state}", label, GUILayout.Height(row));
                    if (GUILayout.Button("Join", button, GUILayout.Width(90f), GUILayout.Height(row))) { lanAddress = h.address; scan.Join(Chaukanna.Scoring.ScoreKeeper.PlayerName, h.address, h.port); }
                    GUILayout.EndHorizontal();
                }
                if (scan.RefusedMessage.Length > 0) GUILayout.Label("<color=#FF8060>Refused by the trainer: " + scan.RefusedMessage + "</color>", label);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(role == Chaukanna.Net.LanSession.Role.Host ? $"<b>HOSTING</b> on {Chaukanna.Net.LanSession.LocalAddresses()} port {port}" : "<b>JOINED</b>", label, GUILayout.Height(row));
                if (GUILayout.Button("Leave", button, GUILayout.Width(100f), GUILayout.Height(row))) lan.Leave();
                GUILayout.EndHorizontal();
                if (role == Chaukanna.Net.LanSession.Role.Host)
                {
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Mode: " + (lan.roster.mode == Chaukanna.Net.NetMode.Solo ? "SOLO (each trainee their own sky, same scenario)" : "TEAM (one shared sky run by this PC)"), button, GUILayout.Height(row)))
                        lan.SetMode(lan.roster.mode == Chaukanna.Net.NetMode.Solo ? Chaukanna.Net.NetMode.Team : Chaukanna.Net.NetMode.Solo);
                    GUILayout.EndHorizontal();
                }
                else
                {
                    GUILayout.Label("Mode: " + lan.roster.mode + ". The trainer picks the scenario and presses START; this PC follows.", small);
                    if (GUILayout.Button(lan.LocalReady ? "READY (click to undo)" : "I'm ready", button, GUILayout.Width(240f), GUILayout.Height(row))) lan.SetReady(!lan.LocalReady);
                }
                GUILayout.Space(6f);
                GUILayout.Label("<b>Units</b>  (sections the report can filter by)", label);
                bool isHost = role == Chaukanna.Net.LanSession.Role.Host;
                string unitToDelete = null, renameFrom = null, renameTo = null;
                foreach (var un in lan.roster.units)
                {
                    int count = 0;
                    foreach (var q in lan.roster.players) if (q.unit == un) count++;
                    GUILayout.BeginHorizontal();
                    if (isHost)
                    {
                        if (!unitEdit.TryGetValue(un, out var buf)) buf = un;
                        string now = GUILayout.TextField(buf, 20, field, GUILayout.Width(170f), GUILayout.Height(row - 4f));
                        unitEdit[un] = now;
                        GUILayout.Label($"{count} seat(s)", small, GUILayout.Width(80f), GUILayout.Height(row - 4f));
                        if (now != un && GUILayout.Button("Rename", button, GUILayout.Width(90f), GUILayout.Height(row - 4f))) { renameFrom = un; renameTo = now; }
                        if (lan.roster.units.Count > 1 && GUILayout.Button("Delete", button, GUILayout.Width(80f), GUILayout.Height(row - 4f))) unitToDelete = un;
                    }
                    else GUILayout.Label($"<b>{un}</b>   {count} seat(s)", label, GUILayout.Height(row - 4f));
                    GUILayout.EndHorizontal();
                }
                if (renameFrom != null) { lan.RenameUnit(renameFrom, renameTo); unitEdit.Remove(renameFrom); }
                if (unitToDelete != null) { lan.DeleteUnit(unitToDelete); unitEdit.Remove(unitToDelete); }
                if (isHost && GUILayout.Button("+ Add unit", button, GUILayout.Width(140f), GUILayout.Height(row - 4f))) lan.AddUnit();

                GUILayout.Space(6f);
                GUILayout.Label(isHost ? "<b>Seats</b>  (tick seats, then assign them below)" : "<b>Seats</b>", label);
                bool team = lan.roster.mode == Chaukanna.Net.NetMode.Team;
                foreach (var p in lan.roster.players)
                {
                    GUILayout.BeginHorizontal();
                    if (isHost)
                    {
                        bool was = lobbySel.Contains(p.clientId);
                        bool now = GUILayout.Toggle(was, "", GUILayout.Width(22f), GUILayout.Height(row - 4f));
                        if (now && !was) lobbySel.Add(p.clientId); else if (!now && was) lobbySel.Remove(p.clientId);
                    }
                    GUILayout.Label($"{p.name}{(p.host ? "  (trainer / host)" : "")}   [{lan.roster.UnitOrDefault(p.unit)}]   {(p.host ? "" : p.ready ? "<color=#60E080>READY</color>" : "<color=#FFB040>not ready</color>")}   {p.state}", label, GUILayout.Height(row - 4f));
                    // Team mode: each seat's role. The trainer clicks to cycle SENTRY / COMMANDER / ANY / OBSERVER.
                    if (team)
                    {
                        string roleName = Chaukanna.Net.NetPlayer.RoleName(p.Role);
                        if (isHost) { if (GUILayout.Button("Role: " + roleName, button, GUILayout.Width(170f), GUILayout.Height(row - 4f))) lan.CycleRole(p.clientId); }
                        else GUILayout.Label("<b>" + roleName + "</b>", label, GUILayout.Width(120f), GUILayout.Height(row - 4f));
                    }
                    GUILayout.EndHorizontal();
                }
                if (isHost)
                {
                    lobbySel.RemoveWhere(id => lan.roster.Find(id) == null);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{lobbySel.Count} selected", small, GUILayout.Width(90f), GUILayout.Height(row - 4f));
                    if (GUILayout.Button("All", button, GUILayout.Width(50f), GUILayout.Height(row - 4f))) foreach (var q in lan.roster.players) lobbySel.Add(q.clientId);
                    if (GUILayout.Button("None", button, GUILayout.Width(60f), GUILayout.Height(row - 4f))) lobbySel.Clear();
                    GUILayout.EndHorizontal();
                    using (new GUIEnabledScope(lobbySel.Count > 0))
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("Unit:", small, GUILayout.Width(40f), GUILayout.Height(row - 4f));
                        foreach (var un in lan.roster.units)
                            if (GUILayout.Button(un, button, GUILayout.Height(row - 4f))) lan.AssignUnit(new List<ulong>(lobbySel), un);
                        GUILayout.EndHorizontal();
                        if (team)
                        {
                            GUILayout.BeginHorizontal();
                            GUILayout.Label("Role:", small, GUILayout.Width(40f), GUILayout.Height(row - 4f));
                            foreach (var r in new[] { Chaukanna.Net.SeatRole.Sentry, Chaukanna.Net.SeatRole.Commander, Chaukanna.Net.SeatRole.Observer, Chaukanna.Net.SeatRole.Any })
                                if (GUILayout.Button(Chaukanna.Net.NetPlayer.RoleName(r), button, GUILayout.Height(row - 4f))) lan.AssignRole(new List<ulong>(lobbySel), r);
                            GUILayout.EndHorizontal();
                        }
                    }
                }
                if (team && !lan.roster.HasCommander()) GUILayout.Label("<color=#FFB040>No commander: nobody can jam or fire.</color>", label);
                else if (team) GUILayout.Label("A SENTRY locks, classifies, reports (R) and checks IFF; the COMMANDER sees the reports (list, mini-map, T key) and decides Hold / Jam / Engage. An OBSERVER only watches and listens (no calls, no actions). ANY does everything.", small);
                if (role == Chaukanna.Net.LanSession.Role.Host) { GUILayout.Space(6f); CommsPanel.Draw(label, small, button, 28f, fontSize); }
                GUILayout.Label("<color=#9FD0FF>Voice is recorded in TEAM runs for the after-action review.</color>  Hold V to talk to people nearby, Y for the radio (the whole team).", small);
            }
            GUILayout.Space(6f);
            if (lan != null) GUILayout.Label(lan.Status, small);
            if (lan != null && lan.AreaProgress.Length > 0) GUILayout.Label("<color=#FFD940>" + lan.AreaProgress + "</color>", label);
            GUILayout.Label("Trainer: press Host, pick SOLO or TEAM, set up the terrain and scenario, then START when every seat is READY. Trainees: type the trainer PC's IP (shown on the host), Join, then press I'm ready. A real-terrain area must be on every PC at the same path; otherwise that PC uses procedural terrain from the same seed. Windows may ask to allow the app through the firewall on the first Host.", small);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        string lanPort = "7777", lanAddress = "192.168.1.10";
        readonly HashSet<ulong> lobbySel = new HashSet<ulong>();
        readonly Dictionary<string, string> unitEdit = new Dictionary<string, string>();
        bool startingFromHost;

        // A trainee's PC: the host pressed START. Adopt its terrain, scenario and seats, then load the field like a solo run.
        void Update()
        {
            // F6: the 3D editor (nothing is running in the menu).
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.f6Key.wasPressedThisFrame && !NumberEntry.AnyFocused && !Downloading) Open3DEditor();
            var lan = Chaukanna.Net.LanSession.Instance;
            if (lan == null || lan.PendingStart == null) return;
            var packet = lan.PendingStart;
            lan.PendingStart = null;
            // A real-terrain run whose area is not on this PC: fetch it from the trainer first (the start resumes after).
            if (!Chaukanna.Net.LanSession.HasArea(packet)) { lan.RequestArea(packet); return; }
            try
            {
                // Asset references (ground materials, masks) cannot cross the network: JSON carries the host's instance
                // ids, which mean nothing here. Keep this PC's own copies of the same assets.
                var kit = Terrain.surfaceKit;
                var mask = Terrain.modificationMask;
                var rules = new List<SurfaceRule>(Terrain.surfaceRules);
                JsonUtility.FromJsonOverwrite(packet.terrainJson, Terrain);
                Terrain.surfaceKit = kit;
                Terrain.modificationMask = mask;
                Terrain.surfaceRules = rules;
                Scenario = JsonUtility.FromJson<ScenarioConfig>(packet.scenarioJson);
                difficultyIndex = packet.difficulty;
                if (Terrain.UsesDem)
                {
                    // The area on this PC: shipped with the build, downloaded here, or just received from the trainer.
                    string local = !string.IsNullOrEmpty(packet.areaName)
                        ? ElevationDownloader.Resolve(ElevationDownloader.ProjectAreasPrefix + packet.areaName + "/" + packet.demFile)
                        : ElevationDownloader.Resolve(Terrain.demPath);
                    if (System.IO.File.Exists(local) && UseArea(local))
                    {
                        // UseArea sets size, centre and blend from the file; the trainer's own settings win.
                        JsonUtility.FromJsonOverwrite(packet.terrainJson, Terrain);
                        Terrain.surfaceKit = kit;
                        Terrain.modificationMask = mask;
                        Terrain.surfaceRules = rules;
                        Terrain.demPath = local;
                        demPath = local;
                        demLoadedFrom = local;
                    }
                    else
                    {
                        SetTerrainMode(TerrainMode.PureProcedural);
                        Message = "The trainer's real-terrain area could not be loaded on this PC: using procedural terrain from the same seed.";
                    }
                }
                Players.Clear();
                ulong me = Unity.Netcode.NetworkManager.Singleton != null ? Unity.Netcode.NetworkManager.Singleton.LocalClientId : 0;
                foreach (var p in packet.players) Players.Add(new PlayerSlot { name = p.name, post = p.post, ready = true, isLocal = p.clientId == me });
                SpawnPoints.Clear();
                SpawnPoints.AddRange(packet.spawnPoints);
                lan.EnterField();
                startingFromHost = true;
                bool ok = StartSession();
                startingFromHost = false;
                if (!ok) Debug.LogWarning("[LAN] Could not start the host's run here: " + Message);
            }
            catch (Exception e) { startingFromHost = false; Message = "Could not start the host's run: " + e.Message; Debug.LogException(e); }
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
