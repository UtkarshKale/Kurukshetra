using System;
using System.Collections.Generic;
using Chaukanna.Replay;
using Chaukanna.Scenario;
using Chaukanna.Scoring;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Trainer
{
    // The presenter's checklist for the one-click DEMO: what to do and which key, in order, with the steps that can be seen from the
    // game (sound on, the card change, run complete, dashboard open, 3D replay open, report written) ticking themselves. Everything
    // else is ticked by hand. F8 or the Hide button puts it away; a small tab remains. Not drawn over the replay or the 3D editor.
    public class DemoChecklist : MonoBehaviour
    {
        public static DemoChecklist Instance { get; private set; }

        public static void Show()
        {
            if (Instance != null) return;
            new GameObject("DemoChecklist").AddComponent<DemoChecklist>();
        }

        class Item
        {
            public string text, keys;
            public Func<DemoChecklist, bool> auto;
            public bool ticked;
        }

        static string Clock(float s) => $"{(int)(s / 60f)}:{(int)(s % 60f):00}";

        readonly List<object> rows = new List<object>();       // strings are headings, Items are steps
        readonly List<Item> items = new List<Item>();
        ScenarioSpawner spawner;
        ScoreKeeper keeper;
        ScoreDashboard dashboard;
        bool hidden, left;
        GUIStyle head, text, keys, small, button;
        Vector2 scroll;

        void Awake()
        {
            Instance = this;
            Heading("BEFORE THE RUN");
            Add("Sound on, speakers up", "", d => Chaukanna.Audio.SoundEngine.Enabled);
            Add("Voice: mic allowed, push to talk", "F10", null);
            Heading("LIVE (run is " + Clock(DemoScenario.Duration) + ")");
            Add("Run started", "", d => d.spawner != null && d.spawner.ScenarioTime > 1f);
            Add("Pick a drone: look, lock it, binoculars", "Right mouse = binoculars, click = lock", null);
            Add("Call it, then act on it", "1-5 call, H hold, R report, J jam, F engage", null);
            Add("Fast reaction on everything selected", "E engage, Q jam", null);
            Add("Own ISR and civilian: call them, do not shoot", "3 own, 4 civilian", null);
            Add("Bird flock about 0:35: group call", "Tab + drag (lasso) or G, then K, 2", null);
            Add("Swarm of 200 from 0:55: lasso it, call it", "Tab + drag, K, 1", null);
            Add("ROE card changes C to A at " + Clock(DemoScenario.RoeFlipAt) + ": point at the banner", "banner flashes, tone", d => ContactRegistry.RoeChanges.Count > 0);
            Add("Trainer view: truth map, feed", "F6", null);
            Heading("AFTER THE RUN");
            Add("RUN COMPLETE", "", d => d.spawner != null && d.spawner.Finished);
            Add("AAR dashboard: cards, charts, player / unit select", "AAR dashboard button on the end screen (or F6 > SCORE)", d => d.dashboard != null && d.dashboard.IsOpen);
            Add("3D replay: follow, chase, free, top views", "3D REPLAY button; Space, 1-4, click a contact", d => Replay3D.Active);
            Add("HTML report (opens in the browser)", "HTML report button on the dashboard header", null);
        }

        void Heading(string t) { rows.Add(t); }
        void Add(string t, string k, Func<DemoChecklist, bool> auto)
        {
            var it = new Item { text = t, keys = k, auto = auto };
            items.Add(it); rows.Add(it);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (keeper == null) keeper = FindAnyObjectByType<ScoreKeeper>();
            if (dashboard == null) dashboard = FindAnyObjectByType<ScoreDashboard>();
            var kb = Keyboard.current;
            if (kb != null && kb.f8Key.wasPressedThisFrame) hidden = !hidden;
            foreach (var it in items) if (!it.ticked && it.auto != null && it.auto(this)) it.ticked = true;     // sticky: once seen, stays ticked
        }

        void OnGUI()
        {
            if (ScenarioEditor3D.Active || Replay3D.Active) return;
            if (head == null)
            {
                head = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = false };
                text = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
                keys = new GUIStyle(GUI.skin.label) { wordWrap = true, fontStyle = FontStyle.Italic };
                small = new GUIStyle(GUI.skin.label) { wordWrap = true };
                button = new GUIStyle(GUI.skin.button);
            }
            GUI.depth = -10;
            float ui = Screen.height / 720f;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(ui, ui, 1f));
            float sw = Screen.width / ui, sh = 720f;
            head.fontSize = 14; text.fontSize = 13; keys.fontSize = 11; small.fontSize = 11; button.fontSize = 12;
            head.normal.textColor = new Color(0.55f, 0.85f, 1f);
            text.normal.textColor = Color.white;
            keys.normal.textColor = new Color(0.75f, 0.78f, 0.82f);
            small.normal.textColor = new Color(0.75f, 0.78f, 0.82f);

            if (hidden)
            {
                var tab = new Rect(left ? 0f : sw - 112f, sh - 30f, 112f, 24f);
                if (GUI.Button(tab, "Checklist (F8)", button)) hidden = false;
                GUI.matrix = saved;
                return;
            }

            const float w = 330f;
            float h = Mathf.Min(sh - 70f, 40f + items.Count * 24f + 3 * 26f + 30f);
            var r = new Rect(left ? 8f : sw - w - 8f, 52f, w, h);
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color; GUI.color = new Color(0.04f, 0.05f, 0.07f, 0.88f);
                GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = old;
            }
            GUILayout.BeginArea(new Rect(r.x + 8f, r.y + 6f, r.width - 16f, r.height - 12f));
            GUILayout.BeginHorizontal();
            GUILayout.Label("PRESENTER CHECKLIST", head);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(left ? "Right" : "Left", button, GUILayout.Width(48f))) left = !left;
            if (GUILayout.Button("Hide", button, GUILayout.Width(44f))) hidden = true;
            GUILayout.EndHorizontal();
            scroll = GUILayout.BeginScrollView(scroll);
            foreach (var row in rows)
            {
                if (row is string heading) { GUILayout.Space(4f); GUILayout.Label(heading, head); continue; }
                var it = (Item)row;
                GUILayout.BeginHorizontal();
                bool now = GUILayout.Toggle(it.ticked, "", GUILayout.Width(18f));
                if (now != it.ticked) it.ticked = now;
                GUILayout.BeginVertical();
                GUILayout.Label(it.ticked ? "<color=#8fdc9a>" + it.text + "</color>" : it.text, text);
                if (it.keys.Length > 0) GUILayout.Label(it.keys, keys);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("F8 hides this. Steps with a tick that you did not click ticked themselves.", small);
            GUILayout.EndArea();
            GUI.matrix = saved;
        }
    }
}
