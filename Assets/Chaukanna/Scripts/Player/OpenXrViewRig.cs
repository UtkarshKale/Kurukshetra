using System.Collections.Generic;
using Chaukanna.Net;
using Chaukanna.Scenario;
using Chaukanna.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;

namespace Chaukanna.Player
{
    // The real headset (OpenXR). Same soldier as the VR simulator: a seated observer with two hands, the sensor tablet in the left, the
    // binoculars raised with both hands, a radial menu on the right. What changes is where the inputs come from (the headset and the two
    // controllers instead of the mouse) and how things reach the eyes (everything is world-space: IMGUI does not render in a headset).
    //
    //   Head          the player camera, under an XrOrigin object, driven by a TrackedPoseDriver (removed again on exit). The origin sits at the
    //                 post; ObserverLook turns it (report slew) and the left thumbstick snap-turns it 30 degrees. No smooth turning, no walking.
    //   Hands         two TrackedPoseDrivers on hand models (the same ones as VR-sim). The right controller's pointer pose is the aim ray.
    //   Right trigger lock the contact nearest the ray (the same test as the desktop click, FOV-scaled through the binoculars).
    //   Left trigger  hold: binoculars. Or both grips held near the face.
    //   Stick click / A  radial menu: right stick picks, right trigger applies, A / B cancel.
    //   X (left)      hold: the tablet comes up bigger to read.   Y (left): the commander's slew to the newest report (grip held: the next one).
    //   Menu          pause: resume / recenter / back to desktop.
    public class OpenXrViewRig : IViewRig
    {
        public string Tier => ViewRigSwitch.TierHeadset;

        public readonly HandGesture Hands = new HandGesture();
        public XrInput Input { get; private set; }
        public HandTablet Tablet { get; private set; }
        public VrHud Hud { get; private set; }
        public XrOptic Optic { get; private set; }
        public VrBrackets Markers { get; private set; }
        public Transform Origin { get; private set; }
        public Transform LeftHand { get; private set; }
        public Transform RightHand { get; private set; }
        public Transform RightAim { get; private set; }
        public bool RadialOpen { get; private set; }
        public bool PauseOpen { get; private set; }
        public float Fps { get; private set; }
        public float EyeVerticalFov { get; private set; }
        public float EyeHorizontalFov { get; private set; }
        public int EyeRows { get; private set; }

        // ViewRigSwitch plumbing.
        public bool ComfortActive;                // the switch asks the question
        public bool ComfortFinished;              // the answer is in ComfortRating / ComfortSickness (0 and -1 = skipped)
        public int ComfortRating = -1, ComfortSickness = -1;
        public ViewMode? WantsMode;               // the pause menu asked to leave the headset

        const float TabletReadScale = 1.8f, HandHideBlend = 0.05f;

        Camera head;
        Binoculars binoculars;
        ContactSensor sensor;
        DecisionLoop decisions;
        ObserverLook look;
        ObserverMove move;
        Transform oldParent;
        float savedNear;
        TrackedPoseDriver headDriver;
        Material glove, sleeve, body, bezel, beam;
        Renderer[] leftRenderers, rightRenderers;
        Transform beamBar;
        Canvas tabletCanvas;
        GameObject markerRoot;
        bool logged, wasComfort;
        int frame, comfortStage;
        SnapTurn comfortStep;
        float lastBlend;
        float detectFovNormal, detectFovOptic;

        static readonly Vector3 LeftRest = new Vector3(-0.2f, -0.35f, 0.3f), RightRest = new Vector3(0.2f, -0.35f, 0.3f);
        static readonly List<string> radialLabels = BuildLabels();

        static List<string> BuildLabels()
        {
            var l = new List<string>();
            foreach (var i in XrMenu.Items) l.Add(i.label);
            return l;
        }

