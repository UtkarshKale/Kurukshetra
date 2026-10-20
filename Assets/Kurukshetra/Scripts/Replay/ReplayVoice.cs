using System;
using System.Collections.Generic;
using System.IO;
using Kurukshetra.Net;
using UnityEngine;

namespace Kurukshetra.Replay
{
    public enum VoiceMode { Off, Clean, Heard }

    // Voice for the replay. CLEAN = what each speaker said (the host's recorded mu-law, decoded). HEARD = what one listener heard,
    // rebuilt from the clean audio and the logged "heard" rows: distance gain, seeded frame dropout, muffle low-pass, and for the
    // radio the 300-3000 Hz band and static. A prototype-level approximation: the chain is the same kind of processing the live path
    // applied, but not sample-identical (the live dropout used a running random stream; this uses a hash of the frame number so a
    // scrub gives the same result every time).
    public class ReplayVoiceBank
    {
        public const int Rate = VoiceCodec.Rate, Frame = VoiceCodec.FrameSamples;
        public readonly VoiceIndex index;
        public readonly Dictionary<int, byte[]> mulaw;
        public string folder = "";

        public ReplayVoiceBank(VoiceIndex index, Dictionary<int, byte[]> mulaw) { this.index = index; this.mulaw = mulaw; }

        public bool HasAudio => index != null && index.bursts.Count > 0 && mulaw.Count > 0;
        public int SeatCount => index != null ? index.seats.Count : 0;

        public static ReplayVoiceBank Load(string folder)
        {
            try
            {
                string idx = Path.Combine(folder, "index.json");
                if (!File.Exists(idx)) return null;
                var index = VoiceIndex.FromJson(File.ReadAllText(idx));
                var files = new Dictionary<int, byte[]>();
                foreach (var s in index.seats)
                {
                    if (string.IsNullOrEmpty(s.file)) continue;
                    string p = Path.Combine(folder, s.file);
                    if (File.Exists(p)) files[s.seatId] = File.ReadAllBytes(p);
                }
                return new ReplayVoiceBank(index, files) { folder = folder };
            }
            catch (Exception e) { Debug.LogWarning("[Replay] Voice folder could not be read: " + e.Message); return null; }
        }

        // The recorded folder named by the black box, else the newest voice_* folder in the sessions folder from the last 6 hours.
        public static string FindFolder(string hint, string sessionsFolder)
        {
            if (!string.IsNullOrEmpty(hint) && File.Exists(Path.Combine(hint, "index.json"))) return hint;
            try
            {
                if (!Directory.Exists(sessionsFolder)) return "";
                string best = ""; DateTime bestTime = DateTime.UtcNow.AddHours(-6);
                foreach (var d in Directory.GetDirectories(sessionsFolder, "voice_*"))
                {
                    var t = Directory.GetLastWriteTimeUtc(d);
                    if (t > bestTime && File.Exists(Path.Combine(d, "index.json"))) { best = d; bestTime = t; }
                }
                return best;
            }
            catch { return ""; }
        }

        // One-pole filter memory for a burst.
        public struct FilterState { public float lp, hpY, hpX; }

        static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352dU; x ^= x >> 15; x *= 0x846ca68bU; x ^= x >> 16;
            return x;
        }

        public static bool FrameDropped(int runId, int listener, int speaker, int frameIndex, float percent)
        {
            if (percent <= 0f) return false;
            uint h = Hash((uint)(VoiceRecorder.DropSeed(runId, listener) * 31 + speaker * 7919 + frameIndex * 104729));
            return h / (double)uint.MaxValue * 100.0 < percent;
        }

        // Decodes one frame of mu-law and applies the listener's chain; adds the result into dst. `count` samples from src[offset..].
        public static void RenderFrame(byte[] src, int offset, int count, float gain, bool radio, float muffleHz, float noise, bool dropped,
            ref FilterState st, uint noiseSeed, float[] dst, int dstOffset)
        {
            float lpA = muffleHz >= 7900f ? 1f : 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Max(20f, muffleHz) / Rate);
            float hpA = radio ? Mathf.Exp(-2f * Mathf.PI * VoiceDegrade.RadioBandLow / Rate) : 0f;
            uint rs = noiseSeed | 1u;
            for (int i = 0; i < count; i++)
            {
                int di = dstOffset + i;
                if (di < 0 || di >= dst.Length) continue;
                float x = dropped || offset + i >= src.Length ? 0f : MuLaw.Decode(src[offset + i]) / 32768f;
                float y = x;
                if (radio) { float hp = hpA * (st.hpY + x - st.hpX); st.hpX = x; st.hpY = hp; y = hp; }
                if (lpA < 1f) { st.lp += lpA * (y - st.lp); y = st.lp; }
                y *= gain;
                if (radio && noise > 0f)
                {
                    rs ^= rs << 13; rs ^= rs >> 17; rs ^= rs << 5;
                    y += ((rs & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f) * noise * 0.25f;
                }
                dst[di] += y;
            }
        }

