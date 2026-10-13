using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Scenario
{
    // Reads a preset and plays it back. The whole run is planned up front from System.Random(seed),
    // so the same seed always gives the same contacts, times, bearings and speeds.
    // Sector is centred on this transform's forward; the ring is centred on its position.
    public class ScenarioSpawner : MonoBehaviour
    {
        [Tooltip("Source of the contact definitions, and of every value below when Override Preset is off.")]
        public ScenarioPreset preset;
        [Tooltip("What hostiles fly at. Defaults to this transform.")]
        public Transform defendedPoint;

        [Header("Inspector override")]
        [Tooltip("On: the values below replace the preset's for this run (the preset asset is not modified). Off: the preset is used unchanged.")]
        public bool overridePreset = true;

        [Header("Run")]
        [Tooltip("Same seed = same contacts, times and bearings.")]
        public int seed = 1001;
        [Tooltip("Pick a new seed every time Play starts. The seed used is printed to the Console.")]
        public bool randomizeSeed;
        [Tooltip("Scenario length in seconds.")]
        public float duration = 300f;
        [Tooltip("Multiplies every contact's speed.")]
        [Range(0.1f, 5f)] public float speedMultiplier = 1f;

        [Header("Mix")]
        public int hostileCount = 3;
        public int friendlyCount = 2;
        public int civilianCount = 2;
        public int birdCount = 6;

        [Header("Geometry")]
        [Tooltip("Arc in front of this transform that contacts appear in, degrees.")]
        [Range(10f, 360f)] public float sectorArcDegrees = 120f;
        [Tooltip("Distance contacts spawn at, metres.")]
        public float spawnRingRadius = 1000f;

        [Header("Spawn time window (s)")]
        public float spawnWindowStart = 3f;
        public float spawnWindowEnd = 240f;

        ScenarioPreset active;   // runtime copy of the preset with overrides applied

        class PlannedSpawn
        {
            public float time;
            public ContactDefinition def;
            public Vector3 start;
            public Vector3 goal;
            public float speed;
            public float orbitRadius;
            public int motionSeed;
        }

        List<PlannedSpawn> plan;
        int next;
        bool finished;

        public float ScenarioTime { get; private set; }
        public bool Finished => finished;
        public float Duration => active != null ? active.duration : 0f;
        public int ActiveSeed => active != null ? active.seed : 0;
        public int RunNumber { get; private set; }   // increments on every (re)start
        public float ActiveArc => active != null ? active.sectorArcDegrees : sectorArcDegrees;
        public float ActiveRadius => active != null ? active.spawnRingRadius : spawnRingRadius;

        // Set by the trainer screen. While it is set it replaces both the preset values and the Inspector override; null = not in use.
        public ScenarioConfig TrainerConfig { get; private set; }
        float activeSpeed = 1f;

        // Start (or restart) a run from a trainer config. Pass null to go back to the Inspector / preset values.
        public void StartRun(ScenarioConfig config)
        {
            TrainerConfig = config != null ? config.Clone().Validate() : null;
            Restart();
        }

        void Start()
        {
            if (preset == null)
            {
                Debug.LogError("[Scenario] ScenarioSpawner has no preset assigned.", this);
                enabled = false;
                return;
            }
            if (defendedPoint == null) defendedPoint = transform;
            Restart();
        }

        // Ends the run on the next frame, e.g. to see the results without waiting out the clock.
        [ContextMenu("End Scenario Now")]
        public void EndNow()
        {
            if (Application.isPlaying && active != null && !finished) ScenarioTime = active.duration;
        }

        // Also in the component's context menu (three dots) while playing.
        [ContextMenu("Restart Scenario")]
        public void Restart()
        {
            if (!Application.isPlaying || preset == null) return;
            foreach (var c in ContactRegistry.Live.ToArray()) c.Despawn("Restart");
            ContactRegistry.Reset();

            if (active != null) Destroy(active);
            active = Instantiate(preset);
            active.name = preset.name;
            activeSpeed = 1f;
            string source = "";
            if (TrainerConfig != null)
            {
                var c = TrainerConfig;
                source = $" (trainer: {c.presetName})";
                active.seed = c.seed;
                active.duration = c.duration;
                active.hostileCount = c.hostileCount;
                active.friendlyCount = c.friendlyCount;
                active.civilianCount = c.civilianCount;
                active.birdCount = c.birdCount;
                active.sectorArcDegrees = c.sectorArcDegrees;
                active.spawnRingRadius = c.spawnRingRadius;
                active.spawnWindowStart = c.spawnWindowStart;
                active.spawnWindowEnd = c.spawnWindowEnd;
                activeSpeed = c.speedMultiplier;
            }
            else if (overridePreset)
            {
                source = " (Inspector override)";
                activeSpeed = speedMultiplier;
                active.seed = seed;
                active.duration = duration;
                active.hostileCount = hostileCount;
                active.friendlyCount = friendlyCount;
                active.civilianCount = civilianCount;
                active.birdCount = birdCount;
                active.sectorArcDegrees = sectorArcDegrees;
                active.spawnRingRadius = spawnRingRadius;
                active.spawnWindowStart = spawnWindowStart;
                active.spawnWindowEnd = spawnWindowEnd;
                if (randomizeSeed) active.seed = System.Environment.TickCount;
            }

            RunConditions.Difficulty = TrainerConfig != null ? TrainerConfig.difficulty : "";
            RunConditions.NightVisionSeconds = 0f;
            RunNumber++;
            ScenarioTime = 0f;
            next = 0;
            finished = false;
            BuildPlan();
            Debug.Log($"[Scenario] '{active.name}'{source} seed {active.seed}: {plan.Count} contacts planned over {active.duration:F0}s, ring {active.spawnRingRadius:F0} m.");
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame && !ContactRegistry.InputBlocked)
            {
                ContactRegistry.SetTruthColours(!ContactRegistry.TruthColours);
                Debug.Log("[Scenario] Truth colours " + (ContactRegistry.TruthColours ? "ON (debug)" : "OFF"));
            }

            if (finished) return;
            ScenarioTime += Time.deltaTime;
            ContactRegistry.ScenarioTime = ScenarioTime;

            while (next < plan.Count && plan[next].time <= ScenarioTime) Spawn(plan[next], ++next);

            if (ScenarioTime >= active.duration)
            {
                finished = true;
                foreach (var c in ContactRegistry.Live.ToArray()) c.Despawn("ScenarioEnd");
                Debug.Log($"[Scenario] Finished at {ScenarioTime:F1}s, {ContactRegistry.Events.Count} events logged.");
            }
        }

        void BuildPlan()
        {
            var rng = new System.Random(active.seed);
            var list = new List<PlannedSpawn>();

            for (int i = 0; i < active.hostileCount && active.hostileClassesEnabled.Count > 0; i++)
                Add(list, rng, active.hostileClassesEnabled[rng.Next(active.hostileClassesEnabled.Count)]);
            for (int i = 0; i < active.friendlyCount; i++) Add(list, rng, ContactClass.FriendlyISR);
            for (int i = 0; i < active.civilianCount; i++) Add(list, rng, ContactClass.CivilianQuad);
            for (int i = 0; i < active.birdCount; i++) Add(list, rng, ContactClass.Bird);

            plan = list.OrderBy(p => p.time).ToList();   // stable sort
        }

        void Add(List<PlannedSpawn> list, System.Random rng, ContactClass contactClass)
        {
            // Draw the same number of values for every contact so one missing definition can't shift the rest.
            float time = Range(rng, active.spawnWindowStart, Mathf.Min(active.spawnWindowEnd, active.duration));
            float half = active.sectorArcDegrees * 0.5f;
            float bearing = Range(rng, -half, half);
            float u1 = (float)rng.NextDouble(), u2 = (float)rng.NextDouble(), u3 = (float)rng.NextDouble();
            float u4 = (float)rng.NextDouble(), u5 = (float)rng.NextDouble();
            int motionSeed = rng.Next();

            var def = active.GetDefinition(contactClass);
            if (def == null)
            {
                Debug.LogWarning($"[Scenario] Preset '{active.name}' has no definition for {contactClass}; skipped.");
                return;
            }

            float r = active.spawnRingRadius;
            float altitude = Mathf.Lerp(def.altitudeRange.x, def.altitudeRange.y, u1);
            var p = new PlannedSpawn
            {
                time = time,
                def = def,
                speed = Mathf.Lerp(def.speedRange.x, def.speedRange.y, u2) * activeSpeed,
                start = OnSector(bearing, r, altitude),
                motionSeed = motionSeed,
            };

            switch (def.behaviour)
            {
                case FlightBehaviour.Approach:
                    p.goal = defendedPoint.position + Vector3.up * 10f;
                    break;
                case FlightBehaviour.Loiter:
                    p.goal = OnSector(Mathf.Lerp(-half, half, u3) * 0.7f, r * Mathf.Lerp(0.4f, 0.7f, u4), altitude);
                    p.orbitRadius = Mathf.Lerp(35f, 70f, u5);
                    break;
                default:
                    // Transit / Erratic: leave through the ring on the far side of the arc from the entry bearing.
                    float exit = (bearing >= 0f ? -1f : 1f) * half * Mathf.Lerp(0.3f, 1f, u3);
                    p.goal = OnSector(exit, r, altitude);
                    break;
            }
            list.Add(p);
        }

        void Spawn(PlannedSpawn p, int id)
        {
            var go = ContactVisuals.Build(p.def, out var material, out var wingL, out var wingR);
            go.name = $"Contact_{id:00}";   // name must not reveal class in the hierarchy either
            go.transform.SetParent(transform, true);
            go.transform.position = p.start;

            var contact = go.AddComponent<Contact>();
            contact.Initialise(id, p.def, material);
            go.AddComponent<ContactMotion>().Initialise(contact, p.def.behaviour, p.speed, p.goal, p.orbitRadius, p.motionSeed, wingL, wingR);
        }

        Vector3 OnSector(float bearingDeg, float radius, float altitude)
        {
            Vector3 forward = transform.forward; forward.y = 0f;
            Vector3 dir = Quaternion.AngleAxis(bearingDeg, Vector3.up) * forward.normalized;
            Vector3 origin = transform.position; origin.y = 0f;
            Vector3 p = origin + dir * radius;
            // Altitude is height above the ground there, so nothing spawns inside a hill on generated terrain.
            float ground = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(p);
            p.y = (float.IsNaN(ground) ? 0f : ground) + altitude;
            return p;
        }

        static float Range(System.Random rng, float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }

        void OnDrawGizmosSelected()
        {
            if (preset == null) return;
            Gizmos.color = Color.yellow;
            float radius = overridePreset ? spawnRingRadius : preset.spawnRingRadius;
            float half = (overridePreset ? sectorArcDegrees : preset.sectorArcDegrees) * 0.5f;
            Vector3 prev = OnSector(-half, radius, 0f);
            Gizmos.DrawLine(transform.position, prev);
            for (int i = 1; i <= 24; i++)
            {
                Vector3 p = OnSector(Mathf.Lerp(-half, half, i / 24f), radius, 0f);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.DrawLine(transform.position, prev);
        }
    }
}
