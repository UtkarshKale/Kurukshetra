using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Net
{
    // Checks for proximity voice and the multiplayer extras that need no second PC and no microphone: mu-law, framing, jitter buffer,
    // proximity curve, trainer degradation, playout, recording index, pose codec, clock sync, discovery packets, the height clamp.
    // CLI: Chaukanna.Net.VoiceSelfTest.Run()
    public static class VoiceSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        static byte[] Frame(byte fill) { var b = new byte[VoiceCodec.FrameSamples]; for (int i = 0; i < b.Length; i++) b[i] = fill; return b; }

        public static bool Run()
        {
            failures = 0;
            lines.Clear();

            // --- mu-law.
            int worstOver = 0;
            double worstRel = 0;
            for (int s = -32768; s <= 32767; s++)
            {
                int decoded = MuLaw.Decode(MuLaw.Encode((short)s));
                int err = Mathf.Abs(decoded - Mathf.Clamp(s, -32635, 32635));
                worstOver = Mathf.Max(worstOver, err);
                if (Mathf.Abs(s) > 1000) worstRel = System.Math.Max(worstRel, err / (double)Mathf.Abs(s));
            }
            Check($"mu-law round trip over all 65536 samples: worst error {worstOver} (bound 1100 at full scale), above 1000 worst {worstRel * 100:F2} % of the value (bound 3.5 %)", worstOver <= 1100 && worstRel < 0.035);
            Check("mu-law: zero encodes to the silence byte 0xFF and back to 0", MuLaw.Encode(0) == VoiceCodec.Silence && MuLaw.Decode(0xFF) == 0);
            Check("mu-law keeps the sign", MuLaw.Decode(MuLaw.Encode(5000)) > 0 && MuLaw.Decode(MuLaw.Encode(-5000)) < 0);
            var sine = new float[VoiceCodec.FrameSamples];
            for (int i = 0; i < sine.Length; i++) sine[i] = 0.5f * Mathf.Sin(i * 0.2f);
            var enc = new byte[sine.Length];
            var dec = new float[sine.Length];
            MuLaw.EncodeFloats(sine, 0, sine.Length, enc, 0);
            MuLaw.DecodeFloats(enc, 0, sine.Length, dec, 0);
            float worstF = 0f;
            for (int i = 0; i < sine.Length; i++) worstF = Mathf.Max(worstF, Mathf.Abs(sine[i] - dec[i]));
            Check($"a 30 ms tone survives encode / decode within {worstF:F4} (bound 0.02)", worstF < 0.02f);

            // --- Framing.
            var f = new VoiceFrame { seat = 3, channel = 1, seq = 65535, timeMs = 123456789, data = enc };
            var packed = VoiceCodec.Pack(f);
            Check($"frame packs to {packed.Length} bytes ({VoiceCodec.HeaderBytes} header + {VoiceCodec.FrameSamples}): one datagram", packed.Length == VoiceCodec.HeaderBytes + VoiceCodec.FrameSamples && packed.Length < 1000);
            bool ok = VoiceCodec.TryUnpack(packed, out var g);
            bool same = ok && g.data.Length == enc.Length;
            for (int i = 0; same && i < enc.Length; i++) same = g.data[i] == enc[i];
            Check("frame unpack returns seat, channel, sequence, time and every byte", ok && same && g.seat == 3 && g.channel == 1 && g.seq == 65535 && g.timeMs == 123456789);
            var cut = new byte[packed.Length - 5];
            System.Array.Copy(packed, cut, cut.Length);
            Check("a truncated or empty frame is refused", !VoiceCodec.TryUnpack(cut, out _) && !VoiceCodec.TryUnpack(null, out _) && !VoiceCodec.TryUnpack(new byte[3], out _));
            Check("30 ms frames: 16 kHz mono = 16 000 B/s = 128 kbit/s of audio", VoiceCodec.Rate * 1 == 16000 && Mathf.Approximately(VoiceCodec.FrameSeconds, 0.03f));

            // --- Jitter buffer.
            var jb = new JitterBuffer(0.1f);
            ushort[] arrival = { 12, 10, 11, 13, 14 };
            foreach (var s in arrival) jb.Push(s, Frame((byte)s));
            var order = new List<int>();
            for (int i = 0; i < 5; i++) if (jb.Pop(out var d, out bool lost) && !lost) order.Add(d[0]);
            Check("jitter buffer: frames arriving 12,10,11,13,14 play as 10,11,12,13,14", order.Count == 5 && order[0] == 10 && order[1] == 11 && order[2] == 12 && order[3] == 13 && order[4] == 14);
            Check("jitter buffer waits for ~100 ms (4 frames) before it starts", Waits());
            var jc = new JitterBuffer(0.1f);
            foreach (ushort s in new ushort[] { 1, 2, 4, 5, 6 }) jc.Push(s, Frame((byte)s));
            int lostCount = 0, got = 0;
            for (int i = 0; i < 6; i++) if (jc.Pop(out _, out bool lost)) { if (lost) lostCount++; else got++; }
            Check("a frame that never arrived (3) is played as silence and counted; the rest play", lostCount == 1 && got == 5 && jc.Concealed == 1);
            var jl = new JitterBuffer(0.1f);
            foreach (ushort s in new ushort[] { 20, 21, 22, 23 }) jl.Push(s, Frame(1));
            jl.Pop(out _, out _); jl.Pop(out _, out _);
            jl.Push(20, Frame(1));
            Check("a frame that arrives after its turn has passed is dropped (late = 1)", jl.Late == 1);
            var jw = new JitterBuffer(0.1f);
            foreach (ushort s in new ushort[] { 65535, 0, 65534, 1 }) jw.Push(s, Frame((byte)(s & 0xFF)));
            var wrap = new List<int>();
            for (int i = 0; i < 4; i++) if (jw.Pop(out var d, out bool lost) && !lost) wrap.Add(d[0]);
            Check("sequence numbers wrap 65534, 65535, 0, 1 in order", wrap.Count == 4 && wrap[0] == 254 && wrap[1] == 255 && wrap[2] == 0 && wrap[3] == 1);
            var jo = new JitterBuffer(0.1f, 6);
            for (ushort s = 0; s < 20; s++) jo.Push(s, Frame(1));
            Check("a buffer that falls far behind skips ahead instead of growing (max 6 frames)", jo.Count <= 6 && jo.Overflow > 0);

            // --- Proximity.
            Check("proximity: full volume at 0, 5 and 10 m", Proximity.Gain(0f, 10f, 60f) == 1f && Proximity.Gain(5f, 10f, 60f) == 1f && Proximity.Gain(10f, 10f, 60f) == 1f);
            Check("proximity: silent at 60 m and beyond", Proximity.Gain(60f, 10f, 60f) == 0f && Proximity.Gain(500f, 10f, 60f) == 0f);
            float g20 = Proximity.Gain(20f, 10f, 60f), g40 = Proximity.Gain(40f, 10f, 60f);
            Check($"proximity: falls smoothly between ({g20:F2} at 20 m, {g40:F2} at 40 m)", g20 > g40 && g40 > 0f && g20 < 1f);
            var wide = new VoiceDegrade { nearRange = 200f };
            Check("a longer nearby range (200 m) keeps a voice audible at 100 m that 60 m silenced", wide.GainFor(100f, false) > 0f && new VoiceDegrade().GainFor(100f, false) == 0f);

            // --- Degradation.
            var deg = new VoiceDegrade { dropoutPercent = 20f };
            var rng = new System.Random(77);
            int dropped = 0;
            for (int i = 0; i < 1000; i++) if (deg.ShouldDrop(rng)) dropped++;
            Check($"dropout 20 % over 1000 frames (seeded) dropped {dropped} (expected 150-250)", dropped >= 150 && dropped <= 250);
            var none = new VoiceDegrade();
            int dropped0 = 0, dropped100 = 0;
            var rng0 = new System.Random(5);
            var all = new VoiceDegrade { dropoutPercent = 100f };
            for (int i = 0; i < 1000; i++) { if (none.ShouldDrop(rng0)) dropped0++; if (all.ShouldDrop(rng0)) dropped100++; }
            Check("dropout 0 % drops nothing, 100 % drops everything", dropped0 == 0 && dropped100 == 1000);
            var again = new System.Random(77);
            int dropped2 = 0;
            for (int i = 0; i < 1000; i++) if (deg.ShouldDrop(again)) dropped2++;
            Check("the same seed gives the same drops (so the heard version can be rebuilt)", dropped2 == dropped);
            var muffled = new VoiceDegrade { muffleHz = 1200f };
            Check("muffle sets the low-pass cutoff: 1200 Hz nearby, 1200 Hz on the radio; default radio band tops out at 3000 Hz, default nearby is open (22000)",
                muffled.EffectiveCutoff(false) == 1200f && muffled.EffectiveCutoff(true) == 1200f && new VoiceDegrade().EffectiveCutoff(true) == 3000f && new VoiceDegrade().EffectiveCutoff(false) == 22000f);
            Check("the radio ignores distance (1000 m) and the nearby range setting", muffled.GainFor(1000f, true) == 1f && new VoiceDegrade { nearRange = 5f }.GainFor(1000f, true) == 1f);
            var bad = new VoiceDegrade { noise = 5f, dropoutPercent = 250f, muffleHz = 5f, nearRange = 1f, fullRange = 10f };
            bad.Clamp();
            Check("only impossible values are corrected (noise 0..1, dropout 0..100, a real cutoff, near > full); a 5000 m range is left alone",
                bad.noise == 1f && bad.dropoutPercent == 100f && bad.muffleHz == 20f && bad.nearRange > bad.fullRange && Clamped(5000f) == 5000f);

            // Playout: decode + radio noise.
            var po = new VoicePlayout { radio = true };
            po.noiseLevel = 0f;
            for (ushort s = 0; s < 8; s++) po.jitter.Push(s, enc);
            var outBuf = new float[VoiceCodec.FrameSamples * 3 + 17];
            po.Fill(outBuf);
            float rmsTone = Rms(outBuf, 0, VoiceCodec.FrameSamples);
            Check($"playout decodes the jitter-buffered frames to sound (rms {rmsTone:F2}, odd buffer sizes handled)", rmsTone > 0.2f);
            var pn = new VoicePlayout { radio = true };
            pn.noiseLevel = 0.6f;
            for (ushort s = 0; s < 8; s++) pn.jitter.Push(s, Frame(VoiceCodec.Silence));
            var nb = new float[VoiceCodec.FrameSamples * 2];
            pn.Fill(nb);
            var pv = new VoicePlayout { radio = false };
            pv.noiseLevel = 0.6f;
            for (ushort s = 0; s < 8; s++) pv.jitter.Push(s, Frame(VoiceCodec.Silence));
            var vb = new float[VoiceCodec.FrameSamples * 2];
            pv.Fill(vb);
            Check($"radio noise 0.6 adds static to the radio (rms {Rms(nb, 0, nb.Length):F3}) and leaves nearby voice clean (rms {Rms(vb, 0, vb.Length):F3})", Rms(nb, 0, nb.Length) > 0.05f && Rms(vb, 0, vb.Length) < 0.001f);
            var idle = new VoicePlayout { radio = true };
            idle.noiseLevel = 1f;
            var ib = new float[480];
            idle.Fill(ib);
            Check("with nobody talking the radio is silent (no hiss out of thin air)", Rms(ib, 0, ib.Length) == 0f);

            // Comms settings: all seats, per-seat override, JSON.
            var comms = new CommsSettings { runId = 42 };
            comms.all.dropoutPercent = 10f;
            comms.Edit(2).muffleHz = 900f;
            var viaJson = JsonUtility.FromJson<CommsSettings>(JsonUtility.ToJson(comms));
            Check("comms: a seat override starts from the all-seats set, other seats follow all-seats; it survives JSON; clearing it restores all-seats",
                comms.For(2).dropoutPercent == 10f && comms.For(2).muffleHz == 900f && comms.For(3).muffleHz == 22000f && viaJson.runId == 42 && viaJson.For(2).muffleHz == 900f && viaJson.For(5).dropoutPercent == 10f
                && Cleared(comms));

            // --- Recording.
            var rec = new VoiceRecorder(42, "2026-10-20 10:00:00");
            rec.SetSeat(2, "S-014", "SENTRY");
            rec.SetSeat(1, "Trainer", "COMMANDER");
            rec.AddFrame(2, 0, 100, 5.00f, Frame(10));
            rec.AddFrame(2, 0, 101, 5.03f, Frame(11));
            rec.AddFrame(2, 0, 104, 5.12f, Frame(12));       // 102, 103 lost: silence goes in
            rec.AddFrame(2, 0, 104, 5.12f, Frame(12));       // duplicate: ignored
            rec.AddFrame(2, 0, 105, 7.00f, Frame(13));       // 2 s later: a new burst
            rec.AddFrame(2, 1, 106, 7.03f, Frame(14));       // other channel: a new burst
            rec.AddFrame(1, 1, 7, 7.03f, Frame(20));
            VoiceDegrade heardDeg = new VoiceDegrade { dropoutPercent = 30f, muffleHz = 1500f };
            bool r1 = rec.AddHeard(5.0f, 2, 1, 0, 25f, heardDeg), r2 = rec.AddHeard(5.05f, 2, 1, 0, 26f, heardDeg), r3 = rec.AddHeard(5.2f, 2, 1, 0, 27f, heardDeg), r4 = rec.AddHeard(5.2f, 1, 2, 1, 80f, heardDeg);
            rec.AddSetting(5.5f, -1, heardDeg);
            Check("recorder: 3 bursts for seat 2 + 1 for the trainer; 2 lost frames filled with silence; duplicate ignored; 6 stored", rec.index.bursts.Count == 4 && rec.FramesLost == 2 && rec.FramesStored == 6 && rec.index.bursts[0].frames == 5 && rec.index.bursts[0].lost == 2);
            Check("recorder: heard rows are thinned to about one per 150 ms per speaker and listener", r1 && !r2 && r3 && r4 && rec.index.heard.Count == 3);
            string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chk_voice_selftest_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                rec.Write(folder, 12.5f);
                string json = System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "index.json"));
                var idx = VoiceIndex.FromJson(json);
                var seat2 = idx.seats.Find(s => s.seatId == 2);
                long size = new System.IO.FileInfo(System.IO.Path.Combine(folder, seat2.file)).Length;
                var raw = System.IO.File.ReadAllBytes(System.IO.Path.Combine(folder, seat2.file));
                Check("recording index JSON round trip: seats, bursts, heard rows, settings, run id, duration",
                    idx.runId == 42 && idx.seats.Count == 2 && idx.bursts.Count == 4 && idx.heard.Count == 3 && idx.settings.Count == 1 && Mathf.Approximately(idx.durationSeconds, 12.5f)
                    && idx.sampleRate == 16000 && idx.heard[0].dropoutPercent == 30f && idx.heard[0].muffleHz == 1500f && idx.heard[2].channel == 1 && idx.heard[2].gain == 1f && idx.settings[0].degrade.muffleHz == 1500f);
                Check($"per-speaker raw mu-law file: seat 2 has 7 frames x 480 = {seat2.bytes} bytes on disk ({size}), lost frames are silence 0xFF, bursts index into it",
                    seat2.frames == 7 && size == 7 * 480 && seat2.bytes == size && raw[2 * 480] == VoiceCodec.Silence && raw[0] == 10 && raw[4 * 480] == 12 && idx.bursts[1].frameOffset == 5 && idx.seats.Find(s => s.seatId == 1).file.StartsWith("seat_1_"));
                Check("the drop seed is stored per seat so dropout can be replayed", seat2.dropSeed == VoiceRecorder.DropSeed(42, 2) && seat2.dropSeed != idx.seats.Find(s => s.seatId == 1).dropSeed);
            }
            finally { try { System.IO.Directory.Delete(folder, true); } catch { } }

            // --- Multiplayer extras.
            var pose = new PoseMsg { seat = 4, time = 123.5f, rel = new Vector3(-250.25f, 1850.5f, 4000f), yaw = 271.3f, pitch = -37.5f, fov = 8.25f, flags = 2 };
            var pb = PoseCodec.Pack(pose);
            bool pok = PoseCodec.TryUnpack(pb, out var pu);
            Check($"pose message {pb.Length} bytes: seat, position, yaw, pitch, FOV, talking flag round trip (yaw error {Mathf.Abs(Mathf.DeltaAngle(pose.yaw, pu.yaw)):F3} deg)",
                pok && pb.Length == PoseMsg.Bytes && pu.seat == 4 && pu.rel == pose.rel && Mathf.Abs(Mathf.DeltaAngle(pose.yaw, pu.yaw)) < 0.02f && Mathf.Abs(pose.pitch - pu.pitch) < 0.01f && Mathf.Abs(pose.fov - pu.fov) < 0.01f && pu.Radio && !pu.Voice && Mathf.Approximately(pu.time, 123.5f));
            Check("a short pose message is refused", !PoseCodec.TryUnpack(new byte[5], out _));

            var cs = new ClockSync();
            // Host clock = client clock + 40 s. Four good samples (rtt 20 ms) and one slow one (rtt 800 ms, asymmetric).
            cs.AddSample(100.00f, 100.02f, 140.01f);
            cs.AddSample(101.00f, 101.02f, 141.01f);
            cs.AddSample(102.00f, 102.80f, 142.05f);
            cs.AddSample(103.00f, 103.02f, 143.01f);
            cs.AddSample(104.00f, 104.02f, 144.01f);
            Check($"clock sync: median offset {cs.Offset:F3} s (true 40.000), the slow outlier is ignored; host time now = {cs.HostNow(110f):F2}", cs.Valid && Mathf.Abs(cs.Offset - 40f) < 0.01f && Mathf.Abs(cs.HostNow(110f) - 150f) < 0.01f);
            cs.Reset();
            Check("clock sync reset: invalid until the first sample", !cs.Valid);

            var reply = LanDiscovery.MakeReply("Trainer PC", 7777, NetMode.Team, "in lobby");
            bool dok = LanDiscovery.TryParseReply(reply, "192.168.1.10", out var host);
            Check("discovery: the host's reply parses to name, address, port, mode, state; a request is recognised; junk is refused",
                dok && host.name == "Trainer PC" && host.address == "192.168.1.10" && host.port == 7777 && host.mode == NetMode.Team && host.state == "in lobby"
                && LanDiscovery.IsRequest(System.Text.Encoding.UTF8.GetBytes(LanDiscovery.Request)) && !LanDiscovery.IsRequest(reply)
                && !LanDiscovery.TryParseReply(System.Text.Encoding.UTF8.GetBytes("hello"), "x", out _) && !LanDiscovery.TryParseReply(System.Text.Encoding.UTF8.GetBytes("KRK1!|a|notaport|1|s"), "x", out _));
            Check("discovery targets include the global broadcast", LanDiscovery.BroadcastTargets().Contains(System.Net.IPAddress.Broadcast));

            var origin = new Vector3(0f, 1500f, 0f);
            var list = new List<SnapContact>
            {
                new SnapContact { id = 1, position = new Vector3(100f, -1500f, 50f), yaw = 10f },        // far below the post
                new SnapContact { id = 2, position = new Vector3(-300f, 1900f, 800f), yaw = 200f },      // high mountain
                new SnapContact { id = 3, position = new Vector3(5f, 12f, 5f), yaw = 90f },
            };
            var snapBytes = Snapshot.Pack(10f, origin, list, 0, list.Count);
            var back = new List<SnapContact>();
            Snapshot.Unpack(snapBytes, origin, back);
            Check($"snapshot height range -2048..+2048 m: -1500, +1900 and 12 m come back within 7 cm ({Mathf.Abs(back[0].position.y + 1500f) * 100f:F1}, {Mathf.Abs(back[1].position.y - 1900f) * 100f:F1}, {Mathf.Abs(back[2].position.y - 12f) * 100f:F1} cm)",
                back.Count == 3 && Mathf.Abs(back[0].position.y + 1500f) < 0.07f && Mathf.Abs(back[1].position.y - 1900f) < 0.07f && Mathf.Abs(back[2].position.y - 12f) < 0.07f);

            // Test tone mode flags.
            VoiceChat.TestTone = true; VoiceChat.TestToneAuto = true;
            bool flags = VoiceChat.TestTone && VoiceChat.TestToneAuto;
            VoiceChat.TestTone = false; VoiceChat.TestToneAuto = false;
            Check("test-tone mode is a static flag (VoiceChat.TestTone; -chkVoiceTone on the command line also holds V on a 1 s / 1 s duty cycle)", flags && !VoiceChat.TestTone);

            string summary = $"[VoiceSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static bool Waits()
        {
            var j = new JitterBuffer(0.1f);
            j.Push(1, Frame(1)); j.Push(2, Frame(1));
            bool early = j.Pop(out _, out _);
            j.Push(3, Frame(1)); j.Push(4, Frame(1));
            return !early && j.Pop(out _, out _);
        }

        static float Clamped(float near) { var d = new VoiceDegrade { nearRange = near }; d.Clamp(); return d.nearRange; }

        static bool Cleared(CommsSettings c) { c.ClearOverride(2); return c.For(2).muffleHz == 22000f; }

        static float Rms(float[] b, int from, int n)
        {
            double s = 0;
            for (int i = from; i < from + n; i++) s += b[i] * b[i];
            return (float)System.Math.Sqrt(s / n);
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