        public void Enter(Camera headCamera)
        {
            head = headCamera;
            binoculars = head.GetComponent<Binoculars>();
            sensor = head.GetComponent<ContactSensor>();
            decisions = head.GetComponent<DecisionLoop>();
            look = head.GetComponent<ObserverLook>();
            move = head.GetComponent<ObserverMove>();
            savedNear = head.nearClipPlane;
            head.nearClipPlane = 0.03f;                         // the hands are closer than the usual near plane

            // The tracked camera cannot be placed directly: it hangs off an origin object that sits at the post.
            oldParent = head.transform.parent;
            Vector3 eye = head.transform.position;
            float yaw = head.transform.eulerAngles.y;
            Origin = new GameObject("XrOrigin").transform;
            Origin.SetPositionAndRotation(AnchorPosition(eye), Quaternion.Euler(0f, yaw, 0f));
            head.transform.SetParent(Origin, false);
            head.transform.localPosition = Vector3.zero;
            head.transform.localRotation = Quaternion.identity;
            if (look != null) look.headsetOrigin = Origin;

            Input = new XrInput();
            Input.Enable();
            headDriver = head.gameObject.AddComponent<TrackedPoseDriver>();
            Drive(headDriver, Input.HeadPosition, Input.HeadRotation);

            // Seated: the tracking origin is the head's start position, recentred.
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var s in inputs)
            {
                s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
                s.TryRecenter();
            }

            glove = Rig.Mat(new Color(0.16f, 0.15f, 0.12f));
            sleeve = Rig.Mat(new Color(0.27f, 0.29f, 0.2f));
            body = Rig.Mat(new Color(0.11f, 0.115f, 0.11f));
            bezel = Rig.Mat(new Color(0.05f, 0.05f, 0.05f));
            beam = Rig.Mat(new Color(1f, 0.35f, 0.25f));

            LeftHand = VrSimViewRig.BuildHand(Origin, "LeftHand", -1f, glove, sleeve);
            RightHand = VrSimViewRig.BuildHand(Origin, "RightHand", 1f, glove, sleeve);
            LeftHand.localPosition = LeftRest;
            RightHand.localPosition = RightRest;
            Drive(LeftHand.gameObject.AddComponent<TrackedPoseDriver>(), Input.LeftPosition, Input.LeftRotation);
            Drive(RightHand.gameObject.AddComponent<TrackedPoseDriver>(), Input.RightPosition, Input.RightRotation);
            leftRenderers = LeftHand.GetComponentsInChildren<Renderer>();
            rightRenderers = RightHand.GetComponentsInChildren<Renderer>();

            // The aim ray follows the pointer pose, which is not the grip pose. A thin red bar shows where it points.
            RightAim = new GameObject("RightAim").transform;
            RightAim.SetParent(Origin, false);
            Drive(RightAim.gameObject.AddComponent<TrackedPoseDriver>(), Input.RightAimPosition, Input.RightAimRotation);
            beamBar = Rig.Part(PrimitiveType.Cube, RightAim, new Vector3(0f, 0f, 0.35f), new Vector3(0.0025f, 0.0025f, 0.7f), beam);

            Tablet = HandTablet.Build(LeftHand, head.GetComponent<SensorController>(), sensor, body, bezel);
            Tablet.transform.localPosition = new Vector3(0.05f, 0.078f, 0.03f);
            tabletCanvas = Tablet.GetComponentInChildren<Canvas>();

            if (binoculars != null)
            {
                binoculars.Zoomed = false;
                binoculars.ExternalBlend = 0f;
            }
            Optic = new XrOptic(head, binoculars != null ? binoculars.zoomedFov : 8f);
            Hud = new VrHud(head, sensor);
            markerRoot = new GameObject("VrMarkers");
            Markers = new VrBrackets(markerRoot.transform);
            Hands.Reset();
            CacheEye();
            wasComfort = false;
            comfortStage = 0;
            logged = false;
            frame = 0;
        }

