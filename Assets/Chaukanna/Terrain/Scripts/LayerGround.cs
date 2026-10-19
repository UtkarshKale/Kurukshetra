using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // The ground as the layers see it: height, elevation above sea level and slope at any x, z (metres east / north of the
    // area centre), read from the heightfield the terrain was built from. Plain data, so tests can make their own.
    public class LayerGround
    {
        public Heightfield field;
        public float yOffset;               // world height = elevation + yOffset

        public float Half => field.sizeMetres * 0.5f;

        public LayerGround(Heightfield f, float worldOffset) { field = f; yOffset = worldOffset; }

        // A flat or tilted plane for tests: elevation = base + slopeX * x.
        public static LayerGround Synthetic(float sizeMetres, int resolution, Func<float, float, float> elevation, float worldOffset = 0f)
        {
            var f = new Heightfield { resolution = resolution, sizeMetres = sizeMetres, metres = new float[resolution * resolution] };
            float spacing = sizeMetres / (resolution - 1), half = sizeMetres * 0.5f, min = float.MaxValue, max = float.MinValue;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                float h = elevation(-half + x * spacing, -half + z * spacing);
                f.metres[z * resolution + x] = h; if (h < min) min = h; if (h > max) max = h;
            }
            f.min = min; f.max = max; f.centreElevation = elevation(0f, 0f);
            return new LayerGround(f, worldOffset);
        }

        public bool Contains(float x, float z) { float h = Half; return x >= -h && x <= h && z >= -h && z <= h; }

        // Elevation above sea level, bilinear.
        public float Elevation(float x, float z)
        {
            int n = field.resolution;
            float fx = Mathf.Clamp((x + Half) / field.Spacing, 0f, n - 1.001f), fz = Mathf.Clamp((z + Half) / field.Spacing, 0f, n - 1.001f);
            int x0 = (int)fx, z0 = (int)fz;
            float tx = fx - x0, tz = fz - z0;
            float a = field.metres[z0 * n + x0], b = field.metres[z0 * n + x0 + 1], c = field.metres[(z0 + 1) * n + x0], d = field.metres[(z0 + 1) * n + x0 + 1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
        }

        public float Height(float x, float z) => Elevation(x, z) + yOffset;

        // Slope in degrees over about one sample spacing (at least 5 m).
        public float SlopeDegrees(float x, float z)
        {
            float d = Mathf.Max(5f, field.Spacing);
            float dx = (Elevation(x + d, z) - Elevation(x - d, z)) / (2f * d), dz = (Elevation(x, z + d) - Elevation(x, z - d)) / (2f * d);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
        }
    }

    // Ground the layers keep clear: a rectangle (any direction) where the Site (watchtower, cantonment, fields) stands,
    // and a pad round each player post. SiteBuilder registers its rectangles when it builds; the layers read them.
    public static class SiteClearance
    {
        public struct Zone
        {
            public Vector2 centre, axis;        // axis: unit direction of the long side
            public float halfLong, halfShort;
            public bool blocksTrees;            // buildings are always kept out
            public bool Contains(float x, float z, float margin)
            {
                var d = new Vector2(x - centre.x, z - centre.y);
                float u = Vector2.Dot(d, axis), v = Vector2.Dot(d, new Vector2(-axis.y, axis.x));
                return Mathf.Abs(u) <= halfLong + margin && Mathf.Abs(v) <= halfShort + margin;
            }
        }

        public const float PostPadMetres = 60f;

        static readonly List<Zone> zones = new List<Zone>();
        public static int Version { get; private set; }
        public static IReadOnlyList<Zone> Zones => zones;

        // Replaces the registered zones. The version only moves when something really changed.
        public static void Set(IList<Zone> next)
        {
            bool same = next.Count == zones.Count;
            for (int i = 0; same && i < next.Count; i++)
            {
                var a = next[i]; var b = zones[i];
                same = (a.centre - b.centre).sqrMagnitude < 0.01f && (a.axis - b.axis).sqrMagnitude < 1e-6f && Mathf.Abs(a.halfLong - b.halfLong) < 0.01f && Mathf.Abs(a.halfShort - b.halfShort) < 0.01f && a.blocksTrees == b.blocksTrees;
            }
            if (same) return;
            zones.Clear();
            zones.AddRange(next);
            Version++;
        }

        public static void Clear() { if (zones.Count > 0) { zones.Clear(); Version++; } }

        public static bool Blocked(float x, float z, bool forTrees, float margin = 0f)
        {
            for (int i = 0; i < zones.Count; i++)
                if ((!forTrees || zones[i].blocksTrees) && zones[i].Contains(x, z, margin)) return true;
            return false;
        }

        // The Site's footprint for a post at `origin` looking along `forward`: the cantonment (side +1 = in front, -1 = behind)
        // and the open fields on the other side, with the numbers of Scenario.SiteProfile.
        public static List<Zone> ForSite(Vector2 origin, Vector2 forward, float side, float wallDistance, float depth, float halfWidth, float fieldsReach)
        {
            var list = new List<Zone>();
            var dir = forward.sqrMagnitude > 1e-6f ? forward.normalized : Vector2.up;
            float cant = wallDistance + depth;
            list.Add(new Zone { centre = origin + dir * side * cant * 0.5f, axis = dir, halfLong = cant * 0.5f + 12f, halfShort = halfWidth + 12f, blocksTrees = true });
            float reach = fieldsReach + 30f;
            list.Add(new Zone { centre = origin - dir * side * reach * 0.5f, axis = dir, halfLong = reach * 0.5f, halfShort = halfWidth * 1.4f + 12f, blocksTrees = false });
            return list;
        }
    }

    // Density fall-off with distance from the post: full inside `startMetres`, then thinning as start / distance, never below 15 %.
    public static class LayerFalloff
    {
        public const float Floor = 0.15f;
        public static float Of(float distance, float startMetres)
        {
            if (distance <= startMetres || startMetres <= 0f) return 1f;
            return Mathf.Clamp(startMetres / distance, Floor, 1f);
        }
    }
}
