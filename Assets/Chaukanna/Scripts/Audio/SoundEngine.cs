using System.Collections.Generic;
using Chaukanna.Scenario;
using Chaukanna.Sensors;
using UnityEngine;

namespace Chaukanna.Audio
{
    // Procedural sound for the field scene. Lives on the observer camera (added by ObserverLook.Start).
    //   - the nearest MaxVoices audible contacts each get a pooled AudioSource whose motor is synthesised in OnAudioFilterRead;
    //     Unity does the 3D panning and the Doppler shift, we do the distance gain, the air-absorption low-pass and the RPM.
    //   - everything else (the rest of the single contacts, each big swarm / bird flock, the dense-sky crowd) is one "bed" voice
    //     per group whose loudness grows with the square root of the count and falls with distance.
    //   - one-shots: gunfire burst and clank on Engage, a jammer hum on the player side on Jam.
    public class SoundEngine : MonoBehaviour
    {
        public const int MaxVoices = 12;
        const int MaxBeds = 4;
        const float SelectInterval = 0.12f;
        const float DroneAudibleRange = 1500f;
        const float BirdAudibleRange = 120f;
        const string PrefOn = "chaukanna.sound.on", PrefVol = "chaukanna.sound.vol";

        public static SoundEngine Instance { get; private set; }

        static bool prefsLoaded, enabledFlag = true;
        static int volume = 70;
        public static bool Enabled { get { LoadPrefs(); return enabledFlag; } set { LoadPrefs(); enabledFlag = value; Save(); } }
        public static int Volume { get { LoadPrefs(); return volume; } set { LoadPrefs(); volume = Mathf.Clamp(value, 0, 100); Save(); } }
        static float Master => Enabled ? Volume / 100f : 0f;

        static void LoadPrefs()
        {
            if (prefsLoaded) return;
            prefsLoaded = true;
            try { enabledFlag = PlayerPrefs.GetInt(PrefOn, 1) != 0; volume = Mathf.Clamp(PlayerPrefs.GetInt(PrefVol, 70), 0, 100); } catch { }
        }
        static void Save() { try { PlayerPrefs.SetInt(PrefOn, enabledFlag ? 1 : 0); PlayerPrefs.SetInt(PrefVol, volume); } catch { } }

        // --- Public effect hooks (safe to call with no engine present) ---

        public static void Gunshot(Vector3 from) { if (Instance != null) Instance.PlayShot(from); }
        public static void Impact(Vector3 at) { if (Instance != null) Instance.PlayImpact(at); }
        public static void JammerPulse(Vector3 droneAt) { if (Instance != null) Instance.jamUntil = Time.unscaledTime + 3.5f; }

        // What the engine is doing, for the F6 panel and for tests.
        public int ActiveVoices { get; private set; }
        public int ActiveBeds { get; private set; }
        public int Candidates { get; private set; }
        public float LastUpdateMs { get; private set; }
        public int LoudestContactId { get; private set; } = -1;     // the sensor panel's sound bearing points at the same one

        class VoiceFilter : MonoBehaviour
        {
            public DroneSynth synth;
            public int sampleRate = 48000;
            void OnAudioFilterRead(float[] data, int channels)
            {
                var s = synth;
                if (s == null) return;
                s.Render(data, data.Length / channels, channels, sampleRate);
            }
        }

        class Voice
        {
            public GameObject go;
            public AudioSource src;
            public DroneSynth synth = new DroneSynth();
            public Contact contact;
            public ContactMotion motion;
            public Vector3 lastPos, vel;
            public float turn;
            public bool inUse;
        }

        class Bed
        {
            public GameObject go;
            public AudioSource src;
            public DroneSynth synth = new DroneSynth();
            public bool birds, inUse;
            public int count;
            public Vector3 pos;
            public float distance;
        }

        readonly Voice[] voices = new Voice[MaxVoices];
        readonly Bed[] beds = new Bed[MaxBeds];
        readonly List<AudioSource> oneShots = new List<AudioSource>();
        AudioSource jamSource;
        AudioClip syncClip, gunClip, hitClip, jamClip;
        float jamUntil, nextSelect;
        int sampleRate = 48000;
        SensorController sensor;

