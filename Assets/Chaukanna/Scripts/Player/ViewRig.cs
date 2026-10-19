using Chaukanna.Scenario;
using Chaukanna.Scoring;
using Chaukanna.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Player
{
    // How the soldier sees the field. Desktop = the screen view as before. VR = a seated soldier with hands, a sensor
    // tablet in the left hand and binoculars raised with both hands. VrSimViewRig is a desktop simulator driven by the mouse;
    // OpenXrViewRig is the real headset. Both are implementations of this same interface, so nothing else has to change.
    public interface IViewRig
    {
        string Tier { get; }                 // recorded with every session: runs on different tiers are never pooled
        void Enter(Camera head);
        void Exit();
        void Tick(float dt);
    }

    public class DesktopViewRig : IViewRig
    {
        public string Tier => ViewRigSwitch.TierDesktop;
        public void Enter(Camera head) { }
        public void Exit() { }
        public void Tick(float dt) { }
    }

    // VR simulator. Mouse = head. Hold B, or hold both mouse buttons = both hands up = binoculars. Right button alone =
    // right hand up. Q = bring the tablet closer to read. Hold E (or the middle button) = radial menu on the right hand.
    public class VrSimViewRig : IViewRig
    {
        public string Tier => ViewRigSwitch.TierVrSim;
        public readonly HandGesture Hands = new HandGesture();
        public HandTablet Tablet { get; private set; }

        Camera head;
        Binoculars binoculars;
        Transform rig, leftHand, rightHand;
        Renderer[] leftRenderers, rightRenderers;
        float savedNear, savedZoomSpeed;
        Material glove, sleeve, body, bezel;

        // Hand poses in head space (metres). Rest = low in view; Face = up at the eyes holding the optic.
        static readonly Vector3 LeftRest = new Vector3(-0.15f, -0.17f, 0.36f), LeftRead = new Vector3(-0.07f, -0.09f, 0.29f), LeftFace = new Vector3(-0.045f, -0.03f, 0.1f);
        static readonly Vector3 RightRest = new Vector3(0.19f, -0.25f, 0.34f), RightFace = new Vector3(0.045f, -0.03f, 0.1f);
        static readonly Quaternion LeftRestRot = Quaternion.Euler(-28f, 18f, 8f), LeftReadRot = Quaternion.Euler(-12f, 8f, 2f), FaceRot = Quaternion.Euler(-5f, 0f, 0f);
        static readonly Quaternion RightRestRot = Quaternion.Euler(-35f, -15f, -20f);

        public void Enter(Camera headCamera)
        {
            head = headCamera;
            binoculars = head.GetComponent<Binoculars>();
            savedNear = head.nearClipPlane;
            head.nearClipPlane = 0.03f;                       // the hands are closer than the usual near plane
            if (binoculars != null)
            {
                savedZoomSpeed = binoculars.zoomSpeed;
                binoculars.zoomSpeed = 20f;                   // optic in over ~150 ms (comfort)
                binoculars.Zoomed = false;
            }
            Hands.Reset();

            glove = Rig.Mat(new Color(0.16f, 0.15f, 0.12f));
            sleeve = Rig.Mat(new Color(0.27f, 0.29f, 0.2f));
            body = Rig.Mat(new Color(0.11f, 0.115f, 0.11f));
            bezel = Rig.Mat(new Color(0.05f, 0.05f, 0.05f));

            rig = new GameObject("VrSimRig").transform;
            rig.SetParent(head.transform, false);
            leftHand = BuildHand(rig, "LeftHand", -1f, glove, sleeve);
            rightHand = BuildHand(rig, "RightHand", 1f, glove, sleeve);
            Tablet = HandTablet.Build(leftHand, head.GetComponent<SensorController>(), head.GetComponent<ContactSensor>(), body, bezel);
            Tablet.transform.localPosition = new Vector3(0.05f, 0.078f, 0.03f);       // held in the palm, screen to the eye
            leftRenderers = leftHand.GetComponentsInChildren<Renderer>();
            rightRenderers = rightHand.GetComponentsInChildren<Renderer>();
            Pose(0f);
        }

        public void Exit()
        {
            if (rig != null) Object.Destroy(rig.gameObject);
            foreach (var m in new[] { glove, sleeve, body, bezel }) if (m != null) Object.Destroy(m);
            if (head != null) head.nearClipPlane = savedNear;
            if (binoculars != null) { binoculars.zoomSpeed = savedZoomSpeed; binoculars.Zoomed = false; }
            rig = null;
            Tablet = null;
        }

        // Inputs come from ViewRigSwitch so the same rig can later read controller poses instead.
        public bool LeftUpInput, RightUpInput, TabletInput;

        public void Tick(float dt)
        {
            if (rig == null) return;
            Hands.Tick(LeftUpInput, RightUpInput, TabletInput, dt);
            if (binoculars != null) binoculars.Zoomed = Hands.BinocularsUp;
            Pose(dt);
        }

        void Pose(float dt)
        {
            // Left: rest > (Q) read > face. Right: rest > face. Hidden once they are at the eyes behind the optic.
            Vector3 left = Vector3.Lerp(Vector3.Lerp(LeftRest, LeftRead, Hands.TabletCloser), LeftFace, Hands.Left);
            Quaternion leftRot = Quaternion.Slerp(Quaternion.Slerp(LeftRestRot, LeftReadRot, Hands.TabletCloser), FaceRot, Hands.Left);
            leftHand.localPosition = left + Sway(0.3f);
            leftHand.localRotation = leftRot;
            rightHand.localPosition = Vector3.Lerp(RightRest, RightFace, Hands.Right) + Sway(1.7f);
            rightHand.localRotation = Quaternion.Slerp(RightRestRot, FaceRot, Hands.Right);
            foreach (var r in leftRenderers) if (r != null) r.enabled = Hands.Left < 0.85f;
            foreach (var r in rightRenderers) if (r != null) r.enabled = Hands.Right < 0.85f;
            var canvas = Tablet != null ? Tablet.GetComponentInChildren<Canvas>() : null;
            if (canvas != null) canvas.enabled = Hands.Left < 0.85f;
        }

        // A breath of movement so the hands do not look glued to the screen.
        static Vector3 Sway(float phase)
        {
            float t = Time.time;
            return new Vector3(Mathf.Sin(t * 0.9f + phase) * 0.0015f, Mathf.Sin(t * 1.3f + phase) * 0.002f, 0f);
        }

        // Gloved hand and sleeve from primitives. side -1 = left. Shared with the headset rig so both VR modes look the same.
        public static Transform BuildHand(Transform parent, string name, float side, Material glove, Material sleeve)
        {
            var hand = new GameObject(name).transform;
            hand.SetParent(parent, false);
            Rig.Part(PrimitiveType.Cube, hand, new Vector3(0f, 0f, 0f), new Vector3(0.085f, 0.03f, 0.1f), glove);                        // palm
            for (int i = 0; i < 4; i++)
                Rig.Part(PrimitiveType.Cube, hand, new Vector3(-0.03f + i * 0.02f, 0.012f, 0.062f), new Vector3(0.017f, 0.022f, 0.035f), glove);   // fingers curled forward
            Rig.Part(PrimitiveType.Cube, hand, new Vector3(-side * 0.05f, 0.012f, 0.02f), new Vector3(0.022f, 0.022f, 0.05f), glove);    // thumb
            var forearm = Rig.Part(PrimitiveType.Capsule, hand, new Vector3(side * 0.02f, -0.02f, -0.15f), new Vector3(0.075f, 0.13f, 0.075f), sleeve);
            forearm.localRotation = Quaternion.Euler(90f, 0f, 0f);
            Rig.Part(PrimitiveType.Cube, hand, new Vector3(0f, -0.004f, -0.05f), new Vector3(0.08f, 0.04f, 0.03f), sleeve);               // cuff
            return hand;
        }
    }

    // Small helpers for building the rig from primitives without colliders (nothing here may block line of sight).
    public static class Rig
    {
        public static Transform Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        public static Material Mat(Color c)
        {
            var shader = Chaukanna.RuntimeMaterials.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Chaukanna.RuntimeMaterials.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) shader = Chaukanna.RuntimeMaterials.Find("Standard");
            var m = new Material(shader) { color = c };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            return m;
        }
    }

    public enum ViewMode { Desktop = 0, VrSim = 1, Headset = 2 }

    // One-click switch between the desktop view, the VR simulator and the real headset. Sits on the observer camera (added automatically).
    // F7 cycles Desktop > VR-sim > Headset > Desktop (the headset step only when an OpenXR runtime starts); the trainer's VIEW setting
    // applies at the start of each run.
    public class ViewRigSwitch : MonoBehaviour
    {
        public const string TierDesktop = "desktop", TierVrSim = "vr-sim", TierHeadset = "vr-openxr", TierMixed = "mixed";

        public static ViewMode Mode { get; private set; }
        public static bool VrActive => Mode != ViewMode.Desktop;      // any VR: the simulator or the headset
        public static bool HeadsetActive => Mode == ViewMode.Headset;
        public static string LastRefusal { get; private set; } = "";  // why the headset would not start, last time it was asked
        public static bool LookFrozen { get; private set; }       // radial menu open: the mouse picks, it does not look
        public IViewRig Current { get; private set; } = new DesktopViewRig();
        public VrSimViewRig Vr => Current as VrSimViewRig;
        public OpenXrViewRig Headset => Current as OpenXrViewRig;

        // Which tier a run counts as: what it started on, or "mixed" if the view was switched during it.
        public static string TierFor(ViewMode atStart, bool switchedDuringRun)
        {
            if (switchedDuringRun) return TierMixed;
            return atStart == ViewMode.Headset ? TierHeadset : atStart == ViewMode.VrSim ? TierVrSim : TierDesktop;
        }

        public static string TierFor(bool vrAtStart, bool switchedDuringRun) => TierFor(vrAtStart ? ViewMode.VrSim : ViewMode.Desktop, switchedDuringRun);

        Camera head;
        ScenarioSpawner spawner;
        DecisionLoop decisions;
        ContactSensor contacts;
        int run = -1;
        ViewMode modeAtStart;
        bool switched;
        ViewMode? pendingMode;                 // asked for by the headset pause menu, applied after its Tick

        // Radial menu state.
        bool radialOpen;
        Vector2 radialAim;
        bool radialActions;
        static readonly string[] ClassItems = { "Hostile quad", "Hostile fixed-wing", "Own ISR drone", "Civilian drone", "Bird" };
        static readonly PlayerCall[] ClassCalls = { PlayerCall.HostileQuad, PlayerCall.HostileFixedWing, PlayerCall.OwnIsrDrone, PlayerCall.CivilianDrone, PlayerCall.Bird };
        static readonly string[] ActionItems = { "Hold", "Report", "Jam", "Engage" };
        static readonly PlayerAction[] Actions = { PlayerAction.Hold, PlayerAction.Report, PlayerAction.Jam, PlayerAction.Engage };
        GUIStyle style, small;

        // After a VR run: two quick questions, or skip.
        bool askComfort;
        int comfortAnswer = -1, sicknessAnswer = -1;
        ScoreKeeper keeper;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Mode = ViewMode.Desktop;
            LastRefusal = "";
            LookFrozen = false;
            RunConditions.DisplayTier = TierDesktop;
        }

        void Awake()
        {
            head = GetComponent<Camera>();
            decisions = GetComponent<DecisionLoop>();
            contacts = GetComponent<ContactSensor>();
        }

        public void Toggle() => Set(!VrActive);

        public void Set(bool vr) => SetMode(vr ? ViewMode.VrSim : ViewMode.Desktop, out _);

        // F7: Desktop > VR-sim > Headset > Desktop. The headset is only offered when a runtime starts; when it does not, the reason is
        // shown and the cycle carries on to the desktop.
        public ViewMode NextMode()
        {
            return Mode == ViewMode.Desktop ? ViewMode.VrSim : Mode == ViewMode.VrSim ? ViewMode.Headset : ViewMode.Desktop;
        }

        public void Cycle()
        {
            var next = NextMode();
            if (next == ViewMode.Headset && !SetMode(ViewMode.Headset, out _)) next = ViewMode.Desktop;
            if (next != ViewMode.Headset) SetMode(next, out _);
        }

        // Changes the view. Returns false (and stays on the rig it had) when the headset cannot start; reason says why.
        public bool SetMode(ViewMode mode, out string reason)
        {
            reason = "";
            if (head == null) head = GetComponent<Camera>();
            if (contacts == null) contacts = GetComponent<ContactSensor>();
            if (mode == Mode && (mode == ViewMode.Desktop ? Current is DesktopViewRig : mode == ViewMode.VrSim ? Current is VrSimViewRig : Current is OpenXrViewRig)) return true;

            if (mode == ViewMode.Headset)
            {
                if (!XrRuntime.TryStart(out reason))
                {
                    LastRefusal = reason;
                    ContactRegistry.Note("View: headset unavailable: " + reason);
                    if (contacts != null) contacts.Say("HEADSET: " + reason, 7f);
                    return false;
                }
            }
            Current.Exit();
            Current = mode == ViewMode.Headset ? (IViewRig)new OpenXrViewRig() : mode == ViewMode.VrSim ? new VrSimViewRig() : new DesktopViewRig();
            Mode = mode;
            LookFrozen = false;
            radialOpen = false;
            Current.Enter(head);
            if (spawner != null && !spawner.Waiting && !spawner.Finished && mode != modeAtStart) switched = true;
            RunConditions.DisplayTier = TierFor(modeAtStart, switched);
            ContactRegistry.Note("View: " + (mode == ViewMode.Headset ? "headset (OpenXR " + XrRuntime.RuntimeName + ")" : mode == ViewMode.VrSim ? "VR mode (simulator)" : "desktop"));
            return true;
        }

        void OnDestroy()
        {
            Current.Exit();
            Mode = ViewMode.Desktop;
            LookFrozen = false;
        }

        void Update()
        {
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (keeper == null) keeper = FindAnyObjectByType<ScoreKeeper>();
            if (spawner != null && spawner.RunNumber != run)
            {
                run = spawner.RunNumber;
                // The trainer's setting decides the view a run starts in. No headset: the simulator is the nearest thing.
                if (spawner.TrainerConfig != null)
                {
                    var want = (ViewMode)spawner.TrainerConfig.EffectiveViewMode;
                    if (!SetMode(want, out _) && want == ViewMode.Headset) SetMode(ViewMode.VrSim, out _);
                }
                modeAtStart = Mode;
                switched = false;
                askComfort = false;
                comfortAnswer = sicknessAnswer = -1;
                RunConditions.DisplayTier = TierFor(modeAtStart, false);
            }
            if (spawner != null && spawner.Finished && keeper != null && keeper.Last != null && modeAtStart != ViewMode.Desktop && !askComfort && comfortAnswer == -1 && keeper.Last.comfortRating < 0)
            {
                askComfort = true;
                Cursor.lockState = CursorLockMode.None;
            }

            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb != null && kb.f7Key.wasPressedThisFrame && !ContactRegistry.InputBlocked) Cycle();

            var headsetRig = Headset;
            if (headsetRig != null)
            {
                headsetRig.ComfortActive = askComfort;
                if (headsetRig.ComfortFinished)
                {
                    headsetRig.ComfortFinished = false;
                    comfortAnswer = headsetRig.ComfortRating;
                    sicknessAnswer = headsetRig.ComfortSickness;
                    FinishComfort(comfortAnswer > 0 && sicknessAnswer >= 0);
                }
                if (headsetRig.WantsMode.HasValue) { pendingMode = headsetRig.WantsMode; headsetRig.WantsMode = null; }
                Current.Tick(Time.deltaTime);
                if (!XrRuntime.Alive) pendingMode = ViewMode.Desktop;      // the runtime went away under us
                if (pendingMode.HasValue)
                {
                    var m = pendingMode.Value;
                    pendingMode = null;
                    SetMode(m, out _);
                }
                return;
            }

            var vrRig = Vr;
            if (vrRig == null) return;
            bool blocked = ContactRegistry.InputBlocked;
            bool captured = Cursor.lockState == CursorLockMode.Locked;

            // Radial menu on the right hand.
            bool menuHeld = !blocked && captured && ((kb != null && kb.eKey.isPressed) || (mouse != null && mouse.middleButton.isPressed));
            if (menuHeld && !radialOpen)
            {
                radialOpen = true;
                radialAim = Vector2.zero;
                // Classes first if anything targeted has no call yet; otherwise the actions. Right-click swaps.
                radialActions = true;
                foreach (var c in contacts.Targets) if (c != null && c.Call == PlayerCall.None) radialActions = false;
            }
            if (radialOpen)
            {
                if (mouse != null)
                {
                    radialAim += mouse.delta.ReadValue() / 120f;
                    if (radialAim.magnitude > 1f) radialAim.Normalize();
                    if (mouse.rightButton.wasPressedThisFrame) { radialActions = !radialActions; radialAim = Vector2.zero; }
                }
                if (!menuHeld)
                {
                    radialOpen = false;
                    ApplyRadial(RadialMenu.Pick(radialAim, radialActions ? Actions.Length : ClassCalls.Length));
                }
            }
            LookFrozen = radialOpen;

            // Hands. Both mouse buttons or B: both up (binoculars). Right button alone: right hand only. Q: tablet closer.
            bool both = !blocked && !radialOpen && ((kb != null && kb.bKey.isPressed) || (mouse != null && mouse.leftButton.isPressed && mouse.rightButton.isPressed && captured));
            vrRig.LeftUpInput = both;
            vrRig.RightUpInput = both || (!blocked && !radialOpen && captured && mouse != null && mouse.rightButton.isPressed);
            vrRig.TabletInput = !blocked && kb != null && kb.qKey.isPressed;
            Current.Tick(Time.deltaTime);
        }

        void ApplyRadial(int index)
        {
            if (index < 0 || decisions == null) return;
            if (radialActions) decisions.Act(Actions[index]);
            else decisions.Classify(ClassCalls[index]);
        }

        void OnGUI()
        {
            if (PlayerView.HideOverlays) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
                small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true };
            }
            float ui = PlayerView.Height / 720f;
            style.fontSize = Mathf.RoundToInt(17f * ui);
            small.fontSize = Mathf.RoundToInt(14f * ui);
            GUI.BeginGroup(PlayerView.Rect);
            if (Vr != null && radialOpen) DrawRadial(ui);
            if (askComfort && !HeadsetActive) DrawComfort(ui);
            GUI.EndGroup();
        }

        void DrawRadial(float ui)
        {
            string[] items = radialActions ? ActionItems : ClassItems;
            int picked = RadialMenu.Pick(radialAim, items.Length);
            Vector2 centre = new Vector2(PlayerView.Width * 0.62f, PlayerView.Height * 0.58f);     // off to the right hand's side
            float radius = 120f * ui;
            if (Event.current.type == EventType.Repaint)
            {
                Fill(new Rect(centre.x - 34f * ui, centre.y - 34f * ui, 68f * ui, 68f * ui), new Color(0f, 0f, 0f, 0.6f));
                Fill(new Rect(centre.x + radialAim.x * 30f * ui - 4f * ui, centre.y - radialAim.y * 30f * ui - 4f * ui, 8f * ui, 8f * ui), Color.white);
            }
            GUI.Label(new Rect(centre.x - 34f * ui, centre.y - 34f * ui, 68f * ui, 68f * ui), radialActions ? "ACT" : "CALL", style);
            for (int i = 0; i < items.Length; i++)
            {
                Vector2 d = RadialMenu.Direction(i, items.Length);
                var box = new Rect(centre.x + d.x * radius - 80f * ui, centre.y - d.y * radius - 20f * ui, 160f * ui, 40f * ui);
                if (Event.current.type == EventType.Repaint) Fill(box, i == picked ? new Color(0.2f, 0.6f, 1f, 0.9f) : new Color(0f, 0f, 0f, 0.65f));
                GUI.Label(box, items[i], style);
            }
            GUI.Label(new Rect(centre.x - 200f * ui, centre.y + radius + 30f * ui, 400f * ui, 40f * ui), "Move the mouse to pick, let go of E to apply.  Right-click: " + (radialActions ? "calls" : "actions"), small);
        }

        void DrawComfort(float ui)
        {
            var box = new Rect(PlayerView.Width * 0.5f - 260f * ui, PlayerView.Height * 0.66f, 520f * ui, 150f * ui);
            if (Event.current.type == EventType.Repaint) Fill(box, new Color(0f, 0f, 0f, 0.8f));
            GUI.Label(new Rect(box.x + 12f * ui, box.y + 8f * ui, box.width - 24f * ui, 24f * ui), "VR comfort check:  how comfortable was that run?  (1 = bad, 5 = fine)", small);
            for (int i = 0; i < 5; i++)
                if (GUI.Toggle(new Rect(box.x + 12f * ui + i * 56f * ui, box.y + 34f * ui, 50f * ui, 28f * ui), comfortAnswer == i + 1, (i + 1).ToString(), GUI.skin.button)) comfortAnswer = i + 1;
            GUI.Label(new Rect(box.x + 12f * ui, box.y + 68f * ui, box.width - 24f * ui, 24f * ui), "Any dizziness or nausea?", small);
            string[] sick = { "None", "Mild", "Strong" };
            for (int i = 0; i < 3; i++)
                if (GUI.Toggle(new Rect(box.x + 12f * ui + i * 96f * ui, box.y + 94f * ui, 90f * ui, 28f * ui), sicknessAnswer == i, sick[i], GUI.skin.button)) sicknessAnswer = i;
            bool done = comfortAnswer > 0 && sicknessAnswer >= 0;
            if (GUI.Button(new Rect(box.xMax - 110f * ui, box.y + 94f * ui, 98f * ui, 28f * ui), done ? "Save" : "Skip")) FinishComfort(done);
        }

        public static readonly string[] SicknessNames = { "None", "Mild", "Strong" };

        // Save the two answers on the finished run (or mark them skipped) and stop asking.
        void FinishComfort(bool done)
        {
            askComfort = false;
            if (keeper != null && keeper.Last != null)
            {
                keeper.Last.comfortRating = done ? comfortAnswer : 0;     // 0 = skipped
                keeper.Last.comfortSickness = done ? SicknessNames[Mathf.Clamp(sicknessAnswer, 0, SicknessNames.Length - 1)] : "skipped";
                keeper.SaveAgain();
            }
            if (comfortAnswer < 0) comfortAnswer = 0;
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
