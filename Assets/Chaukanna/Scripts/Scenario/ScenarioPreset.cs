using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scenario
{
    [CreateAssetMenu(menuName = "Chaukanna/Scenario Preset", fileName = "ScenarioPreset")]
    public class ScenarioPreset : ScriptableObject
    {
        [Header("Run")]
        public int seed = 1;
        [Tooltip("Seconds.")]
        public float duration = 300f;

        [Header("Mix")]
        public List<ContactClass> hostileClassesEnabled = new List<ContactClass>
        {
            ContactClass.HostileQuad, ContactClass.HostileFixedWing,
        };
        [Tooltip("Hostile prevalence: number of hostiles in the run.")]
        public int hostileCount = 3;
        public int friendlyCount = 2;
        public int civilianCount = 2;
        [Tooltip("Bird activity: number of birds in the run.")]
        public int birdCount = 6;

        [Header("Environment (stored only for now)")]
        [Range(0f, 24f)] public float timeOfDayHours = 10f;
        public float visibilityMetres = 5000f;

        [Header("Geometry")]
        [Tooltip("Arc in front of the observer that contacts appear in, degrees.")]
        [Range(10f, 360f)] public float sectorArcDegrees = 120f;
        public float spawnRingRadius = 300f;

        [Header("Spawn time window (s)")]
        public float spawnWindowStart = 3f;
        public float spawnWindowEnd = 240f;

        [Header("Site (rural / urban surroundings)")]
        [Tooltip("Buildings, trees and bushes used when a run has a Rural or Urban site. Empty = simple shapes.")]
        public SiteProfile siteProfile;

        [Header("Contact definitions (one per class)")]
        public List<ContactDefinition> definitions = new List<ContactDefinition>();

        public ContactDefinition GetDefinition(ContactClass c)
        {
            foreach (var d in definitions)
                if (d != null && d.contactClass == c) return d;
            return null;
        }
    }
}
