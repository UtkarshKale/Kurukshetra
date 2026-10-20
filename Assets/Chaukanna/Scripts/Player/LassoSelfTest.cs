using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Player
{
    // Checks for the lasso: loop maths, depth band, the visibility filter (a hidden drone inside the loop is never picked),
    // add / remove, whole swarms in one loop, and the time it takes. Builds its own camera, wall and contacts far away from
    // the scene and removes them (and their registry entries) again, so it can run in Edit or Play mode.
    // CLI: Chaukanna.Player.LassoSelfTest.Run()
    public static class LassoSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();

            // Loop maths.
            var square = new List<Vector2> { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 100), new Vector2(0, 100) };
            Check("convex loop: centre inside, outside point outside", LassoMath.Inside(new Vector2(50, 50), square) && !LassoMath.Inside(new Vector2(150, 50), square));
            // A "C": the notch on the right is outside although it is inside the loop's bounding box.
            var cShape = new List<Vector2> { new Vector2(0, 0), new Vector2(100, 0), new Vector2(100, 30), new Vector2(30, 30), new Vector2(30, 70), new Vector2(100, 70), new Vector2(100, 100), new Vector2(0, 100) };
            Check("concave loop: notch outside, arms and spine inside",
                !LassoMath.Inside(new Vector2(70, 50), cShape) && LassoMath.Inside(new Vector2(70, 15), cShape) && LassoMath.Inside(new Vector2(70, 85), cShape) && LassoMath.Inside(new Vector2(15, 50), cShape));
            Check("fewer than 3 points selects nothing", !LassoMath.Inside(new Vector2(1, 1), new List<Vector2> { Vector2.zero, new Vector2(5, 5) }));
            var pts = new List<Vector2>();
            LassoMath.AddPoint(pts, Vector2.zero, 4f); LassoMath.AddPoint(pts, new Vector2(2, 0), 4f); LassoMath.AddPoint(pts, new Vector2(5, 0), 4f);
            Check("a slow hand does not pile up points (2 of 3 kept)", pts.Count == 2);
            Check("area of a 100 x 100 loop is 10000", Mathf.Abs(LassoMath.Area(square) - 10000f) < 0.01f);

            // Modifiers.
            var sel = new List<int> { 1, 2 };
            LassoMath.Apply(sel, new List<int> { 3, 4 }, LassoMath.ModeFrom(false, false));
            bool replace = sel.Count == 2 && sel.Contains(3) && sel.Contains(4);
            LassoMath.Apply(sel, new List<int> { 4, 5 }, LassoMath.ModeFrom(true, false));
            bool add = sel.Count == 3 && sel.Contains(5);
            LassoMath.Apply(sel, new List<int> { 3, 9 }, LassoMath.ModeFrom(false, true));
            bool remove = sel.Count == 2 && !sel.Contains(3);
            Check("no key replaces, Shift adds (no duplicates), Ctrl removes", replace && add && remove);

            // Scene checks: a camera far from the map, looking north.
            int liveBefore = ContactRegistry.Live.Count, recordsBefore = ContactRegistry.Records.Count, eventsBefore = ContactRegistry.Events.Count;
            var made = new List<GameObject>();
            var def = ScriptableObject.CreateInstance<ContactDefinition>();
            def.contactClass = ContactClass.HostileQuad;
            def.size = 1.5f;
            try
            {
                Vector3 eye = new Vector3(60000f, 3000f, 60000f);
                var camGo = new GameObject("LassoSelfTestEye", typeof(Camera));
                made.Add(camGo);
                camGo.transform.SetPositionAndRotation(eye, Quaternion.identity);
                var cam = camGo.GetComponent<Camera>();
                cam.enabled = false;
                cam.fieldOfView = 60f;
                var sensor = camGo.AddComponent<ContactSensor>();

                Contact Make(Vector3 at)
                {
                    var go = new GameObject("LassoSelfTestContact");
                    made.Add(go);
                    go.transform.position = at;
                    var c = go.AddComponent<Contact>();
                    c.Initialise(9000 + made.Count, def, null);
                    return c;
                }

                // Visibility filter: two drones side by side at 300 m, a wall in front of the right-hand one.
                var open = Make(eye + new Vector3(-20f, 0f, 300f));
                var hidden = Make(eye + new Vector3(20f, 0f, 300f));
                var behind = Make(eye + new Vector3(0f, 0f, -300f));
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                made.Add(wall);
                wall.transform.position = eye + new Vector3(20f, 0f, 150f);
                wall.transform.localScale = new Vector3(30f, 20f, 2f);   // x 5..35 m: covers the right-hand line (x 10 m here), not the left (x -10 m)
                Physics.SyncTransforms();

                var all = new List<Contact> { open, hidden, behind };
                var seen = new List<Contact>();
                sensor.SeeNow(all, seen);
                var whole = ScreenLoop(cam, new Vector2(0f, 0f), new Vector2(cam.pixelWidth, cam.pixelHeight));
                var picked = new List<Contact>();
                LassoMath.Pick(seen, cam, whole, 0f, 2000f, picked);
                Check("a drone behind a wall, inside the loop, is not selected; the one in the open is",
                    picked.Count == 1 && picked[0] == open && seen.Contains(open) && !seen.Contains(hidden));
                Check("a drone behind the soldier is never selected", !seen.Contains(behind) && !picked.Contains(behind));
                Object.DestroyImmediate(wall);
                Physics.SyncTransforms();
                sensor.SeeNow(all, seen);
                LassoMath.Pick(seen, cam, whole, 0f, 2000f, picked);
                Check("with the wall gone, the same loop picks both", picked.Count == 2);

                // Depth band: a near and a far drone on the same line of sight.
                var near = Make(eye + new Vector3(0f, 5f, 200f));
                var far = Make(eye + new Vector3(0f, 12f, 700f));
                var pair = new List<Contact> { near, far };
                LassoMath.Pick(pair, cam, whole, 0f, 400f, picked);
                bool nearOnly = picked.Count == 1 && picked[0] == near;
                LassoMath.Pick(pair, cam, whole, 400f, 2000f, picked);
                bool farOnly = picked.Count == 1 && picked[0] == far;
                LassoMath.Pick(pair, cam, whole, 0f, 2000f, picked);
                Check("depth band 0-400 m picks only the near drone, 400-2000 m only the far one, no band both", nearOnly && farOnly && picked.Count == 2);

                // A loop that misses: drawn round empty sky on the left.
                var left = ScreenLoop(cam, new Vector2(0f, 0f), new Vector2(cam.pixelWidth * 0.05f, cam.pixelHeight * 0.05f));
                LassoMath.Pick(seen, cam, left, 0f, 2000f, picked);
                Check("a loop round empty sky picks nothing", picked.Count == 0);

                // Add / remove through the sensor's own selection.
                sensor.ClearSelection();
                sensor.ApplyLasso(new List<Contact> { open }, LassoMode.Replace, "test");
                sensor.ApplyLasso(new List<Contact> { hidden }, LassoMode.Add, "test");
                bool two = sensor.Selected.Count == 2;
                sensor.ApplyLasso(new List<Contact> { open }, LassoMode.Remove, "test");
                bool one = sensor.Selected.Count == 1 && sensor.Selected[0] == hidden;
                sensor.ApplyLasso(new List<Contact> { near, far }, LassoMode.Replace, "test");
                Check("sensor group: replace, add, remove and replace again", two && one && sensor.Selected.Count == 2 && sensor.Targets.Count == 2 && near.FirstLockTime >= 0f);

                // Swarms: 24 and 48 drones in a cluster 400 m out, one loop round the cluster.
                foreach (int size in new[] { 24, 48 })
                {
                    var swarm = new List<Contact>();
                    var rng = new System.Random(size);
                    for (int i = 0; i < size; i++)
                        swarm.Add(Make(eye + new Vector3((float)(rng.NextDouble() - 0.5) * 80f, (float)(rng.NextDouble() - 0.5) * 40f + 30f, 400f + (float)(rng.NextDouble() - 0.5) * 60f)));
                    sensor.SeeNow(swarm, seen);
                    var loop = LoopRound(cam, swarm, 30f);
                    LassoMath.Pick(seen, cam, loop, 0f, 2000f, picked);
                    sensor.ApplyLasso(picked, LassoMode.Replace, "test");
                    Check($"{size}-drone swarm: all {size} selected with one loop", seen.Count == size && picked.Count == size && sensor.Selected.Count == size);
                    if (size == 48)
                    {
                        // Timing: one pick per frame while drawing, 48 contacts, a 200-point loop.
                        var circle = new List<Vector2>();
                        Vector2 mid = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f);
                        for (int i = 0; i < 200; i++) { float a = i * Mathf.PI * 2f / 200f; circle.Add(mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * cam.pixelHeight * 0.45f); }
                        const int runs = 500;
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        for (int i = 0; i < runs; i++) LassoMath.Pick(seen, cam, circle, 0f, 2000f, picked);
                        sw.Stop();
                        double ms = sw.Elapsed.TotalMilliseconds / runs;
                        Check($"48 contacts, 200-point loop: {ms:F4} ms per frame (budget 0.2 ms)", ms < 0.2);
                    }
                }
                sensor.ClearSelection();
            }
            finally
            {
                // Remove every trace from the registry before the objects go, so a running session is untouched.
                var ours = new HashSet<Contact>();
                foreach (var go in made) if (go != null && go.TryGetComponent<Contact>(out var c)) ours.Add(c);
                ContactRegistry.Live.RemoveAll(c => ours.Contains(c));
                if (ContactRegistry.Records.Count > recordsBefore) ContactRegistry.Records.RemoveRange(recordsBefore, ContactRegistry.Records.Count - recordsBefore);
                if (ContactRegistry.Events.Count > eventsBefore) ContactRegistry.Events.RemoveRange(eventsBefore, ContactRegistry.Events.Count - eventsBefore);
                foreach (var go in made) if (go != null) Object.DestroyImmediate(go);
                Object.DestroyImmediate(def);
                Physics.SyncTransforms();
            }
            Check("registry left as it was", ContactRegistry.Live.Count == liveBefore && ContactRegistry.Records.Count == recordsBefore && ContactRegistry.Events.Count == eventsBefore);

            string summary = $"[LassoSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        // A rectangle as a loop, in screen pixels.
        static List<Vector2> ScreenLoop(Camera cam, Vector2 min, Vector2 max)
        {
            return new List<Vector2> { min, new Vector2(max.x, min.y), max, new Vector2(min.x, max.y) };
        }

        // A loop round the contacts' screen positions with a margin: the hand-drawn loop a soldier would make.
        static List<Vector2> LoopRound(Camera cam, List<Contact> contacts, float margin)
        {
            Vector2 mid = Vector2.zero;
            var sp = new List<Vector2>();
            foreach (var c in contacts) { Vector3 p = cam.WorldToScreenPoint(c.transform.position); sp.Add(p); mid += (Vector2)p; }
            mid /= sp.Count;
            float radius = 0f;
            foreach (var p in sp) radius = Mathf.Max(radius, (p - mid).magnitude);
            radius += margin;
            var loop = new List<Vector2>();
            for (int i = 0; i < 64; i++)
            {
                float a = i * Mathf.PI * 2f / 64f;
                // Wobble so it is not a perfect circle (concave in places), like a real hand.
                float r = radius * (1f + 0.08f * Mathf.Sin(a * 5f) + 0.1f);
                loop.Add(mid + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            return loop;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
