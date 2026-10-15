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

        void Start()
        {
            cam = GetComponent<Camera>();
            // Walking is a separate component, added here so existing scenes get it without being edited.
            if (GetComponent<Chaukanna.Player.ObserverMove>() == null) gameObject.AddComponent<Chaukanna.Player.ObserverMove>();
            if (GetComponent<Chaukanna.Player.ViewRigSwitch>() == null) gameObject.AddComponent<Chaukanna.Player.ViewRigSwitch>();
            if (GetComponent<Chaukanna.Player.ContactSensor>() != null && GetComponent<Chaukanna.Player.LassoSelect>() == null) gameObject.AddComponent<Chaukanna.Player.LassoSelect>();
            Chaukanna.Player.DisplayCalibration.ApplyTo(cam);
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = -Mathf.DeltaAngle(0f, e.x);
        }

        void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || ContactRegistry.InputBlocked) return;
            if (Chaukanna.Player.LassoSelect.Holding) return;          // Tab held: the mouse draws the lasso, it does not look

            // Only a click inside the player's view captures the mouse (matters in split-screen).
            if (mouse.leftButton.wasPressedThisFrame && PlayerView.Contains(mouse.position.ReadValue())) Cursor.lockState = CursorLockMode.Locked;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (Chaukanna.Player.ViewRigSwitch.LookFrozen) return;     // VR radial menu open: the mouse is picking an item

            Vector2 delta = mouse.delta.ReadValue();
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
