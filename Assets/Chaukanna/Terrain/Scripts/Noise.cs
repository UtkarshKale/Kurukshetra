using System;

namespace Chaukanna.TerrainGen
{
    // Seeded, coherent 2D noise. Everything is a pure function of (x, y, seed), so the same inputs always give the
    // same value and neighbouring terrain tiles agree along their shared edge. Coordinates are doubles so large world
    // offsets do not lose precision.
    public static class Noise
    {
        static uint Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 0x9E3779B1u + 0x7F4A7C15u;
                h ^= (uint)x * 0x85EBCA6Bu;
                h = (h << 13) | (h >> 19);
                h ^= (uint)y * 0xC2B2AE35u;
                h *= 0x27D4EB2Fu;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return h;
            }
        }

        static double Hash01(int x, int y, int seed)
        {
            return (Hash(x, y, seed) & 0xFFFFFF) / (double)0x1000000;
        }

        static double Fade(double t)
        {
            return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        }

        static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }

        // Dot product of the offset with this lattice corner's unit gradient.
        static double Corner(int ix, int iy, double dx, double dy, int seed)
        {
            double angle = Hash01(ix, iy, seed) * Math.PI * 2.0;
            return Math.Cos(angle) * dx + Math.Sin(angle) * dy;
        }

        // Gradient (Perlin-style) noise, one feature per unit. Output is in [-1, 1].
        public static double Gradient(double x, double y, int seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            double u = Fade(fx), v = Fade(fy);
            double a = Lerp(Corner(x0, y0, fx, fy, seed), Corner(x0 + 1, y0, fx - 1.0, fy, seed), u);
            double b = Lerp(Corner(x0, y0 + 1, fx, fy - 1.0, seed), Corner(x0 + 1, y0 + 1, fx - 1.0, fy - 1.0, seed), u);
            // Raw 2D gradient noise spans about +/-0.7071; scale to +/-1 and clamp the rare overshoot.
            double n = Lerp(a, b, v) * 1.41421356;
            return n < -1.0 ? -1.0 : n > 1.0 ? 1.0 : n;
        }

        // Cellular (Voronoi) noise: distance to the nearest feature point (f1) and the second nearest (f2), in cell units.
        public static void Cellular(double x, double y, int seed, out double f1, out double f2)
        {
            int cx = (int)Math.Floor(x), cy = (int)Math.Floor(y);
            f1 = f2 = double.MaxValue;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int ix = cx + i, iy = cy + j;
                double px = ix + Hash01(ix, iy, seed), py = iy + Hash01(ix, iy, seed ^ 0x51ED27);
                double dx = px - x, dy = py - y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                if (d < f1) { f2 = f1; f1 = d; }
                else if (d < f2) f2 = d;
            }
        }

        // Fractal Brownian motion: octaves of gradient noise, each `lacunarity` times finer and `persistence` times weaker.
        // Divided by the sum of octave weights, so the result stays in [-1, 1] whatever the octave count.
        public static double Fbm(double x, double y, int seed, int octaves, double lacunarity, double persistence)
        {
            double sum = 0.0, weight = 0.0, amplitude = 1.0, frequency = 1.0;
            for (int o = 0; o < octaves; o++)
            {
                sum += Gradient(x * frequency, y * frequency, seed + o * 1013) * amplitude;
                weight += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return weight > 0.0 ? sum / weight : 0.0;
        }

        // Ridged fractal: each octave is 1 - |noise| raised to `exponent`, giving sharp crests. Result is in [0, 1].
        public static double Ridged(double x, double y, int seed, int octaves, double lacunarity, double persistence, double exponent)
        {
            double sum = 0.0, weight = 0.0, amplitude = 1.0, frequency = 1.0;
            for (int o = 0; o < octaves; o++)
            {
                double ridge = 1.0 - Math.Abs(Gradient(x * frequency, y * frequency, seed + o * 1013));
                sum += Math.Pow(ridge, exponent) * amplitude;
                weight += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }
            return weight > 0.0 ? sum / weight : 0.0;
        }
    }
}
