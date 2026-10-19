using System;
using System.Collections.Generic;
using Chaukanna.Scenario;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Net
{
    // Proximity voice for TEAM runs over the LAN (no package, no internet). Hold V to talk to people near you, hold Y for the radio
    // (the whole team hears it, whatever the distance). The microphone (16 kHz mono) is cut into 30 ms frames, mu-law coded and sent
    // to the host as unreliable named messages; the host records the clean frames and relays them to the other seats; each listener
    // plays them from an AudioSource at the speaker's seat, through a ~100 ms jitter buffer. The trainer's degradation (range, radio
    // noise, dropout, muffle) is applied HERE, on the listener's side, so the host's recording stays clean.
    public class VoiceChat : MonoBehaviour
    {
        public static VoiceChat Instance { get; private set; }
        // Testing without a person: replaces the microphone by a generated tone. With TestToneAuto the seat also "presses" V for 1 s
        // out of every 2 s (command line -chkVoiceTone).
        public static bool TestTone, TestToneAuto;
        // Owner rule: voice is recorded in TEAM runs.
        public static bool RecordInTeam = true;

        public CommsSettings Comms => lan.Comms;
        public bool Transmitting { get; private set; }
        public int TxChannel { get; private set; }
        public string MicStatus { get; private set; } = "";
        public int FramesSent, FramesReceived, FramesDroppedByDegrade;
        public VoiceRecorder Recorder { get; private set; }
        public string LastRecordingFolder { get; private set; } = "";

        LanSession lan;
        readonly Dictionary<int, Speaker> speakers = new Dictionary<int, Speaker>();
        System.Random dropRng = new System.Random(1);
        AudioClip micClip;
        int micPos;
        bool micTried;
        readonly List<float> pending = new List<float>();
        ushort seq;
        float tonePhase, toneDebt;
        float runStartReal;
        float nextCommsSend, nextPeriodic;
        bool commsDirty;
        bool wasTransmitting;
        double resamplePos;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; TestTone = TestToneAuto = false; RecordInTeam = true; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-chkVoiceTone") >= 0) { TestTone = true; TestToneAuto = true; }
        }

        // ---- Lifecycle (LanSession).

        public static VoiceChat Begin(LanSession session, bool team)
        {
            End();
            if (!team) return null;
            var v = session.gameObject.AddComponent<VoiceChat>();
            v.lan = session;
            Instance = v;
            v.runStartReal = Time.realtimeSinceStartup;
            int runId = Mathf.Abs(System.Environment.TickCount % 1000000);
            v.Comms.runId = runId;
            v.dropRng = new System.Random(VoiceRecorder.DropSeed(runId, (int)session.LocalClientId));
            if (session.IsHost && RecordInTeam) v.Recorder = new VoiceRecorder(runId, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            if (session.IsHost)
            {
                v.commsDirty = true;
                if (v.Recorder != null)
                {
                    v.Recorder.AddSetting(0f, -1, v.Comms.all);
                    foreach (var s in v.Comms.seats) v.Recorder.AddSetting(0f, s.seat, s.degrade);
                }
            }
            return v;
        }

        public static void End()
        {
            var v = Instance;
            if (v == null) return;
            v.FinishRecording();
            v.StopMic();
            foreach (var s in v.speakers.Values) s.Destroy();
            v.speakers.Clear();
            Instance = null;
            Destroy(v);
        }

        // Host: write the voice files (once per run). Called when the run finishes and again, harmlessly, when everyone leaves.
        public string FinishRecording()
        {
            if (Recorder == null) return LastRecordingFolder;
            var rec = Recorder;
            Recorder = null;
            if (rec.FramesStored == 0 && rec.index.settings.Count == 0) return "";
            string folder = System.IO.Path.Combine(Application.persistentDataPath, "sessions", "voice_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            try
            {
                rec.Write(folder, ContactRegistry.ScenarioTime);
                LastRecordingFolder = folder;
                lan.NoteExternal($"Voice recorded: {rec.FramesStored} frames ({rec.BytesStored / 1024} KB), {rec.FramesLost} lost, in {folder}");
            }
            catch (Exception e) { lan.NoteExternal("Could not write the voice recording: " + e.Message); }
            return LastRecordingFolder;
        }

        void OnDestroy() { StopMic(); if (Instance == this) Instance = null; }

        // ---- Microphone and push-to-talk.

        void StartMic()
        {
            if (micTried || TestTone) return;
            micTried = true;
            if (Microphone.devices.Length == 0) { MicStatus = "No microphone found on this PC."; lan.NoteExternal(MicStatus); return; }
            try
            {
                micClip = Microphone.Start(null, true, 1, VoiceCodec.Rate);
                MicStatus = "Microphone: " + Microphone.devices[0];
            }
            catch (Exception e) { MicStatus = "Microphone could not start: " + e.Message; lan.NoteExternal(MicStatus); }
        }

        void StopMic()
        {
            if (micClip != null) { try { Microphone.End(null); } catch { } micClip = null; }
        }

        // New microphone samples since last time, resampled to 16 kHz when the device ran at another rate.
        void ReadMic(List<float> into)
        {
            if (micClip == null) return;
            int pos = Microphone.GetPosition(null);
            if (pos <= 0 && micPos == 0) return;
            int len = micClip.samples;
            if (pos == micPos) return;
            int rate = micClip.frequency;
            var raw = rate == VoiceCodec.Rate ? into : resampleSource;
            if (pos > micPos) AppendRange(raw, micPos, pos - micPos);
            else { AppendRange(raw, micPos, len - micPos); AppendRange(raw, 0, pos); }
            micPos = pos;
            if (rate == VoiceCodec.Rate) return;
            // Linear interpolation between neighbouring device samples; resamplePos carries over between calls.
            double step = rate / (double)VoiceCodec.Rate;
            while (resamplePos + 1 < resampleSource.Count)
            {
                int i = (int)resamplePos;
                into.Add(Mathf.Lerp(resampleSource[i], resampleSource[i + 1], (float)(resamplePos - i)));
                resamplePos += step;
            }
            int used = (int)resamplePos;
            resampleSource.RemoveRange(0, Mathf.Min(used, resampleSource.Count));
            resamplePos -= used;
        }

        readonly List<float> resampleSource = new List<float>();

        void AppendRange(List<float> into, int offset, int count)
        {
            if (count <= 0) return;
            var tmp = new float[count];
            micClip.GetData(tmp, offset);
            into.AddRange(tmp);
        }
        void MakeTone(List<float> into, float dt)
        {
            toneDebt += dt * VoiceCodec.Rate;
            int n = (int)toneDebt;
            toneDebt -= n;
            // Each seat has its own pitch so two tone talkers can be told apart by ear.
            float f = 220f + 55f * (lan != null ? (int)lan.LocalClientId : 0);
            float inc = 2f * Mathf.PI * f / VoiceCodec.Rate;
            for (int i = 0; i < n; i++)
            {
                into.Add(0.3f * Mathf.Sin(tonePhase));
                tonePhase += inc;
                if (tonePhase > 2f * Mathf.PI) tonePhase -= 2f * Mathf.PI;
            }
        }

        int WantedChannel()
        {
            if (TestToneAuto) return Mathf.FloorToInt(Time.realtimeSinceStartup) % 2 == 0 ? 0 : -1;
            if (ContactRegistry.InputBlocked) return -1;
            var kb = Keyboard.current;
            if (kb == null) return -1;
            if (kb.yKey.isPressed) return 1;
            if (kb.vKey.isPressed) return 0;
            return -1;
        }

        void Update()
        {
            if (lan == null || !lan.InField) return;
            int want = WantedChannel();
            Transmitting = want >= 0;
            TxChannel = Mathf.Max(0, want);

            if (!TestTone) { if (want >= 0 || micClip != null) StartMic(); }
            pending.Clear();
            if (TestTone) { if (want >= 0) MakeTone(pending, Time.deltaTime); else toneDebt = 0f; }
            else ReadMic(pending);

            if (want >= 0)
            {
                if (!wasTransmitting) buffered.Clear();
                buffered.AddRange(pending);
                while (buffered.Count >= VoiceCodec.FrameSamples) SendFrame(want);
            }
            else buffered.Clear();
            wasTransmitting = want >= 0;

            // Host: push changed comms settings to every seat (at most 4 times a second).
            if (lan.IsHost && Time.unscaledTime >= nextPeriodic) { nextPeriodic = Time.unscaledTime + 5f; commsDirty = true; }      // late loaders get the settings within 5 s
            if (lan.IsHost && commsDirty && Time.unscaledTime >= nextCommsSend)
            {
                nextCommsSend = Time.unscaledTime + 0.25f;
                commsDirty = false;
                foreach (var p in lan.roster.players) if (!p.host) lan.SendJsonTo(Msg.Comms, p.clientId, Comms);
            }
            UpdateSpeakers();
        }

        readonly List<float> buffered = new List<float>();
        readonly float[] frameFloats = new float[VoiceCodec.FrameSamples];

        void SendFrame(int channel)
        {
            for (int i = 0; i < VoiceCodec.FrameSamples; i++) frameFloats[i] = buffered[i];
            buffered.RemoveRange(0, VoiceCodec.FrameSamples);
            var data = new byte[VoiceCodec.FrameSamples];
            MuLaw.EncodeFloats(frameFloats, 0, VoiceCodec.FrameSamples, data, 0);
            var f = new VoiceFrame { seat = (ushort)lan.LocalClientId, channel = (byte)channel, seq = seq++, timeMs = (int)((Time.realtimeSinceStartup - runStartReal) * 1000f), data = data };
            FramesSent++;
            if (lan.IsHost) OnFrameAtHost(lan.LocalClientId, f);
            else lan.SendBytes(Msg.Voice, NetworkManager.ServerClientId, VoiceCodec.Pack(f), NetworkDelivery.UnreliableSequenced);
        }

        // ---- Receiving.

        // LanSession's handler for Msg.Voice.
        public static void OnMessage(ulong from, FastBufferReader r)
        {
            r.ReadValueSafe(out int len);
            var bytes = new byte[len];
            r.ReadBytesSafe(ref bytes, len);
            var v = Instance;
            if (v == null || !VoiceCodec.TryUnpack(bytes, out var f)) return;
            if (v.lan.IsHost) { f.seat = (ushort)from; v.OnFrameAtHost(from, f); }      // from a trainee: the sender is who the transport says
            else v.Play(f);                                                              // relayed by the host
        }

        public static void OnCommsMessage(FastBufferReader r)
        {
            r.ReadValueSafe(out string json);
            var v = Instance;
            if (v == null || v.lan.IsHost) return;
            var c = JsonUtility.FromJson<CommsSettings>(json);
            if (c == null) return;
            bool newRun = c.runId != v.Comms.runId;
            v.Comms.runId = c.runId;
            v.Comms.all = c.all;
            v.Comms.seats = c.seats;
            if (newRun) v.dropRng = new System.Random(VoiceRecorder.DropSeed(c.runId, (int)v.lan.LocalClientId));
        }

        // Host: a frame from any seat (its own included): record it clean, log what each listener will hear, relay it, play it here.
        void OnFrameAtHost(ulong from, VoiceFrame f)
        {
            float t = ContactRegistry.ScenarioTime;
            if (Recorder != null)
            {
                var sp = lan.roster.Find(from);
                Recorder.SetSeat((int)from, sp != null ? sp.name : "seat" + from, sp != null ? NetPlayer.RoleName(sp.Role) : "");
                Recorder.AddFrame((int)from, f.channel, f.seq, t, f.data);
                foreach (var p in lan.roster.players)
                {
                    if (p.clientId == from) continue;
                    float dist = lan.DistanceBetween(from, p.clientId);
                    Recorder.AddHeard(t, (int)from, (int)p.clientId, f.channel, dist, Comms.For(p.clientId));
                }
            }
            var packed = VoiceCodec.Pack(f);
            foreach (var p in lan.roster.players)
                if (!p.host && p.clientId != from) lan.SendBytes(Msg.Voice, p.clientId, packed, NetworkDelivery.UnreliableSequenced);
            if (from != lan.LocalClientId) Play(f);
        }

        // Listener side: degrade, then jitter buffer.
        void Play(VoiceFrame f)
        {
            FramesReceived++;
            var d = Comms.For(lan.LocalClientId);
            if (d.ShouldDrop(dropRng)) { FramesDroppedByDegrade++; return; }
            GetSpeaker(f.seat, f.channel).playout.jitter.Push(f.seq, f.data);
            speakerHeard[f.seat] = Time.unscaledTime;
        }

        readonly Dictionary<int, float> speakerHeard = new Dictionary<int, float>();

        Speaker GetSpeaker(int seat, int channel)
        {
            int key = seat * 2 + (channel == 1 ? 1 : 0);
            if (!speakers.TryGetValue(key, out var s))
            {
                s = new Speaker(this, seat, channel == 1);
                speakers[key] = s;
            }
            return s;
        }

        void UpdateSpeakers()
        {
            var cam = Camera.main;
            var d = Comms.For(lan.LocalClientId);
            foreach (var s in speakers.Values)
            {
                Vector3 pos = cam != null ? cam.transform.position : Vector3.zero;
                if (lan.SeatPoses.TryGetValue((ulong)s.seat, out var pose)) pos = pose.position;
                s.go.transform.position = pos;
                float dist = cam != null ? Vector3.Distance(cam.transform.position, pos) : 0f;
                s.Apply(d, dist);
            }
        }

        // Seats currently heard, for the HUD.
        public void HeardNow(List<string> into)
        {
            into.Clear();
            foreach (var kv in speakerHeard)
                if (Time.unscaledTime - kv.Value < 0.4f)
                {
                    var p = lan.roster.Find((ulong)kv.Key);
                    into.Add(p != null ? p.name : "seat " + kv.Key);
                }
        }

        // Host: the trainer changed a value. `target` = ulong.MaxValue for all seats, else a seat id.
        public void CommsChanged(ulong target)
        {
            if (!lan.IsHost) return;
            commsDirty = true;
            if (Recorder != null)
            {
                var d = target == ulong.MaxValue ? Comms.all : Comms.For(target);
                Recorder.AddSetting(ContactRegistry.ScenarioTime, target == ulong.MaxValue ? -1 : (int)target, d);
            }
        }

        // ---- A listener's AudioSource at the speaker's seat.

        class Speaker
        {
            public readonly int seat;
            public readonly bool radio;
            public readonly GameObject go;
            public readonly VoicePlayout playout = new VoicePlayout();
            readonly AudioSource src;
            readonly AudioLowPassFilter low;
            readonly AudioClip clip;

            public Speaker(VoiceChat owner, int seat, bool radio)
            {
                this.seat = seat; this.radio = radio;
                go = new GameObject($"voice {(radio ? "radio" : "nearby")} seat {seat}");
                go.transform.SetParent(owner.transform, false);
                playout.radio = radio;
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                // Proximity is applied as the volume (so it is exactly the tested gain curve); the 3D blend only places the voice.
                src.spatialBlend = radio ? 0f : 1f;
                src.rolloffMode = AudioRolloffMode.Custom;
                src.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Constant(0f, 1f, 1f));
                src.minDistance = 1f;
                src.maxDistance = 100000f;
                low = go.AddComponent<AudioLowPassFilter>();
                low.cutoffFrequency = radio ? VoiceDegrade.RadioBandHigh : 22000f;
                if (radio)
                {
                    var high = go.AddComponent<AudioHighPassFilter>();
                    high.cutoffFrequency = VoiceDegrade.RadioBandLow;
                    var dist = go.AddComponent<AudioDistortionFilter>();
                    dist.distortionLevel = 0.3f;
                }
                clip = AudioClip.Create($"voice {seat}", VoiceCodec.Rate, 1, VoiceCodec.Rate, true, playout.Fill);
                src.clip = clip;
                src.Play();
            }

            public void Apply(VoiceDegrade d, float distance)
            {
                src.volume = d.GainFor(distance, radio);
                low.cutoffFrequency = d.EffectiveCutoff(radio);
                playout.noiseLevel = d.noise;
            }

            public void Destroy()
            {
                if (src != null) src.Stop();
                if (go != null) UnityEngine.Object.Destroy(go);
                if (clip != null) UnityEngine.Object.Destroy(clip);
            }
        }

        // ---- HUD: a red REC mic while transmitting, who is being heard.

        GUIStyle hudStyle;
        readonly List<string> heardList = new List<string>();

        void OnGUI()
        {
            if (lan == null || !lan.InField || PlayerView.HideOverlays || Event.current.type != EventType.Repaint) return;
            HeardNow(heardList);
            if (!Transmitting && heardList.Count == 0) return;
            float ui = PlayerView.Height / 720f;
            if (hudStyle == null) hudStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            hudStyle.fontSize = Mathf.RoundToInt(15f * ui);
            var rect = PlayerView.Rect;
            GUI.BeginGroup(rect);
            float y = rect.height - 70f * ui;
            if (Transmitting)
            {
                var box = new Rect(14f * ui, y, 190f * ui, 28f * ui);
                var old = GUI.color;
                GUI.color = new Color(0.6f, 0.05f, 0.05f, 0.85f);
                GUI.DrawTexture(box, Texture2D.whiteTexture);
                GUI.color = old;
                hudStyle.normal.textColor = Color.white;
                bool blink = Mathf.FloorToInt(Time.unscaledTime * 2f) % 2 == 0;
                GUI.Label(new Rect(box.x + 8f * ui, box.y, box.width, box.height), $"{(blink ? "REC" : "rec")}  MIC  {(TxChannel == 1 ? "RADIO (Y)" : "VOICE (V)")}", hudStyle);
                y -= 26f * ui;
            }
            if (heardList.Count > 0)
            {
                hudStyle.normal.textColor = new Color(0.7f, 1f, 0.7f);
                GUI.Label(new Rect(14f * ui, y, 400f * ui, 26f * ui), "hearing: " + string.Join(", ", heardList), hudStyle);
            }
            GUI.EndGroup();
        }
    }
}
