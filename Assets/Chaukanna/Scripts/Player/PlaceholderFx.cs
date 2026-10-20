using UnityEngine;

namespace Chaukanna.Player
{
    // Throwaway effects built from primitives in code. Cosmetic only.
    public class PlaceholderFx : MonoBehaviour
    {
        float life, age;
        Vector3 startScale, endScale;

        public static void Burst(Vector3 position, float size)
        {
            Chaukanna.Audio.SoundEngine.Impact(position);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Fx_Burst";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = position;
            go.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(1f, 0.85f, 0.4f));
            go.AddComponent<PlaceholderFx>().Play(0.5f, Vector3.one * size * 0.2f, Vector3.one * size);
        }

        public static void Tracer(Vector3 from, Vector3 to)
        {
            Chaukanna.Audio.SoundEngine.Gunshot(from);
            var go = new GameObject("Fx_Tracer");
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Unlit(new Color(1f, 0.95f, 0.6f));
            line.positionCount = 2;
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            // Widens with distance so it reads as a streak at any range.
            line.startWidth = 0.02f;
            line.endWidth = Mathf.Max(0.3f, Vector3.Distance(from, to) * 0.002f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.AddComponent<PlaceholderFx>().Play(0.2f, Vector3.one, Vector3.one);
        }

        static Material Unlit(Color colour)
        {
            var shader = Chaukanna.RuntimeMaterials.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Chaukanna.RuntimeMaterials.Find("Sprites/Default");
            return new Material(shader) { color = colour };
        }

        void Play(float life, Vector3 startScale, Vector3 endScale)
        {
            this.life = life;
            this.startScale = startScale;
            this.endScale = endScale;
            transform.localScale = startScale;
        }

        void Update()
        {
            age += Time.deltaTime;
            transform.localScale = Vector3.Lerp(startScale, endScale, age / life);
            if (age >= life) Destroy(gameObject);
        }

        void OnDestroy()
        {
            var r = GetComponent<Renderer>();
            if (r != null && r.sharedMaterial != null) Destroy(r.sharedMaterial);
        }
    }
}
