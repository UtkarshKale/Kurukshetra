using System;
using System.Collections.Generic;
using Kurukshetra.Scenario;

namespace Kurukshetra.Scoring
{
    // What the trainee calls a whole group (K key): a drone swarm, a bird flock or a mix of both.
    public enum GroupCall { None = 0, DroneSwarm = 1, BirdFlock = 2, Mixed = 3 }

    // One call on one group: what was called, what the group really was, when, and how long after its first member was in view.
    [Serializable]
    public class GroupCallRecord
    {
        public int groupId = -1;           // the swarm / flock the members came from, -1 = none known
        public string call, truth;         // GroupCall names
        public float time;                 // scenario seconds of the call
        public float seconds = -1f;        // from the first member in view to the call; -1 = unknown
        public int count;                  // members in the selection when it was called
        public bool correct;
        public List<int> members = new List<int>();
    }

    [Serializable]
    public class GroupCallResult
    {
        public bool happened;
        public int calls, right, wrong;
        public float meanSeconds = -1f;    // mean time to call, over the right calls that have a time
        public int points, maxPoints;
        public string summary = "";
        public List<GroupCallRecord> items = new List<GroupCallRecord>();
    }

    // The group calls of a run and their scoring. One record per group: the first call on a group is the one that counts (a later
    // call on the same group, or on a selection that mostly overlaps it, is refused). Scored right / wrong, with the time to call.
    // Members of a called group still keep their own per-contact 1-5 records and scores.
    public static class GroupCalls
    {
        public const int RightPoints = 10;                // a node of its own: 10 for the right group call, 0 for a wrong one
        public const int MinMembers = 2;
        public static readonly List<GroupCallRecord> Records = new List<GroupCallRecord>();

        public static void Reset() { Records.Clear(); }

        public static string Label(GroupCall c)
        {
            return c == GroupCall.DroneSwarm ? "Drone swarm" : c == GroupCall.BirdFlock ? "Bird flock" : c == GroupCall.Mixed ? "Mixed" : "no call";
        }

        // What a set of contacts really is: all birds, all drones (any drone class), or both.
        public static GroupCall TruthOf(IEnumerable<ContactClass> classes)
        {
            bool birds = false, drones = false;
            foreach (var c in classes) { if (c == ContactClass.Bird) birds = true; else drones = true; }
            return birds && drones ? GroupCall.Mixed : birds ? GroupCall.BirdFlock : drones ? GroupCall.DroneSwarm : GroupCall.None;
        }

        // The group id most of the members share (>= 0), or -1.
        public static int GroupOf(IList<ContactRecord> members)
        {
            int best = -1, bestN = 0;
            for (int i = 0; i < members.Count; i++)
            {
                int g = members[i].groupId;
                if (g < 0) continue;
                int n = 0;
                for (int j = 0; j < members.Count; j++) if (members[j].groupId == g) n++;
                if (n > bestN) { best = g; bestN = n; }
            }
            return bestN * 2 >= members.Count ? best : -1;
        }

        public static GroupCallRecord Find(IList<GroupCallRecord> records, int groupId, IList<ContactRecord> members)
        {
            foreach (var e in records)
            {
                if (groupId >= 0 && e.groupId == groupId) return e;
                int overlap = 0;
                foreach (var m in members) if (e.members.Contains(m.id)) overlap++;
                if (overlap * 2 >= Math.Min(e.members.Count, members.Count) && overlap > 0) return e;
            }
            return null;
        }

        // Records the call. Returns the new record, or null when the group has already been called (existing is set) or the
        // selection is too small to be a group. Pure on its arguments, so a test can use its own list.
        public static GroupCallRecord Declare(IList<GroupCallRecord> into, IList<ContactRecord> members, GroupCall call, float now, out GroupCallRecord existing)
        {
            existing = null;
            if (call == GroupCall.None || members == null || members.Count < MinMembers) return null;
            int gid = GroupOf(members);
            existing = Find(into, gid, members);
            if (existing != null) return null;
            var classes = new List<ContactClass>(members.Count);
            float firstSeen = float.MaxValue;
            foreach (var m in members)
            {
                classes.Add(m.truthClass);
                float seen = m.firstInViewTime >= 0f ? m.firstInViewTime : m.spawnTime;
                if (seen < firstSeen) firstSeen = seen;
            }
            var truth = TruthOf(classes);
            var rec = new GroupCallRecord
            {
                groupId = gid, call = call.ToString(), truth = truth.ToString(), time = now, count = members.Count, correct = call == truth,
                seconds = firstSeen == float.MaxValue ? -1f : Math.Max(0f, now - firstSeen),
            };
            foreach (var m in members) rec.members.Add(m.id);
            into.Add(rec);
            return rec;
        }

        public static GroupCallResult Evaluate(IList<GroupCallRecord> records)
        {
            var r = new GroupCallResult();
            if (records == null || records.Count == 0) return r;
            r.happened = true;
            float sum = 0f; int timed = 0;
            foreach (var g in records)
            {
                r.items.Add(g);
                r.calls++;
                if (g.correct) { r.right++; if (g.seconds >= 0f) { sum += g.seconds; timed++; } }
                else r.wrong++;
            }
            r.meanSeconds = timed > 0 ? sum / timed : -1f;
            r.points = r.right * RightPoints;
            r.maxPoints = r.calls * RightPoints;
            r.summary = $"{r.right} of {r.calls} group call{(r.calls == 1 ? "" : "s")} right"
                + (r.meanSeconds >= 0f ? $", {r.meanSeconds:F0} s from first sight to the call on average" : "")
                + (r.wrong > 0 ? "; wrong: " + string.Join(", ", WrongList(records)) : "");
            return r;
        }

        static List<string> WrongList(IList<GroupCallRecord> records)
        {
            var l = new List<string>();
            foreach (var g in records) if (!g.correct) l.Add($"called {g.call} but it was {g.truth} ({g.count} members)");
            return l;
        }
    }
}
