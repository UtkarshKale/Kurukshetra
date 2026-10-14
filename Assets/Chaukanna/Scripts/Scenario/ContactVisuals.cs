using UnityEngine;

namespace Chaukanna.Scenario
{
    // Placeholder silhouettes built from primitives. Model forward is +Z.
    public static class ContactVisuals
    {
        public static readonly Color Neutral = new Color(0.35f, 0.35f, 0.37f);
        public const string WingLeftName = "WingPivotL";
        public const string WingRightName = "WingPivotR";

        // Debug only (F1). Never shown to the player by default.
        public static Color TruthColour(ContactClass c)
        {
            switch (c)
            {
                case ContactClass.HostileQuad: return new Color(0.9f, 0.1f, 0.1f);
                case ContactClass.HostileFixedWing: return new Color(1f, 0.45f, 0f);
                case ContactClass.FriendlyISR: return new Color(0.15f, 0.4f, 1f);
                case ContactClass.CivilianQuad: return new Color(0.1f, 0.75f, 0.2f);
                case ContactClass.Bird: return new Color(1f, 0.9f, 0.1f);
                default: return Color.magenta;
            }
        }

        public const string RotorPrefix = "Rotor";

        // Which look a contact gets. All looks of a class are the same size for detection; only the shape differs.
        // A non-empty override list (the spawner's "Models" lists) replaces the definition's own prefab and variants.
        public static GameObject PickPrefab(ContactDefinition def, int variantSeed, System.Collections.Generic.List<GameObject> overrideModels = null)
        {
            var choices = new System.Collections.Generic.List<GameObject>();
            if (overrideModels != null)
                foreach (var m in overrideModels) if (m != null) choices.Add(m);
            if (choices.Count == 0)
            {
                if (def.variants != null)
                    foreach (var m in def.variants) if (m != null) choices.Add(m);
                if (def.prefab != null) choices.Add(def.prefab);
            }
            return choices.Count == 0 ? null : choices[(variantSeed & 0x7FFFFFFF) % choices.Count];
        }

        // Returns the root. Every material on the contact is replaced by its own copy, so tinting one contact (F1 debug)
        // never touches another contact or the asset.
        public static GameObject Build(ContactDefinition def, out Material[] materials, out Transform wingL, out Transform wingR, int variantSeed = 0,
            System.Collections.Generic.List<GameObject> overrideModels = null)
        {
            GameObject prefab = PickPrefab(def, variantSeed, overrideModels);
            GameObject root = prefab != null ? Fit(Object.Instantiate(prefab)) : BuildPlaceholder(def.silhouette, def.cue);
            root.transform.localScale = Vector3.one * def.size;
            wingL = FindDeep(root.transform, WingLeftName);
            wingR = FindDeep(root.transform, WingRightName);

            var copies = new System.Collections.Generic.Dictionary<Material, Material>();
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                var shared = r.sharedMaterials;
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == null) continue;
                    if (!copies.TryGetValue(shared[i], out Material copy)) copies[shared[i]] = copy = new Material(shared[i]);
                    shared[i] = copy;
                }
                r.sharedMaterials = shared;
            }
            materials = new Material[copies.Count];
            copies.Values.CopyTo(materials, 0);
            return root;
        }

        // Any model can be dragged in, whatever size it was made at: if it is not already about one unit across, it is put
        // under a new root, centred and scaled to one unit, so the definition's Size (metres) means the same for every model.
        static GameObject Fit(GameObject model)
        {
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return model;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            float span = Mathf.Max(b.size.x, b.size.z);
            if (span <= 0.0001f || (span >= 0.6f && span <= 1.8f)) return model;

            var root = new GameObject();
            model.transform.SetParent(root.transform, false);
            float k = 1f / span;
            model.transform.localScale *= k;
            model.transform.localPosition = -b.center * k;
            return root;
        }

        static Transform FindDeep(Transform root, string name)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name == name) return t;
            return null;
        }
        // Rotor parts of a model (objects whose name starts with "Rotor"), for spinning.
        public static Transform[] FindRotors(GameObject root)
        {
            var list = new System.Collections.Generic.List<Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith(RotorPrefix, System.StringComparison.OrdinalIgnoreCase)) list.Add(t);
            return list.ToArray();
        }

        // Also used in the Editor to bake the placeholder prefabs.
        public static GameObject BuildPlaceholder(Silhouette silhouette, Cue cue = Cue.None)
        {
            var root = new GameObject();
            switch (silhouette)
            {
                case Silhouette.Quad: BuildQuad(root.transform); break;
                case Silhouette.FixedWing: BuildFixedWing(root.transform); break;
                default: BuildBird(root.transform); break;
            }
            BuildCue(root.transform, cue);
            return root;
        }

        public static void BuildCue(Transform root, Cue cue)
        {
            switch (cue)
            {
                case Cue.PayloadBox: Part(PrimitiveType.Cube, root, new Vector3(0f, -0.14f, 0f), new Vector3(0.2f, 0.16f, 0.3f)); break;
                case Cue.GimbalBall: Part(PrimitiveType.Sphere, root, new Vector3(0f, -0.13f, 0.12f), Vector3.one * 0.16f); break;
                case Cue.AntennaStick: Part(PrimitiveType.Cube, root, new Vector3(0f, 0.2f, -0.1f), new Vector3(0.025f, 0.35f, 0.025f)); break;
            }
        }

        static void BuildQuad(Transform root)
        {
            Part(PrimitiveType.Cube, root, Vector3.zero, new Vector3(0.5f, 0.12f, 0.5f));
            for (int i = 0; i < 4; i++)
            {
                float x = (i & 1) == 0 ? -0.4f : 0.4f;
                float z = (i & 2) == 0 ? -0.4f : 0.4f;
                Part(PrimitiveType.Cylinder, root, new Vector3(x, 0.08f, z), new Vector3(0.35f, 0.01f, 0.35f));
            }
        }

        static void BuildFixedWing(Transform root)
        {
            Part(PrimitiveType.Cube, root, Vector3.zero, new Vector3(0.12f, 0.12f, 1f));                       // fuselage
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, 0.1f), new Vector3(1.4f, 0.03f, 0.25f));        // wing
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0f, -0.45f), new Vector3(0.45f, 0.03f, 0.15f));     // tailplane
            Part(PrimitiveType.Cube, root, new Vector3(0f, 0.1f, -0.45f), new Vector3(0.03f, 0.2f, 0.15f));    // fin
        }

        static void BuildBird(Transform root)
        {
            Part(PrimitiveType.Sphere, root, Vector3.zero, new Vector3(0.15f, 0.15f, 0.4f));
            Wing(root, WingLeftName, -1f);
            Wing(root, WingRightName, 1f);
        }

        // Pivot at the shoulder so ContactMotion can flap by rotating it about Z.
        static void Wing(Transform root, string name, float side)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(root, false);
            pivot.localPosition = new Vector3(0.07f * side, 0f, 0f);
            Part(PrimitiveType.Quad, pivot, new Vector3(0.3f * side, 0f, 0f), new Vector3(0.6f, 0.22f, 1f))
                .localRotation = Quaternion.Euler(-90f, 0f, 0f);   // quads are one-sided: face the ground observer
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(type);
            var collider = go.GetComponent<Collider>();
            if (Application.isPlaying) Object.Destroy(collider); else Object.DestroyImmediate(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            return go.transform;
        }
    }
}
