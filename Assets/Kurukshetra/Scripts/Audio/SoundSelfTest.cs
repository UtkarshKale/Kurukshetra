using System.Collections.Generic;
using UnityEngine;

namespace Kurukshetra.Audio
{
    // Checks for the procedural sound maths. CLI: Kurukshetra.Audio.SoundSelfTest.Run()
    public static class SoundSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();
        const int SR = 48000;

        public static string Run()
        {
            failures = 0;
            lines.Clear();

            // 1. Blade-pass frequency: quad, 2 blades, 6000 RPM = 200 Hz; FPV 3 blades 20000 RPM = 1000 Hz.
            Check("BPF quad 6000 rpm = 200 Hz", Mathf.Approximately(SoundMath.BladePassHz(2, 6000f), 200f));
            Check("BPF fpv 20000 rpm = 1000 Hz", Mathf.Approximately(SoundMath.BladePassHz(3, 20000f), 1000f));

            // 2. Harmonics: render a quad at 6000 RPM and look for peaks at n x 200 Hz in a DFT (Goertzel scan).
            var s = new DroneSynth();
            s.Init(DroneKind.Quad, 5);
            s.targetRpm = 6000f; s.SnapRpm(); s.gain = 1f; s.SnapGain(); s.cutoffHz = 16000f;
            const int frames = 16384;
            var buf = new float[frames];
            s.Render(buf, frames, 1, SR);        // warm up the filters
            s.Render(buf, frames, 1, SR);
            bool allPeaks = true;
            string peakText = "";
            for (int n = 1; n <= 4; n++)
            {
                float f = 200f * n;
                float peak = BandPeak(buf, f * 0.96f, f * 1.04f), floor = BandPeak(buf, (f + 100f) * 0.98f, (f + 100f) * 1.02f);
                peakText += $" h{n}={peak / Mathf.Max(1e-9f, floor):0.0}x";
                if (peak < 4f * floor) allPeaks = false;
            }
            Check("Quad 6000 rpm: peaks near 200/400/600/800 Hz (>4x the gap level):" + peakText, allPeaks);
            float fund = BandPeak(buf, 192f, 208f), none = BandPeak(buf, 100f, 110f);
            Check("Fundamental is the strongest low tone (BPF, not shaft)", fund > BandPeak(buf, 96f, 104f));

            // 3. RPM rises with speed, climb and turn; a jammed drone loses RPM.
            float hover = SoundMath.Rpm(DroneKind.Quad, 0f, 0f, 0f, 0f), fast = SoundMath.Rpm(DroneKind.Quad, 18f, 0f, 0f, 0f);
            float climbing = SoundMath.Rpm(DroneKind.Quad, 0f, 4f, 0f, 0f), turning = SoundMath.Rpm(DroneKind.Quad, 0f, 0f, 90f, 0f);
            Check($"RPM rises with speed ({hover:0} -> {fast:0}), climb ({climbing:0}), turn ({turning:0})", fast > hover + 500f && climbing > hover && turning > hover);
            Check("FPV runs at far higher blade-pass than a quad", SoundMath.BladePassHz(3, SoundMath.Rpm(DroneKind.Fpv, 20f, 0f, 0f, 0f)) > 3f * SoundMath.BladePassHz(2, fast));
            Check("Jam drops RPM", SoundMath.Rpm(DroneKind.Quad, 10f, 0f, 0f, 1f) < 0.6f * SoundMath.Rpm(DroneKind.Quad, 10f, 0f, 0f, 0f));
            s.targetRpm = 9000f;
            s.Render(buf, 4096, 1, SR);
            float r0 = s.CurrentRpm;
            for (int k = 0; k < 2; k++) s.Render(buf, 12000, 1, SR);
            Check($"Synth RPM glides to its target ({r0:0} -> {s.CurrentRpm:0})", s.CurrentRpm > r0 && Mathf.Abs(s.CurrentRpm - 9000f) < 120f);

            // 4. Distance: gain and low-pass cutoff fall with range; the rendered level really is lower and duller.
            Check("Gain falls with distance", SoundMath.DistanceGain(100f) < SoundMath.DistanceGain(20f) && SoundMath.DistanceGain(800f) < SoundMath.DistanceGain(100f));
            Check("Gain halves per doubling (-6 dB)", Mathf.Approximately(SoundMath.DistanceGain(200f) / SoundMath.DistanceGain(400f), 2f));
            Check("Air cutoff falls with distance", SoundMath.AirCutoffHz(50f) > SoundMath.AirCutoffHz(300f) && SoundMath.AirCutoffHz(300f) > SoundMath.AirCutoffHz(1500f));
            float nearHf = HfEnergy(DroneKind.Quad, 20f), farHf = HfEnergy(DroneKind.Quad, 1200f);
            Check($"Rendered high-frequency energy near {nearHf:0.0000} > 5x far {farHf:0.000000} (3-5 kHz level relative to the 200 Hz tone)", nearHf > 5f * farHf);

            // 5. Voice budget: the nearest (highest-score) N are picked, best first.
            var score = new float[40];
            for (int i = 0; i < score.Length; i++) score[i] = 1f / (1f + ((i * 17) % 40));       // a shuffled set of distinct scores
            var top = new int[12];
            int got = SoundMath.SelectTop(score, score.Length, 12, top);
            bool ordered = got == 12, right = true;
            var sorted = (float[])score.Clone();
            System.Array.Sort(sorted); System.Array.Reverse(sorted);
            for (int k = 0; k < 12; k++) { if (Mathf.Abs(score[top[k]] - sorted[k]) > 1e-6f) right = false; }
            Check("Voice budget picks the 12 highest scores in order", ordered && right);
            Check("Budget with fewer candidates returns all", SoundMath.SelectTop(score, 5, 12, top) == 5);

            // 6. Swarm bed grows with count, falls with distance.
            Check("Bed louder for 3000 than 30 than 1 drones", SoundMath.BedLevel(3000, 600f) > SoundMath.BedLevel(30, 600f) && SoundMath.BedLevel(30, 600f) > SoundMath.BedLevel(1, 600f));
            Check("Bed falls with distance", SoundMath.BedLevel(3000, 2000f) < SoundMath.BedLevel(3000, 300f));
            Check("3000 drones at 800 m beat one drone at 800 m by far", SoundMath.BedLevel(3000, 800f) > 10f * SoundMath.ClassGain(DroneKind.Quad) * SoundMath.DistanceGain(800f));

            // 7. No allocation in the synth routine: 100 buffers of 1024 stereo frames, every kind, jammed too.
            bool noAlloc = true;
            var stereo = new float[2048];
            foreach (DroneKind kind in new[] { DroneKind.Quad, DroneKind.Fpv, DroneKind.FixedWing, DroneKind.Bird })
            {
                var z = new DroneSynth();
                z.Init(kind, 9); z.gain = 0.5f; z.sputter = kind == DroneKind.Quad ? 1f : 0f;
                z.Render(stereo, 1024, 2, SR);      // first call, table already built by Init
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 100; i++) { z.targetRpm = 7000f + i * 10f; z.cutoffHz = 3000f; z.Render(stereo, 1024, 2, SR); }
                long used = System.GC.GetAllocatedBytesForCurrentThread() - before;
                if (used != 0) { noAlloc = false; Note($"{kind}: {used} bytes allocated"); }
            }
            Check("Zero allocations over 100 buffers (all four kinds)", noAlloc);

