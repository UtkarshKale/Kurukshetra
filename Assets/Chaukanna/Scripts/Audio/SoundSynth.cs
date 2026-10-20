using UnityEngine;

namespace Chaukanna.Audio
{
    public enum DroneKind { Quad = 0, Fpv = 1, FixedWing = 2, Bird = 3 }

    // Pure maths of the procedural sound (no Unity audio objects), so a self-test can check it without playing anything.
    public static class SoundMath
    {
        public const float RefDistance = 10f;           // metres at which a voice has its full level
        public const int TableSize = 2048;

        public static int Blades(DroneKind k) => k == DroneKind.Fpv ? 3 : 2;
        public static int Rotors(DroneKind k) => k == DroneKind.Quad || k == DroneKind.Fpv ? 4 : k == DroneKind.FixedWing ? 1 : 0;
        public static float HoverRpm(DroneKind k) => k == DroneKind.Quad ? 6000f : k == DroneKind.Fpv ? 18000f : k == DroneKind.FixedWing ? 6500f : 360f;
        public static float MaxRpm(DroneKind k) => k == DroneKind.Quad ? 9500f : k == DroneKind.Fpv ? 30000f : k == DroneKind.FixedWing ? 8500f : 600f;
        static float SpeedRef(DroneKind k) => k == DroneKind.Quad ? 20f : k == DroneKind.Fpv ? 35f : k == DroneKind.FixedWing ? 40f : 14f;
        public static float ClassGain(DroneKind k) => k == DroneKind.Quad ? 0.30f : k == DroneKind.Fpv ? 0.35f : k == DroneKind.FixedWing ? 0.40f : 0.10f;

        // Blade-pass frequency in Hz: blades x RPM / 60.
        public static float BladePassHz(int blades, float rpm) => blades * rpm / 60f;

        // Thrust demand from the motion: speed, climb (m/s, + up) and turn rate (deg/s) raise the RPM; a jammed drone loses power.
        // For a bird the "RPM" is wing beats per minute.
        public static float Rpm(DroneKind k, float speed, float climb, float turnRateDegPerSec, float jam01)
        {
            float demand = 0.55f * speed / SpeedRef(k) + 0.25f * Mathf.Max(0f, climb) / 5f + 0.20f * Mathf.Abs(turnRateDegPerSec) / 90f
                         - 0.06f * Mathf.Max(0f, -climb) / 5f;
            float rpm = Mathf.Lerp(HoverRpm(k), MaxRpm(k), Mathf.Clamp01(demand));
            return rpm * Mathf.Lerp(1f, 0.45f, Mathf.Clamp01(jam01));
        }

        // Spherical spreading: -6 dB per doubling beyond the reference distance.
        public static float DistanceGain(float distance) => RefDistance / Mathf.Max(RefDistance, distance);

        // Air absorption: high frequencies fade first. Cutoff of the voice's low-pass at this range.
        public static float AirCutoffHz(float distance) => Mathf.Max(300f, 16000f / (1f + Mathf.Max(0f, distance) / 120f));

        public static float OnePoleCoef(float cutoffHz, float sampleRate) => 1f - Mathf.Exp(-2f * Mathf.PI * Mathf.Min(cutoffHz, sampleRate * 0.45f) / sampleRate);

        // Indices of the `n` highest scores (descending) into `result`; returns how many were chosen. Allocation-free.
        public static int SelectTop(float[] score, int count, int n, int[] result)
        {
            int got = 0;
            for (int i = 0; i < count; i++)
            {
                float s = score[i];
                int pos = got;
                while (pos > 0 && score[result[pos - 1]] < s) pos--;
                if (pos >= n) continue;
                int end = Mathf.Min(got, n - 1);
                for (int j = end; j > pos; j--) result[j] = result[j - 1];
                result[pos] = i;
                if (got < n) got++;
            }
            return got;
        }

        // Many drones add up incoherently (power adds, so amplitude grows with the square root of the count). A bed is a crowd
        // of sources, so it is referenced at 50 m rather than 10 m.
        public static float BedLevel(int count, float distance)
            => count <= 0 ? 0f : Mathf.Min(1f, 0.05f * Mathf.Sqrt(count) * 50f / Mathf.Max(50f, distance));

