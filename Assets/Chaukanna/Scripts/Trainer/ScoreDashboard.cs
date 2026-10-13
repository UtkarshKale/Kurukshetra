using System.Collections.Generic;
using Chaukanna.Scenario;
using Chaukanna.Scoring;
using UnityEngine;
using UnityEngine.InputSystem;
using C = Chaukanna.Trainer.DashboardCharts;

namespace Chaukanna.Trainer
{
    // The trainer's score dashboard. Opened only from the SCORE button on the trainer panel; the player never sees it
    // unless the trainer expands it over the whole window. Reads everything through IPlayerSource / IUnitStatsSource.
    public class ScoreDashboard : MonoBehaviour
    {
        [Tooltip("Text size at 720p; scales with window height.")]
        public int fontSize = 15;
        [Tooltip("Most contacts drawn on the timeline and in the per-contact list.")]
        public int maxContactRows = 60;

        public IPlayerSource Players;
        public IUnitStatsSource Unit;

        public bool IsOpen { get; private set; }
        public bool Expanded { get; private set; }

        // Height (at 720p) the trainer panel keeps for its own header while the dashboard is open in its half.
        public const float TrainerHeaderHeight = 96f;

        int selected;
        float scroll, pageHeight, viewHeight;
        ScenarioSpawner spawner;
        TrainerScreen trainer;
        SessionResult totalsFor;
        int totalAttempts, totalHolds;
        List<TrendPoint> trend = new List<TrendPoint>();
        int trendRealCount = -1;

        void Start()
        {
            spawner = FindAnyObjectByType<ScenarioSpawner>();
            trainer = GetComponent<TrainerScreen>();
            if (Players == null) Players = new LocalPlayerSource(FindAnyObjectByType<ScoreKeeper>(), spawner);
            if (Unit == null) Unit = new SampleUnitStatsSource();
        }

        public void Open()
        {
            IsOpen = true;
            scroll = 0f;
        }

        public void Close()
        {
            SetExpanded(false);
            IsOpen = false;
        }

        // Expanded = fills the whole window and hides the player view, so player input is ignored while it is up.
        public void SetExpanded(bool on)
        {
            Expanded = on;
            ContactRegistry.InputBlocked = on;
            if (on) Cursor.lockState = CursorLockMode.None;
        }

        void OnDisable()
        {
            if (Expanded) ContactRegistry.InputBlocked = false;
        }

        void Update()
        {
            if (!IsOpen) return;
            if (!PlayerView.Split && !Expanded) { Close(); return; }   // the trainer panel was hidden
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.pageDownKey.wasPressedThisFrame) scroll += viewHeight * 0.8f;
            if (kb.pageUpKey.wasPressedThisFrame) scroll -= viewHeight * 0.8f;
        }

        void OnGUI()
        {
            if (!IsOpen || Players == null) return;
            GUI.depth = -20;   // above the trainer panel and every player overlay
            C.EnsureStyles(fontSize);

            // Lay out in 720p units and scale to the window.
            float ui = Screen.height / 720f;
            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(ui, ui, 1f));
            Rect region = Expanded
                ? new Rect(0f, 0f, Screen.width / ui, 720f)
                : new Rect(0f, TrainerHeaderHeight, Screen.width * PlayerView.TrainerFraction / ui, 720f - TrainerHeaderHeight);

            GUI.BeginGroup(region);
            C.Fill(new Rect(0f, 0f, region.width, region.height), C.Background);
            const float pad = 12f, headH = 40f;

            // Header: title and window buttons.
            GUI.Label(new Rect(pad, 4f, region.width - 260f, headH - 8f), "SCORE DASHBOARD", C.Header);
            if (GUI.Button(new Rect(region.width - 236f, 6f, 120f, headH - 12f), Expanded ? "Collapse" : "Expand")) SetExpanded(!Expanded);
            if (GUI.Button(new Rect(region.width - 108f, 6f, 96f, headH - 12f), "Close")) Close();
            C.Fill(new Rect(0f, headH, region.width, 1f), new Color(1f, 1f, 1f, 0.15f));

