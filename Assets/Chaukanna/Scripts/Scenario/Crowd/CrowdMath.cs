using Unity.Collections;
using Unity.Mathematics;

namespace Chaukanna.Scenario
{
    // The crowd motion as pure maths (Burst-compatible: static methods on Unity.Mathematics types only). Position is a
    // closed-form function of the agent and the scenario time, so the same seed gives the same sky on every PC, a promoted
    // drone continues exactly on its path, and no integration or neighbour search is ever needed.
    public static class CrowdMath
    {
        public const float MinAbove = 15f;        // no crowd drone flies lower than this above the ground (m)
        public const float LaneSpacing = 12f;     // between the lanes of a delivery corridor (m)
        public const float LevelGap = 15f;        // outbound and return legs of a corridor fly this far apart in height (m)
        public const float Never = 1e9f;

        public static bool Alive(in CrowdAgent a, float t) => t >= a.spawnT && t < a.endT;

        static float Wrap(float s, float period) => s - period * math.floor(s / period);

        // x, z in world metres; y = altitude above the ground (not a world height).
        public static float3 Pos(in CrowdAgent a, float t)
        {
            float2 c = a.centre.xz;
            switch ((CrowdPattern)a.pattern)
            {
                case CrowdPattern.Orbit:
                {
                    float ang = a.phase + a.dir * a.speed / a.radius * t;
                    float2 p = c + a.radius * new float2(math.cos(ang), math.sin(ang));
                    return new float3(p.x, a.centre.y, p.y);
                }
                case CrowdPattern.Corridor:
                {
                    float len = a.length;
                    float s = Wrap(a.phase + a.speed * t, 2f * len);
                    float u = s < len ? s : 2f * len - s;
                    float2 perp = new float2(-a.axis.y, a.axis.x);
                    float2 p = c + a.axis * (u - 0.5f * len) + perp * a.offset.x;
                    float ramp = math.min(30f, len * 0.25f);
                    float h = math.smoothstep(len - ramp, len + ramp, s) * (1f - math.smoothstep(2f * len - ramp, 2f * len, s));
                    return new float3(p.x, a.centre.y + LevelGap * h, p.y);
                }
                case CrowdPattern.Grid:
                {
                    float len = a.length, spacing = a.radius;
                    int lanes = math.max(1, (int)a.lanes);
                    float legSpan = len + spacing;
                    float total = lanes * len + (lanes - 1) * spacing;
                    float q = Wrap(a.phase + a.speed * t, 2f * total);
                    if (q > total) q = 2f * total - q;
                    int i = math.min((int)math.floor(q / legSpan), lanes - 1);
                    float r = q - i * legSpan;
                    if (i == lanes - 1) r = math.min(r, len);
                    bool even = (i & 1) == 0;
                    float x, y;
                    if (r <= len) { x = even ? r : len - r; y = i * spacing; }
                    else { x = even ? len : 0f; y = i * spacing + (r - len); }
                    x -= 0.5f * len;
                    y -= 0.5f * (lanes - 1) * spacing;
                    float2 perp = new float2(-a.axis.y, a.axis.x);
                    float2 p = c + a.axis * x + perp * y;
                    return new float3(p.x, a.centre.y, p.y);
                }
                case CrowdPattern.Station:
                {
                    float amp = a.radius, w = a.speed;
                    float x = amp * math.sin(w * t + a.phase);
                    float z = 0.8f * amp * math.sin(1.37f * w * t + 1.3f * a.phase);
                    float2 perp = new float2(-a.axis.y, a.axis.x);
                    float2 p = c + a.axis * x + perp * z;
                    return new float3(p.x, a.centre.y + 2f * math.sin(0.3f * t + a.phase), p.y);
                }
                case CrowdPattern.Cluster:
                {
                    float ang = a.phase + a.dir * a.speed / a.radius * t;
                    float2 anchor = c + a.radius * new float2(math.cos(ang), math.sin(ang));
                    float2 fwd = a.dir * new float2(-math.sin(ang), math.cos(ang));      // heading along the circle
                    float2 right = new float2(fwd.y, -fwd.x);
                    float2 p = anchor + right * a.offset.x + fwd * a.offset.z;
                    return new float3(p.x, a.centre.y + a.offset.y, p.y);
                }
                default:    // Flock
                {
                    float w1 = a.speed / a.radius;
                    float2 anchor = c + new float2(a.radius * math.sin(w1 * t + a.phase), 0.8f * a.radius * math.sin(1.31f * w1 * t + 2.1f * a.phase));
                    float mp = a.memberPhase;
                    float3 wob = 3f * new float3(math.sin(1.7f * t + mp), 0.4f * math.sin(2.3f * t + 1.7f * mp), math.cos(1.3f * t + 0.6f * mp));
                    float2 p = anchor + a.offset.xz + wob.xz;
                    return new float3(p.x, a.centre.y + 10f * math.sin(0.05f * t + a.phase) + a.offset.y + wob.y, p.y);
                }
            }
        }

        // Heading (radians about +Y, 0 = +Z) from where it will be a moment later.
        public static float Yaw(in CrowdAgent a, float t)
        {
            float3 p0 = Pos(a, t), p1 = Pos(a, t + 0.05f);
            return math.atan2(p1.x - p0.x, p1.z - p0.z);
        }

        // Bilinear ground height from the height grid (row-major, n x n, origin = south-west corner, invCell = 1 / cell size);
        // clamps at the edges.
        public static float Ground(in NativeArray<float> h, float2 origin, float invCell, int n, float x, float z)
        {
            float fx = math.clamp((x - origin.x) * invCell, 0f, n - 1f);
            float fz = math.clamp((z - origin.y) * invCell, 0f, n - 1f);
            int i0 = math.min((int)fx, n - 2), j0 = math.min((int)fz, n - 2);
            float tx = fx - i0, tz = fz - j0;
            float a = h[j0 * n + i0], b = h[j0 * n + i0 + 1], c = h[(j0 + 1) * n + i0], d = h[(j0 + 1) * n + i0 + 1];
            return math.lerp(math.lerp(a, b, tx), math.lerp(c, d, tx), tz);
        }

        public static float WorldY(float altitude, float ground) => ground + math.max(altitude, MinAbove);

        // World position: Pos with the ground added and the 15 m floor applied.
        public static float3 World(in CrowdAgent a, float t, in NativeArray<float> h, float2 origin, float invCell, int n)
        {
            float3 p = Pos(a, t);
            return new float3(p.x, WorldY(p.y, Ground(h, origin, invCell, n, p.x, p.z)), p.z);
        }
    }
}
