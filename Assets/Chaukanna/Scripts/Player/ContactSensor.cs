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

        // Headset: the view is not the screen. The headset rig (OpenXrViewRig) fills this every frame: the lock ray (the right hand's
        // pointer, or the optic's centre when the binoculars are up), the field-of-view box that "in view" is tested against, and the
        // vertical FOVs that scale the size threshold and the lock tolerance (a headset camera's own fieldOfView cannot zoom).
        // Inactive on the desktop and in VR-sim, where the camera itself is the view.
        public struct ViewOverride
        {
            public bool active;
            public Vector3 aimOrigin, aimDirection;
            public Quaternion viewRotation;
            public float halfH, halfV;          // half field of view of the box, degrees
            public float detectFov, lockFov;    // see above
        }
        public ViewOverride Headset;

        // Multi-select: Ctrl (or Shift) + click adds or removes the contact under the aim dot, holding the button and
        // sweeping adds every contact the dot passes over, G selects everything in view, X or Esc clears, and holding Tab
        // draws a lasso round several at once (LassoSelect).
        // While the list is not empty, the classify and action keys apply to every contact in it.
        public readonly List<Contact> Selected = new List<Contact>();
        readonly List<Contact> targets = new List<Contact>();

        // What the decision keys act on: the selection if there is one, otherwise the locked contact.
        public List<Contact> Targets
        {
            get
            {
                targets.Clear();
                if (Selected.Count > 0) targets.AddRange(Selected);
                else if (Locked != null) targets.Add(Locked);
                return targets;
            }
        }

        public void ClearSelection()
        {
            Selected.Clear();
        }

        public bool Select(Contact c, bool toggle = false)
        {
            if (c == null) return false;
            if (Selected.Contains(c))
            {
                if (toggle) Selected.Remove(c);
                return !toggle;
            }
            // The contact that was simply locked joins the group, so "lock one, Ctrl+click the next" gives two.
            if (Selected.Count == 0 && Locked != null && Locked != c) Selected.Add(Locked);
            Selected.Add(c);
            if (c.FirstLockTime < 0f) c.MarkLocked($"range {Vector3.Distance(transform.position, c.transform.position):F0} m (group)");
            return true;
        }

        public int SelectAllInView()
        {
            foreach (var c in visible) Select(c);
            return Selected.Count;
        }
        public int VisibleCount => visible.Count;
        // What the player can see this frame (the detection test below). The lasso only ever picks from this.
        public List<Contact> Visible => visible;

        // The lasso's result. Replace = the picked contacts become the group, Add = join it, Remove = leave it.
        public void ApplyLasso(List<Contact> picked, LassoMode mode, string how)
        {
            if (mode == LassoMode.Add && Selected.Count == 0 && Locked != null) Selected.Add(Locked);   // the locked one stays in
            LassoMath.Apply(Selected, picked, mode);
            if (mode == LassoMode.Remove) return;
            foreach (var c in picked)
                if (c != null && c.FirstLockTime < 0f) c.MarkLocked($"range {Vector3.Distance(transform.position, c.transform.position):F0} m ({how})");
        }

        // The same detection test the lasso relies on, for any list of contacts (self-tests).
        public void SeeNow(IEnumerable<Contact> from, List<Contact> into)
        {
            into.Clear();
            foreach (var c in from) if (c != null && CanSee(c, out _, out _)) into.Add(c);
        }
        public int DetectedTotal { get; private set; }

        readonly List<Contact> visible = new List<Contact>();
        Camera cam;
        Binoculars binoculars;
        bool cursorWasLocked;
        GUIStyle labelStyle;

        Camera Cam => cam != null ? cam : (cam = GetComponent<Camera>());
        float FovScale => (Headset.active ? Headset.detectFov : Cam.fieldOfView) / referenceFov;
        float LockScale => (Headset.active ? Headset.lockFov : Cam.fieldOfView) / referenceFov;

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

            Selected.RemoveAll(c => c == null);    // despawned

            // Positive identification: a target looked at through the binoculars (zoomed in) while it is in view.
            if (binoculars != null && binoculars.ZoomBlend > 0.8f)
            {
                if (Locked != null && visible.Contains(Locked)) Locked.Data.opticsVerified = true;
                foreach (var c in Selected) if (c != null && visible.Contains(c)) c.Data.opticsVerified = true;
            }

            // The click that captures the mouse (ObserverLook) must not also lock.
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            bool add = kb != null && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed || kb.leftShiftKey.isPressed && !RunConditions.AllowMovement);
            bool lasso = LassoSelect.Holding;   // Tab held: the lasso owns the mouse and Esc
            if (mouse != null && cursorWasLocked && !ContactRegistry.InputBlocked && !lasso)
            {
                if (add)
                {
                    // Click toggles; keeping the button down and sweeping only ever adds.
                    if (mouse.leftButton.wasPressedThisFrame) Select(AtCentre(), true);
                    else if (mouse.leftButton.isPressed) Select(AtCentre());
                }
                else if (mouse.leftButton.wasPressedThisFrame)
                {
                    ClearSelection();
                    TryLockAtCentre();
                }
            }
            if (kb != null && !ContactRegistry.InputBlocked && !lasso)
            {
                if (kb.gKey.wasPressedThisFrame) SelectAllInView();
                if (kb.xKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame) ClearSelection();
            }
            cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
        }

        // The visible contact nearest the aim dot, within the lock tolerance. Null = none.
        Contact AtCentre()
        {
            Contact best = null;
            float bestAngle = lockToleranceDegrees * LockScale;
            Vector3 aimFrom = Headset.active ? Headset.aimOrigin : transform.position;
            Vector3 aimDir = Headset.active ? Headset.aimDirection : transform.forward;
            foreach (var c in visible)
            {
                float angle = Vector3.Angle(aimDir, c.transform.position - aimFrom);
                if (angle <= bestAngle) { best = c; bestAngle = angle; }
            }
            // Dense sky: a crowd drone nearer the aim than the best contact is promoted to a real contact and returned.
            var crowd = CrowdSky.Active;
            if (crowd != null)
            {
                int index = crowd.Pick(Cam, aimDir, bestAngle, this, out float crowdAngle);
                if (index >= 0 && crowdAngle < bestAngle)
                {
                    var promoted = crowd.Promote(index);
                    if (promoted != null) return promoted;
                }
            }
            // Big swarm: same idea.
            foreach (var swarm in SwarmSky.All)
            {
                int index = swarm.Pick(Cam, aimDir, bestAngle, this, out float swarmAngle);
                if (index >= 0 && swarmAngle < bestAngle)
                {
                    var promoted = swarm.Promote(index);
                    if (promoted != null) return promoted;
                }
            }
            return best;
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
            return CanSeePoint(c.transform.position, c.Definition.size, out range, out apparentDegrees);
        }

        // The detection test for any point: a contact, or a crowd drone that is not a contact yet (dense sky). lineOfSight
        // false skips the Physics.Linecast, for a cheap first pass over many candidates.
        public bool CanSeePoint(Vector3 pos, float size, out float range, out float apparentDegrees, bool lineOfSight = true)
        {
            Vector3 eye = transform.position;
            range = Vector3.Distance(eye, pos);
            apparentDegrees = 2f * Mathf.Atan2(size * 0.5f, range) * Mathf.Rad2Deg;

            if (range > EffectiveMaxRange) return false;
            if (apparentDegrees < minApparentDegrees * sizeThresholdMultiplier * FovScale) return false;
            if (Headset.active)
            {
                // A headset has no single viewport: in view = inside the eye's (or the optic's) angular box.
                if (!InAngularBox(pos, eye, Headset.viewRotation, Headset.halfH, Headset.halfV)) return false;
            }
            else
            {
                Vector3 vp = Cam.WorldToViewportPoint(pos);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) return false;
            }
            return !lineOfSight || LineOfSight(pos);
        }

        // Is the point within halfH / halfV degrees (horizontal / vertical) of the view direction?
        public static bool InAngularBox(Vector3 point, Vector3 eye, Quaternion viewRotation, float halfH, float halfV)
        {
            Vector3 local = Quaternion.Inverse(viewRotation) * (point - eye);
            if (local.z <= 0f) return false;
            return Mathf.Abs(Mathf.Atan2(local.x, local.z)) * Mathf.Rad2Deg <= halfH && Mathf.Abs(Mathf.Atan2(local.y, local.z)) * Mathf.Rad2Deg <= halfV;
        }

        public bool LineOfSight(Vector3 pos) => !Physics.Linecast(transform.position, pos, terrainMask, QueryTriggerInteraction.Ignore);

        // A short message under the aim dot ("IFF not available").
        string say;
        float sayUntil;
        public string Said => Time.unscaledTime < sayUntil ? say ?? "" : "";      // the message showing now (self-tests)
        public void Say(string text, float seconds = 2f)
        {
            say = text;
            sayUntil = Time.unscaledTime + seconds;
        }

        // Locks the visible contact nearest the screen centre, or unlocks if there is none within tolerance.
        public bool TryLockAtCentre()
        {
            Contact best = AtCentre();
            if (best == Locked) return best != null;
            if (Locked != null) ContactRegistry.Record(ContactEventType.Unlock, Locked, null);
            Locked = best;
            if (best != null) best.MarkLocked($"range {Vector3.Distance(transform.position, best.transform.position):F0} m");
            return best != null;
        }

        // Locks a given contact whether or not it is under the aim dot: the commander's T key (slew to a report), a team client's
        // crowd pick coming back from the host. Same bookkeeping as a click.
        public bool LockContact(Contact c, string how)
        {
            if (c == null) return false;
            ClearSelection();
            if (c == Locked) return true;
            if (Locked != null) ContactRegistry.Record(ContactEventType.Unlock, Locked, null);
            Locked = c;
            c.MarkLocked($"range {Vector3.Distance(transform.position, c.transform.position):F0} m ({how})");
            return true;
        }

        void OnGUI()
        {
            if (PlayerView.HideOverlays || Headset.active) return;      // in the headset the brackets and label are world-space (VrHud)
            GUI.BeginGroup(PlayerView.Rect);
            DrawGui();
            GUI.EndGroup();
        }

        void DrawGui()
        {
            if (Event.current.type != EventType.Repaint) return;
            float cx = PlayerView.Width * 0.5f, cy = PlayerView.Height * 0.5f;
            Box(new Rect(cx - 1f, cy - 1f, 2f, 2f), new Color(1f, 1f, 1f, 0.6f));   // aim dot

            float ui = PlayerView.Height / 720f;
            if (labelStyle == null) labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
            labelStyle.fontSize = Mathf.RoundToInt(labelFontSize * ui);

            if (!string.IsNullOrEmpty(say) && Time.unscaledTime < sayUntil)
                GUI.Label(new Rect(cx - 300f * ui, cy + 70f * ui, 600f * ui, 30f * ui), say, new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter });

            // Every contact in the group gets cyan brackets and its own short tag.
            var groupColour = new Color(0.3f, 0.9f, 1f);
            foreach (var c in Selected)
            {
                if (c == null || !Brackets(c, groupColour, ui, out float gx, out float gy, out float gh)) continue;
                string tag = $"#{c.Id:00}" + (c.Call != PlayerCall.None ? " " + c.Call.Label() : "") + (c.Actioned ? " " + ActionTag(c.Action) : "") + (c.Data.iffChecked ? "  " + Iff.Line(c) : "");
                GUI.Label(new Rect(gx + gh + 6f * ui, gy - 20f * ui, 600f * ui, 40f * ui), tag, labelStyle);
            }
            if (Selected.Count > 0)
            {
                GUI.Label(new Rect(cx - 300f * ui, cy + 40f * ui, 600f * ui, 30f * ui),
                    $"GROUP: {Selected.Count} selected.  1-5 and H R J F apply to all.  X clears.", new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter });
                return;
            }

            if (Locked == null) return;
            if (!Brackets(Locked, Color.white, ui, out float x, out float y, out float h)) return;
            GUI.Label(new Rect(x + h + 6f * ui, y - 30f * ui, 900f * ui, 90f * ui), LockLabel(Locked), labelStyle);
        }

        // The text beside the lock brackets (desktop) or in the headset HUD. Id, range and the player's own call and action; never the truth class.
        public string LockLabel(Contact c)
        {
            float range = Vector3.Distance(transform.position, c.transform.position);
            string label = $"#{c.Id:00}  {range:F0} m";
            if (c.Call != PlayerCall.None) label += "  CALL: " + c.Call.Label();
            if (c.CanFireAgain) label += "  MISSED - F TO FIRE AGAIN";
            else if (c.OnHold) label += "  HELD - R / J / F to act";
            else if (c.Actioned) label += "  " + ActionTag(c.Action) + (c.CanChangeAction ? "  (1-5 / H R J F to change)" : "  (1-5 to change the call)");
            // What a sensor would flag: the track has left the pattern it was flying. Not the truth class.
            if (c.AnomalyShown) label += "\n! TRACK LEFT ITS PATROL PATTERN";
            // Dense sky: the filed-route line (what an air picture shows) and the IFF / Remote ID answer.
            string route = Iff.RouteCue(c);
            if (route.Length > 0) label += "\n" + route;
            string iff = Iff.Line(c);
            if (iff.Length > 0) label += "\n" + iff;
            return label;
        }

        // Corner brackets around a contact. False when it is behind the camera.
        bool Brackets(Contact c, Color col, float ui, out float x, out float y, out float h)
        {
            x = y = 0f;
            h = 14f * ui;
            Vector3 sp = cam.WorldToScreenPoint(c.transform.position);
            if (sp.z <= 0f) return false;
            x = sp.x - PlayerView.Rect.x;
            y = PlayerView.Height - sp.y;   // screen pixels to this view's GUI space
            float len = 6f * ui, t = Mathf.Max(1f, 2f * ui);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float px = x + sx * h, py = y + sy * h;
                Box(new Rect(Mathf.Min(px, px - sx * len), py - t * 0.5f, len, t), col);
                Box(new Rect(px - t * 0.5f, Mathf.Min(py, py - sy * len), t, len), col);
            }
            return true;
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
