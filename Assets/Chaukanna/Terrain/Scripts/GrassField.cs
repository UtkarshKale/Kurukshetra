using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.TerrainGen
{
    // Grass clumps drawn as GPU-instanced meshes around the player's camera. The cover (0..1) comes from the layer builder on a
    // coarse grid; clumps are placed per 16 m cell from the seed when the camera comes near, and dropped when it goes away,
    // so the whole map costs nothing until you stand on it. Far cells are thinned. Drawn for the player camera only.
    // (Unity's terrain detail layers were tried first: they did not draw at all in this project's pipeline, even in a minimal
    // test terrain, so the grass is drawn here instead.)
    public class GrassField : MonoBehaviour
    {
        public const float CellSize = 16f, DefaultRadius = 150f, ClumpsPerSquareMetre = 0.8f;
        const int BatchSize = 1000, MaxCellsPerFrame = 40;

        public float radius = DefaultRadius;
        public int Drawn { get; private set; }
        public int CachedCells => cache.Count;

        LayerGround ground;
        float[] cover; int cn; float coarse, half;
        int seed;
        Mesh mesh; Material material;
        Vector2 width, height;
        public Camera cameraOverride;       // tests; null = Camera.main

        readonly Dictionary<long, Matrix4x4[]> cache = new Dictionary<long, Matrix4x4[]>();
        readonly Matrix4x4[] batch = new Matrix4x4[BatchSize];
        readonly Plane[] planes = new Plane[6];
        readonly List<long> evict = new List<long>();

        public void Setup(LayerGround groundModel, float[] coverGrid, int coverSize, float coverCell, float halfMetres, int randomSeed, Mesh clumpMesh, Material clumpMaterial, Vector2 widthRange, Vector2 heightRange)
        {
            ground = groundModel; cover = coverGrid; cn = coverSize; coarse = coverCell; half = halfMetres; seed = randomSeed;
            mesh = clumpMesh; material = clumpMaterial; width = widthRange; height = heightRange;
            cache.Clear();
        }

        float CoverAt(float x, float z)
        {
            int ix = Mathf.Clamp((int)((x + half) / coarse), 0, cn - 1), iz = Mathf.Clamp((int)((z + half) / coarse), 0, cn - 1);
            return cover[iz * cn + ix];
        }

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        // The clumps of one 16 m cell.
        Matrix4x4[] Cell(int cx, int cz)
        {
            long key = Key(cx, cz);
            if (cache.TryGetValue(key, out var found)) return found;
            float x0 = cx * CellSize, z0 = cz * CellSize;
            Matrix4x4[] result = System.Array.Empty<Matrix4x4>();
            if (x0 + CellSize > -half && x0 < half && z0 + CellSize > -half && z0 < half)
            {
                float c = Mathf.Max(CoverAt(x0 + 2f, z0 + 2f), CoverAt(x0 + CellSize - 2f, z0 + 2f), Mathf.Max(CoverAt(x0 + 2f, z0 + CellSize - 2f), CoverAt(x0 + CellSize - 2f, z0 + CellSize - 2f)));
                if (c > 0.001f)
                {
                    float expected = c * ClumpsPerSquareMetre * CellSize * CellSize;
                    int n = Mathf.FloorToInt(expected);
                    if (LayerScatter.Rand(cx, cz, seed ^ 0x6A) < expected - n) n++;
                    var list = new List<Matrix4x4>(n);
                    for (int i = 0; i < n; i++)
                    {
                        float x = x0 + LayerScatter.Rand(cx * 977 + i, cz, seed ^ 0x61) * CellSize, z = z0 + LayerScatter.Rand(cx, cz * 983 + i, seed ^ 0x62) * CellSize;
                        // The cover can change across the cell: keep a clump with the local cover's share of the cell's best.
                        if (LayerScatter.Rand(cx * 991 + i, cz * 997 + i, seed ^ 0x63) * c > CoverAt(x, z)) continue;
                        if (!ground.Contains(x, z)) continue;
                        float w = Mathf.Lerp(width.x, width.y, LayerScatter.Rand(i, cx * 31 + cz, seed ^ 0x64)), h = Mathf.Lerp(height.x, height.y, LayerScatter.Rand(i, cx * 37 + cz, seed ^ 0x65));
                        float yaw = LayerScatter.Rand(i, cx * 41 + cz, seed ^ 0x66) * 360f;
                        list.Add(Matrix4x4.TRS(new Vector3(x, ground.Height(x, z) - 0.02f, z), Quaternion.Euler(0f, yaw, 0f), new Vector3(w, h, w)));
                    }
                    result = list.ToArray();
                }
            }
            cache[key] = result;
            return result;
        }

        // How many clumps stand within `metres` of a point (builds those cells). For tests and reports.
        public int ClumpsNear(Vector2 point, float metres)
        {
            int total = 0;
            int x0 = Mathf.FloorToInt((point.x - metres) / CellSize), x1 = Mathf.FloorToInt((point.x + metres) / CellSize), z0 = Mathf.FloorToInt((point.y - metres) / CellSize), z1 = Mathf.FloorToInt((point.y + metres) / CellSize);
            for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                var m = Cell(cx, cz);
                float dx = Mathf.Max(0f, Mathf.Abs((cx + 0.5f) * CellSize - point.x) - CellSize * 0.5f), dz = Mathf.Max(0f, Mathf.Abs((cz + 0.5f) * CellSize - point.y) - CellSize * 0.5f);
                if (dx * dx + dz * dz > metres * metres) continue;
                total += m.Length;
            }
            return total;
        }

        void LateUpdate()
        {
            var cam = cameraOverride != null ? cameraOverride : Camera.main;
            if (cam == null || mesh == null || material == null) return;
            Draw(cam);
        }

        // The draw radius after the player's view-distance scale (never under ViewDistance.GrassFloor on a shorter setting).
        public float EffectiveRadius => ViewDistance.Range(radius, ViewDistance.GrassFloor);

        public void Draw(Camera cam)
        {
            float radius = EffectiveRadius;
            Vector3 p = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            int x0 = Mathf.FloorToInt((p.x - radius) / CellSize), x1 = Mathf.FloorToInt((p.x + radius) / CellSize), z0 = Mathf.FloorToInt((p.z - radius) / CellSize), z1 = Mathf.FloorToInt((p.z + radius) / CellSize);
            var rp = new RenderParams(material) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
            int filled = 0, built = 0, drawn = 0;
            for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                float mx = (cx + 0.5f) * CellSize, mz = (cz + 0.5f) * CellSize;
                float dx = mx - p.x, dz = mz - p.z, d = Mathf.Sqrt(dx * dx + dz * dz) - CellSize * 0.7f;
                if (d > radius) continue;
                if (!cache.ContainsKey(Key(cx, cz)))
                {
                    if (built >= MaxCellsPerFrame) continue;
                    built++;
                }
                var cell = Cell(cx, cz);
                if (cell.Length == 0) continue;
                float gy = ground.Contains(mx, mz) ? ground.Height(mx, mz) : p.y;
                var bounds = new Bounds(new Vector3(mx, gy + 1f, mz), new Vector3(CellSize + 2f, 12f, CellSize + 2f));
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                // Inside 40 % of the range every clump is drawn as it is. Beyond that each clump has its own place on a smooth
                // density ramp (by its own distance, not its cell's), and grows in and out over a short distance instead of popping.
                bool whole = d + CellSize * 0.7f + CellSize * 0.71f < radius * 0.4f;
                for (int i = 0; i < cell.Length; i++)
                {
                    var m = cell[i];
                    if (!whole)
                    {
                        float ix = m.m03 - p.x, iz = m.m23 - p.z;
                        float sc = ViewDistance.InstanceScale(Mathf.Sqrt(ix * ix + iz * iz), radius, m.m03, m.m23);
                        if (sc < 0.02f) continue;
                        if (sc < 0.999f) ViewDistance.ScaleAboutPosition(ref m, sc);
                    }
                    batch[filled++] = m;
                    if (filled == BatchSize) { Graphics.RenderMeshInstanced(rp, mesh, 0, batch, filled); drawn += filled; filled = 0; }
                }
            }
            if (filled > 0) { Graphics.RenderMeshInstanced(rp, mesh, 0, batch, filled); drawn += filled; }
            Drawn = drawn;

            // Forget cells the camera has left.
            if (cache.Count > 900)
            {
                evict.Clear();
                foreach (var key in cache.Keys)
                {
                    int cx = (int)(key >> 32), cz = (int)(key & 0xFFFFFFFF);
                    if (Mathf.Abs((cx + 0.5f) * CellSize - p.x) > radius + 48f || Mathf.Abs((cz + 0.5f) * CellSize - p.z) > radius + 48f) evict.Add(key);
                }
                foreach (var key in evict) cache.Remove(key);
            }
        }
    }
}
