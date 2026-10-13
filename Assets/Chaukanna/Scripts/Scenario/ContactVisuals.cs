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

        // Returns the root; one material instance is shared by all parts of the contact.
        public static GameObject Build(ContactDefinition def, out Material material, out Transform wingL, out Transform wingR)
        {
            GameObject root = def.prefab != null ? Object.Instantiate(def.prefab) : BuildPlaceholder(def.silhouette, def.cue);
            root.transform.localScale = Vector3.one * def.size;
            wingL = root.transform.Find(WingLeftName);
            wingR = root.transform.Find(WingRightName);

            material = null;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                if (material == null) material = new Material(r.sharedMaterial);
                r.sharedMaterial = material;
            }
            return root;
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

        static void BuildCue(Transform root, Cue cue)
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
