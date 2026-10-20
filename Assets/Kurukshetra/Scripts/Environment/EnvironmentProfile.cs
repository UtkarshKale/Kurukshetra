using System;
using Kurukshetra.Scenario;
using UnityEngine;

namespace Kurukshetra.Environment
{
    // How a time of day looks. Day has no entry: Day is the scene exactly as the user set it up.
    [Serializable]
    public class TimeLook
    {
        [Header("Sun or moon")]
        [Tooltip("Degrees above the horizon.")]
        [Range(-10f, 90f)] public float sunElevation = 10f;
        public Color sunColour = Color.white;
        public float sunIntensity = 0.5f;

        [Header("Ambient and sky")]
        public Color ambientColour = new Color(0.2f, 0.2f, 0.25f);
        [Tooltip("Sky box for this time of day. Empty = dim and tint the scene's own sky box instead.")]
        public Material skybox;
        [Tooltip("Multiplies the sky box brightness (1 = as authored).")]
        public float skyExposure = 0.5f;
        public Color skyTint = new Color(0.5f, 0.5f, 0.5f);
        [Range(0f, 1f)] public float reflectionIntensity = 0.5f;
        public Color fogColour = new Color(0.4f, 0.4f, 0.45f);

        [Header("Detection")]
        [Tooltip("Multiplies the smallest apparent size that can be picked out. 2 = contacts must look twice as big.")]
        public float minVisibleSizeMultiplier = 1.5f;
    }

    // Every tunable for time of day, visibility and night vision, in one asset.
    [CreateAssetMenu(menuName = "Kurukshetra/Environment Profile", fileName = "EnvironmentProfile")]
    public class EnvironmentProfile : ScriptableObject
    {
        public TimeLook dusk = new TimeLook
        {
            sunElevation = 6f, sunColour = new Color(1f, 0.6f, 0.35f), sunIntensity = 0.45f,
            ambientColour = new Color(0.28f, 0.24f, 0.28f), skyExposure = 0.45f, skyTint = new Color(0.55f, 0.45f, 0.42f),
            reflectionIntensity = 0.4f, fogColour = new Color(0.45f, 0.36f, 0.36f), minVisibleSizeMultiplier = 1.4f,
        };
        public TimeLook night = new TimeLook
        {
            sunElevation = 35f, sunColour = new Color(0.6f, 0.7f, 1f), sunIntensity = 0.06f,
            ambientColour = new Color(0.02f, 0.03f, 0.05f), skyExposure = 0.04f, skyTint = new Color(0.35f, 0.4f, 0.55f),
            reflectionIntensity = 0.04f, fogColour = new Color(0.02f, 0.03f, 0.05f), minVisibleSizeMultiplier = 2f,
        };
        [Tooltip("Fog colour by day, when visibility is not clear.")]
        public Color dayFogColour = new Color(0.75f, 0.8f, 0.86f);

        [Header("Visibility: share of the sensor's maximum visible range that is left")]
        [Range(0.05f, 1f)] public float hazyRange = 0.6f;
        [Range(0.05f, 1f)] public float foggyRange = 0.35f;

        [Header("Night vision (N key, dusk and night only)")]
        [Tooltip("Share of the time-of-day detection penalty that night vision removes. 0.6 at night: 2x becomes 1.4x.")]
        [Range(0f, 1f)] public float nightVisionRecovery = 0.6f;
        public Color nightVisionTint = new Color(0.25f, 1f, 0.3f);
        [Tooltip("Scene lighting while night vision is on, so the picture is actually brighter.")]
        public Color nightVisionAmbient = new Color(0.22f, 0.4f, 0.22f);
        public float nightVisionSunIntensity = 0.5f;
        public float nightVisionSkyExposure = 0.3f;
        [Tooltip("Strength of the green wash over the view.")]
        [Range(0f, 0.6f)] public float nightVisionWash = 0.22f;
        [Range(0f, 0.5f)] public float nightVisionNoise = 0.12f;
        [Tooltip("Diameter of the clear circle, as a fraction of the view height. Smaller = narrower view.")]
        [Range(0.5f, 1.6f)] public float nightVisionTube = 1.05f;

        public TimeLook Look(TimeOfDay time)
        {
            return time == TimeOfDay.Night ? night : time == TimeOfDay.Dusk ? dusk : null;
        }
    }

    // The gameplay side of the environment, kept free of scene objects so it can be self-tested.
    public static class EnvironmentMath
    {
        // What the minimum visible apparent size is multiplied by.
        public static float SizeMultiplier(EnvironmentProfile p, TimeOfDay time, bool nightVision)
        {
            var look = p.Look(time);
            if (look == null) return 1f;
            float penalty = Mathf.Max(1f, look.minVisibleSizeMultiplier);
            return nightVision ? 1f + (penalty - 1f) * (1f - p.nightVisionRecovery) : penalty;
        }

        // Share of the maximum visible range that is left.
        public static float RangeMultiplier(EnvironmentProfile p, Visibility v)
        {
            return v == Visibility.Foggy ? p.foggyRange : v == Visibility.Hazy ? p.hazyRange : 1f;
        }

        // Exponential-squared fog density that fades things out at about the given distance.
        public static float FogDensity(float visibilityMetres)
        {
            return visibilityMetres > 0f ? 2f / visibilityMetres : 0f;
        }

        // Worse by `steps` levels (difficulty adds haze), clamped to the ends.
        public static Visibility Worsen(Visibility v, int steps)
        {
            return (Visibility)Mathf.Clamp((int)v + steps, (int)Visibility.Clear, (int)Visibility.Foggy);
        }

        public static string Describe(TimeOfDay time, Visibility visibility, float nvPercent)
        {
            return $"{time}, {visibility}" + (time == TimeOfDay.Day ? "" : $", NV used {nvPercent:F0}%");
        }
    }
}
