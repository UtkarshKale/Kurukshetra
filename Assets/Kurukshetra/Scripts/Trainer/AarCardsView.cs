using System.Collections.Generic;
using Kurukshetra.Scoring;
using UnityEngine;
using C = Kurukshetra.Trainer.DashboardCharts;

namespace Kurukshetra.Trainer
{
    // Draws the AAR cards (Scoring/AarCards.cs) as a responsive grid with IMGUI rectangles. Lines go through LassoMath.Line,
    // which copes with the scaled GUI.matrix the dashboard uses. Rebuilds the card data twice a second, never per frame.
    public class AarCardsView
    {
        public const float CardGap = 10f;
        public const float NormalHeight = 220f, TallHeight = 290f;

        List<AarCard> cards = new List<AarCard>();
        float nextBuild;
        object builtFor;
        GUIStyle wrap, tiny;

        public IList<AarCard> Cards => cards;

        public static int ColumnsFor(float width) => width >= 1000f ? 3 : width >= 620f ? 2 : 1;

        public void Rebuild(SessionResult s, IList<TrendPoint> trend, LearnerProfile learner)
        {
            var weakest = learner != null && learner.observations.Count > 0 ? LearnerModel.Weakest(learner, 3) : null;
            cards = AarCards.Build(s, trend, weakest);
        }

        static Color StatusColour(CardStatus s) => s == CardStatus.Good ? C.Good : s == CardStatus.Warn ? C.Warn : s == CardStatus.Bad ? C.Bad : C.Neutral;

        void EnsureStyles()
        {
            if (wrap == null)
            {
                wrap = new GUIStyle(C.Small) { wordWrap = true, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip };
                tiny = new GUIStyle(C.Small) { alignment = TextAnchor.MiddleLeft };
            }
            wrap.fontSize = C.Label.fontSize - 1;
            wrap.normal.textColor = C.Text;
            tiny.fontSize = C.Small.fontSize;
            tiny.normal.textColor = C.Dim;
        }

        // Draws at (x, y) with width w; returns the y below the grid.
        public float Draw(float x, float y, float w, SessionResult s, IList<TrendPoint> trend, LearnerProfile learner)
        {
            EnsureStyles();
            if (s == null)
            {
                GUI.Label(new Rect(x, y, w, 24f), "No run data yet. Press START on the trainer panel.", C.Label);
                return y + 34f;
            }
            if (!ReferenceEquals(builtFor, s) || Time.unscaledTime >= nextBuild)
            {
                builtFor = s;
                nextBuild = Time.unscaledTime + 0.5f;
                Rebuild(s, trend, learner);
            }

            int cols = ColumnsFor(w);
            float cw = (w - CardGap * (cols - 1)) / cols;
            int i = 0;
            while (i < cards.Count)
            {
                int n = Mathf.Min(cols, cards.Count - i);
                float rowH = NormalHeight;
                for (int k = 0; k < n; k++) if (cards[i + k].tall) rowH = TallHeight;
                for (int k = 0; k < n; k++) DrawCard(new Rect(x + k * (cw + CardGap), y, cw, rowH), cards[i + k]);
                y += rowH + CardGap;
                i += n;
            }
            return y;
        }

        void DrawCard(Rect r, AarCard card)
        {
            Color tone = StatusColour(card.status);
            C.Fill(r, new Color(tone.r, tone.g, tone.b, 0.35f));                 // border
            C.Fill(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, r.height - 2f), new Color(0.085f, 0.11f, 0.15f, 1f));
            C.Fill(new Rect(r.x + 1f, r.y + 1f, r.width - 2f, 3f), tone);        // status stripe

            float p = 10f, inner = r.width - p * 2f;
            C.Fill(new Rect(r.x + p, r.y + 12f, 8f, 8f), tone);
            GUI.Label(new Rect(r.x + p + 14f, r.y + 7f, inner - 90f, 18f), card.title, tiny);
            var word = new GUIStyle(tiny) { alignment = TextAnchor.MiddleRight, fontStyle = FontStyle.Bold };
            word.normal.textColor = tone;
            GUI.Label(new Rect(r.xMax - p - 80f, r.y + 7f, 80f, 18f), card.StatusWord, word);

            var oldBig = C.Big.normal.textColor;
            C.Big.normal.textColor = card.status == CardStatus.Neutral ? C.Dim : tone;
            GUI.Label(new Rect(r.x + p, r.y + 26f, inner, 38f), card.hero, C.Big);
            C.Big.normal.textColor = oldBig;

            GUI.Label(new Rect(r.x + p, r.y + 64f, inner, 36f), card.verdict, wrap);
            var chart = new Rect(r.x + p, r.y + 104f, inner, r.height - 104f - 22f);
            DrawChart(chart, card);
            GUI.Label(new Rect(r.x + p, r.yMax - 20f, inner, 18f), card.footer, tiny);
        }

