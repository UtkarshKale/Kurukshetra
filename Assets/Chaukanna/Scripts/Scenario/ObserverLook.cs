using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Scenario
{
    // Fixed observation post: mouse-look only. Click to capture the mouse, Esc to release.
    public class ObserverLook : MonoBehaviour
    {
        public float sensitivity = 0.08f;   // degrees per pixel of mouse movement, at referenceFov
        public float referenceFov = 60f;    // sensitivity scales with FOV so binoculars stay controllable
        public float minPitch = -20f;
        public float maxPitch = 85f;
        [Tooltip("Most the view may turn per second (true-size display caps it at 60). 0 = no cap.")]
        public float maxPanDegreesPerSecond;

        float yaw, pitch;
        Camera cam;
        Chaukanna.Player.Binoculars binoc;

        // Headset: the head is tracked, so the mouse does not look. The body (this origin, the parent of the camera) turns instead:
        // the report slew and the snap turn both rotate it. Set by the headset rig, null otherwise.
        [System.NonSerialized] public Transform headsetOrigin;

        // Puts the mouse look back where the head ended up when the headset rig lets go of the camera.
        public void SetView(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
            transform.rotation = Quaternion.Euler(-pitch, yaw, 0f);
        }

        void Start()
        {
            cam = GetComponent<Camera>();
            // Walking is a separate component, added here so existing scenes get it without being edited.
            if (GetComponent<Chaukanna.Player.ObserverMove>() == null) gameObject.AddComponent<Chaukanna.Player.ObserverMove>();
            if (GetComponent<Chaukanna.Player.ViewRigSwitch>() == null) gameObject.AddComponent<Chaukanna.Player.ViewRigSwitch>();
            if (GetComponent<Chaukanna.Audio.SoundEngine>() == null) gameObject.AddComponent<Chaukanna.Audio.SoundEngine>();
            if (GetComponent<Chaukanna.Player.ContactSensor>() != null && GetComponent<Chaukanna.Player.LassoSelect>() == null) gameObject.AddComponent<Chaukanna.Player.LassoSelect>();
            Chaukanna.Player.DisplayCalibration.ApplyTo(cam);
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = -Mathf.DeltaAngle(0f, e.x);
        }

        // Turns the view to a point over `seconds` (the commander's T key). Works without the mouse and in VR-sim too, which is
        // looked through this same camera. The mouse takes over again when it ends.
        public bool Slewing => slewing;
        bool slewing;
        float slewT, slewDur, fromYaw, fromPitch, toYaw, toPitch;

        public void SlewTo(Vector3 worldPoint, float seconds = 0.3f)
        {
            Vector3 d = worldPoint - transform.position;
            if (d.sqrMagnitude < 0.01f) return;
            Vector3 e = transform.eulerAngles;
            fromYaw = e.y;
            fromPitch = -Mathf.DeltaAngle(0f, e.x);
            toYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            toPitch = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg, minPitch, maxPitch);
            slewT = 0f;
            slewDur = Mathf.Max(0.01f, seconds);
            slewing = true;
        }

        void Update()
        {
            if (headsetOrigin != null)
            {
                if (slewing)
                {
                    slewT += Time.deltaTime;
                    float kh = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(slewT / slewDur));
                    float want = Mathf.LerpAngle(fromYaw, toYaw, kh);
                    headsetOrigin.Rotate(0f, Mathf.DeltaAngle(transform.eulerAngles.y, want), 0f, Space.World);
                    if (slewT >= slewDur) slewing = false;
                }
                return;
            }
            if (slewing)
            {
                slewT += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(slewT / slewDur));
                yaw = Mathf.LerpAngle(fromYaw, toYaw, k);
                pitch = Mathf.Lerp(fromPitch, toPitch, k);
                transform.rotation = Quaternion.Euler(-pitch, yaw, 0f);
                if (slewT >= slewDur) slewing = false;
                return;
            }
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || ContactRegistry.InputBlocked) return;
            if (Chaukanna.Player.LassoSelect.Holding) return;          // Tab held: the mouse draws the lasso, it does not look

            // Only a click inside the player's view captures the mouse (matters in split-screen).
            if (mouse.leftButton.wasPressedThisFrame && PlayerView.Contains(mouse.position.ReadValue()) && !Chaukanna.Trainer.ScenarioEditor3D.PointerOverTestBar(mouse.position.ReadValue())) Cursor.lockState = CursorLockMode.Locked;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (Chaukanna.Player.ViewRigSwitch.LookFrozen) return;     // VR radial menu open: the mouse is picking an item

            if (binoc == null && cam != null) binoc = cam.GetComponent<Chaukanna.Player.Binoculars>();
            Vector2 delta = ControlsPrefs.Look(mouse.delta.ReadValue(), binoc != null && binoc.ZoomBlend > 0.5f);
            float s = sensitivity * (cam != null ? cam.fieldOfView / referenceFov : 1f);
            if (maxPanDegreesPerSecond > 0f && Time.deltaTime > 0f)
            {
                float most = maxPanDegreesPerSecond * Time.deltaTime / Mathf.Max(1e-5f, s);
                delta = Vector2.ClampMagnitude(delta, most);
            }
            yaw += delta.x * s;
            pitch = Mathf.Clamp(pitch + delta.y * s, minPitch, maxPitch);
            transform.rotation = Quaternion.Euler(-pitch, yaw, 0f);
        }
    }
}
