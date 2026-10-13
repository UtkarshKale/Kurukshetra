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
        RectTransform panel;
        LineChart scoreChart, detectionChart, accuracyChart;
        BarChart countsChart, classChart, binsChart;
        Vector4 rect;
        bool built;

        void Build()
        {
            if (built) return;
            built = true;

            var canvasGo = new GameObject("ScoreChartsCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            // Charts show values on hover; that needs an event system, and this project uses the new Input System.
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            var panelGo = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvasGo.transform, false);
            panelGo.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 1f);
            panel = panelGo.GetComponent<RectTransform>();
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);     // measured from the top-left, like the dashboard
            panel.pivot = new Vector2(0f, 1f);

            scoreChart = Make<LineChart>("Score % by session");
            detectionChart = Make<LineChart>("Average detection time (s) by session");
            accuracyChart = Make<LineChart>("Classification accuracy and engagement decisions (%)");
            countsChart = Make<BarChart>("This run: what went wrong");
            classChart = Make<BarChart>("This run: calls by true class");
            binsChart = Make<BarChart>("This run: time to detect each contact");
            Layout();
        }

        T Make<T>(string title) where T : BaseChart
        {
            var go = new GameObject(title, typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            var chart = go.AddComponent<T>();
            chart.Init();
            chart.UpdateTheme(ThemeType.Dark);
            chart.EnsureChartComponent<Title>().text = title;
            chart.RemoveData();
            return chart;
        }

        // Three charts across, two down, filling the panel.
        void Layout()
        {
            if (!built) return;
            panel.anchoredPosition = new Vector2(rect.x, -rect.y);
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

        // x, y from the top-left of the window, then width and height, all in screen pixels.
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