        static float[][] tables;
        // One shaft revolution. Harmonics of the blade-pass frequency dominate; shaft-rate components are weak (stronger on the
        // fixed wing, whose engine-and-prop sound is rougher).
        public static float[] Table(DroneKind k)
        {
            if (tables == null) tables = new float[3][];
            int t = k == DroneKind.Fpv ? 1 : k == DroneKind.FixedWing ? 2 : 0;
            if (tables[t] != null) return tables[t];
            var tab = new float[TableSize + 1];
            int blades = Blades(k), maxM = blades * (k == DroneKind.Fpv ? 8 : 10);
            for (int m = 1; m <= maxM; m++)
            {
                float a = m % blades == 0 ? Mathf.Pow(m / (float)blades, k == DroneKind.Fpv ? -0.9f : -1.2f)
                                          : (k == DroneKind.FixedWing ? 0.35f : 0.10f) / m;
                float ph = (m * 1.37f) % (2f * Mathf.PI);
                for (int i = 0; i < TableSize; i++) tab[i] += a * Mathf.Sin(2f * Mathf.PI * m * i / TableSize + ph);
            }
            float peak = 0f;
            for (int i = 0; i < TableSize; i++) peak = Mathf.Max(peak, Mathf.Abs(tab[i]));
            for (int i = 0; i < TableSize; i++) tab[i] /= peak;
            tab[TableSize] = tab[0];
            return tables[t] = tab;
        }

        // --- One-shot effects, generated into float arrays (mono) ---

        public static float[] GunBurst(int sr)
        {
            var d = new float[Mathf.RoundToInt(sr * 0.9f)];
            uint r = 12345;
            float lp = 0f;
            for (int shot = 0; shot < 3; shot++)
            {
                int start = Mathf.RoundToInt(sr * 0.085f * shot);
                for (int i = start; i < d.Length; i++)
                {
                    float t = (i - start) / (float)sr;
                    if (t > 0.5f) break;
                    r ^= r << 13; r ^= r >> 17; r ^= r << 5;
                    float n = (r / (float)uint.MaxValue) * 2f - 1f;
                    lp += (n - lp) * Mathf.Lerp(0.9f, 0.12f, Mathf.Clamp01(t * 12f));      // crack first, then it dulls into the boom
                    d[i] += lp * Mathf.Exp(-t * 28f) * 0.8f + Mathf.Sin(2f * Mathf.PI * (95f - 50f * t) * t) * Mathf.Exp(-t * 18f) * 0.6f
                            + lp * Mathf.Exp(-t * 6f) * 0.12f;      // short tail
                }
            }
            Normalise(d, 0.8f);
            return d;
        }

