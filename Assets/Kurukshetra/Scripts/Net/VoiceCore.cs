using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Kurukshetra.Net
{
    // The pure parts of proximity voice (no Unity audio, no network): codec, framing, jitter buffer, proximity, degradation,
    // playout and the recording. VoiceChat wires them to the microphone, the LAN and AudioSources; VoiceSelfTest checks them.

    // G.711 mu-law: 16-bit PCM to 8 bits and back. 16 kHz mono = 16 000 bytes a second = 128 kbit/s.
    public static class MuLaw
    {
        const int Bias = 0x84, Clip = 32635;
        static readonly short[] decodeTable = BuildTable();

        static short[] BuildTable()
        {
            var t = new short[256];
            for (int i = 0; i < 256; i++) t[i] = DecodeSlow((byte)i);
            return t;
        }

        public static byte Encode(short pcm)
        {
            int sample = pcm;
            int sign = (sample >> 8) & 0x80;
            if (sign != 0) sample = -sample;
            if (sample > Clip) sample = Clip;
            sample += Bias;
            int exponent = 7;
            for (int mask = 0x4000; (sample & mask) == 0 && exponent > 0; exponent--, mask >>= 1) { }
            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        static short DecodeSlow(byte u)
        {
            u = (byte)~u;
            int sign = u & 0x80, exponent = (u >> 4) & 0x07, mantissa = u & 0x0F;
            int sample = (((mantissa << 3) + Bias) << exponent) - Bias;
            return (short)(sign != 0 ? -sample : sample);
        }

        public static short Decode(byte u) => decodeTable[u];

        public static void EncodeFloats(float[] src, int srcOffset, int count, byte[] dst, int dstOffset)
        {
            for (int i = 0; i < count; i++)
                dst[dstOffset + i] = Encode((short)Mathf.Clamp(Mathf.RoundToInt(src[srcOffset + i] * 32767f), -32768, 32767));
        }

        public static void DecodeFloats(byte[] src, int srcOffset, int count, float[] dst, int dstOffset)
        {
            for (int i = 0; i < count; i++) dst[dstOffset + i] = decodeTable[src[srcOffset + i]] / 32768f;
        }
    }

    // One 30 ms frame on the wire.
    public struct VoiceFrame
    {
        public ushort seat;
        public byte channel;               // 0 = voice (V, nearby), 1 = radio (Y, whole team)
        public ushort seq;
        public int timeMs;                 // sender's clock, for ordering only
        public byte[] data;                // mu-law, FrameSamples bytes
    }

    public static class VoiceCodec
    {
        public const int Rate = 16000, FrameSamples = 480;            // 30 ms
        public const int HeaderBytes = 2 + 1 + 2 + 4 + 2;
        public const float FrameSeconds = FrameSamples / (float)Rate;
        public const byte Silence = 0xFF;                              // mu-law zero

        public static byte[] Pack(VoiceFrame f)
        {
            int n = f.data != null ? f.data.Length : 0;
            var b = new byte[HeaderBytes + n];
            b[0] = (byte)f.seat; b[1] = (byte)(f.seat >> 8);
            b[2] = f.channel;
            b[3] = (byte)f.seq; b[4] = (byte)(f.seq >> 8);
            b[5] = (byte)f.timeMs; b[6] = (byte)(f.timeMs >> 8); b[7] = (byte)(f.timeMs >> 16); b[8] = (byte)(f.timeMs >> 24);
            b[9] = (byte)n; b[10] = (byte)(n >> 8);
            if (n > 0) Buffer.BlockCopy(f.data, 0, b, HeaderBytes, n);
            return b;
        }

        public static bool TryUnpack(byte[] b, out VoiceFrame f)
        {
            f = default;
            if (b == null || b.Length < HeaderBytes) return false;
            int n = b[9] | (b[10] << 8);
            if (n < 0 || n > 4096 || b.Length < HeaderBytes + n) return false;
            f.seat = (ushort)(b[0] | (b[1] << 8));
            f.channel = b[2];
            f.seq = (ushort)(b[3] | (b[4] << 8));
            f.timeMs = b[5] | (b[6] << 8) | (b[7] << 16) | (b[8] << 24);
            f.data = new byte[n];
            Buffer.BlockCopy(b, HeaderBytes, f.data, 0, n);
            return true;
        }
    }

    // Reorders frames that arrive out of order, waits ~100 ms before starting playback, and fills a missing frame with silence.
    // Thread-safe: the network side pushes on the main thread, the audio thread pops.
    public class JitterBuffer
    {
        readonly object gate = new object();
        readonly SortedDictionary<int, byte[]> frames = new SortedDictionary<int, byte[]>();
        readonly int targetFrames, maxFrames;
        int next;                          // unwrapped sequence number due next
        bool started, playing;
        float idleSeconds;
        public int Late, Concealed, Overflow, Played;

        public JitterBuffer(float targetSeconds = 0.1f, int maxFrames = 16)
        {
            targetFrames = Mathf.Max(1, Mathf.CeilToInt(targetSeconds / VoiceCodec.FrameSeconds));
            this.maxFrames = maxFrames;
        }

        public int Count { get { lock (gate) return frames.Count; } }
        public bool Playing { get { lock (gate) return playing; } }

        public void Push(ushort seq, byte[] data)
        {
            lock (gate)
            {
                if (!started) { started = true; next = seq; }
                int abs = next + (short)(seq - (ushort)next);
                if (!playing && abs < next) next = abs;                    // still buffering: an earlier frame arrived late, start from it
                else if (abs < next) { Late++; return; }                   // already played past it
                if (frames.ContainsKey(abs)) return;
                frames[abs] = data;
                if (!playing && frames.Count >= targetFrames) { playing = true; idleSeconds = 0f; }
                while (frames.Count > maxFrames)                           // too far behind: skip ahead
                {
                    int first = -1;
                    foreach (var k in frames.Keys) { first = k; break; }
                    frames.Remove(first);
                    next = first + 1;
                    Overflow++;
                }
            }
        }

        // The next 30 ms. false = nothing to play (the talker has stopped, or playback has not started): the caller outputs silence.
        // A frame that never arrived comes back as silence with lost = true.
        public bool Pop(out byte[] data, out bool lost)
        {
            lock (gate)
            {
                data = null; lost = false;
                if (!playing) return false;
                if (frames.Count == 0)
                {
                    idleSeconds += VoiceCodec.FrameSeconds;
                    if (idleSeconds > 0.3f) { playing = false; started = false; }
                    return false;
                }
                idleSeconds = 0f;
                if (frames.TryGetValue(next, out data)) frames.Remove(next);
                else
                {
                    // Newer frames wait but this one never came: conceal it. If the gap is long, jump to the oldest frame instead.
                    int first = -1;
                    foreach (var k in frames.Keys) { first = k; break; }
                    if (first - next > 8) { next = first; frames.TryGetValue(next, out data); frames.Remove(next); }
                    else { data = null; lost = true; Concealed++; }
                }
                next++;
                Played++;
                return true;
            }
        }
    }

    // Distance to loudness. Full volume inside fullRange, silent beyond maxRange, smooth in between.
    public static class Proximity
    {
        public static float Gain(float distance, float fullRange, float maxRange)
        {
            if (distance <= fullRange) return 1f;
            if (distance >= maxRange || maxRange <= fullRange) return 0f;
            float t = (distance - fullRange) / (maxRange - fullRange);
            return (1f - t) * (1f - t);
        }
    }

    // What the trainer can do to the comms of a seat, live. Applied by the LISTENER's PC after the clean frame has arrived, so the
    // host's recording stays clean and the heard version can be rebuilt from these numbers.
    [Serializable]
    public class VoiceDegrade
    {
        public float nearRange = 60f;       // m: nearby voice is silent beyond this
        public float fullRange = 10f;       // m: nearby voice is at full volume inside this
        public float noise;                 // 0..1: radio static
        public float dropoutPercent;        // 0..100: share of frames lost
        public float muffleHz = 22000f;     // low-pass cutoff; 22000 = off

        public const float RadioBandLow = 300f, RadioBandHigh = 3000f;

        public VoiceDegrade Clone() => (VoiceDegrade)MemberwiseClone();

        // Only physically impossible values are corrected: noise and dropout are shares, a cutoff must be a real frequency.
        public void Clamp()
        {
            fullRange = Mathf.Max(0f, fullRange);
            nearRange = Mathf.Max(fullRange + 0.1f, nearRange);
            noise = Mathf.Clamp01(noise);
            dropoutPercent = Mathf.Clamp(dropoutPercent, 0f, 100f);
            muffleHz = Mathf.Clamp(muffleHz, 20f, 22000f);
        }

        public bool ShouldDrop(System.Random rng) => dropoutPercent > 0f && rng.NextDouble() * 100.0 < dropoutPercent;

        public float EffectiveCutoff(bool radio) => radio ? Mathf.Min(muffleHz, RadioBandHigh) : muffleHz;

        // Radio ignores distance entirely.
        public float GainFor(float distance, bool radio) => radio ? 1f : Proximity.Gain(distance, fullRange, nearRange);

        public bool SameAs(VoiceDegrade o) => o != null && nearRange == o.nearRange && fullRange == o.fullRange && noise == o.noise && dropoutPercent == o.dropoutPercent && muffleHz == o.muffleHz;
    }

    [Serializable]
    public class SeatDegrade
    {
        public int seat;
        public VoiceDegrade degrade = new VoiceDegrade();
    }

    // The host's comms settings: one set for all seats plus optional per-seat overrides. Sent to every trainee when it changes.
    [Serializable]
    public class CommsSettings
    {
        public int runId;
        public VoiceDegrade all = new VoiceDegrade();
        public List<SeatDegrade> seats = new List<SeatDegrade>();

        public VoiceDegrade For(ulong seat)
        {
            foreach (var s in seats) if (s.seat == (int)seat) return s.degrade;
            return all;
        }

        public bool HasOverride(ulong seat) { foreach (var s in seats) if (s.seat == (int)seat) return true; return false; }

        // target = ulong.MaxValue edits the "all seats" set; otherwise that seat's own set (made from the all-seats set on first edit).
        public VoiceDegrade Edit(ulong target)
        {
            if (target == ulong.MaxValue) return all;
            foreach (var s in seats) if (s.seat == (int)target) return s.degrade;
            var n = new SeatDegrade { seat = (int)target, degrade = all.Clone() };
            seats.Add(n);
            return n.degrade;
        }

        public void ClearOverride(ulong seat) => seats.RemoveAll(s => s.seat == (int)seat);
    }

    // Turns jitter-buffered mu-law frames into samples for an AudioClip's PCM callback (runs on the audio thread).
    public class VoicePlayout
    {
        public readonly JitterBuffer jitter = new JitterBuffer();
        public volatile float noiseLevel;          // 0..1, radio only
        public bool radio;
        readonly System.Random rng = new System.Random(12345);
        readonly float[] frame = new float[VoiceCodec.FrameSamples];
        int frameLeft, frameAt;
        bool frameIsVoice;

        public void Fill(float[] dst)
        {
            int i = 0;
            while (i < dst.Length)
            {
                if (frameLeft == 0)
                {
                    if (jitter.Pop(out var bytes, out bool lost) && !lost && bytes != null)
                    {
                        int n = Mathf.Min(bytes.Length, frame.Length);
                        MuLaw.DecodeFloats(bytes, 0, n, frame, 0);
                        for (int k = n; k < frame.Length; k++) frame[k] = 0f;
                        frameIsVoice = true;
                    }
                    else
                    {
                        Array.Clear(frame, 0, frame.Length);
                        frameIsVoice = jitter.Playing;       // a lost frame inside a talk burst still carries the radio hiss
                    }
                    if (radio && noiseLevel > 0f && frameIsVoice)
                        for (int k = 0; k < frame.Length; k++) frame[k] += ((float)rng.NextDouble() * 2f - 1f) * noiseLevel * 0.25f;
                    frameLeft = frame.Length;
                    frameAt = 0;
                }
                int take = Mathf.Min(frameLeft, dst.Length - i);
                Array.Copy(frame, frameAt, dst, i, take);
                i += take; frameAt += take; frameLeft -= take;
            }
        }
    }

    // ---- Recording index (JSON) for the replay phase.

    [Serializable]
    public class VoiceSeat
    {
        public int seatId;
        public string name = "", role = "", file = "";
        public int frames, bytes, dropSeed;
    }

    // A run of consecutive frames from one speaker on one channel; missing frames are filled with silence so the file is
    // time-aligned inside a burst. t0 = host scenario time (seconds) of its first frame.
    [Serializable]
    public class VoiceBurst
    {
        public int speaker, channel;
        public float t0;
        public int frameOffset, frames, lost;
    }

    // What one listener heard from one speaker around time t: with the clean frames this rebuilds the degraded version.
    [Serializable]
    public class HeardRow
    {
        public float t;
        public int speaker, listener, channel;
        public float distance, gain, nearRange, fullRange, noise, dropoutPercent, muffleHz;
    }

    [Serializable]
    public class SettingsChange
    {
        public float t;
        public int seat;                    // -1 = all seats
        public VoiceDegrade degrade = new VoiceDegrade();
    }

    [Serializable]
    public class VoiceIndex
    {
        public int version = 1;
        public string started = "", codec = "mulaw8", mode = "TEAM";
        public int sampleRate = VoiceCodec.Rate, frameSamples = VoiceCodec.FrameSamples, runId;
        public float durationSeconds;
        public List<VoiceSeat> seats = new List<VoiceSeat>();
        public List<VoiceBurst> bursts = new List<VoiceBurst>();
        public List<HeardRow> heard = new List<HeardRow>();
        public List<SettingsChange> settings = new List<SettingsChange>();

        public string ToJson() => JsonUtility.ToJson(this, true);
        public static VoiceIndex FromJson(string json) => JsonUtility.FromJson<VoiceIndex>(json);
    }

    // Host: keeps every speaker's clean frames as they arrive, plus the heard parameters; writes them when the run ends.
    public class VoiceRecorder
    {
        class Track
        {
            public string name = "", role = "";
            public MemoryStream data = new MemoryStream();
            public int frames;
            public VoiceBurst burst;
            public int lastSeq, lastChannel = -1;
            public float lastT;
        }

        readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>();
        public readonly VoiceIndex index = new VoiceIndex();
        public int FramesStored, FramesLost;
        public long BytesStored;
        readonly Dictionary<long, float> lastHeard = new Dictionary<long, float>();
        public int RunId => index.runId;

        public VoiceRecorder(int runId, string started)
        {
            index.runId = runId;
            index.started = started;
        }

        public void SetSeat(int seat, string name, string role)
        {
            var t = Get(seat);
            t.name = name; t.role = role;
        }

        Track Get(int seat)
        {
            if (!tracks.TryGetValue(seat, out var t)) { t = new Track(); tracks[seat] = t; }
            return t;
        }

        // `t` = host scenario time in seconds when the frame arrived.
        public void AddFrame(int seat, int channel, ushort seq, float t, byte[] data)
        {
            var tr = Get(seat);
            int gap = tr.burst != null ? (short)(seq - (ushort)tr.lastSeq) : 0;
            bool sameBurst = tr.burst != null && channel == tr.lastChannel && t - tr.lastT <= 0.5f;
            if (sameBurst && gap <= 0) return;                          // duplicate / stale
            bool newBurst = !sameBurst || gap > 20;
            if (newBurst)
            {
                tr.burst = new VoiceBurst { speaker = seat, channel = channel, t0 = t, frameOffset = tr.frames };
                index.bursts.Add(tr.burst);
            }
            else
            {
                for (int i = 1; i < gap; i++)                           // silence for frames that never arrived
                {
                    for (int k = 0; k < VoiceCodec.FrameSamples; k++) tr.data.WriteByte(VoiceCodec.Silence);
                    tr.frames++; tr.burst.frames++; tr.burst.lost++; FramesLost++;
                }
            }
            tr.data.Write(data, 0, data.Length);
            tr.frames++; tr.burst.frames++;
            tr.lastSeq = seq; tr.lastChannel = channel; tr.lastT = t;
            FramesStored++;
            BytesStored += data.Length;
        }

        // One row about every 150 ms per (speaker, listener) while a speaker talks.
        public bool AddHeard(float t, int speaker, int listener, int channel, float distance, VoiceDegrade d)
        {
            long key = ((long)speaker << 32) | (uint)listener;
            if (lastHeard.TryGetValue(key, out float last) && t - last < 0.15f && last <= t) return false;
            lastHeard[key] = t;
            index.heard.Add(new HeardRow
            {
                t = t, speaker = speaker, listener = listener, channel = channel, distance = distance,
                gain = d.GainFor(distance, channel == 1), nearRange = d.nearRange, fullRange = d.fullRange, noise = d.noise,
                dropoutPercent = d.dropoutPercent, muffleHz = d.EffectiveCutoff(channel == 1),
            });
            return true;
        }

        public void AddSetting(float t, int seat, VoiceDegrade d) => index.settings.Add(new SettingsChange { t = t, seat = seat, degrade = d.Clone() });

        // Writes seat_<id>_<name>.mulaw per speaker and index.json. Returns the folder.
        public string Write(string folder, float duration)
        {
            Directory.CreateDirectory(folder);
            index.durationSeconds = duration;
            index.seats.Clear();
            foreach (var kv in tracks)
            {
                string safe = Sanitize(kv.Value.name);
                string file = $"seat_{kv.Key}_{safe}.mulaw";
                if (kv.Value.frames > 0) File.WriteAllBytes(Path.Combine(folder, file), kv.Value.data.ToArray());
                index.seats.Add(new VoiceSeat
                {
                    seatId = kv.Key, name = kv.Value.name, role = kv.Value.role, file = kv.Value.frames > 0 ? file : "",
                    frames = kv.Value.frames, bytes = (int)kv.Value.data.Length, dropSeed = DropSeed(index.runId, kv.Key),
                });
            }
            File.WriteAllText(Path.Combine(folder, "index.json"), index.ToJson());
            return folder;
        }

        public static int DropSeed(int runId, int listener) => runId * 7919 + listener;

        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "seat";
            var sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.ToString();
        }
    }
}
