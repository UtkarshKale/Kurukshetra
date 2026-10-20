using Chaukanna.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Player
{
    // F2 toggles a plain text readout. Shows no truth data.
    [RequireComponent(typeof(Camera), typeof(ContactSensor))]
    public class DebugHud : MonoBehaviour
    {
        public bool show;
        [Tooltip("Text size at 720p; scales with screen height so it stays readable at any Game view resolution.")]
        public int fontSize = 26;

        Camera cam;
        ContactSensor sensor;
        GUIStyle style;

        void Awake()
        {
            cam = GetComponent<Camera>();
            sensor = GetComponent<ContactSensor>();
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame && !ContactRegistry.InputBlocked) show = !show;
        }

        void OnGUI()
        {
            if (PlayerView.HideOverlays) return;
            GUI.BeginGroup(PlayerView.Rect);
            DrawGui();
            GUI.EndGroup();
        }

        void DrawGui()
        {
            if (!show || Event.current.type != EventType.Repaint) return;
            float ui = PlayerView.Height / 720f;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false };
            style.fontSize = Mathf.RoundToInt(fontSize * ui);
            style.normal.textColor = Color.white;

            var c = sensor.Locked;
            string locked = c != null ? $"#{c.Id:00}  call {c.Call.Label()}  action {(c.Actioned ? c.Action.ToString() : "none")}" : "none";
            var text = new GUIContent($"FOV {cam.fieldOfView:F1}\nDetected {sensor.DetectedTotal} (in view {sensor.VisibleCount})\nLocked {locked}");
            Vector2 size = style.CalcSize(text);
            float pad = 8f * ui;
            var box = new Rect(pad, pad, size.x + pad * 2f, size.y + pad * 2f);

            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(box.x + pad, box.y + pad, size.x, size.y), text, style);
        }
    }
}
