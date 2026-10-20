using System.Collections;
using System.Collections.Generic;
using System.IO;
using Kurukshetra.Scenario;
using Kurukshetra.Scoring;
using UnityEngine;
using UnityEngine.InputSystem;
using C = Kurukshetra.Trainer.DashboardCharts;

namespace Kurukshetra.Trainer
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
        // Report filter: "" = the player picked in the roster; otherwise a SessionFilter key (all players, or one unit).
        string scopeKey = "";
        SessionResult scopeSession;
        float scopeNext;
        string scopeBuiltKey;
        float scroll, pageHeight, viewHeight;
        ScenarioSpawner spawner;
        TrainerScreen trainer;
        SessionResult totalsFor;
        int totalAttempts, totalHolds;
        List<TrendPoint> trend = new List<TrendPoint>();
        ChartModel[] trendModels;
        int trendModelsFor = -1;
        int trendRealCount = -1;

        // The flowchart decision tree: which checkpoints are folded, whether it is shown at all, and whose path is lit.
        public readonly TreeViewState Tree = new TreeViewState();

        // Charts tab: the six charts of ChartModels, drawn with IMGUI by ChartKit (the same layout as the HTML report's Charts view).
        public bool ChartsActive { get; private set; }
        float chartScroll, chartPageHeight;
        List<ChartModel> chartModels = new List<ChartModel>();
        float nextChartBuild;
        object chartsBuiltFor;

        // Switch between the built-in Overview and the Charts tab (also what the header button does).
        public void ShowCharts(bool on)
        {
            ChartsActive = on;
            chartScroll = 0f;
            nextChartBuild = 0f;
        }

        // Cards (default) or the detailed sections.
        public bool CardsView = true;
        readonly AarCardsView cardsView = new AarCardsView();
        bool capturing;
        string exportNote = "";

        ScoreKeeper keeper;
        string reportNote = "";
        bool open3dReplay;
        float replayT;
        bool replayPlaying;

        void Start()
        {
            spawner = FindAnyObjectByType<ScenarioSpawner>();
            keeper = FindAnyObjectByType<ScoreKeeper>();
            trainer = GetComponent<TrainerScreen>();
            if (Players == null) Players = Kurukshetra.Net.LanSession.Current == Kurukshetra.Net.LanSession.Role.Host ? new NetPlayerSource(FindAnyObjectByType<ScoreKeeper>(), spawner) : (IPlayerSource)new LocalPlayerSource(FindAnyObjectByType<ScoreKeeper>(), spawner);
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
            PlayerView.HideOverlays = false;
        }

        void Update()
        {
            if (!IsOpen) return;
            if (!PlayerView.Split && !Expanded) { Close(); return; }   // the trainer panel was hidden

            var kb = Keyboard.current;
            if (open3dReplay)
            {
                open3dReplay = false;
                if (!Kurukshetra.Replay.Replay3D.OpenLatest(keeper, out string why)) reportNote = why;
                return;
            }
            if (kb == null) return;
            if (replayPlaying)
            {
                replayT += Time.unscaledDeltaTime * ReplaySpeed;
                if (replayT >= ReplayEnd()) { replayT = ReplayEnd(); replayPlaying = false; }
            }
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
            const float pad = 12f;
            // Header: title and window buttons. In the trainer half (about 540 units wide) the four buttons would run under the
            // title, so the header gets two rows there: the title above, the buttons side by side below.
            bool narrow = region.width < 780f;
            float headH = narrow ? 76f : 40f;
            C.Fill(new Rect(0f, 0f, region.width, region.height), C.Background);

            Rect closeR, expandR, chartsR, reportR, noteR, titleR, replayR;
            if (narrow)
            {
                float gap = 6f, bw = (region.width - pad * 2f - gap * 4f) / 5f;
                titleR = new Rect(pad, 4f, region.width - pad * 2f, 30f);
                reportR = new Rect(pad, 40f, bw, 30f);
                chartsR = new Rect(pad + (bw + gap), 40f, bw, 30f);
                expandR = new Rect(pad + (bw + gap) * 2f, 40f, bw, 30f);
                closeR = new Rect(pad + (bw + gap) * 3f, 40f, bw, 30f);
                replayR = new Rect(pad + (bw + gap) * 4f, 40f, bw, 30f);
                float tw = C.Header.CalcSize(new GUIContent("SCORE DASHBOARD")).x + 12f;
                noteR = new Rect(pad + tw, 8f, Mathf.Max(0f, region.width - pad * 2f - tw), 22f);
                titleR.width = tw;
            }
            else
            {
                titleR = new Rect(pad, 4f, region.width - 260f, headH - 8f);
                reportR = new Rect(region.width - 492f, 6f, 120f, headH - 12f);
                replayR = new Rect(region.width - 640f, 6f, 140f, headH - 12f);
                chartsR = new Rect(region.width - 364f, 6f, 120f, headH - 12f);
                expandR = new Rect(region.width - 236f, 6f, 120f, headH - 12f);
                closeR = new Rect(region.width - 108f, 6f, 96f, headH - 12f);
                noteR = new Rect(pad + 250f, 10f, Mathf.Max(0f, region.width - 920f), 20f);
            }
            GUI.Label(titleR, "SCORE DASHBOARD", C.Header);
            if (GUI.Button(expandR, Expanded ? "Collapse" : "Expand")) SetExpanded(!Expanded);
            if (GUI.Button(closeR, "Close")) Close();
            if (keeper != null && GUI.Button(reportR, "HTML report"))
            {
                // The whole AAR as one file that opens in any browser; written at the end of every run, or now as a snapshot.
                ExportScoped(true);
            }
            if (GUI.Button(replayR, "3D REPLAY")) open3dReplay = true;
            if (reportNote.Length > 0) GUI.Label(noteR, reportNote, C.Small);
            if (GUI.Button(chartsR, ChartsActive ? "Overview" : "Charts"))
            {
                ShowCharts(!ChartsActive);
            }
            C.Fill(new Rect(0f, headH, region.width, 1f), new Color(1f, 1f, 1f, 0.15f));

            if (ChartsActive)
            {
                ChartsTab(region, headH, pad);
                GUI.EndGroup();
                GUI.matrix = saved;
                return;
            }

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
            if (CardsView)
            {
                float y = Roster(pad, top, w);
                y = ViewToggle(pad, y, w);
                var shown = ScopedSession(player, out var shownHistory);
                if (shown != null) y = ExportBar(pad, y, w, shown);
                if (scopeKey.Length == 0) EnsureTrend(player);
                else trend = shownHistory != null ? new List<TrendPoint>(shownHistory) : new List<TrendPoint>();
                y = cardsView.Draw(pad, y, w, shown, trend, keeper != null ? keeper.Learner : null);
                bottom = y;
            }
            else if (w >= 900f)
            {
                float y = Roster(pad, top, w * 0.42f);
                y = ViewToggle(pad, y, w * 0.42f);
                // Wide: overview on the left, per-contact detail on the right.
                float leftW = w * 0.42f, rightX = pad + leftW + pad, rightW = w - leftW - pad;
                y = Overview(pad, y, leftW, player);
                y = Quality(pad, y, leftW, player);
                y = LearnerSection(pad, y, leftW);
                y = Trends(pad, y, leftW, player, false);
                y = UnitPanel(pad, y, leftW);
                float y2 = TreeSection(rightX, top, rightW, player);
                y2 = Timeline(rightX, y2, rightW, player);
                y2 = Replay(rightX, y2, rightW, player);
                y2 = ContactList(rightX, y2, rightW, player);
                bottom = Mathf.Max(y, y2);
            }
            else
            {
                float y = Roster(pad, top, w);
                y = ViewToggle(pad, y, w);
                y = Overview(pad, y, w, player);
                y = Quality(pad, y, w, player);
                y = LearnerSection(pad, y, w);
                y = TreeSection(pad, y, w, player);
                y = Timeline(pad, y, w, player);
                y = Replay(pad, y, w, player);
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

        // --- Cards / Details switch and the exports. ---

        // The saved sessions of this player, oldest first. Real sessions only: no sample points are mixed in.
        void EnsureTrend(PlayerEntry player)
        {
            int real = player != null ? player.history.Count : 0;
            if (real != trendRealCount)
            {
                trendRealCount = real;
                trend = player != null ? new List<TrendPoint>(player.history) : new List<TrendPoint>();
            }
        }

        // The Charts tab: players on top, then the three trends over saved sessions and the three charts of this run.
        void ChartsTab(Rect region, float headH, float pad)
        {
            var view = new Rect(0f, headH + 2f, region.width, region.height - headH - 2f);
            viewHeight = view.height;
            if (Event.current.type == EventType.ScrollWheel && view.Contains(Event.current.mousePosition))
            {
                chartScroll += Event.current.delta.y * 24f;
                Event.current.Use();
            }
            chartScroll = Mathf.Clamp(chartScroll, 0f, Mathf.Max(0f, chartPageHeight - view.height));
            GUI.BeginGroup(view);
            float w = view.width - pad * 2f, y = pad - chartScroll;
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, Players.Count - 1));
            var player = Players.Count > 0 ? Players.Get(selected) : null;
            var shown = ScopedSession(player, out var shownHistory);
            if (!ReferenceEquals(chartsBuiltFor, shown) || Time.unscaledTime >= nextChartBuild)
            {
                chartsBuiltFor = shown;
                nextChartBuild = Time.unscaledTime + 0.5f;
                chartModels = ChartModels.All(shown, shownHistory);
            }
            y = Roster(pad, y, w);

            int cols = w >= 1000f ? 3 : w >= 640f ? 2 : 1;
            const float gap = 10f, chartH = 290f;
            float cw = (w - gap * (cols - 1)) / cols;
            int real = shownHistory != null ? shownHistory.Count : 0;
            for (int section = 0; section < 2; section++)
            {
                C.SectionTitle(new Rect(pad, y, w, 26f), section == 0 ? $"TRENDS: {real} saved session{(real == 1 ? "" : "s")}" : "THIS RUN");
                y += 32f;
                for (int k = 0; k < 3; k++)
                {
                    int idx = section * 3 + k;
                    if (idx >= chartModels.Count) break;
                    int col = k % cols, row = k / cols;
                    ChartKit.Draw(new Rect(pad + col * (cw + gap), y + row * (chartH + gap), cw, chartH), chartModels[idx]);
                }
                y += Mathf.CeilToInt(3f / cols) * (chartH + gap) + 8f;
            }
            GUI.Label(new Rect(pad, y, w, 20f), "Hover a bar or a point for the exact numbers. Green, amber and red mean the same thing on every chart.", C.Small);
            y += 30f;
            chartPageHeight = y + chartScroll + pad;
            GUI.EndGroup();
            if (chartPageHeight > view.height)
            {
                float barH = view.height * view.height / chartPageHeight;
                float barY = view.y + (view.height - barH) * chartScroll / (chartPageHeight - view.height);
                C.Fill(new Rect(region.width - 5f, barY, 4f, barH), new Color(1f, 1f, 1f, 0.35f));
            }
        }

        // --- Report filter: All players / one unit / one player. ---

        // Every roster player that has a session, with their seat and unit.
        List<SessionPart> Parts()
        {
            var list = new List<SessionPart>();
            if (Players == null) return list;
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players.Get(i);
                if (p == null || p.current == null) continue;
                list.Add(new SessionPart { player = p.name, seat = p.seatId, unit = string.IsNullOrEmpty(p.unit) ? "Unit 1" : p.unit, session = p.current, history = p.history });
            }
            return list;
        }

        // The session the cards and charts show: the roster's selected player, or the combined view of the filter (rebuilt twice a second).
        SessionResult ScopedSession(PlayerEntry player, out IList<TrendPoint> history)
        {
            history = player != null ? player.history : null;
            if (scopeKey.Length == 0) return player != null ? player.current : null;
            var parts = Parts();
            if (scopeSession == null || scopeBuiltKey != scopeKey || Time.unscaledTime >= scopeNext)
            {
                scopeBuiltKey = scopeKey;
                scopeNext = Time.unscaledTime + 0.5f;
                scopeSession = SessionFilter.View(parts, scopeKey);
            }
            history = SessionFilter.HistoryFor(parts, scopeKey);
            return scopeSession;
        }

        void ExportScoped(bool header)
        {
            string path = keeper != null ? keeper.ExportReport(Parts()) : null;
            string note = path != null ? "Report: " + path : "No run to report yet.";
            if (header) reportNote = note; else exportNote = note;
            if (path != null) Application.OpenURL("file:///" + path.Replace('\\', '/'));
        }

        // Chips under the roster: the clicked player, All players, and one per unit when there is more than one.
        float FilterBar(float x, float y, float w)
        {
            var parts = Parts();
            if (parts.Count < 2)
            {
                GUI.Label(new Rect(x, y, w, 20f), SessionFilter.SoloNote, C.Small);
                return y + 26f;
            }
            var units = SessionFilter.Units(parts);
            var keys = new List<string> { "", SessionFilter.AllKey };
            if (units.Count > 1) foreach (var u in units) keys.Add(SessionFilter.UnitKey(u));
            float cx = x + 62f, bh = 26f;
            GUI.Label(new Rect(x, y, 60f, bh), "SHOW:", C.Small);
            foreach (var k in keys)
            {
                string label = k.Length == 0 ? "Selected player" : SessionFilter.Label(k, SessionFilter.Select(parts, k).Count);
                float bw = Mathf.Max(90f, C.Label.CalcSize(new GUIContent(label)).x + 20f);
                if (cx + bw > x + w) { cx = x + 62f; y += bh + 4f; }
                var r = new Rect(cx, y, bw, bh);
                if (k == scopeKey) C.Fill(r, C.Selected);
                if (GUI.Button(r, label)) { scopeKey = k; scopeSession = null; }
                cx += bw + 6f;
            }
            y += bh + 6f;
            GUI.Label(new Rect(x, y, w, 20f), "Cards, charts and the HTML report follow this: all players, one unit, or the player you click above.", C.Small);
            y += 26f;

            // Unit comparison: a row is a unit or one of its players; click a row to show it.
            float[] col = { 0.26f, 0.08f, 0.1f, 0.12f, 0.18f, 0.14f, 0.12f };
            string[] head = { "UNIT / PLAYER", "PLAYERS", "CONTACTS", "DETECTION", "DECISION QUALITY", "GOOD RESULT", "AVG SCORE" };
            float hx = x + 6f;
            for (int i = 0; i < head.Length; i++) { GUI.Label(new Rect(hx, y, w * col[i], 20f), head[i], C.Small); hx += w * col[i]; }
            y += 22f;
            foreach (var r in SessionFilter.Compare(parts))
            {
                var rr = new Rect(x, y, w, 24f);
                bool on = r.key == scopeKey;
                C.Fill(rr, on ? C.Selected : rr.Contains(Event.current.mousePosition) ? C.PanelHover : C.Panel);
                if (GUI.Button(rr, GUIContent.none, GUIStyle.none)) { scopeKey = r.key; scopeSession = null; }
                string[] cells = { (r.isUnit ? "<b>" + r.label + "</b>" : "   " + r.label), r.players.ToString(), r.contacts.ToString(), SessionFilter.Pct(r.detection),
                    r.quality < 0f ? "n/a" : r.quality.ToString("F0") + " (n " + r.qualityN + ")", SessionFilter.Pct(r.goodOutcome), SessionFilter.Pct(r.avgScore) };
                hx = x + 6f;
                for (int i = 0; i < cells.Length; i++) { GUI.Label(new Rect(hx, y, w * col[i], 24f), cells[i], C.Label); hx += w * col[i]; }
                y += 26f;
            }
            return y + 6f;
        }

        float ViewToggle(float x, float y, float w)
        {
            float bw = 110f;
            var a = new Rect(x, y, bw, 26f);
            var b = new Rect(x + bw + 6f, y, bw, 26f);
            C.Fill(CardsView ? a : b, C.Selected);
            if (GUI.Button(a, "Cards")) CardsView = true;
            if (GUI.Button(b, "Details")) CardsView = false;
            GUI.Label(new Rect(x + bw * 2f + 16f, y, Mathf.Max(0f, w - bw * 2f - 16f), 26f), CardsView ? "Weakest problem first" : "The full sections", C.Small);
            return y + 36f;
        }

        // Open report, card images, data export, open folder.
        float ExportBar(float x, float y, float w, SessionResult s)
        {
            string[] names = { "Open report (HTML)", "Save card images (PNG)", "Export data (JSON + CSV)", "Open folder" };
            int perRow = w >= 640f ? 4 : 2;
            float gap = 6f, bw = (w - gap * (perRow - 1)) / perRow, bh = 28f;
            for (int i = 0; i < names.Length; i++)
            {
                var r = new Rect(x + (i % perRow) * (bw + gap), y + (i / perRow) * (bh + gap), bw, bh);
                if (!GUI.Button(r, names[i])) continue;
                if (i == 0)
                {
                    ExportScoped(false);
                }
                else if (i == 1) { if (!capturing) StartCoroutine(CaptureCards()); }
                else if (i == 2)
                {
                    var files = AarCards.ExportData(s, HtmlReport.ReportsFolder);
                    exportNote = files != null ? "Saved: " + files[0] + " and " + files[1] : "Could not write the data files.";
                }
                else OpenReportsFolder();
            }
            y += Mathf.CeilToInt(names.Length / (float)perRow) * (bh + gap);
            if (exportNote.Length > 0)
            {
                GUI.Label(new Rect(x, y, w, 18f), exportNote, C.Small);
                y += 20f;
            }
            return y + 6f;
        }

        void OpenReportsFolder()
        {
            try
            {
                Directory.CreateDirectory(HtmlReport.ReportsFolder);
                Application.OpenURL("file:///" + HtmlReport.ReportsFolder.Replace('\\', '/'));
                exportNote = "Folder: " + HtmlReport.ReportsFolder;
            }
            catch (System.Exception e) { exportNote = "Could not open the folder: " + e.Message; }
        }

        // One PNG per screenful of the expanded cards view (the whole window, so the Expanded dashboard is what gets saved).
        IEnumerator CaptureCards()
        {
            capturing = true;
            bool wasExpanded = Expanded;
            float oldScroll = scroll;
            SetExpanded(true);
            scroll = 0f;
            yield return null;
            yield return null;
            string folder = HtmlReport.ReportsFolder;
            int saved = 0;
            string stem = Path.Combine(folder, $"aar_cards_{System.DateTime.Now:yyyyMMdd_HHmmss}");
            try { Directory.CreateDirectory(folder); } catch (System.Exception) { }
            int pages = Mathf.Clamp(Mathf.CeilToInt(pageHeight / Mathf.Max(1f, viewHeight)), 1, 8);
            for (int i = 0; i < pages; i++)
            {
                scroll = i * viewHeight * 0.95f;
                yield return null;
                yield return new WaitForEndOfFrame();
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex != null)
                {
                    try { File.WriteAllBytes($"{stem}_{i + 1}.png", tex.EncodeToPNG()); saved++; }
                    catch (System.Exception e) { exportNote = "Could not write the PNG: " + e.Message; }
                    Destroy(tex);
                }
            }
            if (saved > 0) exportNote = $"Saved {saved} card image(s): {stem}_1.png";
            scroll = oldScroll;
            SetExpanded(wasExpanded);
            capturing = false;
        }

        // --- Sections. Each draws at (x, y) with width w and returns the y below it. ---

        float Roster(float x, float y, float w)
        {
            C.SectionTitle(new Rect(x, y, w, 26f), "PLAYERS");
            y += 32f;
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players.Get(i);
                // LAN team run: the seat's role, and for a sentry / commander the link they are measured on.
                string extra = RoleLine(p);
                float rowH = extra.Length > 0 ? 54f : 34f;
                var row = new Rect(x, y, w, rowH);
                bool hover = row.Contains(Event.current.mousePosition);
                C.Fill(row, i == selected ? C.Selected : hover ? C.PanelHover : C.Panel);
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) { selected = i; scopeKey = ""; }
                string score = p.current != null ? $"Score {p.current.totalPoints} / {p.current.maxPoints}" : "no score yet";
                string role = string.IsNullOrEmpty(p.role) ? "" : $"  <color=#FFD060>{p.role}</color>";
                GUI.Label(new Rect(x + 10f, y, w * 0.34f, 34f), "<b>" + p.name + "</b>" + role, C.Label);
                GUI.Label(new Rect(x + w * 0.36f, y, w * 0.3f, 34f), p.state, C.Label);
                GUI.Label(new Rect(x + w * 0.66f, y, w * 0.34f - 8f, 34f), score, C.Label);
                if (extra.Length > 0) GUI.Label(new Rect(x + 10f, y + 30f, w - 20f, 20f), extra, C.Small);
                y += rowH + 4f;
            }
            GUI.Label(new Rect(x, y, w, 20f), "Click a player to see their stats. More players appear here with multiplayer.", C.Small);
            return FilterBar(x, y + 30f, w);
        }

        // The roster's second line for a seat with a role: sentry = reports and lock-to-report, commander = report-to-decision.
        public static string RoleLine(PlayerEntry p)
        {
            var s = p != null ? p.current : null;
            if (s == null || string.IsNullOrEmpty(p.role) || p.role == "ANY") return "";
            string handled = s.handledByTeam > 0 ? $", {s.handledByTeam} handled by teammates" : "";
            if (p.role == "SENTRY")
                return $"Sentry: {s.sentryReports} report{(s.sentryReports == 1 ? "" : "s")}, lock to report " + (s.sentryReportLatencyMean < 0f ? "-" : s.sentryReportLatencyMean.ToString("F1") + " s") + handled;
            return "Commander: report to decision " + (s.commanderDelayMean < 0f ? "-" : s.commanderDelayMean.ToString("F1") + " s") + $" ({s.commanderDelayCount} decision{(s.commanderDelayCount == 1 ? "" : "s")})"
                + (s.upstreamWaivers > 0 ? $", {s.upstreamWaivers} miss waived (report too late)" : "") + (s.reportAloneEngagements > 0 ? $", {s.reportAloneEngagements} engaged on a report alone" : "")
                + (s.handoffFratricides > 0 ? $", {s.handoffFratricides} hand-off fratricide" : "") + handled;
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
            GUI.Label(new Rect(x, y, w, 20f), $"Difficulty: <b>{(string.IsNullOrEmpty(s.difficulty) ? "none" : s.difficulty)}</b>     Conditions: <b>{conditions}</b>     Display: <b>{s.displayTier}</b>", C.Label);
            y += 24f;
            if (s.swarm != null && s.swarm.happened)
            {
                string sc = ColorUtility.ToHtmlStringRGB(s.swarm.leakers > 0 ? C.Bad : s.swarm.nearestFirstRate >= 0.5f ? C.Good : C.Warn);
                GUI.Label(new Rect(x, y, w, 20f), $"<b><color=#{sc}>Swarm triage</color></b>  {s.swarm.summary}", C.Label);
                y += 24f;
            }
            if (s.groupCall != null && s.groupCall.happened)
            {
                string gc = ColorUtility.ToHtmlStringRGB(s.groupCall.wrong > 0 ? C.Warn : C.Good);
                GUI.Label(new Rect(x, y, w, 20f), $"<b><color=#{gc}>Group calls</color></b>  {s.groupCall.summary}", C.Label);
                y += 24f;
            }
            if (s.cardChange != null && s.cardChange.happened)
            {
                string colour = ColorUtility.ToHtmlStringRGB(s.cardChange.staleErrors > 0 || s.cardChange.adaptationSeconds < 0f ? C.Warn : C.Good);
                GUI.Label(new Rect(x, y, w, 20f), $"<b><color=#{colour}>Card change</color></b>  {s.cardChange.summary}", C.Label);
                y += 24f;
            }
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

        // Full scoring (doc §5.4): process score, the process x outcome grid, the clock, and the two signal-detection results.
        float Quality(float x, float y, float w, PlayerEntry player)
        {
            var s = player != null ? player.current : null;
            if (s == null) return y;
            C.SectionTitle(new Rect(x, y, w, 26f), "DECISION QUALITY");
            y += 32f;

            // Process: judged on what the player could know (their own call, the card, the evidence), not on luck.
            float gap = 8f, tileW = Mathf.Min(220f, (w - gap) * 0.4f), th = 66f;
            C.Tile(new Rect(x, y, tileW, th), s.DecisionQualityValue, s.DecisionQualityCaption, s.processScore < 0f ? C.Dim : Grade(s.processScore));

            // 2 x 2 grid: rows = decision sound / unsound, columns = good / bad result.
            float gx = x + tileW + gap, gw = w - tileW - gap, cw = gw * 0.5f - 2f, chh = th * 0.5f - 2f;
            Cell(new Rect(gx, y, cw, chh), $"{s.gridSoundSuccessful}  sound, good result", C.Good);
            Cell(new Rect(gx + cw + 4f, y, cw, chh), $"{s.gridSoundUnlucky}  sound, bad luck", C.Warn);
            Cell(new Rect(gx, y + chh + 4f, cw, chh), $"{s.gridLucky}  unsound, lucky", C.Warn);
            Cell(new Rect(gx + cw + 4f, y + chh + 4f, cw, chh), $"{s.gridUnsound}  unsound", C.Bad);
            y += th + 10f;

            string Warn(int n) => n > 0 ? $"<color=#{ColorUtility.ToHtmlStringRGB(C.Warn)}><b>{n}</b></color>" : $"<b>{n}</b>";
            GUI.Label(new Rect(x, y, w, 20f), $"Clock misses {Warn(s.clockMisses)}   Correct rejections <b>{s.correctRejections}</b>   Unverified engagements {Warn(s.unverifiedEngagements)}   Search failures {Warn(s.searchFailures)}   Perception failures {Warn(s.perceptionFailures)}", C.Small);
            y += 24f;

            if (s.prevalence != "Custom" || s.quietWatch)
            {
                string pv = $"Prevalence <b>{s.prevalence}</b>, realised {(s.realisedHostileShare < 0f ? "-" : (100f * s.realisedHostileShare).ToString("F0") + " %")} hostile" + (s.prevalenceNote.Length > 0 ? $"  <color=#{ColorUtility.ToHtmlStringRGB(C.Warn)}>{s.prevalenceNote}</color>" : "");
                GUI.Label(new Rect(x, y, w, 20f), pv, C.Small);
                y += 22f;
            }
            if (s.crowdTracks > 0 || s.iffChallenges > 0)
            {
                GUI.Label(new Rect(x, y, w, 20f), $"<b>Dense sky</b>  {s.crowdTracks} crowd tracks (peak {s.crowdPeak} airborne), {s.crowdChecked} checked, {s.iffChallenges} IFF challenges, <b>{s.crowdFriendliesHit}</b> friendlies engaged or jammed", C.Small);
                y += 22f;
            }
            if (!string.IsNullOrEmpty(s.seatRole) || s.handledByTeam > 0)
            {
                string chain = s.seatRole == "COMMANDER" ? $"report to decision {(s.commanderDelayMean < 0f ? "-" : s.commanderDelayMean.ToString("F1") + " s")} ({s.commanderDelayCount}), {s.upstreamWaivers} miss(es) waived as upstream, {s.reportAloneEngagements} engaged on a report alone, {s.handoffFratricides} hand-off fratricide"
                    : s.seatRole == "SENTRY" ? $"{s.sentryReports} reported, lock to report {(s.sentryReportLatencyMean < 0f ? "-" : s.sentryReportLatencyMean.ToString("F1") + " s")}" : "";
                GUI.Label(new Rect(x, y, w, 20f), $"<b>Team{(string.IsNullOrEmpty(s.seatRole) ? "" : ": " + s.seatRole)}</b>  {chain}{(chain.Length > 0 ? ", " : "")}{s.handledByTeam} handled by teammates", C.Small);
                y += 22f;
            }
            if (s.quietWatch)
            {
                GUI.Label(new Rect(x, y, w, 20f), "<b>Quiet watch vigilance</b>  " + s.vigilance.summary, C.Small);
                y += 22f;
            }
            y = SdtRow(x, y, w, "Report: is there a drone?", s.reportSdt);
            y = SdtRow(x, y, w, "Engage: is it hostile?", s.engageSdt);
            GUI.Label(new Rect(x, y, w, 18f), SessionResult.DecisionQualityHelp + " The result is what actually happened.", C.Small);
            return y + 28f;
        }

        // Module 9: what the learner model has learned about this soldier over all their sessions, and the next run it suggests.
        float LearnerSection(float x, float y, float w)
        {
            var p = keeper != null ? keeper.Learner : null;
            C.SectionTitle(new Rect(x, y, w, 26f), "LEARNER MODEL (all sessions)");
            y += 32f;
            if (p == null || p.observations.Count == 0)
            {
                GUI.Label(new Rect(x, y, w, 20f), "No sessions yet. The model starts learning after the first finished run.", C.Small);
                return y + 28f;
            }
            float ability = 1f / (1f + Mathf.Exp(-p.theta));
            GUI.Label(new Rect(x, y, w, 20f), $"<b>{p.player}</b>   {p.sessions} sessions, {p.observations.Count} graded events.   Ability {p.theta:+0.00;-0.00;0.00} (logit; 0 = average trainee)", C.Label);
            y += 24f;
            GUI.Label(new Rect(x, y, w, 18f), "Weakest recognition (predicted right call):", C.Small);
            y += 20f;
            foreach (var c in LearnerModel.Weakest(p, 3))
            {
                var bar = new Rect(x + 8f, y + 4f, Mathf.Min(160f, w * 0.3f), 10f);
                C.Fill(bar, C.Panel);
                C.Fill(new Rect(bar.x, bar.y, bar.width * c.p, bar.height), c.p >= 0.75f ? C.Good : c.p >= 0.5f ? C.Warn : C.Bad);
                GUI.Label(new Rect(bar.xMax + 8f, y, w - bar.width - 20f, 18f), $"{c.p * 100f:F0} %  {LearnerModel.Pretty(c.cls)}, {c.light}, {LearnerModel.RangeNames[c.rangeBin]}  ({c.seen} seen)", C.Small);
                y += 20f;
            }
            var plan = LearnerModel.Plan(p);
            GUI.Label(new Rect(x, y + 2f, w - 130f, 36f), plan.text, C.Small);
            if (trainer != null && GUI.Button(new Rect(x + w - 124f, y, 124f, 24f), "Use for next run")) trainer.ApplyLearnerPlan(plan);
            y += 40f;
            GUI.Label(new Rect(x, y, w, 18f), "One logistic model (ability + task + class + light + range), pooled with the other soldiers on this PC. Starting estimates are used until there is data.", C.Small);
            return y + 26f;
        }

        // d' (telling them apart) and c (trigger-happy < 0 < hesitant) on a slider, only once there are 20 of each.
        float SdtRow(float x, float y, float w, string name, SdtResult r)
        {
            GUI.Label(new Rect(x, y, w, 20f), $"<b>{name}</b>   {r.hits}/{r.signals} hits, {r.falseAlarms}/{r.noise} false alarms", C.Small);
            y += 20f;
            if (!r.gated)
            {
                GUI.Label(new Rect(x, y, w, 20f), $"d' and c: insufficient data (needs {20} of each; pooled across sessions later)", C.Small);
                return y + 24f;
            }
            float barW = Mathf.Max(120f, w - 190f);
            var bar = new Rect(x, y + 6f, barW, 8f);
            C.Fill(bar, C.Panel);
            C.Fill(new Rect(bar.center.x - 1f, bar.y - 3f, 2f, 14f), C.Dim);
            float t = Mathf.InverseLerp(-1.5f, 1.5f, r.c);
            C.Fill(new Rect(bar.x + t * bar.width - 4f, bar.y - 4f, 8f, 16f), Mathf.Abs(r.c) < 0.3f ? C.Good : C.Warn);
            GUI.Label(new Rect(x + barW + 8f, y, 182f, 20f), $"d' {r.dPrime:F2}   c {r.c:+0.00;-0.00;0.00}", C.Small);
            GUI.Label(new Rect(x, y + 14f, barW, 18f), "trigger-happy", C.Small);
            GUI.Label(new Rect(x + barW - 60f, y + 14f, 60f, 18f), "hesitant", C.Small);
            return y + 36f;
        }

        static void Cell(Rect r, string text, Color colour)
        {
            C.Fill(r, C.Panel);
            C.Fill(new Rect(r.x, r.y, 4f, r.height), colour);
            GUI.Label(new Rect(r.x + 10f, r.y, r.width - 12f, r.height), text, C.Small);
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

        // AAR replay: the logged run played back top-down (truth view), with where the soldier was looking and what was on
        // their screen. Click a contact for "your call" against "the sound call". Built from the logged tracks of the latest
        // run (an annotated replay, not a re-simulation).
        const float ReplaySpeed = 10f;
        bool replayFollow;      // centre the replay map on the soldier instead of the post

        static Vector2 ReplayOffset(float wx, float wz, float scale)
        {
            Vector3 d = Quaternion.Euler(0f, -RunLog.sectorYaw, 0f) * (new Vector3(wx, 0f, wz) - RunLog.post);
            return new Vector2(d.x, -d.z) * scale;
        }

        float ReplayEnd()
        {
            float end = 0f;
            foreach (var r in ContactRegistry.Records) if (r.track.Count > 0) end = Mathf.Max(end, r.track[r.track.Count - 1].t);
            return end;
        }

        float Replay(float x, float y, float w, PlayerEntry player)
        {
            C.SectionTitle(new Rect(x, y, w, 26f), "REPLAY: YOUR CALL vs THE SOUND CALL");
            y += 30f;
            var s = player != null ? player.current : null;
            float end = ReplayEnd();
            if (s == null || end <= 0f)
            {
                GUI.Label(new Rect(x, y, w, 24f), "Nothing logged yet. The replay fills in as the run goes.", C.Small);
                return y + 30f;
            }
            if (GUI.Button(new Rect(x, y, 70f, 22f), replayPlaying ? "Pause" : "Play"))
            {
                if (!replayPlaying && replayT >= end) replayT = 0f;
                replayPlaying = !replayPlaying;
            }
            replayT = GUI.HorizontalSlider(new Rect(x + 80f, y + 6f, w - 160f, 14f), replayT, 0f, end);
            GUI.Label(new Rect(x + w - 74f, y, 74f, 22f), $"{replayT:F0} / {end:F0} s", C.Small);
            y += 28f;

            float size = Mathf.Min(w, 380f);
            var map = new Rect(x, y, size, size);
            var local = new Rect(0f, 0f, size, size);       // everything below is drawn inside this group, in map-local coordinates
            GUI.BeginGroup(map);
            if (Event.current.type == EventType.Repaint) C.Fill(local, new Color(0.06f, 0.08f, 0.11f, 1f));
            float radius = Mathf.Max(100f, RunLog.spawnRadius), scale = size * 0.5f / (radius * 1.1f);
            ViewPoint view = default;
            bool haveView = false;
            foreach (var v in RunLog.view) { if (v.t <= replayT) { view = v; haveView = true; } else break; }
            Vector2 centre = local.center;
            if (replayFollow && haveView) centre = local.center - ReplayOffset(view.x, view.z, scale);
            Quaternion turn = Quaternion.Euler(0f, -RunLog.sectorYaw, 0f);
            System.Func<float, float, Vector2> toMap = (wx, wz) =>
            {
                Vector3 d = turn * (new Vector3(wx, 0f, wz) - RunLog.post);
                return centre + new Vector2(d.x, -d.z) * scale;
            };
            bool repaint = Event.current.type == EventType.Repaint;

            if (repaint)
            {
                // Spawn ring.
                for (int i = 0; i < 72; i++)
                {
                    float a = i * 5f * Mathf.Deg2Rad;
                    var q = centre + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius * scale;
                    if (local.Contains(q)) C.Fill(new Rect(q.x - 0.75f, q.y - 0.75f, 1.5f, 1.5f), new Color(1f, 1f, 1f, 0.25f));
                }
                // The soldier's path so far, fading with age.
                Vector2 prevP = default; bool havePrev = false;
                foreach (var v in RunLog.view)
                {
                    if (v.t > replayT) break;
                    Vector2 p = toMap(v.x, v.z);
                    if (havePrev && (p - prevP).sqrMagnitude > 4f)
                    {
                        float age = Mathf.Clamp01((replayT - v.t) / 120f);
                        GuiDraw.Line(prevP, p, 2f, new Color(0.4f, 1f, 0.5f, Mathf.Lerp(0.8f, 0.15f, age)), local);
                        prevP = p;
                    }
                    else if (!havePrev) prevP = p;
                    havePrev = true;
                }
                // Contact trails: the last 10 s of each contact's track, in its class colour.
                foreach (var rec in ContactRegistry.Records)
                {
                    var col = ClassColour(rec.truthClass.ToString()); col.a = 0.35f;
                    for (int k = 1; k < rec.track.Count; k++)
                    {
                        var a = rec.track[k - 1]; var b = rec.track[k];
                        if (b.t > replayT) break;
                        if (b.t < replayT - 10f) continue;
                        GuiDraw.Line(toMap(a.x, a.z), toMap(b.x, b.z), 1.5f, col, local);
                    }
                }
                // Where the soldier is looking: a faint filled wedge (narrow and bright through the binoculars), its edges, and an arrow.
                if (haveView)
                {
                    Vector2 eye = toMap(view.x, view.z);
                    bool optic = view.fov < 20f;
                    float yawRel = view.yaw - RunLog.sectorYaw, half = Mathf.Max(0.5f, view.fov * 0.5f);
                    Color wedge = optic ? new Color(1f, 1f, 0.5f, 0.35f) : new Color(0.4f, 1f, 0.5f, 0.12f);
                    GuiDraw.Wedge(eye, yawRel, half, radius * scale, wedge, local);
                    GuiDraw.WedgeEdges(eye, yawRel, half, radius * scale, 1.5f, optic ? new Color(1f, 1f, 0.5f, 0.9f) : new Color(0.4f, 1f, 0.5f, 0.7f), local);
                    Vector2 tip = eye + GuiDraw.Dir(yawRel) * 11f;
                    GuiDraw.Line(eye, tip, 3f, Color.white, local);
                    if (local.Contains(eye)) C.Fill(new Rect(eye.x - 3f, eye.y - 3f, 6f, 6f), new Color(0.4f, 1f, 0.5f, 1f));
                }
            }
            Vector2 post = toMap(RunLog.post.x, RunLog.post.z);
            if (repaint && local.Contains(post)) C.Fill(new Rect(post.x - 4f, post.y - 4f, 8f, 8f), Color.white);

            // Contacts at this moment; a click picks the nearest.
            ContactResult focus = Tree.focus >= 0 && Tree.focus < s.contacts.Count ? s.contacts[Tree.focus] : null;
            int clicked = -1;
            float best = 14f;
            bool click = Event.current.type == EventType.MouseDown && local.Contains(Event.current.mousePosition);
            for (int i = 0; i < s.contacts.Count; i++)
            {
                var c = s.contacts[i];
                ContactRecord rec = null;
                foreach (var r in ContactRegistry.Records) if (r.id == c.id) { rec = r; break; }
                if (rec == null || !At(rec.track, replayT, out Vector3 at, out bool inView)) continue;
                Vector2 p = toMap(at.x, at.z);
                if (!local.Contains(p)) continue;
                if (repaint)
                {
                    if (inView) C.Fill(new Rect(p.x - 7f, p.y - 7f, 14f, 14f), new Color(0.35f, 1f, 0.45f, 0.45f));
                    C.Fill(new Rect(p.x - 3f, p.y - 3f, 6f, 6f), ClassColour(c.truthClass));
                    if (c == focus) { C.Fill(new Rect(p.x - 9f, p.y - 10f, 18f, 1.5f), Color.white); C.Fill(new Rect(p.x - 9f, p.y + 9f, 18f, 1.5f), Color.white); }
                    // Event pop-ups: a call or action shows for 4 s after it happened.
                    string pop = null;
                    if (rec.callTime >= 0f && replayT >= rec.callTime && replayT < rec.callTime + 4f) pop = "called " + rec.call;
                    if (rec.actionTime >= 0f && replayT >= rec.actionTime && replayT < rec.actionTime + 4f) pop = rec.action + (string.IsNullOrEmpty(rec.actionResult) ? "" : ": " + rec.actionResult);
                    if (pop == null && c.actionTime >= 0f && replayT >= c.actionTime) pop = c.action;
                    if (pop != null) GUI.Label(new Rect(p.x + 7f, p.y - 16f, 150f, 16f), pop, C.Small);
                }
                if (click) { float d = Vector2.Distance(p, Event.current.mousePosition); if (d < best) { best = d; clicked = i; } }
            }
            GUI.EndGroup();
            if (GUI.Button(new Rect(x + size - 74f, y + size - 20f, 72f, 18f), replayFollow ? "Follow: on" : "Follow: off")) replayFollow = !replayFollow;
            if (clicked >= 0) { Tree.focus = clicked; Event.current.Use(); }

            // The picked contact's two calls, beside the map (or under it when narrow).
            bool beside = size + 12f <= w - 200f;
            float tx = beside ? x + size + 12f : x, ty = beside ? y : y + size + 8f, tw = beside ? w - size - 12f : w;
            if (focus == null) GUI.Label(new Rect(tx, ty, tw, 40f), "Click a contact on the map (or 'tree' in the list) to compare your call with the sound call.", C.Small);
            else
            {
                string tone = ColorUtility.ToHtmlStringRGB(focus.matchedSound ? C.Good : C.Warn);
                GUI.Label(new Rect(tx, ty, tw, 22f), $"<b>#{focus.id:00} {focus.truthClass}</b>   {focus.grid}", C.Label);
                GUI.Label(new Rect(tx, ty + 24f, tw, 40f), "Your call: " + focus.yourCall, C.Small);
                GUI.Label(new Rect(tx, ty + 44f, tw, 40f), $"<color=#{tone}>Sound call: {focus.soundCall}</color>", C.Small);
                GUI.Label(new Rect(tx, ty + 64f, tw, 40f), "Consequence: " + focus.consequence, C.Small);
                GUI.Label(new Rect(tx, ty + 92f, tw, 40f), "Annotated replay of the logged run, not a re-simulation.", C.Small);
            }
            float bottom = Mathf.Max(y + size, ty + 120f);
            GUI.Label(new Rect(x, bottom + 2f, w, 18f), "Up = sector centre, white square = the post, green wedge = the soldier's view (yellow = optic), green trail = their path, halo = on their screen.", C.Small);
            return bottom + 28f;
        }

        static bool At(List<TrackPoint> track, float t, out Vector3 pos, out bool inView)
        {
            pos = Vector3.zero;
            inView = false;
            if (track.Count == 0 || t < track[0].t || t > track[track.Count - 1].t + 0.6f) return false;
            int i = 0;
            while (i + 1 < track.Count && track[i + 1].t <= t) i++;
            var a = track[i];
            var b = i + 1 < track.Count ? track[i + 1] : a;
            float k = b.t > a.t ? Mathf.Clamp01((t - a.t) / (b.t - a.t)) : 0f;
            pos = Vector3.Lerp(new Vector3(a.x, a.y, a.z), new Vector3(b.x, b.y, b.z), k);
            inView = a.inView;
            return true;
        }

        static Color ClassColour(string truth)
        {
            switch (truth)
            {
                case "HostileQuad":
                case "HostileFixedWing": return new Color(1f, 0.3f, 0.25f);
                case "FriendlyISR": return new Color(0.3f, 0.55f, 1f);
                case "CivilianQuad": return new Color(1f, 0.9f, 0.2f);
                default: return new Color(0.75f, 0.75f, 0.75f);
            }
        }

        // The flowchart tree. One button shows or hides all of it; each checkpoint inside folds with a click.
        float TreeSection(float x, float y, float w, PlayerEntry player)
        {
            C.SectionTitle(new Rect(x, y, w - 150f, 26f), "DECISION TREE");
            if (GUI.Button(new Rect(x + w - 140f, y, 140f, 24f), Tree.hidden ? "Show tree" : "Hide tree")) Tree.hidden = !Tree.hidden;
            y += 32f;
            if (Tree.hidden) return y + 4f;
            return DecisionTreeView.Draw(x, y, w, player != null ? player.current : null, Tree, fontSize);
        }

        float ContactList(float x, float y, float w, PlayerEntry player)
        {
            C.SectionTitle(new Rect(x, y, w, 26f), "EVERY CONTACT");
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
                    + (c.decisionLatency >= 0f ? $"   call to action {c.decisionLatency:F1} s" : "")
                    + (c.callChanges + c.actionChanges > 0 ? $"   changed {c.callChanges + c.actionChanges}x" : "")
                    + (c.group ? "   group" : "")
                    + (c.n1aSeconds >= 0f ? $"   N1a {c.n1aSeconds:F1} s" : "")
                    + (c.n1bSeconds >= 0f ? $"   N1b {c.n1bSeconds:F1} s ({c.perceptionBand})" : "")
                    + (c.deadline >= 0f ? $"   clock {c.clockOpen:F0}-{c.deadline:F0} s" : "")
                    + (c.clockMiss ? "   MISS" : c.correctRejection ? "   correct rejection" : "")
                    + (c.unverified ? "   UNVERIFIED" : "")
                    + (!string.IsNullOrEmpty(c.missKind) ? "   " + c.missKind.Split(':')[0] : "")
                    + (!string.IsNullOrEmpty(c.processSeverity) ? $"   process {c.processSeverity}, {c.outcome}" : "");
                GUI.Label(new Rect(x, y, w * 0.34f, 20f), $"<b>#{c.id:00} {c.truthClass}</b>{(c.deceptive ? " (blend-in)" : "")}   = {(c.scored ? c.total.ToString() : "not scored")}", C.Label);
                GUI.Label(new Rect(x + w * 0.34f, y, w * 0.66f - 64f, 20f), extra, C.Small);
                // Light this contact's path in the tree above.
                if (GUI.Button(new Rect(x + w - 60f, y, 60f, 20f), Tree.focus == i ? "in tree" : "tree")) { Tree.focus = i; Tree.hidden = false; }
                C.TreePath(new Rect(x, y + 22f, w, 40f), c);
                y += 70f;
                if (!string.IsNullOrEmpty(c.soundCall))
                {
                    string tone = ColorUtility.ToHtmlStringRGB(c.matchedSound ? C.Good : C.Warn);
                    GUI.Label(new Rect(x, y - 8f, w, 20f), $"Your call: {c.yourCall}    <color=#{tone}>Sound call: {c.soundCall}</color>", C.Small);
                    GUI.Label(new Rect(x, y + 10f, w, 20f), "Consequence: " + c.consequence, C.Small);
                    y += 34f;
                }
            }
            if (rows < s.contacts.Count) { GUI.Label(new Rect(x, y, w, 20f), $"First {rows} of {s.contacts.Count} contacts shown.", C.Small); y += 24f; }
            return y + 6f;
        }

        float Trends(float x, float y, float w, PlayerEntry player, bool sideBySide)
        {
            int real = player != null ? player.history.Count : 0;
            C.SectionTitle(new Rect(x, y, w, 26f), $"TRENDS: {real} saved session{(real == 1 ? "" : "s")}");
            y += 32f;
            var hist = player != null ? player.history : null;
            if (trendModels == null || real != trendModelsFor)
            {
                trendModelsFor = real;
                trendModels = new[] { ChartModels.ScoreTrend(hist), ChartModels.DetectionTrend(hist), ChartModels.AccuracyTrend(hist) };
            }
            float gap = 8f, h = 250f;
            float cw = sideBySide && w >= 900f ? (w - gap * 2f) / 3f : sideBySide && w >= 560f ? (w - gap) / 2f : w;
            int cols = Mathf.Max(1, Mathf.FloorToInt((w + gap) / (cw + gap)));
            for (int i = 0; i < 3; i++) ChartKit.Draw(new Rect(x + (i % cols) * (cw + gap), y + (i / cols) * (h + gap), cw, h), trendModels[i]);
            return y + Mathf.CeilToInt(3f / cols) * (h + gap) + 8f;
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
