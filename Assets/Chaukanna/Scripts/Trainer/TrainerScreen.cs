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
        GUIStyle label, button, title, small;
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
            if (!PlayerView.Split || spawner == null) return;
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
            Fill(new Rect(0f, 0f, w, h), new Color(0.05f, 0.06f, 0.08f, 1f));

            const float pad = 10f, row = 24f;
            GUILayout.BeginArea(new Rect(pad, pad, w - pad * 2f, h - pad * 2f));

            GUILayout.Label("TRAINER  (F6 to hide)", title);

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
            GUILayout.Label($"Seed  <b>{Working.seed}</b>", label, GUILayout.Width(130f));
            if (GUILayout.Button("-", button, GUILayout.Width(30f), GUILayout.Height(row))) Working.seed--;
            if (GUILayout.Button("+", button, GUILayout.Width(30f), GUILayout.Height(row))) Working.seed++;
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
            if (GUILayout.Button("Sensors: " + (Working.sensorsDegraded ? "DEGRADED" : "OK"), button, GUILayout.Height(row))) Working.sensorsDegraded = !Working.sensorsDegraded;
            if (GUILayout.Button("ROE at start: " + (Working.roeAtStart == RoeCard.UnderAttack ? "UNDER ATTACK" : "WEAPONS HOLD"), button, GUILayout.Height(row)))
                Working.roeAtStart = Working.roeAtStart == RoeCard.UnderAttack ? RoeCard.WeaponsHold : RoeCard.UnderAttack;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ROE flip: " + (Working.roeFlip ? "ON, to UNDER ATTACK" : "OFF"), button, GUILayout.Height(row)))
            {
                Working.roeFlip = !Working.roeFlip;
                Working.roeFlipTo = RoeCard.UnderAttack;
            }
            Working.roeFlipTime = Stepper("Flip at s", Mathf.RoundToInt(Working.roeFlipTime), 10, row);
            GUILayout.EndHorizontal();

            // Conditions and difficulty: each button steps to the next value.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Time: " + Working.timeOfDay.ToString().ToUpper(), button, GUILayout.Height(row))) Working.timeOfDay = (TimeOfDay)(((int)Working.timeOfDay + 1) % 3);
            if (GUILayout.Button("Visibility: " + Working.visibility.ToString().ToUpper(), button, GUILayout.Height(row))) Working.visibility = (Visibility)(((int)Working.visibility + 1) % 3);
            if (difficulties.Count > 0 && GUILayout.Button("Difficulty: " + DifficultyName(difficultyIndex).ToUpper(), button, GUILayout.Height(row))) SetDifficulty((difficultyIndex + 1) % difficulties.Count);
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

            // Truth map and event feed fill what is left.
            float feedH = feedLines * (small.fontSize + 5f) + 8f;
            float top = pad + controlsBottom + 10f;
            float mapSize = Mathf.Max(80f, Mathf.Min(w - pad * 2f - 150f, h - top - feedH - pad * 2f));
            DrawMap(new Rect(pad, top, mapSize, mapSize));
            DrawLegend(pad + mapSize + 10f, top);
            DrawFeed(new Rect(pad, h - feedH - pad, w - pad * 2f, feedH));

            GUI.matrix = saved;
        }

        int Stepper(string name, int value, int step, float row)
        {
            GUILayout.Label($"{name}  <b>{value}</b>", label, GUILayout.Width(120f), GUILayout.Height(row));
            if (GUILayout.Button("-", button, GUILayout.Width(30f), GUILayout.Height(row))) value -= step;
            if (GUILayout.Button("+", button, GUILayout.Width(30f), GUILayout.Height(row))) value += step;
            GUILayout.Space(12f);
            return value;
        }

        string Status()
        {
            string source = applied ? "" : "  (Inspector values; press START to apply)";
            if (spawner.Finished) return "COMPLETE" + source;
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

            float radius = Mathf.Max(1f, spawner.ActiveRadius);
            float scale = r.width * 0.5f / (radius * 1.08f);
            Vector2 centre = new Vector2(r.width * 0.5f, r.height * 0.5f);
            Vector3 origin = spawner.transform.position;
            float sectorYaw = spawner.transform.eulerAngles.y;
            var line = new Color(1f, 1f, 1f, 0.35f);

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
                Dotted(centre, yaw - hFov, radius * scale, cone);
                Dotted(centre, yaw + hFov, radius * scale, cone);
            }
            Dot(centre, 7f, Color.white);   // the post

            var live = ContactRegistry.Live;
            for (int i = 0; i < live.Count; i++)
            {
                Vector3 d = Quaternion.Euler(0f, -sectorYaw, 0f) * (live[i].transform.position - origin);
                Vector2 p = centre + new Vector2(d.x, -d.z) * scale;
                if (live[i].Data.firstLockTime >= 0f && !live[i].Resolved) Dot(p, 10f, new Color(1f, 1f, 1f, 0.5f));   // has been locked
                Dot(p, 6f, Colour(live[i].TruthClass));
            }
            GUI.EndGroup();
        }

        void DrawLegend(float x, float y)
        {
            if (Event.current.type != EventType.Repaint) return;
            string[] names = { "Hostile", "Own ISR", "Civilian", "Bird", "Post", "View cone" };
            Color[] colours = { hostileColour, ownColour, civilianColour, birdColour, Color.white, new Color(0.4f, 1f, 0.5f) };
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
