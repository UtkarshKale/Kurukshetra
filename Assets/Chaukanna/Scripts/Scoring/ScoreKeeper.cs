using System;
using System.IO;
using Chaukanna.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Scoring
{
    // Scores the run, prints [Score] lines and saves the session as JSON when it ends.
    // The player never sees a score: it is shown only on the trainer's score dashboard (SCORE button on the trainer panel).
    public class ScoreKeeper : MonoBehaviour
    {
        [Tooltip("Leave empty to use the built-in defaults.")]
        public ScoringRules rules;
        [Tooltip("Write each session to <persistentDataPath>/sessions/ for the dashboard's trend charts.")]
        public bool saveSessionJson = true;
        [Tooltip("End-of-run line text size at 720p; scales with screen height.")]
        public int titleFontSize = 28;

        public SessionResult Last { get; private set; }   // null until the run has ended
        public string LastSavedPath { get; private set; }
        public int SavedCount { get; private set; }       // goes up each time a session file is written
        public static string SessionsFolder => Path.Combine(Application.persistentDataPath, "sessions");

        ScenarioSpawner spawner;
        int run = -1;
        GUIStyle titleStyle;
        SessionResult live;
        float nextLiveTime;
        RuleSet defaults;

        RuleSet Rules => rules != null ? rules.rules : (defaults ?? (defaults = RuleSet.Defaults()));

        // The finished result, or the score so far while a run is in progress (refreshed at most twice a second).
        public SessionResult Current()
        {
            if (Last != null) return Last;
            if (spawner == null) return null;
            if (live == null || Time.unscaledTime >= nextLiveTime)
            {
                nextLiveTime = Time.unscaledTime + 0.5f;
                live = ScoreCalculator.Calculate(ContactRegistry.Records, Rules, spawner.ScenarioTime, spawner.ActiveSeed);
                StampConditions(live);
            }
            return live;
        }

        void Update()
        {
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner == null) return;
            if (spawner.RunNumber != run)
            {
                run = spawner.RunNumber;
                Last = null;
                live = null;
            }
            if (!spawner.Finished) return;

            // Wait a frame for the end-of-run despawns to reach the records.
            if (Last == null && ContactRegistry.Live.Count == 0) Finish();

            var kb = Keyboard.current;
            if (Last != null && kb != null && kb.rKey.wasPressedThisFrame && !ContactRegistry.InputBlocked) spawner.Restart();
        }

        void Finish()
        {
            Last = ScoreCalculator.Calculate(ContactRegistry.Records, Rules, spawner.ScenarioTime, spawner.ActiveSeed);
            Last.timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            StampConditions(Last);
            Debug.Log($"[Score] Conditions: {Last.timeOfDay}, {Last.visibility}, night vision {Last.nightVisionPercent:F0}% of the run. Difficulty: {(Last.difficulty.Length > 0 ? Last.difficulty : "none")}");
            foreach (var line in ScoreCalculator.Describe(Last).Split('\n'))
                if (line.Trim().Length > 0) Debug.Log(line.TrimEnd());

            if (!saveSessionJson) return;
            try
            {
                Directory.CreateDirectory(SessionsFolder);
                LastSavedPath = Path.Combine(SessionsFolder, $"session_{DateTime.Now:yyyyMMdd_HHmmss}_seed{Last.seed}.json");
                File.WriteAllText(LastSavedPath, JsonUtility.ToJson(Last, true));
                SavedCount++;
                Debug.Log("[Score] Session saved to " + LastSavedPath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Score] Could not save session: " + e.Message);
            }
        }

        void StampConditions(SessionResult s)
        {
            s.timeOfDay = RunConditions.Time.ToString();
            s.visibility = RunConditions.Visibility.ToString();
            s.difficulty = RunConditions.Difficulty ?? "";
            s.allowMovement = RunConditions.AllowMovement;
            s.nightVisionPercent = spawner.ScenarioTime > 0f ? Mathf.Clamp(100f * RunConditions.NightVisionSeconds / spawner.ScenarioTime, 0f, 100f) : 0f;
        }

        [ContextMenu("Run Scoring Self-Test")]
        void RunSelfTest()
        {
            ScoreSelfTest.Run();
        }

        // Player view at the end of a run: neutral, no score.
        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint || spawner == null || !spawner.Finished || PlayerView.HideOverlays) return;
            GUI.BeginGroup(PlayerView.Rect);
            float ui = PlayerView.Height / 720f;
            if (titleStyle == null) titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false, fontStyle = FontStyle.Bold };
            titleStyle.fontSize = Mathf.RoundToInt(titleFontSize * ui);
            titleStyle.normal.textColor = Color.white;

            var content = new GUIContent("RUN COMPLETE    Press R to restart");
            Vector2 size = titleStyle.CalcSize(content);
            float pad = 16f * ui;
            var box = new Rect((PlayerView.Width - size.x) * 0.5f - pad, (PlayerView.Height - size.y) * 0.5f - pad, size.x + pad * 2f, size.y + pad * 2f);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(box.x + pad, box.y + pad, size.x, size.y), content, titleStyle);
            GUI.EndGroup();
        }
    }
}