        public void Exit()
        {
            if (Origin == null && head == null) return;
            if (sensor != null) sensor.Headset = default;
            if (binoculars != null) { binoculars.ExternalBlend = -1f; binoculars.Zoomed = false; }
            if (head != null)
            {
                head.nearClipPlane = savedNear;
                if (headDriver != null) { headDriver.enabled = false; Object.Destroy(headDriver); }
                Vector3 e = head.transform.eulerAngles;
                float yaw = e.y;
                head.transform.SetParent(oldParent, true);
                head.transform.position = move != null && move.HavePost ? move.Post : Origin != null ? Origin.position : head.transform.position;
                if (look != null) { look.headsetOrigin = null; look.SetView(yaw, 0f); }
                else head.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
            if (Hud != null) Hud.Dispose();
            if (Optic != null) Optic.Dispose();
            if (Markers != null) Markers.Dispose();
            if (markerRoot != null) Object.Destroy(markerRoot);
            if (Origin != null) Object.Destroy(Origin.gameObject);     // hands, tablet, aim object
            foreach (var m in new[] { glove, sleeve, body, bezel, beam }) if (m != null) Object.Destroy(m);
            if (Input != null) Input.Dispose();
            Input = null;
            Origin = null;
            Tablet = null;
            Hud = null;
            Optic = null;
            Markers = null;
            RadialOpen = PauseOpen = false;
            head = null;
            XrRuntime.Stop();
        }

        // The origin is where the post is, not where the (tracked, moving) camera happens to be.
        Vector3 AnchorPosition(Vector3 fallback) => move != null && move.HavePost ? move.Post : fallback;

        static void Drive(TrackedPoseDriver d, InputAction position, InputAction rotation)
        {
            d.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            d.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
            d.ignoreTrackingState = true;
            d.positionInput = new InputActionProperty(position);
            d.rotationInput = new InputActionProperty(rotation);
        }

        // One eye's field of view and the pixel rows, for the size rule. Falls back to the camera's own FOV before the headset reports.
        void CacheEye()
        {
            float h = 0f, v = 0f;
            int rows = 0;
            if (XRSettings.isDeviceActive)
            {
                XrDisplay.FieldOfView(head.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left), out h, out v);
                rows = XRSettings.eyeTextureHeight;
            }
            if (v < 20f || v > 170f) { v = Mathf.Clamp(head.fieldOfView, 30f, 120f); h = Camera.VerticalToHorizontalFieldOfView(v, 1f); }
            if (h < 20f || h > 170f) h = Camera.VerticalToHorizontalFieldOfView(v, 1f);
            if (rows <= 0) rows = 1440;
            EyeVerticalFov = v;
            EyeHorizontalFov = h;
            EyeRows = rows;

            // The size rule: the same angular threshold as the calibrated desktop, unless this headset cannot resolve it.
            float minApparent = sensor != null ? sensor.minApparentDegrees : 0.15f, reference = sensor != null ? sensor.referenceFov : 60f;
            float desktopFov = binoculars != null ? binoculars.normalFov : XrDisplay.DesktopRuleFov();
            float zoomed = binoculars != null ? binoculars.zoomedFov : 8f;
            detectFovNormal = XrDisplay.ThresholdFov(desktopFov, v, rows, minApparent, reference);
            detectFovOptic = XrDisplay.ThresholdFov(zoomed, zoomed, XrOptic.TextureSize, minApparent, reference);
        }

        public void Tick(float dt)
        {
            if (Origin == null || head == null || Input == null) return;
            frame++;
            if (frame % 90 == 1) CacheEye();
            if (!logged && (XRSettings.isDeviceActive && frame > 5 || frame > 150))
            {
                logged = true;
                Debug.Log($"[XR] Headset view: per eye {EyeHorizontalFov:F0} x {EyeVerticalFov:F0} deg, {EyeRows} px high = {XrDisplay.ArcminPerPixel(EyeVerticalFov, EyeRows):F2} arcmin/px; size rule fov {detectFovNormal:F1} (desktop reference {(binoculars != null ? binoculars.normalFov : 60f):F1}); optic {XrOptic.TextureSize} px over {(binoculars != null ? binoculars.zoomedFov : 8f):F1} deg = {XrDisplay.ArcminPerPixel(binoculars != null ? binoculars.zoomedFov : 8f, XrOptic.TextureSize):F2} arcmin/px.");
            }
            if (dt > 0f) Fps = Mathf.Lerp(Fps <= 0f ? 1f / dt : Fps, 1f / dt, 0.05f);

            var p = Input;
            p.Poll();
            if (move != null && move.HavePost) Origin.position = move.Post;          // the post may have moved with a new run

            bool blocked = ContactRegistry.InputBlocked;
            bool menuUp = RadialOpen || PauseOpen || ComfortActive;

            // Snap turn on the left stick (comfort: no smooth turning).
            int snap = p.Snap.Step(p.LeftStickValue.x);
            if (snap != 0 && !menuUp && !blocked) Origin.Rotate(0f, snap * SnapTurn.StepDegrees, 0f, Space.World);

            // Hands up: both grips near the face, or the left trigger held. Menus and prompts keep the binoculars down.
            float lDist = Vector3.Distance(LeftHand.position, head.transform.position), rDist = Vector3.Distance(RightHand.position, head.transform.position);
            bool wantBino = !menuUp && !blocked && XrMenu.BinocularsRequested(p.LeftGripP.Held, lDist, p.RightGripP.Held, rDist, p.LeftTriggerP.Held);
            bool read = !menuUp && p.LeftPrimaryP.Held;
            Hands.Tick(wantBino, wantBino, read, dt);
            float blend = Hands.OpticBlend;
            lastBlend = blend;
            if (binoculars != null) { binoculars.Zoomed = Hands.BinocularsUp; binoculars.ExternalBlend = blend; }
            Optic.Tick(blend, dt);
            UpdateSensorView(blend);

            // Hands and tablet are hidden while the optic is up (they are closer than its window), and when a controller is not tracked.
            bool hide = blend > HandHideBlend;
            SetVisible(leftRenderers, !hide && p.Tracked(true));
            SetVisible(rightRenderers, !hide && p.Tracked(false));
            if (tabletCanvas != null) tabletCanvas.enabled = !hide && p.Tracked(true);
            if (beamBar != null) beamBar.gameObject.SetActive(!hide && !menuUp && p.Tracked(false));
            if (Tablet != null)
            {
                Tablet.transform.localScale = Vector3.one * Mathf.Lerp(1f, TabletReadScale, Hands.TabletCloser);
                Tablet.Fps = Fps;
            }

            // Lock: the right trigger, on the contact nearest the ray (or the optic's centre).
            if (p.RightTriggerP.Down && !menuUp && !blocked && sensor != null)
            {
                sensor.ClearSelection();
                sensor.TryLockAtCentre();
            }
            if (p.LeftSecondaryP.Down && !menuUp && !blocked && RunConditions.SeatRole == SeatRole.Commander)
            {
                var board = head.GetComponent<ReportBoard>();
                if (board != null) board.Slew(p.LeftGripP.Held);
            }

            TickMenus(p, menuUp, blocked);
            TickComfort(p);

            Hud.Tick(dt, ComfortActive ? ComfortText() : null);
            DrawMarkers();
        }

