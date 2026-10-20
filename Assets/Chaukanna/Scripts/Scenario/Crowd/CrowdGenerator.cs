using System;
using Unity.Mathematics;

namespace Chaukanna.Scenario
{
    // What the generator needs to know about one crowd class (taken from its ContactDefinition).
    public struct CrowdClassSpec
    {
        public float size, speedMin, speedMax, altMin, altMax;
        public int variants;
        public CrowdClassSpec(float size, float speedMin, float speedMax, float altMin, float altMax, int variants = 1)
        { this.size = size; this.speedMin = speedMin; this.speedMax = speedMax; this.altMin = altMin; this.altMax = altMax; this.variants = variants; }
    }

    // Builds the crowd of a run from the seed. Mix (shares of the crowd count): own ISR orbits 10 %, delivery corridors
    // 45 %, survey grids 10 %, station keeping 5 %, clusters 15 %, bird flocks 15 %. A System.Random of its own
    // (seed ^ 0x0C0FFEE), so adding a crowd never changes the plan of the hostiles and decoys. Every random value is drawn
    // whether or not it is used, so changing a percentage never moves the rest of the crowd.
    public static class CrowdGenerator
    {
        public static readonly float[] Shares = { 0.10f, 0.45f, 0.10f, 0.05f, 0.15f, 0.15f };   // indexed by CrowdPattern
        public const int MaxVariants = 8;
        const byte Own = (byte)ContactClass.FriendlyISR, Civil = (byte)ContactClass.CivilianQuad, Bird = (byte)ContactClass.Bird;

        // Specs for the three crowd classes when no definitions are at hand (self-tests).
        public static CrowdClassSpec[] DefaultSpecs()
        {
            var s = new CrowdClassSpec[5];
            s[Own] = new CrowdClassSpec(4f, 18f, 26f, 90f, 150f);
            s[Civil] = new CrowdClassSpec(2.2f, 8f, 14f, 25f, 70f);
            s[Bird] = new CrowdClassSpec(1.8f, 8f, 14f, 15f, 60f);
            return s;
        }

        // How many agents each pattern gets: largest-remainder rounding, so the parts always add up to count.
        public static int[] Counts(int count)
        {
            var n = new int[6];
            var rem = new double[6];
            int sum = 0;
            for (int i = 0; i < 6; i++)
            {
                double exact = count * (double)Shares[i];
                n[i] = (int)Math.Floor(exact);
                rem[i] = exact - n[i];
                sum += n[i];
            }
            while (sum < count)
            {
                int best = 0;
                for (int i = 1; i < 6; i++) if (rem[i] > rem[best]) best = i;
                n[best]++;
                rem[best] = -1;
                sum++;
            }
            return n;
        }