        public static float Energy(float[] a, int from = 0, int to = -1)
        {
            if (to < 0 || to > a.Length) to = a.Length;
            double e = 0;
            for (int i = from; i < to; i++) e += a[i] * (double)a[i];
            return (float)e;
        }

        // The listener's heard rows from `speaker`, in time order.
        List<HeardRow> RowsFor(int speaker, int listener)
        {
            var list = new List<HeardRow>();
            foreach (var h in index.heard) if (h.speaker == speaker && h.listener == listener) list.Add(h);
            list.Sort((a, b) => a.t.CompareTo(b.t));
            return list;
        }

        // A mono clip's samples covering 0..duration s at 16 kHz. listener is ignored for CLEAN (every speaker, mixed).
        public float[] Render(VoiceMode mode, int listener, float duration)
        {
            int total = Mathf.Max(Frame, Mathf.CeilToInt((duration + 1f) * Rate));
            var outBuf = new float[total];
            if (mode == VoiceMode.Off || !HasAudio) return outBuf;
            var rowCache = new Dictionary<int, List<HeardRow>>();
            foreach (var b in index.bursts)
            {
                if (!mulaw.TryGetValue(b.speaker, out var data)) continue;
                bool radio = b.channel == 1;
                List<HeardRow> rows = null;
                if (mode == VoiceMode.Heard)
                {
                    if (b.speaker == listener) continue;                       // you do not hear yourself over the air
                    if (!rowCache.TryGetValue(b.speaker, out rows)) { rows = RowsFor(b.speaker, listener); rowCache[b.speaker] = rows; }
                    if (rows.Count == 0) continue;                              // never in range of this listener
                }
                var st = new FilterState();
                int start = Mathf.RoundToInt(b.t0 * Rate);
                for (int f = 0; f < b.frames; f++)
                {
                    int byteOffset = (b.frameOffset + f) * Frame;
                    if (byteOffset >= data.Length) break;
                    int dstOffset = start + f * Frame;
                    if (dstOffset >= total) break;
                    if (mode == VoiceMode.Clean)
                    {
                        var none = new FilterState();
                        RenderFrame(data, byteOffset, Mathf.Min(Frame, data.Length - byteOffset), 1f, false, 22000f, 0f, false, ref none, 0, outBuf, dstOffset);
                        continue;
                    }
                    float ft = b.t0 + f * VoiceCodec.FrameSeconds;
                    HeardRow row = null;
                    for (int r = 0; r < rows.Count; r++) { if (rows[r].t <= ft) row = rows[r]; else { if (row == null && rows[r].t - ft < 0.5f) row = rows[r]; break; } }
                    if (row == null || ft - row.t > 1f) row = null;
                    float gain = row != null ? row.gain : 0f;
                    bool drop = row != null && FrameDropped(index.runId, listener, b.speaker, b.frameOffset + f, row.dropoutPercent);
                    if (row == null) continue;
                    RenderFrame(data, byteOffset, Mathf.Min(Frame, data.Length - byteOffset), gain, radio, row.muffleHz, row.noise, drop, ref st,
                        (uint)(b.speaker * 7919 + b.frameOffset + f + listener * 104729), outBuf, dstOffset);
                }
            }
            return outBuf;
        }

        // Talk bars for the timeline: (start, end, speaker) per burst.
        public List<Vector3> Bars()
        {
            var list = new List<Vector3>();
            if (index == null) return list;
            foreach (var b in index.bursts) list.Add(new Vector3(b.t0, b.t0 + b.frames * VoiceCodec.FrameSeconds, b.speaker));
            return list;
        }
    }
}