        void DrawChart(Rect r, AarCard c)
        {
            switch (c.chart)
            {
                case CardChart.Bars: Bars(r, c); break;
                case CardChart.Histogram: if (c.model != null) ChartKit.Draw(r, c.model, true); else NoData(r, "no data"); break;
                case CardChart.Heatmap: if (c.session != null && c.session.confusion != null && c.session.confusionRows != null) C.Heatmap(r, c.session); else NoData(r, "nothing classified"); break;
                case CardChart.Stacked: Stacked(r, c); break;
                case CardChart.Grid: GridChart(r, c); break;
                case CardChart.Sliders: Sliders(r, c); break;
                case CardChart.Ticks: Ticks(r, c); break;
                case CardChart.Timeline: TimelineChart(r, c); break;
                case CardChart.Line: if (c.model != null) ChartKit.Draw(r, c.model, true); else NoData(r, "no data"); break;
                case CardChart.CardChange: CardChangeChart(r, c); break;
            }
        }

        void NoData(Rect r, string text)
        {
            var centre = new GUIStyle(tiny) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(r, text, centre);
        }

        void Bars(Rect r, AarCard c)
        {
            if (c.values == null || c.values.Length == 0) { NoData(r, "no data"); return; }
            int n = c.values.Length;
            float rowH = Mathf.Min(26f, r.height / n), labelW = r.width * 0.36f, valueW = 64f, barW = r.width - labelW - valueW - 8f;
            for (int i = 0; i < n; i++)
            {
                float y = r.y + i * rowH;
                GUI.Label(new Rect(r.x, y, labelW, rowH), c.labels[i], tiny);
                C.Fill(new Rect(r.x + labelW, y + 4f, barW, rowH - 8f), C.Panel);
                float f = Mathf.Clamp01(c.values[i] / Mathf.Max(0.0001f, c.max));
                C.Fill(new Rect(r.x + labelW, y + 4f, barW * f, rowH - 8f), StatusColour(c.barStatus != null ? c.barStatus[i] : CardStatus.Neutral));
                GUI.Label(new Rect(r.x + labelW + barW + 6f, y, valueW, rowH), c.valueText != null ? c.valueText[i] : c.values[i].ToString("F0"), C.Small);
            }
        }

        void Stacked(Rect r, AarCard c)
        {
            if (c.labels == null || c.labels.Length == 0) { NoData(r, "no decisions"); return; }
            int n = c.labels.Length, mx = 1;
            for (int i = 0; i < n; i++) mx = Mathf.Max(mx, c.right[i] + c.wrong[i]);
            float rowH = Mathf.Min(30f, r.height / n), labelW = 60f, barW = r.width - labelW - 110f;
            for (int i = 0; i < n; i++)
            {
                float y = r.y + i * rowH, wr = barW * c.right[i] / mx, ww = barW * c.wrong[i] / mx;
                GUI.Label(new Rect(r.x, y, labelW, rowH), "Card " + c.labels[i], tiny);
                C.Fill(new Rect(r.x + labelW, y + 4f, wr, rowH - 8f), C.Good);
                C.Fill(new Rect(r.x + labelW + wr, y + 4f, ww, rowH - 8f), C.Bad);
                GUI.Label(new Rect(r.x + labelW + wr + ww + 6f, y, 120f, rowH), $"{c.right[i]} right, {c.wrong[i]} wrong", C.Small);
            }
        }

        void GridChart(Rect r, AarCard c)
        {
            if (c.counts == null) { NoData(r, "no data"); return; }
            float cw = (r.width - 6f) / 2f, ch = (r.height - 6f) / 2f;
            for (int i = 0; i < 4; i++)
            {
                var cell = new Rect(r.x + (i % 2) * (cw + 6f), r.y + (i / 2) * (ch + 6f), cw, ch);
                C.Fill(cell, C.Panel);
                C.Fill(new Rect(cell.x, cell.y, 4f, cell.height), StatusColour(c.barStatus[i]));
                GUI.Label(new Rect(cell.x + 10f, cell.y, 40f, cell.height), c.counts[i].ToString(), C.Big);
                GUI.Label(new Rect(cell.x + 50f, cell.y, cell.width - 54f, cell.height), c.labels[i], tiny);
            }
        }

