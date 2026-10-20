using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kurukshetra.Scenario
{
    // One answered item.
    [Serializable]
    public class DrillItem
    {
        public string cls, call;
        public int tier, rangeBin;
        public float range, seconds;    // seconds from appearing to the call; -1 = no answer in time
        public bool correct;
    }

    // One drill, saved to <persistentDataPath>/range/ and fed to the learner model.
    [Serializable]
    public class DrillResult
    {
        public string timestamp, player = "Player 1", light = "Day";
        public int seed, items, correct, tierReached = 1, freezesUsed;
        public bool fluent;                 // the fluency criterion was met at the top tier
        public List<DrillItem> answers = new List<DrillItem>();
        public List<string> classes = new List<string>();
        public List<float> meanSeconds = new List<float>();     // per class, right answers only; -1 = none
        public List<float> ghostSeconds = new List<float>();    // the same from the previous drill; -1 = none
    }

    // The Range drill (doc §1.5 Beat 1, module 1): short presentations in the same 3D world. One contact at a time appears
    // in front of the soldier, flying a short straight pass; they call it with 1-5 and get the answer at once.
    // - Classes are interleaved (never three of a kind in a row) and instances never repeat (range, bearing, height and
    //   model all come from the seed); the learner model's focus class turns up more often.
    // - Tiers: 1 = near, 8 s to answer, 3 freezes (Space); 2 = middle distance, 5 s, 1 freeze; 3 = far, 3 s, no freeze.
    //   Fluency (10 of the last 12 right within 3 s) moves the soldier up a tier; fluency at tier 3 ends the drill early.
    // - Each class's mean answer time is shown against the previous drill's ("ghost").
    // Real video clips (about 20 % of items in the doc) need open-licence footage and are not in yet.
    public class RangeDrill : MonoBehaviour
    {
        public static DrillResult Last { get; private set; }
        public static bool Active { get; private set; }

        public static readonly ContactClass[] Classes = { ContactClass.HostileQuad, ContactClass.HostileFixedWing, ContactClass.FriendlyISR, ContactClass.CivilianQuad, ContactClass.Bird };
        static readonly float[] TierSeconds = { 8f, 5f, 3f };
        static readonly int[] TierFreezes = { 3, 1, 0 };
        static readonly Vector2[] TierRange = { new Vector2(150f, 350f), new Vector2(300f, 650f), new Vector2(550f, 1000f) };
        public const int FluencyWindow = 12, FluencyNeeded = 10;
        public const float FluencySeconds = 3f;

        ScenarioSpawner spawner;
        System.Random rng;
        DrillResult result;
        int itemsWanted, tier = 1, freezesLeft, nextId = 900, windowStart;
        Contact item;
        ContactClass itemClass;
        float itemShown, itemRange, frozenFor, gapUntil, feedbackUntil;
        bool frozen;
        string feedback = "";
        bool feedbackRight;
        readonly List<ContactClass> recent = new List<ContactClass>();
        string focusClass = "";
        GUIStyle style;

        public static string Folder => Path.Combine(Application.persistentDataPath, "range");

        public void Begin(ScenarioSpawner owner, ScenarioConfig cfg)
        {
            spawner = owner;
            rng = new System.Random((cfg != null ? cfg.seed : 1) ^ 0x2545F491);
            itemsWanted = Mathf.Max(1, cfg != null ? cfg.drillItems : 30);
            tier = 1;
            windowStart = 0;
            freezesLeft = TierFreezes[0];
            recent.Clear();
            result = new DrillResult { timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), seed = cfg != null ? cfg.seed : 0, light = RunConditions.Time.ToString(), player = Kurukshetra.Scoring.ScoreKeeper.PlayerName };
            var ghost = LoadPrevious();
            foreach (var c in Classes)
            {
                result.classes.Add(c.ToString());
                result.meanSeconds.Add(-1f);
                result.ghostSeconds.Add(ghost != null ? GhostFor(ghost, c.ToString()) : -1f);
            }
            var learner = Kurukshetra.Scoring.LearnerModel.Load(Kurukshetra.Scoring.ScoreKeeper.PlayerName);
            focusClass = learner.observations.Count > 0 ? Kurukshetra.Scoring.LearnerModel.Plan(learner).focusClass : "";
            item = null;
            gapUntil = Time.time + 1.5f;
            Active = true;
            enabled = true;
        }

        public void Stop()
        {
            if (item != null) item.Despawn("DrillItem");
            item = null;
            Active = false;
            enabled = false;
        }

        void OnDisable() { Active = false; }

        // --- Pure choices, so they can be tested.
        public static ContactClass PickClass(System.Random rng, IList<ContactClass> recent, string focus)
        {
            for (int tries = 0; tries < 20; tries++)
            {
                ContactClass c = !string.IsNullOrEmpty(focus) && rng.NextDouble() < 0.35 && Enum.TryParse(focus, out ContactClass f) ? f : Classes[rng.Next(Classes.Length)];
                int n = recent.Count;
                if (n >= 2 && recent[n - 1] == c && recent[n - 2] == c) continue;   // never three in a row
                return c;
            }
            return Classes[rng.Next(Classes.Length)];
        }

        // Fluency: at least 10 of the last 12 right, each within 3 s.
        // Only answers from `from` on count, so moving up a tier starts a fresh window.
        public static bool Fluent(IList<DrillItem> answers, int from = 0)
        {
            if (answers.Count - from < FluencyWindow) return false;
            int good = 0;
            for (int i = answers.Count - FluencyWindow; i < answers.Count; i++)
                if (answers[i].correct && answers[i].seconds >= 0f && answers[i].seconds <= FluencySeconds) good++;
            return good >= FluencyNeeded;
        }

        public static float RangeFor(System.Random rng, int tier) => Mathf.Lerp(TierRange[tier - 1].x, TierRange[tier - 1].y, (float)rng.NextDouble());

        void Update()
        {
            if (spawner == null || result == null) return;
            if (item == null && !ReferenceEquals(item, null)) { item = null; }   // left the view before an answer

            if (item == null)
            {
                if (Time.time < gapUntil) return;
                if (result.items >= itemsWanted) { Finish(); return; }
                ShowNext();
                return;
            }

            var kb = Keyboard.current;
            if (kb == null || ContactRegistry.InputBlocked) return;
            // Freeze: the item holds still and the clock stops (tier 1: 3 per drill, tier 2: 1, tier 3: none).
            if (kb.spaceKey.wasPressedThisFrame && !frozen && freezesLeft > 0) { frozen = true; freezesLeft--; result.freezesUsed++; SetFrozen(true); }
            else if (kb.spaceKey.wasPressedThisFrame && frozen) { frozen = false; SetFrozen(false); }
            if (frozen) { frozenFor += Time.deltaTime; return; }

            PlayerCall call = PlayerCall.None;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) call = PlayerCall.HostileQuad;
            else if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) call = PlayerCall.HostileFixedWing;
            else if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) call = PlayerCall.OwnIsrDrone;
            else if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) call = PlayerCall.CivilianDrone;
            else if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) call = PlayerCall.Bird;
            float seconds = Time.time - itemShown - frozenFor;
            if (call != PlayerCall.None) Answer(call, seconds);
            else if (seconds > TierSeconds[tier - 1]) Answer(PlayerCall.None, -1f);
        }

        void ShowNext()
        {
            var cam = Camera.main;
            if (cam == null) return;
            itemClass = PickClass(rng, recent, focusClass);
            recent.Add(itemClass);
            itemRange = RangeFor(rng, tier);
            // In front of the soldier: inside the middle of the view, 25-110 m up, crossing slowly so it stays in view.
            // Inside the middle 60 % of the view, whatever the field of view (the true-size view is narrow).
            float hFov = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect);
            float yaw = cam.transform.eulerAngles.y + ((float)rng.NextDouble() - 0.5f) * hFov * 0.6f;
            float pitch = -Mathf.DeltaAngle(0f, cam.transform.eulerAngles.x) + ((float)rng.NextDouble() - 0.5f) * cam.fieldOfView * 0.6f;
            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 eye = cam.transform.position;
            float height = Mathf.Max(15f, itemRange * Mathf.Tan(pitch * Mathf.Deg2Rad));
            Vector3 start = eye + dir * itemRange;
            start.y = eye.y + height;
            Vector3 across = Vector3.Cross(Vector3.up, dir) * (rng.NextDouble() < 0.5 ? -1f : 1f);
            float speed = itemClass == ContactClass.HostileFixedWing ? 22f : itemClass == ContactClass.Bird ? 9f : 12f;
            item = spawner.SpawnSingle(itemClass, start, start + across * 2000f, speed, rng.Next(), nextId++);
            if (item == null) { gapUntil = Time.time + 0.5f; return; }
            item.GetComponent<ContactMotion>()?.ForceTransit(start + across * 2000f, speed);
            itemShown = Time.time;
            frozenFor = 0f;
            frozen = false;
        }

        void SetFrozen(bool on)
        {
            var m = item != null ? item.GetComponent<ContactMotion>() : null;
            if (m != null) m.Frozen = on;
        }

        void Answer(PlayerCall call, float seconds)
        {
            bool right = call != PlayerCall.None && call == Kurukshetra.Scoring.ScoreCalculator.RightCall(itemClass);
            var a = new DrillItem { cls = itemClass.ToString(), call = call.Label(), tier = tier, range = itemRange, rangeBin = Kurukshetra.Scoring.LearnerModel.RangeBin(itemRange), seconds = seconds, correct = right };
            result.answers.Add(a);
            result.items++;
            if (right) result.correct++;
            if (item != null) { if (call != PlayerCall.None) item.Classify(call, itemRange); item.Despawn("DrillItem"); }
            item = null;
            feedbackRight = right;
            string truth = Kurukshetra.Scoring.ScoreCalculator.RightCall(itemClass).Label();
            feedback = right ? $"RIGHT: {truth}  ({seconds:F1} s)" : call == PlayerCall.None ? $"TOO SLOW: it was a {truth}" : $"WRONG: it was a {truth}, you said {call.Label()}";
            feedbackUntil = Time.time + 1.6f;
            gapUntil = Time.time + 1.8f;

            if (Fluent(result.answers, windowStart))
            {
                if (tier < 3)
                {
                    tier++;
                    freezesLeft = TierFreezes[tier - 1];
                    result.tierReached = tier;
                    feedback += $"   FLUENT: up to tier {tier}";
                    windowStart = result.answers.Count;
                }
                else { result.fluent = true; itemsWanted = result.items; }
            }
        }

        void Finish()
        {
            for (int i = 0; i < Classes.Length; i++)
            {
                float sum = 0f; int n = 0;
                foreach (var a in result.answers) if (a.cls == Classes[i].ToString() && a.correct) { sum += a.seconds; n++; }
                result.meanSeconds[i] = n > 0 ? sum / n : -1f;
            }
            Last = result;
            Save(result);
            FeedLearner(result);
            Debug.Log($"[Range] Drill done: {result.correct}/{result.items} right, tier {result.tierReached}{(result.fluent ? ", fluent" : "")}, {result.freezesUsed} freezes.");
            Active = false;
            enabled = false;
            spawner.EndNow();
        }

        // The drill's answers are recognition events for the learner model (task "classify").
        public static void FeedLearner(DrillResult r)
        {
            try
            {
                var p = Kurukshetra.Scoring.LearnerModel.Load(r.player);
                var obs = new List<Kurukshetra.Scoring.LearnerObservation>();
                foreach (var a in r.answers)
                    if (!string.IsNullOrEmpty(a.cls))
                        obs.Add(new Kurukshetra.Scoring.LearnerObservation { task = "classify", cls = a.cls, light = r.light, rangeBin = a.rangeBin, correct = a.correct, session = "range " + r.timestamp });
                Kurukshetra.Scoring.LearnerModel.Add(p, obs);
                Kurukshetra.Scoring.LearnerModel.Fit(p, Kurukshetra.Scoring.LearnerModel.Pooled(Kurukshetra.Scoring.LearnerModel.LoadOthers(r.player)));
                Kurukshetra.Scoring.LearnerModel.Save(p);
            }
            catch (Exception e) { Debug.LogWarning("[Range] Learner update failed: " + e.Message); }
        }

        static void Save(DrillResult r)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(Path.Combine(Folder, $"range_{DateTime.Now:yyyyMMdd_HHmmss}.json"), JsonUtility.ToJson(r, true));
            }
            catch (Exception e) { Debug.LogWarning("[Range] Could not save the drill: " + e.Message); }
        }

        static DrillResult LoadPrevious()
        {
            try
            {
                if (!Directory.Exists(Folder)) return null;
                var files = Directory.GetFiles(Folder, "range_*.json");
                if (files.Length == 0) return null;
                Array.Sort(files);
                return JsonUtility.FromJson<DrillResult>(File.ReadAllText(files[files.Length - 1]));
            }
            catch { return null; }
        }

        public static float GhostFor(DrillResult previous, string cls)
        {
            int i = previous.classes.IndexOf(cls);
            return i >= 0 && i < previous.meanSeconds.Count ? previous.meanSeconds[i] : -1f;
        }

        void OnGUI()
        {
            if (!Active || result == null || PlayerView.HideOverlays || Event.current.type != EventType.Repaint) return;
            GUI.BeginGroup(PlayerView.Rect);
            float ui = PlayerView.Height / 720f, w = PlayerView.Width;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, fontStyle = FontStyle.Bold };
            style.fontSize = Mathf.RoundToInt(17f * ui);
            style.normal.textColor = Color.white;

            int good = 0;
            for (int i = Mathf.Max(windowStart, result.answers.Count - FluencyWindow); i < result.answers.Count; i++)
                if (result.answers[i].correct && result.answers[i].seconds <= FluencySeconds) good++;
            float left = item != null ? Mathf.Max(0f, TierSeconds[tier - 1] - (Time.time - itemShown - frozenFor)) : 0f;
            string head = $"RANGE DRILL   tier {tier}   item {Mathf.Min(result.items + 1, itemsWanted)} / {itemsWanted}   right {result.correct}   fluency {good}/{FluencyWindow} (need {FluencyNeeded} within {FluencySeconds:F0} s)"
                + (TierFreezes[tier - 1] > 0 ? $"   freezes {freezesLeft} (Space)" : "") + (item != null ? $"   {left:F1} s" : "");
            var box = new Rect(w * 0.5f - 470f * ui, 10f * ui, 940f * ui, 30f * ui);
            Fill(box, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(box, head, style);
            if (frozen) GUI.Label(new Rect(w * 0.5f - 200f * ui, 44f * ui, 400f * ui, 26f * ui), "FROZEN: Space to go on", style);
            if (Time.time < feedbackUntil)
            {
                var fb = new Rect(w * 0.5f - 360f * ui, PlayerView.Height * 0.62f, 720f * ui, 40f * ui);
                Fill(fb, feedbackRight ? new Color(0.1f, 0.45f, 0.2f, 0.85f) : new Color(0.55f, 0.12f, 0.1f, 0.85f));
                GUI.Label(fb, feedback, style);
            }
            // Ghost: this drill's mean answer time per class against the previous drill's.
            var small = new GUIStyle(style) { fontSize = Mathf.RoundToInt(13f * ui), fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft };
            float y = 48f * ui;
            for (int i = 0; i < Classes.Length; i++)
            {
                float sum = 0f; int n = 0;
                foreach (var a in result.answers) if (a.cls == Classes[i].ToString() && a.correct) { sum += a.seconds; n++; }
                string now = n > 0 ? (sum / n).ToString("F1") + " s" : "-";
                float g = result.ghostSeconds[i];
                GUI.Label(new Rect(12f * ui, y, 360f * ui, 20f * ui), $"{Kurukshetra.Scoring.LearnerModel.Pretty(Classes[i].ToString())}: {now}" + (g >= 0f ? $"  (last drill {g:F1} s)" : ""), small);
                y += 20f * ui;
            }
            GUI.EndGroup();
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
