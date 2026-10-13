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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Time = TimeOfDay.Day;
            Visibility = Visibility.Clear;
            Difficulty = "";
            NightVisionSeconds = 0f;
            AllowMovement = false;
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
        [Range(0.1f, 5f)] public float speedMultiplier = 1f;

        [Header("Mix")]
        public int hostileCount = 3;
        public int friendlyCount = 2;
        public int civilianCount = 2;
        public int birdCount = 6;

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
        public RoeCard roeFlipTo = RoeCard.UnderAttack;

        [Header("Conditions")]
        public TimeOfDay timeOfDay = TimeOfDay.Day;
        public Visibility visibility = Visibility.Clear;
        [Tooltip("Extra multiplier on the smallest apparent size that can be picked out (from the difficulty level). 1 = none.")]
        public float detectionSizeMultiplier = 1f;
        [Tooltip("Name of the difficulty level applied to this config; empty = none.")]
        public string difficulty = "";

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

        // Clamp everything to sane ranges. Returns this for chaining.
        public ScenarioConfig Validate()
        {
            duration = Mathf.Clamp(duration, 30f, 1800f);
            speedMultiplier = Mathf.Clamp(speedMultiplier, 0.1f, 5f);
            hostileCount = Mathf.Clamp(hostileCount, 0, 50);
            friendlyCount = Mathf.Clamp(friendlyCount, 0, 50);
            civilianCount = Mathf.Clamp(civilianCount, 0, 50);
            birdCount = Mathf.Clamp(birdCount, 0, 100);
            sectorArcDegrees = Mathf.Clamp(sectorArcDegrees, 10f, 360f);
            spawnRingRadius = Mathf.Clamp(spawnRingRadius, 200f, 3000f);
            spawnWindowStart = Mathf.Clamp(spawnWindowStart, 0f, duration);
            spawnWindowEnd = Mathf.Clamp(spawnWindowEnd, spawnWindowStart, duration);
            roeFlipTime = Mathf.Clamp(roeFlipTime, 0f, duration);
            if (aiErrorOverride > 1f) aiErrorOverride = 1f;
            detectionSizeMultiplier = Mathf.Clamp(detectionSizeMultiplier, 0.25f, 4f);
            moveRadius = Mathf.Clamp(moveRadius, 0f, 2000f);
            walkSpeed = Mathf.Clamp(walkSpeed, 0.5f, 15f);
            runSpeed = Mathf.Clamp(runSpeed, walkSpeed, 20f);
            return this;
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
