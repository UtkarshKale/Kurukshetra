using Chaukanna.Player;
using Chaukanna.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Environment
{
    // Applies time of day, visibility and night vision at run start, at runtime only.
    // Day + Clear is the scene exactly as the user built it: the original light, ambient, sky and fog values are
    // remembered at start and put back whenever Day is selected and when this component is disabled.
    public class TimeOfDayController : MonoBehaviour
    {
        public EnvironmentProfile profile;

        [Header("Used when no trainer config is applied")]
        public TimeOfDay defaultTime = TimeOfDay.Day;
        public Visibility defaultVisibility = Visibility.Clear;

        [Tooltip("HUD text size at 720p; scales with screen height.")]
        public int fontSize = 18;

        public TimeOfDay Time { get; private set; }
        public Visibility Visibility { get; private set; }
        public bool NightVisionOn { get; private set; }
        public bool NightVisionAvailable => Time != TimeOfDay.Day;

        ScenarioSpawner spawner;
        ContactSensor sensor;
        int run = -1;
        float difficultySize = 1f;

        // The user's scene, remembered so it can be restored.
        bool captured;
        Light sun;
        Quaternion sunRotation;
        Color sunColour;
        float sunIntensity;
        UnityEngine.Rendering.AmbientMode ambientMode;
        Color ambientLight;
        float ambientIntensity, reflectionIntensity;
        Material skybox;
        bool fog;
        FogMode fogMode;
        float fogDensity;
        Color fogColour;

        Material skyInstance;
        Texture2D tube, noise;
        float tubeBuiltFor = -1f;
        GUIStyle style;

        void Start()
        {
            Capture();
        }

        void Capture()
        {
            if (captured) return;
            captured = true;
            sun = RenderSettings.sun;
            if (sun == null)
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional) { sun = l; break; }
            if (sun != null)
            {
                sunRotation = sun.transform.rotation;
                sunColour = sun.color;
                sunIntensity = sun.intensity;
            }
            ambientMode = RenderSettings.ambientMode;
            ambientLight = RenderSettings.ambientLight;
            ambientIntensity = RenderSettings.ambientIntensity;
            reflectionIntensity = RenderSettings.reflectionIntensity;
            skybox = RenderSettings.skybox;
            fog = RenderSettings.fog;
            fogMode = RenderSettings.fogMode;
            fogDensity = RenderSettings.fogDensity;
            fogColour = RenderSettings.fogColor;
        }

        // Put the scene back exactly as it was found.
        public void Restore()
        {
            if (!captured) return;
            if (sun != null)
            {
                sun.transform.rotation = sunRotation;
                sun.color = sunColour;
                sun.intensity = sunIntensity;
            }
            RenderSettings.ambientMode = ambientMode;
            RenderSettings.ambientLight = ambientLight;
            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.skybox = skybox;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogColor = fogColour;
        }

        void OnDisable()
        {
            Restore();
            if (skyInstance != null) Destroy(skyInstance);
        }

        void Update()
        {
            if (profile == null) return;
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (sensor == null) sensor = FindAnyObjectByType<ContactSensor>();
            if (spawner == null) return;

            if (spawner.RunNumber != run)
            {
                run = spawner.RunNumber;
                var cfg = spawner.TrainerConfig;
                difficultySize = cfg != null ? cfg.detectionSizeMultiplier : 1f;
                NightVisionOn = false;
                Apply(cfg != null ? cfg.timeOfDay : defaultTime, cfg != null ? cfg.visibility : defaultVisibility);
            }

            var kb = Keyboard.current;
            if (kb != null && kb.nKey.wasPressedThisFrame && !ContactRegistry.InputBlocked && NightVisionAvailable && !spawner.Finished)
                SetNightVision(!NightVisionOn);
            if (NightVisionOn && !spawner.Finished) RunConditions.NightVisionSeconds += UnityEngine.Time.deltaTime;
        }

        public void SetNightVision(bool on)
        {
            if (on && !NightVisionAvailable) return;
            if (on == NightVisionOn) return;
            NightVisionOn = on;
            ContactRegistry.Note("Night vision " + (on ? "ON" : "OFF"));
            Apply(Time, Visibility);
        }

        // Applies a look. Always starts from the user's own scene, so nothing accumulates.
        public void Apply(TimeOfDay time, Visibility visibility)
        {
            Capture();
            Restore();
            Time = RunConditions.Time = time;
            Visibility = RunConditions.Visibility = visibility;
            if (time == TimeOfDay.Day) NightVisionOn = false;

            var look = profile.Look(time);
            if (look != null)
            {
                float sunPower = NightVisionOn ? Mathf.Max(look.sunIntensity, profile.nightVisionSunIntensity) : look.sunIntensity;
                Color sunTone = NightVisionOn ? profile.nightVisionTint : look.sunColour;
                Color ambient = NightVisionOn ? profile.nightVisionAmbient : look.ambientColour;
                float exposure = NightVisionOn ? Mathf.Max(look.skyExposure, profile.nightVisionSkyExposure) : look.skyExposure;
                Color tint = NightVisionOn ? profile.nightVisionTint * 0.5f : look.skyTint;

                if (sun != null)
                {
                    // Keep the user's compass direction; only the height above the horizon changes.
                    sun.transform.rotation = Quaternion.Euler(look.sunElevation, sunRotation.eulerAngles.y, 0f);
                    sun.color = sunTone;
                    sun.intensity = sunPower;
                }
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = ambient;
                RenderSettings.reflectionIntensity = NightVisionOn ? Mathf.Max(look.reflectionIntensity, 0.3f) : look.reflectionIntensity;

                // The user's sky box asset is never edited: a runtime copy is dimmed and tinted instead.
                // The same goes for a sky box assigned in the profile: only the copy is changed.
                Material sky = look.skybox != null ? look.skybox : skybox;
                if (sky != null)
                {
                    if (skyInstance == null || skyInstance.shader != sky.shader) { if (skyInstance != null) Destroy(skyInstance); skyInstance = new Material(sky); }
                    skyInstance.name = sky.name + " (runtime copy)";
                    skyInstance.CopyPropertiesFromMaterial(sky);
                    if (skyInstance.HasProperty("_Exposure")) skyInstance.SetFloat("_Exposure", sky.GetFloat("_Exposure") * exposure);
                    if (skyInstance.HasProperty("_Tint")) skyInstance.SetColor("_Tint", tint);
                    RenderSettings.skybox = skyInstance;
                }
            }

            float range = EnvironmentMath.RangeMultiplier(profile, visibility);
            if (visibility != Visibility.Clear)
            {
                float maxRange = sensor != null ? sensor.maxVisibleRange : 2000f;
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogDensity = EnvironmentMath.FogDensity(maxRange * range);
                RenderSettings.fogColor = NightVisionOn ? profile.nightVisionAmbient * 0.6f : look != null ? look.fogColour : profile.dayFogColour;
            }

            if (sensor != null)
            {
                sensor.sizeThresholdMultiplier = EnvironmentMath.SizeMultiplier(profile, time, NightVisionOn) * difficultySize;
                sensor.bestSizeThresholdMultiplier = EnvironmentMath.SizeMultiplier(profile, time, time != TimeOfDay.Day) * difficultySize;
                sensor.rangeMultiplier = range;
            }
        }

        // Night-vision goggles: green wash, moving grain and a dark surround. Drawn under the binocular mask and the HUD.
        void OnGUI()
        {
            if (profile == null || Event.current.type != EventType.Repaint) return;
            GUI.depth = 20;
            GUI.BeginGroup(PlayerView.Rect);
            float w = PlayerView.Width, h = PlayerView.Height, ui = h / 720f;

            if (NightVisionOn)
            {
                if (noise == null) BuildNoise();
                if (tube == null || tubeBuiltFor != profile.nightVisionTube) BuildTube();
                var old = GUI.color;

                var wash = profile.nightVisionTint;
                GUI.color = new Color(wash.r, wash.g, wash.b, profile.nightVisionWash);
                GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);

                // Grain: the same texture, shifted every frame.
                GUI.color = new Color(wash.r, wash.g, wash.b, profile.nightVisionNoise);
                float ox = Random.value, oy = Random.value, tiles = h / 256f;
                GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, w, h), noise, new Rect(ox, oy, tiles * w / h, tiles));

                // Dark surround: narrows the usable view.
                GUI.color = Color.black;
                float d = h * profile.nightVisionTube * 1.4f;   // the texture's clear circle is 1/1.4 of its size
                var r = new Rect((w - d) * 0.5f, (h - d) * 0.5f, d, d);
                GUI.DrawTexture(r, tube);
                if (r.x > 0f) { GUI.DrawTexture(new Rect(0f, 0f, r.x, h), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(r.xMax, 0f, w - r.xMax, h), Texture2D.whiteTexture); }
                if (r.y > 0f) { GUI.DrawTexture(new Rect(0f, 0f, w, r.y), Texture2D.whiteTexture); GUI.DrawTexture(new Rect(0f, r.yMax, w, h - r.yMax), Texture2D.whiteTexture); }
                GUI.color = old;
            }

            // HUD tag, under the ROE banner.
            if (NightVisionAvailable && spawner != null && !spawner.Finished)
            {
                if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontStyle = FontStyle.Bold };
                style.fontSize = Mathf.RoundToInt(fontSize * ui);
                style.normal.textColor = NightVisionOn ? new Color(0.5f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.7f);
                string text = NightVisionOn ? "NV ON  (N)" : "N: night vision";
                var size = style.CalcSize(new GUIContent(text));
                var box = new Rect((w - size.x) * 0.5f - 8f * ui, 58f * ui, size.x + 16f * ui, size.y + 6f * ui);
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.55f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = old;
                GUI.Label(box, text, style);
            }
            GUI.EndGroup();
        }

        void BuildNoise()
        {
            const int n = 128;
            noise = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point };
            var rng = new System.Random(11);
            var px = new Color32[n * n];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, (byte)rng.Next(256));
            noise.SetPixels32(px);
            noise.Apply();
        }

        // White texture, transparent inside a soft-edged circle of 1/1.4 its size, opaque outside.
        void BuildTube()
        {
            const int n = 256;
            if (tube == null) tube = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tubeBuiltFor = profile.nightVisionTube;
            var px = new Color32[n * n];
            float half = n * 0.5f, clear = 1f / 1.4f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = new Vector2(x + 0.5f - half, y + 0.5f - half).magnitude / half;
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(clear * 0.72f, clear, d));
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tube.SetPixels32(px);
            tube.Apply();
        }

        void OnDestroy()
        {
            if (tube != null) Destroy(tube);
            if (noise != null) Destroy(noise);
        }
    }
}