        float[] score = new float[64];
        Contact[] cand = new Contact[64];
        readonly int[] top = new int[MaxVoices];

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            Instance = this;
            sampleRate = AudioSettings.outputSampleRate;
            sensor = GetComponent<SensorController>();
            if (FindFirstObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();
            syncClip = AudioClip.Create("Chaukanna_SoundSync", 2048, 1, sampleRate, false);    // silent; just keeps the source running
            gunClip = MakeClip("Chaukanna_Gun", SoundMath.GunBurst(sampleRate));
            hitClip = MakeClip("Chaukanna_Hit", SoundMath.HitClank(sampleRate));
            jamClip = MakeClip("Chaukanna_Jam", SoundMath.JammerHum(sampleRate));
            var root = new GameObject("Chaukanna_Sound");
            root.transform.SetParent(transform, false);
            for (int i = 0; i < MaxVoices; i++)
            {
                var v = new Voice();
                v.go = new GameObject("Voice" + i);
                v.go.transform.SetParent(root.transform, false);
                v.src = Setup3D(v.go, out var f);
                f.synth = v.synth; f.sampleRate = sampleRate;
                voices[i] = v;
            }
            for (int i = 0; i < MaxBeds; i++)
            {
                var b = new Bed();
                b.go = new GameObject("Bed" + i);
                b.go.transform.SetParent(root.transform, false);
                b.src = Setup3D(b.go, out var f);
                f.synth = b.synth; f.sampleRate = sampleRate;
                beds[i] = b;
            }
            var jg = new GameObject("JammerHum");
            jg.transform.SetParent(root.transform, false);
            jamSource = jg.AddComponent<AudioSource>();
            jamSource.clip = jamClip; jamSource.loop = true; jamSource.spatialBlend = 0f; jamSource.playOnAwake = false;
            for (int i = 0; i < 4; i++)
            {
                var g = new GameObject("OneShot" + i);
                g.transform.SetParent(root.transform, false);
                var a = g.AddComponent<AudioSource>();
                a.playOnAwake = false; a.spatialBlend = 1f; a.dopplerLevel = 0f; a.rolloffMode = AudioRolloffMode.Logarithmic;
                a.minDistance = 30f; a.maxDistance = 6000f;
                oneShots.Add(a);
            }
        }

        AudioClip MakeClip(string name, float[] samples)
        {
            var c = AudioClip.Create(name, samples.Length, 1, sampleRate, false);
            c.SetData(samples, 0);
            return c;
        }

        AudioSource Setup3D(GameObject go, out VoiceFilter filter)
        {
            var s = go.AddComponent<AudioSource>();
            s.clip = syncClip; s.loop = true; s.playOnAwake = false;
            s.spatialBlend = 1f; s.dopplerLevel = 1f; s.rolloffMode = AudioRolloffMode.Linear;
            s.minDistance = 1f; s.maxDistance = 100000f;      // our own gain does the distance falloff; Unity's stays near 1
            s.volume = 1f;
            filter = go.AddComponent<VoiceFilter>();
            return s;
        }

        // The score the sensor panel's sound bearing uses (SensorMath.HeardLevel), so the loudest voice is the loudest bearing.
        float Score(Contact c, float range)
        {
            float loud = 0.3f, half = 400f;
            if (sensor != null && sensor.profile != null)
            {
                var sig = sensor.profile.Signature(c.TruthClass);
                if (sig != null) loud = sig.loudness;
                half = sensor.profile.soundHalfRange;
            }
            else if (c.TruthClass == ContactClass.Bird) loud = 0.05f;
            return SensorMath.HeardLevel(loud, range, half);
        }

        static DroneKind KindFor(Contact c, ContactMotion m)
        {
            switch (c.TruthClass)
            {
                case ContactClass.Bird: return DroneKind.Bird;
                case ContactClass.HostileFixedWing: return DroneKind.FixedWing;
                case ContactClass.HostileQuad: return m != null && m.Speed >= 22f ? DroneKind.Fpv : DroneKind.Quad;
                default: return DroneKind.Quad;
            }
        }

