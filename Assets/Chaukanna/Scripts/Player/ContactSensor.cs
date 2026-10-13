using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Player
{
    // What the observer can see and has locked. Sits on the observer camera.
    // Detection and lock are logged to ContactRegistry; nothing here ever shows the truth class.
    [RequireComponent(typeof(Camera))]
    public class ContactSensor : MonoBehaviour
    {
        [Header("Detection")]
        public float maxVisibleRange = 2000f;
        [Tooltip("Smallest apparent size (degrees) that can be picked out at referenceFov. Scales down with FOV, so binoculars help.")]
        public float minApparentDegrees = 0.15f;
        public float referenceFov = 60f;
        [Tooltip("Layers that block line of sight.")]
        public LayerMask terrainMask = ~0;

        [Header("Lock")]
        [Tooltip("How far off screen centre (degrees) a click still locks, at referenceFov. Scales with FOV.")]
        public float lockToleranceDegrees = 2f;
        [Tooltip("Lock label text size at 720p; scales with screen height.")]
        public int labelFontSize = 18;

        // Set by the time-of-day controller each run; not saved with the scene.
        [System.NonSerialized] public float sizeThresholdMultiplier = 1f;       // time of day, night vision, difficulty
        [System.NonSerialized] public float bestSizeThresholdMultiplier = 1f;   // the same with night vision on, for the detection clock
        [System.NonSerialized] public float rangeMultiplier = 1f;               // visibility

        public float EffectiveMaxRange => maxVisibleRange * rangeMultiplier;

        public Contact Locked { get; private set; }
        public int VisibleCount => visible.Count;
        public int DetectedTotal { get; private set; }

        readonly List<Contact> visible = new List<Contact>();
        Camera cam;
        Binoculars binoculars;
        bool cursorWasLocked;
        GUIStyle labelStyle;

        float FovScale => cam.fieldOfView / referenceFov;

        void Awake()
        {
            cam = GetComponent<Camera>();
            binoculars = GetComponent<Binoculars>();
        }

        void Update()
        {
            visible.Clear();
            float bestFov = binoculars != null ? Mathf.Min(binoculars.zoomedFov, binoculars.normalFov) : cam.fieldOfView;
            foreach (var c in ContactRegistry.Live)
            {
                if (c.Data.firstDetectableTime < 0f && Detectable(c, bestFov)) c.MarkDetectable();
                if (!CanSee(c, out float range, out float apparent)) continue;
                visible.Add(c);
                if (c.FirstInViewTime < 0f)
                {
                    DetectedTotal++;
                    c.MarkFirstInView($"range {range:F0} m, {apparent:F2} deg, fov {cam.fieldOfView:F0}");
                }
            }

            if (Locked == null && !ReferenceEquals(Locked, null)) Locked = null;   // despawned

            // The click that captures the mouse (ObserverLook) must not also lock.
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && cursorWasLocked && !ContactRegistry.InputBlocked) TryLockAtCentre();
            cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
        }

        // Could the player see it at the given FOV if they were looking the right way? Used as the start of the detection clock.
        bool Detectable(Contact c, float fov)
        {
            Vector3 eye = transform.position, pos = c.transform.position;
            float range = Vector3.Distance(eye, pos);
            if (range > EffectiveMaxRange) return false;
            if (2f * Mathf.Atan2(c.Definition.size * 0.5f, range) * Mathf.Rad2Deg < minApparentDegrees * bestSizeThresholdMultiplier * fov / referenceFov) return false;
            return !Physics.Linecast(eye, pos, terrainMask, QueryTriggerInteraction.Ignore);
        }

        public bool CanSee(Contact c, out float range, out float apparentDegrees)
        {
            Vector3 eye = transform.position;
            Vector3 pos = c.transform.position;
            range = Vector3.Distance(eye, pos);
            apparentDegrees = 2f * Mathf.Atan2(c.Definition.size * 0.5f, range) * Mathf.Rad2Deg;

            if (range > EffectiveMaxRange) return false;
            if (apparentDegrees < minApparentDegrees * sizeThresholdMultiplier * FovScale) return false;
            Vector3 vp = cam.WorldToViewportPoint(pos);
            if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return false;
            return !Physics.Linecast(eye, pos, terrainMask, QueryTriggerInteraction.Ignore);
        }

        // Locks the visible contact nearest the screen centre, or unlocks if there is none within tolerance.
        public bool TryLockAtCentre()
        {
            Contact best = null;
            float bestAngle = lockToleranceDegrees * FovScale;
            foreach (var c in visible)
            {
                float angle = Vector3.Angle(transform.forward, c.transform.position - transform.position);
                if (angle <= bestAngle) { best = c; bestAngle = angle; }
            }

            if (best == Locked) return best != null;
            if (Locked != null) ContactRegistry.Record(ContactEventType.Unlock, Locked, null);
            Locked = best;
            if (best != null) best.MarkLocked($"range {Vector3.Distance(transform.position, best.transform.position):F0} m");
            return best != null;
        }

        void OnGUI()
        {
            GUI.BeginGroup(PlayerView.Rect);
            DrawGui();
            GUI.EndGroup();
        }

        void DrawGui()
        {
            if (Event.current.type != EventType.Repaint) return;
            float cx = PlayerView.Width * 0.5f, cy = PlayerView.Height * 0.5f;
            Box(new Rect(cx - 1f, cy - 1f, 2f, 2f), new Color(1f, 1f, 1f, 0.6f));   // aim dot

            if (Locked == null) return;
            Vector3 sp = cam.WorldToScreenPoint(Locked.transform.position);
            if (sp.z <= 0f) return;
            float x = sp.x - PlayerView.Rect.x, y = PlayerView.Height - sp.y;   // screen pixels to this view's GUI space
            float ui = PlayerView.Height / 720f;
            float h = 14f * ui, len = 6f * ui, t = Mathf.Max(1f, 2f * ui);
            var col = Color.white;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float px = x + sx * h, py = y + sy * h;
                Box(new Rect(Mathf.Min(px, px - sx * len), py - t * 0.5f, len, t), col);
                Box(new Rect(px - t * 0.5f, Mathf.Min(py, py - sy * len), t, len), col);
            }

            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
            labelStyle.fontSize = Mathf.RoundToInt(labelFontSize * ui);
            float range = Vector3.Distance(transform.position, Locked.transform.position);
            // Id, range and the player's own call and action; never the truth class.
            string label = $"#{Locked.Id:00}  {range:F0} m";
            if (Locked.Call != PlayerCall.None) label += "  CALL: " + Locked.Call.Label();
            if (Locked.CanFireAgain) label += "  MISSED - F TO FIRE AGAIN";
            else if (Locked.OnHold) label += "  HELD - R / J / F to act";
            else if (Locked.Actioned) label += "  " + ActionTag(Locked.Action);
            GUI.Label(new Rect(x + h + 6f * ui, y - 20f * ui, 900f * ui, 40f * ui), label, labelStyle);
        }

        public static string ActionTag(PlayerAction a)
        {
            switch (a)
            {
                case PlayerAction.Hold: return "HELD";
                case PlayerAction.Report: return "REPORTED";
                case PlayerAction.Jam: return "JAMMED";
                case PlayerAction.Engage: return "ENGAGED";
                default: return "";
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