        public static CrowdAgent[] Generate(int seed, int count, float2 post, float radius, float duration, float noIffPercent, CrowdClassSpec[] spec)
        {
            count = Math.Max(0, count);
            var agents = new CrowdAgent[count];
            if (count == 0) return agents;
            var rng = new System.Random(seed ^ 0x0C0FFEE);
            var n = Counts(count);
            radius = Math.Max(radius, 100f);
            duration = Math.Max(duration, 1f);
            int k = 0;

            // Own ISR orbits.
            for (int i = 0; i < n[0]; i++)
            {
                var a = Base(rng, CrowdPattern.Orbit, Own, spec, duration, noIffPercent);
                a.centre = Centre(rng, post, radius * 0.8f, 0.05f, spec[Own], out _);
                a.radius = Lerp(rng, 120f, 350f);
                a.speed = Lerp(rng, spec[Own].speedMin, spec[Own].speedMax);
                a.dir = Dir(rng);
                a.phase = Phase(rng);
                Window(rng, duration, ref a);
                agents[k++] = a;
            }

            // Delivery corridors: 2 to 4 per run, most of the civil traffic lives in them.
            int corridors = 2 + rng.Next(3);
            var cc = new CrowdAgent[corridors];
            for (int c = 0; c < corridors; c++)
            {
                var a = new CrowdAgent();
                a.centre = Centre(rng, post, radius * 0.6f, 0.05f, spec[Civil], out _);
                a.centre.y = Lerp(rng, 40f, 80f);
                a.axis = Axis(rng);
                a.length = Math.Min(Lerp(rng, 500f, 1400f), radius * 1.6f);
                cc[c] = a;
            }
            for (int i = 0; i < n[1]; i++)
            {
                var a = Base(rng, CrowdPattern.Corridor, Civil, spec, duration, noIffPercent);
                var cor = cc[rng.Next(corridors)];
                int lane = rng.Next(3);
                a.centre = cor.centre; a.axis = cor.axis; a.length = cor.length;
                a.offset = new float3((lane - 1) * CrowdMath.LaneSpacing, 0f, 0f);
                a.speed = Lerp(rng, spec[Civil].speedMin, spec[Civil].speedMax);
                a.phase = (float)rng.NextDouble() * 2f * a.length;
                Window(rng, duration, ref a);
                agents[k++] = a;
            }

            // Survey / agri grids, half own, half civil.
            for (int i = 0; i < n[2]; i++)
            {
                byte cls = (i & 1) == 0 ? Own : Civil;
                var a = Base(rng, CrowdPattern.Grid, cls, spec, duration, noIffPercent);
                a.centre = Centre(rng, post, radius * 0.8f, 0.05f, spec[cls], out _);
                a.axis = Axis(rng);
                a.lanes = 4 + rng.Next(7);
                a.radius = Lerp(rng, 25f, 40f);
                a.length = Lerp(rng, 200f, 500f);
                a.speed = Lerp(rng, spec[cls].speedMin, spec[cls].speedMax);
                a.phase = (float)rng.NextDouble() * (a.lanes * a.length);
                Window(rng, duration, ref a);
                agents[k++] = a;
            }

            // Station keeping (photography, inspection).
            for (int i = 0; i < n[3]; i++)
            {
                var a = Base(rng, CrowdPattern.Station, Civil, spec, duration, noIffPercent);
                a.centre = Centre(rng, post, radius * 0.9f, 0.05f, spec[Civil], out _);
                a.axis = Axis(rng);
                a.radius = Lerp(rng, 5f, 20f);
                a.speed = Lerp(rng, 0.15f, 0.35f);
                a.phase = Phase(rng);
                Window(rng, duration, ref a);
                agents[k++] = a;
            }

            // Clusters (light show, formation survey) of 20 to 200: one anchor on an orbit, members on a grid or ring.
            int left = n[4];
            while (left > 0)
            {
                int m = Math.Min(left, 20 + rng.Next(181));
                if (left - m < 20) m = left;                // never leave a tiny remainder as its own cluster
                left -= m;
                var anchor = Base(rng, CrowdPattern.Cluster, Civil, spec, duration, 0f);
                anchor.centre = Centre(rng, post, radius * 0.6f, 0.05f, spec[Civil], out _);
                anchor.centre.y = Lerp(rng, 60f, 120f);
                anchor.radius = Lerp(rng, 200f, 500f);
                anchor.speed = Lerp(rng, 6f, 10f);
                anchor.dir = Dir(rng);
                anchor.phase = Phase(rng);
                Window(rng, duration, ref anchor);
                bool ring = m <= 60 && rng.NextDouble() < 0.5;
                float sp = Lerp(rng, 7f, 12f);
                int cols = (int)Math.Ceiling(Math.Sqrt(m)), rows = (m + cols - 1) / cols;
                float ringR = Math.Max(15f, sp * m / (2f * (float)Math.PI));
                for (int j = 0; j < m; j++)
                {
                    var a = anchor;
                    double noIff = rng.NextDouble(), layer = rng.NextDouble();
                    a.iff = (byte)(noIff * 100.0 < noIffPercent ? 1 : 0);
                    a.variant = (byte)(spec[Civil].variants <= 1 ? 0 : rng.Next(Math.Min(spec[Civil].variants, MaxVariants)));
                    if (ring)
                    {
                        float th = j * 2f * (float)Math.PI / m;
                        a.offset = new float3((float)Math.Cos(th) * ringR, (float)layer * 3f, (float)Math.Sin(th) * ringR);
                    }
                    else
                    {
                        int cx = j % cols, cz = j / cols;
                        a.offset = new float3((cx - (cols - 1) * 0.5f) * sp, (float)layer * 3f, (cz - (rows - 1) * 0.5f) * sp);
                    }
                    agents[k++] = a;
                }
            }

            // Bird flocks of 8 to 40 wandering over a large area. Birds have no IFF and no Remote ID.
            left = n[5];
            while (left > 0)
            {
                int m = Math.Min(left, 8 + rng.Next(33));
                if (left - m < 8) m = left;
                left -= m;
                var anchor = Base(rng, CrowdPattern.Flock, Bird, spec, duration, 0f);
                anchor.centre = Centre(rng, post, radius * 0.7f, 0.05f, spec[Bird], out _);
                anchor.centre.y = Lerp(rng, 30f, 120f);
                anchor.radius = Lerp(rng, 300f, 900f);
                anchor.speed = Lerp(rng, spec[Bird].speedMin, spec[Bird].speedMax);
                anchor.phase = Phase(rng);
                Window(rng, duration, ref anchor);
                float spread = Lerp(rng, 15f, 40f);
                for (int j = 0; j < m; j++)
                {
                    var a = anchor;
                    double ox = rng.NextDouble() * 2 - 1, oy = rng.NextDouble() * 2 - 1, oz = rng.NextDouble() * 2 - 1, mp = rng.NextDouble();
                    a.offset = new float3((float)ox * spread, (float)oy * 10f, (float)oz * spread);
                    a.memberPhase = (float)mp * 6.2831853f;
                    int memberLook = spec[Bird].variants <= 1 ? 0 : rng.Next(Math.Min(spec[Bird].variants, MaxVariants));   // drawn to keep the stream; a flock is one species
                    a.variant = anchor.variant;
                    a.iff = 1;
                    agents[k++] = a;
                }
            }
            return agents;
        }

