using System.Collections.Generic;

namespace Kurukshetra.Scenario
{
    // IFF / Remote ID (the I key). The soldier challenges the locked contact or the selected ones; the answer comes back
    // after 0.6 to 1.4 s (seeded per contact). It is fallible evidence, not proof: own drones can have the transponder off,
    // civil drones can fly without Remote ID, birds never answer, and a hostile may answer as an own drone (a spoof).
    // Scoring is not changed by IFF in DS-1; the facts go to the record for the AAR.
    public static class Iff
    {
        public const float MinDelay = 0.6f, MaxDelay = 1.4f;
        public const string NotAvailable = "IFF not available";

        static readonly List<Contact> pending = new List<Contact>();
        static int runSeed;

        public static void BeginRun(int seed) { runSeed = seed; pending.Clear(); }

        // A repeatable number in [0, 1) from (seed, id, salt): the same contact always draws the same value.
        public static float Hash01(int seed, int id, int salt)
        {
            unchecked
            {
                uint h = (uint)seed * 2654435761u ^ (uint)id * 40503u ^ (uint)salt * 2246822519u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }

        public static bool Roll(int seed, int id, float percent, int salt) => Hash01(seed, id, salt) * 100f < percent;
        public static float Delay(int seed, int id) => MinDelay + (MaxDelay - MinDelay) * Hash01(seed, id, 0x1FF);

        // What the answer says, by what the contact really is. anomalyShown: a blend-in hostile has already given itself away.
        public static string ReplyText(ContactClass truth, bool deceptive, bool anomalyShown, bool noReplyFlag, bool spoofFlag, int seed, int id)
        {
            switch (truth)
            {
                case ContactClass.FriendlyISR:
                    return noReplyFlag ? "IFF: NO REPLY" : "IFF: VALID (own)";
                case ContactClass.CivilianQuad:
                    return noReplyFlag ? "NO REMOTE ID" : "REMOTE ID: civil, operator " + (1000 + (int)(Hash01(seed, id, 0x0B) * 9000f)) + ", route filed";
                case ContactClass.Bird:
                    return "IFF: NO REPLY";
                default:    // hostile
                    if (deceptive) return anomalyShown ? "IFF: NO REPLY" : "IFF: VALID (own)";
                    return spoofFlag ? "IFF: VALID (own)" : "IFF: NO REPLY";
            }
        }

        // The route line of the lock label (what a real air picture shows); empty when the run has no crowd or no aid.
        public static string RouteCue(Contact c)
        {
            if (!RunConditions.CrowdActive || !RunConditions.IffAid || c == null) return "";
            var d = c.Data;
            switch (c.TruthClass)
            {
                case ContactClass.FriendlyISR:
                case ContactClass.CivilianQuad:
                    return "ON FILED ROUTE (" + (d.fromCrowd ? PatternLabel(d.crowdPattern) : c.TruthClass == ContactClass.FriendlyISR ? "patrol" : "transit") + ")";
                case ContactClass.Bird:
                    return "NO FILED ROUTE";
                default:
                    if (c.Deceptive) return c.AnomalyShown ? "" : "ON PATROL ROUTE";
                    return "NO FILED ROUTE";
            }
        }

        public static string PatternLabel(int pattern)
        {
            switch ((CrowdPattern)pattern)
            {
                case CrowdPattern.Orbit: return "patrol";
                case CrowdPattern.Corridor: return "corridor";
                case CrowdPattern.Grid: return "survey";
                case CrowdPattern.Station: return "inspection";
                case CrowdPattern.Cluster: return "formation";
                default: return "survey";
            }
        }

        // Truth-side flags for a contact that was planned (not a crowd drone), drawn from the seed and id.
        public static void SetFlags(ContactRecord r, int seed, int id, float friendlyNoIffPercent, float hostileSpoofPercent)
        {
            r.iffNoReply = Roll(seed, id, friendlyNoIffPercent, 1);
            r.iffSpoof = Roll(seed, id, hostileSpoofPercent, 2);
        }

        // Challenge one contact. False when it was already challenged.
        public static bool Ask(Contact c)
        {
            if (c == null || c.Data.iffChecked) return false;
            c.Data.iffChecked = true;
            c.Data.iffAskedTime = ContactRegistry.ScenarioTime;
            c.Data.iffReply = "";
            c.Data.Log("IFF challenge");
            pending.Add(c);
            return true;
        }

        public static bool Pending(Contact c) => c != null && c.Data.iffChecked && c.Data.iffTime < 0f;
        public static bool Replied(Contact c) => c != null && c.Data.iffTime >= 0f;

        // Delivers the replies that are due. Called every frame.
        public static void Tick(float now)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var c = pending[i];
                if (c == null) { pending.RemoveAt(i); continue; }
                var d = c.Data;
                if (now < d.iffAskedTime + Delay(runSeed, c.Id)) continue;
                d.iffTime = now;
                d.iffReply = ReplyText(c.TruthClass, c.Deceptive, c.AnomalyShown, d.iffNoReply, d.iffSpoof, runSeed, c.Id);
                d.Log("IFF reply: " + d.iffReply);
                pending.RemoveAt(i);
            }
        }

        // One line for the lock label; empty when the contact was never challenged.
        public static string Line(Contact c)
        {
            if (c == null || !c.Data.iffChecked) return "";
            return c.Data.iffTime < 0f ? "IFF: CHALLENGING..." : c.Data.iffReply;
        }
    }
}
