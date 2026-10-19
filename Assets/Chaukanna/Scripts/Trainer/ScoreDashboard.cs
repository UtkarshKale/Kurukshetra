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

        // The flowchart decision tree: which checkpoints are folded, whether it is shown at all, and whose path is lit.
        public readonly TreeViewState Tree = new TreeViewState();

        // Charts tab: drawn by the optional XCharts panel (Assets/Chaukanna/Charts), found by name so this script never
        // depends on it. Without it the tab says so and the built-in drawing on the Overview tab is still there.
        public bool ChartsActive { get; private set; }
        Component chartsPanel;
        bool chartsLookedFor;
        float nextChartSend;
        string lastChartJson = "";

        // Switch between the built-in Overview and the XCharts tab (also what the header button does).
        public void ShowCharts(bool on)
        {
            ChartsActive = on;
            nextChartSend = 0f;
            if (!on) SetChartsVisible(false);
        }

        Component ChartsPanel()
        {
            if (chartsPanel != null || chartsLookedFor) return chartsPanel;
            chartsLookedFor = true;
            var type = System.Type.GetType("Chaukanna.Charts.XChartsPanel, Chaukanna.Charts");
            if (type != null) chartsPanel = new GameObject("ScoreCharts").AddComponent(type);
            return chartsPanel;
        }

        void SetChartsVisible(bool visible)
        {
            if (chartsPanel != null) chartsPanel.SendMessage("SetVisible", visible, SendMessageOptions.DontRequireReceiver);

        }

        ScoreKeeper keeper;
        string reportNote = "";
        float replayT;
        bool replayPlaying;

        void Start()
        {
            spawner = FindAnyObjectByType<ScenarioSpawner>();
            keeper = FindAnyObjectByType<ScoreKeeper>();
            trainer = GetComponent<TrainerScreen>();
            if (Players == null) Players = Chaukanna.Net.LanSession.Current == Chaukanna.Net.LanSession.Role.Host ? new NetPlayerSource(FindAnyObjectByType<ScoreKeeper>(), spawner) : (IPlayerSource)new LocalPlayerSource(FindAnyObjectByType<ScoreKeeper>(), spawner);
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
            SetChartsVisible(false);
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

            Rect closeR, expandR, chartsR, reportR, noteR, titleR;
            if (narrow)
            {
                float gap = 6f, bw = (region.width - pad * 2f - gap * 3f) / 4f;
                titleR = new Rect(pad, 4f, region.width - pad * 2f, 30f);
                reportR = new Rect(pad, 40f, bw, 30f);
                chartsR = new Rect(pad + (bw + gap), 40f, bw, 30f);
                expandR = new Rect(pad + (bw + gap) * 2f, 40f, bw, 30f);
                closeR = new Rect(pad + (bw + gap) * 3f, 40f, bw, 30f);
                float tw = C.Header.CalcSize(new GUIContent("SCORE DASHBOARD")).x + 12f;
                noteR = new Rect(pad + tw, 8f, Mathf.Max(0f, region.width - pad * 2f - tw), 22f);
                titleR.width = tw;
            }
            else
            {
                titleR = new Rect(pad, 4f, region.width - 260f, headH - 8f);
                reportR = new Rect(region.width - 492f, 6f, 120f, headH - 12f);
                chartsR = new Rect(region.width - 364f, 6f, 120f, headH - 12f);
                expandR = new Rect(region.width - 236f, 6f, 120f, headH - 12f);
                closeR = new Rect(region.width - 108f, 6f, 96f, headH - 12f);
                noteR = new Rect(pad + 250f, 10f, Mathf.Max(0f, region.width - 760f), 20f);
            }
            GUI.Label(titleR, "SCORE DASHBOARD", C.Header);
            if (GUI.Button(expandR, Expanded ? "Collapse" : "Expand")) SetExpanded(!Expanded);
            if (GUI.Button(closeR, "Close")) Close();
            if (keeper != null && GUI.Button(reportR, "HTML report"))
            {
                // The whole AAR as one file that opens in any browser; written at the end of every run, or now as a snapshot.
                string path = keeper.ExportReport();
                reportNote = path != null ? "Report: " + path : "No run to report yet.";
                if (path != null) Application.OpenURL("file:///" + path.Replace('\\', '/'));
            }
            if (reportNote.Length > 0) GUI.Label(noteR, reportNote, C.Small);
            if (GUI.Button(chartsR, ChartsActive ? "Overview" : "Charts"))
            {
                ChartsActive = !ChartsActive;
                nextChartSend = 0f;
                if (!ChartsActive) SetChartsVisible(false);
            }
            C.Fill(new Rect(0f, headH, region.width, 1f), new Color(1f, 1f, 1f, 0.15f));

            if (ChartsActive)
            {
                var panel = ChartsPanel();
                if (panel == null)
                {
                    C.Fill(new Rect(0f, headH + 2f, region.width, region.height - headH - 2f), C.Background);
                    GUI.Label(new Rect(pad, headH + 12f, region.width - pad * 2f, 60f), "The XCharts panel is not available (Assets/Chaukanna/Charts or XCharts is missing). The Overview tab has the built-in charts.", C.Label);
                }
                else if (Event.current.type == EventType.Repaint)
                {
                    // The panel renders the charts into a texture of exactly this size; it is painted here like any picture.
                    var area = new Rect(0f, headH + 2f, region.width, region.height - headH - 2f);
                    panel.SendMessage("SetRectPixels", new Vector4(0f, 0f, area.width * ui, area.height * ui));
                    SetChartsVisible(true);
                    var chartCamera = panel.GetComponentInChildren<Camera>(true);
                    if (chartCamera != null && chartCamera.targetTexture != null) GUI.DrawTexture(area, chartCamera.targetTexture, ScaleMode.StretchToFill, false);
                    if (Time.unscaledTime >= nextChartSend)
                    {
                        // Refresh every two seconds, and only when something changed.
                        nextChartSend = Time.unscaledTime + 2f;
                        selected = Mathf.Clamp(selected, 0, Mathf.Max(0, Players.Count - 1));
                        var who = Players.Count > 0 ? Players.Get(selected) : null;
                        var history = TrendData.PadWithSamples(who != null ? who.history : new List<TrendPoint>(), TrendData.ChartSessions);
                        string json = JsonUtility.ToJson(ChartPayload.Build(who != null ? who.name : "", who != null ? who.current : null, history));
                        if (json != lastChartJson) { lastChartJson = json; panel.SendMessage("SetJson", json); }
                    }
                }
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
            if (w >= 900f)
            {
                // Wide: overview on the left, per-contact detail on the right.
                float leftW = w * 0.42f, rightX = pad + leftW + pad, rightW = w - leftW - pad;
                float y = Roster(pad, top, leftW);
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
                if (GUI.Button(row, GUIContent.none, GUIStyle.none)) selected = i;
                string score = p.current != null ? $"Score {p.current.totalPoints} / {p.current.maxPoints}" : "no score yet";
                string role = string.IsNullOrEmpty(p.role) ? "" : $"  <color=#FFD060>{p.role}</color>";
                GUI.Label(new Rect(x + 10f, y, w * 0.34f, 34f), "<b>" + p.name + "</b>" + role, C.Label);
                GUI.Label(new Rect(x + w * 0.36f, y, w * 0.3f, 34f), p.state, C.Label);
                GUI.Label(new Rect(x + w * 0.66f, y, w * 0.34f - 8f, 34f), score, C.Label);
                if (extra.Length > 0) GUI.Label(new Rect(x + 10f, y + 30f, w - 20f, 20f), extra, C.Small);
                y += rowH + 4f;
            }
            GUI.Label(new Rect(x, y, w, 20f), "Click a player to see their stats. More players appear here with multiplayer.", C.Small);
            return y + 30f;
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
            C.Tile(new Rect(x, y, tileW, th), s.processScore < 0f ? "-" : s.processScore.ToString("F0") + " / 100", "Process score (on belief)", s.processScore < 0f ? C.Dim : Grade(s.processScore));

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
            GUI.Label(new Rect(x, y, w, 18f), "Process uses the player's own call, the card and the evidence they had; the result is what happened. Placeholder thresholds.", C.Small);
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
            GUI.Label(new Rect(x, y, w, 18f), "One logistic model (ability + task + class + light + range), pooled with the other soldiers on this PC. Placeholder priors until there is data.", C.Small);
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
            if (Event.current.type == EventType.Repaint) C.Fill(map, new Color(0.06f, 0.08f, 0.11f, 1f));
            float radius = Mathf.Max(100f, RunLog.spawnRadius), scale = size * 0.5f / (radius * 1.1f);
            Vector2 centre = map.center;
            Quaternion turn = Quaternion.Euler(0f, -RunLog.sectorYaw, 0f);
            System.Func<float, float, Vector2> toMap = (wx, wz) =>
            {
                Vector3 d = turn * (new Vector3(wx, 0f, wz) - RunLog.post);
                return centre + new Vector2(d.x, -d.z) * scale;
            };

            // Where the soldier was looking.
            ViewPoint view = default;
            bool haveView = false;
            foreach (var v in RunLog.view) { if (v.t <= replayT) { view = v; haveView = true; } else break; }
            if (haveView && Event.current.type == EventType.Repaint)
            {
                Vector2 eye = toMap(view.x, view.z);
                foreach (float side in new[] { -0.5f, 0.5f })
                {
                    float a = (view.yaw - RunLog.sectorYaw + side * view.fov) * Mathf.Deg2Rad;
                    Chaukanna.Player.LassoMath.Line(eye, eye + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius * scale, 1.5f, new Color(0.4f, 1f, 0.5f, 0.7f));
                }
            }
            if (Event.current.type == EventType.Repaint) C.Fill(new Rect(centre.x - 4f, centre.y - 4f, 8f, 8f), Color.white);

            // Contacts at this moment; a click picks the nearest.
            ContactResult focus = Tree.focus >= 0 && Tree.focus < s.contacts.Count ? s.contacts[Tree.focus] : null;
            int clicked = -1;
            float best = 14f;
            bool click = Event.current.type == EventType.MouseDown && map.Contains(Event.current.mousePosition);
            for (int i = 0; i < s.contacts.Count; i++)
            {
                var c = s.contacts[i];
                ContactRecord rec = null;
                foreach (var r in ContactRegistry.Records) if (r.id == c.id) { rec = r; break; }
                if (rec == null || !At(rec.track, replayT, out Vector3 at, out bool inView)) continue;
                Vector2 p = toMap(at.x, at.z);
                if (!map.Contains(p)) continue;
                if (Event.current.type == EventType.Repaint)
                {
                    if (inView) C.Fill(new Rect(p.x - 7f, p.y - 7f, 14f, 14f), new Color(0.35f, 1f, 0.45f, 0.45f));
                    C.Fill(new Rect(p.x - 3f, p.y - 3f, 6f, 6f), ClassColour(c.truthClass));
                    if (c == focus) { C.Fill(new Rect(p.x - 9f, p.y - 10f, 18f, 1.5f), Color.white); C.Fill(new Rect(p.x - 9f, p.y + 9f, 18f, 1.5f), Color.white); }
                    if (c.actionTime >= 0f && replayT >= c.actionTime) GUI.Label(new Rect(p.x + 7f, p.y - 16f, 90f, 16f), c.action, C.Small);
                }
                if (click) { float d = Vector2.Distance(p, Event.current.mousePosition); if (d < best) { best = d; clicked = i; } }
            }
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
            GUI.Label(new Rect(x, bottom + 2f, w, 18f), "Up = sector centre, white square = the post, green lines = the soldier's view, green halo = on the soldier's screen.", C.Small);
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