        public static float[] HitClank(int sr)
        {
            var d = new float[Mathf.RoundToInt(sr * 0.4f)];
            uint r = 999;
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)sr;
                r ^= r << 13; r ^= r >> 17; r ^= r << 5;
                float n = (r / (float)uint.MaxValue) * 2f - 1f;
                d[i] = (Mathf.Sin(2f * Mathf.PI * 1130f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 1790f * t) * 0.35f + Mathf.Sin(2f * Mathf.PI * 2470f * t) * 0.25f) * Mathf.Exp(-t * 14f)
                       + n * Mathf.Exp(-t * 60f) * 0.7f + Mathf.Sin(2f * Mathf.PI * 70f * t) * Mathf.Exp(-t * 25f);
            }
            Normalise(d, 0.8f);
            return d;
        }

        // One second, loops without a click (every frequency is a whole number of Hz).
        public static float[] JammerHum(int sr)
        {
            var d = new float[sr];
            for (int i = 0; i < d.Length; i++)
            {
                float t = i / (float)sr, w = 2f * Mathf.PI * t;
                float saw = 0f;
                for (int h = 1; h <= 6; h++) saw += Mathf.Sin(w * 60f * h) / h;
                float warble = Mathf.Sin(w * 1000f + 2.5f * Mathf.Sin(w * 7f));
                d[i] = saw * 0.5f + warble * 0.25f * (0.6f + 0.4f * Mathf.Sin(w * 3f));
            }
            Normalise(d, 0.5f);
            return d;
        }

        static void Normalise(float[] d, float peakTo)
        {
            float p = 1e-6f;
            for (int i = 0; i < d.Length; i++) p = Mathf.Max(p, Mathf.Abs(d[i]));
            float k = peakTo / p;
            for (int i = 0; i < d.Length; i++) d[i] *= k;
        }
    }

    // One drone's motor, synthesised sample by sample. The main thread writes the target fields; the audio thread (or a test)
    // calls Render. No allocation after Init. Float field writes are atomic, so no lock is needed for a prototype.
    public sealed class DroneSynth
    {
        public DroneKind kind;
        public float targetRpm = 6000f;        // wing beats per minute for a bird
        public float gain;                      // already includes distance and master volume
        public float cutoffHz = 16000f;         // air-absorption low-pass
        public float sputter;                   // 0..1: jammed, power cutting in and out
        public float density = 1f;              // bed only: how many birds / drones the sound stands for (flatten flaps)

        int rotors;
        readonly float[] detune = new float[8];
        readonly float[] phase = new float[8];
        float[] table;
        float rpm, g, lp1, lp2, noiseLp, env = 1f, gateTimer, flap, chirpT, chirpLen, chirpNext;
        bool gateOpen = true;
        uint rng;

        public float CurrentRpm => rpm;

        // rotorsOverride / spread > 0 make a "bed": many rotors spread widely in pitch, which blurs into a buzz.
        public void Init(DroneKind k, int seed, int rotorsOverride = -1, float spread = -1f)
        {
            kind = k;
            rng = unchecked((uint)(seed * 2654435761u + 1u));
            if (rng == 0) rng = 1;
            rotors = rotorsOverride > 0 ? Mathf.Min(8, rotorsOverride) : SoundMath.Rotors(k);
            float sp = spread >= 0f ? spread : (k == DroneKind.FixedWing ? 0f : k == DroneKind.Fpv ? 0.010f : 0.012f);
            for (int r = 0; r < 8; r++)
            {
                detune[r] = (Rand() * 2f - 1f) * sp;
                phase[r] = Rand();
            }
            table = k == DroneKind.Bird ? null : SoundMath.Table(k);
            rpm = targetRpm = SoundMath.HoverRpm(k);
            g = 0f; env = 1f; gateOpen = true; flap = Rand(); chirpNext = 0.5f + 3f * Rand(); chirpT = chirpLen = 0f;
        }

        float Rand()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return rng / (float)uint.MaxValue;
        }

        public void SnapRpm() { rpm = targetRpm; }
        public void SnapGain() { g = gain; }

        // Writes (does not add) `frames` frames of interleaved audio.
        public void Render(float[] data, int frames, int channels, int sampleRate)
        {
            float sr = sampleRate;
            float rpmCoef = 1f - Mathf.Exp(-1f / (0.15f * sr));
            float gCoef = 1f - Mathf.Exp(-1f / (0.03f * sr));
            float a = SoundMath.OnePoleCoef(cutoffHz, sr);
            float hover = SoundMath.HoverRpm(kind);
            float noiseLevel = kind == DroneKind.Fpv ? 0.16f : kind == DroneKind.FixedWing ? 0.09f : 0.11f;
            float rotorNorm = rotors > 0 ? 1f / Mathf.Sqrt(rotors) : 1f;
            for (int f = 0; f < frames; f++)
            {
                rpm += (targetRpm - rpm) * rpmCoef;
                g += (gain - g) * gCoef;
                float n = Rand() * 2f - 1f;
                float s;
                if (kind == DroneKind.Bird)
                {
                    noiseLp += (n - noiseLp) * 0.10f;
                    flap += (rpm / 60f) / sr; if (flap >= 1f) flap -= 1f;
                    float beat = Mathf.Sin(2f * Mathf.PI * flap); beat = beat > 0f ? beat * beat * beat * beat : 0f;
                    float depth = 1f / (1f + 0.15f * (density - 1f));
                    s = noiseLp * 3f * (beat * depth + (1f - depth) * 0.5f);
                    chirpNext -= 1f / sr;
                    if (chirpNext <= 0f && chirpLen <= 0f) { chirpLen = 0.09f + 0.08f * Rand(); chirpT = 0f; }
                    if (chirpLen > 0f)
                    {
                        chirpT += 1f / sr;
                        if (chirpT >= chirpLen) { chirpLen = 0f; chirpNext = 1.5f + 4f * Rand() / Mathf.Max(1f, density * 0.2f); }
                        else s += Mathf.Sin(2f * Mathf.PI * (2900f * chirpT + 3000f * chirpT * chirpT)) * Mathf.Sin(Mathf.PI * chirpT / chirpLen) * 0.35f / Mathf.Sqrt(Mathf.Max(1f, density));
                    }
                }
                else
                {
                    float inc = rpm / 60f / sr;
                    s = 0f;
                    for (int r = 0; r < rotors; r++)
                    {
                        float p = phase[r] + inc * (1f + detune[r]);
                        p -= (int)p;
                        phase[r] = p;
                        float idx = p * SoundMath.TableSize;
                        int i0 = (int)idx; if (i0 > 2047) i0 = 2047;
                        float fr = idx - i0;
                        s += table[i0] + (table[i0 + 1] - table[i0]) * fr;
                    }
                    s *= rotorNorm;
                    noiseLp += (n - noiseLp) * 0.22f;
                    s = s * 0.8f + noiseLp * noiseLevel * (0.5f + 0.5f * rpm / hover);
                }
                if (sputter > 0f)
                {
                    gateTimer -= 1f;
                    if (gateTimer <= 0f) { gateOpen = Rand() > 0.5f * sputter; gateTimer = (0.02f + 0.07f * Rand()) * sr; }
                    env += ((gateOpen ? 1f : 0.1f) - env) * 0.01f;
                }
                else env += (1f - env) * 0.01f;
                lp1 += (s * env - lp1) * a;
                lp2 += (lp1 - lp2) * a;
                float o = lp2 * g;
                for (int c = 0; c < channels; c++) data[f * channels + c] = o;
            }
        }
    }
}
