using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // Plain 2D geometry on the ground plane (x east, z north) for the terrain layers: polygons with holes, point tests,
    // ear-clipping triangulation and building prisms. No Unity objects, so it is easy to test.
    public static class LayerGeometry
    {
        // Shoelace area of x,z pairs: positive when the ring runs counter-clockwise seen from above.
        public static float SignedArea(float[] p)
        {
            double a = 0.0;
            int n = p.Length / 2;
            for (int i = 0, j = n - 1; i < n; j = i++) a += (double)p[j * 2] * p[i * 2 + 1] - (double)p[i * 2] * p[j * 2 + 1];
            return (float)(a * 0.5);
        }

        public static float[] Reversed(float[] p)
        {
            int n = p.Length / 2;
            var r = new float[p.Length];
            for (int i = 0; i < n; i++) { r[i * 2] = p[(n - 1 - i) * 2]; r[i * 2 + 1] = p[(n - 1 - i) * 2 + 1]; }
            return r;
        }

        public static float[] Ccw(float[] p) => SignedArea(p) < 0f ? Reversed(p) : p;
        public static float[] Cw(float[] p) => SignedArea(p) > 0f ? Reversed(p) : p;

        public static Vector2 Centroid(float[] p)
        {
            int n = p.Length / 2;
            double cx = 0.0, cz = 0.0, a = 0.0;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                double cr = (double)p[j * 2] * p[i * 2 + 1] - (double)p[i * 2] * p[j * 2 + 1];
                a += cr; cx += (p[j * 2] + p[i * 2]) * cr; cz += (p[j * 2 + 1] + p[i * 2 + 1]) * cr;
            }
            if (Math.Abs(a) < 1e-6)
            {
                double sx = 0.0, sz = 0.0;
                for (int i = 0; i < n; i++) { sx += p[i * 2]; sz += p[i * 2 + 1]; }
                return new Vector2((float)(sx / Math.Max(1, n)), (float)(sz / Math.Max(1, n)));
            }
            return new Vector2((float)(cx / (3.0 * a)), (float)(cz / (3.0 * a)));
        }

        public static void Bounds(float[] p, out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
            for (int i = 0; i < p.Length; i += 2)
            {
                if (p[i] < minX) minX = p[i]; if (p[i] > maxX) maxX = p[i];
                if (p[i + 1] < minZ) minZ = p[i + 1]; if (p[i + 1] > maxZ) maxZ = p[i + 1];
            }
        }

        // The oriented box around a footprint: aligned with its longest edge. Returns centre, the long-axis direction and half sizes.
        public static void OrientedBox(float[] p, out Vector2 centre, out Vector2 axis, out float halfLong, out float halfShort)
        {
            int n = p.Length / 2;
            float best = -1f; axis = Vector2.right;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float dx = p[i * 2] - p[j * 2], dz = p[i * 2 + 1] - p[j * 2 + 1], l = dx * dx + dz * dz;
                if (l > best) { best = l; axis = new Vector2(dx, dz); }
            }
            axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector2.right;
            var side = new Vector2(-axis.y, axis.x);
            float u0 = float.MaxValue, u1 = float.MinValue, v0 = float.MaxValue, v1 = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                var q = new Vector2(p[i * 2], p[i * 2 + 1]);
                float u = Vector2.Dot(q, axis), v = Vector2.Dot(q, side);
                if (u < u0) u0 = u; if (u > u1) u1 = u; if (v < v0) v0 = v; if (v > v1) v1 = v;
            }
            halfLong = (u1 - u0) * 0.5f; halfShort = (v1 - v0) * 0.5f;
            centre = axis * ((u0 + u1) * 0.5f) + side * ((v0 + v1) * 0.5f);
        }

        public static float[] BoxRing(Vector2 centre, Vector2 axis, float halfLong, float halfShort)
        {
            var side = new Vector2(-axis.y, axis.x);
            Vector2 a = centre - axis * halfLong - side * halfShort, b = centre + axis * halfLong - side * halfShort, c = centre + axis * halfLong + side * halfShort, d = centre - axis * halfLong + side * halfShort;
            return new[] { a.x, a.y, b.x, b.y, c.x, c.y, d.x, d.y };      // counter-clockwise (axis x side is a left turn)
        }

        // --- Triangulation: ear clipping with hole bridging ---

        // Triangles of a polygon with holes. Rings are x,z pairs; `outer` any winding, holes any winding. Returns the
        // vertex list (the rings joined through bridges, so some points repeat) and counter-clockwise triangle indices.
        public static bool Triangulate(float[] outer, IList<float[]> holes, out List<Vector2> verts, out List<int> tris)
        {
            verts = new List<Vector2>();
            tris = new List<int>();
            if (outer == null || outer.Length < 6) return false;
            outer = Ccw(outer);
            var ring = new List<int>();
            for (int i = 0; i < outer.Length; i += 2) { verts.Add(new Vector2(outer[i], outer[i + 1])); ring.Add(verts.Count - 1); }

            if (holes != null && holes.Count > 0)
            {
                var hs = new List<float[]>();
                foreach (var h in holes) if (h != null && h.Length >= 6) hs.Add(Cw(h));
                hs.Sort((a, b) => MaxX(b).CompareTo(MaxX(a)));
                foreach (var h in hs) Bridge(verts, ring, h);
            }
            return EarClip(verts, ring, tris);
        }

        static float MaxX(float[] p) { float m = float.MinValue; for (int i = 0; i < p.Length; i += 2) if (p[i] > m) m = p[i]; return m; }

        // Joins a clockwise hole to the polygon ring through the nearest vertex it can see.
        static void Bridge(List<Vector2> verts, List<int> ring, float[] hole)
        {
            int hn = hole.Length / 2, hRight = 0;
            for (int i = 1; i < hn; i++) if (hole[i * 2] > hole[hRight * 2]) hRight = i;
            var h = new Vector2(hole[hRight * 2], hole[hRight * 2 + 1]);

            int bestAt = -1; float bestD = float.MaxValue;
            for (int k = 0; k < ring.Count; k++)
            {
                var v = verts[ring[k]];
                float d = (v - h).sqrMagnitude;
                if (d >= bestD) continue;
                if (!Visible(verts, ring, k, h, hole)) continue;
                bestD = d; bestAt = k;
            }
            if (bestAt < 0) return;      // cannot connect: the hole is ignored (the footprint is filled)

            // ... V, H, hole vertices around back to H, V, ...
            var insert = new List<int>();
            for (int i = 0; i <= hn; i++)
            {
                int hi = (hRight + i) % hn;
                verts.Add(new Vector2(hole[hi * 2], hole[hi * 2 + 1]));
                insert.Add(verts.Count - 1);
            }
            verts.Add(verts[ring[bestAt]]);
            insert.Add(verts.Count - 1);
            ring.InsertRange(bestAt + 1, insert);
        }

        static bool Visible(List<Vector2> verts, List<int> ring, int k, Vector2 h, float[] hole)
        {
            var v = verts[ring[k]];
            for (int i = 0; i < ring.Count; i++)
            {
                if (i == k || (i + 1) % ring.Count == k) continue;
                var a = verts[ring[i]]; var b = verts[ring[(i + 1) % ring.Count]];
                if (SegmentsCross(v, h, a, b)) return false;
            }
            int hn = hole.Length / 2;
            for (int i = 0; i < hn; i++)
            {
                var a = new Vector2(hole[i * 2], hole[i * 2 + 1]); var b = new Vector2(hole[((i + 1) % hn) * 2], hole[((i + 1) % hn) * 2 + 1]);
                if (a == h || b == h) continue;
                if (SegmentsCross(v, h, a, b)) return false;
            }
            return true;
        }

        // Proper crossing (shared end points do not count).
        static bool SegmentsCross(Vector2 p1, Vector2 p2, Vector2 q1, Vector2 q2)
        {
            if (p1 == q1 || p1 == q2 || p2 == q1 || p2 == q2) return false;
            float d1 = Cross(q1, q2, p1), d2 = Cross(q1, q2, p2), d3 = Cross(p1, p2, q1), d4 = Cross(p1, p2, q2);
            return ((d1 > 0f && d2 < 0f) || (d1 < 0f && d2 > 0f)) && ((d3 > 0f && d4 < 0f) || (d3 < 0f && d4 > 0f));
        }

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        static bool EarClip(List<Vector2> verts, List<int> ring, List<int> tris)
        {
            var r = new List<int>(ring);
            int guard = r.Count * r.Count + 16;
            while (r.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                int n = r.Count;
                for (int k = 0; k < n; k++)
                {
                    int ip = r[(k + n - 1) % n], ic = r[k], inx = r[(k + 1) % n];
                    Vector2 a = verts[ip], b = verts[ic], c = verts[inx];
                    float area = Cross(a, b, c);
                    if (Mathf.Abs(area) < 1e-6f) { r.RemoveAt(k); clipped = true; break; }      // collinear: drop the point
                    if (area < 0f) continue;                                                    // reflex
                    bool ear = true;
                    for (int m = 0; m < n && ear; m++)
                    {
                        int im = r[m];
                        if (im == ip || im == ic || im == inx) continue;
                        Vector2 q = verts[im];
                        if (q == a || q == b || q == c) continue;
                        if (Cross(a, b, q) >= -1e-6f && Cross(b, c, q) >= -1e-6f && Cross(c, a, q) >= -1e-6f) ear = false;
                    }
                    if (!ear) continue;
                    tris.Add(ip); tris.Add(ic); tris.Add(inx);
                    r.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped)
                {
                    // A badly drawn footprint: take the sharpest convex corner anyway so this ends.
                    int bestK = -1; float bestA = float.MaxValue;
                    for (int k = 0; k < n; k++)
                    {
                        float area = Cross(verts[r[(k + n - 1) % n]], verts[r[k]], verts[r[(k + 1) % n]]);
                        if (area > 0f && area < bestA) { bestA = area; bestK = k; }
                    }
                    if (bestK < 0) break;
                    tris.Add(r[(bestK + n - 1) % n]); tris.Add(r[bestK]); tris.Add(r[(bestK + 1) % n]);
                    r.RemoveAt(bestK);
                }
            }
            if (r.Count == 3 && Mathf.Abs(Cross(verts[r[0]], verts[r[1]], verts[r[2]])) > 1e-6f)
            {
                if (Cross(verts[r[0]], verts[r[1]], verts[r[2]]) > 0f) { tris.Add(r[0]); tris.Add(r[1]); tris.Add(r[2]); }
                else { tris.Add(r[0]); tris.Add(r[2]); tris.Add(r[1]); }
            }
            return tris.Count > 0;
        }
    }

    // Point-in-polygon for a polygon with holes, even-odd rule, with the edges bucketed by height so a test only
    // looks at the few edges near the point. Built once per polygon.
    public class PolyIndex
    {
        readonly float[] x0, z0, x1, z1;
        readonly int[] bucketStart, bucketEdges;
        readonly float minX, maxX, minZ, maxZ, bucketH;
        readonly int buckets;

        public float MinX => minX; public float MaxX => maxX; public float MinZ => minZ; public float MaxZ => maxZ;

        public PolyIndex(float[] outer, IList<LayerRing> holes)
        {
            var rings = new List<float[]> { outer };
            if (holes != null) foreach (var h in holes) if (h != null && h.p != null && h.p.Length >= 6) rings.Add(h.p);
            int edges = 0;
            foreach (var r in rings) edges += r.Length / 2;
            x0 = new float[edges]; z0 = new float[edges]; x1 = new float[edges]; z1 = new float[edges];
            minX = minZ = float.MaxValue; maxX = maxZ = float.MinValue;
            int e = 0;
            foreach (var r in rings)
            {
                int n = r.Length / 2;
                for (int i = 0, j = n - 1; i < n; j = i++)
                {
                    x0[e] = r[j * 2]; z0[e] = r[j * 2 + 1]; x1[e] = r[i * 2]; z1[e] = r[i * 2 + 1];
                    if (x1[e] < minX) minX = x1[e]; if (x1[e] > maxX) maxX = x1[e];
                    if (z1[e] < minZ) minZ = z1[e]; if (z1[e] > maxZ) maxZ = z1[e];
                    e++;
                }
            }
            buckets = Mathf.Clamp(edges / 6, 1, 2048);
            bucketH = Mathf.Max(1e-3f, (maxZ - minZ) / buckets);
            var counts = new int[buckets + 1];
            for (int k = 0; k < edges; k++) { Range(k, out int lo, out int hi); for (int b = lo; b <= hi; b++) counts[b + 1]++; }
            for (int b = 0; b < buckets; b++) counts[b + 1] += counts[b];
            bucketStart = counts;
            bucketEdges = new int[counts[buckets]];
            var fill = new int[buckets];
            for (int k = 0; k < edges; k++) { Range(k, out int lo, out int hi); for (int b = lo; b <= hi; b++) bucketEdges[bucketStart[b] + fill[b]++] = k; }
        }

        void Range(int k, out int lo, out int hi)
        {
            float a = Mathf.Min(z0[k], z1[k]), b = Mathf.Max(z0[k], z1[k]);
            lo = Mathf.Clamp((int)((a - minZ) / bucketH), 0, buckets - 1);
            hi = Mathf.Clamp((int)((b - minZ) / bucketH), 0, buckets - 1);
        }

        public bool Contains(float x, float z)
        {
            if (x < minX || x > maxX || z < minZ || z > maxZ) return false;
            int b = Mathf.Clamp((int)((z - minZ) / bucketH), 0, buckets - 1);
            bool inside = false;
            for (int s = bucketStart[b]; s < bucketStart[b + 1]; s++)
            {
                int k = bucketEdges[s];
                float za = z0[k], zb = z1[k];
                if ((za > z) == (zb > z)) continue;
                float xi = x0[k] + (z - za) / (zb - za) * (x1[k] - x0[k]);
                if (x < xi) inside = !inside;
            }
            return inside;
        }
    }

    // Walls and roofs of extruded footprints, collected into one mesh (two submeshes: walls, roofs).
    public class PrismMesh
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uvs = new List<Vector2>();
        public readonly List<int> walls = new List<int>(), roofs = new List<int>();
        public int Triangles => (walls.Count + roofs.Count) / 3;

        // One footprint from yBottom to yTop. Positions are written relative to `origin` (x, z), y as given.
        // `wallUv` / `roofUv`: when given, every wall (roof) vertex of this prism gets that one texture coordinate, so a palette
        // texture can colour each building from one shared material (BuildingPalette). Otherwise the UVs tile the surface.
        public bool AddPrism(float[] outer, IList<float[]> holes, float yBottom, float yTop, float originX, float originZ, Vector2? wallUv = null, Vector2? roofUv = null)
        {
            if (!LayerGeometry.Triangulate(outer, holes, out var tv, out var tt)) return false;
            outer = LayerGeometry.Ccw(outer);

            // Roof.
            int baseIndex = vertices.Count;
            foreach (var v in tv) { vertices.Add(new Vector3(v.x - originX, yTop, v.y - originZ)); normals.Add(Vector3.up); uvs.Add(roofUv ?? new Vector2(v.x * 0.1f, v.y * 0.1f)); }
            for (int i = 0; i < tt.Count; i += 3) { roofs.Add(baseIndex + tt[i]); roofs.Add(baseIndex + tt[i + 2]); roofs.Add(baseIndex + tt[i + 1]); }

            // Walls: outer ring counter-clockwise, hole rings clockwise; the outward normal is the right of the direction of travel.
            AddWalls(outer, yBottom, yTop, originX, originZ, wallUv);
            if (holes != null) foreach (var h in holes) if (h != null && h.Length >= 6) AddWalls(LayerGeometry.Cw(h), yBottom, yTop, originX, originZ, wallUv);
            return true;
        }

        void AddWalls(float[] ring, float yBottom, float yTop, float originX, float originZ, Vector2? wallUv)
        {
            int n = ring.Length / 2;
            float run = 0f;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float ax = ring[j * 2], az = ring[j * 2 + 1], bx = ring[i * 2], bz = ring[i * 2 + 1];
                float dx = bx - ax, dz = bz - az, len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 0.01f) continue;
                var nrm = new Vector3(dz / len, 0f, -dx / len);
                int b = vertices.Count;
                vertices.Add(new Vector3(ax - originX, yBottom, az - originZ)); vertices.Add(new Vector3(ax - originX, yTop, az - originZ));
                vertices.Add(new Vector3(bx - originX, yTop, bz - originZ)); vertices.Add(new Vector3(bx - originX, yBottom, bz - originZ));
                for (int k = 0; k < 4; k++) normals.Add(nrm);
                float h = (yTop - yBottom) * 0.25f;
                if (wallUv.HasValue) { for (int k = 0; k < 4; k++) uvs.Add(wallUv.Value); }
                else { uvs.Add(new Vector2(run * 0.25f, 0f)); uvs.Add(new Vector2(run * 0.25f, h)); uvs.Add(new Vector2((run + len) * 0.25f, h)); uvs.Add(new Vector2((run + len) * 0.25f, 0f)); }
                walls.Add(b); walls.Add(b + 1); walls.Add(b + 2); walls.Add(b); walls.Add(b + 2); walls.Add(b + 3);
                run += len;
            }
        }

        public Mesh ToMesh(string name)
        {
            var m = new Mesh { name = name };
            if (vertices.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(vertices);
            m.SetNormals(normals);
            m.SetUVs(0, uvs);
            m.subMeshCount = 2;
            m.SetTriangles(walls, 0);
            m.SetTriangles(roofs, 1);
            m.RecalculateBounds();
            return m;
        }
    }
}
