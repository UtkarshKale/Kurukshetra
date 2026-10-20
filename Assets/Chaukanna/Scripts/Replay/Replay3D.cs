using System;
using System.Collections.Generic;
using System.IO;
using Chaukanna.Scenario;
using Chaukanna.Scoring;
using Chaukanna.Trainer;
using UnityEngine;
using UnityEngine.InputSystem;
using C = Chaukanna.Trainer.DashboardCharts;

namespace Chaukanna.Replay
{
    // The 3D replay: the recorded run played back inside the field scene (MixedSky, already loaded). The live sim is frozen and
    // hidden, and every contact, group and seat is a simple proxy driven from the black box. A reconstruction from logged poses,
    // not a screen recording.
    public class Replay3D : MonoBehaviour
    {
        public enum CamMode { Follow, Chase, Free, Top }

        public static Replay3D Instance { get; private set; }
        public static bool Active => Instance != null;

        public BlackBoxData Data { get; private set; }
        public Camera ReplayCamera => cam;
        public float Time01 => end > 0f ? t / end : 0f;
        public float T => t;
        public bool Playing => playing;
        public float Speed => speed;
        public CamMode Mode => mode;
        public VoiceMode Voice => voiceMode;
        public bool VoiceAvailable => bank != null && bank.HasAudio;
        public string Note { get; private set; } = "";

        // ---- Opening and closing ----

        // From the dashboard: the latest run's black box (in memory, or the file named in the session result).
        public static bool OpenLatest(ScoreKeeper keeper, out string why)
        {
            why = "";
            if (Active) return true;
            var data = BlackBoxRecorder.Latest;
            if ((data == null || data.seats.Count == 0) && keeper != null && keeper.Last != null && !string.IsNullOrEmpty(keeper.Last.blackBox) && File.Exists(keeper.Last.blackBox))
            {
                try { data = BlackBoxData.Load(keeper.Last.blackBox); }
                catch (Exception e) { why = "Black box could not be read: " + e.Message; return false; }
            }
            if (data == null || data.seats.Count == 0) { why = "No black box yet. It records while a run is going."; return false; }
            if (data.EndTime <= 0.5f) { why = "The black box is empty so far."; return false; }
            return Open(data) != null;
        }

        public static Replay3D Open(BlackBoxData data)
        {
            if (Instance != null) return Instance;
            var go = new GameObject("Replay3D");
            var r = go.AddComponent<Replay3D>();
            r.Begin(data);
            return r;
        }

        public void Exit() { Destroy(gameObject); }

        // State saved while the replay is up.
        ScenarioSpawner spawner;
        bool spawnerWasEnabled;
        readonly List<GameObject> hiddenContacts = new List<GameObject>();
        readonly List<Behaviour> disabledSkies = new List<Behaviour>();
        bool prevHide, prevBlocked, prevSplit;
        CursorLockMode prevLock;
        bool prevCursorVisible;
        ScoreDashboard dashboard;
        TrainerScreen trainer;
        bool dashWasOpen, dashWasExpanded;

