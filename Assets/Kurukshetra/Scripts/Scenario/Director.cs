using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kurukshetra.Scenario
{
    // One thing the Director did, kept as a difficulty covariate for the AAR (doc §1.9: every injection is logged).
    [Serializable]
    public class DirectorEvent
    {
        public float time;
        public string kind;         // wave / decoy / sensor drop / hold back
        public string reason;
        public int intensity;
    }

    // The Scenario Director (doc §1.9, module 10), training runs only: off in tests and reference packages.
    // Every `checkSeconds` it reads the ground-truth threat state and the soldier's recent accuracy, never their input rate:
    //  - doing well (recent calls mostly right, no live hostile left unanswered, nothing leaked): intensity goes up and it
    //    adds a small wave, a decoy, or a sensor drop;
    //  - struggling (calls mostly wrong, two or more live hostiles unanswered, or a leak): it holds back for a while.
    //    Downward moves are capped per run, so a soldier cannot make it easier by playing badly; wrong calls made implausibly
    //    fast after the lock are counted as possible sandbagging and stop it from easing off.
    // Its state is hidden from the soldier. Everything is seeded and logged.
    public class Director : MonoBehaviour
    {
        public static readonly List<DirectorEvent> Log = new List<DirectorEvent>();
        public static int SandbagFlags { get; private set; }
        public static bool Running { get; private set; }

        public float checkSeconds = 20f;
        public int maxDownMoves = 2;
        [Tooltip("A wrong call made sooner than this after the lock is counted as possible sandbagging.")]
        public float sandbagSeconds = 0.6f;

        ScenarioSpawner spawner;
        System.Random rng;
        float nextCheck, holdUntil;
        int intensity = 1, downMoves, nextId = 500;
        readonly HashSet<int> judged = new HashSet<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Log.Clear(); SandbagFlags = 0; Running = false; }

        public void Begin(ScenarioSpawner owner, int seed)
        {
            spawner = owner;
            rng = new System.Random(seed ^ 0x6A09E667);
            Log.Clear();
            judged.Clear();
            SandbagFlags = 0;
            intensity = 1;
            downMoves = 0;
            nextCheck = checkSeconds;
            nextId = Mathf.Max(500, owner.FirstFreeId);      // clear of the crowd track numbers
            holdUntil = -1f;
            Running = true;
            enabled = true;
        }

        public void Stop() { Running = false; enabled = false; }

        // What it reads: recent accuracy, live hostiles not yet answered, leaks, sandbagging. Pure, for the self-test.
        public struct Assessment { public int recentCalls, recentRight, unansweredHostiles, leaks, fastWrong; }

        public static Assessment Assess(IList<ContactRecord> records, IList<Contact> live, float now, float window, float sandbagSeconds)
        {
            var a = new Assessment();
            foreach (var r in records)
            {
                if (r.callTime >= 0f && now - r.callTime <= window)
                {
                    a.recentCalls++;
                    bool right = r.call == RightCallFor(r.truthClass);
                    if (right) a.recentRight++;
                    else if (r.firstLockTime >= 0f && r.callTime - r.firstLockTime < sandbagSeconds) a.fastWrong++;
                }
                if (r.despawnReason == "ReachedDefendedPoint" && r.despawnTime >= 0f && now - r.despawnTime <= window && r.truthClass.IsHostile()) a.leaks++;
            }
            if (live != null)
                foreach (var c in live)
                    if (c != null && c.TruthClass.IsHostile() && c.FirstInViewTime >= 0f && !c.Data.Decided) a.unansweredHostiles++;
            return a;
        }

        static PlayerCall RightCallFor(ContactClass c)
        {
            switch (c)
            {
                case ContactClass.HostileQuad: return PlayerCall.HostileQuad;
                case ContactClass.HostileFixedWing: return PlayerCall.HostileFixedWing;
                case ContactClass.FriendlyISR: return PlayerCall.OwnIsrDrone;
                case ContactClass.CivilianQuad: return PlayerCall.CivilianDrone;
                default: return PlayerCall.Bird;
            }
        }

        // +1 = push harder, -1 = ease off, 0 = keep as is.
        public static int Decide(Assessment a, bool canEaseOff)
        {
            bool struggling = a.leaks > 0 || a.unansweredHostiles >= 2 || (a.recentCalls >= 3 && a.recentRight < a.recentCalls * 0.5f);
            bool doingWell = a.leaks == 0 && a.unansweredHostiles == 0 && (a.recentCalls == 0 || a.recentRight >= a.recentCalls * 0.8f);
            if (struggling) return canEaseOff && a.fastWrong == 0 ? -1 : 0;
            return doingWell ? 1 : 0;
        }

        void Update()
        {
            if (spawner == null || spawner.Finished || spawner.Waiting) return;
            float now = ContactRegistry.ScenarioTime;
            if (now < nextCheck) return;
            nextCheck = now + checkSeconds;

            var a = Assess(ContactRegistry.Records, ContactRegistry.Live, now, 60f, sandbagSeconds);
            foreach (var r in ContactRegistry.Records)
                if (r.callTime >= 0f && !judged.Contains(r.id))
                {
                    judged.Add(r.id);
                    if (r.call != RightCallFor(r.truthClass) && r.firstLockTime >= 0f && r.callTime - r.firstLockTime < sandbagSeconds) SandbagFlags++;
                }
            int move = Decide(a, downMoves < maxDownMoves);
            if (now < holdUntil && move > 0) move = 0;
            if (move < 0)
            {
                downMoves++;
                intensity = Mathf.Max(0, intensity - 1);
                holdUntil = now + checkSeconds * 2f;
                Record("hold back", Why(a), now);
            }
            else if (move > 0)
            {
                intensity = Mathf.Min(4, intensity + 1);
                Inject(now, Why(a));
            }
        }

        static string Why(Assessment a) => $"{a.recentRight}/{a.recentCalls} recent calls right, {a.unansweredHostiles} hostile(s) unanswered, {a.leaks} leak(s)" + (a.fastWrong > 0 ? $", {a.fastWrong} very fast wrong call(s)" : "");

        void Inject(float now, string why)
        {
            double roll = rng.NextDouble();
            if (roll < 0.55)
            {
                int n = 1 + Mathf.Min(intensity, 3) / 2 + rng.Next(2);
                for (int i = 0; i < n; i++) SpawnFromRing(i == 0 ? ContactClass.HostileQuad : PickDecoy());
                Record($"wave of {n}", why, now);
            }
            else if (roll < 0.85)
            {
                SpawnFromRing(PickDecoy());
                Record("decoy", why, now);
            }
            else
            {
                var sensors = FindAnyObjectByType<Kurukshetra.Sensors.SensorController>();
                if (sensors != null && sensors.state == Kurukshetra.Sensors.SensorState.Ok)
                {
                    sensors.SetState(Kurukshetra.Sensors.SensorState.Degraded);
                    Invoke(nameof(RestoreSensors), 30f);
                    Record("sensor drop (30 s)", why, now);
                }
                else { SpawnFromRing(PickDecoy()); Record("decoy", why, now); }
            }
        }

        void RestoreSensors()
        {
            var sensors = FindAnyObjectByType<Kurukshetra.Sensors.SensorController>();
            if (sensors != null) sensors.SetState(Kurukshetra.Sensors.SensorState.Ok);
        }

        ContactClass PickDecoy()
        {
            var d = new[] { ContactClass.Bird, ContactClass.Bird, ContactClass.CivilianQuad, ContactClass.FriendlyISR };
            return d[rng.Next(d.Length)];
        }

        void SpawnFromRing(ContactClass cls)
        {
            Vector3 post = spawner.defendedPoint != null ? spawner.defendedPoint.position : spawner.transform.position;
            float half = spawner.ActiveArc * 0.5f;
            float bearing = spawner.transform.eulerAngles.y + spawner.ActiveSectorOffset + Mathf.Lerp(-half, half, (float)rng.NextDouble());
            Vector3 dir = Quaternion.Euler(0f, bearing, 0f) * Vector3.forward;
            Vector3 start = post + dir * spawner.ActiveRadius + Vector3.up * Mathf.Lerp(50f, 120f, (float)rng.NextDouble());
            Vector3 goal = cls.IsHostile() ? post + Vector3.up * 10f : post + Quaternion.Euler(0f, 90f * (rng.NextDouble() < 0.5 ? -1f : 1f), 0f) * dir * spawner.ActiveRadius + Vector3.up * 60f;
            spawner.SpawnSingle(cls, start, goal, cls == ContactClass.Bird ? 10f : 14f, rng.Next(), nextId++);
        }

        void Record(string kind, string reason, float now)
        {
            Log.Add(new DirectorEvent { time = now, kind = kind, reason = reason, intensity = intensity });
            ContactRegistry.Note($"Director: {kind} ({reason})");
        }
    }
}
