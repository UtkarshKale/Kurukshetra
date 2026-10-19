using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // Where the post stands: open ground (None, as before), the rural side of a cantonment (fields and a canal in front,
    // the cantonment behind) or its urban edge (the cantonment's buildings in front, drones come in over them).
    public enum SiteType { None, Rural, Urban }

    // What a site is built from. Models are optional: anything missing is made from simple shapes.
    [CreateAssetMenu(menuName = "Chaukanna/Site Profile", fileName = "SiteProfile")]
    public class SiteProfile : ScriptableObject
    {
        [Header("Models (any prefab or model; scaled to the sizes below)")]
        public List<GameObject> buildings = new List<GameObject>();
        public List<GameObject> trees = new List<GameObject>();
        public List<GameObject> bushes = new List<GameObject>();

        [Header("Cantonment")]
        [Tooltip("Distance from the post to the perimeter wall, metres.")]
        public float wallDistance = 12f;
        [Tooltip("Urban post: the soldier stands on a watchtower this high, so they can see over the wall and the roofs, metres.")]
        public float towerHeight = 8f;
        public float wallHeight = 3f;
        [Tooltip("Width of the cantonment either side of the post's line of sight, metres.")]
        public float halfWidth = 380f;
        [Tooltip("How far the cantonment reaches from the wall, metres.")]
        public float depth = 600f;
        [Tooltip("Size of a town block (building plot plus street), metres.")]
        public float blockSize = 36f;
        [Range(0f, 1f)] public float buildingShare = 0.55f;
        public Vector2 buildingFootprint = new Vector2(10f, 22f);
        public Vector2 buildingHeight = new Vector2(6f, 22f);
        [Tooltip("Street trees, one every this many metres along the streets.")]
        public float streetTreeSpacing = 55f;

        [Header("Rural side")]
        [Tooltip("Tree lines across the fields at these distances from the post, metres.")]
        public List<float> treeLines = new List<float> { 180f, 420f, 700f };
        public float treeLineSpacing = 14f;
        [Tooltip("Canal across the fields at this distance from the post, metres (0 = none).")]
        public float canalDistance = 300f;
        public float canalWidth = 8f;
        public Vector2 treeHeight = new Vector2(8f, 15f);
    }

    // Builds a site around the post at run start. Everything stands on the ground (generated terrain, real terrain or the
    // flat ground), has a collider so it blocks line of sight like a hill does, and comes from the seed, so the same
    // seed always builds the same site.
    public static class SiteBuilder
    {
        public const string RootName = "Site (generated)";
        // The ground the site occupies, as rectangles the terrain layers (trees, buildings) keep clear of: the cantonment on
        // its side of the post and the open fields on the other. Same numbers as Build, so a layer built before the site
        // and the site itself agree.
        public static List<Chaukanna.TerrainGen.SiteClearance.Zone> Clearance(Vector3 origin, Vector3 forward, SiteType type, SiteProfile profile)
        {
            if (type == SiteType.None) return new List<Chaukanna.TerrainGen.SiteClearance.Zone>();
            var p = profile != null ? profile : ScriptableObject.CreateInstance<SiteProfile>();
            Vector3 fwd = forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            float reach = p.canalDistance + p.canalWidth;
            foreach (float d in p.treeLines) reach = Mathf.Max(reach, d + 6f);
            var zones = Chaukanna.TerrainGen.SiteClearance.ForSite(new Vector2(origin.x, origin.z), new Vector2(fwd.x, fwd.z), type == SiteType.Urban ? 1f : -1f, p.wallDistance, p.depth, p.halfWidth, reach);
            if (profile == null) Kill(p);
            return zones;
        }


        public static GameObject Build(Transform post, SiteType type, SiteProfile profile, int seed)
        {
            if (post != null) Chaukanna.TerrainGen.SiteClearance.Set(Clearance(post.position, post.forward, type, profile));   // the terrain layers keep clear of it
            if (type == SiteType.None || post == null) return null;
            var root = new GameObject(RootName);
            var rng = new System.Random(seed ^ 0x517E);
            var p = profile != null ? profile : ScriptableObject.CreateInstance<SiteProfile>();
            Vector3 origin = post.position;
            Vector3 fwd = post.forward; fwd.y = 0f; fwd = fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            var mats = new Materials();

            // Urban: the cantonment lies in front of the post (drones cross the town) and the post is a watchtower on the
            // wall line; rural: the cantonment behind, open fields in front, the post on the ground.
            float side = type == SiteType.Urban ? 1f : -1f;
            EyeHeight = 1.7f;
            if (type == SiteType.Urban)
            {
                BuildTower(root.transform, origin, fwd, p.towerHeight, mats);
                EyeHeight = p.towerHeight + 1.7f;
            }
            BuildCantonment(root.transform, origin, fwd * side, right, p, rng, mats);
            if (type == SiteType.Rural) BuildFields(root.transform, origin, fwd, right, p, rng, mats);
            else BuildFields(root.transform, origin, -fwd, right, p, rng, mats);
            if (profile == null) Kill(p);
            return root;
        }

        // Eye height above the ground at the post for the last site built (1.7 m on the ground, more on a tower).
        public static float EyeHeight { get; private set; } = 1.7f;

        static void BuildTower(Transform root, Vector3 origin, Vector3 fwd, float height, Materials m)
        {
            var tower = new GameObject("Watchtower").transform;
            tower.SetParent(root, false);
            float g = Ground(origin);
            var rot = Quaternion.LookRotation(fwd);
            for (int i = 0; i < 4; i++)
            {
                Vector3 leg = origin + rot * new Vector3((i & 1) == 0 ? -1.8f : 1.8f, 0f, (i & 2) == 0 ? -1.8f : 1.8f);
                Prim(tower, PrimitiveType.Cube, "TowerLeg", new Vector3(leg.x, g + height * 0.5f, leg.z), rot, new Vector3(0.3f, height, 0.3f), m.metal);
            }
            // Floor and a waist-high parapet, open above (the soldier looks over it).
            Prim(tower, PrimitiveType.Cube, "TowerFloor", new Vector3(origin.x, g + height - 0.1f, origin.z), rot, new Vector3(4.2f, 0.2f, 4.2f), m.shed);
            foreach (var side in new[] { new Vector3(0f, 0f, 2.1f), new Vector3(0f, 0f, -2.1f), new Vector3(2.1f, 0f, 0f), new Vector3(-2.1f, 0f, 0f) })
            {
                bool alongX = side.z != 0f;
                var at = origin + rot * side;
                var wall = Prim(tower, PrimitiveType.Cube, "Parapet", new Vector3(at.x, g + height + 0.5f, at.z), rot, alongX ? new Vector3(4.2f, 1f, 0.15f) : new Vector3(0.15f, 1f, 4.2f), m.wall);
                Kill(wall.GetComponent<Collider>());     // never blocks the soldier's own line of sight
            }
        }

        static void BuildCantonment(Transform root, Vector3 origin, Vector3 dir, Vector3 right, SiteProfile p, System.Random rng, Materials m)
        {
            var town = new GameObject("Cantonment").transform;
            town.SetParent(root, false);

            // Perimeter wall facing the post, in 10 m panels that follow the ground.
            for (float x = -p.halfWidth; x <= p.halfWidth; x += 10f)
            {
                Vector3 at = origin + dir * p.wallDistance + right * (x + 5f);
                Box(town, "Wall", at, Quaternion.LookRotation(dir), new Vector3(10.2f, p.wallHeight, 0.4f), m.wall, p.wallHeight * 0.5f);
            }
            // Side walls, so the compound reads as a compound.
            for (float d = p.wallDistance; d <= p.wallDistance + p.depth; d += 10f)
                foreach (float s in new[] { -1f, 1f })
                    Box(town, "Wall", origin + dir * (d + 5f) + right * s * p.halfWidth, Quaternion.LookRotation(right), new Vector3(10.2f, p.wallHeight, 0.4f), m.wall, p.wallHeight * 0.5f);

            // Landmarks near the gate: a water tank on legs and a generator shed.
            Vector3 tankAt = origin + dir * (p.wallDistance + 40f) + right * 45f;
            float g = Ground(tankAt);
            for (int i = 0; i < 4; i++)
            {
                Vector3 leg = tankAt + new Vector3((i & 1) == 0 ? -3f : 3f, 0f, (i & 2) == 0 ? -3f : 3f);
                Prim(town, PrimitiveType.Cylinder, "TankLeg", new Vector3(leg.x, g + 7f, leg.z), Quaternion.identity, new Vector3(0.5f, 7f, 0.5f), m.metal);
            }
            Prim(town, PrimitiveType.Cylinder, "WaterTank", new Vector3(tankAt.x, g + 17f, tankAt.z), Quaternion.identity, new Vector3(9f, 3f, 9f), m.tank);
            Vector3 shedAt = origin + dir * (p.wallDistance + 22f) - right * 30f;
            Box(town, "GeneratorShed", shedAt, Quaternion.LookRotation(dir), new Vector3(7f, 3.2f, 5f), m.shed, 1.6f);
            Prim(town, PrimitiveType.Cylinder, "Exhaust", shedAt + Vector3.up * (Ground(shedAt) - shedAt.y + 4f), Quaternion.identity, new Vector3(0.4f, 1.5f, 0.4f), m.metal);

            // Blocks of buildings with streets between them, and street trees.
            for (float d = p.wallDistance + 70f; d < p.wallDistance + p.depth; d += p.blockSize)
                for (float x = -p.halfWidth + p.blockSize * 0.5f; x < p.halfWidth; x += p.blockSize)
                {
                    double roll = rng.NextDouble(), u1 = rng.NextDouble(), u2 = rng.NextDouble(), u3 = rng.NextDouble(), pick = rng.NextDouble();
                    if (roll > p.buildingShare) continue;
                    Vector3 at = origin + dir * d + right * x;
                    float w = Mathf.Lerp(p.buildingFootprint.x, p.buildingFootprint.y, (float)u1);
                    float h = Mathf.Lerp(p.buildingHeight.x, p.buildingHeight.y, (float)(u2 * u2));
                    var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, 90f * (int)(u3 * 4), 0f);
                    if (p.buildings.Count > 0) Model(town, p.buildings[(int)(pick * p.buildings.Count) % p.buildings.Count], at, rot, w, h, true);
                    else Box(town, "Building", at, rot, new Vector3(w, h, w * 0.8f), m.building, h * 0.5f);
                }
            for (float d = p.wallDistance + 55f; d < p.wallDistance + p.depth; d += p.streetTreeSpacing)
                for (float x = -p.halfWidth + 20f; x < p.halfWidth; x += p.blockSize * 2f)
                    Tree(town, origin + dir * d + right * (x + (float)(rng.NextDouble() - 0.5) * 6f), p, rng, m);
        }

        static void BuildFields(Transform root, Vector3 origin, Vector3 dir, Vector3 right, SiteProfile p, System.Random rng, Materials m)
        {
            var fields = new GameObject("Fields").transform;
            fields.SetParent(root, false);
            // Tree lines across the fields, with gaps (field entrances).
            foreach (float d in p.treeLines)
                for (float x = -p.halfWidth * 1.4f; x <= p.halfWidth * 1.4f; x += p.treeLineSpacing)
                {
                    if (rng.NextDouble() < 0.15) continue;
                    Tree(fields, origin + dir * (d + (float)(rng.NextDouble() - 0.5) * 6f) + right * x, p, rng, m);
                }
            // A canal: flat water strips that follow the ground, with banks of bushes.
            if (p.canalDistance > 0f)
                for (float x = -p.halfWidth * 1.5f; x <= p.halfWidth * 1.5f; x += 12f)
                {
                    Vector3 at = origin + dir * p.canalDistance + right * (x + 6f);
                    var water = Prim(fields, PrimitiveType.Cube, "Canal", new Vector3(at.x, Ground(at) + 0.05f, at.z), Quaternion.LookRotation(dir), new Vector3(12.2f, 0.1f, p.canalWidth), m.water);
                    Kill(water.GetComponent<Collider>());      // water does not block sight
                    if (p.bushes.Count > 0 && rng.NextDouble() < 0.35)
                        Model(fields, p.bushes[rng.Next(p.bushes.Count)], at + dir * (p.canalWidth * 0.5f + 2f), Quaternion.Euler(0f, rng.Next(360), 0f), 3f, 2f, false);
                }
        }

        static void Tree(Transform parent, Vector3 at, SiteProfile p, System.Random rng, Materials m)
        {
            float h = Mathf.Lerp(p.treeHeight.x, p.treeHeight.y, (float)rng.NextDouble());
            var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
            if (p.trees.Count > 0) { Model(parent, p.trees[rng.Next(p.trees.Count)], at, rot, h * 0.6f, h, false); return; }
            float g = Ground(at);
            Prim(parent, PrimitiveType.Cylinder, "Trunk", new Vector3(at.x, g + h * 0.25f, at.z), rot, new Vector3(0.5f, h * 0.25f, 0.5f), m.trunk);
            Prim(parent, PrimitiveType.Sphere, "Crown", new Vector3(at.x, g + h * 0.7f, at.z), rot, new Vector3(h * 0.55f, h * 0.6f, h * 0.55f), m.leaves);
        }

        // A model scaled to a footprint (width) and height, standing on the ground, with a box collider round it.
        static void Model(Transform parent, GameObject prefab, Vector3 at, Quaternion rot, float width, float height, bool building)
        {
            if (prefab == null) return;
            var go = Object.Instantiate(prefab, parent);
            go.transform.rotation = rot * prefab.transform.rotation;      // keep the model's own import rotation (many are authored Z-up)
            go.transform.localScale = Vector3.one;
            var b = Bounds(go);
            if (b.size.y < 1e-3f) { Kill(go); return; }
            float sxz = width / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z)), sy = height / b.size.y;
            go.transform.localScale = building ? new Vector3(sxz, sy, sxz) : Vector3.one * sy;
            b = Bounds(go);
            float g = LowestGround(at, Mathf.Max(b.extents.x, b.extents.z));
            go.transform.position += new Vector3(at.x - b.center.x, g - b.min.y - (building ? 0.5f : 0.2f), at.z - b.center.z);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Kill(c);
            b = Bounds(go);
            var box = go.AddComponent<BoxCollider>();
            box.center = go.transform.InverseTransformPoint(b.center);
            var s = go.transform.lossyScale;
            Vector3 local = Quaternion.Inverse(go.transform.rotation) * b.size;
            box.size = new Vector3(Mathf.Abs(local.x) / Mathf.Max(1e-4f, s.x), Mathf.Abs(local.y) / Mathf.Max(1e-4f, s.y), Mathf.Abs(local.z) / Mathf.Max(1e-4f, s.z)) * (building ? 0.98f : 0.6f);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = building ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.On;
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        // A box standing on the ground (sunk a little where the ground slopes), with its collider.
        static Transform Box(Transform parent, string name, Vector3 at, Quaternion rot, Vector3 size, Material mat, float halfHeight)
        {
            float g = LowestGround(at, Mathf.Max(size.x, size.z) * 0.5f);
            return Prim(parent, PrimitiveType.Cube, name, new Vector3(at.x, g + halfHeight - 0.3f, at.z), rot, size, mat);
        }

        static Transform Prim(Transform parent, PrimitiveType type, string name, Vector3 at, Quaternion rot, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(at, rot);
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        static void Kill(Object o) { if (o == null) return; if (Application.isPlaying) Object.Destroy(o); else Object.DestroyImmediate(o); }

        public static float Ground(Vector3 at)
        {
            float g = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(at);
            return float.IsNaN(g) ? 0f : g;
        }

        static float LowestGround(Vector3 at, float radius)
        {
            float g = Ground(at);
            for (int i = 0; i < 4; i++)
                g = Mathf.Min(g, Ground(at + new Vector3((i & 1) == 0 ? -radius : radius, 0f, (i & 2) == 0 ? -radius : radius)));
            return g;
        }

        class Materials
        {
            public Material wall = Mat(new Color(0.62f, 0.58f, 0.52f)), building = Mat(new Color(0.72f, 0.68f, 0.6f)), tank = Mat(new Color(0.55f, 0.6f, 0.62f)),
                metal = Mat(new Color(0.3f, 0.3f, 0.32f)), shed = Mat(new Color(0.42f, 0.45f, 0.38f)), water = Mat(new Color(0.18f, 0.3f, 0.36f)),
                trunk = Mat(new Color(0.3f, 0.22f, 0.15f)), leaves = Mat(new Color(0.2f, 0.32f, 0.14f));

            static Material Mat(Color c)
            {
                var shader = Chaukanna.RuntimeMaterials.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Chaukanna.RuntimeMaterials.Find("Standard");
                var m = new Material(shader) { color = c };
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.1f);
                return m;
            }
        }
    }
}
