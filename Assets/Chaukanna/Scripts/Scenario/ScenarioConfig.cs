using System;
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
        public static string PackageId = "";

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
        [Tooltip("Bird flocks crossing the sector, each flying together.")]
        public int birdFlocks;
        public int flockSize = 8;

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
            return (ScenarioConfig)MemberwiseClone();
        }

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

        // Empty when nothing needs saying. Never blocks START.
        public string Warning()
        {
            string s = "";
            if (TotalContacts > ManyContacts) s += $"{TotalContacts} contacts in one run may slow the simulation down (above about {ManyContacts}).  ";
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