            // 8. Effects exist, are finite and non-silent; the hum loops.
            var gun = SoundMath.GunBurst(SR); var hit = SoundMath.HitClank(SR); var hum = SoundMath.JammerHum(SR);
            Check("Gunfire, clank and jammer-hum clips are finite and audible", Audible(gun) && Audible(hit) && Audible(hum) && Mathf.Abs(hum[0]) < 0.1f && Mathf.Abs(hum[hum.Length - 1]) < 0.4f);

            Debug.Log("SoundSelfTest: " + (failures == 0 ? "ALL OK" : failures + " FAILED") + "\n" + string.Join("\n", lines));
            return failures == 0 ? "ALL OK (" + lines.Count + " checks)" : failures + " FAILED: " + string.Join(" | ", lines.FindAll(l => l.StartsWith("FAIL")));
        }

        // Writes 3 s of one quad (hover, then speeding up) to a WAV so a person can listen.
        public static string WriteQuadWav(string path, float distance = 30f)
        {
            var s = new DroneSynth();
            s.Init(DroneKind.Quad, 3);
            s.gain = SoundMath.ClassGain(DroneKind.Quad) * SoundMath.DistanceGain(distance) * 3f;     // a bit hot for the file
            s.cutoffHz = SoundMath.AirCutoffHz(distance);
            s.SnapGain();
            var all = new float[SR * 4];
            var chunk = new float[1024];
            for (int o = 0; o < all.Length; o += 1024)
            {
                float t = o / (float)SR;
                s.targetRpm = SoundMath.Rpm(DroneKind.Quad, t < 1f ? 0f : Mathf.Min(18f, (t - 1f) * 9f), 0f, t > 3f ? 60f : 0f, 0f);
                s.Render(chunk, 1024, 1, SR);
                int n = Mathf.Min(1024, all.Length - o);
                System.Array.Copy(chunk, 0, all, o, n);
            }
            WriteWav(path, all, SR);
            return path;
        }

        public static void WriteWav(string path, float[] mono, int sr)
        {
            using (var fs = new System.IO.FileStream(path, System.IO.FileMode.Create))
            using (var w = new System.IO.BinaryWriter(fs))
            {
                int bytes = mono.Length * 2;
                w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
                w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(sr); w.Write(sr * 2); w.Write((short)2); w.Write((short)16);
                w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
                for (int i = 0; i < mono.Length; i++) w.Write((short)(Mathf.Clamp(mono[i], -1f, 1f) * 32000f));
            }
        }

        static float HfEnergy(DroneKind k, float distance)
        {
            var s = new DroneSynth();
            s.Init(k, 4);
            s.gain = 1f; s.SnapGain(); s.cutoffHz = SoundMath.AirCutoffHz(distance);
            var b = new float[8192];
            s.Render(b, 8192, 1, SR);
            s.Render(b, 8192, 1, SR);
            return (BandPeak(b, 3000f, 3000f) + BandPeak(b, 4000f, 4000f) + BandPeak(b, 5000f, 5000f)) / (BandPeak(b, 190f, 210f) + 1e-9f);
        }

        // Largest Goertzel magnitude over a frequency band, 1 Hz steps.
        static float BandPeak(float[] x, float f0, float f1)
        {
            float best = 0f;
            for (float f = f0; f <= f1; f += 1f)
            {
                double w = 2.0 * System.Math.PI * f / SR, c = 2.0 * System.Math.Cos(w), s1 = 0, s2 = 0;
                for (int i = 0; i < x.Length; i++) { double s0 = x[i] + c * s1 - s2; s2 = s1; s1 = s0; }
                float mag = (float)System.Math.Sqrt(s1 * s1 + s2 * s2 - c * s1 * s2);
                if (mag > best) best = mag;
            }
            return best;
        }

        static bool Audible(float[] d)
        {
            float p = 0f;
            foreach (float v in d) { if (float.IsNaN(v) || float.IsInfinity(v)) return false; p = Mathf.Max(p, Mathf.Abs(v)); }
            return p > 0.05f;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "ok   " : "FAIL ") + name);
        }
        static void Note(string text) { lines.Add("note " + text); }
    }
}
