using System.Collections.Generic;
using Kurukshetra.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kurukshetra.Player
{
    public enum LassoMode { Replace, Add, Remove }

    // Loop maths shared by the player's lasso and the trainer map's lasso. Points are screen or map pixels; any loop shape.
    public static class LassoMath
    {
        // Even-odd rule, so concave and self-crossing loops work the way they look.
        public static bool Inside(Vector2 p, List<Vector2> loop)
        {
            if (loop.Count < 3) return false;
            Copy(loop);
            return Inside(p.x, p.y, xs, ys, loop.Count);
        }

        // Plain float arrays: several times faster than going through the list for every edge (matters with 48+ contacts).
        static float[] xs = new float[64], ys = new float[64];
        static void Copy(List<Vector2> loop)
        {
            if (xs.Length < loop.Count) { xs = new float[loop.Count * 2]; ys = new float[loop.Count * 2]; }
            for (int i = 0; i < loop.Count; i++) { xs[i] = loop[i].x; ys[i] = loop[i].y; }
        }

        static bool Inside(float px, float py, float[] x, float[] y, int n)
        {
            bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
                if ((y[i] > py) != (y[j] > py) && px < (x[j] - x[i]) * (py - y[i]) / (y[j] - y[i]) + x[i]) inside = !inside;
            return inside;
        }

        // For testing many points against one loop (the crowd): copy the loop once, then call InsidePrepared per point.
        public static void Prepare(List<Vector2> loop, out Vector2 min, out Vector2 max)
        {
            min = max = loop.Count > 0 ? loop[0] : Vector2.zero;
            for (int i = 1; i < loop.Count; i++) { min = Vector2.Min(min, loop[i]); max = Vector2.Max(max, loop[i]); }
            Copy(loop);
        }

        public static bool InsidePrepared(float px, float py, int n) => Inside(px, py, xs, ys, n);

        public static float Area(List<Vector2> loop)
        {
            float twice = 0f;
            for (int i = 0, j = loop.Count - 1; i < loop.Count; j = i++) twice += (loop[j].x - loop[i].x) * (loop[j].y + loop[i].y);
            return Mathf.Abs(twice) * 0.5f;
        }

        public static bool InBand(float range, float near, float far) => range >= near && range <= far;

        // Adds the point when it is at least minStep from the last one, so a slow hand does not pile up points.
        public static bool AddPoint(List<Vector2> loop, Vector2 p, float minStep)
        {
            if (loop.Count > 0 && (loop[loop.Count - 1] - p).sqrMagnitude < minStep * minStep) return false;
            loop.Add(p);
            return true;
        }

        // The loop seen as a volume: the cone from the eye through the loop, cut to the near / far depth band.
        // Candidates must already have passed the player's detection test (ContactSensor.Visible), so the lasso can never
        // pick something the player could not see. Loop points are screen pixels from the bottom-left, as WorldToScreenPoint.
        public static void Pick(List<Contact> candidates, Camera cam, List<Vector2> loop, float near, float far, List<Contact> result)
        {
            result.Clear();
            if (loop.Count < 3) return;
            Vector2 min = loop[0], max = loop[0];   // the loop's box: most contacts outside it skip the full test
            for (int i = 1; i < loop.Count; i++) { min = Vector2.Min(min, loop[i]); max = Vector2.Max(max, loop[i]); }
            Copy(loop);
            int n = loop.Count;
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c == null) continue;
                Vector3 pos = c.transform.position;
                if (!InBand(Vector3.Distance(eye, pos), near, far)) continue;
                Vector3 sp = cam.WorldToScreenPoint(pos);
                if (sp.z <= 0f || sp.x < min.x || sp.x > max.x || sp.y < min.y || sp.y > max.y) continue;
                if (Inside(sp.x, sp.y, xs, ys, n)) result.Add(c);
            }
        }

        public static void Apply<T>(List<T> selection, IList<T> picked, LassoMode mode)
        {
            if (mode == LassoMode.Replace) selection.Clear();
            foreach (var p in picked)
            {
                if (mode == LassoMode.Remove) selection.Remove(p);
                else if (!selection.Contains(p)) selection.Add(p);
            }
        }

        public static LassoMode ModeFrom(bool shift, bool ctrl) => ctrl ? LassoMode.Remove : shift ? LassoMode.Add : LassoMode.Replace;

        // IMGUI line, rotation-free and clipped to the screen (see Trainer.GuiDraw: rotation broke under scaled matrices).
        public static void Line(Vector2 a, Vector2 b, float width, Color c)
        {
            Kurukshetra.Trainer.GuiDraw.Line(a, b, width, c, new Rect(-100000f, -100000f, 200000f, 200000f));
        }

        // A glowing loop: a wide faint stroke under a thin bright one, the closing edge fainter.
        public static void DrawLoop(IList<Vector2> guiPoints, Color colour, float ui)
        {
            int n = guiPoints.Count;
            if (n < 2) return;
            var glow = new Color(colour.r, colour.g, colour.b, 0.22f);
            for (int i = 1; i < n; i++) Line(guiPoints[i - 1], guiPoints[i], 7f * ui, glow);
            for (int i = 1; i < n; i++) Line(guiPoints[i - 1], guiPoints[i], Mathf.Max(1.5f, 2f * ui), colour);
            Line(guiPoints[n - 1], guiPoints[0], Mathf.Max(1f, 1.2f * ui), new Color(colour.r, colour.g, colour.b, 0.45f));
        }

        // Faint fill: the loop rasterised with scanlines into a small texture, stretched over the area. Rebuilt only when the
        // loop changes. guiPoints are in the same space as area (GUI pixels, top-left origin).
        public static void Fill(ref Texture2D tex, ref Color32[] pixels, IList<Vector2> guiPoints, Rect area, int downscale)
        {
            int w = Mathf.Max(8, Mathf.CeilToInt(area.width / downscale)), h = Mathf.Max(8, Mathf.CeilToInt(area.height / downscale));
            if (tex == null || tex.width != w || tex.height != h)
            {
                if (tex != null) Object.Destroy(tex);
                tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                pixels = new Color32[w * h];
            }
            var clear = new Color32(255, 255, 255, 0);
            var full = new Color32(255, 255, 255, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;
            int n = guiPoints.Count;
            var xs = new List<float>(16);
            for (int row = 0; row < h && n >= 3; row++)
            {
                float y = area.y + (row + 0.5f) * area.height / h;
                xs.Clear();
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    Vector2 a = guiPoints[i], b = guiPoints[j];
                    if ((a.y > y) != (b.y > y)) xs.Add(a.x + (y - a.y) * (b.x - a.x) / (b.y - a.y));
                }
                xs.Sort();
                int texRow = h - 1 - row;   // texture rows run bottom-up, GUI rows top-down
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int x0 = Mathf.Clamp(Mathf.CeilToInt((xs[k] - area.x) / area.width * w - 0.5f), 0, w);
                    int x1 = Mathf.Clamp(Mathf.FloorToInt((xs[k + 1] - area.x) / area.width * w - 0.5f), -1, w - 1);
                    for (int x = x0; x <= x1; x++) pixels[texRow * w + x] = full;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false);
        }
    }

    // The player's lasso. Hold Tab: the view stops turning and a cursor appears. Drag with the left button to draw a loop
    // round the drones; let go and every contact you can see right now (the normal detection test: range, size, in view,
    // line of sight past terrain, buildings and trees) that lies inside the loop and inside the depth band is selected.
    // Shift = add to the group, Ctrl = take out of it, neither = replace it. Mouse wheel = far edge of the depth band,
    // Alt + wheel = near edge, middle click resets it. A click without a loop picks the contact under the cursor.
    // Esc drops the loop being drawn. Works through the binoculars (the zoomed camera is used as it is) and in VR mode.
    [RequireComponent(typeof(ContactSensor))]
    public class LassoSelect : MonoBehaviour
    {
        public static bool Holding { get; private set; }

        [Tooltip("A new loop point is added every this many pixels (at 720p).")]
        public float minStepPixels = 4f;
        [Tooltip("A click without a loop picks the nearest contact within this many pixels of the cursor (at 720p).")]
        public float clickPickPixels = 26f;
        public int maxPoints = 600;
        public Color lineColour = new Color(1f, 0.85f, 0.25f);
        [Tooltip("Mouse wheel step of the depth band, metres.")]
        public float bandStep = 100f;

        public float Near { get; private set; }
        public float Far { get; private set; } = float.MaxValue;
        public bool BandSet => Near > 0f || Far < sensor.EffectiveMaxRange;
        public int LastPicked { get; private set; }

        ContactSensor sensor;
        Camera cam;
        readonly List<Vector2> loop = new List<Vector2>();       // screen pixels, bottom-left origin
        readonly List<Vector2> guiLoop = new List<Vector2>();
        readonly List<Contact> picked = new List<Contact>();
        readonly List<Contact> candidates = new List<Contact>();
        readonly List<int> crowdScratch = new List<int>();
        Vector2 cursor;
        bool drawing, fillDirty;
        LassoMode liveMode;
        string result;
        float resultUntil;
        Texture2D fillTex;
        Color32[] fillPixels;
        GUIStyle style;

        void Awake()
        {
            sensor = GetComponent<ContactSensor>();
            cam = GetComponent<Camera>();
        }

        void OnDisable() { Holding = false; drawing = false; loop.Clear(); }
        void OnDestroy() { if (fillTex != null) Destroy(fillTex); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Holding = false; }

        float Ui => PlayerView.Height / 720f;

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null || mouse == null) return;
            float maxRange = sensor.EffectiveMaxRange;
            if (Far > maxRange) Far = maxRange;
            if (Near > Far) Near = 0f;

            bool want = kb.tabKey.isPressed && !ContactRegistry.InputBlocked && Application.isFocused;
            if (want && !Holding)
            {
                Holding = true;
                var view = PlayerView.Rect;
                cursor = Cursor.lockState == CursorLockMode.Locked ? new Vector2(view.x + view.width * 0.5f, view.height * 0.5f) : mouse.position.ReadValue();
            }
            else if (!want && Holding)
            {
                Holding = false;
                drawing = false;
                loop.Clear();
                picked.Clear();
            }
            if (!Holding) return;

            // With the mouse captured the cursor is ours, moved by the mouse; otherwise it is the real pointer.
            if (Cursor.lockState == CursorLockMode.Locked) cursor += mouse.delta.ReadValue();
            else cursor = mouse.position.ReadValue();
            var r = PlayerView.Rect;
            cursor.x = Mathf.Clamp(cursor.x, r.x, r.xMax - 1f);
            cursor.y = Mathf.Clamp(cursor.y, 0f, r.height - 1f);

            // Depth band.
            float wheel = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                bool alt = kb.leftAltKey.isPressed || kb.rightAltKey.isPressed;
                float step = Mathf.Sign(wheel) * bandStep;
                if (alt) Near = Mathf.Clamp(Near + step, 0f, Mathf.Max(0f, Far - bandStep));
                else Far = Mathf.Clamp(Far + step, Near + bandStep, maxRange);
            }
            if (mouse.middleButton.wasPressedThisFrame) { Near = 0f; Far = maxRange; }

            liveMode = LassoMath.ModeFrom(kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed, kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed);
            if (kb.escapeKey.wasPressedThisFrame) { drawing = false; loop.Clear(); picked.Clear(); fillDirty = true; return; }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                drawing = true;
                loop.Clear();
                loop.Add(cursor);
                fillDirty = true;
            }
            else if (drawing && mouse.leftButton.isPressed)
            {
                if (loop.Count < maxPoints && LassoMath.AddPoint(loop, cursor, minStepPixels * Ui)) fillDirty = true;
            }
            else if (drawing)
            {
                Finish(liveMode);
                return;
            }

            if (drawing) Preview();
        }

        void Candidates()
        {
            candidates.Clear();
            foreach (var c in sensor.Visible) candidates.Add(c);
        }

        void Preview()
        {
            Candidates();
            LassoMath.Pick(candidates, cam, loop, Near, Far, picked);
        }

        void Finish(LassoMode mode)
        {
            drawing = false;
            Candidates();
            float minArea = 12f * 12f * Ui * Ui;
            int crowdMade = 0, crowdTotal = 0;
            var crowd = CrowdSky.Active;
            if (loop.Count >= 3 && LassoMath.Area(loop) >= minArea)
            {
                LassoMath.Pick(candidates, cam, loop, Near, Far, picked);
                // Dense sky: crowd drones in the loop become real contacts (up to 48, nearest the post first).
                if (crowd != null && mode != LassoMode.Remove) crowdMade = crowd.PromoteForLasso(cam, loop, Near, Far, sensor, picked, out crowdTotal);
                if (mode != LassoMode.Remove)
                    foreach (var swarmSky in SwarmSky.All) { crowdMade += swarmSky.PromoteForLasso(cam, loop, Near, Far, sensor, picked, out int swarmTotal); crowdTotal += swarmTotal; }
            }
            else
            {
                PickNearCursor();
                if (picked.Count == 0 && crowd != null && mode != LassoMode.Remove) PickCrowdNearCursor(crowd);
                if (mode != LassoMode.Remove)
                    foreach (var swarmSky in SwarmSky.All) { if (picked.Count > 0) break; PickSwarmNearCursor(swarmSky); }
            }
            sensor.ApplyLasso(picked, mode, "lasso");
            LastPicked = picked.Count;
            string verb = mode == LassoMode.Remove ? "taken out of the group" : mode == LassoMode.Add ? "added" : "selected";
            result = picked.Count == 0 ? "Nothing you can see is inside the loop" : $"{picked.Count} {verb}  ({sensor.Selected.Count} in group)";
            if (crowdTotal > crowdMade) result += $"   {crowdMade} of {crowdTotal} crowd drones in the loop: tighten the loop";
            resultUntil = Time.unscaledTime + 2f;
            loop.Clear();
            picked.Clear();
            fillDirty = true;
        }

        // A click: the visible contact nearest the cursor, within reach and in the band.
        void PickNearCursor()
        {
            picked.Clear();
            Contact best = null;
            float bestDist = clickPickPixels * Ui;
            Vector3 eye = cam.transform.position;
            foreach (var c in candidates)
            {
                if (c == null || !LassoMath.InBand(Vector3.Distance(eye, c.transform.position), Near, Far)) continue;
                Vector3 sp = cam.WorldToScreenPoint(c.transform.position);
                if (sp.z <= 0f) continue;
                float d = Vector2.Distance(new Vector2(sp.x, sp.y), cursor);
                if (d <= bestDist) { best = c; bestDist = d; }
            }
            if (best != null) picked.Add(best);
        }

        // A click without a loop: the crowd drone nearest the cursor (within the same pixel reach), promoted.
        void PickCrowdNearCursor(CrowdSky crowd)
        {
            Ray ray = cam.ScreenPointToRay(new Vector3(cursor.x, cursor.y, 0f));
            float degPerPixel = cam.fieldOfView / Mathf.Max(1, cam.pixelHeight);
            int index = crowd.Pick(cam, ray.direction, clickPickPixels * Ui * degPerPixel, sensor);
            if (index < 0) return;
            if (!LassoMath.InBand(Vector3.Distance(cam.transform.position, crowd.PositionOf(index)), Near, Far)) return;
            var c = crowd.Promote(index);
            if (c != null) picked.Add(c);
        }

        // Same for the big swarm.
        void PickSwarmNearCursor(SwarmSky swarm)
        {
            Ray ray = cam.ScreenPointToRay(new Vector3(cursor.x, cursor.y, 0f));
            float degPerPixel = cam.fieldOfView / Mathf.Max(1, cam.pixelHeight);
            int index = swarm.Pick(cam, ray.direction, clickPickPixels * Ui * degPerPixel, sensor, out _);
            if (index < 0) return;
            if (!LassoMath.InBand(Vector3.Distance(cam.transform.position, swarm.PositionOf(index)), Near, Far)) return;
            var c = swarm.Promote(index);
            if (c != null) picked.Add(c);
        }

        void OnGUI()
        {
            if (PlayerView.HideOverlays || Event.current.type != EventType.Repaint) return;
            bool showResult = Time.unscaledTime < resultUntil;
            if (!Holding && !showResult) return;
            GUI.BeginGroup(PlayerView.Rect);
            float ui = Ui;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = true, fontStyle = FontStyle.Bold };
            style.fontSize = Mathf.RoundToInt(16f * ui);
            style.normal.textColor = Color.white;
            float w = PlayerView.Width, h = PlayerView.Height;

            if (showResult && !Holding) Banner(new Rect(w * 0.5f - 320f * ui, h * 0.5f + 70f * ui, 640f * ui, 30f * ui), result);

            if (Holding)
            {
                string band = BandSet ? $"<color=#FFD940>DEPTH {Near:F0} - {Far:F0} m</color>" : $"DEPTH all (to {Far:F0} m)";
                Banner(new Rect(w * 0.5f - 420f * ui, 12f * ui, 840f * ui, 30f * ui),
                    $"LASSO   drag round drones   {band}");
                var hintStyle = new GUIStyle(style) { fontStyle = FontStyle.Normal, fontSize = Mathf.RoundToInt(13f * ui) };
                var hint = new Rect(w * 0.5f - 420f * ui, 44f * ui, 840f * ui, 24f * ui);
                Box(hint, new Color(0f, 0f, 0f, 0.45f));
                GUI.Label(hint, "Shift add   Ctrl remove   Wheel far edge   Alt+wheel near edge   Middle click reset   Esc cancel   let go of Tab to look", hintStyle);

                guiLoop.Clear();
                foreach (var p in loop) guiLoop.Add(ToGui(p));
                Vector2 cur = ToGui(cursor);
                if (drawing && guiLoop.Count >= 3)
                {
                    var area = new Rect(0f, 0f, w, h);
                    if (fillDirty || fillTex == null) { LassoMath.Fill(ref fillTex, ref fillPixels, guiLoop, area, 6); fillDirty = false; }
                    var old = GUI.color;
                    GUI.color = new Color(lineColour.r, lineColour.g, lineColour.b, 0.13f);
                    GUI.DrawTexture(area, fillTex);
                    GUI.color = old;
                }
                if (drawing) LassoMath.DrawLoop(guiLoop, liveMode == LassoMode.Remove ? new Color(1f, 0.4f, 0.35f) : lineColour, ui);

                // What would be caught: a ring on each, so the soldier sees the effect before letting go.
                var ring = liveMode == LassoMode.Remove ? new Color(1f, 0.4f, 0.35f) : lineColour;
                foreach (var c in picked)
                {
                    if (c == null) continue;
                    Vector3 sp = cam.WorldToScreenPoint(c.transform.position);
                    if (sp.z <= 0f) continue;
                    Ring(ToGui(new Vector2(sp.x, sp.y)), 11f * ui, ring, ui);
                }

                // Cursor: a small cross with the live count and mode beside it.
                var cross = new Color(1f, 1f, 1f, 0.9f);
                Box(new Rect(cur.x - 8f * ui, cur.y - 1f, 16f * ui, 2f), cross);
                Box(new Rect(cur.x - 1f, cur.y - 8f * ui, 2f, 16f * ui), cross);
                if (drawing)
                {
                    string mode = liveMode == LassoMode.Add ? "+ " : liveMode == LassoMode.Remove ? "- " : "";
                    var badge = new Rect(cur.x + 12f * ui, cur.y + 6f * ui, 150f * ui, 24f * ui);
                    Box(badge, new Color(0f, 0f, 0f, 0.6f));
                    int crowdInLoop = 0;
                    var crowd = CrowdSky.Active;
                    if (crowd != null && loop.Count >= 3) { crowd.InLoop(cam, loop, Near, Far, sensor, crowdScratch); crowdInLoop = crowdScratch.Count; }
                    if (loop.Count >= 3) foreach (var sky in SwarmSky.All) { sky.InLoop(cam, loop, Near, Far, sensor, crowdScratch); crowdInLoop += crowdScratch.Count; }
                    GUI.Label(badge, $"{mode}{picked.Count + crowdInLoop} in loop", style);
                }
            }
            GUI.EndGroup();
        }

        Vector2 ToGui(Vector2 screen) => new Vector2(screen.x - PlayerView.Rect.x, PlayerView.Height - screen.y);

        void Banner(Rect r, string text)
        {
            Box(r, new Color(0f, 0f, 0f, 0.6f));
            GUI.Label(r, text, style);
        }

        static void Ring(Vector2 c, float radius, Color col, float ui)
        {
            const int segments = 16;
            Vector2 prev = c + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector2 next = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                LassoMath.Line(prev, next, Mathf.Max(1.5f, 2f * ui), col);
                prev = next;
            }
        }

        static void Box(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
