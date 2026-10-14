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
        [Tooltip("Start a run as soon as Play begins, without waiting for the trainer's START. For quick testing only.")]
        public bool autoStart;

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
        [Min(0.01f)] public float speedMultiplier = 1f;

        [Header("Mix")]
        public int hostileCount = 3;
        public int friendlyCount = 2;
        public int civilianCount = 2;
        public int birdCount = 6;
        [Tooltip("Share of the hostiles (percent) that fly an own-drone patrol to blend in, then break off for the post.")]
        [Range(0f, 100f)] public float blendInPercent;

        [Header("Site (Inspector override)")]
        public SiteType site;

        [Header("Swarm and flocks (Inspector override)")]
        [Tooltip("Hostile drones arriving together from one bearing. 0 = none.")]
        public int swarmSize;
        public float swarmTime = 60f;
        [Tooltip("Degrees from the sector centre; above 360 = from the seed.")]
        public float swarmBearing = 999f;
        public int birdFlocks;
        public int flockSize = 8;
        [Tooltip("How swarms fly together. Empty = built-in defaults.")]
        public SwarmProfile swarmProfile;
        [Tooltip("How bird flocks fly together. Empty = built-in defaults.")]
        public SwarmProfile flockProfile;

        [Header("Geometry")]
        [Tooltip("Arc in front of this transform that contacts appear in, degrees.")]
        [Range(10f, 360f)] public float sectorArcDegrees = 120f;
        [Tooltip("Distance contacts spawn at, metres.")]
        public float spawnRingRadius = 1000f;

        [Header("Spawn time window (s)")]
        public float spawnWindowStart = 3f;
        public float spawnWindowEnd = 240f;

        [Header("Models: drag prefabs here")]
        [Tooltip("Drag any prefab or model from the Project panel into these lists. A list with something in it replaces the models set on that class's Contact Definition; an empty list leaves the definition's own models in use. With several entries each contact picks one from the seed. Models are scaled to the class size automatically; the nose should point along +Z (blue arrow).")]
        public List<GameObject> hostileQuadModels = new List<GameObject>();
        public List<GameObject> hostileFixedWingModels = new List<GameObject>();
        public List<GameObject> friendlyIsrModels = new List<GameObject>();
        public List<GameObject> civilianQuadModels = new List<GameObject>();
        public List<GameObject> birdModels = new List<GameObject>();

        public List<GameObject> ModelsFor(ContactClass c)
        {
            switch (c)
            {
                case ContactClass.HostileQuad: return hostileQuadModels;
                case ContactClass.HostileFixedWing: return hostileFixedWingModels;
                case ContactClass.FriendlyISR: return friendlyIsrModels;
                case ContactClass.CivilianQuad: return civilianQuadModels;
                default: return birdModels;
            }
        }

        ScenarioPreset active;   // runtime copy of the preset with overrides applied

        class PlannedSpawn
        {
            public float time;
            public ContactDefinition def;       // what it really is
            public ContactDefinition look;      // what it looks and flies like (differs for a blend-in hostile)
            public bool deceptive;
            public float anomalyDelay;          // seconds after spawn that a blend-in hostile breaks off
            public Vector3 start;
            public Vector3 goal;
            public float speed;
            public float orbitRadius;
            public int motionSeed;
            public int group = -1;              // swarm or flock id; -1 = flies on its own
        }

        // Swarms and flocks of this run, keyed by id; members join as they spawn.
        readonly Dictionary<int, SwarmGroup> groups = new Dictionary<int, SwarmGroup>();
        int aSwarmSize, aBirdFlocks, aFlockSize = 8;
        SiteType aSite;
        GameObject siteRoot;
        public GameObject SiteRoot => siteRoot;
        float aSwarmTime = 60f, aSwarmBearing = 999f;
        SwarmProfile defaultSwarm, defaultFlock;
        public IEnumerable<SwarmGroup> Groups => groups.Values;

        List<PlannedSpawn> plan;
        int next;
        bool finished;

        public float ScenarioTime { get; private set; }
        public bool Finished => finished && plan != null;
        public float Duration => active != null ? active.duration : 0f;
        public int ActiveSeed => active != null ? active.seed : 0;
        public int RunNumber { get; private set; }   // increments on every (re)start
        public float ActiveArc => active != null ? active.sectorArcDegrees : sectorArcDegrees;
        public float ActiveRadius => active != null ? active.spawnRingRadius : spawnRingRadius;

        // Set by the trainer screen. While it is set it replaces both the preset values and the Inspector override; null = not in use.
        public ScenarioConfig TrainerConfig { get; private set; }
        float activeSpeed = 1f;
        float activeBlendIn;

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
            // Nothing happens until the trainer presses START (or StartRun / Restart is called), unless Auto Start is ticked.
            if (autoStart) Restart();
        }

        // True until the first run has been started.
        public bool Waiting => plan == null;

        // Set by the trainer's setup: where drones may appear (world x, z). Empty = the ring in front of the post.
        [System.NonSerialized] public List<Vector3> customSpawnPoints = new List<Vector3>();

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
            activeBlendIn = 0f;
            aSwarmSize = 0; aBirdFlocks = 0; aFlockSize = 8; aSwarmTime = 60f; aSwarmBearing = 999f;
            aSite = SiteType.None;
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
                activeBlendIn = Mathf.Max(0f, c.blendInPercent);
                aSwarmSize = c.swarmSize; aSwarmTime = c.swarmTime; aSwarmBearing = c.swarmBearing; aBirdFlocks = c.birdFlocks; aFlockSize = c.flockSize;
                aSite = c.site;
            }
            else if (overridePreset)
            {
                source = " (Inspector override)";
                activeSpeed = speedMultiplier;
                activeBlendIn = blendInPercent;
                aSwarmSize = swarmSize; aSwarmTime = swarmTime; aSwarmBearing = swarmBearing; aBirdFlocks = birdFlocks; aFlockSize = flockSize;
                aSite = site;
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
            RunConditions.AllowMovement = TrainerConfig != null && TrainerConfig.allowMovement;
            // The site around the post: rebuilt each run (the post may have moved, the seed decides the layout).
            if (siteRoot != null) Destroy(siteRoot);
            siteRoot = SiteBuilder.Build(transform, aSite, preset.siteProfile, active.seed);
            if (siteRoot != null)
            {
                Debug.Log($"[Scenario] Site: {aSite} ({siteRoot.GetComponentsInChildren<Collider>().Length} solid objects; they block line of sight).");
                // The soldier stands on the post: on the ground (rural) or up the watchtower (urban).
                var cam = Camera.main;
                if (cam != null)
                {
                    Vector3 eye = transform.position;
                    eye.y = SiteBuilder.Ground(eye) + SiteBuilder.EyeHeight;
                    cam.transform.position = eye;
                    var walker = cam.GetComponent<Chaukanna.Player.ObserverMove>();
                    if (walker != null) walker.SetPost(eye);
                }
            }

            RunNumber++;
            ScenarioTime = 0f;
            nextRangeSample = 0f;
            next = 0;
            finished = false;
            BuildPlan();
            Debug.Log($"[Scenario] '{active.name}'{source} seed {active.seed}: {plan.Count} contacts planned over {active.duration:F0}s, ring {active.spawnRingRadius:F0} m.");
        }

        const float RangeSampleSeconds = 0.5f;
        float nextRangeSample;
        Chaukanna.Player.ContactSensor logSensor;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f1Key.wasPressedThisFrame && !ContactRegistry.InputBlocked)
            {
                ContactRegistry.SetTruthColours(!ContactRegistry.TruthColours);
                Debug.Log("[Scenario] Truth colours " + (ContactRegistry.TruthColours ? "ON (debug)" : "OFF"));
            }

            if (finished || plan == null) return;
            ScenarioTime += Time.deltaTime;
            ContactRegistry.ScenarioTime = ScenarioTime;

            while (next < plan.Count && plan[next].time <= ScenarioTime) Spawn(plan[next], ++next);
            foreach (var g in groups.Values) g.Step(Time.deltaTime);

            // Range to the post twice a second, for the scoring's decision clock (release points are scoring rules).
            if (ScenarioTime >= nextRangeSample)
            {
                nextRangeSample = ScenarioTime + RangeSampleSeconds;
                Vector3 post = defendedPoint != null ? defendedPoint.position : transform.position;
                if (logSensor == null && Camera.main != null) logSensor = Camera.main.GetComponent<Chaukanna.Player.ContactSensor>();
                foreach (var c in ContactRegistry.Live)
                {
                    c.Data.SampleRange(ScenarioTime, c.transform.position, post);
                    c.Data.SampleTrack(ScenarioTime, c.transform.position, logSensor != null && logSensor.Visible.Contains(c));
                }
                RunLog.post = post;
                RunLog.sectorYaw = transform.eulerAngles.y;
                RunLog.spawnRadius = ActiveRadius;
                var cam = logSensor != null ? logSensor.GetComponent<Camera>() : Camera.main;
                if (cam != null)
                {
                    var e = cam.transform.eulerAngles;
                    RunLog.view.Add(new ViewPoint { t = ScenarioTime, yaw = e.y, pitch = -Mathf.DeltaAngle(0f, e.x), fov = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect), x = cam.transform.position.x, z = cam.transform.position.z });
                }
            }

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

            PlanGroups(list);
            plan = list.OrderBy(p => p.time).ToList();   // stable sort
            ReportModels();
            ReportBlockedStarts();
        }

        // How many contacts start where the post cannot see them because terrain is in the way. They may still fly into
        // view; scoring decides per contact from what actually happened (ScoringRules.excludeNeverDetectable).
        void ReportBlockedStarts()
        {
            if (plan.Count == 0) return;
            Vector3 eye = (defendedPoint != null ? defendedPoint.position : transform.position) + Vector3.up * 1.7f;
            int blocked = 0;
            foreach (var p in plan)
                if (Physics.Linecast(eye, p.start, ~0, QueryTriggerInteraction.Ignore)) blocked++;
            if (blocked > 0)
                Debug.Log($"[Scenario] {blocked} of {plan.Count} planned contacts start behind terrain (no line of sight from the post at their start point). Any that never come into view are scored \"not detectable\", not missed.");
        }

        // Says in the Console which model each planned contact will wear, and names any class left on a grey placeholder.
        void ReportModels()
        {
            var counts = new SortedDictionary<string, int>();
            var missing = new HashSet<ContactClass>();
            int withModel = 0;
            foreach (var p in plan)
            {
                var prefab = ContactVisuals.PickPrefab(p.look, p.motionSeed / 7, ModelsFor(p.look.contactClass));
                if (prefab == null) { missing.Add(p.look.contactClass); continue; }
                withModel++;
                counts.TryGetValue(prefab.name, out int n);
                counts[prefab.name] = n + 1;
            }
            Debug.Log($"[Scenario] Models: {withModel} of {plan.Count} contacts use a model prefab ({string.Join(", ", counts.Select(kv => kv.Key + " x" + kv.Value))}).");
            int blendIn = plan.Count(p => p.deceptive);
            if (blendIn > 0) Debug.Log($"[Scenario] {blendIn} hostile{(blendIn > 1 ? "s" : "")} will blend in as own drones before breaking off ({activeBlendIn:F0} % of hostiles).");
            foreach (var c in missing)
                Debug.LogWarning($"[Scenario] {c} has no model: it will spawn as a grey placeholder. Drag a prefab into the spawner's Models list for it, or into its Contact Definition.", this);
        }

        // Swarms and bird flocks, from a random stream of their own so adding a swarm never changes the rest of the plan.
        void PlanGroups(List<PlannedSpawn> list)
        {
            groups.Clear();
            var rng = new System.Random(active.seed ^ 0x5A17);
            float half = active.sectorArcDegrees * 0.5f, r = active.spawnRingRadius;
            int id = 0;
            var log = new List<string>();
            if (aSwarmSize > 0)
            {
                var def = active.GetDefinition(ContactClass.HostileQuad) ?? active.GetDefinition(ContactClass.HostileFixedWing);
                if (def == null) Debug.LogWarning("[Scenario] No hostile definition in the preset, so no swarm.");
                else
                {
                    float bearing = aSwarmBearing <= 360f ? aSwarmBearing : Range(rng, -half, half);
                    float altitude = Mathf.Lerp(def.altitudeRange.x, def.altitudeRange.y, 0.5f);
                    Vector3 centre = GroupCentre(OnSector(bearing, r, altitude), altitude, rng.Next());
                    float speed = Mathf.Lerp(def.speedRange.x, def.speedRange.y, 0.5f) * activeSpeed;
                    var prof = swarmProfile != null ? swarmProfile : (defaultSwarm != null ? defaultSwarm : (defaultSwarm = SwarmProfile.Drones()));
                    AddGroup(list, id++, def, aSwarmSize, centre, defendedPoint.position + Vector3.up * 10f, speed, Mathf.Min(aSwarmTime, active.duration), prof, rng.Next(), "ReachedDefendedPoint");
                    log.Add($"a swarm of {aSwarmSize} {def.contactClass} at {aSwarmTime:F0} s from {bearing:F0} deg");
                }
            }
            var bird = active.GetDefinition(ContactClass.Bird);
            for (int f = 0; f < aBirdFlocks && bird != null; f++)
            {
                float bearing = Range(rng, -half, half);
                float exit = (bearing >= 0f ? -1f : 1f) * half * Mathf.Lerp(0.3f, 1f, (float)rng.NextDouble());
                float altitude = Mathf.Lerp(bird.altitudeRange.x, bird.altitudeRange.y, (float)rng.NextDouble());
                Vector3 centre = GroupCentre(OnSector(bearing, r, altitude), altitude, rng.Next());
                float speed = Mathf.Lerp(bird.speedRange.x, bird.speedRange.y, (float)rng.NextDouble()) * activeSpeed;
                float time = Range(rng, active.spawnWindowStart, Mathf.Min(active.spawnWindowEnd, active.duration));
                var prof = flockProfile != null ? flockProfile : (defaultFlock != null ? defaultFlock : (defaultFlock = SwarmProfile.Birds()));
                AddGroup(list, id++, bird, aFlockSize, centre, OnSector(exit, r, altitude), speed, time, prof, rng.Next(), "ExitedSector");
            }
            if (aBirdFlocks > 0) log.Add($"{aBirdFlocks} bird flock{(aBirdFlocks > 1 ? "s" : "")} of {aFlockSize}");
            if (log.Count > 0) Debug.Log("[Scenario] Groups: " + string.Join("; ", log) + ".");
        }

        // A trainer-placed spawn point replaces the ring for groups too.
        Vector3 GroupCentre(Vector3 onRing, float altitude, int seed)
        {
            if (customSpawnPoints == null || customSpawnPoints.Count == 0) return onRing;
            Vector3 point = customSpawnPoints[(seed & 0x7FFFFFFF) % customSpawnPoints.Count];
            float ground = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(point);
            point.y = (float.IsNaN(ground) ? 0f : ground) + altitude;
            return point;
        }

        void AddGroup(List<PlannedSpawn> list, int id, ContactDefinition def, int count, Vector3 centre, Vector3 target, float speed, float time, SwarmProfile prof, int seed, string arrival)
        {
            groups[id] = new SwarmGroup(id, prof, target, speed, arrival);
            var offsets = SwarmPlanner.Layout(count, seed, prof, target - centre);
            for (int i = 0; i < count; i++)
                list.Add(new PlannedSpawn
                {
                    time = time + prof.spawnStagger * i / Mathf.Max(1, count - 1),
                    def = def, look = def, start = centre + offsets[i], goal = target, speed = speed,
                    motionSeed = seed + i * 7919, group = id,
                });
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

            // A blend-in hostile wears an own drone's model and flies its patrol; it is still a hostile fixed-wing.
            // Chosen from a value already drawn, so switching blend-in on or off never changes the rest of the plan.
            ContactDefinition truth = def;
            bool hostile = contactClass == ContactClass.HostileQuad || contactClass == ContactClass.HostileFixedWing;
            bool deceptive = hostile && activeBlendIn > 0f && ((motionSeed >> 3) & 0x7FFFFFFF) % 100 < activeBlendIn;
            if (deceptive)
            {
                var own = active.GetDefinition(ContactClass.FriendlyISR);
                if (own == null) deceptive = false;
                else
                {
                    truth = active.GetDefinition(ContactClass.HostileFixedWing) ?? def;
                    def = own;
                }
            }

            float r = active.spawnRingRadius;
            float altitude = Mathf.Lerp(def.altitudeRange.x, def.altitudeRange.y, u1);
            var p = new PlannedSpawn
            {
                time = time,
                def = truth,
                look = def,
                deceptive = deceptive,
                anomalyDelay = Mathf.Lerp(25f, 70f, u2),
                speed = Mathf.Lerp(def.speedRange.x, def.speedRange.y, u2) * activeSpeed,
                start = OnSector(bearing, r, altitude),
                motionSeed = motionSeed,
            };
            // Trainer-placed spawn points replace the ring. The point is picked from values already drawn, so the rest of
            // the plan (times, speeds, classes) is the same with or without them.
            if (customSpawnPoints != null && customSpawnPoints.Count > 0)
            {
                Vector3 point = customSpawnPoints[(motionSeed & 0x7FFFFFFF) % customSpawnPoints.Count];
                point += new Vector3((u4 - 0.5f) * 60f, 0f, (u5 - 0.5f) * 60f);     // spread, so drones from one point don't stack
                float ground = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(point);
                point.y = (float.IsNaN(ground) ? 0f : ground) + altitude;
                p.start = point;
            }

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
            var go = ContactVisuals.Build(p.look, out var materials, out var wingL, out var wingR, p.motionSeed / 7, ModelsFor(p.look.contactClass));
            go.name = $"Contact_{id:00}";   // name must not reveal class in the hierarchy either
            go.transform.SetParent(transform, true);
            go.transform.position = p.start;

            var contact = go.AddComponent<Contact>();
            contact.Initialise(id, p.def, materials, p.deceptive);
            var motion = go.AddComponent<ContactMotion>();
            motion.Initialise(contact, p.look.behaviour, p.speed, p.goal, p.orbitRadius, p.motionSeed, wingL, wingR);
            if (p.deceptive) motion.SetDeception(p.anomalyDelay, defendedPoint.position + Vector3.up * 10f);
            if (p.group >= 0 && groups.TryGetValue(p.group, out var group))
            {
                group.Add(motion);
                if (p.def.contactClass == ContactClass.HostileQuad || p.def.contactClass == ContactClass.HostileFixedWing) contact.Data.swarmId = p.group;
            }
            motion.rotors = ContactVisuals.FindRotors(go);
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