        void Update()
        {
            float t0 = Time.realtimeSinceStartup;
            float master = Master;
            if (master <= 0f)
            {
                StopAll();
                UpdateJam(0f);
                return;
            }
            if (Time.unscaledTime >= nextSelect)
            {
                nextSelect = Time.unscaledTime + SelectInterval;
                Select();
            }
            Vector3 ear = transform.position;
            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            int active = 0;
            for (int i = 0; i < MaxVoices; i++)
            {
                var v = voices[i];
                if (!v.inUse) continue;
                if (v.contact == null) { Release(v); continue; }
                active++;
                Vector3 p = v.contact.transform.position;
                Vector3 nv = (p - v.lastPos) / dt;
                if ((p - v.lastPos).sqrMagnitude > 10000f) nv = v.vel;      // a teleport is not a speed
                Vector3 oldDir = v.vel.sqrMagnitude > 1f ? v.vel.normalized : Vector3.zero;
                v.vel = Vector3.Lerp(v.vel, nv, 1f - Mathf.Exp(-dt / 0.25f));
                if (oldDir != Vector3.zero && v.vel.sqrMagnitude > 1f)
                    v.turn = Mathf.Lerp(v.turn, Vector3.Angle(oldDir, v.vel.normalized) / dt, 1f - Mathf.Exp(-dt / 0.5f));
                v.lastPos = p;
                v.go.transform.position = p;
                float dist = Vector3.Distance(p, ear);
                bool jammed = v.motion != null && v.motion.Jammed;
                var flat = new Vector3(v.vel.x, 0f, v.vel.z);
                v.synth.targetRpm = SoundMath.Rpm(v.synth.kind, flat.magnitude, v.vel.y, v.turn, jammed ? 1f : 0f);
                v.synth.sputter = jammed ? 1f : 0f;
                v.synth.cutoffHz = SoundMath.AirCutoffHz(dist);
                v.synth.gain = SoundMath.ClassGain(v.synth.kind) * SoundMath.DistanceGain(dist) * master;
            }
            ActiveVoices = active;
            for (int i = 0; i < MaxBeds; i++)
            {
                var b = beds[i];
                if (!b.inUse) continue;
                b.go.transform.position = b.pos;
                b.synth.cutoffHz = SoundMath.AirCutoffHz(b.distance);
                b.synth.gain = SoundMath.BedLevel(b.count, b.distance) * (b.birds ? 0.12f : 0.35f) * master;
            }
            UpdateJam(master);
            LastUpdateMs = (Time.realtimeSinceStartup - t0) * 1000f;
        }

        void UpdateJam(float master)
        {
            if (jamSource == null) return;
            bool on = master > 0f && Time.unscaledTime < jamUntil;
            if (on) { jamSource.volume = 0.25f * master; if (!jamSource.isPlaying) jamSource.Play(); }
            else if (jamSource.isPlaying) jamSource.Stop();
        }

        void StopAll()
        {
            for (int i = 0; i < MaxVoices; i++) if (voices[i].inUse) Release(voices[i]);
            for (int i = 0; i < MaxBeds; i++) if (beds[i].inUse) { beds[i].inUse = false; beds[i].src.Stop(); beds[i].synth.gain = 0f; }
            ActiveVoices = ActiveBeds = 0;
        }

        void Release(Voice v)
        {
            v.inUse = false; v.contact = null; v.motion = null;
            v.synth.gain = 0f;
            v.src.Stop();
        }

        // Every SelectInterval: choose which contacts get a real voice, and what goes into the beds.
        void Select()
        {
            var live = ContactRegistry.Live;
            Vector3 ear = transform.position;
            if (score.Length < live.Count) { score = new float[live.Count * 2]; cand = new Contact[live.Count * 2]; }
            int n = 0;
            for (int i = 0; i < live.Count; i++)
            {
                var c = live[i];
                if (c == null) continue;
                float range = Vector3.Distance(c.transform.position, ear);
                bool bird = c.TruthClass == ContactClass.Bird;
                if (range > (bird ? BirdAudibleRange : DroneAudibleRange)) continue;
                cand[n] = c;
                score[n] = Score(c, range);
                n++;
            }
            Candidates = n;
            int got = SoundMath.SelectTop(score, n, MaxVoices, top);
            LoudestContactId = got > 0 ? cand[top[0]].Id : -1;

            // Keep voices whose contact is still chosen; free the rest.
            for (int i = 0; i < MaxVoices; i++)
            {
                var v = voices[i];
                if (!v.inUse) continue;
                bool keep = false;
                for (int k = 0; k < got; k++) if (cand[top[k]] == v.contact) { keep = true; break; }
                if (!keep) Release(v);
            }
            for (int k = 0; k < got; k++)
            {
                var c = cand[top[k]];
                bool has = false;
                for (int i = 0; i < MaxVoices; i++) if (voices[i].inUse && voices[i].contact == c) { has = true; break; }
                if (has) continue;
                for (int i = 0; i < MaxVoices; i++)
                {
                    if (voices[i].inUse) continue;
                    Bind(voices[i], c);
                    break;
                }
            }

            // Beds. 0: single contacts that did not get a voice (and are not birds). Then one per swarm / flock, then the crowd.
            int bedCount = 0;
            int restCount = 0; Vector3 restSum = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var c = cand[i];
                if (c.TruthClass == ContactClass.Bird) continue;
                bool voiced = false;
                for (int k = 0; k < got; k++) if (top[k] == i) { voiced = true; break; }
                if (voiced) continue;
                restCount++; restSum += c.transform.position;
            }
            if (restCount > 0) SetBed(bedCount++, false, restCount, restSum / restCount, ear);
            for (int s = 0; s < SwarmSky.All.Count && bedCount < MaxBeds; s++)
            {
                var sky = SwarmSky.All[s];
                if (sky == null || !sky.Started) continue;
                int flying = sky.Summary().flying;
                if (flying <= 0) continue;
                Vector3 sum = Vector3.zero; int taken = 0;
                int total = sky.Count;
                int step = Mathf.Max(1, total / 48);
                for (int i = 0; i < total; i += step)
                    if (sky.IsFlying(i)) { sum += sky.PositionOf(i); taken++; }
                if (taken == 0) continue;
                SetBed(bedCount++, sky.IsBirds, flying, sum / taken, ear);
            }
            var crowd = CrowdSky.Active;
            if (crowd != null && crowd.AliveCount > 0 && bedCount < MaxBeds)
            {
                // The dense sky surrounds the post: stand its bed at a typical drone distance, straight overhead-ish.
                Vector3 mid = ear + Vector3.up * 80f;
                SetBed(bedCount++, false, crowd.AliveCount, mid, ear, Mathf.Max(150f, crowd.Radius * 0.5f));
            }
            for (int i = bedCount; i < MaxBeds; i++)
                if (beds[i].inUse) { beds[i].inUse = false; beds[i].src.Stop(); beds[i].synth.gain = 0f; }
            ActiveBeds = bedCount;
        }

