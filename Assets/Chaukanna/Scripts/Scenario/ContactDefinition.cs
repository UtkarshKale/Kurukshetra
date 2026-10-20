using UnityEngine;

namespace Chaukanna.Scenario
{
    [CreateAssetMenu(menuName = "Chaukanna/Contact Definition", fileName = "ContactDefinition")]
    public class ContactDefinition : ScriptableObject
    {
        public ContactClass contactClass;
        public string displayName;

        [Header("Models: drag prefabs here")]
        [Tooltip("The model for this class. Drag any prefab or model from the Project panel. Only if this and Variants are both empty is a grey placeholder built from the silhouette below.")]
        public GameObject prefab;
        [Tooltip("Other looks for this class (different drone or bird types). Each contact picks one from the scenario seed, so the same seed shows the same mix. Empty = always the prefab above.")]
        public System.Collections.Generic.List<GameObject> variants = new System.Collections.Generic.List<GameObject>();
        [Header("Placeholder (used only when no model is set)")]
        public Silhouette silhouette;
        [Tooltip("Placeholder identification cue. A design decision: this is what makes look-alike classes separable through binoculars.")]
        public Cue cue;

        [Header("Size and flight")]
        [Tooltip("Span in metres. Models are scaled to this automatically.")]
        public float size = 1f;

        public FlightBehaviour behaviour;
        [Tooltip("Metres per second (min, max).")]
        public Vector2 speedRange = new Vector2(10f, 15f);
        [Tooltip("Metres above ground (min, max).")]
        public Vector2 altitudeRange = new Vector2(30f, 80f);
    }
}