        void Sliders(Rect r, AarCard c)
        {
            for (int i = 0; i < 2; i++)
            {
                var d = c.sdt[i];
                float y = r.y + i * (r.height * 0.5f);
                GUI.Label(new Rect(r.x, y, r.width, 16f), $"{c.labels[i]}  {d.hits}/{d.signals} hits, {d.falseAlarms}/{d.noise} false alarms", tiny);
                if (!d.gated) { GUI.Label(new Rect(r.x, y + 16f, r.width, 16f), "d' and c: insufficient data (needs 20 of each)", tiny); continue; }
                float barW = r.width - 110f;
                var bar = new Rect(r.x, y + 24f, barW, 8f);
                C.Fill(bar, C.Panel);
                C.Fill(new Rect(bar.center.x - 1f, bar.y - 3f, 2f, 14f), C.Dim);
                float t = Mathf.InverseLerp(-1.5f, 1.5f, d.c);
                C.Fill(new Rect(bar.x + t * bar.width - 4f, bar.y - 4f, 8f, 16f), Mathf.Abs(d.c) < 0.3f ? C.Good : C.Warn);
                GUI.Label(new Rect(r.x + barW + 6f, y + 18f, 104f, 20f), $"d' {d.dPrime:F2}  c {d.c:+0.00;-0.00;0.00}", C.Small);
            }
        }

        void Ticks(Rect r, AarCard c)
        {
            if (c.counts == null || c.counts.Length == 0) { NoData(r, "no clock started"); return; }
            int n = c.counts.Length, perRow = Mathf.Max(1, Mathf.Min(n, 20));
            float sz = Mathf.Min(16f, r.width / perRow - 3f);
            for (int i = 0; i < n; i++)
                C.Fill(new Rect(r.x + (i % perRow) * (sz + 3f), r.y + 2f + (i / perRow) * (sz + 4f), sz, sz), c.counts[i] == 1 ? C.Bad : c.counts[i] == 2 ? C.Neutral : C.Good);
            GUI.Label(new Rect(r.x, r.yMax - 14f, r.width, 14f), "green = in time, red = missed, grey = left alone correctly", tiny);
        }

        void TimelineChart(Rect r, AarCard c)
        {
            var s = c.session;
            if (s == null || s.contacts.Count == 0) { NoData(r, "no contacts"); return; }
            float dur = Mathf.Max(1f, s.duration);
            int rows = Mathf.Min(s.contacts.Count, 30);
            float rowH = Mathf.Clamp((r.height - 16f) / rows, 3f, 8f);
            for (int i = 0; i < rows; i++)
            {
                var k = s.contacts[i];
                float y = r.y + i * rowH;
                if (k.spawnTime < 0f) continue;
                float end = k.despawnTime >= 0f ? k.despawnTime : dur;
                float x0 = r.x + r.width * Mathf.Clamp01(k.spawnTime / dur), x1 = r.x + r.width * Mathf.Clamp01(end / dur);
                C.Fill(new Rect(x0, y, Mathf.Max(2f, x1 - x0), rowH - 1f), C.Neutral);
                if (k.firstInViewTime >= 0f)
                {
                    float v0 = r.x + r.width * Mathf.Clamp01(k.firstInViewTime / dur);
                    C.Fill(new Rect(v0, y, Mathf.Max(2f, x1 - v0), rowH - 1f), C.Accent);
                }
                if (k.actionTime >= 0f) C.Fill(new Rect(r.x + r.width * Mathf.Clamp01(k.actionTime / dur) - 1.5f, y, 3f, rowH - 1f), k.nodes.Count > 2 && k.nodes[2].passed ? C.Good : C.Bad);
            }
            GUI.Label(new Rect(r.x, r.yMax - 14f, r.width, 14f), "grey = in the air, blue = in view, bar = action", tiny);
        }

        void CardChangeChart(Rect r, AarCard c)
        {
            var cc = c.session != null ? c.session.cardChange : null;
            if (cc == null || !cc.happened) { NoData(r, "card did not change"); return; }
            float bw = Mathf.Min(110f, r.width * 0.3f), bh = 44f, y = r.y + 4f;
            var a = new Rect(r.x, y, bw, bh);
            var b = new Rect(r.xMax - bw, y, bw, bh);
            C.Fill(a, C.Panel);
            C.Fill(b, Color.Lerp(C.Panel, StatusColour(c.status), 0.5f));
            var centre = new GUIStyle(C.Big) { alignment = TextAnchor.MiddleCenter };
            centre.fontSize = C.Label.fontSize + 4;
            GUI.Label(a, "Card " + cc.from, centre);
            GUI.Label(b, "Card " + cc.to, centre);
            C.Fill(new Rect(a.xMax + 6f, y + bh * 0.5f - 1f, b.x - a.xMax - 12f, 2f), C.Accent);
            GUI.Label(new Rect(a.xMax, y - 2f, b.x - a.xMax, 16f), $"at {cc.time:F0} s", new GUIStyle(tiny) { alignment = TextAnchor.MiddleCenter });
            GUI.Label(new Rect(r.x, y + bh + 8f, r.width, 16f), cc.adaptationSeconds < 0f ? "no decision has used the new card yet" : $"first right decision under the new card after {cc.adaptationSeconds:F1} s", tiny);
            GUI.Label(new Rect(r.x, y + bh + 24f, r.width, 16f), $"{cc.staleErrors} decision(s) followed the old card", tiny);
        }
    }
}
