using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Trainer
{
    // Trainer's setup and live view. F6 toggles split-screen: trainer on the left, player view on the right.
    // Everything here may show truth; nothing from here is drawn in the player's part of the window.
    public class TrainerScreen : MonoBehaviour
    {
        [Tooltip("Scripted scenarios offered as buttons. The spawner's own preset is added as the last entry.")]
        public List<TrainerPreset> presets = new List<TrainerPreset>();
        public RandomizeBounds randomizeBounds = new RandomizeBounds();

        [Header("Difficulty")]
        [Tooltip("Easiest first. Applied on top of the chosen preset or randomized scenario when START is pressed.")]
        public List<DifficultyProfile> difficulties = new List<DifficultyProfile>();
        [Tooltip("Index into the list above that runs start on.")]
        public int difficultyIndex = 1;
        [Tooltip("A finished run scoring above this percent recommends one level harder.")]
        public float harderAbovePercent = 80f;
        [Tooltip("A finished run scoring below this percent recommends one level easier.")]
        public float easierBelowPercent = 50f;
        [Tooltip("Start with the trainer screen showing.")]
        public bool splitOnStart;
        [Tooltip("Share of the window width the trainer takes in split-screen.")]
        [Range(0.3f, 0.6f)] public float trainerFraction = 0.42f;
        [Tooltip("Text size at 720p; scales with window height.")]
        public int fontSize = 15;
        public int feedLines = 6;

        [Header("Truth map colours")]
        public Color hostileColour = new Color(1f, 0.25f, 0.2f);
        public Color ownColour = new Color(0.3f, 0.55f, 1f);
        public Color civilianColour = new Color(1f, 0.9f, 0.2f);
        public Color birdColour = new Color(0.7f, 0.7f, 0.7f);

        // The configuration being edited; START applies it.
        public ScenarioConfig Working { get; private set; }

        ScenarioSpawner spawner;
        ScoreDashboard dashboard;
        Chaukanna.Scoring.ScoreKeeper scoreKeeper;
        Camera playerCamera;
        GUIStyle label, button, title, small, field;
        bool wasTyping;
        bool applied;   // a trainer config has been started at least once
        float controlsBottom = 340f;   // measured each repaint so the map starts right under the controls

        void Start()
        {
            spawner = FindAnyObjectByType<ScenarioSpawner>();
            playerCamera = Camera.main;
            scoreKeeper = FindAnyObjectByType<Chaukanna.Scoring.ScoreKeeper>();
            dashboard = GetComponent<ScoreDashboard>();
            if (dashboard == null) dashboard = gameObject.AddComponent<ScoreDashboard>();
            Working = spawner != null && spawner.preset != null ? ScenarioConfig.FromPreset(spawner.preset) : new ScenarioConfig();
            SetSplit(splitOnStart);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f6Key.wasPressedThisFrame) SetSplit(!PlayerView.Split);
        }

        public void SetSplit(bool on)
        {
            PlayerView.TrainerFraction = trainerFraction;
            PlayerView.Split = on;
            if (playerCamera != null) playerCamera.rect = PlayerView.Viewport;
            if (on) Cursor.lockState = CursorLockMode.None;   // free the mouse for the trainer's buttons
        }

        void OnDisable()
        {
            PlayerView.Split = false;
            if (playerCamera != null) playerCamera.rect = new Rect(0f, 0f, 1f, 1f);
        }

        // --- Actions (also callable from tests) ---

        public int PresetCount => presets.Count + 1;

        public string PresetName(int index)
        {
            if (index < presets.Count) return presets[index] != null ? presets[index].config.presetName : "(missing)";
            return spawner != null && spawner.preset != null ? spawner.preset.name : "Preset";
        }

        public void LoadPreset(int index)
        {
            if (index < presets.Count)
            {
                if (presets[index] != null) Working = presets[index].config.Clone().Validate();
            }
            else if (spawner != null && spawner.preset != null) Working = ScenarioConfig.FromPreset(spawner.preset).Validate();
        }

        public void RandomizeWithSeed(int seed)
        {
            Working = ScenarioRandomizer.Randomize(seed, randomizeBounds, Working, CurrentDifficulty);
        }

        public DifficultyProfile CurrentDifficulty => difficulties.Count > 0 ? difficulties[Mathf.Clamp(difficultyIndex, 0, difficulties.Count - 1)] : null;

        public void SetDifficulty(int index)
        {
            if (difficulties.Count > 0) difficultyIndex = Mathf.Clamp(index, 0, difficulties.Count - 1);
        }

        // Level index recommended after a finished run, or -1 when there is nothing to recommend yet.
        public int RecommendedLevel(Chaukanna.Scoring.SessionResult finished)
        {
            if (finished == null || difficulties.Count == 0) return -1;
            int played = difficulties.FindIndex(d => d != null && d.displayName == finished.difficulty);
            if (played < 0) played = Mathf.Clamp(difficultyIndex, 0, difficulties.Count - 1);
            return DifficultyAdvisor.Recommend(played, difficulties.Count, finished.ScorePercent, harderAbovePercent, easierBelowPercent);
        }

        public string DifficultyName(int index)
        {
            return index >= 0 && index < difficulties.Count && difficulties[index] != null ? difficulties[index].displayName : "-";
        }

        // Used when the run comes from the trainer menu: adopt its scenario and level, then start.
        public void StartSessionRun(ScenarioConfig scenario, int difficulty)
        {
            Working = scenario.Clone().Validate();
            SetDifficulty(difficulty);
            StartRun();
        }

        public void StartRun()
        {
            if (spawner == null) return;
            var difficulty = CurrentDifficulty;
            spawner.StartRun(difficulty != null ? difficulty.Apply(Working) : Working);   // Working itself stays unscaled
            applied = true;
        }

        // --- UI ---

        void OnGUI()
        {
            if (spawner == null) return;
            if (!PlayerView.Split)
            {
                // Nothing runs until the trainer starts it. Say how, in the player's view.
                if (spawner.Waiting && Event.current.type == EventType.Repaint)
                {
                    var hint = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(22f * Screen.height / 720f), fontStyle = FontStyle.Bold };
                    hint.normal.textColor = Color.white;
                    var box = new Rect(Screen.width * 0.5f - 330f * Screen.height / 720f, Screen.height * 0.42f, 660f * Screen.height / 720f, 70f * Screen.height / 720f);
                    Fill(box, new Color(0f, 0f, 0f, 0.7f));
                    GUI.Label(box, "Waiting for the trainer.\nPress F6 for the trainer panel, then START.", hint);
                }
                return;
            }
            if (dashboard != null && dashboard.Expanded) return;   // the dashboard is covering the whole window
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { wordWrap = false, alignment = TextAnchor.MiddleLeft, richText = true };
                button = new GUIStyle(GUI.skin.button);
                title = new GUIStyle(label) { fontStyle = FontStyle.Bold };
                small = new GUIStyle(label) { clipping = TextClipping.Clip };
            }
            label.fontSize = button.fontSize = fontSize;
            title.fontSize = fontSize + 5;
            small.fontSize = fontSize - 2;
            label.normal.textColor = title.normal.textColor = small.normal.textColor = Color.white;

            // Lay out in 720p units and scale to the window, like the player HUD.
            float ui = Screen.height / 720f;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(ui, ui, 1f));
            float w = Screen.width * PlayerView.TrainerFraction / ui, h = 720f;
            // With the dashboard's Charts tab open, paint only the header so the charts canvas underneath is not covered.
            bool chartsOpen = dashboard != null && dashboard.IsOpen && dashboard.ChartsActive;
            Fill(new Rect(0f, 0f, w, chartsOpen ? ScoreDashboard.TrainerHeaderHeight : h), new Color(0.05f, 0.06f, 0.08f, 1f));

            const float pad = 10f, row = 24f;
            GUILayout.BeginArea(new Rect(pad, pad, w - pad * 2f, h - pad * 2f));

            GUILayout.BeginHorizontal();
            GUILayout.Label("TRAINER  (F6 to hide)", title);
            if (!string.IsNullOrEmpty(Chaukanna.Session.SessionSetup.menuScenePath) && GUILayout.Button("Back to menu", button, GUILayout.Width(120f), GUILayout.Height(row)))
            {
                SetSplit(false);
                Chaukanna.Session.SessionSetup.Load(Chaukanna.Session.SessionSetup.menuScenePath);
            }
            GUILayout.EndHorizontal();

            // While the score dashboard is open it takes over this half; keep just the button that closes it.
            if (dashboard != null && dashboard.IsOpen)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("SCORE (open) - click to close", new GUIStyle(button) { fontSize = fontSize + 2 }, GUILayout.Width(260f), GUILayout.Height(row * 1.5f)))
                    dashboard.Close();
                GUILayout.Label("  " + Status(), label, GUILayout.Height(row * 1.5f));
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                GUI.matrix = saved;
                return;
            }

            // Presets, two per row.
            for (int i = 0; i < PresetCount; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 2 && j < PresetCount; j++)
                    if (GUILayout.Button(PresetName(j), button, GUILayout.Height(row))) LoadPreset(j);
                GUILayout.EndHorizontal();
            }

            // Seed and randomize.
            GUILayout.BeginHorizontal();
            Working.seed = Stepper("Seed", Working.seed, 1, row, int.MinValue, int.MaxValue);
            if (GUILayout.Button("Randomize (new seed)", button, GUILayout.Height(row))) RandomizeWithSeed(System.Environment.TickCount & 0xFFFFF);
            if (GUILayout.Button("From this seed", button, GUILayout.Height(row))) RandomizeWithSeed(Working.seed);
            GUILayout.EndHorizontal();
            GUILayout.Label($"Scenario: {Working.presetName}", label);

            // Counts and geometry, two steppers per row.
            GUILayout.BeginHorizontal();
            Working.hostileCount = Stepper("Hostile", Working.hostileCount, 1, row);
            Working.friendlyCount = Stepper("Own ISR", Working.friendlyCount, 1, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Working.civilianCount = Stepper("Civilian", Working.civilianCount, 1, row);
            Working.birdCount = Stepper("Birds", Working.birdCount, 1, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Working.duration = Stepper("Duration s", Mathf.RoundToInt(Working.duration), 30, row);
            Working.spawnRingRadius = Stepper("Ring m", Mathf.RoundToInt(Working.spawnRingRadius), 100, row);
            GUILayout.EndHorizontal();

            // Sensors and ROE.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Site: " + Working.site.ToString().ToUpper(), button, GUILayout.Height(row))) Working.site = (SiteType)(((int)Working.site + 1) % 3);
            if (GUILayout.Button("Sensors: " + (Working.sensorsDegraded ? "DEGRADED" : "OK"), button, GUILayout.Height(row))) Working.sensorsDegraded = !Working.sensorsDegraded;
            if (GUILayout.Button("ROE at start: " + RoeCards.Short(Working.roeAtStart), button, GUILayout.Height(row))) Working.roeAtStart = RoeCards.Next(Working.roeAtStart);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            string flipMode = !Working.roeFlip ? "OFF" : Working.roeFlipRandom ? "RANDOM" : "SET TIME";
            if (GUILayout.Button("Card change: " + flipMode, button, GUILayout.Height(row)))
            {
                if (!Working.roeFlip) { Working.roeFlip = true; Working.roeFlipRandom = false; }
                else if (!Working.roeFlipRandom) Working.roeFlipRandom = true;
                else { Working.roeFlip = false; Working.roeFlipRandom = false; }
            }
            if (Working.roeFlip && GUILayout.Button("to " + RoeCards.Short(Working.roeFlipTo), button, GUILayout.Height(row))) Working.roeFlipTo = RoeCards.Next(Working.roeFlipTo);
            if (Working.roeFlip && !Working.roeFlipRandom) Working.roeFlipTime = Stepper("At s", Mathf.RoundToInt(Working.roeFlipTime), 10, row);
            GUILayout.EndHorizontal();

            // Conditions and difficulty: each button steps to the next value.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Time: " + Working.timeOfDay.ToString().ToUpper(), button, GUILayout.Height(row))) Working.timeOfDay = (TimeOfDay)(((int)Working.timeOfDay + 1) % 3);
            if (GUILayout.Button("Visibility: " + Working.visibility.ToString().ToUpper(), button, GUILayout.Height(row))) Working.visibility = (Visibility)(((int)Working.visibility + 1) % 3);
            if (difficulties.Count > 0 && GUILayout.Button("Difficulty: " + DifficultyName(difficultyIndex).ToUpper(), button, GUILayout.Height(row))) SetDifficulty((difficultyIndex + 1) % difficulties.Count);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Soldier: " + (Working.allowMovement ? "CAN MOVE" : "FIXED AT POST"), button, GUILayout.Height(row))) Working.allowMovement = !Working.allowMovement;
            if (GUILayout.Button("VR: " + (Working.vrMode ? "ON" : "OFF"), button, GUILayout.Width(70f), GUILayout.Height(row)))
            {
                // One click: switches the player's view now, and keeps it for the next START.
                Working.vrMode = !Working.vrMode;
                var view = playerCamera != null ? playerCamera.GetComponent<Chaukanna.Player.ViewRigSwitch>() : null;
                if (view != null) view.Set(Working.vrMode);
            }
            Working.moveRadius = Stepper("Radius m", Mathf.RoundToInt(Working.moveRadius), 10, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Working.swarmSize = Stepper("Swarm", Working.swarmSize, 8, row);
            Working.swarmTime = Stepper("Swarm at s", Mathf.RoundToInt(Working.swarmTime), 10, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Working.birdFlocks = Stepper("Flocks", Working.birdFlocks, 1, row);
            Working.flockSize = Stepper("Flock size", Working.flockSize, 2, row, 1);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Working.blendInPercent = Stepper("Blend-in %", Mathf.RoundToInt(Working.blendInPercent), 5, row, -1, 100);
            GUILayout.Label(Working.blendInPercent < 0f ? "hostiles posing as own drones: set by difficulty" : "of hostiles pose as own drones", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();

            // After a run: a suggestion only. The trainer decides.
            int recommended = scoreKeeper != null && spawner.Finished ? RecommendedLevel(scoreKeeper.Last) : -1;
            if (recommended >= 0)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Last run {scoreKeeper.Last.ScorePercent:F0} %.  Recommended next: <b>{DifficultyName(recommended)}</b>", label, GUILayout.Height(row));
                if (recommended != difficultyIndex && GUILayout.Button("Apply", button, GUILayout.Width(80f), GUILayout.Height(row))) SetDifficulty(recommended);
                GUILayout.EndHorizontal();
            }
            Working.Validate();
            string warning = Working.Warning();
            if (warning.Length > 0) GUILayout.Label("<color=#FFB040>" + warning + "</color>", label);

            // Start and status.
            GUILayout.BeginHorizontal();
            var oldColour = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.9f, 0.4f);
            if (GUILayout.Button("<b>START</b>", new GUIStyle(button) { richText = true, fontSize = fontSize + 5 }, GUILayout.Width(150f), GUILayout.Height(row * 1.5f))) StartRun();
            GUI.backgroundColor = oldColour;
            // The only way into the score dashboard. It opens in this half; the player's view is untouched unless the trainer expands it.
            if (dashboard != null && GUILayout.Button("SCORE", new GUIStyle(button) { fontSize = fontSize + 2 }, GUILayout.Width(120f), GUILayout.Height(row * 1.5f)))
                dashboard.Open();
            GUILayout.Label("  " + Status(), label, GUILayout.Height(row * 1.5f));
            GUILayout.EndHorizontal();
            if (Event.current.type == EventType.Repaint) controlsBottom = GUILayoutUtility.GetLastRect().yMax;
            GUILayout.EndArea();

            // While a number box has focus the player's hotkeys are ignored, so typed digits don't classify contacts.
            bool typing = NumberEntry.AnyFocused;
            if (typing != wasTyping)
            {
                wasTyping = typing;
                ContactRegistry.InputBlocked = typing || (dashboard != null && dashboard.Expanded);
            }

            // Truth map and event feed fill what is left.
            float feedH = feedLines * (small.fontSize + 5f) + 8f;
            float top = pad + controlsBottom + 10f;
            float mapSize = Mathf.Max(80f, Mathf.Min(w - pad * 2f - 150f, h - top - feedH - pad * 2f));
            var mapRect = new Rect(pad, top, mapSize, mapSize);
            MapLassoInput(mapRect);
            DrawMap(mapRect);
            DrawLegend(pad + mapSize + 10f, top);
            DrawMapSelection(pad + mapSize + 10f, top + 8f * 22f + 14f, row);
            DrawFeed(new Rect(pad, h - feedH - pad, w - pad * 2f, feedH));

            GUI.matrix = saved;
        }

        // Label, a box to type the value into, and - / + buttons.
        int Stepper(string name, int value, int step, float row, int min = 0, int max = int.MaxValue)
        {
            if (field == null) field = new GUIStyle(GUI.skin.textField);
            field.fontSize = fontSize;
            return NumberEntry.Stepper("field." + name, name, value, step, min, max, label, field, button, row, 84f);
        }

        string Status()
        {
            string source = applied ? "" : "  (Inspector values; press START to apply)";
            if (spawner.Waiting) return "WAITING: press START to begin";
            int t = (int)spawner.ScenarioTime, d = (int)spawner.Duration;
            string level = spawner.TrainerConfig != null && spawner.TrainerConfig.difficulty.Length > 0 ? "   " + spawner.TrainerConfig.difficulty : "";
            return $"RUNNING {t / 60:00}:{t % 60:00} / {d / 60:00}:{d % 60:00}   seed {spawner.ActiveSeed}{level}{source}";
        }

        Color Colour(ContactClass c)
        {
            switch (c)
            {
                case ContactClass.HostileQuad:
                case ContactClass.HostileFixedWing: return hostileColour;
                case ContactClass.FriendlyISR: return ownColour;
                case ContactClass.CivilianQuad: return civilianColour;
                default: return birdColour;
            }
        }

        // Top-down truth map. Up = the centre of the sector; the post is in the middle.
        void DrawMap(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            Fill(r, new Color(0.1f, 0.13f, 0.16f, 1f));
            GUI.BeginGroup(r);

            var frame = Frame(r);
            float radius = frame.radius, scale = frame.scale, sectorYaw = frame.yaw;
            Vector2 centre = frame.centre;
            Vector3 origin = frame.origin;
            var line = new Color(1f, 1f, 1f, 0.35f);

            // Site objects (walls, buildings, trees) as small grey marks; the trainer's lasso picks them too.
            SiteObjects(siteScratch);
            foreach (var t in siteScratch) Dot(ToMap(frame, t.position), 2f, new Color(0.75f, 0.72f, 0.65f, 0.45f));
            foreach (var t in mapSite) if (t != null) Dot(ToMap(frame, t.position), 5f, new Color(1f, 0.85f, 0.25f, 0.9f));

            // Spawn ring and sector edges.
            for (int i = 0; i < 90; i++) Dot(centre + Polar(i * 4f, radius * scale), 1.5f, line);
            float half = spawner.ActiveArc * 0.5f;
            Dotted(centre, -half, radius * scale, line);
            Dotted(centre, half, radius * scale, line);

            // Player's view cone: where the camera points and how wide it sees.
            if (playerCamera != null)
            {
                float yaw = playerCamera.transform.eulerAngles.y - sectorYaw;
                float hFov = Camera.VerticalToHorizontalFieldOfView(playerCamera.fieldOfView, playerCamera.aspect) * 0.5f;
                var cone = new Color(0.4f, 1f, 0.5f, 0.8f);
                // Drawn from where the soldier is now, which is not the post once movement is allowed.
                Vector3 offset = Quaternion.Euler(0f, -sectorYaw, 0f) * (playerCamera.transform.position - origin);
                Vector2 soldier = centre + new Vector2(offset.x, -offset.z) * scale;
                Dotted(soldier, yaw - hFov, radius * scale, cone);
                Dotted(soldier, yaw + hFov, radius * scale, cone);
                Dot(soldier, 6f, cone);
            }
            Dot(centre, 7f, Color.white);   // the post

            var live = ContactRegistry.Live;
            for (int i = 0; i < live.Count; i++)
            {
                Vector3 d = Quaternion.Euler(0f, -sectorYaw, 0f) * (live[i].transform.position - origin);
                Vector2 p = centre + new Vector2(d.x, -d.z) * scale;
                if (live[i].Data.firstLockTime >= 0f && !live[i].Resolved) Dot(p, 10f, new Color(1f, 1f, 1f, 0.5f));   // has been locked
                // A blend-in hostile: magenta frame while it is still acting friendly, solid once it has broken off.
                if (live[i].Deceptive) Dot(p, live[i].AnomalyShown ? 13f : 11f, new Color(1f, 0.2f, 0.9f, live[i].AnomalyShown ? 1f : 0.7f));
                if (mapContacts.Contains(live[i])) Dot(p, 12f, new Color(1f, 0.85f, 0.25f, 0.95f));   // in the trainer's lasso selection
                Dot(p, 6f, Colour(live[i].TruthClass));
            }

            if (mapDrawing && mapLoop.Count >= 3)
            {
                var area = new Rect(0f, 0f, r.width, r.height);
                if (mapFillDirty || mapFill == null) { Chaukanna.Player.LassoMath.Fill(ref mapFill, ref mapFillPixels, mapLoop, area, 3); mapFillDirty = false; }
                var old = GUI.color;
                GUI.color = new Color(1f, 0.85f, 0.25f, 0.15f);
                GUI.DrawTexture(area, mapFill);
                GUI.color = old;
            }
            if (mapDrawing) Chaukanna.Player.LassoMath.DrawLoop(mapLoop, new Color(1f, 0.85f, 0.25f), 1f);
            GUI.EndGroup();
        }

        // Trainer lasso on the truth map: drag a loop round contacts and site objects to select them together (Shift adds,
        // Ctrl removes, a click picks one). This is the truth view, so there is no visibility filter here.
        // Prototype: select, count and delete. Bulk move, change class and set path are listed in TODO.
        public readonly List<Contact> mapContacts = new List<Contact>();
        public readonly List<Transform> mapSite = new List<Transform>();
        readonly List<Transform> siteScratch = new List<Transform>();
        readonly List<Vector2> mapLoop = new List<Vector2>();   // map-local GUI pixels
        bool mapDrawing, mapFillDirty;
        Texture2D mapFill;
        Color32[] mapFillPixels;

        public struct MapFrame { public Vector2 centre; public float scale, radius, yaw; public Vector3 origin; }

        public MapFrame Frame(Rect r)
        {
            float radius = Mathf.Max(1f, spawner.ActiveRadius);
            return new MapFrame
            {
                radius = radius,
                scale = r.width * 0.5f / (radius * 1.08f),
                centre = new Vector2(r.width * 0.5f, r.height * 0.5f),
                origin = spawner.transform.position,
                yaw = spawner.transform.eulerAngles.y,
            };
        }

        public static Vector2 ToMap(MapFrame f, Vector3 world)
        {
            Vector3 d = Quaternion.Euler(0f, -f.yaw, 0f) * (world - f.origin);
            return f.centre + new Vector2(d.x, -d.z) * f.scale;
        }

        // The selectable parts of the generated site: everything under its Cantonment and Fields groups (not the watchtower).
        public void SiteObjects(List<Transform> into)
        {
            into.Clear();
            var root = spawner != null ? spawner.SiteRoot : null;
            if (root == null) return;
            foreach (Transform group in root.transform)
                if (group.name == "Cantonment" || group.name == "Fields")
                    foreach (Transform t in group) into.Add(t);
        }

        void MapLassoInput(Rect r)
        {
            var e = Event.current;
            Vector2 local = e.mousePosition - r.position;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0 || !r.Contains(e.mousePosition)) return;
                    mapDrawing = true;
                    mapLoop.Clear();
                    mapLoop.Add(local);
                    mapFillDirty = true;
                    e.Use();
                    break;
                case EventType.MouseDrag:
                    if (!mapDrawing) return;
                    if (Chaukanna.Player.LassoMath.AddPoint(mapLoop, local, 3f)) mapFillDirty = true;
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (!mapDrawing) return;
                    mapDrawing = false;
                    MapLassoFinish(r, mapLoop, Chaukanna.Player.LassoMath.ModeFrom(e.shift, e.control));
                    mapLoop.Clear();
                    e.Use();
                    break;
            }
        }

        // Applies a loop (map-local pixels) to the trainer's selection. Public for the self-test.
        public void MapLassoFinish(Rect r, List<Vector2> loop, Chaukanna.Player.LassoMode mode)
        {
            var frame = Frame(r);
            var contacts = new List<Contact>();
            var site = new List<Transform>();
            SiteObjects(siteScratch);
            if (loop.Count >= 3 && Chaukanna.Player.LassoMath.Area(loop) >= 36f)
            {
                foreach (var c in ContactRegistry.Live) if (c != null && Chaukanna.Player.LassoMath.Inside(ToMap(frame, c.transform.position), loop)) contacts.Add(c);
                foreach (var t in siteScratch) if (Chaukanna.Player.LassoMath.Inside(ToMap(frame, t.position), loop)) site.Add(t);
            }
            else if (loop.Count > 0)
            {
                // A click: the nearest contact, or failing that the nearest site object, within 8 px.
                Vector2 at = loop[loop.Count - 1];
                Contact bestC = null; Transform bestT = null;
                float best = 8f;
                foreach (var c in ContactRegistry.Live) { float d = c != null ? Vector2.Distance(ToMap(frame, c.transform.position), at) : 1e9f; if (d <= best) { best = d; bestC = c; } }
                if (bestC == null) { best = 8f; foreach (var t in siteScratch) { float d = Vector2.Distance(ToMap(frame, t.position), at); if (d <= best) { best = d; bestT = t; } } }
                if (bestC != null) contacts.Add(bestC);
                if (bestT != null) site.Add(bestT);
            }
            Chaukanna.Player.LassoMath.Apply(mapContacts, contacts, mode);
            Chaukanna.Player.LassoMath.Apply(mapSite, site, mode);
        }

        // Count readout and the bulk actions, beside the legend.
        void DrawMapSelection(float x, float y, float row)
        {
            mapContacts.RemoveAll(c => c == null);
            mapSite.RemoveAll(t => t == null);
            GUI.Label(new Rect(x, y, 150f, 22f), "MAP LASSO", small);
            GUI.Label(new Rect(x, y + 20f, 150f, 22f), "drag on the map", small);
            GUI.Label(new Rect(x, y + 40f, 150f, 22f), "Shift add  Ctrl remove", small);
            GUI.Label(new Rect(x, y + 64f, 150f, 22f), $"{mapContacts.Count} contacts", small);
            GUI.Label(new Rect(x, y + 84f, 150f, 22f), $"{mapSite.Count} site objects", small);
            if (mapContacts.Count + mapSite.Count == 0) return;
            if (GUI.Button(new Rect(x, y + 110f, 140f, row), "DELETE", button)) DeleteMapSelection();
            if (GUI.Button(new Rect(x, y + 114f + row, 140f, row), "CLEAR", button)) { mapContacts.Clear(); mapSite.Clear(); }
        }

        public void DeleteMapSelection()
        {
            int contacts = 0, site = 0;
            foreach (var c in mapContacts) if (c != null) { c.Despawn(Contact.RemovedByTrainer); contacts++; }
            foreach (var t in mapSite) if (t != null) { Destroy(t.gameObject); site++; }
            mapContacts.Clear();
            mapSite.Clear();
            ContactRegistry.Note($"Trainer removed {contacts} contacts and {site} site objects");
        }

        void DrawLegend(float x, float y)
        {
            if (Event.current.type != EventType.Repaint) return;
            string[] names = { "Hostile", "Own ISR", "Civilian", "Bird", "Blend-in hostile", "Post", "View cone" };
            Color[] colours = { hostileColour, ownColour, civilianColour, birdColour, new Color(1f, 0.2f, 0.9f), Color.white, new Color(0.4f, 1f, 0.5f) };
            for (int i = 0; i < names.Length; i++)
            {
                Fill(new Rect(x, y + i * 22f + 6f, 10f, 10f), colours[i]);
                GUI.Label(new Rect(x + 16f, y + i * 22f, 130f, 22f), names[i], small);
            }
            GUI.Label(new Rect(x, y + names.Length * 22f + 4f, 140f, 22f), $"Live {ContactRegistry.Live.Count}", small);
        }

        void DrawFeed(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            Fill(r, new Color(0f, 0f, 0f, 0.5f));
            var feed = ContactRegistry.Feed;
            float lineH = small.fontSize + 5f;
            int first = Mathf.Max(0, feed.Count - feedLines);
            for (int i = first; i < feed.Count; i++)   // newest at the bottom
                GUI.Label(new Rect(r.x + 6f, r.y + 4f + (i - first) * lineH, r.width - 12f, lineH), feed[i], small);
        }

        // Bearing 0 = up, clockwise.
        static Vector2 Polar(float bearingDeg, float length)
        {
            float a = bearingDeg * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * length;
        }

        static void Dotted(Vector2 from, float bearingDeg, float length, Color c)
        {
            for (float d = 6f; d <= length; d += 6f) Dot(from + Polar(bearingDeg, d), 1.5f, c);
        }

        static void Dot(Vector2 p, float size, Color c)
        {
            Fill(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), c);
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
