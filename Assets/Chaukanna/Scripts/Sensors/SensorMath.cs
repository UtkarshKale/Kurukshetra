using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Sensors
{
    public struct AiHint
    {
        public PlayerCall call;
        public int confidence;   // percent
        public bool wrong;
    }

    // Stateless sensor maths, kept apart from the MonoBehaviour so it can be self-tested.
    public static class SensorMath
    {
        // The class the AI would be right to say.
        public static PlayerCall RightCall(ContactClass c)
        {
            switch (c)
            {
                case ContactClass.HostileQuad: return PlayerCall.HostileQuad;
                case ContactClass.HostileFixedWing: return PlayerCall.HostileFixedWing;
                case ContactClass.FriendlyISR: return PlayerCall.OwnIsrDrone;
                case ContactClass.CivilianQuad: return PlayerCall.CivilianDrone;
                case ContactClass.Bird: return PlayerCall.Bird;
                default: return PlayerCall.None;
            }
        }

        static System.Random Rng(int seed, int contactId, int salt)
        {
            unchecked { return new System.Random(seed * 73856093 ^ contactId * 19349663 ^ salt); }
        }

        // Same seed and contact id always give the same hint.
        public static AiHint PickHint(SensorProfile profile, int seed, int contactId, ContactClass truth, float errorRate)
        {
            var rng = Rng(seed, contactId, 0x0A1);
            double roll = rng.NextDouble(), pick = rng.NextDouble(), conf = rng.NextDouble();

            var confusion = profile.Confusion(truth);
            bool wrong = roll < errorRate && confusion != null && confusion.wrongOptions.Count > 0;
            Vector2 band = wrong ? profile.aiWrongConfidence : profile.aiRightConfidence;
            return new AiHint
            {
                wrong = wrong,
                call = wrong ? confusion.wrongOptions[Mathf.Min((int)(pick * confusion.wrongOptions.Count), confusion.wrongOptions.Count - 1)] : RightCall(truth),
                confidence = Mathf.RoundToInt(Mathf.Lerp(band.x, band.y, (float)conf)),
            };
        }

        // 0-1 multiplier fixed per contact, so two drones of one class don't read identically.
        public static float Variation(int seed, int contactId, float variation)
        {
            return 1f + ((float)Rng(seed, contactId, 0x0B2).NextDouble() * 2f - 1f) * variation;
        }

        // Received RF: emitter strength falling linearly to zero at the edge of sensor range.
        public static float RfLevel(float strength, float range, float sensorRange)
        {
            if (sensorRange <= 0f || range >= sensorRange) return 0f;
            return Mathf.Clamp01(strength * (1f - range / sensorRange));
        }

        // Heard loudness: half at halfRange, a quarter-ish beyond.
        public static float HeardLevel(float loudness, float range, float halfRange)
        {
            float r = range / Mathf.Max(1f, halfRange);
            return loudness / (1f + r * r);
        }

        // Compass bearing of a world direction, degrees clockwise from +Z.
        public static float WorldBearing(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }

        // Bearing relative to where the observer faces: positive = to the right, range -180..180.
        public static float RelativeBearing(float facingYaw, float worldBearing)
        {
            return Mathf.DeltaAngle(facingYaw, worldBearing);
        }
    }
}
