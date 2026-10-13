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

        float yaw, pitch;
        Camera cam;

        void Start()
        {
            cam = GetComponent<Camera>();
            Vector3 e = transform.eulerAngles;
            yaw = e.y;
            pitch = -Mathf.DeltaAngle(0f, e.x);
        }

        void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            if (mouse == null || ContactRegistry.InputBlocked) return;

            // Only a click inside the player's view captures the mouse (matters in split-screen).
            if (mouse.leftButton.wasPressedThisFrame && PlayerView.Contains(mouse.position.ReadValue())) Cursor.lockState = CursorLockMode.Locked;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            if (Cursor.lockState != CursorLockMode.Locked) return;

            Vector2 delta = mouse.delta.ReadValue();
            float s = sensitivity * (cam != null ? cam.fieldOfView / referenceFov : 1f);
            yaw += delta.x * s;
            pitch = Mathf.Clamp(pitch + delta.y * s, minPitch, maxPitch);
            transform.rotation = Quaternion.Euler(-pitch, yaw, 0f);
        }
    }
}
