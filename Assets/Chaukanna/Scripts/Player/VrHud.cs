using System.Collections.Generic;
using Chaukanna.Net;
using Chaukanna.Scenario;
using UnityEngine;
using UnityEngine.UI;

namespace Chaukanna.Player
{
    // Numbers for the headset HUD that need no scene, so they are checked without a headset.
    public static class HudMath
    {
        public const float RecentreDegrees = 35f;

        // The HUD stays where it is until the view has turned more than this far (yaw) away from it.
        public static bool NeedsRecentre(float anchorYaw, float headYaw, float limit = RecentreDegrees)
        {
            return Mathf.Abs(Mathf.DeltaAngle(anchorYaw, headYaw)) > limit;
        }

        // Side length (metres) of a marker that must cover `angularDegrees` of the view at `distance` metres: the same size on the eye
        // at 100 m and at 1000 m.
        public static float MarkerSize(float distance, float angularDegrees)
        {
            return 2f * Mathf.Max(0.01f, distance) * Mathf.Tan(angularDegrees * 0.5f * Mathf.Deg2Rad);
        }

        // The angle a marker of `size` metres covers at `distance` metres.
        public static float AngleOf(float size, float distance)
        {
            return 2f * Mathf.Atan2(size * 0.5f, Mathf.Max(0.01f, distance)) * Mathf.Rad2Deg;
        }
    }

    // Four small quads round a contact, always the same angular size however far away it is (headset equivalent of the aim brackets).
    public class VrBrackets
    {
        public const float BoxDegrees = 1.6f, CornerDegrees = 0.4f;

        class Set
        {
            public GameObject root;
            public Transform[] corners = new Transform[4];
            public Renderer[] renderers = new Renderer[4];
        }

        readonly Transform parent;
        readonly List<Set> pool = new List<Set>();
        readonly Dictionary<Color, Material> materials = new Dictionary<Color, Material>();
        int used;

        public VrBrackets(Transform parent) { this.parent = parent; }

        public int InUse => used;
        public int PoolSize => pool.Count;

        public void Begin() { used = 0; }

        // One bracket set at `position`, seen from `eye`.
        public Transform Show(Vector3 eye, Vector3 position, Color colour, float boxDegrees = BoxDegrees, float cornerDegrees = CornerDegrees)
        {
            while (pool.Count <= used) pool.Add(Build(pool.Count));
            var set = pool[used++];
            float distance = Vector3.Distance(eye, position);
            float box = HudMath.MarkerSize(distance, boxDegrees), corner = HudMath.MarkerSize(distance, cornerDegrees);
            set.root.SetActive(true);
            Vector3 toContact = position - eye;
            set.root.transform.SetPositionAndRotation(position, toContact.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(toContact) : Quaternion.identity);
            var m = MaterialFor(colour);
            int i = 0;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2, i++)
            {
                set.corners[i].localPosition = new Vector3(sx * box * 0.5f, sy * box * 0.5f, 0f);
                set.corners[i].localScale = new Vector3(corner, corner, 1f);
                set.renderers[i].sharedMaterial = m;
            }
            return set.root.transform;
        }

        public void End()
        {
            for (int i = used; i < pool.Count; i++) pool[i].root.SetActive(false);
        }

        Set Build(int index)
        {
            var set = new Set { root = new GameObject("Bracket" + index) };
            set.root.transform.SetParent(parent, false);
            for (int i = 0; i < 4; i++)
            {
                set.corners[i] = Rig.Part(PrimitiveType.Quad, set.root.transform, Vector3.zero, Vector3.one, MaterialFor(Color.white));
                set.renderers[i] = set.corners[i].GetComponent<Renderer>();
            }
            return set;
        }

        Material MaterialFor(Color c)
        {
            if (materials.TryGetValue(c, out var m) && m != null) return m;
            var baseMat = Chaukanna.RuntimeMaterials.Unlit;
            m = baseMat != null ? new Material(baseMat) : Rig.Mat(c);
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            materials[c] = m;
            return m;
        }