        // The fields every agent has; draws a fixed number of random values.
        static CrowdAgent Base(System.Random rng, CrowdPattern pattern, byte cls, CrowdClassSpec[] spec, float duration, float noIffPercent)
        {
            double noIff = rng.NextDouble();
            double variantDraw = rng.NextDouble();
            int variants = Math.Min(Math.Max(1, spec[cls].variants), MaxVariants);
            return new CrowdAgent
            {
                pattern = (byte)pattern, cls = cls, size = spec[cls].size, spawnT = -1f, endT = CrowdMath.Never, dir = 1f,
                variant = (byte)Math.Min((int)(variantDraw * variants), variants - 1),
                iff = (byte)(cls == Bird ? 1 : noIff * 100.0 < noIffPercent ? 1 : 0),
                lanes = 1f, length = 1f, radius = 1f, speed = 1f,
            };
        }

        // 80 % are up from the start; 10 % arrive during the run, 10 % go home during it.
        static void Window(System.Random rng, float duration, ref CrowdAgent a)
        {
            double r = rng.NextDouble(), u = rng.NextDouble();
            if (r < 0.1) a.spawnT = (float)(duration * (0.1 + 0.6 * u));
            else if (r < 0.2) a.endT = (float)(duration * (0.4 + 0.5 * u));
        }

        static float3 Centre(System.Random rng, float2 post, float maxR, float minFrac, CrowdClassSpec s, out float2 xz)
        {
            double a = rng.NextDouble() * 2 * Math.PI, u = rng.NextDouble(), alt = rng.NextDouble();
            float r = (float)(maxR * (minFrac + (1 - minFrac) * Math.Pow(u, 0.8)));
            xz = post + r * new float2((float)Math.Cos(a), (float)Math.Sin(a));
            return new float3(xz.x, s.altMin + (float)alt * (s.altMax - s.altMin), xz.y);
        }

        static float2 Axis(System.Random rng)
        {
            double a = rng.NextDouble() * 2 * Math.PI;
            return new float2((float)Math.Cos(a), (float)Math.Sin(a));
        }

        static float Lerp(System.Random rng, float lo, float hi) => lo + (float)rng.NextDouble() * (hi - lo);
        static float Dir(System.Random rng) => rng.Next(2) == 0 ? -1f : 1f;
        static float Phase(System.Random rng) => (float)rng.NextDouble() * 6.2831853f;
    }

    // Track numbers. With a crowd the planned contacts and the crowd agents draw their ids from one shuffled permutation, so a
    // number never tells which is which. Without a crowd the ids stay 1, 2, 3... as before.
    public static class CrowdIds
    {
        public static int[] Permutation(int n, int seed)
        {
            var p = new int[Math.Max(0, n)];
            for (int i = 0; i < p.Length; i++) p[i] = i + 1;
            var rng = new System.Random(seed ^ 0x1D5);
            for (int i = p.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int t = p[i]; p[i] = p[j]; p[j] = t;
            }
            return p;
        }
    }
}