        void SetBed(int i, bool birds, int count, Vector3 pos, Vector3 ear, float distanceOverride = -1f)
        {
            var b = beds[i];
            if (!b.inUse || b.birds != birds)
            {
                if (birds) b.synth.Init(DroneKind.Bird, 77 + i);
                else b.synth.Init(DroneKind.Quad, 31 + i, 8, 0.07f);      // eight rotors spread +-7 % blur into a buzz
                b.synth.targetRpm = birds ? 420f : 6800f;
                b.synth.SnapRpm();
                b.inUse = true;
                b.src.Play();
            }
            b.birds = birds; b.count = count; b.pos = pos;
            b.distance = distanceOverride > 0f ? distanceOverride : Vector3.Distance(pos, ear);
            b.synth.density = count;
        }

        void Bind(Voice v, Contact c)
        {
            v.contact = c;
            v.motion = c.GetComponent<ContactMotion>();
            var kind = KindFor(c, v.motion);
            v.synth.gain = 0f;
            v.synth.Init(kind, c.Id * 7919 + 13);
            v.synth.targetRpm = SoundMath.HoverRpm(kind);
            v.synth.SnapRpm();
            v.lastPos = c.transform.position;
            v.vel = Vector3.zero; v.turn = 0f;
            v.go.transform.position = v.lastPos;
            v.inUse = true;
            v.src.Play();
        }

        AudioSource FreeOneShot()
        {
            for (int i = 0; i < oneShots.Count; i++) if (!oneShots[i].isPlaying) return oneShots[i];
            return oneShots[0];
        }

        void PlayShot(Vector3 from)
        {
            float m = Master;
            if (m <= 0f || gunClip == null) return;
            var a = FreeOneShot();
            a.transform.position = from; a.spatialBlend = 0.3f; a.volume = 0.8f * m;
            a.clip = gunClip; a.Play();
        }

        void PlayImpact(Vector3 at)
        {
            float m = Master;
            if (m <= 0f || hitClip == null) return;
            float d = Vector3.Distance(at, transform.position);
            var a = FreeOneShot();
            a.transform.position = at; a.spatialBlend = 1f; a.volume = Mathf.Clamp01(0.9f * SoundMath.DistanceGain(d * 0.3f)) * m;
            a.clip = hitClip;
            a.PlayDelayed(Mathf.Min(4f, d / 343f));           // sound travels at 343 m/s
        }

        // The F6 trainer panel: sound on/off and master volume (typeable, 0-100).
        static GUIStyle fieldStyle;
        public static void DrawPanel(GUIStyle label, GUIStyle small, GUIStyle button, float row, int fontSize)
        {
            if (fieldStyle == null) fieldStyle = new GUIStyle(GUI.skin.textField);
            fieldStyle.fontSize = fontSize;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sound: " + (Enabled ? "ON" : "OFF"), button, GUILayout.Width(100f), GUILayout.Height(row))) Enabled = !Enabled;
            int v = Chaukanna.Trainer.NumberEntry.Stepper("field.soundVolume", "Volume %", Volume, 5, 0, 100, label, fieldStyle, button, row, 84f);
            if (v != Volume) Volume = v;
            var e = Instance;
            if (e != null) GUILayout.Label($"{e.ActiveVoices}/{MaxVoices} voices, {e.ActiveBeds} beds", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();
        }
    }
}
