using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XCharts.Runtime;

namespace Chaukanna.Charts
{
    // The score dashboard's charts, drawn with XCharts (MIT, github.com/XCharts-Team/XCharts).
    //
    // This is the ONLY code that touches XCharts, and it lives in its own assembly. The rest of the project never
    // references it: the dashboard finds this component by name, and talks to it with SendMessage and a JSON string.
    // So if XCharts is removed, delete this Charts folder too and the dashboard falls back to its built-in drawing.
    public class XChartsPanel : MonoBehaviour
    {
        // Mirror of the JSON the dashboard sends (see ChartPayload in Scripts/Trainer/DashboardData.cs).
        [Serializable] public class Named { public string name; public float value; }
        [Serializable] public class SessionPoint { public string label; public float scorePercent, detectionSeconds, classificationAccuracy, engagementScore; public bool sample; }
        [Serializable] public class ClassRow { public string name; public int right, wrong, noCall; }
        [Serializable]
        public class Payload
        {
            public string playerName = "";
            public List<SessionPoint> sessions = new List<SessionPoint>();
            public List<Named> counts = new List<Named>();
            public List<ClassRow> classes = new List<ClassRow>();
            public List<Named> detectionBins = new List<Named>();
            public List<Named> nodePoints = new List<Named>();
        }

        Canvas canvas;
        Camera chartCamera;
        RenderTexture target;
        RectTransform panel;
        LineChart scoreChart, detectionChart, accuracyChart;
        BarChart countsChart, classChart, binsChart;
        Vector4 rect;
        bool built;

        // The charts are drawn by their own camera into a texture, and the dashboard paints that texture like any other
        // picture. (A canvas drawn straight to the screen always ends up UNDER the trainer's immediate-mode panels,
        // which is why the charts could not be seen before.)
        public Texture Picture => target;

        void Build()
        {
            if (built) return;
            built = true;

            // Far below the world and looking at nothing but its own canvas, so no other camera sees the charts and
            // this camera sees nothing else.
            var cameraGo = new GameObject("ScoreChartsCamera");
            cameraGo.transform.SetParent(transform, false);
            cameraGo.transform.position = new Vector3(0f, -20000f, 0f);
            chartCamera = cameraGo.AddComponent<Camera>();
            chartCamera.orthographic = true;
            chartCamera.nearClipPlane = 0.1f;
            chartCamera.farClipPlane = 30f;
            chartCamera.clearFlags = CameraClearFlags.SolidColor;
            chartCamera.backgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
            chartCamera.depth = -50f;
            chartCamera.allowHDR = false;
            chartCamera.allowMSAA = false;

            var canvasGo = new GameObject("ScoreChartsCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = chartCamera;
            canvas.planeDistance = 10f;
            canvasGo.AddComponent<CanvasScaler>();

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            panelGo.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);
            panel = panelGo.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);     // measured from the top-left, like the dashboard
            panel.pivot = new Vector2(0f, 1f);

            scoreChart = Make<LineChart>("Score % by session");
            detectionChart = Make<LineChart>("Detection time (s) by session");
            accuracyChart = Make<LineChart>("Classification / engagement %");
            countsChart = Make<BarChart>("This run: what went wrong");
            classChart = Make<BarChart>("This run: calls by true class");
            binsChart = Make<BarChart>("This run: time to detect each contact");
            Layout();
        }

