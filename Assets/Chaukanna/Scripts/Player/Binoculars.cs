using Chaukanna.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Player
{
    // Right-click toggles binoculars: FOV lerps between normal and zoomed. When zoomed, everything outside
    // two overlapping lens circles is black; the view inside stays clear, with a thin reticle on top.
    [RequireComponent(typeof(Camera))]
    public class Binoculars : MonoBehaviour
    {
        [Header("Zoom")]
        public float normalFov = 60f;
        public float zoomedFov = 8f;
        [Tooltip("Higher is snappier.")]
        public float zoomSpeed = 10f;

        [Header("Lens mask (fractions of screen height)")]
        [Tooltip("Diameter of each lens circle.")]
        [Range(0.5f, 1f)] public float lensDiameter = 0.9f;
        [Tooltip("Distance between the two lens centres. Smaller = more overlap.")]
        [Range(0f, 1f)] public float lensSeparation = 0.62f;
        [Tooltip("Width of the soft rim at the lens edge.")]
        [Range(0.002f, 0.05f)] public float rimSoftness = 0.012f;

        [Header("Reticle (fractions of screen height)")]
        public Color reticleColour = new Color(0f, 0f, 0f, 0.8f);
        public float crosshairSize = 0.02f;
        public float lineThickness = 0.002f;
        [Tooltip("Half-size of the corner-bracket frame around the centre.")]
        public float frameHalfSize = 0.16f;
        public int scaleTicks = 11;

        public bool Zoomed { get; set; }

        Camera cam;
        Texture2D mask;
        float maskDiameter, maskSeparation, maskRim;

        // 0 = normal, 1 = fully zoomed.
        float Blend => Mathf.InverseLerp(normalFov, zoomedFov, cam.fieldOfView);

        void Awake()
        {
            cam = GetComponent<Camera>();
            cam.fieldOfView = normalFov;
        }

        void Update()
        {
            var mouse = Mouse.current;
            if (mouse != null && mouse.rightButton.wasPressedThisFrame && !Chaukanna.Scenario.ContactRegistry.InputBlocked) Zoomed = !Zoomed;

            float target = Zoomed ? zoomedFov : normalFov;
            float fov = Mathf.Lerp(cam.fieldOfView, target, 1f - Mathf.Exp(-zoomSpeed * Time.unscaledDeltaTime));
            cam.fieldOfView = Mathf.Abs(fov - target) < 0.01f ? target : fov;
        }

        void OnGUI()
        {
            GUI.BeginGroup(PlayerView.Rect);
            DrawGui();
            GUI.EndGroup();
        }

        void DrawGui()
        {
            GUI.depth = 10;   // behind the lock marker and HUD
            float blend = Blend;
            if (blend <= 0.01f || Event.current.type != EventType.Repaint) return;
            if (mask == null || maskDiameter != lensDiameter || maskSeparation != lensSeparation || maskRim != rimSoftness) RebuildMask();

            // The mask texture covers a 2:1 rect of full screen height, centred; bars fill whatever is left.
            // Lens unit: the view height, shrunk if the view is too narrow to hold both lenses.
            float h = Mathf.Min(PlayerView.Height, PlayerView.Width / (lensSeparation + lensDiameter + 0.04f)), w = h * 2f;
            float x = (PlayerView.Width - w) * 0.5f, top = (PlayerView.Height - h) * 0.5f;
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, blend);
            GUI.DrawTexture(new Rect(x, top, w, h), mask);
            if (x > 0f)
            {
                GUI.DrawTexture(new Rect(0f, top, x, h), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(x + w, top, x, h), Texture2D.whiteTexture);
            }
            if (top > 0f)
            {
                GUI.DrawTexture(new Rect(0f, 0f, PlayerView.Width, top), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(0f, top + h, PlayerView.Width, top), Texture2D.whiteTexture);
            }

            GUI.color = new Color(reticleColour.r, reticleColour.g, reticleColour.b, reticleColour.a * blend);
            DrawReticle(PlayerView.Width * 0.5f, PlayerView.Height * 0.5f, h);
            GUI.color = old;
        }

        void DrawReticle(float cx, float cy, float h)
        {
            float t = Mathf.Max(1f, lineThickness * h);
            float c = crosshairSize * h, gap = c * 0.3f;

            // Centre crosshair with an open middle so it never hides a small contact.
            Line(cx - c, cy, c - gap, t, true); Line(cx + gap, cy, c - gap, t, true);
            Line(cx, cy - c, c - gap, t, false); Line(cx, cy + gap, c - gap, t, false);

            // Corner brackets.
            float f = frameHalfSize * h, arm = c;
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
            {
                float px = cx + sx * f, py = cy + sy * f;
                Line(Mathf.Min(px, px - sx * arm), py, arm, t, true);
                Line(px, Mathf.Min(py, py - sy * arm), arm, t, false);
            }

            // Tick scales near the outer edge of each lens.
            float edge = (lensSeparation + lensDiameter) * 0.5f * h - 0.06f * h;
            float span = lensDiameter * 0.35f * h;
            for (int i = 0; i < scaleTicks; i++)
            {
                float y = cy + Mathf.Lerp(-span, span, scaleTicks > 1 ? i / (float)(scaleTicks - 1) : 0.5f);
                float len = (i == scaleTicks / 2 ? 1.6f : i % 2 == 0 ? 1f : 0.6f) * c;
                Line(cx - edge, y, len, t, true);
                Line(cx + edge - len, y, len, t, true);
            }
        }

        // Horizontal: starts at (x, y) going right. Vertical: starts at (x, y) going down.
        static void Line(float x, float y, float length, float thickness, bool horizontal)
        {
            GUI.DrawTexture(horizontal
                ? new Rect(x, y - thickness * 0.5f, length, thickness)
                : new Rect(x - thickness * 0.5f, y, thickness, length), Texture2D.whiteTexture);
        }

        void OnDestroy()
        {
            if (mask != null) Destroy(mask);
        }

        // White texture: transparent inside either lens circle, opaque outside.
        void RebuildMask()
        {
            const int H = 512, W = H * 2;
            if (mask == null) mask = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            maskDiameter = lensDiameter; maskSeparation = lensSeparation; maskRim = rimSoftness;

            var pixels = new Color32[W * H];
            float radius = lensDiameter * 0.5f, offset = lensSeparation * 0.5f;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                // Units of screen height, origin at the centre.
                float u = (x + 0.5f) / H - 1f, v = (y + 0.5f) / H - 0.5f;
                float d = Mathf.Min(new Vector2(u - offset, v).magnitude, new Vector2(u + offset, v).magnitude);
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius - rimSoftness, radius, d));
                pixels[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            mask.SetPixels32(pixels);
            mask.Apply();
        }
    }
}
