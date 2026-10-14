using System.Collections.Generic;
using Chaukanna.Scoring;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // One branch of a checkpoint: how many contacts took it, and whether the contact being looked at did.
    public class TreeNode
    {
        public string key;          // matches ContactResult.detBranch / classBranch / actBranch / outcomeBranch
        public string label;
        public int tone;            // 0 neutral, 1 good, 2 caution, 3 bad
        public int count;
        public float percent;       // of all contacts in the run
        public bool taken;          // by the focused contact (focus view only)
    }

    public class TreeStage
    {
        public string title;
        public int reached;         // contacts that got as far as this checkpoint
        public bool focusReached;
        public List<TreeNode> nodes = new List<TreeNode>();
    }

    // The decision tree as plain data, so it can be tested without drawing anything.
    // Checkpoints: DETECTED? > CLASSIFIED > ACTION > OUTCOME > DECISION vs RESULT, each with the branches a contact can take.
    public static class DecisionTreeData
    {
        public const int StageCount = 5;

        static readonly string[] Titles = { "DETECTED?", "CLASSIFIED", "ACTION", "OUTCOME", "DECISION vs RESULT" };
        static readonly string[][] Keys =
        {
            new[] { "Fast", "Medium", "Slow", "Missed", "Not detectable", "Not scored" },
            new[] { "Right", "Caught deception", "Second guess", "Near miss", "Wrong", "Deceived", "Changed to wrong", "No call" },
            new[] { "Hold", "Report", "Jam", "Engage", "Clock ran out", "Left alone", "No action" },
            new[] { "Right action", "Wrong action", "False engagement", "Unresolved" },
            new[] { "Sound, successful", "Sound, unlucky", "Lucky", "Unsound", "Pending", "No decision" },
        };
        static readonly string[][] Labels =
        {
            new[] { "Seen fast", "Seen, medium", "Seen late", "Never seen", "Could not be seen", "Not scored (late spawn)" },
            new[] { "Right call", "Caught the deception", "Right on a second guess", "Near miss", "Wrong call", "Deceived by a blend-in", "Changed right to wrong", "No call" },
            new[] { "Hold", "Report", "Jam", "Engage", "Clock ran out (miss)", "Left alone (correct rejection)", "No action" },
            new[] { "Right action", "Wrong action", "False engagement", "Unresolved" },
            new[] { "Sound call, good result", "Sound call, bad luck", "Unsound call, lucky", "Unsound call", "Result not known yet", "No decision" },
        };
        static readonly int[][] Tones =
        {
            new[] { 1, 2, 2, 3, 0, 0 },
            new[] { 1, 1, 2, 2, 3, 3, 3, 0 },
            new[] { 0, 0, 0, 0, 3, 1, 0 },
            new[] { 1, 3, 3, 0 },
            new[] { 1, 2, 2, 3, 0, 0 },
        };

        public static string Branch(ContactResult c, int stage)
        {
            switch (stage)
            {
                case 0: return c.detBranch;
                case 1: return c.classBranch;
                case 2: return c.actBranch;
                case 3: return c.outcomeBranch;
                default: return c.grid;   // process (judged on what the player believed) x outcome (what happened)
            }
        }

        // focus = null gives the whole run; otherwise the same counts with that contact's path marked.
        public static List<TreeStage> Build(SessionResult s, ContactResult focus = null)
        {
            var stages = new List<TreeStage>();
            int total = s != null ? s.contacts.Count : 0;
            for (int i = 0; i < StageCount; i++)
            {
                var stage = new TreeStage { title = Titles[i], focusReached = focus != null && !string.IsNullOrEmpty(Branch(focus, i)) };
                for (int k = 0; k < Keys[i].Length; k++)
                {
                    var node = new TreeNode { key = Keys[i][k], label = Labels[i][k], tone = Tones[i][k] };
                    if (s != null)
                        foreach (var c in s.contacts)
                            if (Branch(c, i) == node.key) node.count++;
                    node.percent = total > 0 ? 100f * node.count / total : 0f;
                    node.taken = focus != null && Branch(focus, i) == node.key;
                    stage.reached += node.count;
                    stage.nodes.Add(node);
                }
                stages.Add(stage);
            }
            return stages;
        }
    }

    // What the trainer has open in the tree. Kept by the dashboard.
    public class TreeViewState
    {
        public bool hidden;                                 // the single show / hide toggle
        public bool[] collapsed = new bool[DecisionTreeData.StageCount];
        public int focus = -1;                              // index into the session's contacts; -1 = all contacts
    }

    // Draws the tree as a flowchart: blue checkpoint bars joined by right-angled connectors, branches hanging off each
    // one, the path taken lit and the rest dim, every branch with its share of the run. A light slate panel, on purpose
    // different from the dark dashboard around it. Immediate-mode, rectangles only.
    public static class DecisionTreeView
    {
        static readonly Color Paper = new Color(0.80f, 0.82f, 0.85f, 1f);
        static readonly Color PaperLight = new Color(0.87f, 0.88f, 0.90f, 1f);
        static readonly Color Ink = new Color(0.08f, 0.10f, 0.14f, 1f);
        static readonly Color InkDim = new Color(0.36f, 0.40f, 0.46f, 1f);
        static readonly Color Blue = new Color(0.06f, 0.35f, 0.78f, 1f);
        static readonly Color BlueSoft = new Color(0.62f, 0.76f, 0.93f, 1f);
        static readonly Color NodeOff = new Color(0.70f, 0.72f, 0.76f, 1f);
        static readonly Color LineOff = new Color(0.45f, 0.49f, 0.55f, 0.7f);
        static readonly Color[] ToneColours =
        {
            new Color(0.45f, 0.49f, 0.55f), new Color(0.15f, 0.62f, 0.30f), new Color(0.85f, 0.58f, 0.10f), new Color(0.82f, 0.22f, 0.20f),
        };

        static GUIStyle title, thin, dark, darkDim, white, percent;

        static void EnsureStyles(int fontSize)
        {
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip, richText = true };
                thin = new GUIStyle(title);
                dark = new GUIStyle(title);
                darkDim = new GUIStyle(title);
                white = new GUIStyle(title) { fontStyle = FontStyle.Bold };
                percent = new GUIStyle(title) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            title.fontSize = fontSize + 13;
            thin.fontSize = fontSize - 2;
            dark.fontSize = darkDim.fontSize = fontSize - 3;
            white.fontSize = fontSize - 1;
            percent.fontSize = fontSize - 3;
            title.normal.textColor = dark.normal.textColor = Ink;
            thin.normal.textColor = darkDim.normal.textColor = InkDim;
            white.normal.textColor = Color.white;
            percent.normal.textColor = Blue;
        }

        const float HeadH = 30f, NodeH = 22f, NodeGap = 4f, BlockGap = 26f, Top = 78f, Pad = 12f;

        // Draws at (x, y) with width w and returns the y below it.
        public static float Draw(float x, float y, float w, SessionResult s, TreeViewState state, int fontSize)
        {
            EnsureStyles(fontSize);
            int count = s != null ? s.contacts.Count : 0;
            state.focus = Mathf.Clamp(state.focus, -1, count - 1);
            var focus = state.focus >= 0 ? s.contacts[state.focus] : null;
            var stages = DecisionTreeData.Build(s, focus);

            // Checkpoints sit side by side when there is room, and wrap onto more rows when there is not.
            int perRow = Mathf.Clamp(Mathf.FloorToInt((w - Pad * 2f + BlockGap) / (230f + BlockGap)), 1, DecisionTreeData.StageCount);
            float blockW = (w - Pad * 2f - BlockGap * (perRow - 1)) / perRow;
            int rows = Mathf.CeilToInt(DecisionTreeData.StageCount / (float)perRow);
            var rowH = new float[rows];
            for (int i = 0; i < stages.Count; i++)
                rowH[i / perRow] = Mathf.Max(rowH[i / perRow], BlockHeight(stages[i], state.collapsed[i]));
            float blocksH = 0f;
            foreach (float h in rowH) blocksH += h + 18f;
            int historyLines = focus != null ? Mathf.Min(focus.history.Count, 10) : 0;
            float historyH = focus != null ? 26f + Mathf.Max(1, historyLines) * 17f + 8f : 0f;
            float height = Top + blocksH + historyH + Pad;

            var panel = new Rect(x, y, w, height);
            Fill(panel, Paper);
            Decorate(panel);

            // Heading, in the style of a chapter title: a thin rule, a small caption above it and the big name below.
            int resolved = 0;
            if (s != null) foreach (var c in s.contacts) if (!c.unresolved) resolved++;
            float done = count > 0 ? 100f * resolved / count : 0f;
            GUI.Label(new Rect(x + Pad, y + 6f, w * 0.6f, 18f), $"<b><color=#14181F>{done:F0}%</color></b> RESOLVED     {count} CONTACT{(count == 1 ? "" : "S")}", thin);
            Fill(new Rect(x + Pad, y + 25f, Mathf.Min(360f, w * 0.5f), 2f), Ink);
            GUI.Label(new Rect(x + Pad, y + 28f, w * 0.55f, 40f), focus == null ? "ALL CONTACTS" : $"CONTACT #{focus.id:00}", title);

            // Whose path is lit: every contact together, or one of them.
            float bx = x + w - Pad;
            if (GUI.Button(new Rect(bx - 34f, y + 34f, 34f, 26f), ">")) state.focus = state.focus + 1 >= count ? -1 : state.focus + 1;
            if (GUI.Button(new Rect(bx - 72f, y + 34f, 34f, 26f), "<")) state.focus = state.focus - 1 < -1 ? count - 1 : state.focus - 1;
            if (GUI.Button(new Rect(bx - 130f, y + 34f, 54f, 26f), "ALL")) state.focus = -1;
            string who = focus == null ? "path share of the whole run" : focus.truthClass + (focus.deceptive ? "  (blend-in)" : "") + (focus.group ? "  [group]" : "") + $"   = {(focus.scored ? focus.total.ToString() : "not scored")}";
            thin.alignment = TextAnchor.MiddleRight;
            GUI.Label(new Rect(x + Pad, y + 8f, w - Pad * 2f, 20f), who, thin);
            thin.alignment = TextAnchor.MiddleLeft;

            if (count == 0)
            {
                GUI.Label(new Rect(x + Pad, y + Top, w - Pad * 2f, 22f), "No contacts yet. The tree fills in as the run goes on.", dark);
                return y + height + 12f;
            }

            float rowY = y + Top;
            for (int i = 0; i < stages.Count; i++)
            {
                int col = i % perRow, row = i / perRow;
                if (col == 0 && i > 0) rowY += rowH[row - 1] + 18f;
                float left = x + Pad + col * (blockW + BlockGap);
                bool lit = focus == null ? stages[i].reached > 0 : stages[i].focusReached;
                Block(new Rect(left, rowY, blockW, rowH[row]), stages[i], i, state, focus != null, lit);

                // Connector to the next checkpoint: straight across on the same row, down and back on a wrap.
                if (i + 1 < stages.Count)
                {
                    bool next = focus == null ? stages[i + 1].reached > 0 : stages[i + 1].focusReached;
                    Color c = next ? Blue : LineOff;
                    float cy = rowY + HeadH * 0.5f;
                    if ((i + 1) % perRow != 0)
                    {
                        Fill(new Rect(left + blockW, cy - 1f, BlockGap, 2f), c);
                        Fill(new Rect(left + blockW + BlockGap - 6f, cy - 3f, 6f, 6f), c);
                    }
                    else
                    {
                        float downTo = rowY + rowH[row] + 18f + HeadH * 0.5f;
                        Fill(new Rect(left + blockW, cy - 1f, 8f, 2f), c);
                        Fill(new Rect(left + blockW + 6f, cy, 2f, rowH[row] + 9f - HeadH * 0.5f), c);
                        Fill(new Rect(x + Pad - 8f, rowY + rowH[row] + 8f, left + blockW + 16f - (x + Pad), 2f), c);
                        Fill(new Rect(x + Pad - 8f, rowY + rowH[row] + 8f, 2f, downTo - (rowY + rowH[row] + 8f)), c);
                        Fill(new Rect(x + Pad - 8f, downTo - 1f, 8f, 2f), c);
                    }
                }
            }
            rowY += rowH[rows - 1] + 18f;

            // One contact: everything it was called and everything done to it, in order.
            if (focus != null)
            {
                Fill(new Rect(x + Pad, rowY, w - Pad * 2f, 1f), LineOff);
                GUI.Label(new Rect(x + Pad, rowY + 4f, w - Pad * 2f, 20f), "<b>DECISION HISTORY</b>" + (focus.callChanges + focus.actionChanges > 0 ? $"     {focus.callChanges} call change{(focus.callChanges == 1 ? "" : "s")}, {focus.actionChanges} action change{(focus.actionChanges == 1 ? "" : "s")}" + (focus.changePenalty != 0 ? $", {focus.changePenalty} for second-guessing" : "") : ""), dark);
                float hy = rowY + 26f;
                if (focus.history.Count == 0) GUI.Label(new Rect(x + Pad, hy, w - Pad * 2f, 17f), "No call and no action on this contact.", darkDim);
                for (int i = focus.history.Count - historyLines; i < focus.history.Count; i++, hy += 17f)
                    GUI.Label(new Rect(x + Pad, hy, w - Pad * 2f, 17f), focus.history[i], focus.history[i].Contains("ANOMALY") ? dark : darkDim);
            }
            return y + height + 12f;
        }

        static float BlockHeight(TreeStage stage, bool collapsed)
        {
            return HeadH + 16f + (collapsed ? 0f : stage.nodes.Count * (NodeH + NodeGap) + 4f);
        }

        static void Block(Rect r, TreeStage stage, int index, TreeViewState state, bool focusView, bool lit)
        {
            bool collapsed = state.collapsed[index];

            // The checkpoint bar. One click folds its branches away or brings them back.
            var head = new Rect(r.x, r.y, r.width, HeadH);
            Fill(new Rect(head.x + 3f, head.y + 3f, head.width, head.height), new Color(0f, 0f, 0f, 0.18f));     // soft shadow
            Fill(head, lit ? Blue : new Color(0.33f, 0.40f, 0.50f));
            Icon(new Rect(head.x + 8f, head.y + 8f, 14f, 14f), index, Color.white);
            GUI.Label(new Rect(head.x + 30f, head.y, head.width - 60f, HeadH), stage.title, white);
            GUI.Label(new Rect(head.xMax - 26f, head.y, 22f, HeadH), collapsed ? "+" : "-", white);
            if (GUI.Button(head, GUIContent.none, GUIStyle.none)) state.collapsed[index] = !collapsed;
            GUI.Label(new Rect(r.x, head.yMax + 1f, r.width, 14f), "CHECKPOINT" + (collapsed ? $"   {stage.nodes.Count} branches folded, click to open" : $"   reached by {stage.reached}"), darkDim);
            if (collapsed) return;

            // Branches hang off a trunk that drops from the bar, each joined by a right-angled stub.
            float trunkX = r.x + 9f, ny = head.yMax + 18f;
            float lastCentre = ny;
            for (int i = 0; i < stage.nodes.Count; i++, ny += NodeH + NodeGap)
            {
                var n = stage.nodes[i];
                bool on = focusView ? n.taken : n.count > 0;
                var box = new Rect(r.x + 24f, ny, r.width - 24f, NodeH);
                float cy = ny + NodeH * 0.5f;
                Fill(new Rect(trunkX, cy - 1f, 15f, on ? 2f : 1f), on ? Blue : LineOff);
                if (on) lastCentre = cy;

                Fill(box, focusView && n.taken ? Blue : on ? BlueSoft : NodeOff);
                Fill(new Rect(box.x, box.y, 4f, box.height), ToneColours[n.tone]);
                // Share of the run as a strip along the bottom edge.
                if (n.count > 0) Fill(new Rect(box.x + 4f, box.yMax - 3f, (box.width - 50f) * Mathf.Clamp01(n.percent / 100f), 3f), focusView && n.taken ? Color.white : Blue);
                var text = focusView && n.taken ? white : on ? dark : darkDim;
                GUI.Label(new Rect(box.x + 10f, box.y - 1f, box.width - 62f, NodeH), n.label + (n.count > 0 ? $"  ({n.count})" : ""), text);
                var pct = new Rect(box.xMax - 46f, box.y + 2f, 44f, NodeH - 4f);
                Fill(pct, PaperLight);
                GUI.Label(pct, n.percent.ToString("F0") + "%", percent);
            }
            float trunkBottom = ny - NodeGap - NodeH * 0.5f;
            Fill(new Rect(trunkX, head.yMax, 1f, trunkBottom - head.yMax), LineOff);
            if (lit && lastCentre > head.yMax) Fill(new Rect(trunkX - 0.5f, head.yMax, 2f, lastCentre - head.yMax + 1f), Blue);
        }

        // A small mark per checkpoint, built from rectangles: eye frame, tag, arrow, target.
        static void Icon(Rect r, int kind, Color c)
        {
            switch (kind)
            {
                case 0:
                    Frame(r, c);
                    Fill(new Rect(r.center.x - 2f, r.center.y - 2f, 4f, 4f), c);
                    break;
                case 1:
                    Fill(new Rect(r.x, r.y + 2f, r.width, 3f), c);
                    Fill(new Rect(r.x, r.y + 7f, r.width * 0.7f, 3f), c);
                    Fill(new Rect(r.x, r.y + 12f, r.width * 0.4f, 2f), c);
                    break;
                case 2:
                    Fill(new Rect(r.x, r.center.y - 1.5f, r.width - 4f, 3f), c);
                    Fill(new Rect(r.xMax - 6f, r.center.y - 5f, 3f, 10f), c);
                    Fill(new Rect(r.xMax - 3f, r.center.y - 2.5f, 3f, 5f), c);
                    break;
                default:
                    Frame(r, c);
                    Fill(new Rect(r.x + 4f, r.y + 4f, r.width - 8f, r.height - 8f), c);
                    break;
            }
        }

        static void Frame(Rect r, Color c)
        {
            Fill(new Rect(r.x, r.y, r.width, 2f), c);
            Fill(new Rect(r.x, r.yMax - 2f, r.width, 2f), c);
            Fill(new Rect(r.x, r.y, 2f, r.height), c);
            Fill(new Rect(r.xMax - 2f, r.y, 2f, r.height), c);
        }

        // Quiet texture so the panel is not a flat slab: lighter bands and a few registration crosses.
        static void Decorate(Rect p)
        {
            Fill(new Rect(p.x, p.y, p.width, 70f), PaperLight);
            Fill(new Rect(p.x + p.width * 0.62f, p.y + 70f, p.width * 0.38f, p.height - 70f), new Color(1f, 1f, 1f, 0.07f));
            Fill(new Rect(p.x + p.width * 0.22f, p.y + 70f, p.width * 0.18f, p.height - 70f), new Color(0f, 0f, 0f, 0.035f));
            var cross = new Color(0.35f, 0.39f, 0.45f, 0.35f);
            for (int i = 0; i < 7; i++)
            {
                float cx = p.x + p.width * ((i * 0.37f + 0.11f) % 1f), cy = p.y + 80f + (p.height - 90f) * ((i * 0.53f + 0.23f) % 1f);
                Fill(new Rect(cx - 5f, cy, 11f, 1f), cross);
                Fill(new Rect(cx, cy - 5f, 1f, 11f), cross);
            }
        }

        static void Fill(Rect r, Color c)
        {
            DashboardCharts.Fill(r, c);
        }
    }
}
