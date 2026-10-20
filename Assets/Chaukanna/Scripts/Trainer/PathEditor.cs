using System;
using System.Collections.Generic;
using Chaukanna.Scenario;
using Chaukanna.Session;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // The 2D path editor, shared by the trainer menu's MAP tab and the in-game F6 map. It edits plain data (the scenario's list of
    // FlightPath, the spawn points, the player posts) through a View that turns world metres into map pixels and back, so the same
    // code works on both maps. Immediate-mode GUI only: nothing here makes a GameObject.
    //
    // Mouse: left-drag a waypoint, spawn point or post to move it. Left-click empty ground does what the tool says (add a waypoint to
    // the selected path, add a spawn point, place the selected player's post); left-click on a path's line in the PATH tool inserts a
    // waypoint between its two neighbours. Right-click a waypoint or spawn point (or press Delete with a waypoint selected) to remove it.
    public class PathEditor
    {
        public struct View
        {
            public Func<Vector2, Vector2> toScreen;      // world (east, north) metres -> GUI position
            public Func<Vector2, Vector2> toWorld;       // GUI position -> world metres
            public float pxPerMetre;
        }

        public enum Tool { Select, Path, Spawn, Post }
        enum Drag { None, Point, Spawn, Post }

        public List<FlightPath> Paths = new List<FlightPath>();
        public List<Vector2> SpawnPoints;                 // null = this map has none to edit
        public List<PlayerSlot> Players;                  // null = posts are not edited here
        public Func<Vector2, Vector2> Clamp = v => v;     // keeps a dropped point inside the terrain
        public Func<Vector2, float> Ground;               // ground height under a point, for the profile strip (null = flat)
        public Action PostsChanged;                       // a post was moved or placed (the menu regenerates its preview)
        public int SelectedPath = -1, SelectedPoint = -1, SelectedPlayer;
        public Tool CurrentTool = Tool.Select;
        public bool SpawnsChanged;                        // the caller copies SpawnPoints back where they live, then clears this

        GUIStyle label, small, button, field, tag;
        int fontSize = 15;
        Drag drag;
        int dragIndex;

        const float HitPx = 10f, SegmentPx = 7f, SnapPx = 14f;

        public void Bind(GUIStyle labelStyle, GUIStyle smallStyle, GUIStyle buttonStyle, GUIStyle fieldStyle, int size)
        {
            label = labelStyle; small = smallStyle; button = buttonStyle; field = fieldStyle; fontSize = size;
            if (tag == null) tag = new GUIStyle(smallStyle) { richText = false, clipping = TextClipping.Overflow };
            tag.fontSize = Mathf.Max(9, size - 3);
        }

        public bool Dragging => drag != Drag.None;      // a point is being dragged: the F6 map keeps its zoom still meanwhile

        public FlightPath Selected => Paths != null && SelectedPath >= 0 && SelectedPath < Paths.Count ? Paths[SelectedPath] : null;

        // --- Colours ---

        public static Color PathColour(PathUse use)
        {
            switch (use)
            {
                case PathUse.Swarm: return new Color(1f, 0.35f, 0.9f);
                case PathUse.Bird: return new Color(0.6f, 0.9f, 1f);
                case PathUse.Flock: return new Color(0.35f, 0.7f, 1f);
                default: return new Color(1f, 0.55f, 0.2f);
            }
        }

        // --- Editing actions (also callable from tests) ---

        public FlightPath AddPath(PathUse use)
        {
            int count = 0;
            foreach (var p in Paths) if (p.use == use) count++;
            var path = new FlightPath { use = use, name = FlightPath.UseLabel(use).Substring(0, 1) + FlightPath.UseLabel(use).Substring(1).ToLowerInvariant() + " path " + (count + 1) };
            Paths.Add(path);
            SelectedPath = Paths.Count - 1;
            SelectedPoint = -1;
            CurrentTool = Tool.Path;
            return path;
        }

        public static float DefaultAlt(PathUse use) => use == PathUse.Bird || use == PathUse.Flock ? FlightPath.DefaultBirdAlt : FlightPath.DefaultDroneAlt;

        public void AddWaypoint(Vector2 world)
        {
            var path = Selected ?? AddPath(PathUse.Drone);
            float alt = path.points.Count > 0 ? path.points[path.points.Count - 1].alt : DefaultAlt(path.use);
            path.points.Add(new Waypoint { x = world.x, z = world.y, alt = alt });
            SelectedPoint = path.points.Count - 1;
        }

        public void InsertWaypoint(int pathIndex, int at, Vector2 world)
        {
            var path = Paths[pathIndex];
            int n = path.points.Count;
            at = Mathf.Clamp(at, 0, n);
            var a = path.points[Mathf.Clamp(at - 1, 0, n - 1)];
            var b = path.points[Mathf.Clamp(at, 0, n - 1)];
            float total = Vector2.Distance(a.Xz, b.Xz), t = total > 0.01f ? Vector2.Distance(a.Xz, world) / total : 0.5f;
            path.points.Insert(at, new Waypoint { x = world.x, z = world.y, alt = Mathf.Lerp(a.alt, b.alt, t) });
            SelectedPath = pathIndex;
            SelectedPoint = at;
        }

        public void DeleteWaypoint(int pathIndex, int index)
        {
            var path = Paths[pathIndex];
            if (index < 0 || index >= path.points.Count) return;
            path.points.RemoveAt(index);
            if (SelectedPath == pathIndex) SelectedPoint = Mathf.Min(SelectedPoint, path.points.Count - 1);
            if (path.points.Count == 0) { Paths.RemoveAt(pathIndex); if (SelectedPath >= Paths.Count) SelectedPath = Paths.Count - 1; SelectedPoint = -1; }
        }

        // --- Hit testing (screen pixels) ---

        bool HitPoint(Vector2 m, View v, out int pathIndex, out int pointIndex)
        {
            pathIndex = pointIndex = -1;
            // The selected path is tested first, so its waypoint wins where two paths cross.
            for (int pass = 0; pass < 2 && pathIndex < 0; pass++)
            {
                float best = HitPx;
                for (int i = 0; i < Paths.Count; i++)
                {
                    if ((pass == 0) != (i == SelectedPath)) continue;
                    var pts = Paths[i].points;
                    for (int j = 0; j < pts.Count; j++)
                    {
                        float d = Vector2.Distance(v.toScreen(pts[j].Xz), m);
                        if (d <= best) { best = d; pathIndex = i; pointIndex = j; }
                    }
                }
            }
            return pathIndex >= 0;
        }

        bool HitSpawn(Vector2 m, View v, out int index)
        {
            index = -1;
            if (SpawnPoints == null) return false;
            float best = HitPx;
            for (int i = 0; i < SpawnPoints.Count; i++)
            {
                float d = Vector2.Distance(v.toScreen(SpawnPoints[i]), m);
                if (d <= best) { best = d; index = i; }
            }
            return index >= 0;
        }

        bool HitPost(Vector2 m, View v, out int index)
        {
            index = -1;
            if (Players == null) return false;
            float best = HitPx + 3f;
            for (int i = 0; i < Players.Count; i++)
            {
                float d = Vector2.Distance(v.toScreen(Players[i].post), m);
                if (d <= best) { best = d; index = i; }
            }
            return index >= 0;
        }

        // The path leg under the pointer: insertAt is the index a new waypoint would take; world is the point on the line.
        bool HitSegment(Vector2 m, View v, out int pathIndex, out int insertAt, out Vector2 world)
        {
            pathIndex = insertAt = -1; world = default;
            float best = SegmentPx;
            for (int i = 0; i < Paths.Count; i++)
            {
                var path = Paths[i];
                int n = path.points.Count;
                int legs = path.mode == PathMode.Loop && n > 2 ? n : n - 1;
                for (int j = 0; j < legs; j++)
                {
                    Vector2 a = v.toScreen(path.points[j].Xz), b = v.toScreen(path.points[(j + 1) % n].Xz);
                    Vector2 ab = b - a;
                    float len2 = ab.sqrMagnitude;
                    float t = len2 > 0.01f ? Mathf.Clamp01(Vector2.Dot(m - a, ab) / len2) : 0f;
                    float d = Vector2.Distance(a + ab * t, m);
                    if (d < best) { best = d; pathIndex = i; insertAt = j + 1; world = v.toWorld(a + ab * t); }
                }
            }
            return pathIndex >= 0;
        }

        // --- Mouse and keys. Call before drawing, with the map's rectangle in the same space as the View. ---

        public void Input(Rect map, View v)
        {
            if (Paths == null) return;
            var e = Event.current;
            if (SelectedPath >= Paths.Count) SelectedPath = Paths.Count - 1;
            if (Selected == null || SelectedPoint >= Selected.points.Count) SelectedPoint = -1;
            Vector2 m = e.mousePosition;

            if (e.type == EventType.MouseDown && map.Contains(m))
            {
                if (e.button == 1)
                {
                    if (HitPoint(m, v, out int rp, out int rj)) DeleteWaypoint(rp, rj);
                    else if (HitSpawn(m, v, out int rs)) { SpawnPoints.RemoveAt(rs); SpawnsChanged = true; }
                    e.Use();
                    return;
                }
                if (e.button != 0) return;
                if (HitPoint(m, v, out int pi, out int wi)) { SelectedPath = pi; SelectedPoint = wi; drag = Drag.Point; dragIndex = wi; e.Use(); return; }
                if (HitSpawn(m, v, out int si)) { drag = Drag.Spawn; dragIndex = si; e.Use(); return; }
                if (HitPost(m, v, out int pl)) { SelectedPlayer = pl; drag = Drag.Post; dragIndex = pl; e.Use(); return; }

                Vector2 world = Clamp(v.toWorld(m));
                bool onLeg = HitSegment(m, v, out int legPath, out int legAt, out Vector2 legWorld);
                switch (CurrentTool)
                {
                    case Tool.Path:
                        if (onLeg) InsertWaypoint(legPath, legAt, Clamp(legWorld));
                        else AddWaypoint(world);
                        break;
                    case Tool.Spawn:
                        if (SpawnPoints != null) { SpawnPoints.Add(world); SpawnsChanged = true; }
                        break;
                    case Tool.Post:
                        if (Players != null && SelectedPlayer >= 0 && SelectedPlayer < Players.Count) { Players[SelectedPlayer].post = world; PostsChanged?.Invoke(); }
                        break;
                    default:
                        if (onLeg) { SelectedPath = legPath; SelectedPoint = -1; }
                        else SelectedPoint = -1;
                        break;
                }
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && drag != Drag.None)
            {
                Vector2 world = Clamp(v.toWorld(m));
                if (drag == Drag.Point && Selected != null && dragIndex < Selected.points.Count)
                {
                    // The first waypoint is where contacts appear: it sticks to a spawn point it is dragged near.
                    if (dragIndex == 0 && SpawnPoints != null)
                        foreach (var s in SpawnPoints) if (Vector2.Distance(v.toScreen(s), m) <= SnapPx) { world = s; break; }
                    var w = Selected.points[dragIndex];
                    w.x = world.x; w.z = world.y;
                    Selected.points[dragIndex] = w;
                }
                else if (drag == Drag.Spawn && SpawnPoints != null && dragIndex < SpawnPoints.Count) { SpawnPoints[dragIndex] = world; SpawnsChanged = true; }
                else if (drag == Drag.Post && Players != null && dragIndex < Players.Count) Players[dragIndex].post = world;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && drag != Drag.None)
            {
                if (drag == Drag.Post) PostsChanged?.Invoke();
                drag = Drag.None;
                e.Use();
            }
            else if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete) && SelectedPoint >= 0 && Selected != null && GUIUtility.keyboardControl == 0 && !NumberEntry.AnyFocused)
            {
                DeleteWaypoint(SelectedPath, SelectedPoint);
                e.Use();
            }
        }

        // --- Drawing ---

        static void Line(Vector2 a, Vector2 b, float width, Color c, Rect clip) => GuiDraw.Line(a, b, width, c, clip);

        static void Square(Vector2 p, float size, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Tag(Vector2 at, string text, Color c)
        {
            var r = new Rect(at.x, at.y, 160f, 16f);
            tag.normal.textColor = new Color(0f, 0f, 0f, 0.9f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, tag);
            tag.normal.textColor = c;
            GUI.Label(r, text, tag);
        }

        // Paths (with the height of every waypoint written beside it), spawn points. `handles` = the editor is live: the selected
        // path is drawn heavier and its waypoints bigger.
        public void Draw(Rect map, View v, bool handles = true)
        {
            if (Event.current.type != EventType.Repaint || Paths == null) return;
            GUI.BeginGroup(map);
            var clip = new Rect(0f, 0f, map.width, map.height);
            Vector2 off = -map.position;
            for (int i = 0; i < Paths.Count; i++)
            {
                var path = Paths[i];
                int n = path.points.Count;
                bool sel = handles && i == SelectedPath;
                Color col = PathColour(path.use);
                Color line = sel ? col : new Color(col.r, col.g, col.b, 0.65f);
                float width = sel ? 3f : 2f;
                for (int j = 1; j < n; j++) Leg(path.points[j - 1].Xz, path.points[j].Xz, v, off, width, line, clip);
                if (path.mode == PathMode.Loop && n > 2) Leg(path.points[n - 1].Xz, path.points[0].Xz, v, off, 1.5f, new Color(col.r, col.g, col.b, 0.4f), clip);
                for (int j = 0; j < n; j++)
                {
                    var w = path.points[j];
                    Vector2 s = v.toScreen(w.Xz) + off;
                    bool picked = sel && j == SelectedPoint;
                    float size = j == 0 ? 12f : 8f;
                    if (sel) size += 2f;
                    Square(s, size + 2f, Color.black);
                    Square(s, size, picked ? new Color(1f, 0.95f, 0.2f) : j == 0 ? Color.white : col);
                    // Uncluttered: other paths show the waypoint number only; the selected path shows its heights.
                    string text = sel ? (j == 0 ? "S " : j + ": ") + w.alt.ToString("F0") + " m" + (w.speed > 0f ? "  " + w.speed.ToString("F0") + " m/s" : "")
                                      : (j == 0 ? "S" : j.ToString());
                    Tag(new Vector2(s.x + 8f, s.y - 18f), text, picked ? new Color(1f, 0.95f, 0.2f) : Color.white);
                }
                if (sel && n > 0 && !string.IsNullOrEmpty(path.name))
                {
                    Vector2 s0 = v.toScreen(path.points[0].Xz) + off;
                    Tag(new Vector2(s0.x + 8f, s0.y + 4f), path.name, new Color(col.r, col.g, col.b, 1f));
                }
            }
            if (SpawnPoints != null)
                foreach (var sp in SpawnPoints)
                {
                    Vector2 s = v.toScreen(sp) + off;
                    Square(s, 11f, Color.black);
                    Square(s, 8f, new Color(1f, 0.3f, 0.25f));
                }
            GUI.EndGroup();
        }

        // A leg with a small arrow head in the middle showing which way it is flown.
        static void Leg(Vector2 aw, Vector2 bw, View v, Vector2 off, float width, Color c, Rect clip)
        {
            Vector2 a = v.toScreen(aw) + off, b = v.toScreen(bw) + off;
            Line(a, b, width, c, clip);
            Vector2 d = b - a;
            if (d.magnitude < 40f) return;
            Vector2 dir = d.normalized, mid = (a + b) * 0.5f;
            Vector2 left = new Vector2(-dir.x * 0.87f + dir.y * 0.5f, -dir.y * 0.87f - dir.x * 0.5f);
            Vector2 right = new Vector2(-dir.x * 0.87f - dir.y * 0.5f, -dir.y * 0.87f + dir.x * 0.5f);
            Line(mid, mid + left * 7f, width, c, clip);
            Line(mid, mid + right * 7f, width, c, clip);
        }

        // --- Profile strip: distance along the selected path (across) against height (up), the ground under it as the lower line. ---

        public const float StripHeight = 120f;

        public void Profile(Rect r)
        {
            if (Event.current.type != EventType.Repaint) return;
            GUI.BeginGroup(r);
            var local = new Rect(0f, 0f, r.width, r.height);
            var bg = GUI.color; GUI.color = new Color(0.07f, 0.09f, 0.12f, 1f); GUI.DrawTexture(local, Texture2D.whiteTexture); GUI.color = bg;
            var path = Selected;
            if (path == null || !path.Usable)
            {
                tag.normal.textColor = new Color(0.8f, 0.85f, 0.9f);
                GUI.Label(new Rect(8f, local.height * 0.5f - 10f, local.width - 16f, 20f), "Altitude profile: select a path with two or more waypoints.", tag);
                GUI.EndGroup();
                return;
            }
            // Samples along the path (every leg, the closing leg of a loop too).
            int n = path.points.Count;
            int legs = path.mode == PathMode.Loop && n > 2 ? n : n - 1;
            float total = path.Length();
            if (total < 1f) total = 1f;
            const int perLeg = 24;
            int count = legs * perLeg + 1;
            var dist = new float[count];
            var ground = new float[count];
            var height = new float[count];
            float run = 0f, lo = float.MaxValue, hi = float.MinValue;
            Vector2 prev = path.points[0].Xz;
            for (int j = 0; j < legs; j++)
            {
                var a = path.points[j];
                var b = path.points[(j + 1) % n];
                for (int k = 0; k <= perLeg; k++)
                {
                    if (j > 0 && k == 0) continue;
                    float t = k / (float)perLeg;
                    Vector2 p = Vector2.Lerp(a.Xz, b.Xz, t);
                    run += Vector2.Distance(prev, p);
                    prev = p;
                    int idx = j * perLeg + k;
                    dist[idx] = run;
                    ground[idx] = Ground != null ? Ground(p) : 0f;
                    height[idx] = ground[idx] + Mathf.Lerp(a.alt, b.alt, t);
                    lo = Mathf.Min(lo, ground[idx]);
                    hi = Mathf.Max(hi, height[idx]);
                }
            }
            run = Mathf.Max(run, 1f);
            float pad = 6f, left = 44f, bottom = 16f;
            float pw = local.width - left - pad, ph = local.height - bottom - pad;
            hi = Mathf.Max(hi, lo + 10f);
            float span = hi - lo;
            lo -= span * 0.05f; hi += span * 0.05f;
            Func<float, float, Vector2> At = (d, y) => new Vector2(left + d / run * pw, pad + (1f - (y - lo) / (hi - lo)) * ph);
            // Ground as thin columns down to the bottom edge, then the flight line over it.
            var earth = new Color(0.35f, 0.3f, 0.2f, 1f);
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 g0 = At(dist[i], ground[i]), g1 = At(dist[i + 1], ground[i + 1]);
                float x = g0.x, w = Mathf.Max(1f, g1.x - g0.x + 0.5f), top = Mathf.Min(g0.y, g1.y);
                var old = GUI.color; GUI.color = earth; GUI.DrawTexture(new Rect(x, top, w, pad + ph - top), Texture2D.whiteTexture); GUI.color = old;
            }
            Color col = PathColour(path.use);
            var strip = new Rect(0f, 0f, local.width, local.height);
            for (int i = 0; i < count - 1; i++) Line(At(dist[i], height[i]), At(dist[i + 1], height[i + 1]), 2f, col, strip);
            // Waypoint markers at their distances.
            float acc = 0f;
            for (int j = 0; j < n; j++)
            {
                if (j > 0) acc += Vector2.Distance(path.points[j - 1].Xz, path.points[j].Xz);
                int idx = Mathf.Min(j * perLeg, count - 1);
                if (j == n - 1 && legs == n - 1) idx = count - 1;
                Vector2 mp = At(dist[idx], height[idx]);
                bool picked = j == SelectedPoint;
                Square(mp, 7f, picked ? new Color(1f, 0.95f, 0.2f) : Color.white);
                Tag(new Vector2(mp.x - 6f, mp.y - 17f), path.points[j].alt.ToString("F0") + " m", picked ? new Color(1f, 0.95f, 0.2f) : Color.white);
            }
            tag.normal.textColor = new Color(0.8f, 0.85f, 0.9f);
            GUI.Label(new Rect(2f, pad - 2f, left - 4f, 16f), hi.ToString("F0"), tag);
            GUI.Label(new Rect(2f, pad + ph - 14f, left - 4f, 16f), lo.ToString("F0"), tag);
            GUI.Label(new Rect(left, local.height - 15f, 300f, 16f), $"0 m  ..  {run:F0} m along the path  (height above the sea / ground, brown = terrain)", tag);
            GUI.EndGroup();
        }

        // --- Side panel (GUILayout) ---

        static readonly string[] ToolNames = { "Move / select", "Add waypoints to the path", "Spawn points", "Player posts" };

        public void ToolGrid(float row, bool withSpawn, bool withPosts)
        {
            GUILayout.Label("<b>Click the map to...</b>   (drag any point to move it, right-click a point to remove it)", label);
            var names = new List<string>();
            var tools = new List<Tool>();
            for (int i = 0; i < ToolNames.Length; i++)
            {
                var t = (Tool)i;
                if ((t == Tool.Spawn && !withSpawn) || (t == Tool.Post && !withPosts)) continue;
                names.Add(ToolNames[i]); tools.Add(t);
            }
            int at = Mathf.Max(0, tools.IndexOf(CurrentTool));
            at = GUILayout.SelectionGrid(at, names.ToArray(), 1, button, GUILayout.Height(row * 1.15f * names.Count));
            CurrentTool = tools[at];
        }

        int Stepper(string id, string name, int value, int step, float row)
        {
            return NumberEntry.Stepper(id, name, value, step, 0, int.MaxValue, label, field, button, row, 120f);
        }

        public void PathPanel(float row)
        {
            if (Paths == null) return;
            GUILayout.Label($"<b>Paths: {Paths.Count}</b>", label);
            for (int i = 0; i < Paths.Count; i++)
            {
                var p = Paths[i];
                string who = FlightPath.UseLabel(p.use) + (p.use == PathUse.Drone ? " " + FlightPath.ClassLabel(p.cls) : "");
                string text = $"{i + 1}. {p.name}   {who}   {p.points.Count} pts   {p.Length():F0} m   {FlightPath.ModeLabel(p.mode)}";
                if (GUILayout.Toggle(SelectedPath == i, text, button, GUILayout.Height(row)) && SelectedPath != i) { SelectedPath = i; SelectedPoint = -1; }
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Drone path", button, GUILayout.Height(row))) AddPath(PathUse.Drone);
            if (GUILayout.Button("+ Swarm path", button, GUILayout.Height(row))) AddPath(PathUse.Swarm);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+ Single bird path", button, GUILayout.Height(row))) AddPath(PathUse.Bird);
            if (GUILayout.Button("+ Bird flock path", button, GUILayout.Height(row))) AddPath(PathUse.Flock);
            GUILayout.EndHorizontal();
            GUILayout.Label("A path starts at its first waypoint (S), which is where the contacts appear. Drone paths go to single drones (all, or one class) in turn, the swarm path to the swarm, flock paths to the flocks in turn. Contacts that no path fits fly as before.", small);

            var path = Selected;
            if (path == null) return;
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Name", label, GUILayout.Width(60f), GUILayout.Height(row));
            path.name = GUILayout.TextField(path.name ?? "", 24, field, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Flown by: <b>" + FlightPath.UseLabel(path.use) + "</b>", button, GUILayout.Height(row))) { path.use = (PathUse)(((int)path.use + 1) % 4); if (path.use != PathUse.Drone) path.cls = -1; }
            if (path.use == PathUse.Drone && GUILayout.Button("Class: <b>" + FlightPath.ClassLabel(path.cls) + "</b>", button, GUILayout.Height(row))) path.cls = path.cls >= 3 ? -1 : path.cls + 1;
            if (GUILayout.Button("At the end: <b>" + FlightPath.ModeLabel(path.mode) + "</b>", button, GUILayout.Height(row))) path.mode = (PathMode)(((int)path.mode + 1) % 3);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (SpawnPoints != null && GUILayout.Button("Start at nearest spawn point", button, GUILayout.Height(row))) StartAtNearestSpawn(path);
            if (GUILayout.Button("Reverse", button, GUILayout.Height(row))) { path.points.Reverse(); SelectedPoint = -1; }
            if (GUILayout.Button("Duplicate", button, GUILayout.Height(row))) DuplicateSelected();
            if (GUILayout.Button("DELETE PATH", button, GUILayout.Height(row))) { Paths.RemoveAt(SelectedPath); SelectedPath = Mathf.Min(SelectedPath, Paths.Count - 1); SelectedPoint = -1; return; }
            GUILayout.EndHorizontal();

            if (SelectedPoint < 0 || SelectedPoint >= path.points.Count)
            {
                GUILayout.Label("Click a waypoint on the map to set its height above ground and its speed.", small);
                return;
            }
            var w = path.points[SelectedPoint];
            GUILayout.Label($"<b>Waypoint {(SelectedPoint == 0 ? "S (the spawn)" : SelectedPoint.ToString())}</b> of {path.points.Count - 1}   at {w.x:F0} E, {w.z:F0} N", label);
            GUILayout.BeginHorizontal();
            int alt = Stepper("path.alt", "Height m", Mathf.RoundToInt(w.alt), 10, row);
            if (alt != Mathf.RoundToInt(w.alt)) w.alt = alt;
            int spd = Stepper("path.speed", "Speed m/s", Mathf.RoundToInt(w.speed), 1, row);
            if (spd != Mathf.RoundToInt(w.speed)) w.speed = spd;
            GUILayout.EndHorizontal();
            GUILayout.Label("Height is metres above the ground there (no upper limit). Speed 0 = the contact's own speed; it applies to the leg that ends at this waypoint.", small);
            path.points[SelectedPoint] = w;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Height to ALL waypoints", button, GUILayout.Height(row)))
                for (int i = 0; i < path.points.Count; i++) { var q = path.points[i]; q.alt = w.alt; path.points[i] = q; }
            if (GUILayout.Button("Insert after this", button, GUILayout.Height(row)) && SelectedPoint < path.points.Count)
            {
                Vector2 b = SelectedPoint + 1 < path.points.Count ? path.points[SelectedPoint + 1].Xz : w.Xz + new Vector2(40f, 0f);
                InsertWaypoint(SelectedPath, SelectedPoint + 1, Clamp((w.Xz + b) * 0.5f));
            }
            if (GUILayout.Button("Delete waypoint", button, GUILayout.Height(row))) DeleteWaypoint(SelectedPath, SelectedPoint);
            GUILayout.EndHorizontal();
        }

        void StartAtNearestSpawn(FlightPath path)
        {
            if (SpawnPoints == null || SpawnPoints.Count == 0 || path.points.Count == 0) return;
            Vector2 from = path.points[0].Xz, best = SpawnPoints[0];
            foreach (var s in SpawnPoints) if (Vector2.Distance(s, from) < Vector2.Distance(best, from)) best = s;
            var w = path.points[0];
            w.x = best.x; w.z = best.y;
            path.points[0] = w;
        }

        void DuplicateSelected()
        {
            var copy = Selected.Clone();
            copy.name += " copy";
            for (int i = 0; i < copy.points.Count; i++) { var q = copy.points[i]; q.x += 60f; q.z += 60f; copy.points[i] = q; }
            Paths.Add(copy);
            SelectedPath = Paths.Count - 1;
            SelectedPoint = -1;
        }
    }
}
