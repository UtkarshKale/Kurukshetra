using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // Noise that wraps: the value at u = 0 continues seamlessly from u = 1 (same for v), so textures made with it tile.
    // u, v are 0..1 across the texture; `period` is how many noise cells fit across it (whole numbers only).
    public static class TileNoise
    {
        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static int Wrap(int i, int period) => ((i % period) + period) % period;

        public static float Value(float u, float v, int period, int seed)
        {
            period = Mathf.Max(1, period);
            float x = u * period, y = v * period;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            float fx = x - ix, fy = y - iy;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int x0 = Wrap(ix, period), x1 = Wrap(ix + 1, period), y0 = Wrap(iy, period), y1 = Wrap(iy + 1, period);
            float a = Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx);
            float b = Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx);
            return Mathf.Lerp(a, b, fy);
        }

        // 0..1, octaves doubling the period each time.
        public static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                sum += Value(u, v, period << o, seed + o * 101) * amp;
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        // 0..1, sharp crests where the noise crosses its middle: good for rock.
        public static float Ridged(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                float r = 1f - Mathf.Abs(Value(u, v, period << o, seed + o * 131) * 2f - 1f);
                sum += r * r * amp;
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        // Cellular (Worley) noise: distances to the nearest and second nearest cell point (in cell widths), and the
        // nearest cell's own random value. Good for stones and pebbles.
        public static void Cells(float u, float v, int period, int seed, out float f1, out float f2, out float id)
        {
            period = Mathf.Max(1, period);
            float x = u * period, y = v * period;
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y);
            f1 = f2 = 9f;
            id = 0f;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = ix + dx, cy = iy + dy;
                int wx = Wrap(cx, period), wy = Wrap(cy, period);
                float px = cx + Hash(wx, wy, seed), py = cy + Hash(wx, wy, seed + 17);
                float d = Mathf.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < f1) { f2 = f1; f1 = d; id = Hash(wx, wy, seed + 29); }
                else if (d < f2) f2 = d;
            }
        }

        // 0 on the borders between cells, 1 inside: cracks.
        public static float CellEdge(float u, float v, int period, int seed)
        {
            Cells(u, v, period, seed, out float f1, out float f2, out _);
            return Mathf.Clamp01((f2 - f1) * 4f);
        }
    }
}
