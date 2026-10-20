using System.Collections.Generic;
using UnityEngine;

namespace Kurukshetra.TerrainGen
{
    // One bush model: the mesh and materials of a kit prefab, plus the matrix from the mesh to the prefab's root (FBX files
    // often carry a 100x scale on a child) so an instance is just TRS(position, yaw, scale) * local.
    public class BushModel
    {
        public Mesh mesh;
        public Material[] materials;
        public Matrix4x4 local = Matrix4x4.identity;
        public float height = 1f;

        // Reads the first MeshFilter + MeshRenderer under a prefab. Null when the prefab has no usable mesh.
        public static BushModel From(GameObject prefab)
        {
            if (prefab == null) return null;
            var mf = prefab.GetComponentInChildren<MeshFilter>(true);
            var mr = prefab.GetComponentInChildren<MeshRenderer>(true);
            if (mf == null || mr == null || mf.sharedMesh == null) return null;
            var mats = mr.sharedMaterials;
            if (mats == null || mats.Length == 0) return null;
            var m = new BushModel { mesh = mf.sharedMesh, materials = mats };
            var root = prefab.transform;
            // For an asset prefab the transforms are in prefab space already: root-to-world is the identity, but go through it anyway.
            m.local = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var b = mf.sharedMesh.bounds;
            m.height = Mathf.Max(0.1f, b.size.y * Mathf.Abs(m.local.GetColumn(1).magnitude));
            return m;
        }
    }

    // Bushes drawn as GPU-instanced meshes round the camera, distance-culled and frustum-culled, no colliders (a bush under
    // 2 m does not hide a drone from a tower). The spots come from the layer builder; this class sorts them into 32 m cells,
    // makes each cell's matrices the first time the camera comes near, and forgets cells the camera has left. Far cells keep
    // a fraction of their bushes. Same approach as GrassField (terrain trees cost per instance; this costs per batch).
    public class BushField : MonoBehaviour
    {
        public const float CellSize = 32f, DefaultRadius = 200f;
        const int BatchSize = 1000, MaxCellsPerFrame = 24, CacheLimit = 900;

        public float radius = DefaultRadius;
        public int Drawn { get; private set; }
        public int Total { get; private set; }
        public int CachedCells => cache.Count;
        public Camera cameraOverride;       // tests; null = Camera.main

        class Cell { public BushSpot[] spots; public Matrix4x4[][] byModel; }

        LayerGround ground;
        BushModel[] normal, snow;
        Vector2 scale;
        readonly Dictionary<long, Cell> cells = new Dictionary<long, Cell>();
        readonly Dictionary<long, Matrix4x4[][]> cache = new Dictionary<long, Matrix4x4[][]>();
        BushModel[] models;                                         // normal then snow
        Matrix4x4[][] batch;
        int[] batchFill;
        readonly Plane[] planes = new Plane[6];
        readonly List<long> evict = new List<long>();

        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        public void Setup(LayerGround groundModel, List<BushSpot> spots, BushModel[] normalModels, BushModel[] snowModels, Vector2 scaleRange, float drawRadius)
        {
            ground = groundModel; normal = normalModels ?? new BushModel[0]; snow = snowModels ?? new BushModel[0]; scale = scaleRange; radius = Mathf.Max(10f, drawRadius);
            var all = new List<BushModel>(normal); all.AddRange(snow);
            models = all.ToArray();
            batch = new Matrix4x4[models.Length][];
            for (int i = 0; i < batch.Length; i++) batch[i] = new Matrix4x4[BatchSize];
            batchFill = new int[models.Length];
            cells.Clear(); cache.Clear();
            var lists = new Dictionary<long, List<BushSpot>>();
            foreach (var s in spots)
            {
                long key = Key(Mathf.FloorToInt(s.x / CellSize), Mathf.FloorToInt(s.z / CellSize));
                if (!lists.TryGetValue(key, out var l)) lists[key] = l = new List<BushSpot>();
                l.Add(s);
            }
            foreach (var kv in lists) cells[kv.Key] = new Cell { spots = kv.Value.ToArray() };
            Total = spots.Count;
        }

        // Which model (index into normal + snow) a spot uses, by the elevation where it stands.
        public int ModelFor(BushSpot s, float elevation)
        {
            bool useSnow = snow.Length > 0 && elevation >= Vegetation.SnowTreesMetres;
            if (useSnow) return normal.Length + (int)((uint)s.hash % (uint)snow.Length);
            if (normal.Length == 0) return snow.Length > 0 ? 0 : -1;
            return (int)((uint)s.hash % (uint)normal.Length);
        }

        Matrix4x4[][] Build(long key, Cell cell)
        {
            var lists = new List<Matrix4x4>[models.Length];
            for (int i = 0; i < lists.Length; i++) lists[i] = new List<Matrix4x4>();
            foreach (var s in cell.spots)
            {
                float elev = ground.Elevation(s.x, s.z);
                int m = ModelFor(s, elev);
                if (m < 0) continue;
                float sc = Mathf.Lerp(Mathf.Min(scale.x, scale.y), Mathf.Max(scale.x, scale.y), s.scale);
                lists[m].Add(Matrix4x4.TRS(new Vector3(s.x, ground.Height(s.x, s.z) - 0.05f, s.z), Quaternion.Euler(0f, s.yaw, 0f), new Vector3(sc, sc * (0.85f + 0.3f * ((s.hash >> 8) & 0xFF) / 255f), sc)) * models[m].local);
            }
            var arrays = new Matrix4x4[models.Length][];
            for (int i = 0; i < arrays.Length; i++) arrays[i] = lists[i].ToArray();
            return arrays;
        }

