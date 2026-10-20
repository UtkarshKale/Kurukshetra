using System;
using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Sensors
{
    public enum SensorState
    {
        Ok,
        Degraded,
    }

    // What one contact class gives off.
    [Serializable]
    public class ClassSignature
    {
        public ContactClass contactClass;
        [Tooltip("Off for anything that emits no RF, e.g. fibre-controlled drones and birds.")]
        public bool rfEmitter = true;
        [Range(0f, 1f)] public float rfStrength;
        [Tooltip("Each contact's strength varies by up to this fraction either way, fixed per contact.")]
        [Range(0f, 0.5f)] public float rfVariation = 0.15f;
        [Range(0f, 1f)] public float loudness;
    }

    // A value that differs between healthy and degraded sensors.
    [Serializable]
    public struct OkDegraded
    {
        public float ok;
        public float degraded;

        public OkDegraded(float ok, float degraded)
        {
            this.ok = ok;
            this.degraded = degraded;
        }

        public float Get(SensorState state)
        {
            return state == SensorState.Ok ? ok : degraded;
        }
    }

    // When the AI is wrong about a class, what it plausibly says instead.
    [Serializable]
    public class HintConfusion
    {
        public ContactClass truth;
        public List<PlayerCall> wrongOptions = new List<PlayerCall>();
    }

    [CreateAssetMenu(menuName = "Chaukanna/Sensor Profile", fileName = "SensorProfile")]
    public class SensorProfile : ScriptableObject
    {
        [Header("Per-class signatures")]
        public List<ClassSignature> signatures = new List<ClassSignature>();

        [Header("Ranges")]
        [Tooltip("Contacts beyond this are not sensed at all (metres).")]
        public float sensorRange = 1200f;
        [Tooltip("Distance at which a sound is heard at half its loudness (metres).")]
        public float soundHalfRange = 400f;
        [Tooltip("Below this heard level there is no sound bearing.")]
        [Range(0f, 0.2f)] public float audibleThreshold = 0.02f;

        [Header("RF bar")]
        [Tooltip("Random wobble added to the RF reading (0-1).")]
        public OkDegraded rfNoise = new OkDegraded(0.01f, 0.15f);

        [Header("Sound bearing")]
        [Tooltip("Seconds the bearing lags behind the truth.")]
        public OkDegraded bearingDelay = new OkDegraded(0f, 2f);
        [Tooltip("Random error on the bearing, degrees either way.")]
        public OkDegraded bearingNoise = new OkDegraded(2f, 20f);

        [Header("AI assist")]
        [Tooltip("Chance the AI hint is wrong. It is wrong on purpose some of the time.")]
        public OkDegraded aiErrorRate = new OkDegraded(0.15f, 0.35f);
        [Tooltip("Confidence shown with a right hint, percent (min, max).")]
        public Vector2 aiRightConfidence = new Vector2(60f, 92f);
        [Tooltip("Confidence shown with a wrong hint, percent (min, max). Overlaps the right range so confidence alone can't be trusted.")]
        public Vector2 aiWrongConfidence = new Vector2(55f, 96f);
        public List<HintConfusion> confusions = new List<HintConfusion>();

        void Reset()
        {
            ApplyDefaults();
        }

        [ContextMenu("Reset To Defaults")]
        public void ApplyDefaults()
        {
            signatures = new List<ClassSignature>
            {
                // Hostile and civilian quads overlap on RF on purpose: RF alone can't decide.
                new ClassSignature { contactClass = ContactClass.HostileQuad, rfEmitter = true, rfStrength = 0.7f, loudness = 0.5f },
                new ClassSignature { contactClass = ContactClass.CivilianQuad, rfEmitter = true, rfStrength = 0.5f, loudness = 0.5f },
                new ClassSignature { contactClass = ContactClass.FriendlyISR, rfEmitter = true, rfStrength = 0.6f, loudness = 0.35f },
                new ClassSignature { contactClass = ContactClass.HostileFixedWing, rfEmitter = true, rfStrength = 0.3f, loudness = 0.9f },
                new ClassSignature { contactClass = ContactClass.Bird, rfEmitter = false, rfStrength = 0f, rfVariation = 0f, loudness = 0.05f },
            };
            confusions = new List<HintConfusion>
            {
                new HintConfusion { truth = ContactClass.HostileQuad, wrongOptions = { PlayerCall.CivilianDrone, PlayerCall.Bird } },
                new HintConfusion { truth = ContactClass.CivilianQuad, wrongOptions = { PlayerCall.HostileQuad, PlayerCall.Bird } },
                new HintConfusion { truth = ContactClass.HostileFixedWing, wrongOptions = { PlayerCall.OwnIsrDrone, PlayerCall.Bird } },
                new HintConfusion { truth = ContactClass.FriendlyISR, wrongOptions = { PlayerCall.HostileFixedWing } },
                new HintConfusion { truth = ContactClass.Bird, wrongOptions = { PlayerCall.HostileQuad, PlayerCall.CivilianDrone } },
            };
        }

        public ClassSignature Signature(ContactClass c)
        {
            for (int i = 0; i < signatures.Count; i++)
                if (signatures[i].contactClass == c) return signatures[i];
            return null;
        }

        public HintConfusion Confusion(ContactClass c)
        {
            for (int i = 0; i < confusions.Count; i++)
                if (confusions[i].truth == c) return confusions[i];
            return null;
        }
    }
}
