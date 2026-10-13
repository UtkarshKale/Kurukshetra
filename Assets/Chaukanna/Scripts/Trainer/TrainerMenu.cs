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
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            SessionSetup.Clear();       // back in the menu: the previous session is over
        }

        void OnDestroy()
        {
            if (preview != null) Destroy(preview);
            if (Terrain != null) Destroy(Terrain);
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
            SessionSetup.scenario = Scenario.Clone().Validate();
            SessionSetup.difficultyIndex = difficultyIndex;
            foreach (var p in Players) SessionSetup.players.Add(new PlayerSlot { name = p.name, post = p.post, ready = p.ready, isLocal = p.isLocal });
            SessionSetup.spawnPoints.AddRange(SpawnPoints);
            SessionSetup.menuScenePath = menuScenePath;
            SessionSetup.Ready = true;
            SessionSetup.Load(fieldScenePath);
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
            int area = Stepper("Area m", Mathf.RoundToInt(Terrain.sizeMetres), 500, row, 2000, 20000);
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

            float size = Mathf.Min(body.height, body.width - leftW - 20f);
            var map = new Rect(body.x + leftW + 20f, body.y, size, size);
            Fill(map, new Color(0.1f, 0.12f, 0.15f));
            if (preview != null) GUI.DrawTexture(map, preview);
            GUI.Label(new Rect(map.x + 8f, map.y + 4f, map.width - 16f, 40f), preview == null ? "No preview yet. Press Generate preview." : previewStale ? "<color=#FFB040>Settings changed since this preview.</color>" : "North is up.", small);
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

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Time: " + Scenario.timeOfDay.ToString().ToUpper(), button, GUILayout.Height(row))) Scenario.timeOfDay = (TimeOfDay)(((int)Scenario.timeOfDay + 1) % 3);
            if (GUILayout.Button("Visibility: " + Scenario.visibility.ToString().ToUpper(), button, GUILayout.Height(row))) Scenario.visibility = (Visibility)(((int)Scenario.visibility + 1) % 3);
            if (difficulties.Count > 0 && GUILayout.Button("Difficulty: " + (Difficulty != null ? Difficulty.displayName.ToUpper() : "-"), button, GUILayout.Height(row))) difficultyIndex = (difficultyIndex + 1) % difficulties.Count;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sensors: " + (Scenario.sensorsDegraded ? "DEGRADED" : "OK"), button, GUILayout.Height(row))) Scenario.sensorsDegraded = !Scenario.sensorsDegraded;
            if (GUILayout.Button("ROE at start: " + (Scenario.roeAtStart == RoeCard.UnderAttack ? "UNDER ATTACK" : "WEAPONS HOLD"), button, GUILayout.Height(row)))
                Scenario.roeAtStart = Scenario.roeAtStart == RoeCard.UnderAttack ? RoeCard.WeaponsHold : RoeCard.UnderAttack;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ROE flip: " + (Scenario.roeFlip ? "ON, to UNDER ATTACK" : "OFF"), button, GUILayout.Height(row))) { Scenario.roeFlip = !Scenario.roeFlip; Scenario.roeFlipTo = RoeCard.UnderAttack; }
            Scenario.roeFlipTime = Stepper("Flip at s", Mathf.RoundToInt(Scenario.roeFlipTime), 10, row);
            GUILayout.EndHorizontal();
            // Soldier: fixed at the post, or free to walk within a radius.
            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Soldier movement: " + (Scenario.allowMovement ? "ALLOWED (W A S D, Shift to run)" : "FIXED AT POST"), button, GUILayout.Width(330f), GUILayout.Height(row))) Scenario.allowMovement = !Scenario.allowMovement;
            GUI.enabled = Scenario.allowMovement;
            Scenario.moveRadius = Stepper("Radius m", Mathf.RoundToInt(Scenario.moveRadius), 10, row, 0, 2000);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Scenario.walkSpeed = Stepper("Walk m/s", Mathf.RoundToInt(Scenario.walkSpeed), 1, row, 1, 15);
            Scenario.runSpeed = Stepper("Run m/s", Mathf.RoundToInt(Scenario.runSpeed), 1, row, 1, 20);
            GUILayout.EndHorizontal();
            GUI.enabled = true;

            Scenario.Validate();
            GUILayout.Space(8f);
            GUILayout.Label("Type a number into any box and press Enter (or click elsewhere), or use - / +. Difficulty is applied on top of these numbers when the run starts. Drone spawn points and player posts are set on the Map tab.", small);
            GUILayout.EndArea();
        }

        DifficultyProfile Difficulty => difficulties.Count > 0 ? difficulties[Mathf.Clamp(difficultyIndex, 0, difficulties.Count - 1)] : null;

        // Label, a box to type the value into, and - / + buttons.
        int Stepper(string name, int value, int step, float row, int min = 0, int max = 1000000)
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
            if (GUILayout.Button("Add player", button, GUILayout.Height(row)) && Players.Count < 8)
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
