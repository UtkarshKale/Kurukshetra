using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scenario
{
    public enum TimeOfDay
    {
        Day,
        Dusk,
        Night,
    }

    // Share of hostiles among the contacts (doc §1.6). Custom = the counts as typed. Placeholder bands, to be set with an officer.
    public enum Prevalence { Custom, Low, Medium, High }

    public enum Visibility
    {
        Clear,
        Hazy,
        Foggy,
    }

    // What the current run is being played under. Written by the spawner and the time-of-day controller, read by scoring.
    public static class RunConditions
    {
        public static TimeOfDay Time;
        public static Visibility Visibility;
        public static string Difficulty = "";
        public static float NightVisionSeconds;
        public static bool AllowMovement;
        public static string DisplayTier = "desktop";   // "desktop", "vr-sim", or "mixed" if the view was switched mid-run
        public static Prevalence Prevalence;
        public static bool QuietWatch, NullMission;
        public static bool RangeDrill;
        public static bool DirectorOn, ReferencePackage;
        // LAN team mode on a trainee's PC: the host runs the sky; this PC only shows proxies and sends decisions.
        public static bool NetTeamClient;
        // LAN team mode: this PC's seat (Sentry / Commander). Any everywhere else, and then no role scoring or refusals apply.
        public static Chaukanna.Net.SeatRole SeatRole = Chaukanna.Net.SeatRole.Any;
        public static string PackageId = "";
        // Who this PC is in a report: its seat and its unit. The lobby-units feature will set UnitId; until then everyone is "Unit 1".
        public static string SeatId = "Seat 1", UnitId = "Unit 1";
        // Dense sky (DS-1): a crowd is in the run; the I key works; the shares of cooperative drones with no reply and hostiles that spoof.
        public static bool CrowdActive;
        public static bool IffAid = true;
        public static float FriendlyNoIffPercent = 5f, HostileSpoofPercent;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Time = TimeOfDay.Day;
            Visibility = Visibility.Clear;
            Difficulty = "";
            NightVisionSeconds = 0f;
            AllowMovement = false;
            DisplayTier = "desktop";
            Prevalence = Prevalence.Custom;
            QuietWatch = NullMission = false;
            RangeDrill = false;
            DirectorOn = ReferencePackage = false;
            PackageId = "";
            SeatId = "Seat 1";
            UnitId = "Unit 1";
            SeatRole = Chaukanna.Net.SeatRole.Any;
            CrowdActive = false;
            IffAid = true;
            FriendlyNoIffPercent = 5f;
            HostileSpoofPercent = 0f;
        }
    }

    // Everything one run needs, as plain data. The trainer screen builds one and hands it to ScenarioSpawner.StartRun.
    // Contact definitions still come from the spawner's ScenarioPreset asset.
    [Serializable]
    public class ScenarioConfig
    {
        public string presetName = "Custom";

        [Header("Run")]
        public int seed = 1001;
        public float duration = 300f;
        public float speedMultiplier = 1f;

        [Header("Mix")]
        public int hostileCount = 3;
        public int friendlyCount = 2;
        public int civilianCount = 2;
        public int birdCount = 6;
        [Tooltip("Share of the hostiles (percent) that fly an own-drone patrol to blend in, then break off for the post. -1 = take it from the difficulty level.")]
        public float blendInPercent = -1f;

        [Header("Prevalence and Quiet watch (module 5)")]
        [Tooltip("Custom = the counts above as typed. Low / Medium / High = the hostile share of 'Contacts' is set from the band (placeholders: about 15 / 35 / 60 %) and the rest keep the decoy mix.")]
        public Prevalence prevalence = Prevalence.Custom;
        [Tooltip("How many contacts (hostile + decoys, not swarms or flocks) when a prevalence is chosen.")]
        public int prevalenceTotal = 12;
        [Tooltip("A long, quiet watch for vigilance: few contacts spread over the whole watch, low prevalence, and sometimes no hostile at all.")]
        public bool quietWatch;
        public float watchMinutes = 45f;
        [Tooltip("Contacts per hour in a Quiet watch (the doc allows up to 4).")]
        public float contactsPerHour = 3f;
        [Tooltip("Chance that a run has no hostile at all (a null mission), so 'there is always a threat' is not a tell. Quiet watch uses 0.2 when this is below 0.")]
        public float nullMissionChance = -1f;

        // Set by ApplyPrevalence; what the run actually got.
        [NonSerialized] public bool nullMission;

        [Header("Director and packages (modules 10-12)")]
        [Tooltip("Training runs only: the Director adds waves, decoys and sensor drops when the soldier is doing well and holds back when they struggle. Always off in a reference package.")]
        public bool director;
        [Tooltip("A locked reference package: Director off, fixed seed family; only these count on the proficiency board.")]
        public bool referencePackage;
        public string packageId = "";
        [Tooltip("Fairness profile a reference package is locked to (doc V9).")]
        public string fairnessId = "";

        [Header("Range drill (module 1)")]
        [Tooltip("A short recognition drill instead of a watch: one contact at a time in front of the soldier, call it with 1-5, feedback at once.")]
        public bool rangeDrill;
        public int drillItems = 30;

        [Header("Site")]
        [Tooltip("None = open ground as before. Rural = fields and a canal in front of the post, the cantonment behind. Urban = the post at the cantonment edge, drones come in over the buildings.")]
        public SiteType site;

        [Header("Swarm and flocks")]
        [Tooltip("Hostile drones arriving together as one swarm from one bearing (8 or 24 in the spec). 0 = no swarm.")]
        public int swarmSize;
        [Tooltip("Seconds into the run when the swarm appears.")]
        public float swarmTime = 60f;
        [Tooltip("Bearing the swarm comes from, degrees from the centre of the sector (negative = left). Above 360 = picked from the seed.")]
        public float swarmBearing = 999f;
        [Tooltip("How a big swarm (more than 64) attacks: Direct, Saturate (spread to several bearings at a set range, then rush together) or Split (two bodies from the flanks).")]
        public SwarmTactic swarmTactic;
        [Tooltip("Bird flocks crossing the sector, each flying together.")]
        public int birdFlocks;
        public int flockSize = 8;

        [Header("Dense sky (DS-1)")]
        [Tooltip("Cooperative drones and birds in the air besides the planned contacts (own ISR, civil delivery and survey drones, light shows, bird flocks). Drawn by the thousand, never hostile. 0 = no crowd.")]
        public int crowdCount;
        [Tooltip("Metres round the post that the crowd fills. 0 = 1.5 x the spawn ring radius.")]
        public float crowdRadius;
        [Tooltip("On: the I key challenges a contact for IFF / Remote ID, and the lock label shows the filed-route line.")]
        public bool iffAid = true;
        [Tooltip("Share (percent) of the own and civil drones that do not answer: transponder off, no Remote ID.")]
        public float friendlyNoIffPercent = 5f;
        [Tooltip("Share (percent) of the hostiles that answer IFF with a valid-looking reply.")]
        public float hostileSpoofPercent;

        [Header("Custom paths (2D path editor)")]
        [Tooltip("Paths drawn on the 2D map. Waypoint 0 is the spawn; each waypoint has its own height above ground and speed. Contacts, the swarm and bird flocks that match a path fly it instead of the usual straight pass.")]
        public List<FlightPath> paths = new List<FlightPath>();

        [Header("3D scenario editor (manual objects, cleared areas, placed groups)")]
        [Tooltip("Trees, bushes, rocks and buildings placed by hand in the 3D editor. Spawned at run start next to the generated layers.")]
        public List<ManualObject> manualObjects = new List<ManualObject>();
        [Tooltip("Round areas where the generated trees, bushes and buildings are left out.")]
        public List<ClearArea> clearAreas = new List<ClearArea>();
        [Tooltip("Drone and bird groups dropped at a point in the 3D editor.")]
        public List<PlacedGroup> placedGroups = new List<PlacedGroup>();
        // Player posts and drone spawn points placed on the map or in the 3D editor (metres east / north of the terrain centre). Saved with the scenario and package.
        public List<PostPoint> playerPosts = new List<PostPoint>();
        public List<Vector2> spawnPoints = new List<Vector2>();

        [Header("Spawn and flight options (scenario settings)")]
        [Tooltip("Metres above the ground that contacts appear at (min, max). Negative = each contact definition's own height range.")]
        public float spawnAltMin = -1f;
        public float spawnAltMax = -1f;
        [Tooltip("Nearest distance (m) a contact may appear at; the farthest is the spawn ring. Negative = all on the ring.")]
        public float spawnDistMin = -1f;
        [Tooltip("Bearing (degrees from the post's forward) of the middle of the spawn sector. 0 = straight ahead. The arc below is centred on it.")]
        public float sectorCentreDegrees;
        [Tooltip("How single drones and birds of each type fly. Default = the contact definition's own pattern; Custom path = a path drawn on the 2D map.")]
        public FlightPattern patternHostileQuad;
        public FlightPattern patternHostileFixedWing, patternOwnIsr, patternCivilian, patternBird;
        [Tooltip("Where bird flocks roost: bearing from the middle of the sector (above 360 = chosen from the seed), distance from the post (0 = chosen), height above ground (negative = chosen). Big flocks circle it; small flocks fly to it.")]
        public float roostBearing = 999f;
        public float roostDistance;
        public float roostAltitude = -1f;

        public FlightPattern PatternFor(ContactClass c)
        {
            switch (c)
            {
                case ContactClass.HostileQuad: return patternHostileQuad;
                case ContactClass.HostileFixedWing: return patternHostileFixedWing;
                case ContactClass.FriendlyISR: return patternOwnIsr;
                case ContactClass.CivilianQuad: return patternCivilian;
                default: return patternBird;
            }
        }

        public void SetPattern(ContactClass c, FlightPattern p)
        {
            switch (c)
            {
                case ContactClass.HostileQuad: patternHostileQuad = p; break;
                case ContactClass.HostileFixedWing: patternHostileFixedWing = p; break;
                case ContactClass.FriendlyISR: patternOwnIsr = p; break;
                case ContactClass.CivilianQuad: patternCivilian = p; break;
                default: patternBird = p; break;
            }
        }

        public bool HasAltRange => spawnAltMin >= 0f || spawnAltMax >= 0f;
        public bool HasRoostOptions => roostBearing <= 360f || roostDistance > 0f || roostAltitude >= 0f;

        [Header("Geometry")]
        public float sectorArcDegrees = 120f;
        public float spawnRingRadius = 1000f;

        [Header("Spawn time window (s)")]
        public float spawnWindowStart = 3f;
        public float spawnWindowEnd = 240f;

        [Header("ROE")]
        public RoeCard roeAtStart = RoeCard.WeaponsHold;
        public bool roeFlip;
        public float roeFlipTime = 120f;
        [Tooltip("On: the change happens at a time drawn from the seed (30 to 70 % of the run) instead of Flip at.")]
        public bool roeFlipRandom;
        public RoeCard roeFlipTo = RoeCard.UnderAttack;

        [Header("Conditions")]
        public TimeOfDay timeOfDay = TimeOfDay.Day;
        public Visibility visibility = Visibility.Clear;
        [Tooltip("Extra multiplier on the smallest apparent size that can be picked out (from the difficulty level). 1 = none.")]
        public float detectionSizeMultiplier = 1f;
        [Tooltip("Name of the difficulty level applied to this config; empty = none.")]
        public string difficulty = "";

        [Header("View")]
        [Tooltip("On: the run starts in VR mode (seated soldier, hands, sensor tablet, binoculars by raising both hands). Movement is off in VR.")]
        public bool vrMode;
        [Tooltip("How the soldier sees the field: 0 desktop, 1 VR-sim (mouse), 2 headset (OpenXR). -1 = not set: derived from VR mode, so old presets and packages keep working.")]
        public int viewMode = -1;

        [Header("Soldier")]
        [Tooltip("On: the soldier can walk (W A S D, Shift to run). Off: fixed at the post, look only.")]
        public bool allowMovement;
        [Tooltip("How far from the post the soldier may walk, metres.")]
        public float moveRadius = 50f;
        public float walkSpeed = 4f;
        public float runSpeed = 7f;

        [Header("Sensors")]
        public bool sensorsDegraded;
        [Tooltip("Negative = use the sensor profile's own error rate.")]
        public float aiErrorOverride = -1f;

        public ScenarioConfig Clone()
        {
            var c = (ScenarioConfig)MemberwiseClone();
            c.paths = FlightPath.CloneAll(paths);      // the list is not shared between copies
            c.manualObjects = ManualLayout.CloneObjects(manualObjects);
            c.clearAreas = ManualLayout.CloneAreas(clearAreas);
            c.placedGroups = ManualLayout.CloneGroups(placedGroups);
            c.playerPosts = PostPoint.CloneAll(playerPosts);
            c.spawnPoints = spawnPoints != null ? new List<Vector2>(spawnPoints) : new List<Vector2>();
            return c;
        }

        public const int ViewDesktop = 0, ViewVrSim = 1, ViewHeadset = 2;

        // The view this config asks for. A preset saved before the headset existed only has vrMode.
        public int EffectiveViewMode => viewMode >= ViewDesktop && viewMode <= ViewHeadset ? viewMode : vrMode ? ViewVrSim : ViewDesktop;

        // Sets both fields together, so code that only knows vrMode (older packages, other builds) still sees VR on for the sim and the headset.
        public void SetViewMode(int mode)
        {
            viewMode = Mathf.Clamp(mode, ViewDesktop, ViewHeadset);
            vrMode = viewMode != ViewDesktop;
        }

        public static string ViewModeLabel(int mode) => mode == ViewHeadset ? "HEADSET" : mode == ViewVrSim ? "VR-SIM" : "DESKTOP";

        // Only what cannot physically work is corrected (negative counts, zero length, a window outside the run).
        // There are no upper limits: the trainer may ask for as many contacts, seconds or metres as they wish.
        // Returns this for chaining.
        public ScenarioConfig Validate()
        {
            duration = Mathf.Max(duration, 1f);
            speedMultiplier = Mathf.Max(speedMultiplier, 0.01f);
            hostileCount = Mathf.Max(hostileCount, 0);
            friendlyCount = Mathf.Max(friendlyCount, 0);
            civilianCount = Mathf.Max(civilianCount, 0);
            birdCount = Mathf.Max(birdCount, 0);
            swarmSize = Mathf.Max(swarmSize, 0);
            birdFlocks = Mathf.Max(birdFlocks, 0);
            flockSize = Mathf.Max(flockSize, 1);
            crowdCount = Mathf.Max(crowdCount, 0);
            crowdRadius = Mathf.Max(crowdRadius, 0f);
            friendlyNoIffPercent = Mathf.Clamp(friendlyNoIffPercent, 0f, 100f);     // shares: cannot pass 100 %
            hostileSpoofPercent = Mathf.Clamp(hostileSpoofPercent, 0f, 100f);
            swarmTime = Mathf.Clamp(swarmTime, 0f, duration);
            blendInPercent = Mathf.Clamp(blendInPercent, -1f, 100f);        // a share: cannot pass 100 %
            sectorArcDegrees = Mathf.Clamp(sectorArcDegrees, 1f, 360f);     // a circle has 360 degrees
            spawnRingRadius = Mathf.Max(spawnRingRadius, 10f);
            spawnWindowStart = Mathf.Clamp(spawnWindowStart, 0f, duration);
            spawnWindowEnd = Mathf.Clamp(spawnWindowEnd, spawnWindowStart, duration);
            roeFlipTime = Mathf.Clamp(roeFlipTime, 0f, duration);
            if (aiErrorOverride > 1f) aiErrorOverride = 1f;
            detectionSizeMultiplier = Mathf.Max(detectionSizeMultiplier, 0.01f);
            moveRadius = Mathf.Max(moveRadius, 0f);
            walkSpeed = Mathf.Max(walkSpeed, 0.5f);
            runSpeed = Mathf.Max(runSpeed, walkSpeed);
            // Spawn options: a range turned round is swapped, a lone end stands for both; nothing else is limited.
            if (spawnAltMin >= 0f || spawnAltMax >= 0f)
            {
                if (spawnAltMin < 0f) spawnAltMin = spawnAltMax;
                if (spawnAltMax < 0f) spawnAltMax = spawnAltMin;
                if (spawnAltMax < spawnAltMin) { float t = spawnAltMin; spawnAltMin = spawnAltMax; spawnAltMax = t; }
            }
            if (spawnDistMin >= 0f) spawnDistMin = Mathf.Min(spawnDistMin, spawnRingRadius);
            if (float.IsNaN(sectorCentreDegrees) || float.IsInfinity(sectorCentreDegrees)) sectorCentreDegrees = 0f;
            sectorCentreDegrees = Mathf.Repeat(sectorCentreDegrees + 180f, 360f) - 180f;       // a bearing: 370 is 10
            if (float.IsNaN(roostBearing)) roostBearing = 999f;
            roostDistance = Mathf.Max(roostDistance, 0f);
            if (paths == null) paths = new List<FlightPath>();      // a scenario saved before paths existed
            paths.RemoveAll(p => p == null);
            foreach (var p in paths) p.Sanitise();
            ManualLayout.Sanitise(this);
            return this;
        }

        public static float Share(Prevalence p) => p == Prevalence.Low ? 0.15f : p == Prevalence.Medium ? 0.35f : p == Prevalence.High ? 0.6f : -1f;

        // The band a realised hostile share should fall in (doc V7).
        public static Vector2 Band(Prevalence p) => p == Prevalence.Low ? new Vector2(0.10f, 0.20f) : p == Prevalence.Medium ? new Vector2(0.25f, 0.45f) : p == Prevalence.High ? new Vector2(0.5f, 0.7f) : new Vector2(0f, 1f);

        // Turns the Quiet watch and prevalence settings into plain counts, times and length, from the seed (same seed, same
        // result). Called once when a run is started; the trainer's own settings are left as typed.
        public ScenarioConfig ApplyPrevalence()
        {
            var rng = new System.Random(seed ^ 0x3C6EF372);
            double roundDraw = rng.NextDouble(), nullDraw = rng.NextDouble();
            int total = -1;
            if (quietWatch)
            {
                duration = Mathf.Max(60f, watchMinutes * 60f);
                total = Mathf.Max(1, Mathf.RoundToInt(contactsPerHour * watchMinutes / 60f));
                spawnWindowStart = Mathf.Min(60f, duration * 0.05f);
                spawnWindowEnd = duration - Mathf.Min(120f, duration * 0.1f);
                swarmSize = 0;
                birdFlocks = 0;
                if (prevalence == Prevalence.Custom) prevalence = Prevalence.Low;
            }
            else if (prevalence != Prevalence.Custom) total = Mathf.Max(1, prevalenceTotal);

            if (prevalence != Prevalence.Custom && total > 0)
            {
                // Stochastic rounding, so a low share over a few contacts still averages out right across seeds.
                int hostile = Mathf.FloorToInt((float)(total * Share(prevalence) + roundDraw));
                hostile = Mathf.Clamp(hostile, 0, total);
                int rest = total - hostile;
                float own = friendlyCount, civ = civilianCount, bird = birdCount, sum = own + civ + bird;
                if (sum <= 0f) { own = 0.2f; civ = 0.2f; bird = 0.6f; sum = 1f; }
                friendlyCount = Mathf.FloorToInt(rest * own / sum);
                civilianCount = Mathf.FloorToInt(rest * civ / sum);
                birdCount = rest - friendlyCount - civilianCount;
                hostileCount = hostile;
            }
            float chance = nullMissionChance >= 0f ? nullMissionChance : quietWatch ? 0.2f : 0f;
            nullMission = chance > 0f && nullDraw < chance;
            if (nullMission) { birdCount += hostileCount; hostileCount = 0; swarmSize = 0; }
            return Validate();
        }

        public int TotalContacts => hostileCount + friendlyCount + civilianCount + birdCount + swarmSize + birdFlocks * flockSize;

        // Values above these still run; the trainer is only told they may be slow or hard to see.
        public const int ManyContacts = 300;
        public const float FarRing = 4000f;
        public const int ManyCrowd = 20000;

        // The metres the crowd fills: the typed radius, or 1.5 x the spawn ring.
        public float CrowdRadiusEffective => crowdRadius > 0f ? crowdRadius : 1.5f * spawnRingRadius;

        // Empty when nothing needs saying. Never blocks START.
        public string Warning()
        {
            string s = "";
            if (TotalContacts > ManyContacts) s += $"{TotalContacts} contacts in one run may slow the simulation down (above about {ManyContacts}).  ";
            if (crowdCount > ManyCrowd) s += $"{crowdCount} crowd tracks may be slow (above about {ManyCrowd}).  ";
            if (spawnRingRadius > FarRing) s += $"At {spawnRingRadius:F0} m small drones start out too far to see, even through binoculars.  ";
            if (quietWatch && contactsPerHour > 4f) s += $"A Quiet watch should have at most 4 contacts per hour ({contactsPerHour:F0} set).  ";
            if (spawnWindowEnd < duration * 0.5f && TotalContacts > 0 && !quietWatch) s += $"All contacts appear in the first {spawnWindowEnd:F0} s of a {duration:F0} s run (raise \"Spawn until s\").  ";
            return s.Trim();
        }
        public static ScenarioConfig FromPreset(ScenarioPreset p)
        {
            return new ScenarioConfig
            {
                presetName = p.name,
                seed = p.seed,
                duration = p.duration,
                hostileCount = p.hostileCount,
                friendlyCount = p.friendlyCount,
                civilianCount = p.civilianCount,
                birdCount = p.birdCount,
                sectorArcDegrees = p.sectorArcDegrees,
                spawnRingRadius = p.spawnRingRadius,
                spawnWindowStart = p.spawnWindowStart,
                spawnWindowEnd = p.spawnWindowEnd,
            };
        }
    }

    // Where the player's view sits in the window. Full window normally; the right-hand part in split-screen.
    // Player overlays draw inside Rect so they stay out of the trainer's half.
    public static class PlayerView
    {
        public static bool Split;
        // True while a trainer view covers the whole window with something the player overlays must not draw over.
        public static bool HideOverlays;
        public static float TrainerFraction = 0.42f;   // of the window width, left side

        public static Rect Rect => Split
            ? new Rect(Screen.width * TrainerFraction, 0f, Screen.width * (1f - TrainerFraction), Screen.height)
            : new Rect(0f, 0f, Screen.width, Screen.height);
        public static float Width => Split ? Screen.width * (1f - TrainerFraction) : Screen.width;
        public static float Height => Screen.height;

        // Camera viewport rect (0-1) matching Rect.
        public static Rect Viewport => Split ? new Rect(TrainerFraction, 0f, 1f - TrainerFraction, 1f) : new Rect(0f, 0f, 1f, 1f);

        // screenPosition: pixels from the bottom-left, as the Input System reports the mouse.
        public static bool Contains(Vector2 screenPosition)
        {
            return !Split || screenPosition.x >= Screen.width * TrainerFraction;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset()
        {
            Split = false;
            HideOverlays = false;
        }
    }
}
