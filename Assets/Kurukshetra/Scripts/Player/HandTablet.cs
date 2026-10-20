using Kurukshetra.Scenario;
using Kurukshetra.Sensors;
using UnityEngine;
using UnityEngine.UI;

namespace Kurukshetra.Player
{
    // The sensor panel as a rugged handheld tablet in the left hand (VR mode), in the style of a game's handheld
    // heartbeat sensor: green phosphor fan of range arcs with a sweep, the sound bearing as a blip on the fan, the RF
    // trace along the bottom, then the AI hint, the locked contact and the ROE card. Same readings as the on-screen panel.
    // Built from primitives and a world-space canvas; no packages, no art assets.
    public class HandTablet : MonoBehaviour
    {
        const float CanvasW = 400f, CanvasH = 300f, MetresPerUnit = 0.000285f;
        const int RfBars = 40;
        static readonly Color Phosphor = new Color(0.35f, 1f, 0.45f, 1f);
        static readonly Color PhosphorDim = new Color(0.2f, 0.75f, 0.3f, 0.55f);

        SensorController sensors;
        ContactSensor contacts;

        // Headset: the frame rate (72 is the comfort floor), shown beside the ROE card. 0 = not shown.
        public float Fps { get; set; }
        Text status, roe, sound, ai, locked, edge;
        readonly Image[] rf = new Image[RfBars];
        RectTransform sweep, blipPivot;
        Image blip;
        float sweepAngle, sweepDir = 1f;
        static Texture2D radar;
        static Font font;

        public static HandTablet Build(Transform parent, SensorController sensors, ContactSensor contacts, Material body, Material bezel)
        {
            var root = new GameObject("SensorTablet").transform;
            root.SetParent(parent, false);

            // Rugged case: body, raised bumpers on the corners, a dark screen recess.
            Rig.Part(PrimitiveType.Cube, root, Vector3.zero, new Vector3(0.152f, 0.112f, 0.02f), body);
            for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                Rig.Part(PrimitiveType.Cube, root, new Vector3(sx * 0.072f, sy * 0.052f, -0.002f), new Vector3(0.022f, 0.022f, 0.026f), bezel);
            Rig.Part(PrimitiveType.Cube, root, new Vector3(0f, -0.06f, 0.004f), new Vector3(0.06f, 0.012f, 0.012f), bezel);   // grip bar

            var tablet = root.gameObject.AddComponent<HandTablet>();
            tablet.sensors = sensors;
            tablet.contacts = contacts;
            tablet.BuildScreen(root);
            return tablet;
        }

        void BuildScreen(Transform root)
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvasGo = new GameObject("Screen", typeof(RectTransform));
            canvasGo.transform.SetParent(root, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0.002f, -0.0105f);     // just in front of the case, facing the eye
            canvasGo.transform.localScale = Vector3.one * MetresPerUnit;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(CanvasW, CanvasH);

            Box(rt, 0f, 0f, CanvasW, CanvasH, new Color(0.015f, 0.05f, 0.025f, 1f));
            status = Label(rt, 26f, 8f, 200f, 22f, 17, TextAnchor.MiddleLeft);
            roe = Label(rt, 190f, 8f, 184f, 22f, 15, TextAnchor.MiddleRight);

