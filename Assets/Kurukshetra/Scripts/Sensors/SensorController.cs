using Kurukshetra.Player;
using Kurukshetra.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kurukshetra.Sensors
{
    // Owns the sensor profile and the Ok / Degraded switch, and draws the bottom-left sensor panel:
    // RF bar, sound bearing, AI assist line, status line. F3 toggles Ok / Degraded (debug).
    // The panel never shows whether the AI hint is wrong; that goes to the contact record for the AAR.
    [RequireComponent(typeof(Camera), typeof(ContactSensor))]
    public class SensorController : MonoBehaviour
    {
        public SensorProfile profile;
        public SensorState state = SensorState.Ok;
        [Tooltip("Seconds between sensor readings.")]
        public float sampleInterval = 0.1f;
        [Tooltip("Panel text size at 720p; scales with screen height.")]
        public int fontSize = 18;

        // Latest readings, for the debug HUD and tests.
        public float RfLevel { get; private set; }
        public bool HasBearing { get; private set; }
        public float WorldBearing { get; private set; }   // as displayed: delayed and noisy when degraded

        const int RfSamples = 80;
        const int BearingSamples = 64;

        readonly float[] rfHistory = new float[RfSamples];
        readonly float[] bearingHistory = new float[BearingSamples];
        readonly bool[] bearingValid = new bool[BearingSamples];
        int rfHead, bearingHead;

        Camera cam;
        ContactSensor sensor;
        ScenarioSpawner spawner;
        System.Random noise = new System.Random(1);
        int run = -1;
        float sampleTimer;
        float aiErrorOverride = -1f;   // from the trainer config; negative = use the profile

        // For the VR hand tablet, which shows the same readings on a screen in the left hand.
        public int RfSampleCount => RfSamples;
        public float RfAt(int i) => rfHistory[(rfHead + 1 + Mathf.Clamp(i, 0, RfSamples - 1)) % RfSamples];   // 0 = oldest
        public string StateText => stateText;
        public string BearingText => bearingText;
        public string AiText => aiText;
        public float RelativeBearing => HasBearing ? SensorMath.RelativeBearing(transform.eulerAngles.y, WorldBearing) : float.NaN;

        // Cached so the panel doesn't build strings every frame.
        string stateText = "", bearingText = "SOUND  --", aiText = "";
        float shownRelative = float.NaN;
        Contact aiTextFor;
        bool aiTextValid;
        SensorState stateTextFor = (SensorState)(-1);
        GUIStyle style;

        void Awake()
        {
            cam = GetComponent<Camera>();
            sensor = GetComponent<ContactSensor>();
        }

        void Update()
        {
            if (profile == null) return;
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner == null) return;
            if (spawner.RunNumber != run)
            {
                run = spawner.RunNumber;
                noise = new System.Random(spawner.ActiveSeed ^ 0x51A5);
                System.Array.Clear(rfHistory, 0, RfSamples);
                System.Array.Clear(bearingValid, 0, BearingSamples);
                sampleTimer = 0f;
                aiTextValid = false;
                var cfg = spawner.TrainerConfig;
                aiErrorOverride = cfg != null ? cfg.aiErrorOverride : -1f;
                if (cfg != null) state = cfg.sensorsDegraded ? SensorState.Degraded : SensorState.Ok;
            }

            var kb = Keyboard.current;
            if (kb != null && kb.f3Key.wasPressedThisFrame && !ContactRegistry.InputBlocked) SetState(state == SensorState.Ok ? SensorState.Degraded : SensorState.Ok);

            if (!spawner.Finished)
            {
                sampleTimer += Time.deltaTime;
                while (sampleTimer >= sampleInterval)
                {
                    sampleTimer -= sampleInterval;
                    Sample();
                }
            }

            UpdateHint();
        }

        public void SetState(SensorState s)
        {
            if (state == s) return;
            state = s;
            ContactRegistry.Note("Sensors " + (s == SensorState.Ok ? "OK" : "DEGRADED"));
        }

        [ContextMenu("Run Sensor Self-Test")]
        void RunSelfTest()
        {
            SensorSelfTest.Run();
        }

        // One reading of every sensor. Public so tests can drive it without waiting for the timer.
        public void Sample()
        {
            Vector3 eye = transform.position;
            float bestRf = 0f, bestSound = 0f;
            Vector3 loudest = Vector3.zero;
            int seed = spawner != null ? spawner.ActiveSeed : 0;

            var live = ContactRegistry.Live;
            for (int i = 0; i < live.Count; i++)
            {
                var c = live[i];
                var sig = profile.Signature(c.TruthClass);
                if (sig == null) continue;
                Vector3 to = c.transform.position - eye;
                float range = to.magnitude;
                if (range > profile.sensorRange) continue;

                if (sig.rfEmitter)
                {
                    float rf = SensorMath.RfLevel(sig.rfStrength * SensorMath.Variation(seed, c.Id, sig.rfVariation), range, profile.sensorRange);
                    if (rf > bestRf) bestRf = rf;
                }
                float heard = SensorMath.HeardLevel(sig.loudness, range, profile.soundHalfRange);
                if (heard > bestSound) { bestSound = heard; loudest = to; }
            }

            float wobble = profile.rfNoise.Get(state);
            RfLevel = Mathf.Clamp01(bestRf + ((float)noise.NextDouble() * 2f - 1f) * wobble);
            rfHead = (rfHead + 1) % RfSamples;
            rfHistory[rfHead] = RfLevel;

            bearingHead = (bearingHead + 1) % BearingSamples;
            bearingValid[bearingHead] = bestSound >= profile.audibleThreshold;
            bearingHistory[bearingHead] = bearingValid[bearingHead]
                ? SensorMath.WorldBearing(loudest) + ((float)noise.NextDouble() * 2f - 1f) * profile.bearingNoise.Get(state)
                : 0f;

            int lag = Mathf.Clamp(Mathf.RoundToInt(profile.bearingDelay.Get(state) / Mathf.Max(0.01f, sampleInterval)), 0, BearingSamples - 1);
            int at = (bearingHead - lag + BearingSamples) % BearingSamples;
            HasBearing = bearingValid[at];
            WorldBearing = bearingHistory[at];

            // Text only changes with a reading, so build it here rather than in OnGUI.
            float relative = HasBearing ? Mathf.Round(SensorMath.RelativeBearing(transform.eulerAngles.y, WorldBearing)) : float.NaN;
            if (!relative.Equals(shownRelative))
            {
                shownRelative = relative;
                bearingText = HasBearing ? $"SOUND  {Mathf.Abs(relative):000}° {(relative < 0f ? "L" : "R")}" : "SOUND  --";
            }
        }

        // The hint for a contact is chosen once, at its first lock, with the error rate in force then.
        void UpdateHint()
        {
            var c = sensor.Locked;
            if (c != null && c.Data.aiHint == PlayerCall.None)
            {
                var hint = SensorMath.PickHint(profile, spawner.ActiveSeed, c.Id, c.TruthClass, aiErrorOverride >= 0f ? aiErrorOverride : profile.aiErrorRate.Get(state));
                c.Data.aiHint = hint.call;
                c.Data.aiConfidence = hint.confidence;
                c.Data.aiHintWrong = hint.wrong;
                ContactRegistry.Record(ContactEventType.AiHint, c, $"AI says {hint.call.Label()} {hint.confidence}%{(hint.wrong ? " (WRONG)" : "")}, sensors {state}");
                aiTextValid = false;
            }
            if (!aiTextValid || !ReferenceEquals(c, aiTextFor))
            {
                aiTextFor = c;
                aiTextValid = true;
                aiText = c != null ? $"AI ASSIST: {c.Data.aiHint.Label()} {c.Data.aiConfidence}%" : "AI ASSIST: --";
            }
            if (stateTextFor != state)
            {
                stateTextFor = state;
                stateText = state == SensorState.Ok ? "SENSORS: OK" : "SENSORS: DEGRADED";
            }
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
            if (profile == null || Event.current.type != EventType.Repaint) return;
            if (ViewRigSwitch.VrActive) return;      // in VR the panel is the tablet in the left hand
            float ui = PlayerView.Height / 720f;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            style.fontSize = Mathf.RoundToInt(fontSize * ui);

            float pad = 8f * ui, line = style.lineHeight * 1.2f;
            float w = 330f * ui, stripH = 44f * ui, dial = 56f * ui;
            float h = pad * 2f + line + stripH + pad + dial + pad + line;
            var panel = new Rect(pad, PlayerView.Height - h - pad, w, h);
            Fill(panel, new Color(0f, 0f, 0f, 0.55f));
            float x = panel.x + pad, y = panel.y + pad, inner = w - pad * 2f;

            // Status line.
            style.normal.textColor = state == SensorState.Ok ? Color.white : new Color(1f, 0.7f, 0.2f);
            GUI.Label(new Rect(x, y, inner, line), stateText, style);
            style.normal.textColor = Color.white;
            y += line;

            // RF history strip: newest on the right, flat line when nothing is emitting.
            float labelW = 34f * ui;
            GUI.Label(new Rect(x, y, labelW, stripH), "RF", style);
            var strip = new Rect(x + labelW, y, inner - labelW, stripH);
            Fill(strip, new Color(1f, 1f, 1f, 0.08f));
            float barW = strip.width / RfSamples;
            var barColour = new Color(0.4f, 0.9f, 0.5f, 0.9f);
            for (int i = 0; i < RfSamples; i++)
            {
                float v = rfHistory[(rfHead + 1 + i) % RfSamples];
                float bh = Mathf.Max(1f * ui, v * strip.height);
                Fill(new Rect(strip.x + i * barW, strip.yMax - bh, Mathf.Max(1f, barW - 1f), bh), barColour);
            }
            y += stripH + pad;

            // Sound bearing dial: up = where the camera is facing.
            var dialRect = new Rect(x, y, dial, dial);
            Fill(dialRect, new Color(1f, 1f, 1f, 0.08f));
            Vector2 centre = dialRect.center;
            Fill(new Rect(centre.x - 1f, dialRect.y, 2f, 5f * ui), new Color(1f, 1f, 1f, 0.6f));   // facing tick
            if (HasBearing)
            {
                var saved = GUI.matrix;
                GUIUtility.RotateAroundPivot(SensorMath.RelativeBearing(transform.eulerAngles.y, WorldBearing), centre);
                float t = Mathf.Max(2f, 3f * ui);
                Fill(new Rect(centre.x - t * 0.5f, centre.y - dial * 0.45f, t, dial * 0.45f), new Color(1f, 0.85f, 0.3f));
                Fill(new Rect(centre.x - t * 1.5f, centre.y - dial * 0.45f, t * 3f, t), new Color(1f, 0.85f, 0.3f));   // arrow head
                GUI.matrix = saved;
            }
            GUI.Label(new Rect(x + dial + pad, y, inner - dial - pad, dial), bearingText, style);
            y += dial + pad;

            // AI assist line.
            GUI.Label(new Rect(x, y, inner, line), aiText, style);
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
