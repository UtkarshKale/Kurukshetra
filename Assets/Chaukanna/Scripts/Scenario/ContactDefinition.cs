using UnityEngine;

namespace Chaukanna.Scenario
{
    [CreateAssetMenu(menuName = "Chaukanna/Contact Definition", fileName = "ContactDefinition")]
    public class ContactDefinition : ScriptableObject
    {
        public ContactClass contactClass;
        public string displayName;

        [Tooltip("Optional. If empty, a placeholder is built from primitives using the silhouette below.")]
        public GameObject prefab;
        public Silhouette silhouette;
        [Tooltip("Placeholder identification cue. A design decision: this is what makes look-alike classes separable through binoculars.")]
        public Cue cue;

        [Tooltip("Approximate span in metres (uniform scale of the placeholder).")]
        public float size = 1f;

        public FlightBehaviour behaviour;
        [Tooltip("Metres per second (min, max).")]
        public Vector2 speedRange = new Vector2(10f, 15f);
        [Tooltip("Metres above ground (min, max).")]
        public Vector2 altitudeRange = new Vector2(30f, 80f);
    }
}