        void TickMenus(XrInput p, bool menuUp, bool blocked)
        {
            if (p.MenuP.Down && !ComfortActive)
            {
                PauseOpen = !PauseOpen;
                RadialOpen = false;
                if (!PauseOpen) Hud.HideRing();
            }
            else if (!RadialOpen && !PauseOpen && !ComfortActive && !blocked && (p.RightStickClickP.Down || p.RightPrimaryP.Down))
            {
                RadialOpen = true;
            }
            else if (RadialOpen)
            {
                int picked = RadialMenu.Pick(p.RightStickValue, XrMenu.Items.Length);
                Hud.ShowRing("CALL", radialLabels, picked, "Stick: pick     Trigger: apply     A or B: cancel");
                if (p.RightTriggerP.Down)
                {
                    if (picked >= 0) XrMenu.Apply(picked, decisions, sensor);
                    CloseRadial();
                }
                else if (p.RightPrimaryP.Down || p.RightStickClickP.Down || p.RightSecondaryP.Down || blocked) CloseRadial();
            }

            if (PauseOpen)
            {
                Vector2 stick = p.RightStickValue.sqrMagnitude > p.LeftStickValue.sqrMagnitude ? p.RightStickValue : p.LeftStickValue;
                int picked = RadialMenu.Pick(stick, XrMenu.PauseItems.Length);
                Hud.ShowRing("PAUSE", XrMenu.PauseItems, picked, "Stick: pick     Trigger: apply     Menu: resume");
                if (p.RightTriggerP.Down || p.LeftTriggerP.Down) ApplyPause(picked);
            }
        }

        void CloseRadial()
        {
            RadialOpen = false;
            Hud.HideRing();
        }

        // Pause menu choice. Also called from the self-test.
        public void ApplyPause(int index)
        {
            switch (index)
            {
                case XrMenu.PauseResume: PauseOpen = false; Hud.HideRing(); break;
                case XrMenu.PauseRecenter:
                    Recenter();
                    PauseOpen = false;
                    Hud.HideRing();
                    break;
                case XrMenu.PauseDesktop: PauseOpen = false; Hud.HideRing(); WantsMode = ViewMode.Desktop; break;
            }
        }

        public void Recenter()
        {
            var inputs = new List<XRInputSubsystem>();
            SubsystemManager.GetSubsystems(inputs);
            foreach (var s in inputs) s.TryRecenter();
        }

