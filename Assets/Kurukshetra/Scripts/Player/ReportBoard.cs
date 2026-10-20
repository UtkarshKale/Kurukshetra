using System.Collections.Generic;
using Kurukshetra.Net;
using Kurukshetra.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kurukshetra.Player
{
    // One sentry report as it reached the commander's seat.
    public class ReportEntry
    {
        public int id, call;
        public string seat = "";
        public float hostTime, localTime;       // host scenario time of the report; this PC's scenario time when it arrived
        public float range, bearing;            // as the reporting seat saw it
        public string fate = "";                // filled in once the contact is gone ("jammed", "reached the post", ...)
    }

    // The commander's picture (LAN team mode): the sentries' reports as a list, a mini-map and amber brackets on the reported
    // contacts. T slews the view to the newest report nobody has dealt with and locks that contact; Shift+T goes to the next one.
    // Added to the observer camera when a run starts on a COMMANDER seat. It shows what was REPORTED (the sentry's call), never
    // the truth class.
    public class ReportBoard : MonoBehaviour
    {
        // Oldest first. Written by LanSession when a report arrives; read by the board and by the self-tests / live test.
        public static readonly List<ReportEntry> Entries = new List<ReportEntry>();
        public static int Slews;                // T / Shift+T slews done this run

        public const int MaxLines = 10;
        public const float MapRingStep = 500f;

        public static void Clear()
        {
            Entries.Clear();
            Slews = 0;
        }

        // A report arrives. A second report on the same contact (the sentry changed the call) updates its line and moves it up.
        public static ReportEntry Add(ReportMsg m)
        {
            ReportEntry e = Entries.Find(x => x.id == m.id);
            if (e == null) { e = new ReportEntry { id = m.id }; }
            else Entries.Remove(e);
            e.call = m.call;
            e.seat = m.seat ?? "";
            e.hostTime = m.time;
            e.localTime = ContactRegistry.ScenarioTime;
            e.range = m.range;
            e.bearing = m.bearing;
            e.fate = "";
            Entries.Add(e);
            return e;
        }

        static Contact LiveContact(int id)
        {
            foreach (var c in ContactRegistry.Live) if (c != null && c.Id == id) return c;
            return null;
        }

        // Reported, still in the air, and this seat has not decided on it yet (a Hold is not a decision).
        public static bool Unhandled(ReportEntry e, out Contact c)
        {
            c = LiveContact(e.id);
            return c != null && !c.Decided && c.Data.handledTime < 0f;
        }

        // The n-th unhandled report counting from the newest; null when there are fewer.
        public static ReportEntry NextUnhandled(int n = 0)
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
                if (Unhandled(Entries[i], out _) && n-- <= 0) return Entries[i];
            return null;
        }

        public static int UnhandledCount()
        {
            int n = 0;
            foreach (var e in Entries) if (Unhandled(e, out _)) n++;
            return n;
        }

        Camera cam;
        ContactSensor sensor;
        ObserverLook look;
        ScenarioSpawner spawner;
        int cursor;
        GUIStyle style, small, amberStyle;
        Texture2D ringTex;
        float ringTexRadius;

        void Awake()
        {
            cam = GetComponent<Camera>();
            sensor = GetComponent<ContactSensor>();
            look = GetComponent<ObserverLook>();
        }

        void OnDestroy()
        {
            if (ringTex != null) Destroy(ringTex);
        }

        void Update()
        {
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            var kb = Keyboard.current;
            if (kb == null || ContactRegistry.InputBlocked || spawner == null || spawner.Finished) return;
            if (kb.tKey.wasPressedThisFrame) Slew(kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        }

        // T = the newest unhandled report, Shift+T = the next one down the list. Turns the view over 0.3 s and locks the contact.
        public bool Slew(bool next)
        {
            int count = UnhandledCount();
            if (count == 0) { if (sensor != null) sensor.Say("No unhandled reports."); return false; }
            cursor = next ? (cursor + 1) % count : 0;
            var e = NextUnhandled(cursor);
            if (e == null || !Unhandled(e, out var c)) return false;
            if (look != null) look.SlewTo(c.transform.position, 0.3f);
            if (sensor != null)
            {
                sensor.LockContact(c, "report slew");
                sensor.Say($"RPT {e.seat}: {((PlayerCall)e.call).Label()}  (#{e.id:00}, {cursor + 1} of {count} unhandled)", 2.5f);
            }
            Slews++;
            return true;
        }

        // --- Drawing ---

        static Color CallColour(int call)
        {
            switch ((PlayerCall)call)
            {
                case PlayerCall.HostileQuad: return new Color(1f, 0.28f, 0.22f);
                case PlayerCall.HostileFixedWing: return new Color(1f, 0.58f, 0.12f);
                case PlayerCall.OwnIsrDrone: return new Color(0.3f, 0.9f, 0.42f);
                case PlayerCall.CivilianDrone: return new Color(0.45f, 0.72f, 1f);
                default: return new Color(0.82f, 0.82f, 0.5f);
            }
        }

        void OnGUI()
        {
            if (PlayerView.HideOverlays || ViewRigSwitch.HeadsetActive || Entries.Count == 0 && !show) return;      // in the headset the list is on the HUD (VrHud)
            GUI.BeginGroup(PlayerView.Rect);
            DrawGui();
            GUI.EndGroup();
        }

        bool show => RunConditions.SeatRole == SeatRole.Commander;

        void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || spawner == null || spawner.Finished || cam == null) return;
            float ui = PlayerView.Height / 720f;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false, richText = true };
                small = new GUIStyle(style);
            }
            style.fontSize = Mathf.RoundToInt(15f * ui);
            small.fontSize = Mathf.RoundToInt(13f * ui);
            style.normal.textColor = Color.white;
            small.normal.textColor = new Color(1f, 1f, 1f, 0.8f);

            float pad = 8f * ui, lineH = 20f * ui;
            float x = pad, y = 44f * ui;
            int lines = Mathf.Min(MaxLines, Entries.Count);
            float listH = lineH * (lines + 1) + pad * 1.5f, listW = 470f * ui;
            Fill(new Rect(x, y, listW, listH), new Color(0f, 0f, 0f, 0.55f));
            int pending = UnhandledCount();
            GUI.Label(new Rect(x + pad, y + pad * 0.5f, listW, lineH), $"<b>REPORTS</b>  <color=#FFFFFFAA>{pending} unhandled   T newest   Shift+T next</color>", style);
            var eye = cam.transform.position;
            for (int i = 0; i < lines; i++)
            {
                var e = Entries[Entries.Count - 1 - i];
                float ly = y + pad * 0.5f + lineH * (i + 1);
                string call = ((PlayerCall)e.call).Label();
                var c = LiveContact(e.id);
                float age = ContactRegistry.ScenarioTime - e.localTime;
                if (c != null)
                {
                    Vector3 d = c.transform.position - eye;
                    e.range = d.magnitude;
                    e.bearing = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f);
                    string state = c.Data.handledTime >= 0f ? $"  <color=#9ADFA0>{c.Data.handledAction.ToLowerInvariant()} by {c.Data.handledBy}</color>" : c.Decided ? $"  <color=#9ADFA0>{c.Action.ToString().ToLowerInvariant()}</color>" : "";
                    GUI.Label(new Rect(x + pad, ly, listW, lineH), $"<color=#{ColorUtility.ToHtmlStringRGB(CallColour(e.call))}>#{e.id:00}  {call,-18}</color> by {e.seat}   {age:F0} s ago   {e.range:F0} m  {e.bearing:000} deg{state}", small);
                }
                else
                {
                    if (e.fate.Length == 0) e.fate = FateOf(e.id);
                    GUI.Label(new Rect(x + pad, ly, listW, lineH), $"<color=#8A8F96>#{e.id:00}  {call,-18} by {e.seat}   {age:F0} s ago   gone: {e.fate}</color>", small);
                }
            }

            DrawMap(x, y + listH + pad, 220f * ui, ui);
            DrawBrackets(ui);
        }

        static string FateOf(int id)
        {
            var r = ContactRegistry.Records.Find(x => x.id == id);
            if (r == null || string.IsNullOrEmpty(r.despawnReason)) return "no longer tracked";
            switch (r.despawnReason)
            {
                case "JammedDown": return "jammed, came down";
                case "EngagedHit": return "shot down";
                case "ReachedDefendedPoint": return "reached the post";
                case "ExitedSector": return "left the sector";
                case "ScenarioEnd": return "run ended";
                default: return r.despawnReason.ToLowerInvariant();
            }
        }

        // Post at the centre, north up, range rings every 500 m, the reported contacts as dots in the colour of the REPORTED call,
        // and the commander's view cone.
        void DrawMap(float x, float y, float size, float ui)
        {
            Vector3 post = spawner.defendedPoint != null ? spawner.defendedPoint.position : spawner.transform.position;
            float far = 0f;
            foreach (var e in Entries)
            {
                var c = LiveContact(e.id);
                if (c == null) continue;
                Vector3 d = c.transform.position - post;
                far = Mathf.Max(far, new Vector2(d.x, d.z).magnitude);
            }
            float radius = Mathf.Max(3f * MapRingStep, Mathf.Ceil((far + 100f) / MapRingStep) * MapRingStep);
            var box = new Rect(x, y, size, size);
            Fill(box, new Color(0.02f, 0.05f, 0.04f, 0.72f));
            EnsureRings(radius);
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GUI.DrawTexture(box, ringTex);
            GUI.color = old;
            float half = size * 0.5f;
            Vector2 centre = new Vector2(x + half, y + half);

            // View cone.
            float yaw = cam.transform.eulerAngles.y;
            float hfov = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect);
            for (int s = -1; s <= 1; s += 2)
            {
                float a = (yaw + s * hfov * 0.5f) * Mathf.Deg2Rad;
                Line(centre, centre + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * half, new Color(1f, 1f, 1f, 0.5f), Mathf.Max(1f, ui));
            }
            Fill(new Rect(centre.x - 3f * ui, centre.y - 3f * ui, 6f * ui, 6f * ui), Color.white);       // the post

            foreach (var e in Entries)
            {
                var c = LiveContact(e.id);
                if (c == null) continue;
                Vector3 d = c.transform.position - post;
                var p = centre + new Vector2(d.x, -d.z) / radius * half;
                p = centre + Vector2.ClampMagnitude(p - centre, half - 3f * ui);
                float dot = 7f * ui;
                Fill(new Rect(p.x - dot * 0.5f, p.y - dot * 0.5f, dot, dot), CallColour(e.call));
            }
            GUI.Label(new Rect(x + 4f * ui, y + size - 18f * ui, size, 18f * ui), $"N up   rings {MapRingStep:F0} m   edge {radius:F0} m", small);
        }

        // Concentric rings every MapRingStep, drawn once into a texture and reused until the map scale changes.
        void EnsureRings(float radius)
        {
            if (ringTex != null && Mathf.Approximately(radius, ringTexRadius)) return;
            const int n = 192;
            if (ringTex == null) { ringTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave }; }
            ringTexRadius = radius;
            var px = new Color32[n * n];
            float half = n * 0.5f;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float dx = i + 0.5f - half, dy = j + 0.5f - half;
                    float metres = Mathf.Sqrt(dx * dx + dy * dy) / half * radius;
                    float nearest = Mathf.Round(metres / MapRingStep) * MapRingStep;
                    bool ring = nearest > 0f && nearest < radius * 0.999f && Mathf.Abs(metres - nearest) < radius / half * 0.75f;
                    bool cross = (Mathf.Abs(dx) < 0.6f || Mathf.Abs(dy) < 0.6f) && metres < radius;
                    px[j * n + i] = ring ? new Color32(120, 200, 150, 190) : cross ? new Color32(90, 150, 120, 70) : new Color32(0, 0, 0, 0);
                }
            ringTex.SetPixels32(px);
            ringTex.Apply(false);
        }

        // Amber corner brackets on the reported contacts that are on screen, with "RPT S-014: Hostile quad".
        void DrawBrackets(float ui)
        {
            var amber = new Color(1f, 0.75f, 0.1f);
            int shown = 0;
            for (int i = Entries.Count - 1; i >= 0 && shown < MaxLines; i--, shown++)
            {
                var e = Entries[i];
                var c = LiveContact(e.id);
                if (c == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(c.transform.position);
                if (sp.z <= 0f) continue;
                float x = sp.x - PlayerView.Rect.x, y = PlayerView.Height - sp.y;
                float h = 18f * ui, len = 7f * ui, t = Mathf.Max(1f, 2f * ui);
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sy = -1; sy <= 1; sy += 2)
                    {
                        float px = x + sx * h, py = y + sy * h;
                        Fill(new Rect(Mathf.Min(px, px - sx * len), py - t * 0.5f, len, t), amber);
                        Fill(new Rect(px - t * 0.5f, Mathf.Min(py, py - sy * len), t, len), amber);
                    }
                if (amberStyle == null) amberStyle = new GUIStyle(small);
                amberStyle.fontSize = small.fontSize;
                amberStyle.normal.textColor = amber;
                GUI.Label(new Rect(x + h + 6f * ui, y + h - 4f * ui, 420f * ui, 22f * ui), $"RPT {e.seat}: {((PlayerCall)e.call).Label()}", amberStyle);
            }
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Line(Vector2 a, Vector2 b, Color c, float width) => Kurukshetra.Trainer.GuiDraw.Line(a, b, width, c, new Rect(-100000f, -100000f, 200000f, 200000f));
    }
}
