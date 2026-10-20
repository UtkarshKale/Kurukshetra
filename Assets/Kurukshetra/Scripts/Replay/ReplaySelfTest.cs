using System.Collections.Generic;
using System.IO;
using Kurukshetra.Net;
using Kurukshetra.Trainer;
using UnityEngine;

namespace Kurukshetra.Replay
{
    // Checks for the black box, the 3D replay's data layer, the replay voice rebuild and the clipped replay-map drawing.
    // CLI: Kurukshetra.Replay.ReplaySelfTest.Run()  (Report has the lines and the size numbers).
    public static class ReplaySelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();
        public static string Report => string.Join("\n", lines);

        static void Check(string name, bool ok, string detail = "")
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name + (detail.Length > 0 ? "  [" + detail + "]" : ""));
        }

        // A 60 s synthetic run: 6 seats walking, 20 contacts, a 3000-agent swarm, a bird flock and a dense sky, plus events.
        public static BlackBoxData Synthetic(float seconds, int seats, int contacts, out int swarmBytesHint)
        {
            var d = new BlackBoxData { started = "2026-10-20 10:00:00", mode = "LAN HOST", seed = 77, origin = new Vector3(100f, 50f, 200f), sectorYaw = 30f, spawnRadius = 1000f };
            var rng = new System.Random(5);
            int ticks = Mathf.RoundToInt(seconds * BlackBoxData.Hz);
            var reps = new Vector3[64]; var alive = new bool[64];
            for (int tick = 0; tick <= ticks; tick++)
            {
                float t = tick * BlackBoxData.TickSeconds;
                for (int s = 0; s < seats; s++)
                {
                    d.Seat(s, "Seat" + s, s == 0 ? "COMMANDER" : "SENTRY");
                    d.AddSeat(tick, s, d.origin + new Vector3(s * 3f + t * 1.5f, 1.7f, s * 2f), 30f + 20f * Mathf.Sin(t * 0.5f + s), 5f * Mathf.Sin(t), s == 2 && t > 20f ? 6f : 60f,
                        (byte)((s == 1 && t > 10f && t < 15f ? 1 : 0) | (s == 2 && t > 20f ? 4 : 0)), s == 0 && t > 5f && t < 12f ? 3 : -1);
                }
                for (int c = 0; c < contacts; c++)
                {
                    float spawn = c * (seconds * 0.5f / contacts);
                    float life = seconds * 0.5f;
                    if (t < spawn || t > spawn + life) continue;
                    float u = (t - spawn);
                    d.AddContact(tick, c, c % 5, c % 5, d.origin + new Vector3(800f - u * 20f, 90f - u, 300f + c * 10f), u > 5f, u > 12f ? 1 : 0, u > 20f ? 3 : 0, c == 3 ? 0 : -1);
                }
                if (tick % BlackBoxData.GroupEvery == 0)
                {
                    for (int a = 0; a < 64; a++)
                    {
                        reps[a] = d.origin + new Vector3(900f - t * 12f + a * 2f, 80f + (a % 7), 100f + a * 3f);
                        alive[a] = !(t > 30f && a % 4 == 0);
                    }
                    d.AddGroup(tick, 1, 0, 3000, 3000 - (t > 30f ? 750 : 0), d.origin + new Vector3(900f - t * 12f, 80f, 190f), reps, alive, 64);
                    d.AddGroup(tick, 2, 1, 200, 200, d.origin + new Vector3(500f, 120f, -300f + t), reps, alive, 64);
                    d.AddGroup(tick, 100000, 2, 5000, 4800, d.origin + new Vector3(0f, 100f, 0f), reps, alive, 64);
                }
            }
            // Events added out of order on purpose.
            d.AddEvent(120, BbEventType.Action, 0, 3, "ACTION #3 Jam (hit)");
            d.AddEvent(50, BbEventType.Lock, 0, 3, "LOCK #3");
            d.AddEvent(100, BbEventType.Call, 0, 3, "CALL #3 HostileQuad");
            d.AddEvent(100, BbEventType.Note, -1, -1, "second event at the same tick");
            d.AddEvent(200, BbEventType.Card, -1, -1, "ROE CardC > CardA");
            d.AddEvent(300, BbEventType.Director, -1, -1, "DIRECTOR wave: test");
            d.AddEvent(10, BbEventType.Spawn, -1, 0, "#0 spawn");
            d.AddEvent(400, BbEventType.GroupCall, 1, -1, "GROUP CALL DroneSwarm x40 (right)");
            d.Finish(seconds);
            swarmBytesHint = 0;
            return d;
        }

        static float Tone(byte[] mulawOut, int frames, float hz, float amp)
        {
            for (int i = 0; i < frames * VoiceCodec.FrameSamples; i++)
                mulawOut[i] = MuLaw.Encode((short)(Mathf.Sin(2f * Mathf.PI * hz * i / VoiceCodec.Rate) * amp * 32767f));
            return 0f;
        }

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            string tmp = Path.Combine(Path.GetTempPath(), "chk_replay_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Recorder();
                Drawing();
                Voice(tmp);
            }
            finally
            {
                GuiDraw.DrawHook = null;
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
            lines.Add(failures == 0 ? "ALL PASS" : failures + " FAILED");
            return failures == 0;
        }

        // ---- Black box ----

        static void Recorder()
        {
            var d = Synthetic(60f, 6, 20, out _);
            var st = new BbState();

            Check("recorder: 6 seats at 10 Hz for 60 s = 601 samples each", d.seats.Count == 6 && d.seats[0].s.Count == 601 && d.seats[5].s.Count == 601, d.seats[0].s.Count + " samples");
            d.AddSeat(10, 0, Vector3.zero, 0, 0, 60, 0, -1);
            Check("recorder: a second sample for the same tick is ignored", d.seats[0].s.Count == 601);
            Check("recorder: groups sampled at 5 Hz with at most 64 representative agents", d.groups.Count == 3 && d.groups[0].reps == 64 && d.groups[0].s.Count == 301 && d.groups[0].total == 3000, d.groups[0].s.Count + " samples");
            Check("events: sorted by time after Finish, same-tick events keep their order", d.EventsAreSorted() && d.events[0].tick == 10 && d.events[2].text == "CALL #3 HostileQuad" && d.events[3].text.StartsWith("second"));

            // Size.
            byte[] bytes = d.ToBytes();
            float perMin = bytes.Length / (60f / 60f);
            Check("size: 6 seats + 20 contacts + 3000-agent swarm + flock + dense sky is bounded", bytes.Length < 1024 * 1024, $"{bytes.Length / 1024} KB for 60 s = {perMin / 1024f:F0} KB/min");
            var seatsOnly = new BlackBoxData { origin = d.origin };
            for (int tick = 0; tick <= 600; tick++) for (int s = 0; s < 6; s++) seatsOnly.AddSeat(tick, s, d.origin + new Vector3(tick * 0.1f, 1.7f, s), 0, 0, 60, 0, -1);
            lines.Add($"INFO  size: seats only (6 x 10 Hz) {seatsOnly.ToBytes().Length / 1024} KB/min; one 64-agent group at 5 Hz {SizeOfOneGroup() / 1024} KB/min; 20 contacts alive for half the run: {SizeOfContacts() / 1024} KB/min");
            var big = Synthetic(120f, 6, 60, out _);
            lines.Add($"INFO  size: 2 min, 6 seats, 60 contacts, 3 groups: {big.ToBytes().Length / 1024} KB = {big.ToBytes().Length / 1024 / 2} KB/min");

            // Round trip, binary and header.
            var back = BlackBoxData.FromBytes(bytes);
            bool same = back.seats.Count == 6 && back.contacts.Count == 20 && back.groups.Count == 3 && back.events.Count == d.events.Count
                && back.origin == d.origin && back.sectorYaw == d.sectorYaw && back.seed == 77 && back.mode == "LAN HOST" && back.seats[2].name == "Seat2" && back.seats[0].role == "COMMANDER";
            Check("round trip (memory): header, seats, contacts, groups, events come back", same);
            bool exact = true;
            for (int s = 0; s < 6 && exact; s++)
                for (int i = 0; i < d.seats[s].s.Count; i += 7)
                {
                    var a = d.seats[s].s[i]; var b = back.seats[s].s[i];
                    if (a.tick != b.tick || (a.pos - b.pos).sqrMagnitude > 1e-6f || Mathf.Abs(a.yaw - b.yaw) > 0.01f || Mathf.Abs(a.pitch - b.pitch) > 0.01f || Mathf.Abs(a.fov - b.fov) > 0.011f || a.flags != b.flags || a.lockId != b.lockId) { exact = false; break; }
                }
            for (int c = 0; c < 20 && exact; c++)
                for (int i = 0; i < d.contacts[c].s.Count; i += 5)
                {
                    var a = d.contacts[c].s[i]; var b = back.contacts[c].s[i];
                    if (a.tick != b.tick || (a.pos - b.pos).sqrMagnitude > 1e-6f || a.flags != b.flags || a.call != b.call || a.action != b.action || a.lockSeat != b.lockSeat) { exact = false; break; }
                }
            var ga = d.groups[0].s[100]; var gb = back.groups[0].s[100];
            for (int i = 0; i < 64 && exact; i++) if ((ga.agents[i] - gb.agents[i]).sqrMagnitude > 1e-6f || ga.alive[i] != gb.alive[i]) exact = false;
            Check("round trip: positions (0.25 m), angles, flags, locks, calls, actions and agent alive flags are unchanged", exact);
            bool ev = true;
            for (int i = 0; i < d.events.Count; i++) if (d.events[i].tick != back.events[i].tick || d.events[i].text != back.events[i].text || d.events[i].type != back.events[i].type || d.events[i].seat != back.events[i].seat) ev = false;
            Check("round trip: events identical and in order", ev);

            string tmp = Path.Combine(Path.GetTempPath(), "chk_bb_" + System.Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                d.voiceFolder = "C:\\x\\voice_1";
                string bin = d.Save(tmp, "blackbox_test");
                var loaded = BlackBoxData.Load(bin);
                var header = JsonUtility.FromJson<BbHeader>(File.ReadAllText(Path.Combine(tmp, "blackbox_test.json")));
                Check("round trip (file): .bin + .json header written and loaded", loaded.contacts.Count == 20 && header.contacts == 20 && header.seats.Count == 6 && header.binFile == "blackbox_test.bin" && header.bytes == new FileInfo(bin).Length && header.voiceFolder == "C:\\x\\voice_1" && loaded.voiceFolder == header.voiceFolder, $"{header.bytes / 1024} KB, {header.bytesPerMinute / 1024f:F0} KB/min");
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }

            // Interpolation.
            d.StateAt(3.05f, st);
            var s1 = st.FindSeat(1);
            float expectX = d.origin.x + 3f + 3.05f * 1.5f;
            Check("interpolation: a seat between two samples is at the in-between position", s1 != null && Mathf.Abs(s1.pos.x - expectX) < 0.3f && Mathf.Abs(s1.pos.z - (d.origin.z + 2f)) < 0.3f, $"x {s1?.pos.x:F2} expected {expectX:F2}");
            var wrap = new BlackBoxData();
            wrap.AddSeat(0, 0, Vector3.zero, 359f, 0f, 60f, 0, -1); wrap.AddSeat(1, 0, Vector3.zero, 1f, 0f, 60f, 0, -1);
            wrap.StateAt(0.05f, st);
            Check("interpolation: yaw takes the short way round 359 to 1", Mathf.Abs(Mathf.DeltaAngle(st.FindSeat(0).yaw, 0f)) < 0.2f, st.FindSeat(0).yaw.ToString("F2"));
            d.StateAt(20.0f, st);
            var s2a = st.FindSeat(2).fov;
            d.StateAt(20.5f, st);
            Check("scrub: FOV and flags follow the samples (seat 2 raises the optic at 20 s)", s2a > 50f && st.FindSeat(2).Binoculars && st.FindSeat(2).fov < 10f);

            // Scrub to T: contact lifetime, call/action states, determinism backwards and forwards.
            d.StateAt(0.5f, st); bool c0 = st.FindContact(0) != null; bool c19early = st.FindContact(19) == null;
            d.StateAt(14f, st); var ct = st.FindContact(0);
            Check("scrub: contacts exist only between their first and last sample", c0 && c19early && ct != null);
            Check("scrub: a contact shows the call and action state of that moment", ct != null && ct.call == 1 && ct.action == 0, "at 14 s: call " + ct?.call + " action " + ct?.action);
            d.StateAt(22f, st); ct = st.FindContact(0);
            Check("scrub: ...and the later action at 22 s", ct != null && ct.action == 3 && ct.inView);
            d.StateAt(31f, st); bool gone = st.FindContact(0) == null;
            Check("scrub: a contact is gone after its last sample", gone);
            var posForward = new List<Vector3>();
            foreach (float tt in new[] { 5f, 17.3f, 40f, 59f }) { d.StateAt(tt, st); posForward.Add(st.FindSeat(3).pos); }
            var posBack = new List<Vector3>();
            foreach (float tt in new[] { 59f, 40f, 17.3f, 5f }) { d.StateAt(tt, st); posBack.Add(st.FindSeat(3).pos); }
            bool det = true; for (int i = 0; i < 4; i++) if (posForward[i] != posBack[3 - i]) det = false;
            Check("scrub: the same T gives the same state going forward or back", det);
            d.StateAt(10f, st);
            var grp = st.Group(0);
            Check("scrub: groups give sampled agents, centroid and flying count", st.GroupCount == 3 && grp.n == 64 && grp.count == 3000 && grp.alive[0]);
            d.StateAt(45f, st);
            Check("scrub: group count and alive flags change over the run", st.Group(0).count == 2250 && !st.Group(0).alive[0]);

            Check("events: next / previous skip spawn and despawn rows", Mathf.Approximately(d.NextEventTime(0f), 5f) && Mathf.Approximately(d.NextEventTime(5f), 10f) && Mathf.Approximately(d.PrevEventTime(10.05f), 5f) && d.NextEventTime(100f) < 0f);
            var win = new List<BbEvent>(); d.EventsIn(9.5f, 12.5f, win);
            Check("events: a time window returns the events inside it, in order", win.Count == 3 && win[0].Time <= win[1].Time && win[1].Time <= win[2].Time, win.Count + " events");

            // Interpolated state is cheap enough to run every frame.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) d.StateAt(i * 0.29f, st);
            sw.Stop();
            Check("speed: 200 StateAt calls (6 seats, 20 contacts, 3 groups) well under a frame each", sw.Elapsed.TotalMilliseconds / 200.0 < 2.0, $"{sw.Elapsed.TotalMilliseconds / 200.0:F3} ms each");
            // One person recorded under two seat ids is merged; two real seats with the same name far apart are not.
            var dup = new BlackBoxData();
            dup.Seat(0, "Player 1"); dup.Seat(7, "Player 1"); dup.Seat(9, "Player 1");
            for (int tick = 0; tick < 100; tick++)
            {
                if (tick < 50) dup.AddSeat(tick, 0, new Vector3(1f, 0f, 1f), 0f, 0f, 60f, 0, -1); else dup.AddSeat(tick, 7, new Vector3(1.5f, 0f, 1f), 0f, 0f, 60f, 0, -1);
                dup.AddSeat(tick, 9, new Vector3(500f, 0f, 500f), 0f, 0f, 60f, 0, -1);
            }
            int merged = dup.MergeDuplicateSeats();
            Check("seats: the same person under two ids is merged, a far-away seat of the same name is kept", merged == 1 && dup.seats.Count == 2 && dup.seatAlias.ContainsKey(7) && dup.seats[0].s.Count == 100);
            Check("viewer: no replay is open during the test", !Replay3D.Active);
        }

        static int SizeOfOneGroup()
        {
            var g = new BlackBoxData();
            var reps = new Vector3[64]; var alive = new bool[64];
            for (int tick = 0; tick <= 600; tick += 2) g.AddGroup(tick, 1, 0, 3000, 3000, Vector3.zero, reps, alive, 64);
            return g.ToBytes().Length;
        }

        static int SizeOfContacts()
        {
            var g = new BlackBoxData();
            for (int tick = 0; tick <= 600; tick++) for (int c = 0; c < 20; c++) if (tick > c * 15 && tick < c * 15 + 300) g.AddContact(tick, c, 0, 0, new Vector3(tick, 10, c), true, 0, 0, -1);
            return g.ToBytes().Length;
        }

        // ---- Clipped drawing ----

        static void Drawing()
        {
            var r = new Rect(10f, 10f, 100f, 100f);
            Vector2 a = new Vector2(20, 20), b = new Vector2(60, 90);
            bool inside = GuiDraw.ClipSegment(ref a, ref b, r);
            Check("clip: a segment inside stays as it is", inside && a == new Vector2(20, 20) && b == new Vector2(60, 90));
            a = new Vector2(-50, 60); b = new Vector2(200, 60);
            bool cross = GuiDraw.ClipSegment(ref a, ref b, r);
            Check("clip: a segment crossing the rectangle is cut at both edges", cross && Mathf.Approximately(a.x, 10f) && Mathf.Approximately(b.x, 110f) && Mathf.Approximately(a.y, 60f));
            a = new Vector2(-50, -50); b = new Vector2(-10, 300);
            Check("clip: a segment fully outside is dropped", !GuiDraw.ClipSegment(ref a, ref b, r));
            a = new Vector2(0, 0); b = new Vector2(10, 10);
            Check("clip: a segment touching only the corner counts as a point", GuiDraw.ClipSegment(ref a, ref b, r) && (b - a).magnitude < 0.01f);
            a = new Vector2(50, 50); b = new Vector2(50, 50);
            Check("clip: a zero-length segment inside is kept, outside is dropped", GuiDraw.ClipSegment(ref a, ref b, r));
            a = new Vector2(500, 50); b = new Vector2(500, 50);
            Check("clip: ...", !GuiDraw.ClipSegment(ref a, ref b, r));
            a = new Vector2(50, -40); b = new Vector2(50, 400);
            Check("clip: a vertical segment is cut top and bottom", GuiDraw.ClipSegment(ref a, ref b, r) && Mathf.Approximately(a.y, 10f) && Mathf.Approximately(b.y, 110f));

            var drawn = new List<Rect>();
            GuiDraw.DrawHook = (rect, col) => drawn.Add(rect);
            GuiDraw.Line(new Vector2(-300, 50), new Vector2(900, 70), 2f, Color.green, r);
            bool linesIn = drawn.Count > 10;
            foreach (var q in drawn) if (q.xMin < r.xMin - 0.001f || q.xMax > r.xMax + 0.001f || q.yMin < r.yMin - 0.001f || q.yMax > r.yMax + 0.001f) linesIn = false;
            Check("line: a long line through the map draws only inside it (no stray lines)", linesIn, drawn.Count + " squares");
            drawn.Clear();
            GuiDraw.Line(new Vector2(300, 300), new Vector2(900, 700), 2f, Color.green, r);
            Check("line: a line fully outside draws nothing", drawn.Count == 0);

            bool wedgeIn = true; int wedges = 0;
            foreach (var apex in new[] { new Vector2(60, 60), new Vector2(-40, 50), new Vector2(150, 150), new Vector2(60, 5), new Vector2(10, 10) })
                foreach (float fov in new[] { 4f, 20f, 60f, 120f })
                    foreach (float yaw in new[] { 0f, 45f, 90f, 200f, 359f })
                    {
                        drawn.Clear();
                        GuiDraw.Wedge(apex, yaw, fov * 0.5f, 400f, Color.green, r);
                        wedges++;
                        foreach (var q in drawn) if (q.xMin < r.xMin - 0.001f || q.xMax > r.xMax + 0.001f || q.yMin < r.yMin - 0.001f || q.yMax > r.yMax + 0.001f || q.width <= 0f || q.height <= 0f) wedgeIn = false;
                    }
            Check("wedge: spans stay inside the map for apex inside / outside and FOV 4 to 120", wedgeIn, wedges + " cases");
            drawn.Clear();
            GuiDraw.Wedge(new Vector2(60, 60), 0f, 30f, 200f, Color.green, r);
            Check("wedge: a view cone pointing up from inside the map is filled", drawn.Count > 10);
            drawn.Clear();
            GuiDraw.Wedge(new Vector2(60, 60), 180f, 2f, 5000f, Color.green, r);
            Check("wedge: a very long narrow cone is still clipped", drawn.Count > 0 && drawn.Count < 60);
            GuiDraw.DrawHook = null;
        }

        // ---- Voice ----

        static float EnergyBetween(float[] a, float t0, float t1) => ReplayVoiceBank.Energy(a, Mathf.RoundToInt(t0 * VoiceCodec.Rate), Mathf.RoundToInt(t1 * VoiceCodec.Rate));

        static ReplayVoiceBank Bank(byte[] speakerData, int frames, int channel, float gain, float muffle, float noise, float dropout, int listener = 2)
        {
            var idx = new VoiceIndex { runId = 4, durationSeconds = 10f };
            idx.seats.Add(new VoiceSeat { seatId = 1, name = "A", file = "a.mulaw", frames = frames, bytes = speakerData.Length });
            idx.bursts.Add(new VoiceBurst { speaker = 1, channel = channel, t0 = 1f, frameOffset = 0, frames = frames });
            for (int f = 0; f < frames; f += 5)
                idx.heard.Add(new HeardRow { t = 1f + f * VoiceCodec.FrameSeconds, speaker = 1, listener = listener, channel = channel, distance = 20f, gain = gain, nearRange = 60, fullRange = 10, noise = noise, dropoutPercent = dropout, muffleHz = muffle });
            return new ReplayVoiceBank(idx, new Dictionary<int, byte[]> { { 1, speakerData } });
        }

        static void Voice(string tmp)
        {
            const int frames = 200;                          // 6 s
            var tone = new byte[frames * VoiceCodec.FrameSamples];
            Tone(tone, frames, 1000f, 0.5f);

            var bank = Bank(tone, frames, 0, 0.5f, 22000f, 0f, 0f);
            float[] clean = bank.Render(VoiceMode.Clean, -1, 8f);
            float[] heard = bank.Render(VoiceMode.Heard, 2, 8f);
            float ec = ReplayVoiceBank.Energy(clean), eh = ReplayVoiceBank.Energy(heard);
            int probe = Mathf.RoundToInt(1.5f * VoiceCodec.Rate);
            Check("voice CLEAN: playback is the decoded mu-law of the recording (same samples as MuLaw.Decode)", Mathf.Abs(clean[probe + 17] - MuLaw.Decode(tone[(probe - VoiceCodec.Rate) + 17]) / 32768f) < 1e-6f && ec > 1f && clean.Length >= 9 * VoiceCodec.Rate, $"energy {ec:F0}");
            Check("voice CLEAN: silent outside the burst", EnergyBetween(clean, 0f, 0.9f) == 0f && EnergyBetween(clean, 7.5f, 8f) == 0f);
            Check("voice HEARD: distance gain 0.5 cuts the energy to a quarter", eh < ec * 0.3f && eh > ec * 0.2f, $"heard/clean = {eh / ec:F3}");
            Check("voice HEARD: the speaker does not hear themselves; a listener with no rows hears nothing",
                ReplayVoiceBank.Energy(bank.Render(VoiceMode.Heard, 1, 8f)) == 0f && ReplayVoiceBank.Energy(bank.Render(VoiceMode.Heard, 9, 8f)) == 0f);
            Check("voice HEARD: rebuilding twice gives the same audio (seeded)", Same(heard, bank.Render(VoiceMode.Heard, 2, 8f)));

            // Gain 0 (out of range) = silence.
            Check("voice HEARD: out of range (gain 0) is silent", ReplayVoiceBank.Energy(Bank(tone, frames, 0, 0f, 22000f, 0f, 0f).Render(VoiceMode.Heard, 2, 8f)) == 0f);

            // Dropout.
            var dropBank = Bank(tone, frames, 0, 1f, 22000f, 0f, 40f);
            float[] dropped = dropBank.Render(VoiceMode.Heard, 2, 8f);
            int silent = 0, loud = 0;
            for (int f = 0; f < frames; f++)
            {
                float e = ReplayVoiceBank.Energy(dropped, Mathf.RoundToInt(1f * VoiceCodec.Rate) + f * VoiceCodec.FrameSamples, Mathf.RoundToInt(1f * VoiceCodec.Rate) + (f + 1) * VoiceCodec.FrameSamples);
                if (e == 0f) silent++; else loud++;
            }
            float share = silent / (float)frames;
            Check("voice HEARD: 40 % dropout silences about 40 % of the frames (voice frames go fully silent)", share > 0.25f && share < 0.55f && loud > 0, $"{share * 100f:F0} % silent");
            Check("voice HEARD: dropout lowers the energy against CLEAN", ReplayVoiceBank.Energy(dropped) < ec * 0.85f);
            Check("voice HEARD: 100 % dropout is silent", ReplayVoiceBank.Energy(Bank(tone, frames, 0, 1f, 22000f, 0f, 100f).Render(VoiceMode.Heard, 2, 8f)) == 0f);

            // Muffle (low-pass).
            var hi = new byte[frames * VoiceCodec.FrameSamples]; Tone(hi, frames, 3500f, 0.5f);
            float eHiClean = ReplayVoiceBank.Energy(Bank(hi, frames, 0, 1f, 22000f, 0f, 0f).Render(VoiceMode.Heard, 2, 8f));
            float eHiMuffled = ReplayVoiceBank.Energy(Bank(hi, frames, 0, 1f, 400f, 0f, 0f).Render(VoiceMode.Heard, 2, 8f));
            Check("voice HEARD: a 400 Hz muffle low-pass removes most of a 3.5 kHz tone", eHiMuffled < eHiClean * 0.1f, $"{eHiMuffled / eHiClean:F3}");
            var lo = new byte[frames * VoiceCodec.FrameSamples]; Tone(lo, frames, 200f, 0.5f);
            float eLo = ReplayVoiceBank.Energy(Bank(lo, frames, 0, 1f, 400f, 0f, 0f).Render(VoiceMode.Heard, 2, 8f)), eLoClean = ReplayVoiceBank.Energy(Bank(lo, frames, 0, 1f, 22000f, 0f, 0f).Render(VoiceMode.Heard, 2, 8f));
            Check("voice HEARD: ...but lets a 200 Hz tone through", eLo > eLoClean * 0.5f, $"{eLo / eLoClean:F3}");

            // Radio: 300 to 3000 Hz band and static.
            var radioLow = Bank(lo, frames, 1, 1f, 22000f, 0f, 0f).Render(VoiceMode.Heard, 2, 8f);
            Check("voice HEARD radio: a 200 Hz tone is cut by the 300 Hz high-pass", ReplayVoiceBank.Energy(radioLow) < eLoClean * 0.6f, $"{ReplayVoiceBank.Energy(radioLow) / eLoClean:F3}");
            var radioNoise = Bank(tone, frames, 1, 1f, 22000f, 0.6f, 100f).Render(VoiceMode.Heard, 2, 8f);
            Check("voice HEARD radio: static still plays on lost frames (hiss, no speech)", ReplayVoiceBank.Energy(radioNoise) > 1f && ReplayVoiceBank.Energy(radioNoise) < ec * 0.1f);
            var nearNoise = Bank(tone, frames, 0, 1f, 22000f, 0.6f, 100f).Render(VoiceMode.Heard, 2, 8f);
            Check("voice HEARD nearby: no static, a dropped frame is silent", ReplayVoiceBank.Energy(nearNoise) == 0f);

            // A real recording written by the live recorder reads back.
            var rec = new VoiceRecorder(9, "2026-10-20 10:00:00");
            rec.SetSeat(1, "Anil", "SENTRY");
            var vd = new VoiceDegrade { muffleHz = 22000f };
            for (int f = 0; f < 40; f++)
            {
                var fr = new byte[VoiceCodec.FrameSamples];
                System.Array.Copy(tone, f * VoiceCodec.FrameSamples, fr, 0, fr.Length);
                rec.AddFrame(1, 0, (ushort)f, 2f + f * 0.03f, fr);
                rec.AddHeard(2f + f * 0.03f, 1, 2, 0, 5f, vd);
            }
            string folder = Path.Combine(tmp, "voice_test");
            rec.Write(folder, 10f);
            var loaded = ReplayVoiceBank.Load(folder);
            Check("voice: a VoiceRecorder folder (index.json + .mulaw) loads into the replay", loaded != null && loaded.HasAudio && loaded.index.bursts.Count == 1);
            if (loaded != null)
            {
                float[] lc = loaded.Render(VoiceMode.Clean, -1, 5f), lh = loaded.Render(VoiceMode.Heard, 2, 5f);
                Check("voice: loaded CLEAN has the speech at its time, HEARD at gain 1 matches it", EnergyBetween(lc, 2f, 3.2f) > 1f && EnergyBetween(lc, 0f, 1.9f) == 0f && Mathf.Abs(ReplayVoiceBank.Energy(lh) / ReplayVoiceBank.Energy(lc) - 1f) < 0.02f);
            }
            Check("voice: FindFolder prefers the folder the black box names", ReplayVoiceBank.FindFolder(folder, tmp) == folder && ReplayVoiceBank.FindFolder("", Path.Combine(tmp, "nothing")) == "");
        }

        static bool Same(float[] a, float[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
