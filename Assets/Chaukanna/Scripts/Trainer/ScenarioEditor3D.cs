using System;
using System.Collections.Generic;
using Chaukanna.Scenario;
using Chaukanna.Session;
using Chaukanna.TerrainGen;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Trainer
{
    // The 3D scenario editor (prototype). The field scene is loaded the way START loads it (terrain, trees, buildings), but instead of a run
    // it opens a free-fly camera and a tool panel. It edits the same data as the 2D map: the scenario's paths (shared PathEditor logic and
    // panel), the spawn points, the player posts, and the scenario options; plus what only the 3D view is good for: hand-placed trees, bushes,
    // rocks and buildings (drawn instanced by ManualField, with a brush), areas cleared of generated layers, and drone / bird groups dropped at
    // a point. Everything lives in SessionSetup.scenario (a working copy), so "Test run from here" is START with these settings, and Exit gives
    // the edited scenario back to the trainer menu.
    //
    // Camera: hold the right mouse button and use W A S D (Q down, E up, Shift fast); scroll sets the speed; F frames the selection.
    // Left mouse: the selected tool. Waypoints, spawn points, posts and groups drag on the ground; the arrow above a waypoint or group drags its height.
    // Ctrl+Z / Ctrl+Y undo and redo. Delete removes the selection. Q / E turn a selected tree, bush or building when the camera is not flying.
    public class ScenarioEditor3D : MonoBehaviour
    {
        public static bool Active { get; private set; }
        public static ScenarioEditor3D Current { get; private set; }

        public enum Tool { Select, Path, Spawn, Post, Group, Object, Erase }
        enum DragKind { None, Waypoint, WaypointAlt, Spawn, Post, Group, GroupAlt, Manual }

        static readonly string[] ToolNames = { "Select", "Path", "Spawn", "Post", "Group", "Object", "Erase" };
        const float PanelWidth = 372f;
        const int MaxClearAreas = 300;

        // --- Opening ---

        public static void Open(ScenarioSpawner spawner)
        {
            if (Current != null) Destroy(Current.gameObject);
            var go = new GameObject("ScenarioEditor3D");
            try { go.AddComponent<ScenarioEditor3D>().Begin(spawner); }
            catch (Exception e)
            {
                // A failure half way through Begin would leave the player's camera blind (culling mask 0) and the controls off.
                // Destroying the object runs OnDestroy, which gives both back; the cause goes to the log (Player.log in a build).
                Debug.LogError("[3D editor] Could not open: " + e);
                Destroy(go);
            }
        }

        // --- State ---

        ScenarioSpawner spawner;
        ScenarioConfig cfg;
        List<PlayerSlot> players;
        List<Vector2> spawns;
        TerrainGenConfig terrainCfg;
        ManualField field;
        readonly PathEditor pathEd = new PathEditor();
        TerrainLayerKit kit;

        Camera cam, mainCam;
        int mainMask;
        CameraClearFlags mainClear;
        bool prevHide, prevBlocked, prevSplit;
        CursorLockMode prevLock;
        bool prevCursor;
        TrainerScreen trainer;
        float yaw, pitch, flySpeed = 60f;
        bool flying;

        Tool tool = Tool.Select;
        bool testing;

        int selManual = -1, selSpawn = -1, selGroup = -1;
        DragKind drag;
        int dragIndex;
        ManualObject dragFrom;
        bool dragMoved;

        // Object palette and brush.
        int palKind, palGroup, palVariant;
        bool mixVariants = true, brush, randomYaw = true, randomScale = true;
        int brushRadius = 15, density = 20, scalePct = 100;
        Vector2 paletteScroll;
        List<KitEntry> palette = new List<KitEntry>();
        bool eraseManual = true, eraseGenerated, eraseRestore;
        Vector2 lastStep;
        bool stepping, hasLastStep;
        readonly System.Random rng = new System.Random(4242);

        PlacedGroup template = new PlacedGroup { kind = 0, count = 1, species = 1, alt = 100f, time = 5f, path = -1 };

        // Hover.
        bool hoverValid;
        Vector3 hover;

        // Clear areas are applied to the layer manager a moment after the last change (the layers rebuild, which takes seconds).
        bool clearDirty;
        float clearDirtyAt;

        // UI.
        GUIStyle label, small, button, field2, labelWorld;
        Vector2 scroll;
        string status = "";
        string packageName = "";
        bool optionsOpen;
        const int FontSize = 14;

        // --- Begin / end ---

        void Begin(ScenarioSpawner s)
        {
            Current = this;
            Active = true;
            spawner = s;
            cfg = SessionSetup.scenario;
            players = SessionSetup.players;
            spawns = SessionSetup.spawnPoints;
            terrainCfg = SessionSetup.terrain;
            if (cfg == null) cfg = SessionSetup.scenario = new ScenarioConfig();
            cfg.Validate();
            packageName = string.IsNullOrEmpty(cfg.presetName) ? "Package" : cfg.presetName;
            kit = TerrainLayerKit.Default;
            field = ManualField.Ensure(cfg.manualObjects);

            prevHide = PlayerView.HideOverlays; prevBlocked = ContactRegistry.InputBlocked; prevSplit = PlayerView.Split;
            prevLock = Cursor.lockState; prevCursor = Cursor.visible;
            trainer = FindAnyObjectByType<TrainerScreen>();
            if (trainer != null && prevSplit) trainer.SetSplit(false);
            PlayerView.HideOverlays = true;
            ContactRegistry.InputBlocked = true;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;

            mainCam = Camera.main;
            if (mainCam != null) { mainMask = mainCam.cullingMask; mainClear = mainCam.clearFlags; mainCam.cullingMask = 0; mainCam.clearFlags = CameraClearFlags.Nothing; }

            var camGo = new GameObject("Editor Camera");
            camGo.transform.SetParent(transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.depth = 100f;
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = Mathf.Max(12000f, (terrainCfg != null ? terrainCfg.sizeMetres : 4000f) * 3f);
            cam.fieldOfView = 60f;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            field.cameraOverride = cam;

            var local = SessionSetup.LocalPlayer;
            Vector2 post = local != null ? local.post : Vector2.zero;
            Vector3 at = new Vector3(post.x, FlightPath.Ground(post.x, post.y), post.y);
            cam.transform.position = at + new Vector3(0f, 90f, -160f);
            cam.transform.LookAt(at + Vector3.up * 10f);
            yaw = cam.transform.eulerAngles.y; pitch = cam.transform.eulerAngles.x;

            pathEd.Paths = cfg.paths;
            pathEd.SpawnPoints = spawns;
            pathEd.Players = players;
            pathEd.Clamp = m => ClampXz(m, 20f);
            pathEd.Ground = m => FlightPath.Ground(m.x, m.y);
            pathEd.SelectedPlayer = Mathf.Max(0, players.IndexOf(local));

            palKind = 0; palGroup = 0; palVariant = 0;
            RefreshPalette();
            ResetUndoTracking();
            Debug.Log($"[3D editor] Open: kit '{kit.name}' ({ManualKit.Entries(kit, 0).Count} trees, {ManualKit.Entries(kit, 1).Count} bushes, {ManualKit.Entries(kit, 2).Count} buildings, {ManualKit.Entries(kit, 3).Count} rocks in the palette), {field.Objects.Count} hand-placed objects, {cfg.paths.Count} paths, {cfg.placedGroups.Count} placed groups, terrain {(terrainCfg != null ? terrainCfg.sizeMetres : 0f):F0} m.");
            SetStatus("3D editor open. Hold the right mouse button and use W A S D to fly.");
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (!testing) Restore();
            Active = false;
            if (field != null) field.cameraOverride = null;
        }

        // Gives the player's camera and the input back.
        void Restore()
        {
            if (mainCam != null) { mainCam.cullingMask = mainMask; mainCam.clearFlags = mainClear; }
            PlayerView.HideOverlays = prevHide;
            ContactRegistry.InputBlocked = false;     // the editor is only opened from the loading screen, which had it on;
            if (trainer != null && prevSplit) trainer.SetSplit(true);
            Cursor.lockState = prevLock; Cursor.visible = prevCursor;
        }

        Vector2 ClampXz(Vector2 p, float margin)
        {
            float half = (terrainCfg != null ? terrainCfg.sizeMetres : 4000f) * 0.5f - margin;
            return new Vector2(Mathf.Clamp(p.x, -half, half), Mathf.Clamp(p.y, -half, half));
        }

        void SetStatus(string s) { status = s; }

        // --- Actions ---

        void ApplyClearAreas()
        {
            clearDirty = false;
            SiteClearance.SetExtra(ManualLayout.ClearZones(cfg.clearAreas));
        }

        // START with these settings, here and now: the field is already built, so the run starts at once.
        void TestRun()
        {
            if (cfg.clearAreas != null) ApplyClearAreas();
            cfg.Validate();
            testing = true;
            Active = false;
            drag = DragKind.None;
            Restore();
            if (cam != null) cam.gameObject.SetActive(false);
            if (field != null) field.cameraOverride = null;
            SessionSetup.editorMode = false;
            foreach (var m in markerRoot) if (m != null) m.SetActive(false);
            FieldBootstrap.PlacePosts(spawner);
            FieldBootstrap.StartRun(spawner);
        }

        // From a test run back to the editor: the field is built again from the same settings (a few seconds).
        void BackToEditor()
        {
            SessionSetup.editorMode = true;
            SessionSetup.LoadWithScreen(SessionSetup.fieldScenePath);
        }

        void ExitToMenu()
        {
            if (cfg.clearAreas != null) ApplyClearAreas();
            string menu = SessionSetup.menuScenePath;
            Restore();
            testing = true;     // Restore already ran
            Active = false;
            if (!string.IsNullOrEmpty(menu)) SessionSetup.Load(menu);
        }

        void SavePackage()
        {
            var spec = SpecExpander.FromConfig(cfg, packageName);
            var issues = SpecValidator.Validate(spec);
            foreach (var i in issues)
                if (i.blocking) { SetStatus($"Not saved: {i.code} {i.message}"); return; }
            try { PackageStore.Save(spec); SetStatus($"Saved package '{spec.name}' in {PackageStore.Folder}"); }
            catch (Exception e) { SetStatus("Could not save: " + e.Message); }
        }

        // --- Undo / redo ---

        class Small
        {
            public List<FlightPath> paths;
            public List<Vector2> spawns;
            public List<PlayerSlot> players;
            public List<PlacedGroup> groups;
            public List<ClearArea> clears;
        }

        class UndoEntry
        {
            public Action undo, redo;
            public bool auto;
            public Small before, after;
        }

        class Op { public int type, index; public ManualObject a, b; }      // 0 add, 1 remove, 2 set

        readonly List<UndoEntry> undoStack = new List<UndoEntry>(), redoStack = new List<UndoEntry>();
        readonly List<Op> stroke = new List<Op>();
        Small lastSnap;
        int lastSig;
        float lastChange = -10f;
        bool boundary = true;
        List<FlightPath> lastPathRefs = new List<FlightPath>();

        Small Capture()
        {
            var s = new Small { paths = FlightPath.CloneAll(cfg.paths), spawns = new List<Vector2>(spawns), groups = ManualLayout.CloneGroups(cfg.placedGroups), clears = ManualLayout.CloneAreas(cfg.clearAreas), players = new List<PlayerSlot>() };
            foreach (var p in players) s.players.Add(new PlayerSlot { name = p.name, post = p.post, ready = p.ready, isLocal = p.isLocal });
            return s;
        }

        void RestoreSmall(Small s)
        {
            cfg.paths.Clear(); cfg.paths.AddRange(FlightPath.CloneAll(s.paths));
            spawns.Clear(); spawns.AddRange(s.spawns);
            cfg.placedGroups.Clear(); cfg.placedGroups.AddRange(s.groups);
            cfg.clearAreas.Clear(); cfg.clearAreas.AddRange(s.clears);
            players.Clear();
            foreach (var p in s.players) players.Add(new PlayerSlot { name = p.name, post = p.post, ready = p.ready, isLocal = p.isLocal });
            pathEd.SelectedPath = Mathf.Min(pathEd.SelectedPath, cfg.paths.Count - 1);
            pathEd.SelectedPoint = -1;
            pathEd.SelectedPlayer = Mathf.Clamp(pathEd.SelectedPlayer, 0, Mathf.Max(0, players.Count - 1));
            selSpawn = -1; selGroup = Mathf.Min(selGroup, cfg.placedGroups.Count - 1);
            clearDirty = true; clearDirtyAt = Time.unscaledTime;
        }

        static int Mix(int h, int v) { unchecked { return (h * 16777619) ^ v; } }
        static int Bits(float f) => BitConverter.SingleToInt32Bits(f);

        int Signature()
        {
            int h = 17;
            foreach (var p in cfg.paths)
            {
                h = Mix(h, p.name != null ? p.name.GetHashCode() : 0); h = Mix(h, (int)p.use); h = Mix(h, p.cls); h = Mix(h, (int)p.mode);
                foreach (var w in p.points) { h = Mix(h, Bits(w.x)); h = Mix(h, Bits(w.z)); h = Mix(h, Bits(w.alt)); h = Mix(h, Bits(w.speed)); }
                h = Mix(h, p.points.Count);
            }
            foreach (var v in spawns) { h = Mix(h, Bits(v.x)); h = Mix(h, Bits(v.y)); }
            foreach (var p in players) { h = Mix(h, Bits(p.post.x)); h = Mix(h, Bits(p.post.y)); h = Mix(h, p.name != null ? p.name.GetHashCode() : 0); }
            foreach (var g in cfg.placedGroups) { h = Mix(h, g.kind); h = Mix(h, g.count); h = Mix(h, g.species); h = Mix(h, Bits(g.x)); h = Mix(h, Bits(g.z)); h = Mix(h, Bits(g.alt)); h = Mix(h, Bits(g.time)); h = Mix(h, g.path); }
            foreach (var a in cfg.clearAreas) { h = Mix(h, Bits(a.x)); h = Mix(h, Bits(a.z)); h = Mix(h, Bits(a.radius)); }
            h = Mix(h, cfg.paths.Count); h = Mix(h, spawns.Count); h = Mix(h, players.Count); h = Mix(h, cfg.placedGroups.Count); h = Mix(h, cfg.clearAreas.Count);
            return h;
        }

        void ResetUndoTracking()
        {
            lastSnap = Capture();
            lastSig = Signature();
            lastPathRefs = new List<FlightPath>(cfg.paths);
        }

        // Called once a frame after the edits: any change to the small data (paths, spawns, posts, groups, cleared areas) becomes an undo
        // step. Changes less than 0.8 s apart (a drag, a typed number) are one step; a mouse press starts a new one.
        void TrackChanges()
        {
            // A path was deleted in the panel: groups that used it lose it, and later paths shift down.
            if (cfg.paths.Count < lastPathRefs.Count)
            {
                int gone = -1;
                for (int i = 0; i < lastPathRefs.Count; i++) if (i >= cfg.paths.Count || cfg.paths[i] != lastPathRefs[i]) { gone = i; break; }
                if (gone >= 0)
                    for (int i = 0; i < cfg.placedGroups.Count; i++)
                    {
                        var g = cfg.placedGroups[i];
                        if (g.path == gone) g.path = -1; else if (g.path > gone) g.path--;
                        cfg.placedGroups[i] = g;
                    }
            }
            lastPathRefs = new List<FlightPath>(cfg.paths);

            int sig = Signature();
            if (sig == lastSig) return;
            var now = Capture();
            if (cfg.clearAreas.Count != lastSnap.clears.Count || ClearsDiffer(lastSnap.clears)) { clearDirty = true; clearDirtyAt = Time.unscaledTime; }
            float t = Time.unscaledTime;
            var top = undoStack.Count > 0 ? undoStack[undoStack.Count - 1] : null;
            if (top != null && top.auto && !boundary && t - lastChange < 0.8f && redoStack.Count == 0) top.after = now;
            else
            {
                var e = new UndoEntry { auto = true, before = lastSnap, after = now };
                e.undo = () => RestoreSmall(e.before);
                e.redo = () => RestoreSmall(e.after);
                Push(e);
            }
            boundary = false;
            lastChange = t;
            lastSnap = now;
            lastSig = sig;
        }

        bool ClearsDiffer(List<ClearArea> other)
        {
            for (int i = 0; i < other.Count && i < cfg.clearAreas.Count; i++)
                if (Mathf.Abs(other[i].x - cfg.clearAreas[i].x) > 0.01f || Mathf.Abs(other[i].z - cfg.clearAreas[i].z) > 0.01f || Mathf.Abs(other[i].radius - cfg.clearAreas[i].radius) > 0.01f) return true;
            return false;
        }

        void Push(UndoEntry e)
        {
            undoStack.Add(e);
            redoStack.Clear();
            if (undoStack.Count > 150) undoStack.RemoveAt(0);
        }

        void Undo()
        {
            EndStroke();
            if (undoStack.Count == 0) { SetStatus("Nothing to undo."); return; }
            var e = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            e.undo();
            redoStack.Add(e);
            ResetUndoTracking();
            boundary = true;
            SetStatus("Undone.");
        }

        void Redo()
        {
            EndStroke();
            if (redoStack.Count == 0) { SetStatus("Nothing to redo."); return; }
            var e = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            e.redo();
            undoStack.Add(e);
            ResetUndoTracking();
            boundary = true;
            SetStatus("Redone.");
        }

        // Manual objects: every change is logged as an operation; a stroke (one click, one brush drag) is one undo step.
        void OpAdd(ManualObject o) { stroke.Add(new Op { type = 0, index = field.Objects.Count, b = o }); field.Add(o); }
        void OpRemove(int i) { stroke.Add(new Op { type = 1, index = i, a = field.Objects[i] }); field.RemoveAt(i); }
        void OpSet(int i, ManualObject o) { stroke.Add(new Op { type = 2, index = i, a = field.Objects[i], b = o }); field.Set(i, o); }

        void EndStroke()
        {
            if (stroke.Count == 0) return;
            var ops = new List<Op>(stroke);
            stroke.Clear();
            Push(new UndoEntry { undo = () => ApplyOps(ops, true), redo = () => ApplyOps(ops, false) });
        }

        void ApplyOps(List<Op> ops, bool reverse)
        {
            if (reverse)
                for (int i = ops.Count - 1; i >= 0; i--)
                {
                    var o = ops[i];
                    if (o.type == 0) field.RemoveAt(o.index); else if (o.type == 1) field.Insert(o.index, o.a); else field.Set(o.index, o.a);
                }
            else
                foreach (var o in ops)
                {
                    if (o.type == 0) field.Insert(o.index, o.b); else if (o.type == 1) field.RemoveAt(o.index); else field.Set(o.index, o.b);
                }
            selManual = -1;
        }

        // --- Update: camera and clicks ---

        bool OverPanel(Vector2 px) => px.x < PanelWidth * (Screen.height / 720f);
        bool Typing => NumberEntry.AnyFocused || GUIUtility.keyboardControl != 0;

        void Update()
        {
            if (testing || cam == null) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Vector2 mp = mouse.position.ReadValue();
            Vector2 mpTop = new Vector2(mp.x, Screen.height - mp.y);
            bool over = OverPanel(mpTop);

            // Fly: right mouse held. The cursor is locked meanwhile so the look is not limited by the window edge.
            if (mouse.rightButton.wasPressedThisFrame && !over && drag == DragKind.None) { flying = true; Cursor.lockState = CursorLockMode.Locked; GUIUtility.keyboardControl = 0; }
            if (flying && !mouse.rightButton.isPressed) { flying = false; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (flying)
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * 0.12f; pitch = Mathf.Clamp(pitch - d.y * 0.12f, -89f, 89f);
                cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
                float sp = flySpeed * (kb.leftShiftKey.isPressed ? 4f : 1f) * dt;
                Vector3 move = Vector3.zero;
                if (kb.wKey.isPressed) move += cam.transform.forward;
                if (kb.sKey.isPressed) move -= cam.transform.forward;
                if (kb.dKey.isPressed) move += cam.transform.right;
                if (kb.aKey.isPressed) move -= cam.transform.right;
                if (kb.eKey.isPressed) move += Vector3.up;
                if (kb.qKey.isPressed) move -= Vector3.up;
                cam.transform.position += move * sp;
            }
            if (!over)
            {
                float wheel = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(wheel) > 0.01f) flySpeed = Mathf.Clamp(flySpeed * Mathf.Pow(1.15f, Mathf.Sign(wheel)), 2f, 4000f);
            }

            // Hover point on the ground (for the brush ring and the status line).
            hoverValid = !over && !flying && PickGround(cam.ScreenPointToRay(mp), out hover);

            // Clear areas are applied after a quiet moment.
            if (clearDirty && !mouse.leftButton.isPressed && Time.unscaledTime - clearDirtyAt > 0.6f) ApplyClearAreas();

            if (!flying) Hotkeys(kb);

            // Left button.
            if (mouse.leftButton.wasPressedThisFrame && !over && !flying)
            {
                GUI.FocusControl(null); GUIUtility.keyboardControl = 0;
                boundary = true;
                OnPress(mp, kb);
            }
            if (mouse.leftButton.isPressed && !flying) OnHold(mp);
            if (mouse.leftButton.wasReleasedThisFrame) OnRelease();
        }

        void LateUpdate()
        {
            if (testing || cam == null) return;
            TrackChanges();
            DrawMarkers();
        }

        void Hotkeys(Keyboard kb)
        {
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            if (ctrl && kb.zKey.wasPressedThisFrame && !Typing) { if (kb.leftShiftKey.isPressed) Redo(); else Undo(); return; }
            if (ctrl && kb.yKey.wasPressedThisFrame && !Typing) { Redo(); return; }
            if (Typing) return;
            if (kb.fKey.wasPressedThisFrame) FrameSelection();
            if (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame) DeleteSelection();
            if (kb.escapeKey.wasPressedThisFrame) { selManual = -1; selSpawn = -1; selGroup = -1; pathEd.SelectedPoint = -1; }
            float turn = kb.leftShiftKey.isPressed ? 5f : 15f;
            if (selManual >= 0 && selManual < field.Objects.Count)
            {
                if (kb.eKey.wasPressedThisFrame) { var o = field.Objects[selManual]; o.yaw += turn; boundary = true; OpSet(selManual, o); EndStroke(); }
                if (kb.qKey.wasPressedThisFrame) { var o = field.Objects[selManual]; o.yaw -= turn; boundary = true; OpSet(selManual, o); EndStroke(); }
            }
            for (int i = 0; i < ToolNames.Length; i++)
            {
                var key = i == 0 ? kb.digit1Key : i == 1 ? kb.digit2Key : i == 2 ? kb.digit3Key : i == 3 ? kb.digit4Key : i == 4 ? kb.digit5Key : i == 5 ? kb.digit6Key : kb.digit7Key;
                if (key.wasPressedThisFrame) { tool = (Tool)i; boundary = true; }
            }
        }

        void DeleteSelection()
        {
            boundary = true;
            if (selManual >= 0 && selManual < field.Objects.Count) { OpRemove(selManual); EndStroke(); selManual = -1; SetStatus("Deleted the object."); return; }
            if (selGroup >= 0 && selGroup < cfg.placedGroups.Count) { cfg.placedGroups.RemoveAt(selGroup); selGroup = -1; SetStatus("Deleted the group."); return; }
            if (selSpawn >= 0 && selSpawn < spawns.Count) { spawns.RemoveAt(selSpawn); selSpawn = -1; SetStatus("Deleted the spawn point."); return; }
            if (pathEd.SelectedPath >= 0 && pathEd.SelectedPoint >= 0 && pathEd.Selected != null) { pathEd.DeleteWaypoint(pathEd.SelectedPath, pathEd.SelectedPoint); SetStatus("Deleted the waypoint."); }
        }

        bool SelectionTarget(out Vector3 at, out float radius)
        {
            at = Vector3.zero; radius = 10f;
            var path = pathEd.Selected;
            if (path != null && pathEd.SelectedPoint >= 0 && pathEd.SelectedPoint < path.points.Count) { at = path.World(pathEd.SelectedPoint); return true; }
            if (selManual >= 0 && selManual < field.Objects.Count) { var o = field.Objects[selManual]; at = new Vector3(o.x, field.GroundOf(selManual) + field.HeightOf(o) * 0.5f, o.z); radius = field.RadiusOf(o) + field.HeightOf(o) * 0.5f; return true; }
            if (selGroup >= 0 && selGroup < cfg.placedGroups.Count) { at = GroupWorld(cfg.placedGroups[selGroup]); return true; }
            if (selSpawn >= 0 && selSpawn < spawns.Count) { at = new Vector3(spawns[selSpawn].x, FlightPath.Ground(spawns[selSpawn].x, spawns[selSpawn].y), spawns[selSpawn].y); return true; }
            if (pathEd.SelectedPlayer >= 0 && pathEd.SelectedPlayer < players.Count) { var p = players[pathEd.SelectedPlayer].post; at = new Vector3(p.x, FlightPath.Ground(p.x, p.y), p.y); return true; }
            return false;
        }

        void FrameSelection()
        {
            if (!SelectionTarget(out var at, out float radius)) { SetStatus("Nothing selected to frame."); return; }
            cam.transform.position = at - cam.transform.forward * Mathf.Max(25f, radius * 3f);
        }

        // --- Picking ---

        // The ground under a ray: the terrain collider when there is one, else a march along the ray over the height function.
        bool PickGround(Ray ray, out Vector3 point)
        {
            if (Physics.Raycast(ray, out var hit, 30000f, ~0, QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                point.y = FlightPath.Ground(point.x, point.z);
                return true;
            }
            if (ray.direction.y > 0.02f) { point = Vector3.zero; return false; }      // pointing at the sky
            Vector3 prev = ray.origin;
            for (float t = 10f; t < 30000f; t += 10f + t * 0.01f)
            {
                Vector3 p = ray.origin + ray.direction * t;
                float g = TerrainBuilder.GroundHeight(new Vector3(p.x, 0f, p.z));
                if (!float.IsNaN(g) && p.y <= g)
                {
                    // Bisect between the last point above ground and this one.
                    Vector3 a = prev, b = p;
                    for (int i = 0; i < 12; i++)
                    {
                        Vector3 m = (a + b) * 0.5f;
                        float gm = TerrainBuilder.GroundHeight(new Vector3(m.x, 0f, m.z));
                        if (!float.IsNaN(gm) && m.y <= gm) b = m; else a = m;
                    }
                    point = b; point.y = FlightPath.Ground(b.x, b.z);
                    return true;
                }
                prev = p;
            }
            point = Vector3.zero;
            return false;
        }

        bool Screen2(Vector3 world, out Vector2 px)
        {
            var s = cam.WorldToScreenPoint(world);
            px = new Vector2(s.x, s.y);
            return s.z > 0.1f;
        }

        float Dist2(Vector2 a, Vector2 b) => Vector2.Distance(a, b);

        Vector3 GroupWorld(PlacedGroup g) => new Vector3(g.x, FlightPath.Ground(g.x, g.z) + g.alt, g.z);

        float PerPixel(Vector3 p) => Vector3.Distance(cam.transform.position, p) * 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / Mathf.Max(1f, Screen.height);

        // The top of the height handle of a waypoint or group: a fixed number of pixels above it.
        Vector3 HandleTop(Vector3 p) => p + Vector3.up * (PerPixel(p) * 34f);

        // Height of the point on a vertical line (through `ground`) that is closest to the mouse ray.
        float AltFromRay(Ray ray, Vector3 ground)
        {
            Vector3 u = Vector3.up, d = ray.direction, w0 = ground - ray.origin;
            float b = Vector3.Dot(u, d), dd = Vector3.Dot(u, w0), e = Vector3.Dot(d, w0);
            float denom = 1f - b * b;
            if (Mathf.Abs(denom) < 1e-4f) return 0f;
            return Mathf.Max(0f, (b * e - dd) / denom);
        }

        bool TryPickHandle(Vector2 mp, out DragKind kind, out int index)
        {
            kind = DragKind.None; index = -1;
            const float hit = 15f;
            Vector2 px;
            // Height handles of the selected waypoint and group come first.
            var path = pathEd.Selected;
            if (path != null && pathEd.SelectedPoint >= 0 && pathEd.SelectedPoint < path.points.Count && Screen2(HandleTop(path.World(pathEd.SelectedPoint)), out px) && Dist2(px, mp) < hit) { kind = DragKind.WaypointAlt; index = pathEd.SelectedPoint; return true; }
            if (selGroup >= 0 && selGroup < cfg.placedGroups.Count && Screen2(HandleTop(GroupWorld(cfg.placedGroups[selGroup])), out px) && Dist2(px, mp) < hit) { kind = DragKind.GroupAlt; index = selGroup; return true; }
            float best = hit;
            // Waypoints of every path (the nearest wins).
            int bestPath = -1, bestPoint = -1;
            for (int i = 0; i < cfg.paths.Count; i++)
                for (int j = 0; j < cfg.paths[i].points.Count; j++)
                    if (Screen2(cfg.paths[i].World(j), out px)) { float d = Dist2(px, mp); if (d < best) { best = d; bestPath = i; bestPoint = j; } }
            if (bestPath >= 0)
            {
                pathEd.SelectedPath = bestPath; pathEd.SelectedPoint = bestPoint;
                selManual = -1; selSpawn = -1; selGroup = -1;
                kind = DragKind.Waypoint; index = bestPoint;
                return true;
            }
            for (int i = 0; i < cfg.placedGroups.Count; i++)
                if (Screen2(GroupWorld(cfg.placedGroups[i]), out px) && Dist2(px, mp) < best) { best = Dist2(px, mp); kind = DragKind.Group; index = i; }
            if (kind == DragKind.Group) { selGroup = index; selManual = -1; selSpawn = -1; pathEd.SelectedPoint = -1; return true; }
            for (int i = 0; i < spawns.Count; i++)
                if (Screen2(new Vector3(spawns[i].x, FlightPath.Ground(spawns[i].x, spawns[i].y) + 2f, spawns[i].y), out px) && Dist2(px, mp) < best) { best = Dist2(px, mp); kind = DragKind.Spawn; index = i; }
            if (kind == DragKind.Spawn) { selSpawn = index; selManual = -1; selGroup = -1; pathEd.SelectedPoint = -1; return true; }
            for (int i = 0; i < players.Count; i++)
                if (Screen2(new Vector3(players[i].post.x, FlightPath.Ground(players[i].post.x, players[i].post.y) + 1f, players[i].post.y), out px) && Dist2(px, mp) < best) { best = Dist2(px, mp); kind = DragKind.Post; index = i; }
            if (kind == DragKind.Post) { pathEd.SelectedPlayer = index; selManual = -1; selGroup = -1; selSpawn = -1; pathEd.SelectedPoint = -1; return true; }
            if (tool == Tool.Select)
            {
                int m = PickManual(mp, 22f);
                if (m >= 0) { selManual = m; selSpawn = -1; selGroup = -1; pathEd.SelectedPoint = -1; kind = DragKind.Manual; index = m; return true; }
            }
            return false;
        }

        int PickManual(Vector2 mp, float within)
        {
            int best = -1;
            float bd = within;
            var list = field.Objects;
            for (int i = 0; i < list.Count; i++)
            {
                var o = list[i];
                var s = cam.WorldToScreenPoint(new Vector3(o.x, field.GroundOf(i) + 1.5f, o.z));
                if (s.z < 0.1f) continue;
                float d = Vector2.Distance(new Vector2(s.x, s.y), mp);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // --- Press / hold / release ---

        void OnPress(Vector2 mp, Keyboard kb)
        {
            var ray = cam.ScreenPointToRay(mp);
            bool brushing = (tool == Tool.Object && brush) || tool == Tool.Erase;
            if (!brushing && TryPickHandle(mp, out var kind, out int idx))
            {
                drag = kind; dragIndex = idx; dragMoved = false;
                if (kind == DragKind.Manual) dragFrom = field.Objects[idx];
                return;
            }
            if (!PickGround(ray, out var point)) { if (tool == Tool.Select) { selManual = -1; selSpawn = -1; selGroup = -1; pathEd.SelectedPoint = -1; } return; }
            Vector2 xz = new Vector2(point.x, point.z);
            switch (tool)
            {
                case Tool.Select:
                    selManual = -1; selSpawn = -1; selGroup = -1; pathEd.SelectedPoint = -1;
                    break;
                case Tool.Path:
                    PathClick(mp, xz);
                    break;
                case Tool.Spawn:
                    spawns.Add(ClampXz(xz, 20f));
                    selSpawn = spawns.Count - 1;
                    SetStatus($"Spawn point {spawns.Count} placed.");
                    break;
                case Tool.Post:
                    if (pathEd.SelectedPlayer >= 0 && pathEd.SelectedPlayer < players.Count)
                    {
                        players[pathEd.SelectedPlayer].post = ClampXz(xz, 100f);
                        SetStatus($"{players[pathEd.SelectedPlayer].name}'s post moved. (The flat pad is rebuilt next time the terrain is built.)");
                    }
                    break;
                case Tool.Group:
                    PlaceGroup(xz);
                    break;
                case Tool.Object:
                    if (brush) { StartStroke(xz); }
                    else { PlaceObject(xz); EndStroke(); }
                    break;
                case Tool.Erase:
                    StartStroke(xz);
                    break;
            }
        }

        void PathClick(Vector2 mp, Vector2 xz)
        {
            var path = pathEd.Selected;
            // Close to a line of the selected path: insert a waypoint between its two ends.
            if (path != null && path.points.Count >= 2)
            {
                int n = path.points.Count;
                for (int i = 1; i < n; i++)
                {
                    if (!Screen2(path.World(i - 1), out var a) || !Screen2(path.World(i), out var b)) continue;
                    if (DistToSegment(mp, a, b) < 8f)
                    {
                        pathEd.InsertWaypoint(pathEd.SelectedPath, i, ClampXz(xz, 20f));
                        SetStatus($"Waypoint inserted into '{path.name}'.");
                        return;
                    }
                }
            }
            pathEd.AddWaypoint(ClampXz(xz, 20f));
            var p = pathEd.Selected;
            SetStatus($"Waypoint {p.points.Count - 1} added to '{p.name}'. Drag the arrow above it to set its height.");
        }

        static float DistToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-4f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        void OnHold(Vector2 mp)
        {
            var ray = cam.ScreenPointToRay(mp);
            if (drag != DragKind.None)
            {
                if (drag == DragKind.WaypointAlt || drag == DragKind.GroupAlt)
                {
                    if (drag == DragKind.WaypointAlt)
                    {
                        var path = pathEd.Selected;
                        if (path == null || dragIndex >= path.points.Count) return;
                        var w = path.points[dragIndex];
                        w.alt = Mathf.Round(AltFromRay(ray, new Vector3(w.x, FlightPath.Ground(w.x, w.z), w.z)));
                        path.points[dragIndex] = w;
                    }
                    else if (dragIndex < cfg.placedGroups.Count)
                    {
                        var g = cfg.placedGroups[dragIndex];
                        g.alt = Mathf.Round(AltFromRay(ray, new Vector3(g.x, FlightPath.Ground(g.x, g.z), g.z)));
                        cfg.placedGroups[dragIndex] = g;
                    }
                    dragMoved = true;
                    return;
                }
                if (!PickGround(ray, out var point)) return;
                Vector2 xz = new Vector2(point.x, point.z);
                dragMoved = true;
                switch (drag)
                {
                    case DragKind.Waypoint:
                    {
                        var path = pathEd.Selected;
                        if (path == null || dragIndex >= path.points.Count) break;
                        var c = ClampXz(xz, 20f);
                        var w = path.points[dragIndex]; w.x = c.x; w.z = c.y; path.points[dragIndex] = w;
                        break;
                    }
                    case DragKind.Spawn: if (dragIndex < spawns.Count) spawns[dragIndex] = ClampXz(xz, 20f); break;
                    case DragKind.Post: if (dragIndex < players.Count) players[dragIndex].post = ClampXz(xz, 100f); break;
                    case DragKind.Group:
                        if (dragIndex < cfg.placedGroups.Count) { var c = ClampXz(xz, 20f); var g = cfg.placedGroups[dragIndex]; g.x = c.x; g.z = c.y; cfg.placedGroups[dragIndex] = g; }
                        break;
                    case DragKind.Manual:
                        if (dragIndex < field.Objects.Count) { var o = field.Objects[dragIndex]; var c = ClampXz(xz, 5f); o.x = c.x; o.z = c.y; field.Set(dragIndex, o); }
                        break;
                }
                return;
            }
            if (stepping && PickGround(ray, out var p2))
            {
                Vector2 xz = new Vector2(p2.x, p2.z);
                float r = tool == Tool.Erase ? brushRadius : brushRadius;
                if (!hasLastStep || Vector2.Distance(xz, lastStep) >= Mathf.Max(2f, r * 0.35f)) BrushStep(xz);
            }
        }

        void OnRelease()
        {
            if (drag == DragKind.Manual && dragMoved && dragIndex < field.Objects.Count)
            {
                var now = field.Objects[dragIndex];
                stroke.Add(new Op { type = 2, index = dragIndex, a = dragFrom, b = now });
                EndStroke();
            }
            drag = DragKind.None;
            stepping = false; hasLastStep = false;
            EndStroke();
        }

        // --- Placing things ---

        void PlaceGroup(Vector2 xz)
        {
            var g = template;
            g.x = xz.x; g.z = xz.y;
            g.path = -1;
            cfg.placedGroups.Add(g);
            selGroup = cfg.placedGroups.Count - 1;
            selManual = -1; selSpawn = -1;
            SetStatus($"{PlacedGroup.Label(g.Kind)} placed. Pick its path in the panel, or drag the arrow above it to set its height.");
        }

        KitEntry CurrentEntry()
        {
            foreach (var e in palette) if (e.group == palGroup && e.variant == palVariant) return e;
            return null;
        }

        void RefreshPalette()
        {
            palette = ManualKit.Entries(kit, palKind);
            // Stay on a group that has something in it.
            bool has = false;
            foreach (var e in palette) if (e.group == palGroup) { has = true; break; }
            if (!has && palette.Count > 0) { palGroup = palette[0].group; palVariant = palette[0].variant; }
            if (palette.Count == 0) { palGroup = 0; palVariant = 0; }
        }

        int VariantCount(int group)
        {
            int n = 0;
            foreach (var e in palette) if (e.group == group) n++;
            return n;
        }

        ManualObject NewObject(Vector2 xz)
        {
            int variant = palVariant;
            if (mixVariants) { int n = VariantCount(palGroup); if (n > 0) variant = rng.Next(n); }
            float s = scalePct / 100f * (randomScale ? 0.8f + 0.45f * (float)rng.NextDouble() : 1f);
            return new ManualObject { kind = palKind, group = palGroup, variant = variant, x = xz.x, z = xz.y, yaw = randomYaw ? (float)rng.NextDouble() * 360f : 0f, scale = s };
        }

        void PlaceObject(Vector2 xz)
        {
            if (palette.Count == 0) { SetStatus("The kit has no prefab for this kind."); return; }
            OpAdd(NewObject(ClampXz(xz, 5f)));
            selManual = field.Objects.Count - 1;
        }

        void StartStroke(Vector2 xz)
        {
            stepping = true; hasLastStep = false;
            BrushStep(xz);
        }

        readonly List<int> scratch = new List<int>();

        void BrushStep(Vector2 xz)
        {
            lastStep = xz; hasLastStep = true;
            float r = brushRadius;
            if (tool == Tool.Erase)
            {
                int removed = 0;
                if (eraseManual)
                {
                    scratch.Clear();
                    field.InCircle(xz, r, scratch);
                    scratch.Sort();
                    for (int k = scratch.Count - 1; k >= 0; k--) { OpRemove(scratch[k]); removed++; }
                    selManual = -1;
                }
                if (eraseGenerated) AddClear(xz, r);
                if (eraseRestore) RestoreCleared(xz, r);
                SetStatus(removed > 0 ? $"Erased {removed} object{(removed > 1 ? "s" : "")}." : eraseGenerated ? "Generated layers cleared here (they rebuild when you let go)." : "Nothing to erase here.");
                return;
            }
            // Object tool: single click, or the brush.
            if (!brush) { PlaceObject(xz); return; }
            if (palette.Count == 0) return;
            float area = Mathf.PI * r * r;
            int target = Mathf.Max(1, Mathf.RoundToInt(density * area / 1000f));
            scratch.Clear();
            field.InCircle(xz, r, scratch, palKind);
            int need = Mathf.Min(400, target - scratch.Count);
            for (int k = 0; k < need; k++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = Mathf.Sqrt((float)rng.NextDouble()) * r;
                var p = ClampXz(xz + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * d, 5f);
                OpAdd(NewObject(p));
            }
            if (need > 0) SetStatus($"Brush: {field.Objects.Count} placed objects in total.");
        }

        void AddClear(Vector2 xz, float r)
        {
            // Already covered by an earlier area: nothing to add.
            foreach (var a in cfg.clearAreas) if (Vector2.Distance(new Vector2(a.x, a.z), xz) + r <= a.radius + 0.5f) return;
            if (cfg.clearAreas.Count >= MaxClearAreas) { SetStatus($"At most {MaxClearAreas} cleared areas (use a bigger brush)."); return; }
            cfg.clearAreas.Add(new ClearArea { x = xz.x, z = xz.y, radius = r });
        }

        void RestoreCleared(Vector2 xz, float r)
        {
            for (int i = cfg.clearAreas.Count - 1; i >= 0; i--)
            {
                var a = cfg.clearAreas[i];
                if (Vector2.Distance(new Vector2(a.x, a.z), xz) <= r + a.radius * 0.5f) cfg.clearAreas.RemoveAt(i);
            }
        }

        // --- Markers (3D lines, posts and handles) ---

        class Pool
        {
            readonly List<GameObject> items = new List<GameObject>();
            readonly Func<GameObject> make;
            int used;
            public Pool(Func<GameObject> make) { this.make = make; }
            public void Begin() { used = 0; }
            public GameObject Next()
            {
                if (used == items.Count) items.Add(make());
                var g = items[used++];
                if (!g.activeSelf) g.SetActive(true);
                return g;
            }
            public void End() { for (int i = used; i < items.Count; i++) if (items[i].activeSelf) items[i].SetActive(false); }
            public List<GameObject> All => items;
        }

        readonly Dictionary<int, Material> mats = new Dictionary<int, Material>();
        Pool spheres, cubes, capsules, lines;
        readonly List<GameObject> markerRoot = new List<GameObject>();
        Transform markers;
        Vector3[] linePts = new Vector3[64];

        Material Mat(Color c)
        {
            int key = ((int)(c.r * 255f) << 24) | ((int)(c.g * 255f) << 16) | ((int)(c.b * 255f) << 8) | (int)(c.a * 255f);
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            var src = RuntimeMaterials.Unlit != null ? RuntimeMaterials.Unlit : RuntimeMaterials.Lit;
            m = src != null ? new Material(src) : new Material(RuntimeMaterials.Find("Universal Render Pipeline/Unlit"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            mats[key] = m;
            return m;
        }

        GameObject MakePrim(PrimitiveType t, string name)
        {
            var go = GameObject.CreatePrimitive(t);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);       // markers are never hit by the ground click
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.SetParent(markers, false);
            markerRoot.Add(go);
            return go;
        }

        GameObject MakeLine()
        {
            var go = new GameObject("Line");
            go.transform.SetParent(markers, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 2;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            markerRoot.Add(go);
            return go;
        }

        void EnsurePools()
        {
            if (markers != null) return;
            markers = new GameObject("Editor markers").transform;
            markers.SetParent(transform, false);
            spheres = new Pool(() => MakePrim(PrimitiveType.Sphere, "Sphere"));
            cubes = new Pool(() => MakePrim(PrimitiveType.Cube, "Cube"));
            capsules = new Pool(() => MakePrim(PrimitiveType.Capsule, "Capsule"));
            lines = new Pool(MakeLine);
        }

        void Prim(Pool pool, Vector3 pos, float px, Color c, float heightScale = 1f)
        {
            var g = pool.Next();
            float s = Mathf.Max(0.2f, PerPixel(pos) * px);
            g.transform.position = pos;
            g.transform.localScale = new Vector3(s, s * heightScale, s);
            g.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
        }

        void Line(IList<Vector3> pts, int count, float px, Color c, bool loop = false)
        {
            var g = lines.Next();
            var lr = g.GetComponent<LineRenderer>();
            lr.loop = loop;
            lr.positionCount = count;
            if (linePts.Length < count) linePts = new Vector3[count * 2];
            for (int i = 0; i < count; i++) linePts[i] = pts[i];
            lr.SetPositions(linePts);
            float w = Mathf.Max(0.05f, PerPixel(pts[count / 2]) * px);
            lr.startWidth = lr.endWidth = w;
            lr.sharedMaterial = Mat(c);
        }

        readonly List<Vector3> scratchPts = new List<Vector3>();

        void Post(Vector3 top, Color c, float px = 2f)
        {
            scratchPts.Clear();
            scratchPts.Add(new Vector3(top.x, FlightPath.Ground(top.x, top.z) + 0.2f, top.z));
            scratchPts.Add(top);
            Line(scratchPts, 2, px, new Color(c.r, c.g, c.b, 1f) * 0.8f);
        }

        void Ring(Vector3 centre, float radius, Color c, float px)
        {
            scratchPts.Clear();
            const int n = 48;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float x = centre.x + Mathf.Cos(a) * radius, z = centre.z + Mathf.Sin(a) * radius;
                scratchPts.Add(new Vector3(x, FlightPath.Ground(x, z) + 0.6f, z));
            }
            Line(scratchPts, n, px, c, true);
        }

        static Color GroupColour(PlacedKind k)
        {
            switch (k)
            {
                case PlacedKind.HostileQuad: case PlacedKind.HostileFixedWing: return new Color(1f, 0.25f, 0.2f);
                case PlacedKind.OwnIsr: return new Color(0.3f, 0.6f, 1f);
                case PlacedKind.CivilianQuad: return new Color(0.4f, 0.9f, 0.4f);
                case PlacedKind.Swarm: return new Color(1f, 0.35f, 0.9f);
                case PlacedKind.Flock: return new Color(0.35f, 0.9f, 1f);
                default: return new Color(0.7f, 0.95f, 1f);
            }
        }

        void DrawMarkers()
        {
            EnsurePools();
            spheres.Begin(); cubes.Begin(); capsules.Begin(); lines.Begin();
            Color sel = new Color(1f, 0.95f, 0.3f);

            // Paths: a line through the waypoints, a post from each down to the ground, a handle on the selected one.
            for (int i = 0; i < cfg.paths.Count; i++)
            {
                var p = cfg.paths[i];
                if (p.points.Count == 0) continue;
                bool isSel = i == pathEd.SelectedPath;
                Color col = PathEditor.PathColour(p.use);
                if (!isSel) col = Color.Lerp(col, Color.gray, 0.35f);
                int n = p.points.Count;
                if (n >= 2)
                {
                    scratchPts.Clear();
                    for (int j = 0; j < n; j++) scratchPts.Add(p.World(j));
                    Line(scratchPts, n, isSel ? 4f : 2.5f, col, p.mode == PathMode.Loop && n > 2);
                }
                for (int j = 0; j < n; j++)
                {
                    Vector3 w = p.World(j);
                    Post(w, col, isSel ? 2.5f : 1.5f);
                    bool selPoint = isSel && j == pathEd.SelectedPoint;
                    if (j == 0) Prim(cubes, w, selPoint ? 20f : 15f, selPoint ? sel : col);
                    else Prim(spheres, w, selPoint ? 20f : 14f, selPoint ? sel : col);
                    if (selPoint)
                    {
                        Vector3 top = HandleTop(w);
                        Prim(cubes, top, 14f, new Color(0.4f, 1f, 0.5f));
                        scratchPts.Clear(); scratchPts.Add(w); scratchPts.Add(top);
                        Line(scratchPts, 2, 3f, new Color(0.4f, 1f, 0.5f));
                    }
                }
            }

            // Spawn points: red posts. Player posts: a standing figure, the selected one cyan.
            for (int i = 0; i < spawns.Count; i++)
            {
                float gy = FlightPath.Ground(spawns[i].x, spawns[i].y);
                Vector3 top = new Vector3(spawns[i].x, gy + 25f, spawns[i].y);
                Post(top, new Color(1f, 0.3f, 0.25f), 3f);
                Prim(cubes, top, i == selSpawn ? 22f : 16f, i == selSpawn ? sel : new Color(1f, 0.3f, 0.25f));
            }
            for (int i = 0; i < players.Count; i++)
            {
                var pp = players[i].post;
                Vector3 b = new Vector3(pp.x, FlightPath.Ground(pp.x, pp.y) + 1f, pp.y);
                Prim(capsules, b, 22f, i == pathEd.SelectedPlayer ? new Color(0.4f, 0.9f, 1f) : Color.white, 1.6f);
            }

            // Placed groups: a ball at its height on a post, with the height handle when selected.
            for (int i = 0; i < cfg.placedGroups.Count; i++)
            {
                var g = cfg.placedGroups[i];
                Vector3 w = GroupWorld(g);
                var c = GroupColour(g.Kind);
                bool s = i == selGroup;
                Post(w, c, 2.5f);
                Prim(spheres, w, s ? 26f : 20f, s ? sel : c);
                if (g.path >= 0 && g.path < cfg.paths.Count && cfg.paths[g.path].Usable)
                {
                    scratchPts.Clear(); scratchPts.Add(w); scratchPts.Add(cfg.paths[g.path].World(1));
                    Line(scratchPts, 2, 1.5f, c);
                }
                if (s)
                {
                    Vector3 top = HandleTop(w);
                    Prim(cubes, top, 14f, new Color(0.4f, 1f, 0.5f));
                    scratchPts.Clear(); scratchPts.Add(w); scratchPts.Add(top);
                    Line(scratchPts, 2, 3f, new Color(0.4f, 1f, 0.5f));
                }
            }

            // The selected tree / bush / building.
            if (selManual >= 0 && selManual < field.Objects.Count)
            {
                var o = field.Objects[selManual];
                Ring(new Vector3(o.x, 0f, o.z), Mathf.Max(1.5f, field.RadiusOf(o)), sel, 3f);
            }

            // Brush ring, and the cleared areas while the eraser is in hand.
            if (hoverValid && ((tool == Tool.Object && brush) || tool == Tool.Erase))
                Ring(hover, brushRadius, tool == Tool.Erase ? new Color(1f, 0.4f, 0.3f) : new Color(0.5f, 1f, 0.5f), 3f);
            if (tool == Tool.Erase || (tool == Tool.Object && eraseGenerated))
                for (int i = 0; i < cfg.clearAreas.Count && i < 120; i++)
                    Ring(new Vector3(cfg.clearAreas[i].x, 0f, cfg.clearAreas[i].z), cfg.clearAreas[i].radius, new Color(1f, 0.7f, 0.2f, 1f), 1.5f);

            spheres.End(); cubes.End(); capsules.End(); lines.End();
        }

        // --- GUI ---

        void OnGUI()
        {
            if (cam == null) return;
            if (testing)
            {
                DrawTestBar();
                return;
            }
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
                small = new GUIStyle(label);
                button = new GUIStyle(GUI.skin.button) { richText = true };
                field2 = new GUIStyle(GUI.skin.textField);
                labelWorld = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleLeft };
            }
            float ui = Screen.height / 720f;
            DrawWorldLabels(ui);

            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(ui, ui, 1f));
            label.fontSize = button.fontSize = field2.fontSize = FontSize;
            small.fontSize = FontSize - 2;
            label.normal.textColor = small.normal.textColor = Color.white;
            DrawPanel();
            GUI.matrix = saved;
            DrawHelp(ui);
        }

        static void Fill(Rect r, Color c)
        {
            if (Event.current.type != EventType.Repaint) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        void Tag(Vector3 world, string text, Color c, float ui, float dy = -4f)
        {
            if (!Screen2(world, out var px)) return;
            var size = labelWorld.CalcSize(new GUIContent(text));
            var r = new Rect(px.x + 8f * ui, Screen.height - px.y - size.y * 0.5f + dy, size.x + 6f, size.y);
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            var old = GUI.color; GUI.color = c;
            GUI.Label(r, text, labelWorld);
            GUI.color = old;
        }

        void DrawWorldLabels(float ui)
        {
            if (Event.current.type != EventType.Repaint) return;
            labelWorld.fontSize = Mathf.RoundToInt(12f * ui);
            labelWorld.normal.textColor = Color.white;
            for (int i = 0; i < cfg.paths.Count; i++)
            {
                var p = cfg.paths[i];
                bool isSel = i == pathEd.SelectedPath;
                for (int j = 0; j < p.points.Count; j++)
                {
                    if (!isSel && j != 0) continue;
                    var w = p.points[j];
                    string t = j == 0 ? $"{p.name}  S  {w.alt:F0} m" : $"{j}  {w.alt:F0} m" + (w.speed > 0f ? $"  {w.speed:F0} m/s" : "");
                    Tag(p.World(j), t, PathEditor.PathColour(p.use), ui);
                }
            }
            for (int i = 0; i < cfg.placedGroups.Count; i++)
            {
                var g = cfg.placedGroups[i];
                Tag(GroupWorld(g), $"{PlacedGroup.Label(g.Kind)} x{g.count}  {g.alt:F0} m  at {g.time:F0} s", GroupColour(g.Kind), ui, 14f);
            }
            for (int i = 0; i < players.Count; i++)
                Tag(new Vector3(players[i].post.x, FlightPath.Ground(players[i].post.x, players[i].post.y) + 3f, players[i].post.y), players[i].name, Color.white, ui);
            for (int i = 0; i < spawns.Count; i++)
                Tag(new Vector3(spawns[i].x, FlightPath.Ground(spawns[i].x, spawns[i].y) + 27f, spawns[i].y), "spawn " + (i + 1), new Color(1f, 0.5f, 0.45f), ui);
        }

        void DrawHelp(float ui)
        {
            if (Event.current.type != EventType.Repaint) return;
            labelWorld.fontSize = Mathf.RoundToInt(13f * ui);
            string hover2 = hoverValid ? $"   ground {hover.x:F0} E, {hover.z:F0} N, {hover.y:F0} m" : "";
            string text = $"<b>{ToolNames[(int)tool]}</b>   right mouse + W A S D fly (Q down, E up, Shift fast, scroll = speed {flySpeed:F0} m/s)   F frame   Del delete   Ctrl+Z / Ctrl+Y{hover2}";
            var size = labelWorld.CalcSize(new GUIContent(text));
            var r = new Rect(PanelWidth * ui + 10f, Screen.height - size.y - 8f, size.x + 12f, size.y + 4f);
            Fill(r, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(r, text, labelWorld);
        }

        void DrawTestBar()
        {
            if (label == null) return;
            float ui = Screen.height / 720f;
            var r = new Rect(Screen.width - 250f * ui, 6f * ui, 244f * ui, 32f * ui);
            button.fontSize = Mathf.RoundToInt(14f * ui);
            if (GUI.Button(r, "Back to the 3D editor", button)) BackToEditor();
        }

        void DrawPanel()
        {
            const float pad = 8f, row = 26f;
            float w = PanelWidth;
            Fill(new Rect(0f, 0f, w, 720f), new Color(0.05f, 0.06f, 0.08f, 0.94f));
            GUILayout.BeginArea(new Rect(pad, pad, w - pad * 2f, 720f - pad * 2f));
            GUILayout.Label("<b>3D SCENARIO EDITOR</b>", new GUIStyle(label) { fontSize = FontSize + 4 });
            // Actions.
            GUILayout.BeginHorizontal();
            GUI.enabled = undoStack.Count > 0;
            if (GUILayout.Button("Undo", button, GUILayout.Height(row))) Undo();
            GUI.enabled = redoStack.Count > 0;
            if (GUILayout.Button("Redo", button, GUILayout.Height(row))) Redo();
            GUI.enabled = true;
            var old = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.4f, 0.9f, 0.5f);
            if (GUILayout.Button("<b>Test run from here</b>", button, GUILayout.Height(row))) TestRun();
            GUI.backgroundColor = old;
            if (GUILayout.Button("Exit", button, GUILayout.Width(52f), GUILayout.Height(row))) ExitToMenu();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Package", small, GUILayout.Width(56f), GUILayout.Height(row));
            packageName = GUILayout.TextField(packageName ?? "", 40, field2, GUILayout.Height(row));
            if (GUILayout.Button("Save package", button, GUILayout.Width(110f), GUILayout.Height(row))) SavePackage();
            GUILayout.EndHorizontal();
            GUILayout.Label($"{cfg.paths.Count} paths   {spawns.Count} spawns   {players.Count} posts   {cfg.placedGroups.Count} groups   {field.Objects.Count} objects ({field.Drawn} drawn)   {cfg.clearAreas.Count} cleared", small);
            if (status.Length > 0) GUILayout.Label("<color=#9FD8FF>" + status + "</color>", small);

            // Tools.
            var tn = new string[ToolNames.Length];
            for (int i = 0; i < tn.Length; i++) tn[i] = (i + 1) + " " + ToolNames[i];
            int t = GUILayout.SelectionGrid((int)tool, tn, 4, button, GUILayout.Height(row * 2f + 4f));
            if (t != (int)tool) { tool = (Tool)t; boundary = true; }

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            switch (tool)
            {
                case Tool.Select: SelectPanel(row); break;
                case Tool.Path: PathPanelUi(row); break;
                case Tool.Spawn: SpawnPanel(row); break;
                case Tool.Post: PostPanel(row); break;
                case Tool.Group: GroupPanel(row); break;
                case Tool.Object: ObjectPanel(row); break;
                case Tool.Erase: ErasePanel(row); break;
            }
            GUILayout.Space(10f);
            optionsOpen = GUILayout.Toggle(optionsOpen, optionsOpen ? "Scenario options (click to fold)" : "Scenario options (click to open)", button, GUILayout.Height(row));
            if (optionsOpen)
            {
                GUI.changed = false;
                ScenarioOptionsPanel.Draw(cfg, "ed3d", label, small, field2, button, row, 150f);
                if (GUI.changed) cfg.Validate();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void SelectPanel(float row)
        {
            GUILayout.Label("Click a waypoint, spawn point, post, group, tree, bush or building to select it; drag to move it. The arrow above a waypoint or group changes its height. Press F to fly to it, Delete to remove it.", small);
            if (selManual >= 0 && selManual < field.Objects.Count) ManualPanel(row);
            else if (selGroup >= 0 && selGroup < cfg.placedGroups.Count) GroupPanel(row);
            else PathPanelUi(row);
        }

        void PathPanelUi(float row)
        {
            GUILayout.Label("<b>Path tool</b>: click the ground to add a waypoint to the selected path (a new drone path is made if none is selected); click close to a line to insert one. Drag a waypoint on the ground; drag the green arrow above it for its height. Height and speed are typeable below.", small);
            pathEd.Bind(label, small, button, field2, FontSize);
            pathEd.PathPanel(row);
        }

        void SpawnPanel(float row)
        {
            GUILayout.Label($"<b>Spawn points: {spawns.Count}</b>   Click the ground to add a red post; drag to move; Delete removes the selected one.", small);
            GUILayout.Label(spawns.Count == 0 ? $"None placed: drones appear on a {cfg.spawnRingRadius:F0} m ring in front of the post." : "Drones appear at these points, spread about 30 m around each. A drone, swarm or flock that has a path appears at its first waypoint instead.", small);
            if (GUILayout.Button("Clear spawn points (use the ring)", button, GUILayout.Height(row))) { spawns.Clear(); selSpawn = -1; }
        }

        void PostPanel(float row)
        {
            GUILayout.Label("<b>Player posts</b>   Select a player, then click the ground to place their post, or drag the white figure.", small);
            for (int i = 0; i < players.Count; i++)
                if (GUILayout.Toggle(pathEd.SelectedPlayer == i, $"{players[i].name}{(players[i].isLocal ? " (this machine)" : "")}   at {players[i].post.x:F0} E, {players[i].post.y:F0} N", button, GUILayout.Height(row))) pathEd.SelectedPlayer = i;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Add player", button, GUILayout.Height(row)))
            {
                players.Add(new PlayerSlot { name = "Player " + (players.Count + 1), post = new Vector2(150f * players.Count, 0f) });
                pathEd.SelectedPlayer = players.Count - 1;
            }
            if (GUILayout.Button("Remove last", button, GUILayout.Height(row)) && players.Count > 1)
            {
                players.RemoveAt(players.Count - 1);
                pathEd.SelectedPlayer = Mathf.Min(pathEd.SelectedPlayer, players.Count - 1);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("The flat pad under a post is built with the terrain, so it stays at the old spot until the terrain is built again (Exit, then START or 3D editor).", small);
        }

        // Drone and bird groups: the template for the next one, or the selected group.
        void GroupPanel(float row)
        {
            bool editing = selGroup >= 0 && selGroup < cfg.placedGroups.Count;
            GUILayout.Label(editing ? "<b>Selected group</b>   (changes apply at once)" : "<b>Place a group</b>   Pick what it is, then click the ground. It appears at its time with the settings below.", small);
            var g = editing ? cfg.placedGroups[selGroup] : template;
            var names = new string[7];
            for (int i = 0; i < 7; i++) names[i] = PlacedGroup.Label((PlacedKind)i);
            int k = GUILayout.SelectionGrid(g.kind, names, 2, button, GUILayout.Height(row * 4f + 8f));
            if (k != g.kind) { g.kind = k; g.path = -1; if (k == (int)PlacedKind.Swarm && g.count < 8) g.count = 8; if (k == (int)PlacedKind.Flock && g.count < 8) g.count = 8; }
            var kind = g.Kind;
            GUILayout.BeginHorizontal();
            g.count = NumberEntry.Stepper("ed3d.count", kind == PlacedKind.Swarm ? "Drones" : kind == PlacedKind.Flock ? "Birds" : "Count", g.count, kind == PlacedKind.Swarm || kind == PlacedKind.Flock ? 8 : 1, 1, int.MaxValue, label, field2, button, row, 70f);
            GUILayout.EndHorizontal();
            if (kind == PlacedKind.Swarm && g.count > SwarmSky.Threshold) GUILayout.Label($"<color=#FFB040>A placed swarm has at most {SwarmSky.Threshold} drones; the scenario's own swarm (Scenario tab) is the big one.</color>", small);
            if (kind == PlacedKind.Flock || kind == PlacedKind.Bird)
            {
                if (GUILayout.Button("Species: <b>" + BirdSpeciesInfo.Name(g.species) + "</b>", button, GUILayout.Height(row))) g.species = (g.species + 1) % BirdSpeciesInfo.Count;
            }
            GUILayout.BeginHorizontal();
            g.alt = NumberEntry.Stepper("ed3d.galt", "Height m", Mathf.RoundToInt(g.alt), 10, 0, int.MaxValue, label, field2, button, row, 70f);
            g.time = NumberEntry.Stepper("ed3d.gtime", "At s", Mathf.RoundToInt(g.time), 5, 0, int.MaxValue, label, field2, button, row, 40f);
            GUILayout.EndHorizontal();

            if (editing)
            {
                string pathName = g.path >= 0 && g.path < cfg.paths.Count ? cfg.paths[g.path].name : "none (" + (kind == PlacedKind.HostileQuad || kind == PlacedKind.HostileFixedWing ? "flies at the post" : kind == PlacedKind.Swarm ? "flies at the post" : kind == PlacedKind.Flock || kind == PlacedKind.Bird ? "crosses over the post" : "loiters here") + ")";
                if (GUILayout.Button("Path: <b>" + pathName + "</b>  (click for the next)", button, GUILayout.Height(row)))
                {
                    g.path = NextPath(kind, g.path);
                    if (g.path >= 0)
                    {
                        var first = cfg.paths[g.path].points[0];
                        g.x = first.x; g.z = first.z; g.alt = first.alt;
                    }
                }
                GUILayout.Label("A group with a path appears at the path's first waypoint and flies it. Draw paths with the Path tool (pick the right flown-by: drone, swarm, bird or flock).", small);
                if (GUILayout.Button("Delete this group", button, GUILayout.Height(row))) { cfg.placedGroups.RemoveAt(selGroup); selGroup = -1; return; }
                cfg.placedGroups[selGroup] = g;
            }
            else template = g;
        }

        // The next usable path that fits the group's kind, after `current`; -1 (none) after the last.
        int NextPath(PlacedKind kind, int current)
        {
            var use = PlacedGroup.UseOf(kind);
            for (int i = current + 1; i < cfg.paths.Count; i++)
            {
                var p = cfg.paths[i];
                if (p.Usable && p.use == use && (use != PathUse.Drone || p.cls < 0 || p.cls == (int)kind)) return i;
            }
            return -1;
        }

        void ObjectPanel(float row)
        {
            GUILayout.Label("<b>Object tool</b>: pick a tree, bush, rock or building from the kit, then click the ground. With the brush on, hold the left button and paint.", small);
            string[] kinds = ManualKit.KindNames;
            int k = GUILayout.SelectionGrid(palKind, kinds, 4, button, GUILayout.Height(row));
            if (k != palKind) { palKind = k; palGroup = 0; palVariant = 0; RefreshPalette(); }
            var groups = ManualKit.GroupNames(kit, palKind);
            if (groups.Length > 1)
            {
                var avail = new List<string>(); var ids = new List<int>();
                for (int g = 0; g < groups.Length; g++) { bool has = false; foreach (var e in palette) if (e.group == g) { has = true; break; } if (has) { avail.Add(groups[g]); ids.Add(g); } }
                if (avail.Count > 0)
                {
                    int cur = Mathf.Max(0, ids.IndexOf(palGroup));
                    int pick = GUILayout.SelectionGrid(cur, avail.ToArray(), 4, button, GUILayout.Height(row * Mathf.Ceil(avail.Count / 4f)));
                    if (pick != cur || ids[pick] != palGroup) { palGroup = ids[pick]; palVariant = 0; }
                }
            }
            if (palette.Count == 0) GUILayout.Label("The layer kit has no prefab of this kind.", small);
            else
            {
                mixVariants = GUILayout.Toggle(mixVariants, "Mix the variants of this group (random one per object)", button, GUILayout.Height(row));
                paletteScroll = GUILayout.BeginScrollView(paletteScroll, GUILayout.Height(150f));
                foreach (var e in palette)
                    if (e.group == palGroup)
                        if (GUILayout.Toggle(!mixVariants && palVariant == e.variant, e.label, button, GUILayout.Height(row - 2f))) { palVariant = e.variant; mixVariants = false; }
                GUILayout.EndScrollView();
            }
            GUILayout.BeginHorizontal();
            scalePct = NumberEntry.Stepper("ed3d.scale", "Size %", scalePct, 10, 5, int.MaxValue, label, field2, button, row, 60f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            randomYaw = GUILayout.Toggle(randomYaw, "Random turn", button, GUILayout.Height(row));
            randomScale = GUILayout.Toggle(randomScale, "Random size", button, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            brush = GUILayout.Toggle(brush, brush ? "Brush ON (hold the left button and paint)" : "Brush off (one object per click)", button, GUILayout.Height(row));
            if (brush)
            {
                GUILayout.BeginHorizontal();
                brushRadius = NumberEntry.Stepper("ed3d.brushR", "Radius m", brushRadius, 5, 1, int.MaxValue, label, field2, button, row, 80f);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                density = NumberEntry.Stepper("ed3d.density", "Per 1000 m2", density, 5, 1, int.MaxValue, label, field2, button, row, 90f);
                GUILayout.EndHorizontal();
                GUILayout.Label("The brush tops the circle up to that many objects of this kind and never past it, so painting twice does not double.", small);
            }
            if (selManual >= 0 && selManual < field.Objects.Count) ManualPanel(row);
        }

        void ManualPanel(float row)
        {
            var o = field.Objects[selManual];
            var entry = ManualKit.Resolve(kit, o.kind, o.group, o.variant, out _);
            GUILayout.Space(6f);
            GUILayout.Label($"<b>Selected: {(entry != null ? entry.name : "object")}</b>  ({ManualKit.KindNames[Mathf.Clamp(o.kind, 0, 3)]})   at {o.x:F0} E, {o.z:F0} N", label);
            GUILayout.BeginHorizontal();
            int yawNow = Mathf.RoundToInt(Mathf.Repeat(o.yaw, 360f));
            int yawNew = NumberEntry.Stepper("ed3d.yaw", "Turn deg", yawNow, 15, 0, 360, label, field2, button, row, 70f);
            int pctNow = Mathf.RoundToInt(o.scale * 100f);
            int pctNew = NumberEntry.Stepper("ed3d.size", "Size %", pctNow, 10, 5, int.MaxValue, label, field2, button, row, 60f);
            GUILayout.EndHorizontal();
            if (yawNew != yawNow || pctNew != pctNow)
            {
                var n = o;
                if (yawNew != yawNow) n.yaw = yawNew;
                if (pctNew != pctNow) n.scale = pctNew / 100f;
                boundary = true;
                OpSet(selManual, n);
                EndStroke();
            }
            if (GUILayout.Button("Delete this object", button, GUILayout.Height(row))) { OpRemove(selManual); EndStroke(); selManual = -1; }
        }

        void ErasePanel(float row)
        {
            GUILayout.Label("<b>Erase tool</b>: click or hold the left button over the ground.", small);
            GUILayout.BeginHorizontal();
            brushRadius = NumberEntry.Stepper("ed3d.eraseR", "Radius m", brushRadius, 5, 1, int.MaxValue, label, field2, button, row, 80f);
            GUILayout.EndHorizontal();
            eraseManual = GUILayout.Toggle(eraseManual, "Delete the objects placed by hand", button, GUILayout.Height(row));
            eraseGenerated = GUILayout.Toggle(eraseGenerated, "Clear the generated trees, bushes and buildings", button, GUILayout.Height(row));
            eraseRestore = GUILayout.Toggle(eraseRestore, "Bring back generated ones (removes cleared circles)", button, GUILayout.Height(row));
            GUILayout.Label("Cleared areas (orange rings) are saved in the scenario; the generated layers are rebuilt without them a moment after you let go, and at every run start. Grass is not cleared.", small);
            if (GUILayout.Button($"Remove all {cfg.clearAreas.Count} cleared areas", button, GUILayout.Height(row))) cfg.clearAreas.Clear();
        }
    }
}