        public void Dispose()
        {
            foreach (var m in materials.Values) if (m != null) Object.Destroy(m);
            materials.Clear();
            foreach (var s in pool) if (s.root != null) Object.Destroy(s.root);
            pool.Clear();
        }
    }

    // The headset HUD. IMGUI does not render in a headset, so everything the desktop draws on screen (ROE card, lock label, group count,
    // role line, the commander's report list, the radial menu, the pause menu and the comfort question) is a world-space canvas about two
    // metres away. It follows the head lazily: it only swings round to the front when the view has turned more than 35 degrees from it.
    public class VrHud
    {
        public const float Distance = 2f, PitchDown = 8f, RecentreSpeed = 140f, Scale = 0.0022f;
        public const float CanvasW = 1000f, CanvasH = 560f;
        const int UiLayer = 5;      // the optic camera does not draw this layer, so the HUD never shows through the binoculars

        static Font font;

        readonly Camera head;
        readonly ContactSensor sensor;
        GameObject root;
        Canvas canvas;
        RectTransform rt;
        Text roe, timer, lockText, group, role, reports, say, hint, centre;
        Image roeBack, lockBack, centreBack, reportsBack;

        // Radial / pause ring.
        GameObject ringRoot;
        Canvas ringCanvas;
        Text ringTitle;
        readonly List<Image> ringBoxes = new List<Image>();
        readonly List<Text> ringLabels = new List<Text>();
        Text ringHelp;
        int ringCount;

        float anchorYaw;
        bool recentring, placed;
        ScenarioSpawner spawner;

        public Transform Root => root != null ? root.transform : null;
        public bool Recentring => recentring;
        public float AnchorYaw => anchorYaw;
        public string RoeText => roe != null ? roe.text : "";
        public string LockTextNow => lockText != null ? lockText.text : "";
        public string ReportsTextNow => reports != null ? reports.text : "";
        public string RoleTextNow => role != null ? role.text : "";
        public string GroupTextNow => group != null ? group.text : "";
        public string CentreTextNow => centre != null ? centre.text : "";
        public bool RingOpen => ringRoot != null && ringRoot.activeSelf;
        public int RingSelected { get; private set; } = -1;

        public VrHud(Camera head, ContactSensor sensor)
        {
            this.head = head;
            this.sensor = sensor;
            Build();
        }

        void Build()
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            root = new GameObject("VrHud", typeof(RectTransform));
            root.layer = UiLayer;
            canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            root.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            rt = (RectTransform)root.transform;
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);
            rt.localScale = Vector3.one * Scale;

            // Top: the ROE card (left, wide) and the mission clock (right).
            roeBack = Box(rt, 20f, 12f, 640f, 56f, new Color(0f, 0f, 0f, 0.62f));
            roe = Label(rt, 20f, 12f, 640f, 56f, 34, TextAnchor.MiddleCenter, FontStyle.Bold);
            Box(rt, 760f, 12f, 220f, 56f, new Color(0f, 0f, 0f, 0.62f));
            timer = Label(rt, 760f, 12f, 220f, 56f, 34, TextAnchor.MiddleCenter, FontStyle.Bold);

            group = Label(rt, 20f, 76f, 640f, 34, 26, TextAnchor.MiddleLeft, FontStyle.Bold);
            role = Label(rt, 20f, 108f, 640f, 34, 26, TextAnchor.MiddleLeft, FontStyle.Bold);

            // The commander's reports, down the right.
            reportsBack = Box(rt, 640f, 80f, 340f, 250f, new Color(0f, 0f, 0f, 0.55f));
            reports = Label(rt, 650f, 84f, 330f, 240f, 22, TextAnchor.UpperLeft, FontStyle.Normal);
            reports.horizontalOverflow = HorizontalWrapMode.Wrap;
            reports.verticalOverflow = VerticalWrapMode.Truncate;
            reports.supportRichText = true;

            // Bottom: the lock label.
            lockBack = Box(rt, 120f, 400f, 760f, 120f, new Color(0f, 0f, 0f, 0.62f));
            lockText = Label(rt, 132f, 404f, 740f, 112f, 28, TextAnchor.MiddleLeft, FontStyle.Bold);
            lockText.horizontalOverflow = HorizontalWrapMode.Wrap;
            lockText.verticalOverflow = VerticalWrapMode.Truncate;
            say = Label(rt, 120f, 352f, 760f, 40f, 28, TextAnchor.MiddleCenter, FontStyle.Bold);
            say.color = new Color(1f, 0.85f, 0.35f);
            hint = Label(rt, 120f, 524f, 760f, 30f, 20, TextAnchor.MiddleCenter, FontStyle.Normal);
            hint.color = new Color(1f, 1f, 1f, 0.7f);

            // Middle: the comfort question.
            centreBack = Box(rt, 200f, 150f, 600f, 190f, new Color(0f, 0f, 0f, 0.82f));
            centre = Label(rt, 214f, 156f, 572f, 178f, 30, TextAnchor.MiddleCenter, FontStyle.Bold);
            centre.horizontalOverflow = HorizontalWrapMode.Wrap;

            BuildRing();
            Place(true);
        }

        // The ring of items (radial menu and pause menu) on its own canvas, in front of the face.
        void BuildRing()
        {
            ringRoot = new GameObject("VrRadial", typeof(RectTransform));
            ringRoot.layer = UiLayer;
            ringCanvas = ringRoot.AddComponent<Canvas>();
            ringCanvas.renderMode = RenderMode.WorldSpace;
            ringRoot.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var r = (RectTransform)ringRoot.transform;
            r.sizeDelta = new Vector2(560f, 520f);
            r.localScale = Vector3.one * 0.0016f;
            r.pivot = new Vector2(0.5f, 0.5f);

            var hub = Box(r, 0f, 0f, 90f, 90f, new Color(0f, 0f, 0f, 0.7f));
            Centre(hub.rectTransform, 0f, 0f);
            ringTitle = Label(r, 0f, 0f, 90f, 90f, 26, TextAnchor.MiddleCenter, FontStyle.Bold);
            Centre(ringTitle.rectTransform, 0f, 0f);
            for (int i = 0; i < XrMenu.Items.Length; i++)
            {
                var back = Box(r, 0f, 0f, 190f, 40f, new Color(0f, 0f, 0f, 0.7f));
                var label = Label(r, 0f, 0f, 190f, 40f, 24, TextAnchor.MiddleCenter, FontStyle.Bold);
                Centre(back.rectTransform, 0f, 0f);
                Centre(label.rectTransform, 0f, 0f);
                ringBoxes.Add(back);
                ringLabels.Add(label);
            }
            ringHelp = Label(r, 0f, -250f, 520f, 30, 22, TextAnchor.MiddleCenter, FontStyle.Normal);
            Centre(ringHelp.rectTransform, 0f, -245f);
            ringRoot.SetActive(false);
        }

        // items: the labels round the ring (clockwise from the top). picked: highlighted one, -1 none. Positions the ring in front of the face.
        public void ShowRing(string title, IList<string> items, int picked, string help)
        {
            ringCount = items.Count;
            RingSelected = picked;
            if (!ringRoot.activeSelf)
            {
                ringRoot.SetActive(true);
                // Where the head looks, a little below the eye line, so the ring is in front of the face and out of the contact's way.
                Vector3 forward = head.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                Vector3 pos = head.transform.position + forward.normalized * 0.85f + Vector3.down * 0.12f;
                ringRoot.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - head.transform.position));
            }
            ringTitle.text = title;
            float radius = 200f;
            for (int i = 0; i < ringBoxes.Count; i++)
            {
                bool on = i < items.Count;
                ringBoxes[i].gameObject.SetActive(on);
                ringLabels[i].gameObject.SetActive(on);
                if (!on) continue;
                Vector2 d = RadialMenu.Direction(i, items.Count);
                Vector2 p = new Vector2(d.x * radius * 1.12f, d.y * radius * 0.92f);
                Centre(ringBoxes[i].rectTransform, p.x, p.y);
                Centre(ringLabels[i].rectTransform, p.x, p.y);
                ringLabels[i].text = items[i];
                ringBoxes[i].color = i == picked ? new Color(0.2f, 0.6f, 1f, 0.95f) : new Color(0f, 0f, 0f, 0.7f);
            }
            ringHelp.text = help;
        }

        public void HideRing()
        {
            if (ringRoot != null && ringRoot.activeSelf) ringRoot.SetActive(false);
            RingSelected = -1;
        }

        float refreshIn;

        // Each frame. comfort: null = nothing to ask, else the lines of the question. The text is rebuilt ten times a second, not every frame.
        public void Tick(float dt, string comfort)
        {
            if (root == null || head == null) return;
            Place(false, dt);
            refreshIn -= dt;
            if (refreshIn > 0f && (comfort == null) == (lastComfort == null) && comfort == lastComfort) return;
            refreshIn = 0.1f;
            Refresh(comfort);
        }

        string lastComfort;

        // Rebuilds every text from the current state (Tick does this at ten hertz; the self-test calls it directly).
        public void Refresh(string comfort)
        {
            lastComfort = comfort;
            if (root == null) return;
            if (spawner == null) spawner = Object.FindAnyObjectByType<ScenarioSpawner>();
            var card = ContactRegistry.CurrentRoe;
            roe.text = "ROE: " + RoeCards.Label(card);
            roeBack.color = RoeCards.IsAttack(card) ? new Color(0.55f, 0.08f, 0.08f, 0.75f) : new Color(0f, 0f, 0f, 0.62f);
            if (spawner != null)
            {
                float left = Mathf.Max(0f, spawner.Duration - spawner.ScenarioTime);
                timer.text = $"{(int)left / 60:00}:{(int)left % 60:00}";
            }

            // Lock label and group count: the same text the desktop shows.
            var targets = sensor != null ? sensor.Targets : null;
            int n = targets != null ? targets.Count : 0;
            if (sensor != null && sensor.Selected.Count > 1) group.text = $"GROUP: {sensor.Selected.Count} selected   (menu: G all in view)";
            else group.text = "";
            if (sensor != null && sensor.Locked != null && sensor.Selected.Count <= 1) lockText.text = sensor.LockLabel(sensor.Locked);
            else if (n > 1) lockText.text = $"GROUP of {n}.  Call and action apply to all of them.";
            else lockText.text = "NO LOCK.  Point the right controller at a contact and pull the trigger.";
            lockBack.color = new Color(0f, 0f, 0f, sensor != null && sensor.Locked != null ? 0.7f : 0.45f);

            var seat = RunConditions.SeatRole;
            role.text = seat != SeatRole.Any ? "ROLE: " + NetPlayer.RoleName(seat) : "";
            say.text = sensor != null ? sensor.Said : "";
            hint.text = "Right stick click / A: call menu     Left trigger: binoculars     X: read tablet     Menu: pause";

            // The commander's report list (newest first), as the desktop board shows it.
            bool commander = seat == SeatRole.Commander;
            reportsBack.gameObject.SetActive(commander);
            reports.gameObject.SetActive(commander);
            if (commander) reports.text = ReportLines();

            bool ask = !string.IsNullOrEmpty(comfort);
            centreBack.gameObject.SetActive(ask);
            centre.gameObject.SetActive(ask);
            if (ask) centre.text = comfort;
        }

        static string ReportLines()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<b>REPORTS</b>  ").Append(ReportBoard.UnhandledCount()).Append(" unhandled  (Y: slew)\n");
            int lines = Mathf.Min(7, ReportBoard.Entries.Count);
            for (int i = 0; i < lines; i++)
            {
                var e = ReportBoard.Entries[ReportBoard.Entries.Count - 1 - i];
                sb.Append('#').Append(e.id.ToString("00")).Append(' ').Append(((PlayerCall)e.call).Label()).Append(" by ").Append(e.seat);
                if (i < lines - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        // Places the HUD two metres out along its anchor yaw, a little below the eye line, facing the head. The anchor only moves when
        // the view has turned more than HudMath.RecentreDegrees away, then swings round to the front and stops.
        void Place(bool snap, float dt = 0f)
        {
            float headYaw = head.transform.eulerAngles.y;
            if (snap || !placed) { anchorYaw = headYaw; placed = true; recentring = false; }
            else
            {
                if (!recentring && HudMath.NeedsRecentre(anchorYaw, headYaw)) recentring = true;
                if (recentring)
                {
                    anchorYaw = Mathf.MoveTowardsAngle(anchorYaw, headYaw, RecentreSpeed * Mathf.Max(dt, 0.001f));
                    if (Mathf.Abs(Mathf.DeltaAngle(anchorYaw, headYaw)) < 1f) recentring = false;
                }
            }
            Vector3 eye = head.transform.position;
            Vector3 dir = Quaternion.Euler(PitchDown, anchorYaw, 0f) * Vector3.forward;
            Vector3 pos = eye + dir * Distance;
            root.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - eye));
        }

        public void Dispose()
        {
            if (root != null) Object.Destroy(root);
            if (ringRoot != null) Object.Destroy(ringRoot);
            root = ringRoot = null;
        }

        // --- uGUI helpers (same look as the tablet) ---

        static void Place(RectTransform t, RectTransform parent, float x, float y, float w, float h)
        {
            t.SetParent(parent, false);
            t.anchorMin = t.anchorMax = new Vector2(0f, 1f);
            t.pivot = new Vector2(0f, 1f);
            t.anchoredPosition = new Vector2(x, -y);
            t.sizeDelta = new Vector2(w, h);
        }

        // Anchored to the middle of the parent, offset (x right, y up).
        static void Centre(RectTransform t, float x, float y)
        {
            t.anchorMin = t.anchorMax = new Vector2(0.5f, 0.5f);
            t.pivot = new Vector2(0.5f, 0.5f);
            t.anchoredPosition = new Vector2(x, y);
        }

        static Image Box(RectTransform parent, float x, float y, float w, float h, Color c)
        {
            var img = new GameObject("Box", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            img.gameObject.layer = UiLayer;
            Place(img.rectTransform, parent, x, y, w, h);
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        static Text Label(RectTransform parent, float x, float y, float w, float h, int size, TextAnchor align, FontStyle style)
        {
            var t = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            t.gameObject.layer = UiLayer;
            Place(t.rectTransform, parent, x, y, w, h);
            t.font = font;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = align;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            var outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return t;
        }
    }
}