            // Scrolling page.
            var view = new Rect(0f, headH + 2f, region.width, region.height - headH - 2f);
            viewHeight = view.height;
            if (Event.current.type == EventType.ScrollWheel && view.Contains(Event.current.mousePosition))
            {
                scroll += Event.current.delta.y * 24f;
                Event.current.Use();
            }
            scroll = Mathf.Clamp(scroll, 0f, Mathf.Max(0f, pageHeight - view.height));

            GUI.BeginGroup(view);
            float w = view.width - pad * 2f, top = pad - scroll;
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, Players.Count - 1));
            var player = Players.Count > 0 ? Players.Get(selected) : null;
            float bottom;
            if (w >= 900f)
            {
                // Wide: overview on the left, per-contact detail on the right.
                float leftW = w * 0.42f, rightX = pad + leftW + pad, rightW = w - leftW - pad;
                float y = Roster(pad, top, leftW);
                y = Overview(pad, y, leftW, player);
                y = Trends(pad, y, leftW, player, false);
                y = UnitPanel(pad, y, leftW);
                float y2 = Timeline(rightX, top, rightW, player);
                y2 = ContactList(rightX, y2, rightW, player);
                bottom = Mathf.Max(y, y2);
            }
            else
            {
                float y = Roster(pad, top, w);
                y = Overview(pad, y, w, player);
                y = Timeline(pad, y, w, player);
                y = Trends(pad, y, w, player, true);
                y = ContactList(pad, y, w, player);
                bottom = UnitPanel(pad, y, w);
            }
            pageHeight = bottom + scroll + pad;
            GUI.EndGroup();

            // Scroll bar.
            if (pageHeight > view.height)
            {
                float barH = view.height * view.height / pageHeight;
                float barY = view.y + (view.height - barH) * scroll / (pageHeight - view.height);
                C.Fill(new Rect(region.width - 5f, barY, 4f, barH), new Color(1f, 1f, 1f, 0.35f));
            }
            GUI.EndGroup();
            GUI.matrix = saved;
        }

        // --- Sections. Each draws at (x, y) with width w and returns the y below it. ---

        float Roster(float x, float y, float w)
        {
            C.SectionTitle(new Rect(x, y, w, 26f), "PLAYERS");
            y += 32f;
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players.Get(i);
                var row = new Rect(x, y, w, 34f);
                bool hover = row.Contains(Event.current.mousePosition);
                C.Fill(row, i == selected ? C.Selected : hover ? C.PanelHover : C.Panel);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) selected = i;
                string score = p.current != null ? $"Score {p.current.totalPoints} / {p.current.maxPoints}" : "no score yet";
                GUI.Label(new Rect(x + 10f, y, w * 0.3f, 34f), "<b>" + p.name + "</b>", C.Label);
                GUI.Label(new Rect(x + w * 0.32f, y, w * 0.34f, 34f), p.state, C.Label);
                GUI.Label(new Rect(x + w * 0.66f, y, w * 0.34f - 8f, 34f), score, C.Label);
                y += 38f;
            }
            GUI.Label(new Rect(x, y, w, 20f), "Click a player to see their stats. More players appear here with multiplayer.", C.Small);
            return y + 30f;
        }

        float Overview(float x, float y, float w, PlayerEntry player)
        {
            string name = player != null ? player.name : "-";
            C.SectionTitle(new Rect(x, y, w, 26f), $"STATS: {name}");
            y += 32f;
            var s = player != null ? player.current : null;
            if (s == null)
            {
                GUI.Label(new Rect(x, y, w, 24f), "No run data yet. Press START on the trainer panel.", C.Label);
                return y + 34f;
            }
            if (!ReferenceEquals(totalsFor, s))
            {
                totalsFor = s;
                totalAttempts = totalHolds = 0;
                foreach (var c in s.contacts) { totalAttempts += c.engageAttempts; totalHolds += c.holds; }
            }

            // Conditions, difficulty and the suggestion for next time.
            string conditions = $"{s.timeOfDay}, {s.visibility}" + (s.timeOfDay == "Day" ? "" : $", NV used {s.nightVisionPercent:F0}%");
            GUI.Label(new Rect(x, y, w, 20f), $"Difficulty: <b>{(string.IsNullOrEmpty(s.difficulty) ? "none" : s.difficulty)}</b>     Conditions: <b>{conditions}</b>", C.Label);
            y += 24f;
            int recommended = trainer != null && spawner != null && spawner.Finished ? trainer.RecommendedLevel(s) : -1;
            if (recommended >= 0)
            {
                GUI.Label(new Rect(x, y, w - 96f, 24f), $"Score {s.ScorePercent:F0} %.  Recommended next: <b>{trainer.DifficultyName(recommended)}</b>", C.Label);
                if (recommended != trainer.difficultyIndex && GUI.Button(new Rect(x + w - 90f, y, 90f, 24f), "Apply")) trainer.SetDifficulty(recommended);
                y += 30f;
            }

            // Big numbers.
            int perRow = w >= 560f ? 4 : 2;
            float gap = 8f, tw = (w - gap * (perRow - 1)) / perRow, th = 66f;
            float pct = s.maxPoints > 0 ? 100f * s.totalPoints / s.maxPoints : 0f;
            string[] values =
            {
                $"{s.totalPoints} / {s.maxPoints}",
                s.averageDetectionSeconds < 0f ? "-" : s.averageDetectionSeconds.ToString("F1") + " s",
                s.classificationAccuracy.ToString("F0") + " %",
                s.engagementDecisionScore.ToString("F0") + " %",
            };
            string[] captions = { "Total score", "Avg detection time", "Classification accuracy", "Engagement decisions" };
            Color[] colours = { Grade(pct), C.Accent, Grade(s.classificationAccuracy), Grade(s.engagementDecisionScore) };
            for (int i = 0; i < 4; i++)
                C.Tile(new Rect(x + (i % perRow) * (tw + gap), y + (i / perRow) * (th + gap), tw, th), values[i], captions[i], colours[i]);
            y += Mathf.CeilToInt(4f / perRow) * (th + gap) + 4f;

            // Counts.
            string[] names = { "Missed", "Unresolved", "False engagements", "Missed hostiles", "Wrong AI followed", "Wrong AI overridden", "Engage attempts", "Holds" };
            int[] counts = { s.missed, s.unresolved, s.falseEngagements, s.missedHostiles, s.followedWrongHint, s.overrodeWrongHintCorrectly, totalAttempts, totalHolds };
            bool[] badWhenHigh = { true, true, true, true, true, false, false, false };
            float ch = 30f;
            for (int i = 0; i < names.Length; i++)
            {
                var cell = new Rect(x + (i % perRow) * (tw + gap), y + (i / perRow) * (ch + 4f), tw, ch);
                C.Fill(cell, C.Panel);
                string colour = ColorUtility.ToHtmlStringRGB(counts[i] > 0 && badWhenHigh[i] ? C.Warn : C.Text);
                GUI.Label(new Rect(cell.x + 8f, cell.y, cell.width - 10f, ch), $"<b><color=#{colour}>{counts[i]}</color></b>  {names[i]}", C.Small);
            }
            y += Mathf.CeilToInt(names.Length / (float)perRow) * (ch + 4f) + 10f;

            // Confusion heatmap.
            GUI.Label(new Rect(x, y, w, 20f), "Confusion: rows = true class, columns = the player's call", C.Small);
            y += 22f;
            C.Heatmap(new Rect(x, y, w, 160f), s);
            return y + 174f;
        }

        float Timeline(float x, float y, float w, PlayerEntry player)
        {
            C.SectionTitle(new Rect(x, y, w, 26f), "REPLAY TIMELINE");
            y += 30f;
            var s = player != null ? player.current : null;
            if (s == null || s.contacts.Count == 0)
            {
                GUI.Label(new Rect(x, y, w, 24f), "No contacts yet.", C.Small);
                return y + 34f;
            }
            Legend(x, y, new[] { "unseen", "in view", "called, deciding", "right action", "wrong action" }, new[] { C.Neutral, C.Accent, C.Warn, C.Good, C.Bad });
            y += 22f;
            float duration = spawner != null && spawner.Duration > 0f ? spawner.Duration : Mathf.Max(1f, s.duration);
            int rows = Mathf.Min(s.contacts.Count, maxContactRows);
            for (int i = 0; i < rows; i++)
            {
                GUI.Label(new Rect(x, y, 36f, 13f), "#" + s.contacts[i].id.ToString("00"), C.Small);
                C.TimelineRow(new Rect(x + 38f, y, w - 38f, 13f), s.contacts[i], duration);
                y += 14f;
            }
            GUI.Label(new Rect(x + 38f, y, w - 38f, 18f), $"0 s{new string(' ', 6)}time{new string(' ', 6)}{duration:F0} s" + (rows < s.contacts.Count ? $"     (first {rows} of {s.contacts.Count} contacts)" : ""), C.Small);
            return y + 30f;
        }

        float ContactList(float x, float y, float w, PlayerEntry player)
        {
            C.SectionTitle(new Rect(x, y, w, 26f), "DECISION TREE PER CONTACT");
            y += 32f;
            var s = player != null ? player.current : null;
            if (s == null || s.contacts.Count == 0)
            {
                GUI.Label(new Rect(x, y, w, 24f), "No contacts yet.", C.Small);
                return y + 34f;
            }
            int rows = Mathf.Min(s.contacts.Count, maxContactRows);
            for (int i = 0; i < rows; i++)
            {
                var c = s.contacts[i];
                string extra = (c.aiText ?? "AI -")
                    + (c.holds > 0 ? $"   held {c.holdSeconds:F0} s" : "")
                    + (c.engageAttempts > 0 ? $"   {c.engageAttempts} shot{(c.engageAttempts > 1 ? "s" : "")}" : "")
                    + (c.decisionLatency >= 0f ? $"   call to action {c.decisionLatency:F1} s" : "");
                GUI.Label(new Rect(x, y, w * 0.34f, 20f), $"<b>#{c.id:00} {c.truthClass}</b>   = {(c.scored ? c.total.ToString() : "not scored")}", C.Label);
                GUI.Label(new Rect(x + w * 0.34f, y, w * 0.66f, 20f), extra, C.Small);
                C.TreePath(new Rect(x, y + 22f, w, 40f), c);
                y += 70f;
            }
            if (rows < s.contacts.Count) { GUI.Label(new Rect(x, y, w, 20f), $"First {rows} of {s.contacts.Count} contacts shown.", C.Small); y += 24f; }
            return y + 6f;
        }

        float Trends(float x, float y, float w, PlayerEntry player, bool sideBySide)
        {
            C.SectionTitle(new Rect(x, y, w, 26f), $"TRENDS: last {TrendData.ChartSessions} sessions");
            y += 30f;
            int real = player != null ? player.history.Count : 0;
            if (real != trendRealCount)
            {
                trendRealCount = real;
                trend = TrendData.PadWithSamples(player != null ? player.history : new List<TrendPoint>(), TrendData.ChartSessions);
            }
            int samples = TrendData.ChartSessions - Mathf.Min(real, TrendData.ChartSessions);
            GUI.Label(new Rect(x, y, w, 18f), samples > 0
                ? $"Solid = {TrendData.ChartSessions - samples} saved session{(TrendData.ChartSessions - samples == 1 ? "" : "s")}.  Dashed, lighter = {samples} generated points (sample data)."
                : "All points are saved sessions.", C.Small);
            y += 22f;

            float gap = 8f, h = 120f;
            float cw = sideBySide && w >= 560f ? (w - gap * 2f) / 3f : w;
            bool row = cw < w;
            C.TrendChart(new Rect(x, y, cw, h), "Avg detection time (lower is better)", trend, p => p.detectionSeconds, 0f, 20f, " s", false);
            C.TrendChart(row ? new Rect(x + cw + gap, y, cw, h) : new Rect(x, y + h + gap, cw, h), "Classification accuracy", trend, p => p.classificationAccuracy, 0f, 100f, " %", true);
            C.TrendChart(row ? new Rect(x + (cw + gap) * 2f, y, cw, h) : new Rect(x, y + (h + gap) * 2f, cw, h), "Engagement decision score", trend, p => p.engagementScore, 0f, 100f, " %", true);
            return y + (row ? h : h * 3f + gap * 2f) + 16f;
        }

        float UnitPanel(float x, float y, float w)
        {
            if (Unit == null) return y;
            C.SectionTitle(new Rect(x, y, w, 26f), Unit.Title);
            y += 30f;
            if (Unit.IsSample)
            {
                GUI.Label(new Rect(x, y, w, 18f), "Not real data. Replaced by unit-level results when multiplayer is added.", C.Small);
                y += 22f;
            }
            float[] cols = { 0f, 0.3f, 0.48f, 0.64f };
            string[] heads = { "Soldier", "Latest score", "Trend", "Weakest area" };
            for (int i = 0; i < heads.Length; i++) GUI.Label(new Rect(x + 8f + w * cols[i], y, w * 0.36f, 22f), heads[i], C.Small);
            y += 24f;
            float sum = 0f;
            foreach (var soldier in Unit.Soldiers)
            {
                var row = new Rect(x, y, w, 26f);
                C.Fill(row, row.Contains(Event.current.mousePosition) ? C.PanelHover : C.Panel);
                int t = TrendData.Trend(soldier.previousScore, soldier.latestScore);
                string colour = ColorUtility.ToHtmlStringRGB(t > 0 ? C.Good : t < 0 ? C.Bad : C.Dim);
                GUI.Label(new Rect(x + 8f, y, w * 0.3f, 26f), soldier.name, C.Label);
                GUI.Label(new Rect(x + 8f + w * cols[1], y, w * 0.18f, 26f), soldier.latestScore.ToString("F0") + " %", C.Label);
                GUI.Label(new Rect(x + 8f + w * cols[2], y, w * 0.16f, 26f), $"<color=#{colour}>{TrendData.TrendWord(t)}</color>", C.Label);
                GUI.Label(new Rect(x + 8f + w * cols[3], y, w * 0.36f - 8f, 26f), soldier.weakestArea, C.Label);
                sum += soldier.latestScore;
                y += 28f;
            }
            if (Unit.Soldiers.Count > 0)
                GUI.Label(new Rect(x + 8f, y, w, 24f), $"<b>Unit average  {sum / Unit.Soldiers.Count:F0} %</b>", C.Label);
            return y + 34f;
        }

        static void Legend(float x, float y, string[] names, Color[] colours)
        {
            float cx = x;
            for (int i = 0; i < names.Length; i++)
            {
                C.Fill(new Rect(cx, y + 5f, 10f, 10f), colours[i]);
                float width = C.Small.CalcSize(new GUIContent(names[i])).x;
                GUI.Label(new Rect(cx + 14f, y, width + 4f, 20f), names[i], C.Small);
                cx += width + 30f;
            }
        }

        static Color Grade(float percent)
        {
            return percent >= 70f ? C.Good : percent >= 45f ? C.Warn : C.Bad;
        }
    }
}