        void EnsureTarget()
        {
            int w = Mathf.Clamp(Mathf.RoundToInt(rect.z), 64, 8192), h = Mathf.Clamp(Mathf.RoundToInt(rect.w), 64, 8192);
            if (target != null && target.width == w && target.height == h) return;
            if (target != null) { chartCamera.targetTexture = null; target.Release(); Destroy(target); }
            target = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "ScoreCharts", antiAliasing = 4 };
            chartCamera.targetTexture = target;
        }

        void OnDestroy()
        {
            if (target != null) { if (chartCamera != null) chartCamera.targetTexture = null; target.Release(); Destroy(target); }
        }
        static Theme ownTheme;

        // The charts wear a private copy of the dark theme.
        // They must NOT call chart.UpdateTheme(Dark): that rewrites XCharts' shared Default theme asset in place, after
        // which XCharts cannot find a theme called "Default" any more and every chart made afterwards fails to build
        // (only the first chart of the first run appears, and none on later runs until Unity is restarted).
        static Theme OwnTheme()
        {
            // Put the shared theme right again if that has already happened in this Editor session.
            var shared = Resources.Load<Theme>("XCTheme-Default");
            if (shared != null && shared.themeType != ThemeType.Default)
            {
                Theme.ResetToDefaultTheme(shared);
                XCThemeMgr.ReloadThemeList();
            }
            if (ownTheme == null)
            {
                var source = XCThemeMgr.GetTheme(ThemeType.Default);
                ownTheme = source != null ? source.CloneTheme() : ScriptableObject.CreateInstance<Theme>();
                Theme.ResetToDarkTheme(ownTheme);
                ownTheme.name = "ChaukannaChartsTheme";
                ownTheme.hideFlags = HideFlags.DontSave;
            }
            return ownTheme;
        }

        T Make<T>(string title) where T : BaseChart
        {
            var go = new GameObject(title, typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            var own = OwnTheme();       // also makes sure XCharts can find its Default theme before the chart wakes up
            var chart = go.AddComponent<T>();
            chart.Init();
            chart.theme.sharedTheme = own;
            chart.EnsureChartComponent<Title>().text = title;
            chart.RemoveData();
            return chart;
        }

        // Three charts across, two down, filling the panel.
        void Layout()
        {
            if (!built) return;
            EnsureTarget();
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(rect.z, rect.w);
            BaseChart[] charts = { scoreChart, detectionChart, accuracyChart, countsChart, classChart, binsChart };
            int columns = rect.z >= 900f ? 3 : rect.z >= 560f ? 2 : 1;
            int rows = Mathf.CeilToInt(charts.Length / (float)columns);
            const float gap = 8f;
            float w = (rect.z - gap * (columns + 1)) / columns, h = (rect.w - gap * (rows + 1)) / rows;
            for (int i = 0; i < charts.Length; i++)
            {
                var rt = charts[i].GetComponent<RectTransform>();
                rt.anchoredPosition = new Vector2(gap + (i % columns) * (w + gap), -(gap + (i / columns) * (h + gap)));
                charts[i].SetSize(Mathf.Max(120f, w), Mathf.Max(90f, h));
            }
        }

        // --- Messages from the dashboard ---

        // Width and height (z, w) in screen pixels: the size of the picture. x and y are not used any more, the dashboard places the picture.
        public void SetRectPixels(Vector4 pixels)
        {
            if (pixels == rect && built) return;
            rect = pixels;
            Build();
            Layout();
        }

        public void SetVisible(bool visible)
        {
            if (visible) Build();
            if (canvas != null) canvas.enabled = visible;
            if (chartCamera != null) chartCamera.enabled = visible;      // nothing is rendered while the tab is closed
        }

        public void SetJson(string json)
        {
            Build();
            Payload p;
            try { p = JsonUtility.FromJson<Payload>(json); }
            catch (Exception e) { Debug.LogWarning("[Charts] Could not read chart data: " + e.Message); return; }
            if (p == null) return;

            // Trends over the last sessions. Labels ending in * are generated sample points, not real sessions.
            Fill(scoreChart, false, p.sessions.Count, i => p.sessions[i].label, new[] { "Score %" }, (s, i) => p.sessions[i].scorePercent);
            Fill(detectionChart, false, p.sessions.Count, i => p.sessions[i].label, new[] { "Seconds" }, (s, i) => p.sessions[i].detectionSeconds);
            Fill(accuracyChart, false, p.sessions.Count, i => p.sessions[i].label, new[] { "Classification", "Engagement" },
                (s, i) => s == 0 ? p.sessions[i].classificationAccuracy : p.sessions[i].engagementScore);

            // This run.
            Fill(countsChart, true, p.counts.Count, i => p.counts[i].name, new[] { "Count" }, (s, i) => p.counts[i].value);
            Fill(classChart, true, p.classes.Count, i => p.classes[i].name, new[] { "Right", "Wrong", "No call" },
                (s, i) => s == 0 ? p.classes[i].right : s == 1 ? p.classes[i].wrong : p.classes[i].noCall);
            Fill(binsChart, true, p.detectionBins.Count, i => p.detectionBins[i].name, new[] { "Contacts" }, (s, i) => p.detectionBins[i].value);
        }

        static void Fill(BaseChart chart, bool bars, int count, Func<int, string> label, string[] series, Func<int, int, float> value)
        {
            chart.RemoveData();
            for (int s = 0; s < series.Length; s++)
            {
                if (bars) chart.AddSerie<Bar>(series[s]); else chart.AddSerie<Line>(series[s]);
            }
            for (int i = 0; i < count; i++)
            {
                chart.AddXAxisData(label(i));
                for (int s = 0; s < series.Length; s++) chart.AddData(s, value(s, i));
            }
            chart.RefreshChart();
        }
    }
}
