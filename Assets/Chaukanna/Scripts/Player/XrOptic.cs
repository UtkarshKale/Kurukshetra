using UnityEngine;
using UnityEngine.UI;

namespace Chaukanna.Player
{
    // The binoculars in a headset. The head camera cannot change its field of view (the headset sets it), so the optic is a second
    // camera with the zoomed field of view that renders into a texture; the texture is shown in a round window on a world-space canvas
    // in front of the eyes, with everything outside the window blacked out, like looking through the lenses. The second camera follows
    // the head a little slowly while the binoculars are up (a hand-held glass is steadier than a head), and only renders then.
    public class XrOptic
    {
        public const float Distance = 1.2f, WindowDegrees = 40f, BlackoutDegrees = 120f;
        public const int TextureSize = 1024, MaskSize = 1024;
        public const float SteadyTime = 0.10f;           // seconds: how slowly the optic follows the head
        const int UiLayer = 5;                            // not drawn by the optic camera itself

        readonly Transform head;
        Camera cam;
        RenderTexture rt;
        GameObject canvasGo;
        CanvasGroup group;
        Canvas canvas;
        Texture2D mask;
        Quaternion aim;
        float fov;

        public Camera Camera => cam;
        public Quaternion Aim => aim;
        public float VerticalFov => fov;
        public bool Rendering => cam != null && cam.enabled;
        public bool CanvasVisible => canvas != null && canvas.enabled;

        public XrOptic(Camera headCamera, float zoomedFov)
        {
            head = headCamera.transform;
            fov = zoomedFov;
            aim = head.rotation;

            var go = new GameObject("OpticCamera");
            cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.CopyFrom(headCamera);
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.fieldOfView = zoomedFov;
            cam.aspect = 1f;
            cam.cullingMask = headCamera.cullingMask & ~(1 << UiLayer);
            cam.depth = headCamera.depth - 10f;               // renders before the eyes do
            cam.rect = new Rect(0f, 0f, 1f, 1f);
            cam.tag = "Untagged";
            rt = new RenderTexture(TextureSize, TextureSize, 24, RenderTextureFormat.ARGB32) { name = "OpticView", antiAliasing = 1, useMipMap = false };
            rt.Create();
            cam.targetTexture = rt;
            go.transform.SetPositionAndRotation(head.position, aim);

            BuildCanvas();
            canvas.enabled = false;
        }

        void BuildCanvas()
        {
            float blackout = 2f * Distance * Mathf.Tan(BlackoutDegrees * 0.5f * Mathf.Deg2Rad);         // metres across the black surround
            float window = 2f * Distance * Mathf.Tan(WindowDegrees * 0.5f * Mathf.Deg2Rad);             // metres across the round window
            float perUnit = blackout / MaskSize;
            float windowUnits = window / perUnit;

            canvasGo = new GameObject("OpticCanvas", typeof(RectTransform));
            canvasGo.layer = UiLayer;
            canvasGo.transform.SetParent(head, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0f, Distance);
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale = Vector3.one * perUnit;
            canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            group = canvasGo.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;
            var rect = (RectTransform)canvasGo.transform;
            rect.sizeDelta = new Vector2(MaskSize, MaskSize);

            // 1. The view through the optic, as large as the window.
            var view = Image(rect, "View", windowUnits);
            view.texture = rt;
            // 2. The black surround with a round hole the size of the window, on top.
            mask = BuildMask(windowUnits / MaskSize);
            var surround = Image(rect, "Surround", MaskSize);
            surround.texture = mask;
            // 3. A fine cross in the middle (open centre so it never hides a small contact).
            float c = windowUnits * 0.06f, gap = c * 0.3f, t = Mathf.Max(1.2f, windowUnits * 0.004f);
            Bar(rect, "L", -c - gap * 0.5f, 0f, c, t);
            Bar(rect, "R", c + gap * 0.5f, 0f, c, t);
            Bar(rect, "U", 0f, c + gap * 0.5f, t, c);
            Bar(rect, "D", 0f, -c - gap * 0.5f, t, c);
        }

        static RawImage Image(RectTransform parent, string name, float size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.layer = UiLayer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = new Vector2(size, size);
            var raw = go.GetComponent<RawImage>();
            raw.raycastTarget = false;
            return raw;
        }

        static void Bar(RectTransform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.layer = UiLayer;
            var r = (RectTransform)go.transform;
            r.SetParent(parent, false);
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.color = new Color(0f, 0f, 0f, 0.85f);
            img.raycastTarget = false;
        }

        // Black everywhere except a round hole (diameter = holeFraction of the width), soft at the rim.
        static Texture2D BuildMask(float holeFraction)
        {
            var tex = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "OpticMask" };
            var px = new Color32[MaskSize * MaskSize];
            float half = MaskSize * 0.5f, radius = holeFraction * half;
            for (int y = 0; y < MaskSize; y++)
            for (int x = 0; x < MaskSize; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half));
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(radius - 2f, radius + 1f, d));
                px[y * MaskSize + x] = new Color32(0, 0, 0, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            return tex;
        }

        // blend 0 = down (nothing rendered), 1 = fully up. The optic follows the head slowly while it is up.
        public void Tick(float blend, float dt)
        {
            bool up = blend > 0.01f;
            if (up) aim = Quaternion.Slerp(aim, head.rotation, 1f - Mathf.Exp(-dt / SteadyTime));
            else aim = head.rotation;
            if (cam == null) return;
            cam.transform.SetPositionAndRotation(head.position, aim);
            cam.enabled = up;
            canvas.enabled = up;
            group.alpha = Mathf.Clamp01(blend);
        }

        public void Dispose()
        {
            if (cam != null) Object.Destroy(cam.gameObject);
            if (canvasGo != null) Object.Destroy(canvasGo);
            if (mask != null) Object.Destroy(mask);
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
            cam = null;
        }
    }
}