            // Range fan: the drawn arcs, a sweep and the sound blip, all pivoting on the fan's centre at the bottom.
            if (radar == null) radar = BuildRadar();
            var fan = new GameObject("Fan", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
            Place(fan, rt, 20f, 34f, 360f, 160f);
            var raw = fan.GetComponent<RawImage>();
            raw.texture = radar;
            raw.color = Phosphor;
            raw.raycastTarget = false;

            sweep = new GameObject("Sweep", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            sweep.SetParent(fan, false);
            sweep.anchorMin = sweep.anchorMax = new Vector2(0.5f, 0f);
            sweep.pivot = new Vector2(0.5f, 0f);
            sweep.sizeDelta = new Vector2(3f, 156f);
            var sweepImage = sweep.GetComponent<Image>();
            sweepImage.color = new Color(Phosphor.r, Phosphor.g, Phosphor.b, 0.45f);
            sweepImage.raycastTarget = false;

            blipPivot = new GameObject("BearingPivot", typeof(RectTransform)).GetComponent<RectTransform>();
            blipPivot.SetParent(fan, false);
            blipPivot.anchorMin = blipPivot.anchorMax = new Vector2(0.5f, 0f);
            blipPivot.sizeDelta = Vector2.zero;
            blip = new GameObject("Blip", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            blip.rectTransform.SetParent(blipPivot, false);
            blip.rectTransform.anchoredPosition = new Vector2(0f, 118f);
            blip.rectTransform.sizeDelta = new Vector2(14f, 14f);
            blip.color = new Color(0.85f, 1f, 0.6f, 1f);
            blip.raycastTarget = false;
            var trail = new GameObject("Trail", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            trail.rectTransform.SetParent(blipPivot, false);
            trail.rectTransform.anchoredPosition = new Vector2(0f, 60f);
            trail.rectTransform.sizeDelta = new Vector2(2f, 110f);
            trail.color = new Color(Phosphor.r, Phosphor.g, Phosphor.b, 0.35f);
            trail.raycastTarget = false;
            edge = Label(rt, 20f, 160f, 360f, 30f, 22, TextAnchor.MiddleCenter);

            sound = Label(rt, 14f, 196f, 372f, 20f, 15, TextAnchor.MiddleLeft);
            // RF trace: newest on the right.
            Label(rt, 14f, 218f, 30f, 26f, 14, TextAnchor.MiddleLeft).text = "RF";
            for (int i = 0; i < RfBars; i++)
            {
                rf[i] = Box(rt, 46f + i * 8.4f, 218f, 6f, 26f, Phosphor);
                rf[i].rectTransform.pivot = new Vector2(0f, 0f);
                rf[i].rectTransform.anchoredPosition = new Vector2(46f + i * 8.4f, -244f);
            }
            ai = Label(rt, 14f, 248f, 372f, 22f, 15, TextAnchor.MiddleLeft);
            locked = Label(rt, 14f, 270f, 372f, 22f, 15, TextAnchor.MiddleLeft);
        }

        void LateUpdate()
        {
            if (sensors == null || contacts == null) return;
            status.text = sensors.StateText;
            status.color = sensors.state == SensorState.Ok ? Phosphor : new Color(1f, 0.75f, 0.25f);
            roe.text = (Fps > 0f ? Mathf.RoundToInt(Fps) + " fps   " : "") + "ROE " + RoeName(ContactRegistry.CurrentRoe);
            roe.color = RoeCards.IsAttack(ContactRegistry.CurrentRoe) ? new Color(1f, 0.4f, 0.3f) : Phosphor;

            // Sweep goes back and forth across the fan; purely to make the screen feel alive.
            sweepAngle += sweepDir * 70f * Time.deltaTime;
            if (Mathf.Abs(sweepAngle) > 85f) { sweepAngle = Mathf.Clamp(sweepAngle, -85f, 85f); sweepDir = -sweepDir; }
            sweep.localRotation = Quaternion.Euler(0f, 0f, -sweepAngle);

            // Sound bearing: up the fan = where the head points. Behind = an arrow on the side to turn to.
            float rel = sensors.RelativeBearing;
            bool has = !float.IsNaN(rel);
            blip.enabled = has && Mathf.Abs(rel) <= 90f;
            blipPivot.gameObject.SetActive(has && Mathf.Abs(rel) <= 90f);
            if (has) blipPivot.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Clamp(rel, -90f, 90f));
            if (blip.enabled) blip.color = new Color(0.85f, 1f, 0.6f, 0.6f + 0.4f * Mathf.PingPong(Time.time * 3f, 1f));
            edge.text = has && rel < -90f ? "<<  SOUND BEHIND LEFT" : has && rel > 90f ? "SOUND BEHIND RIGHT  >>" : "";
            sound.text = sensors.BearingText;

            for (int i = 0; i < RfBars; i++)
            {
                float v = sensors.RfAt(sensors.RfSampleCount - RfBars + i);
                rf[i].rectTransform.sizeDelta = new Vector2(6f, Mathf.Max(2f, v * 26f));
            }
            ai.text = sensors.AiText;

            var targets = contacts.Targets;
            if (targets.Count > 1) locked.text = $"GROUP  {targets.Count} SELECTED";
            else if (targets.Count == 1 && targets[0] != null)
            {
                var c = targets[0];
                float range = Vector3.Distance(contacts.transform.position, c.transform.position);
                locked.text = $"LOCK #{c.Id:00}  {range:F0} m" + (c.Call != PlayerCall.None ? "  " + c.Call.Label().ToUpper() : "") + (c.AnomalyShown ? "  ! OFF PATTERN" : "");
            }
            else locked.text = "LOCK  --";
        }

        static string RoeName(RoeCard card) => RoeCards.Short(card);

        // The fan: a half disc of range arcs and bearing spokes, drawn once into a texture.
        static Texture2D BuildRadar()
        {
            const int W = 512, H = 228;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SensorFan" };
            var px = new Color32[W * H];
            float cx = W * 0.5f, r = H - 4f;
            for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                float dx = x - cx, dy = y, d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = 0f;
                if (d <= r)
                {
                    a = 0.06f;                                                       // faint screen fill inside the fan
                    float ring = d / r * 4f;
                    if (Mathf.Abs(ring - Mathf.Round(ring)) * r / 4f < 1.2f && ring > 0.5f) a = 0.75f;     // range arcs
                    float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;
                    if (Mathf.Abs(ang / 30f - Mathf.Round(ang / 30f)) * 30f * Mathf.Deg2Rad * d < 1.0f && d > 12f) a = Mathf.Max(a, 0.4f);   // spokes
                }
                if (Mathf.Abs(d - r) < 2f) a = 1f;                                   // outer rim
                px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }

        static void Place(RectTransform t, RectTransform parent, float x, float y, float w, float h)
        {
            t.SetParent(parent, false);
            t.anchorMin = t.anchorMax = new Vector2(0f, 1f);
            t.pivot = new Vector2(0f, 1f);
            t.anchoredPosition = new Vector2(x, -y);
            t.sizeDelta = new Vector2(w, h);
        }

        static Image Box(RectTransform parent, float x, float y, float w, float h, Color c)
        {
            var img = new GameObject("Box", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            Place(img.rectTransform, parent, x, y, w, h);
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        static Text Label(RectTransform parent, float x, float y, float w, float h, int size, TextAnchor align)
        {
            var t = new GameObject("Text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            Place(t.rectTransform, parent, x, y, w, h);
            t.font = font;
            t.fontSize = size;
            t.alignment = align;
            t.color = Phosphor;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.raycastTarget = false;
            var glow = t.gameObject.AddComponent<Outline>();     // a little bleed, like a phosphor screen
            glow.effectColor = new Color(PhosphorDim.r, PhosphorDim.g, PhosphorDim.b, 0.3f);
            glow.effectDistance = new Vector2(1f, -1f);
            return t;
        }
    }
}