        void Begin(BlackBoxData data)
        {
            Instance = this;
            Data = data;
            data.MergeDuplicateSeats();         // one person recorded under two seat ids showed as two "Player 1" buttons and two view cones
            end = Mathf.Max(0.5f, data.EndTime);
            spawner = FindAnyObjectByType<ScenarioSpawner>();
            dashboard = FindAnyObjectByType<ScoreDashboard>();
            trainer = FindAnyObjectByType<TrainerScreen>();
            if (spawner != null) { spawnerWasEnabled = spawner.enabled; spawner.enabled = false; }
            foreach (var c in ContactRegistry.Live.ToArray()) if (c != null && c.gameObject.activeSelf) { c.gameObject.SetActive(false); hiddenContacts.Add(c.gameObject); }
            foreach (var s in SwarmSky.All.ToArray()) if (s != null && s.enabled) { s.enabled = false; disabledSkies.Add(s); }
            var crowd = CrowdSky.Active;
            if (crowd != null && crowd.enabled) { crowd.enabled = false; disabledSkies.Add(crowd); }

            prevHide = PlayerView.HideOverlays; prevBlocked = ContactRegistry.InputBlocked; prevSplit = PlayerView.Split;
            prevLock = Cursor.lockState; prevCursorVisible = Cursor.visible;
            if (dashboard != null) { dashWasOpen = dashboard.IsOpen; dashWasExpanded = dashboard.Expanded; dashboard.Close(); }
            PlayerView.HideOverlays = true;
            ContactRegistry.InputBlocked = true;
            if (trainer != null && prevSplit) trainer.SetSplit(false);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;

            var camGo = new GameObject("Replay3D Camera");
            camGo.transform.SetParent(transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.depth = 100f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 9000f;
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            audioSrc = gameObject.AddComponent<AudioSource>();
            audioSrc.spatialBlend = 0f; audioSrc.playOnAwake = false; audioSrc.loop = false;

            Vector3 o = data.origin;
            topSize = Mathf.Max(200f, data.spawnRadius * 0.7f);
            topCentre = o + Quaternion.Euler(0f, data.sectorYaw, 0f) * Vector3.forward * (data.spawnRadius * 0.4f);
            BuildStatic();
            mode = CamMode.Top;
            selSeat = 0;
            string folder = ReplayVoiceBank.FindFolder(data.voiceFolder, BlackBoxRecorder.SessionsFolder);
            bank = string.IsNullOrEmpty(folder) ? null : ReplayVoiceBank.Load(folder);
            Note = bank != null && bank.HasAudio ? $"Voice recording: {bank.index.bursts.Count} bursts from {bank.SeatCount} seats." : "No voice recording for this run (solo run, or voice off).";
            Apply();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (audioSrc != null) audioSrc.Stop();
            if (voiceClip != null) Destroy(voiceClip);
            foreach (var go in hiddenContacts) if (go != null) go.SetActive(true);
            foreach (var b in disabledSkies) if (b != null) b.enabled = true;
            if (spawner != null) spawner.enabled = spawnerWasEnabled;
            PlayerView.HideOverlays = prevHide;
            ContactRegistry.InputBlocked = prevBlocked;
            if (trainer != null && prevSplit) trainer.SetSplit(true);
            Cursor.lockState = prevLock; Cursor.visible = prevCursorVisible;
            if (dashboard != null && dashWasOpen) { dashboard.Open(); dashboard.SetExpanded(dashWasExpanded); }
            if (!buildingsOn) SetBuildings(true);
            foreach (var m in mats.Values) if (m != null) Destroy(m);
        }

        // The terrain's building layer (the real OSM buildings: close to the post they are big solid blocks) can be hidden.
        bool buildingsOn = true;
        void SetBuildings(bool on)
        {
            buildingsOn = on;
            var m = Chaukanna.TerrainGen.TerrainLayerManager.Current;
            if (m != null) m.SetLayerVisible(Chaukanna.TerrainGen.LayerKind.Buildings, on);
        }

        // A click picks the contact nearest the cursor (within 30 px): it gets a label and a trail, whatever its class.
        void PickContact(Vector2 mouse)
        {
            float best = 30f; int pick = -1;
            for (int i = 0; i < st.ContactCount; i++)
            {
                var c = st.Contact(i);
                Vector3 sp = cam.WorldToScreenPoint(c.pos);
                if (sp.z <= 0f) continue;
                float d = Vector2.Distance(new Vector2(sp.x, sp.y), mouse);
                if (d < best) { best = d; pick = c.id; }
            }
            selContact = pick;
        }

        // ---- Clock ----

        Camera cam;
        float t, end, speed = 1f;
        bool playing, voiceSeek;
        CamMode mode;
        int selSeat;
        bool truthOverlay = true;
        readonly BbState st = new BbState();
        readonly List<BbEvent> recent = new List<BbEvent>();

        public void SetTime(float time) { t = Mathf.Clamp(time, 0f, end); voiceSeek = true; }
        public void SetPlaying(bool on) { if (on && t >= end - 0.01f) SetTime(0f); playing = on; }
        public void SetMode(CamMode m) { if (m == CamMode.Free && mode != CamMode.Free) InitFree(); mode = m; }
        public void SelectSeat(int index) { if (Data.seats.Count > 0) selSeat = (index % Data.seats.Count + Data.seats.Count) % Data.seats.Count; voiceDirty = true; }
        public void SetSpeed(float s) { speed = Mathf.Clamp(s, 0.25f, 8f); }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.escapeKey.wasPressedThisFrame) { Exit(); return; }
                if (kb.spaceKey.wasPressedThisFrame) SetPlaying(!playing);
                if (kb.pageDownKey.wasPressedThisFrame) { float n = Data.NextEventTime(t); if (n >= 0f) SetTime(n); }
                if (kb.pageUpKey.wasPressedThisFrame) { float p = Data.PrevEventTime(t); SetTime(p >= 0f ? p : 0f); }
                if (kb.commaKey.wasPressedThisFrame) SetTime(t - 0.1f);
                if (kb.periodKey.wasPressedThisFrame) SetTime(t + 0.1f);
                if (kb.digit1Key.wasPressedThisFrame) SetMode(CamMode.Follow);
                if (kb.digit2Key.wasPressedThisFrame) SetMode(CamMode.Chase);
                if (kb.digit3Key.wasPressedThisFrame) SetMode(CamMode.Free);
                if (kb.digit4Key.wasPressedThisFrame) SetMode(CamMode.Top);
            }
            var ms = Mouse.current;
            if (ms != null && cam != null && ms.leftButton.wasPressedThisFrame && !MouseOverBar()) PickContact(ms.position.ReadValue());
            if (playing)
            {
                t += Mathf.Min(UnityEngine.Time.unscaledDeltaTime, 0.1f) * speed;
                if (t >= end) { t = end; playing = false; }
            }
        }

        void LateUpdate()
        {
            Apply();
            DriveCamera();
            VoiceUpdate();
        }

        // ---- Materials and primitives ----

        readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

        Material Mat(Color c)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c);
            if (mats.TryGetValue(key, out var m) && m != null) return m;
            var src = RuntimeMaterials.Unlit != null ? RuntimeMaterials.Unlit : RuntimeMaterials.Lit;
            m = src != null ? new Material(src) : new Material(RuntimeMaterials.Find("Universal Render Pipeline/Unlit"));
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            mats[key] = m;
            return m;
        }

        GameObject Prim(PrimitiveType type, Color c, string name)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(c);
            go.transform.SetParent(transform, false);
            return go;
        }

        class LineBatch
        {
            readonly Mesh mesh;
            readonly List<Vector3> v = new List<Vector3>();
            readonly List<int> idx = new List<int>();
            public readonly GameObject go;
            public LineBatch(string name, Material m, Transform parent)
            {
                go = new GameObject(name);
                go.transform.SetParent(parent, false);
                mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = m;
            }
            public void Begin() { v.Clear(); idx.Clear(); }
            public void Add(Vector3 a, Vector3 b) { idx.Add(v.Count); v.Add(a); idx.Add(v.Count); v.Add(b); }
            public void Circle(Vector3 c, float r, int segs = 28)
            {
                Vector3 prev = c + new Vector3(r, 0f, 0f);
                for (int i = 1; i <= segs; i++)
                {
                    float a = i * Mathf.PI * 2f / segs;
                    Vector3 p = c + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    Add(prev, p); prev = p;
                }
            }
            public void End()
            {
                mesh.Clear();
                if (v.Count == 0) return;
                mesh.SetVertices(v);
                mesh.SetIndices(idx, MeshTopology.Lines, 0);
                mesh.RecalculateBounds();
            }
            public void Destroy() { if (mesh != null) UnityEngine.Object.Destroy(mesh); }
        }

        LineBatch lockLines, tracers, rings, callRings, talkRings, staticLines;
        // Trails: one batch per truth class (hostile, friendly, civilian, bird) and per age band (old, middle, new), so they fade.
        readonly LineBatch[,] trailBands = new LineBatch[4, 3];
        static readonly float[] BandBright = { 0.3f, 0.6f, 1f };
        readonly Dictionary<int, LineBatch> frusta = new Dictionary<int, LineBatch>();
        readonly Dictionary<int, GameObject> avatars = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> contactGo = new Dictionary<int, GameObject>();
        readonly Dictionary<int, List<GameObject>> groupGo = new Dictionary<int, List<GameObject>>();
        // Seat colours: greens, ambers and teals only (blue is friendly, red hostile, yellow birds, white civilian; nothing purple).
        static readonly Color[] SeatColours =
        {
            new Color(0.4f, 1f, 0.5f), new Color(1f, 0.8f, 0.3f), new Color(0.3f, 0.9f, 0.8f),
            new Color(0.8f, 1f, 0.35f), new Color(1f, 0.6f, 0.25f), new Color(0.7f, 1f, 0.85f),
        };
        static Color SeatColour(int index) => SeatColours[((index % SeatColours.Length) + SeatColours.Length) % SeatColours.Length];

        // The replay's own truth colours: hostile red, friendly blue, civilian white / grey, bird yellow.
        public static Color ClassColour(int truth)
        {
            switch ((ContactClass)truth)
            {
                case ContactClass.HostileQuad: return new Color(0.95f, 0.15f, 0.12f);
                case ContactClass.HostileFixedWing: return new Color(1f, 0.2f, 0.1f);
                case ContactClass.FriendlyISR: return new Color(0.2f, 0.5f, 1f);
                case ContactClass.CivilianQuad: return new Color(0.85f, 0.86f, 0.9f);
                case ContactClass.Bird: return new Color(1f, 0.88f, 0.1f);
                default: return new Color(0.7f, 0.7f, 0.72f);
            }
        }

        static Color LabelColour(Color c) => Color.Lerp(c, Color.white, 0.4f);        // readable on a dark sky (pure blue is not)
        static readonly Color NeutralColour = new Color(0.7f, 0.7f, 0.72f);

        static string ClassName(int truth)
        {
            switch ((ContactClass)truth)
            {
                case ContactClass.HostileQuad: return "Hostile quad";
                case ContactClass.HostileFixedWing: return "Hostile fixed-wing";
                case ContactClass.FriendlyISR: return "Own ISR";
                case ContactClass.CivilianQuad: return "Civilian drone";
                case ContactClass.Bird: return "Bird";
                default: return "Unknown";
            }
        }

        static int TrailClass(int truth) => truth <= 1 ? 0 : truth == 2 ? 1 : truth == 3 ? 2 : 3;

        void BuildStatic()
        {
            lockLines = new LineBatch("locks", Mat(Color.white), transform);
            tracers = new LineBatch("tracers", Mat(new Color(1f, 0.9f, 0.2f)), transform);
            rings = new LineBatch("jam rings", Mat(new Color(1f, 0.6f, 0.1f)), transform);
            callRings = new LineBatch("call rings", Mat(new Color(0.8f, 0.9f, 1f)), transform);
            talkRings = new LineBatch("talk rings", Mat(new Color(0.4f, 1f, 0.6f)), transform);
            for (int k = 0; k < 4; k++)
            {
                Color basis = k == 0 ? ClassColour(0) : k == 1 ? ClassColour(2) : k == 2 ? ClassColour(3) : ClassColour(4);
                for (int b = 0; b < 3; b++) trailBands[k, b] = new LineBatch("trail " + k + "." + b, Mat(new Color(basis.r * BandBright[b], basis.g * BandBright[b], basis.b * BandBright[b], 1f)), transform);
            }
            staticLines = new LineBatch("post", Mat(new Color(1f, 1f, 1f, 1f)), transform);
            staticLines.Begin();
            Vector3 o = Data.origin;
            staticLines.Add(o, o + Vector3.up * 60f);
            staticLines.Circle(o + Vector3.up * 0.5f, 15f);
            if (Data.spawnRadius > 10f) staticLines.Circle(o + Vector3.up * 2f, Data.spawnRadius, 96);
            staticLines.End();
        }

        // ---- Applying a moment ----

        // prio 0 = always tried first (seats, the selected contact, lock holders), 1 = an event or a group, 2 = a contact near the camera.
        struct Label { public Vector3 world; public string text; public Color colour; public int prio; public float dist; }
        readonly List<Label> labels = new List<Label>();
        string hudLine = "";

        // Natural size of the proxy (a uniform scale of the shape) and the shape's span at scale 1, in metres.
        static float NaturalScale(int truth)
        {
            switch ((ContactClass)truth)
            {
                case ContactClass.HostileFixedWing: return 1.6f;
                case ContactClass.FriendlyISR: return 1.2f;
                default: return 1f;
            }
        }

        static float SpanOf(int truth) => truth == (int)ContactClass.HostileFixedWing || truth == (int)ContactClass.FriendlyISR ? 1.4f : 1f;

        static Mesh MeshFor(int truth)
        {
            switch ((ContactClass)truth)
            {
                case ContactClass.Bird: return ReplayShapes.Bird;
                case ContactClass.HostileFixedWing:
                case ContactClass.FriendlyISR: return ReplayShapes.FixedWing;
                default: return ReplayShapes.Quad;
            }
        }

        const float MinContactPx = 10f, MinAgentPx = 6f;

        // How many metres wide something has to be, where it is, to cover `px` pixels of the replay view.
        float PxToMetres(Vector3 p, float px)
        {
            float h = Mathf.Max(1f, Screen.height);
            if (cam.orthographic) return px * 2f * cam.orthographicSize / h;
            float d = Vector3.Distance(cam.transform.position, p);
            return px * 2f * d * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) / h;
        }

        GameObject NewProxy(string name, Mesh mesh, Color c)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat(c);
            return go;
        }

        // One marker per contact id for the whole replay: the same id is always the same object, so it moves rather than jumps.
        GameObject ContactProxy(int id, int truth)
        {
            if (contactGo.TryGetValue(id, out var go) && go != null) return go;
            go = NewProxy("c" + id, MeshFor(truth), ClassColour(truth));
            contactGo[id] = go;
            return go;
        }

        void SetColour(GameObject go, Color c)
        {
            var r = go.GetComponent<MeshRenderer>();
            var m = Mat(c);
            if (r != null && r.sharedMaterial != m) r.sharedMaterial = m;
        }

        readonly HashSet<int> seen = new HashSet<int>();
        readonly HashSet<int> eventLabelled = new HashSet<int>();
        readonly List<KeyValuePair<float, int>> nearList = new List<KeyValuePair<float, int>>();
        readonly List<KeyValuePair<float, int>> trailList = new List<KeyValuePair<float, int>>();
        int selContact = -1;

        string ContactText(BbContactState c)
        {
            string txt = "#" + c.id;
            if (truthOverlay) txt += " " + ClassName(c.truth);
            if (c.call != 0) txt += "  call " + (PlayerCall)c.call;
            if (c.action != 0) txt += "  " + (PlayerAction)c.action;
            return txt;
        }

        // Where the contact is heading at that moment, smoothed over 0.6 s of the log so the marker does not twitch with the 0.25 m snapping.
        Vector3 Velocity(BbContact bc, float time, BbContactState c)
        {
            var p1 = SamplePos(bc, time + 0.3f, out bool o1);
            var p0 = SamplePos(bc, time - 0.3f, out bool o0);
            if (o1 && o0) return (p1 - p0) / 0.6f;
            if (o1) return (p1 - c.pos) / 0.3f;
            if (o0) return (c.pos - p0) / 0.3f;
            return c.vel;
        }

        public void Apply()
        {
            if (Data == null || cam == null) return;
            Data.StateAt(t, st);
            labels.Clear();
            seen.Clear();
            eventLabelled.Clear();
            Vector3 camPos = cam.transform.position;

            // Seats.
            Vector3 sel = Vector3.zero; bool haveSel = false;
            for (int i = 0; i < st.SeatCount; i++)
            {
                var s = st.Seat(i);
                int idx = SeatIndex(s.id);
                var col = SeatColour(idx);
                if (!avatars.TryGetValue(s.id, out var av) || av == null) { av = Prim(PrimitiveType.Capsule, col, "seat " + s.id); avatars[s.id] = av; }
                if (!frusta.TryGetValue(s.id, out var fr)) { fr = new LineBatch("view " + s.id, Mat(col), transform); frusta[s.id] = fr; }
                bool mine = idx == selSeat;
                if (mine) { sel = s.pos; haveSel = true; }
                // In the follow view the selected seat is the camera; another seat standing at the same spot (within 3 m) would
                // only show a second view cone and a label right on top of the picture.
                bool hide = mode == CamMode.Follow && (mine || Vector3.Distance(s.pos, camPos) < 3f);
                av.SetActive(!hide);
                av.transform.position = s.pos + Vector3.down * 0.85f;
                av.transform.rotation = Quaternion.Euler(0f, s.yaw, 0f);
                float k = cam.orthographic ? cam.orthographicSize * 0.012f : Mathf.Max(1f, Vector3.Distance(camPos, s.pos) * 0.006f);
                av.transform.localScale = new Vector3(0.6f * k, 0.9f * k, 0.6f * k);
                fr.Begin();
                if (!hide) Frustum(fr, s);
                fr.End();
                if (!hide) labels.Add(new Label { world = s.pos + Vector3.up * (1.2f * k), text = s.name + (s.Binoculars ? "  [optic]" : "") + (s.Talking ? "  [talking]" : ""), colour = col, prio = 0 });
            }
            talkRings.Begin();
            for (int i = 0; i < st.SeatCount; i++)
            {
                var s = st.Seat(i);
                if (s.Talking && !(mode == CamMode.Follow && (SeatIndex(s.id) == selSeat || Vector3.Distance(s.pos, camPos) < 3f))) talkRings.Circle(s.pos + Vector3.up * 0.4f, 2.5f);
            }
            talkRings.End();

            // Contacts: every one is positioned from the log by the replay clock (interpolated between its 10 Hz samples), drawn as a
            // small drone or bird shape in its truth colour, never smaller than MinContactPx on screen.
            lockLines.Begin();
            foreach (var b in trailBands) b.Begin();
            nearList.Clear(); trailList.Clear();
            for (int i = 0; i < st.ContactCount; i++)
            {
                var c = st.Contact(i);
                seen.Add(c.id);
                var go = ContactProxy(c.id, c.truth);
                go.SetActive(true);
                go.transform.position = c.pos;
                var bc = Data.FindContact(c.id);
                Vector3 vel = bc != null ? Velocity(bc, t, c) : c.vel;
                bool quad = c.truth == (byte)ContactClass.HostileQuad || c.truth == (byte)ContactClass.CivilianQuad;
                Vector3 look = quad ? new Vector3(vel.x, 0f, vel.z) : vel;
                if (look.sqrMagnitude > 0.09f) go.transform.rotation = Quaternion.LookRotation(look);
                float sc = Mathf.Max(NaturalScale(c.truth), PxToMetres(c.pos, MinContactPx) / SpanOf(c.truth));
                go.transform.localScale = Vector3.one * sc;
                Color col = truthOverlay ? ClassColour(c.truth) : NeutralColour;
                SetColour(go, col);
                float dist = cam.orthographic ? Vector3.Distance(c.pos, topCentre) : Vector3.Distance(c.pos, camPos);
                bool isSel = c.id == selContact;
                if (isSel || c.lockSeat >= 0) labels.Add(new Label { world = c.pos + Vector3.up * sc * 1.2f, text = ContactText(c) + (c.inView ? "  (in view)" : ""), colour = LabelColour(col), prio = 0, dist = dist });
                else nearList.Add(new KeyValuePair<float, int>(dist, i));
                if (c.truth != (byte)ContactClass.Bird || isSel || c.lockSeat >= 0) trailList.Add(new KeyValuePair<float, int>(isSel ? -1f : dist, i));
            }
            foreach (var kv in contactGo) if (kv.Value != null && !seen.Contains(kv.Key)) kv.Value.SetActive(false);
            for (int i = 0; i < st.SeatCount; i++)
            {
                var s = st.Seat(i);
                if (s.lockId < 0) continue;
                var c = st.FindContact(s.lockId);
                if (c != null) lockLines.Add(s.pos + Vector3.down * 0.4f, c.pos);
            }

            // Trails: the last 5 s, only for the 40 nearest drones (and the selected contact, and any lock), never for birds in the
            // crowd: a flock of erratic birds drawn as 9 s polylines was the wall of jagged lines. Older parts are dimmer.
            trailList.Sort((a, b) => a.Key.CompareTo(b.Key));
            const float TrailSeconds = 5f, TrailStep = 0.25f;
            int segs = Mathf.RoundToInt(TrailSeconds / TrailStep);
            for (int n = 0; n < trailList.Count && n < 40; n++)
            {
                var c = st.Contact(trailList[n].Value);
                var bc = Data.FindContact(c.id);
                if (bc == null) continue;
                int cls = TrailClass(c.truth);
                Vector3 prev = default; bool have = false;
                for (int k = segs; k >= 0; k--)
                {
                    var p = SamplePos(bc, t - k * TrailStep, out bool ok);
                    if (!ok) { have = false; continue; }
                    if (have)
                    {
                        int band = Mathf.Min(2, (segs - k) * 3 / segs);       // 0 = oldest third ... 2 = newest third
                        trailBands[cls, band].Add(prev, p);
                    }
                    prev = p; have = true;
                }
            }
            lockLines.End();
            foreach (var b in trailBands) b.End();

            // Recent events: jam rings, engage tracers, call rings, and a short label at the contact.
            tracers.Begin(); rings.Begin(); callRings.Begin();
            Data.EventsIn(t - 4f, t, recent);
            foreach (var e in recent)
            {
                float age = t - e.Time;
                var c = e.contact >= 0 ? st.FindContact(e.contact) : null;
                if (e.type == BbEventType.Action && c != null)
                {
                    if (e.text.Contains("Jam")) { rings.Circle(c.pos, 8f + age * 14f); rings.Circle(c.pos, 4f + age * 7f); }
                    else if (e.text.Contains("Engage"))
                    {
                        var seat = e.seat >= 0 ? st.FindSeat(e.seat) : null;
                        Vector3 from = seat != null ? seat.pos + Vector3.down * 0.4f : (haveSel ? sel : Data.origin);
                        if (age < 1.2f) tracers.Add(from, c.pos);
                        rings.Circle(c.pos, 3f + age * 4f);
                    }
                    else callRings.Circle(c.pos, 6f);
                    eventLabelled.Add(c.id);
                    labels.Add(new Label { world = c.pos + Vector3.up * 3f, text = e.text, colour = new Color(1f, 0.9f, 0.3f), prio = 1, dist = Vector3.Distance(c.pos, camPos) });
                }
                else if (e.type == BbEventType.Call && c != null)
                {
                    callRings.Circle(c.pos, 10f + age * 3f);
                    eventLabelled.Add(c.id);
                    labels.Add(new Label { world = c.pos + Vector3.up * 3f, text = e.text, colour = new Color(0.8f, 0.92f, 1f), prio = 1, dist = Vector3.Distance(c.pos, camPos) });
                }
                else if (e.type == BbEventType.Lock && c != null)
                {
                    eventLabelled.Add(c.id);
                    labels.Add(new Label { world = c.pos + Vector3.up * 3f, text = e.text, colour = Color.white, prio = 1, dist = Vector3.Distance(c.pos, camPos) });
                }
            }
            tracers.End(); rings.End(); callRings.End();

            // Plain contact labels: only the nearest few (not the ones that already carry an event label), so the picture stays readable.
            nearList.Sort((a, b) => a.Key.CompareTo(b.Key));
            float nearLimit = cam.orthographic ? (topSize < 500f ? topSize * 1.2f : 0f) : 600f;
            for (int n = 0, added = 0; n < nearList.Count && added < 8; n++)
            {
                if (nearList[n].Key > nearLimit) break;
                var c = st.Contact(nearList[n].Value);
                if (eventLabelled.Contains(c.id)) continue;
                Color col = truthOverlay ? ClassColour(c.truth) : Color.white;
                labels.Add(new Label { world = c.pos + Vector3.up * 2f, text = ContactText(c), colour = LabelColour(col), prio = 2, dist = nearList[n].Key });
                added++;
            }

            // Groups: the sampled agents as small drone or bird shapes (swarm red, bird flock yellow; a dense sky is a mix of
            // classes the log does not keep per agent, so it is neutral), oriented along their own motion.
            for (int i = 0; i < st.GroupCount; i++)
            {
                var g = st.Group(i);
                if (!groupGo.TryGetValue(g.id, out var pool)) { pool = new List<GameObject>(); groupGo[g.id] = pool; }
                Color gc = g.kind == 1 ? ClassColour((int)ContactClass.Bird) : g.kind == 2 ? new Color(0.78f, 0.8f, 0.85f) : ClassColour((int)ContactClass.HostileQuad);
                if (!truthOverlay) gc = NeutralColour;
                Mesh gm = g.kind == 1 ? ReplayShapes.Bird : ReplayShapes.Quad;
                while (pool.Count < g.n) pool.Add(NewProxy("g" + g.id + "_" + pool.Count, gm, gc));
                for (int r = 0; r < pool.Count; r++)
                {
                    bool on = r < g.n && g.alive[r];
                    pool[r].SetActive(on);
                    if (!on) continue;
                    pool[r].transform.position = g.agents[r];
                    pool[r].transform.localScale = Vector3.one * Mathf.Max(1f, PxToMetres(g.agents[r], MinAgentPx));
                    Vector3 v = r < g.vels.Length ? g.vels[r] : Vector3.zero;
                    if (g.kind != 1) v.y = 0f;
                    if (v.sqrMagnitude > 0.09f) pool[r].transform.rotation = Quaternion.LookRotation(v);
                    SetColour(pool[r], gc);
                }
                string kind = g.kind == 1 ? "Bird flock" : g.kind == 2 ? "Dense sky" : "Swarm";
                labels.Add(new Label { world = g.centroid + Vector3.up * 20f, text = $"{kind} {g.total} ({g.count} flying, {Mathf.Min(g.n, g.total)} shown)", colour = LabelColour(gc), prio = 1, dist = Vector3.Distance(g.centroid, camPos) });
            }
            foreach (var kv in groupGo) { bool present = false; for (int i = 0; i < st.GroupCount; i++) if (st.Group(i).id == kv.Key) present = true; if (!present) foreach (var go in kv.Value) go.SetActive(false); }

            // Follow-mode HUD text.
            hudLine = "";
            if (Data.seats.Count > 0)
            {
                var s = st.FindSeat(Data.seats[Mathf.Clamp(selSeat, 0, Data.seats.Count - 1)].id);
                if (s != null) hudLine = $"{s.name}   FOV {s.fov:F1} deg{(s.Binoculars ? " (optic)" : "")}   yaw {s.yaw:F0}  pitch {s.pitch:F0}" + (s.lockId >= 0 ? $"   LOCK #{s.lockId}" : "");
            }
        }

        // Where the contact is at `time` (a pure function of the replay clock: the same answer going forward, backward or after a speed
        // change). Linear between the two 10 Hz samples around it. Not ok = it was not in the sky then.
        static Vector3 SamplePos(BbContact c, float time, out bool ok)
        {
            float tf = time * BlackBoxData.Hz;
            ok = false;
            if (c.s.Count == 0 || tf < c.s[0].tick || tf > c.s[c.s.Count - 1].tick) return Vector3.zero;
            int lo = 0, hi = c.s.Count - 1, ans = 0;
            while (lo <= hi) { int mid = (lo + hi) >> 1; if (c.s[mid].tick <= tf) { ans = mid; lo = mid + 1; } else hi = mid - 1; }
            var a = c.s[ans]; var b = ans + 1 < c.s.Count ? c.s[ans + 1] : a;
            float k = b.tick > a.tick && b.tick - a.tick <= BlackBoxData.MaxContactGap ? Mathf.Clamp01((tf - a.tick) / (b.tick - a.tick)) : 0f;
            ok = true;
            return Vector3.Lerp(a.pos, b.pos, k);
        }

        int SeatIndex(int id)
        {
            if (Data.seatAlias.TryGetValue(id, out int merged)) id = merged;
            for (int i = 0; i < Data.seats.Count; i++) if (Data.seats[i].id == id) return i;
            return 0;
        }

        void Frustum(LineBatch b, BbSeatState s)
        {
            float len = cam.orthographic ? Mathf.Max(120f, cam.orthographicSize * 0.35f) : 220f;
            float half = Mathf.Clamp(s.fov, 1f, 140f) * 0.5f * Mathf.Deg2Rad;
            float w = Mathf.Tan(half) * len, h = w * 9f / 16f;
            var rot = Quaternion.Euler(-s.pitch, s.yaw, 0f);
            Vector3 eye = s.pos;
            Vector3 c0 = eye + rot * new Vector3(-w, -h, len), c1 = eye + rot * new Vector3(w, -h, len), c2 = eye + rot * new Vector3(w, h, len), c3 = eye + rot * new Vector3(-w, h, len);
            b.Add(eye, c0); b.Add(eye, c1); b.Add(eye, c2); b.Add(eye, c3);
            b.Add(c0, c1); b.Add(c1, c2); b.Add(c2, c3); b.Add(c3, c0);
            if (s.Binoculars) { b.Add(eye, eye + rot * new Vector3(0f, 0f, len * 1.6f)); }
        }

        // ---- Cameras ----

        Vector3 freePos; float freeYaw, freePitch;
        Vector3 topCentre; float topSize;

        void InitFree()
        {
            freePos = cam.transform.position; var e = cam.transform.eulerAngles;
            freeYaw = e.y; freePitch = Mathf.DeltaAngle(0f, e.x);
            if (mode == CamMode.Top) { freePos = topCentre + Vector3.up * 400f; freePitch = 50f; freeYaw = Data.sectorYaw; }
        }

        bool MouseOverBar()
        {
            var m = Mouse.current;
            return m != null && m.position.ReadValue().y < BarHeightPx;
        }

        float BarHeightPx => 130f * Screen.height / 720f;

        void DriveCamera()
        {
            var kb = Keyboard.current; var mouse = Mouse.current;
            float dt = Mathf.Min(UnityEngine.Time.unscaledDeltaTime, 0.1f);
            BbSeatState s = Data.seats.Count > 0 ? st.FindSeat(Data.seats[Mathf.Clamp(selSeat, 0, Data.seats.Count - 1)].id) : null;
            cam.orthographic = mode == CamMode.Top;
            switch (mode)
            {
                case CamMode.Follow:
                    if (s != null)
                    {
                        cam.transform.SetPositionAndRotation(s.pos, Quaternion.Euler(-s.pitch, s.yaw, 0f));
                        cam.fieldOfView = Mathf.Clamp(Camera.HorizontalToVerticalFieldOfView(Mathf.Clamp(s.fov, 2f, 120f), Mathf.Max(0.5f, cam.aspect)), 1f, 120f);
                    }
                    break;
                case CamMode.Chase:
                    if (s != null)
                    {
                        var rot = Quaternion.Euler(Mathf.Clamp(-s.pitch, -30f, 30f) * 0.5f, s.yaw, 0f);
                        cam.transform.position = s.pos + rot * new Vector3(0f, 3.5f, -12f);
                        cam.transform.LookAt(s.pos + rot * new Vector3(0f, 0f, 40f));
                        cam.fieldOfView = 65f;
                    }
                    break;
                case CamMode.Free:
                    if (kb != null && mouse != null)
                    {
                        if (mouse.rightButton.isPressed && !MouseOverBar())
                        {
                            var d = mouse.delta.ReadValue();
                            freeYaw += d.x * 0.12f; freePitch = Mathf.Clamp(freePitch - d.y * 0.12f, -89f, 89f);
                        }
                        var rot = Quaternion.Euler(freePitch, freeYaw, 0f);
                        float sp = (kb.leftShiftKey.isPressed ? 240f : 60f) * dt;
                        Vector3 mv = Vector3.zero;
                        if (kb.wKey.isPressed) mv += rot * Vector3.forward;
                        if (kb.sKey.isPressed) mv -= rot * Vector3.forward;
                        if (kb.dKey.isPressed) mv += rot * Vector3.right;
                        if (kb.aKey.isPressed) mv -= rot * Vector3.right;
                        if (kb.eKey.isPressed) mv += Vector3.up;
                        if (kb.qKey.isPressed) mv -= Vector3.up;
                        freePos += mv * sp;
                    }
                    cam.transform.SetPositionAndRotation(freePos, Quaternion.Euler(freePitch, freeYaw, 0f));
                    cam.fieldOfView = 65f;
                    break;
                case CamMode.Top:
                    if (mouse != null && !MouseOverBar())
                    {
                        float sc = mouse.scroll.ReadValue().y;
                        if (Mathf.Abs(sc) > 0.01f) topSize = Mathf.Clamp(topSize * Mathf.Exp(-sc * 0.0015f), 30f, 12000f);
                        if (mouse.rightButton.isPressed)
                        {
                            var d = mouse.delta.ReadValue();
                            float per = topSize * 2f / Screen.height;
                            topCentre -= cam.transform.right * d.x * per + cam.transform.up * d.y * per;
                        }
                    }
                    cam.orthographicSize = topSize;
                    cam.transform.SetPositionAndRotation(new Vector3(topCentre.x, Data.origin.y + 3000f, topCentre.z), Quaternion.Euler(90f, Data.sectorYaw, 0f));
                    cam.farClipPlane = 9000f;
                    break;
            }
        }

        // ---- Voice ----

        ReplayVoiceBank bank;
        AudioSource audioSrc;
        AudioClip voiceClip;
        VoiceMode voiceMode = VoiceMode.Off, clipMode = VoiceMode.Off;
        int clipListener = int.MinValue;
        bool voiceDirty;

        public void SetVoice(VoiceMode m)
        {
            if (m != VoiceMode.Off && !VoiceAvailable) { Note = "No voice recording for this run."; m = VoiceMode.Off; }
            voiceMode = m; voiceSeek = true;
        }

        int ListenerId => Data.seats.Count > 0 ? Data.seats[Mathf.Clamp(selSeat, 0, Data.seats.Count - 1)].id : 0;

        void VoiceUpdate()
        {
            if (audioSrc == null) return;
            if (voiceMode == VoiceMode.Off || bank == null) { if (audioSrc.isPlaying) audioSrc.Stop(); return; }
            int listener = voiceMode == VoiceMode.Heard ? ListenerId : -1;
            if (voiceClip == null || clipMode != voiceMode || clipListener != listener || voiceDirty)
            {
                float[] pcm = bank.Render(voiceMode, listener, Mathf.Min(end, 1800f));
                if (voiceClip != null) Destroy(voiceClip);
                voiceClip = AudioClip.Create("replay voice", pcm.Length, 1, ReplayVoiceBank.Rate, false);
                voiceClip.SetData(pcm, 0);
                audioSrc.clip = voiceClip;
                clipMode = voiceMode; clipListener = listener; voiceDirty = false; voiceSeek = true;
                if (audioSrc.isPlaying) audioSrc.Stop();
            }
            bool wantPlay = playing && speed >= 0.25f && speed <= 3f && t < voiceClip.length - 0.05f;
            if (!wantPlay) { if (audioSrc.isPlaying) audioSrc.Pause(); return; }
            audioSrc.pitch = speed;
            if (!audioSrc.isPlaying) { audioSrc.time = Mathf.Clamp(t, 0f, voiceClip.length - 0.05f); audioSrc.Play(); voiceSeek = false; }
            else if (voiceSeek || Mathf.Abs(audioSrc.time - t) > 0.35f) { audioSrc.time = Mathf.Clamp(t, 0f, voiceClip.length - 0.05f); voiceSeek = false; }
        }

        // ---- IMGUI ----

        GUIStyle labelStyle, hudStyle;

        void OnGUI()
        {
            if (Data == null || cam == null) return;
            GUI.depth = -30;
            C.EnsureStyles(15);
            float ui = Screen.height / 720f;
            if (Event.current.type == EventType.Repaint)
            {
                if (labelStyle == null) { labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = false }; labelStyle.normal.textColor = Color.white; }
                labelStyle.fontSize = Mathf.RoundToInt(12f * ui);
                DrawLabels(ui);
                if (mode == CamMode.Follow) DrawFollowHud(ui);
            }

            var saved = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(ui, ui, 1f));
            Bar(Screen.width / ui, 720f);
            GUI.matrix = saved;
        }

        // Labels are placed most important first and a label that would cover one already placed is moved down a line (up to three
        // times) or dropped, so the picture never turns into a pile of overlapping text.
        readonly List<Rect> placed = new List<Rect>();
        readonly List<Label> sorted = new List<Label>();

        void DrawLabels(float ui)
        {
            sorted.Clear();
            sorted.AddRange(labels);
            sorted.Sort((a, b) => a.prio != b.prio ? a.prio.CompareTo(b.prio) : a.dist.CompareTo(b.dist));
            placed.Clear();
            int shown = 0;
            foreach (var l in sorted)
            {
                if (shown >= 40) break;
                Vector3 sp = cam.WorldToScreenPoint(l.world);
                if (sp.z <= 0f || sp.x < -50 || sp.x > Screen.width + 50 || sp.y < BarHeightPx || sp.y > Screen.height) continue;
                var size = labelStyle.CalcSize(new GUIContent(l.text));
                var r = new Rect(sp.x + 6f, Screen.height - sp.y - size.y - 2f, size.x + 4f, size.y);
                bool ok = false;
                for (int attempt = 0; attempt < 4 && !ok; attempt++)
                {
                    ok = true;
                    foreach (var q in placed) if (q.Overlaps(r)) { ok = false; break; }
                    if (!ok) r.y += size.y;
                }
                if (!ok) continue;
                placed.Add(r);
                shown++;
                labelStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
                GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), l.text, labelStyle);
                labelStyle.normal.textColor = l.colour;
                GUI.Label(r, l.text, labelStyle);
            }
        }

        void DrawFollowHud(float ui)
        {
            float cx = Screen.width * 0.5f, cy = (Screen.height + BarHeightPx) * 0.5f;
            float len = 14f * ui;
            Color old = GUI.color; GUI.color = new Color(0.4f, 1f, 0.5f, 0.9f);
            GUI.DrawTexture(new Rect(cx - len, cy - 0.75f, len * 2f, 1.5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 0.75f, cy - len, 1.5f, len * 2f), Texture2D.whiteTexture);
            if (Data.seats.Count > 0)
            {
                var s = st.FindSeat(Data.seats[Mathf.Clamp(selSeat, 0, Data.seats.Count - 1)].id);
                if (s != null && s.lockId >= 0)
                {
                    var c = st.FindContact(s.lockId);
                    if (c != null)
                    {
                        Vector3 sp = cam.WorldToScreenPoint(c.pos);
                        if (sp.z > 0f)
                        {
                            float h = 22f * ui, x = sp.x, y = Screen.height - sp.y, a = h * 0.4f;
                            GUI.color = Color.white;
                            foreach (var sx in new[] { -1f, 1f })
                                foreach (var sy in new[] { -1f, 1f })
                                {
                                    GUI.DrawTexture(new Rect(x + sx * h - (sx > 0 ? a : 0f), y + sy * h - (sy > 0 ? 1.5f : 0f), a, 1.5f), Texture2D.whiteTexture);
                                    GUI.DrawTexture(new Rect(x + sx * h - (sx > 0 ? 1.5f : 0f), y + sy * h - (sy > 0 ? a : 0f), 1.5f, a), Texture2D.whiteTexture);
                                }
                        }
                    }
                }
            }
            GUI.color = old;
            if (hudStyle == null) hudStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
            hudStyle.fontSize = Mathf.RoundToInt(14f * ui);
            hudStyle.normal.textColor = new Color(0.4f, 1f, 0.5f);
            GUI.Label(new Rect(0f, 8f * ui, Screen.width, 24f * ui), "WHAT THEY SAW (reconstructed from the logged pose, not a screen recording)", hudStyle);
            GUI.Label(new Rect(0f, 28f * ui, Screen.width, 24f * ui), hudLine, hudStyle);
        }

        static Color EventColour(BbEventType type)
        {
            switch (type)
            {
                case BbEventType.Lock: return Color.white;
                case BbEventType.Call: return C.Accent;
                case BbEventType.Action: return C.Warn;
                case BbEventType.Card: return new Color(1f, 1f, 0.3f);
                case BbEventType.Director: return new Color(1f, 0.55f, 0.15f);
                case BbEventType.GroupCall: return C.Good;
                case BbEventType.Talk: return C.Dim;
                default: return C.Neutral;
            }
        }

        static string Clock(float s) => $"{(int)(s / 60f)}:{(int)(s % 60f):00}";

        void Bar(float w, float h)
        {
            float bh = 130f, y0 = h - bh;
            C.Fill(new Rect(0f, y0, w, bh), new Color(0.05f, 0.06f, 0.08f, 0.92f));
            if (Event.current.type == EventType.MouseDown && Event.current.mousePosition.y >= y0) { /* the bar owns clicks in its area */ }
            const float pad = 10f;
            float x = pad, y = y0 + 6f;
            if (GUI.Button(new Rect(x, y, 64f, 24f), playing ? "Pause" : "Play")) SetPlaying(!playing); x += 70f;
            if (GUI.Button(new Rect(x, y, 34f, 24f), "|<")) { float p = Data.PrevEventTime(t); SetTime(p >= 0f ? p : 0f); } x += 38f;
            if (GUI.Button(new Rect(x, y, 34f, 24f), ">|")) { float n = Data.NextEventTime(t); if (n >= 0f) SetTime(n); } x += 42f;
            foreach (float sp in new[] { 0.25f, 0.5f, 1f, 2f, 4f, 8f })
            {
                bool on = Mathf.Approximately(speed, sp);
                var old = GUI.backgroundColor; if (on) GUI.backgroundColor = C.Accent;
                if (GUI.Button(new Rect(x, y, 42f, 24f), sp + "x")) SetSpeed(sp);
                GUI.backgroundColor = old; x += 44f;
            }
            x += 8f;
            GUI.Label(new Rect(x, y + 2f, 130f, 22f), $"{Clock(t)} / {Clock(end)}", C.Label);
            if (GUI.Button(new Rect(w - pad - 100f, y, 100f, 24f), "Exit replay")) { Exit(); return; }
            // The two voice versions, side by side and named: what was said (clean), and what the selected seat heard (degraded).
            {
                float vx = w - pad - 100f - 14f - 372f;
                GUI.enabled = VoiceAvailable;
                GUI.Label(new Rect(vx - 52f, y + 2f, 50f, 22f), VoiceAvailable ? "VOICE" : "VOICE n/a", C.Small);
                foreach (var vm in new[] { VoiceMode.Off, VoiceMode.Clean, VoiceMode.Heard })
                {
                    string text = vm == VoiceMode.Off ? "Voice off" : vm == VoiceMode.Clean ? "Clean recording" : "As heard (degraded)";
                    float bw = vm == VoiceMode.Off ? 72f : vm == VoiceMode.Clean ? 120f : 170f;
                    var old = GUI.backgroundColor; if (voiceMode == vm) GUI.backgroundColor = C.Accent;
                    if (GUI.Button(new Rect(vx, y, bw, 24f), text)) SetVoice(vm);
                    GUI.backgroundColor = old; vx += bw + 5f;
                }
                GUI.enabled = true;
            }

            // Timeline: event ticks above, talk bars below.
            float sy = y0 + 36f;
            var track = new Rect(pad, sy + 8f, w - pad * 2f, 14f);
            foreach (var e in Data.events)
            {
                if (!BlackBoxData.IsMajor(e.type)) continue;
                float ex = track.x + e.Time / end * track.width;
                C.Fill(new Rect(ex - 1f, sy, 2f, 8f), EventColour(e.type));
            }
            float nt = GUI.HorizontalSlider(track, t, 0f, end);
            if (!Mathf.Approximately(nt, t)) SetTime(nt);
            if (bank != null)
                foreach (var b in bank.Bars())
                {
                    float bx = track.x + b.x / end * track.width, bw = Mathf.Max(2f, (b.y - b.x) / end * track.width);
                    C.Fill(new Rect(bx, sy + 24f, bw, 3f), SeatColour(SeatIndex((int)b.z)));
                }

            // Row 3: seats, cameras, overlays, voice.
            y = y0 + 68f; x = pad;
            GUI.Label(new Rect(x, y + 2f, 40f, 22f), "Seat", C.Small); x += 36f;
            for (int i = 0; i < Data.seats.Count && i < 8; i++)
            {
                var old = GUI.backgroundColor; if (i == selSeat) GUI.backgroundColor = SeatColour(i);
                string nm = Data.seats[i].name; if (nm.Length > 9) nm = nm.Substring(0, 9);
                if (GUI.Button(new Rect(x, y, 78f, 24f), nm)) SelectSeat(i);
                GUI.backgroundColor = old; x += 80f;
            }
            x += 12f;
            foreach (CamMode m in new[] { CamMode.Follow, CamMode.Chase, CamMode.Free, CamMode.Top })
            {
                var old = GUI.backgroundColor; if (m == mode) GUI.backgroundColor = C.Accent;
                if (GUI.Button(new Rect(x, y, 66f, 24f), m.ToString().ToUpper())) SetMode(m);
                GUI.backgroundColor = old; x += 68f;
            }
            x += 12f;
            if (GUI.Button(new Rect(x, y, 96f, 24f), "Truth: " + (truthOverlay ? "ON" : "OFF"))) truthOverlay = !truthOverlay; x += 100f;
            if (GUI.Button(new Rect(x, y, 120f, 24f), "Buildings: " + (buildingsOn ? "ON" : "OFF"))) SetBuildings(!buildingsOn); x += 124f;

            // Row 4: latest events and the honesty line.
            y = y0 + 98f;
            string last = "";
            int found = 0;
            for (int i = Data.events.Count - 1; i >= 0 && found < 3; i--)
            {
                var e = Data.events[i];
                if (e.Time > t || !BlackBoxData.IsMajor(e.type)) continue;
                last = $"{Clock(e.Time)} {e.text}   |   " + last; found++;
            }
            GUI.Label(new Rect(pad, y, w - pad * 2f, 18f), last.Length > 0 ? last : "No events yet.", C.Small);
            GUI.Label(new Rect(pad, y + 16f, w - pad * 2f, 18f), "Reconstructed from the logged poses, not a screen recording.   Space play/pause   PgUp/PgDn events   , . step   1-4 cameras   click a contact to label it   Esc exit   " + (voiceMode == VoiceMode.Heard ? "AS HEARD (degraded) = rebuilt for the selected seat: " + (Data.seats.Count > 0 ? Data.seats[Mathf.Clamp(selSeat, 0, Data.seats.Count - 1)].name : "") + " (approximation).   Pick another seat to hear theirs." : voiceMode == VoiceMode.Clean ? "CLEAN recording = what each speaker said, before range, noise, dropout and muffle." : Note), C.Small);
        }
    }
    // Small drone and bird shapes for the replay markers, built once from boxes (forward is +Z, about 1 m across at scale 1).
    // Birds reuse the flock's own bird mesh.
    static class ReplayShapes
    {
        static Mesh quad, fixedWing;
        public static Mesh Quad => quad != null ? quad : (quad = BuildQuad());
        public static Mesh FixedWing => fixedWing != null ? fixedWing : (fixedWing = BuildFixedWing());
        public static Mesh Bird => BirdMesh.Get(0, BirdSpeciesInfo.GlidePose);

        sealed class Builder
        {
            readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
            readonly List<int> t = new List<int>();

            // Each face is added with both windings so it shows from either side whatever the material's culling.
            public void Box(Vector3 centre, Vector3 size, float yawDeg)
            {
                var rot = Quaternion.Euler(0f, yawDeg, 0f);
                var normals = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                foreach (var nrm in normals)
                {
                    Vector3 a = Mathf.Abs(nrm.y) > 0.5f ? Vector3.right : Vector3.up;
                    Vector3 b = Vector3.Cross(nrm, a);
                    int i0 = v.Count;
                    foreach (var corner in new[] { -a - b, a - b, a + b, -a + b })
                    {
                        Vector3 local = Vector3.Scale(nrm * 0.5f + corner * 0.5f, size);
                        v.Add(centre + rot * local);
                        n.Add(rot * nrm);
                    }
                    t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3);
                    t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2);
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
                m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0);
                m.RecalculateBounds();
                return m;
            }
        }

        static Mesh BuildQuad()
        {
            var b = new Builder();
            b.Box(Vector3.zero, new Vector3(0.3f, 0.1f, 0.3f), 0f);                         // body
            b.Box(Vector3.zero, new Vector3(1f, 0.04f, 0.07f), 45f);                        // two arms in an X
            b.Box(Vector3.zero, new Vector3(1f, 0.04f, 0.07f), -45f);
            for (int i = 0; i < 4; i++)
                b.Box(new Vector3((i & 1) == 0 ? -0.35f : 0.35f, 0.06f, (i & 2) == 0 ? -0.35f : 0.35f), new Vector3(0.3f, 0.015f, 0.3f), 0f);   // rotors
            return b.ToMesh("ReplayQuad");
        }

        static Mesh BuildFixedWing()
        {
            var b = new Builder();
            b.Box(Vector3.zero, new Vector3(0.12f, 0.12f, 1f), 0f);                         // fuselage
            b.Box(new Vector3(0f, 0f, 0.1f), new Vector3(1.4f, 0.03f, 0.25f), 0f);          // wing
            b.Box(new Vector3(0f, 0f, -0.45f), new Vector3(0.45f, 0.03f, 0.15f), 0f);       // tailplane
            b.Box(new Vector3(0f, 0.1f, -0.45f), new Vector3(0.03f, 0.2f, 0.15f), 0f);      // fin
            return b.ToMesh("ReplayFixedWing");
        }
    }
}