        void LateUpdate()
        {
            var cam = cameraOverride != null ? cameraOverride : Camera.main;
            if (cam == null || models == null || models.Length == 0) return;
            Draw(cam);
        }

        void Flush(int m, Camera cam)
        {
            int n = batchFill[m];
            if (n == 0) return;
            var model = models[m];
            for (int sub = 0; sub < model.materials.Length && sub < model.mesh.subMeshCount; sub++)
            {
                var mat = model.materials[sub];
                if (mat == null) continue;
                var rp = new RenderParams(mat) { camera = cam, shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false };
                Graphics.RenderMeshInstanced(rp, model.mesh, sub, batch[m], n);
            }
            batchFill[m] = 0;
        }

        // The draw radius after the player's view-distance scale (never under ViewDistance.BushFloor on a shorter setting).
        public float EffectiveRadius => ViewDistance.Range(radius, ViewDistance.BushFloor);

        public void Draw(Camera cam)
        {
            float radius = EffectiveRadius;
            Vector3 p = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            int x0 = Mathf.FloorToInt((p.x - radius) / CellSize), x1 = Mathf.FloorToInt((p.x + radius) / CellSize), z0 = Mathf.FloorToInt((p.z - radius) / CellSize), z1 = Mathf.FloorToInt((p.z + radius) / CellSize);
            int built = 0, drawn = 0;
            for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
            {
                long key = Key(cx, cz);
                if (!cells.TryGetValue(key, out var cell)) continue;
                float mx = (cx + 0.5f) * CellSize, mz = (cz + 0.5f) * CellSize;
                float dx = mx - p.x, dz = mz - p.z, d = Mathf.Sqrt(dx * dx + dz * dz) - CellSize * 0.7f;
                if (d > radius) continue;
                float gy = ground.Contains(mx, mz) ? ground.Height(mx, mz) : p.y;
                var bounds = new Bounds(new Vector3(mx, gy + 2f, mz), new Vector3(CellSize + 4f, 40f, CellSize + 4f));
                if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                if (!cache.TryGetValue(key, out var arrays))
                {
                    if (built >= MaxCellsPerFrame) continue;
                    built++;
                    cache[key] = arrays = Build(key, cell);
                }
                // Inside 40 % of the range every bush is drawn. Beyond that each bush sits on a smooth density ramp by its own
                // distance and grows in and out over a short distance instead of popping.
                bool whole = d + CellSize * 0.7f + CellSize * 0.71f < radius * 0.4f;
                for (int m = 0; m < arrays.Length; m++)
                {
                    var list = arrays[m];
                    for (int i = 0; i < list.Length; i++)
                    {
                        var mat = list[i];
                        if (!whole)
                        {
                            float ix = mat.m03 - p.x, iz = mat.m23 - p.z;
                            float sc = ViewDistance.InstanceScale(Mathf.Sqrt(ix * ix + iz * iz), radius, mat.m03, mat.m23);
                            if (sc < 0.02f) continue;
                            if (sc < 0.999f) ViewDistance.ScaleAboutPosition(ref mat, sc);
                        }
                        batch[m][batchFill[m]++] = mat;
                        if (batchFill[m] == BatchSize) { drawn += BatchSize; Flush(m, cam); }
                    }
                }
            }
            for (int m = 0; m < models.Length; m++) { drawn += batchFill[m]; Flush(m, cam); }
            Drawn = drawn;

            if (cache.Count > CacheLimit)
            {
                evict.Clear();
                foreach (var key in cache.Keys)
                {
                    int cx = (int)(key >> 32), cz = (int)(key & 0xFFFFFFFF);
                    if (Mathf.Abs((cx + 0.5f) * CellSize - p.x) > radius + 64f || Mathf.Abs((cz + 0.5f) * CellSize - p.z) > radius + 64f) evict.Add(key);
                }
                foreach (var key in evict) cache.Remove(key);
            }
        }

        // Bushes standing within `metres` of a point (counts the spots, builds nothing). For tests and reports.
        public int BushesNear(Vector2 point, float metres)
        {
            int total = 0;
            int x0 = Mathf.FloorToInt((point.x - metres) / CellSize), x1 = Mathf.FloorToInt((point.x + metres) / CellSize), z0 = Mathf.FloorToInt((point.y - metres) / CellSize), z1 = Mathf.FloorToInt((point.y + metres) / CellSize);
            float r2 = metres * metres;
            for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
                if (cells.TryGetValue(Key(cx, cz), out var cell))
                    foreach (var s in cell.spots) if ((s.x - point.x) * (s.x - point.x) + (s.z - point.y) * (s.z - point.y) <= r2) total++;
            return total;
        }
    }
}