        // After a headset run: two quick questions with the thumbstick (left / right picks, trigger confirms, B skips).
        void TickComfort(XrInput p)
        {
            if (!ComfortActive) { wasComfort = false; return; }
            if (!wasComfort)
            {
                wasComfort = true;
                comfortStage = 0;
                ComfortRating = 3;
                ComfortSickness = 0;
                RadialOpen = PauseOpen = false;
                Hud.HideRing();
            }
            int step = comfortStep.Step(p.RightStickValue.x);
            if (comfortStage == 0) ComfortRating = Mathf.Clamp(ComfortRating + step, 1, 5);
            else ComfortSickness = Mathf.Clamp(ComfortSickness + step, 0, ViewRigSwitch.SicknessNames.Length - 1);
            if (p.RightSecondaryP.Down)
            {
                ComfortRating = 0;
                ComfortSickness = -1;
                ComfortFinished = true;
            }
            else if (p.RightTriggerP.Down)
            {
                if (comfortStage == 0) comfortStage = 1;
                else ComfortFinished = true;
            }
        }

        string ComfortText()
        {
            if (comfortStage == 0)
            {
                var sb = new System.Text.StringBuilder("VR comfort check\nHow comfortable was that run?\n");
                for (int i = 1; i <= 5; i++) sb.Append(i == ComfortRating ? "[" + i + "]" : " " + i + " ").Append("  ");
                return sb.Append("\n(1 bad, 5 fine)   Stick: change   Trigger: next   B: skip").ToString();
            }
            var t = new System.Text.StringBuilder("Any dizziness or nausea?\n");
            for (int i = 0; i < ViewRigSwitch.SicknessNames.Length; i++) t.Append(i == ComfortSickness ? "[" + ViewRigSwitch.SicknessNames[i] + "]" : " " + ViewRigSwitch.SicknessNames[i] + " ").Append("  ");
            return t.Append("\nStick: change   Trigger: save   B: skip").ToString();
        }

        // The sensor's view while in the headset: the lock ray, the field-of-view box and the two scale FOVs. See ContactSensor.ViewOverride.
        void UpdateSensorView(float blend)
        {
            if (sensor == null) return;
            bool optic = blend > 0.5f;
            var v = new ContactSensor.ViewOverride { active = true };
            if (optic)
            {
                v.viewRotation = Optic.Aim;
                v.halfH = v.halfV = Optic.VerticalFov * 0.5f;
                v.aimOrigin = head.transform.position;
                v.aimDirection = Optic.Aim * Vector3.forward;
            }
            else
            {
                v.viewRotation = head.transform.rotation;
                v.halfH = EyeHorizontalFov * 0.5f;
                v.halfV = EyeVerticalFov * 0.5f;
                if (Input != null && Input.Tracked(false)) { v.aimOrigin = RightAim.position; v.aimDirection = RightAim.forward; }
                else { v.aimOrigin = head.transform.position; v.aimDirection = head.transform.forward; }
            }
            v.detectFov = Mathf.Lerp(detectFovNormal, detectFovOptic, blend);
            v.lockFov = Mathf.Lerp(sensor.referenceFov, binoculars != null ? binoculars.zoomedFov : 8f, blend);
            sensor.Headset = v;
        }

        // Lock brackets (white), the group (cyan) and, for a commander, the reported contacts (amber).
        void DrawMarkers()
        {
            if (Markers == null || sensor == null) return;
            Vector3 eye = head.transform.position;
            Markers.Begin();
            if (sensor.Selected.Count > 0)
            {
                foreach (var c in sensor.Selected) if (c != null) Markers.Show(eye, c.transform.position, new Color(0.3f, 0.9f, 1f));
            }
            else if (sensor.Locked != null) Markers.Show(eye, sensor.Locked.transform.position, Color.white);
            if (RunConditions.SeatRole == SeatRole.Commander)
            {
                int shown = 0;
                for (int i = ReportBoard.Entries.Count - 1; i >= 0 && shown < ReportBoard.MaxLines; i--, shown++)
                {
                    Contact c = null;
                    foreach (var live in ContactRegistry.Live) if (live != null && live.Id == ReportBoard.Entries[i].id) { c = live; break; }
                    if (c != null) Markers.Show(eye, c.transform.position, new Color(1f, 0.75f, 0.1f), VrBrackets.BoxDegrees * 1.5f);
                }
            }
            Markers.End();
        }

        static void SetVisible(Renderer[] renderers, bool on)
        {
            if (renderers == null) return;
            foreach (var r in renderers) if (r != null && r.enabled != on) r.enabled = on;
        }
    }
}
