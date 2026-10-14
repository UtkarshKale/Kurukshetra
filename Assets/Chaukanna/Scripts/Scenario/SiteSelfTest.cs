using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // Checks the rural / urban site builder far away from the play area (flat ground, simple shapes): the same seed builds
    // the same site, everything stands on the ground, the wall and buildings block line of sight and the fields do not,
    // and the urban post is raised. CLI: Chaukanna.Scenario.SiteSelfTest.Run()
    public static class SiteSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var post = new GameObject("SiteTestPost").transform;
            post.position = new Vector3(100000f, 0f, 100000f);       // well away from any terrain: ground is y = 0
            post.rotation = Quaternion.identity;
            var built = new List<GameObject>();
            try
            {
                var urban = Build(post, SiteType.Urban, 11, built);
                var urban2 = Build(post, SiteType.Urban, 11, built);
                var other = Build(post, SiteType.Urban, 12, built);
                Check("same seed builds the same site, another seed a different one", Same(urban, urban2) && !Same(urban, other));
                Check("urban post is a watchtower (eye above 8 m)", SiteBuilder.EyeHeight > 8f);

                int floating = 0, sunk = 0, n = 0;
                foreach (var r in urban.GetComponentsInChildren<Renderer>())
                {
                    if (r.name == "WaterTank" || r.name == "Exhaust" || r.name.StartsWith("Tower") || r.name == "Parapet" || r.name == "Crown") continue;   // up on legs by design
                    n++;
                    if (r.bounds.min.y > 0.5f) floating++;
                    if (r.bounds.max.y < 0f) sunk++;
                }
                Check($"all {n} ground objects stand on the ground ({floating} floating, {sunk} buried)", floating == 0 && sunk == 0);

                Physics.SyncTransforms();
                Vector3 eye = post.position + Vector3.up * SiteBuilder.EyeHeight;
                // Over the town at roof height: something is in the way. Low over the wall from the ground: the wall.
                Check("line of sight across the town at roof height is blocked", Physics.Linecast(post.position + Vector3.up * 6f, post.position + new Vector3(0f, 6f, 500f)));
                Check("the soldier on the tower sees out over the wall to a drone at 80 m up", !Physics.Linecast(eye, post.position + new Vector3(0f, 80f, 400f)));

                foreach (var g in built) Object.DestroyImmediate(g);
                built.Clear();
                var rural = Build(post, SiteType.Rural, 11, built);
                Physics.SyncTransforms();
                Check("rural post stands on the ground", Mathf.Approximately(SiteBuilder.EyeHeight, 1.7f));
                Check("rural: open view over the fields to a drone 60 m up at 600 m", !Physics.Linecast(post.position + Vector3.up * 1.7f, post.position + new Vector3(0f, 60f, 600f)));
                Check("rural: the cantonment is behind the post", Physics.Linecast(post.position + Vector3.up * 2f, post.position + new Vector3(0f, 2f, -300f)));
                Check("no site when None", SiteBuilder.Build(post, SiteType.None, null, 11) == null);
            }
            finally
            {
                foreach (var g in built) if (g != null) Object.DestroyImmediate(g);
                Object.DestroyImmediate(post.gameObject);
            }
            string summary = $"[SiteSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static GameObject Build(Transform post, SiteType type, int seed, List<GameObject> built)
        {
            var g = SiteBuilder.Build(post, type, null, seed);
            built.Add(g);
            return g;
        }

        static bool Same(GameObject a, GameObject b)
        {
            var ta = a.GetComponentsInChildren<Transform>();
            var tb = b.GetComponentsInChildren<Transform>();
            if (ta.Length != tb.Length) return false;
            for (int i = 0; i < ta.Length; i++)
                if ((ta[i].position - tb[i].position).sqrMagnitude > 1e-6f || ta[i].name != tb[i].name) return false;
            return true;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
