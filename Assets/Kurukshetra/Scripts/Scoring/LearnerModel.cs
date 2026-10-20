using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Kurukshetra.Scenario;
using UnityEngine;

namespace Kurukshetra.Scoring
{
    // One graded event from a run: was the soldier right at this task, on this class, in this light, at this range?
    [Serializable]
    public class LearnerObservation
    {
        public string task;         // detect / classify / decide
        public string cls;          // ContactClass name
        public string light;        // Day / Dusk / Night
        public int rangeBin;        // see LearnerModel.RangeBin
        public bool correct;
        public string session;      // timestamp of the run it came from
    }

    [Serializable]
    public class LearnerWeight
    {
        public string key;          // e.g. "task:classify", "class:Bird", "light:Night", "range:2"
        public float beta;
        public int n, right;        // evidence behind it, for display
    }

    // Everything kept about one soldier. Saved as JSON in <persistentDataPath>/learner/.
    [Serializable]
    public class LearnerProfile
    {
        public string player = "Player 1";
        public float theta;                         // ability, logit units; 0 = an average trainee on an average item
        public int sessions;
        public List<LearnerWeight> weights = new List<LearnerWeight>();
        public List<LearnerObservation> observations = new List<LearnerObservation>();

        public float Beta(string key)
        {
            foreach (var w in weights) if (w.key == key) return w.beta;
            return 0f;
        }
    }

    // What the model suggests for the next run.
    [Serializable]
    public class LearnerPlan
    {
        public string focusClass = "";              // the class to see more of
        public TimeOfDay light;
        public int rangeBin;
        public float spawnRadius;
        public float predicted;                     // predicted P(right call) for that cell
        public int difficultyStep;                  // -1 easier, 0 same, +1 harder
        public string text = "";
    }

    // A factored learner model in the spirit of PFA / AFM (doc §1.9, module 9): one logistic model,
    //   logit P(correct) = ability + task + class + light + range,
    // fitted to every graded event the soldier has produced, with each weight pulled towards the pooled value of all
    // other soldiers on this PC (or towards a placeholder prior when there are none). It predicts P(correct) for any cell,
    // names the weakest ones, and picks the next run's conditions so the predicted success is near 75 %.
    // Interactions (e.g. night x fixed-wing) are not modelled: that needs more data than a few sessions give.
    public static class LearnerModel
    {
        public const float TargetSuccess = 0.75f;
        public const int MaxObservations = 3000;
        public static readonly string[] Tasks = { "detect", "classify", "decide" };
        public static readonly string[] Classes = { "HostileQuad", "HostileFixedWing", "FriendlyISR", "CivilianQuad", "Bird" };
        public static readonly string[] Lights = { "Day", "Dusk", "Night" };
        public static readonly float[] RangeEdges = { 300f, 600f, 1000f };    // bins: <300, 300-600, 600-1000, >1000 m
        public static readonly string[] RangeNames = { "under 300 m", "300-600 m", "600-1000 m", "over 1000 m" };

        public static string Folder => Path.Combine(Application.persistentDataPath, "learner");

        public static int RangeBin(float metres)
        {
            if (metres < 0f) return 1;
            for (int i = 0; i < RangeEdges.Length; i++) if (metres < RangeEdges[i]) return i;
            return RangeEdges.Length;
        }

        // Placeholder priors, used only before there is any data: tasks get harder down the chain, look-alike classes and
        // night and range make recognition harder. To be replaced by pooled cohort values as soon as sessions exist.
        public static float Prior(string key)
        {
            switch (key)
            {
                case "task:detect": return 1.0f;
                case "task:classify": return 0.6f;
                case "task:decide": return 0.8f;
                case "class:HostileFixedWing": return -0.3f;
                case "class:Bird": return -0.2f;
                case "class:CivilianQuad": return -0.1f;
                case "light:Dusk": return -0.3f;
                case "light:Night": return -0.8f;
                case "range:0": return 0.4f;
                case "range:2": return -0.4f;
                case "range:3": return -0.9f;
                default: return 0f;
            }
        }

        // --- Observations from a finished run.
        public static List<LearnerObservation> Observe(SessionResult s, IList<ContactRecord> records, RuleSet rules)
        {
            var list = new List<LearnerObservation>();
            if (s == null) return list;
            string light = string.IsNullOrEmpty(s.timeOfDay) ? "Day" : s.timeOfDay;
            foreach (var c in s.contacts)
            {
                if (!c.scored) continue;
                ContactRecord r = null;
                if (records != null) foreach (var x in records) if (x.id == c.id) { r = x; break; }
                float rangeSeen = r != null ? RangeAt(r, c.firstInViewTime >= 0f ? c.firstInViewTime : (r.firstDetectableTime >= 0f ? r.firstDetectableTime : r.spawnTime)) : -1f;

                // Detect: brought into view within the medium band.
                list.Add(new LearnerObservation { task = "detect", cls = c.truthClass, light = light, rangeBin = RangeBin(rangeSeen), correct = c.detected && c.detectionSeconds <= rules.mediumSeconds, session = s.timestamp });
                // Classify: a call was made on something in view.
                if (c.detected && c.call != "none")
                {
                    float callRange = r != null && r.callRange > 0f ? r.callRange : rangeSeen;
                    string callLight = r != null && r.callLight >= 0 ? ((TimeOfDay)r.callLight).ToString() : light;
                    bool right = c.nodes.Count > 1 && c.nodes[1].passed;
                    list.Add(new LearnerObservation { task = "classify", cls = c.truthClass, light = callLight, rangeBin = RangeBin(callRange), correct = right, session = s.timestamp });
                }
                // Decide: an action was taken, or the clock ran out, or it was correctly left alone.
                if (c.action != "none" || c.clockMiss || c.correctRejection)
                {
                    bool right = !c.clockMiss && (c.correctRejection || (c.nodes.Count > 2 && c.nodes[2].passed));
                    list.Add(new LearnerObservation { task = "decide", cls = c.truthClass, light = light, rangeBin = RangeBin(rangeSeen), correct = right, session = s.timestamp });
                }
            }
            return list;
        }

        static float RangeAt(ContactRecord r, float time)
        {
            if (r.rangeTrack.Count == 0) return r.callRange > 0f ? r.callRange : -1f;
            foreach (var p in r.rangeTrack) if (p.x >= time) return p.y;
            return r.rangeTrack[r.rangeTrack.Count - 1].y;
        }

        // --- Fit.
        static IEnumerable<string> Keys(LearnerObservation o)
        {
            yield return "task:" + o.task;
            yield return "class:" + o.cls;
            yield return "light:" + o.light;
            yield return "range:" + o.rangeBin;
        }

        public static float Logit(LearnerProfile p, string task, string cls, string light, int rangeBin)
        {
            return p.theta + p.Beta("task:" + task) + p.Beta("class:" + cls) + p.Beta("light:" + light) + p.Beta("range:" + rangeBin);
        }

        public static float Predict(LearnerProfile p, string task, string cls, string light, int rangeBin)
        {
            return 1f / (1f + Mathf.Exp(-Logit(p, task, cls, light, rangeBin)));
        }

        // Every factor the model knows about.
        public static List<string> AllKeys()
        {
            var keys = new List<string>();
            foreach (var t in Tasks) keys.Add("task:" + t);
            foreach (var c in Classes) keys.Add("class:" + c);
            foreach (var l in Lights) keys.Add("light:" + l);
            for (int b = 0; b <= RangeEdges.Length; b++) keys.Add("range:" + b);
            return keys;
        }

        // Ridge-regularised logistic regression by gradient descent. Each weight is pulled towards its pooled value
        // (`pooled`, from the other soldiers), ability towards 0. Small data (a few thousand events): a full refit is cheap.
        public static void Fit(LearnerProfile p, Dictionary<string, float> pooled, float shrink = 1f, int iterations = 300, float rate = 0.5f)
        {
            var keys = AllKeys();
            var index = new Dictionary<string, int>();
            for (int i = 0; i < keys.Count; i++) index[keys[i]] = i;
            var beta = new float[keys.Count];
            var prior = new float[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                prior[i] = pooled != null && pooled.TryGetValue(keys[i], out float v) ? v : Prior(keys[i]);
                float old = p.Beta(keys[i]);
                beta[i] = p.weights.Exists(w => w.key == keys[i]) ? old : prior[i];
            }
            float theta = p.theta;
            int n = p.observations.Count;
            var rows = new int[n][];
            var y = new float[n];
            for (int k = 0; k < n; k++)
            {
                var o = p.observations[k];
                var idx = new List<int>(4);
                foreach (var key in Keys(o)) if (index.TryGetValue(key, out int at)) idx.Add(at);
                rows[k] = idx.ToArray();
                y[k] = o.correct ? 1f : 0f;
            }
            var grad = new float[keys.Count];
            for (int it = 0; it < iterations && n > 0; it++)
            {
                Array.Clear(grad, 0, grad.Length);
                float gTheta = 0f;
                for (int k = 0; k < n; k++)
                {
                    float z = theta;
                    foreach (int at in rows[k]) z += beta[at];
                    float err = 1f / (1f + Mathf.Exp(-z)) - y[k];
                    gTheta += err;
                    foreach (int at in rows[k]) grad[at] += err;
                }
                theta -= rate * (gTheta / n + shrink * theta / Mathf.Max(n, 1) * 0.1f);
                for (int i = 0; i < beta.Length; i++) beta[i] -= rate * (grad[i] / n + shrink * (beta[i] - prior[i]) / n);
            }
            p.theta = theta;
            p.weights.Clear();
            for (int i = 0; i < keys.Count; i++)
            {
                var w = new LearnerWeight { key = keys[i], beta = beta[i] };
                foreach (var o in p.observations)
                    foreach (var key in Keys(o))
                        if (key == keys[i]) { w.n++; if (o.correct) w.right++; }
                p.weights.Add(w);
            }
        }

        // Pooled weights: the mean of every other soldier's fitted weights on this PC.
        public static Dictionary<string, float> Pooled(IList<LearnerProfile> others)
        {
            if (others == null || others.Count == 0) return null;
            var sum = new Dictionary<string, float>();
            var count = new Dictionary<string, int>();
            foreach (var o in others)
                foreach (var w in o.weights)
                {
                    sum.TryGetValue(w.key, out float s); sum[w.key] = s + w.beta;
                    count.TryGetValue(w.key, out int c); count[w.key] = c + 1;
                }
            var mean = new Dictionary<string, float>();
            foreach (var kv in sum) mean[kv.Key] = kv.Value / count[kv.Key];
            return mean;
        }

        public static void Add(LearnerProfile p, List<LearnerObservation> obs)
        {
            p.observations.AddRange(obs);
            if (p.observations.Count > MaxObservations) p.observations.RemoveRange(0, p.observations.Count - MaxObservations);
            p.sessions++;
        }

        // --- What to say and what to do next.
        public struct Cell { public string task, cls, light; public int rangeBin; public float p; public int seen; }

        // The weakest recognition cells (classify task), lowest predicted first, among cells the soldier has met at least once.
        public static List<Cell> Weakest(LearnerProfile p, int count)
        {
            var cells = new List<Cell>();
            foreach (var cls in Classes)
                foreach (var light in Lights)
                    for (int b = 0; b <= RangeEdges.Length; b++)
                    {
                        int seen = 0;
                        foreach (var o in p.observations) if (o.task == "classify" && o.cls == cls && o.light == light && o.rangeBin == b) seen++;
                        if (seen == 0) continue;
                        cells.Add(new Cell { task = "classify", cls = cls, light = light, rangeBin = b, p = Predict(p, "classify", cls, light, b), seen = seen });
                    }
            cells.Sort((a, b) => a.p.CompareTo(b.p));
            if (cells.Count > count) cells.RemoveRange(count, cells.Count - count);
            return cells;
        }

        // The next run: the class, light and range whose predicted right-call rate is closest to 75 %, leaning towards the
        // soldier's weak classes. Difficulty moves one level when ability is clearly above or below the target.
        public static LearnerPlan Plan(LearnerProfile p)
        {
            var plan = new LearnerPlan();
            float best = float.MaxValue;
            foreach (var cls in Classes)
                foreach (var light in Lights)
                    for (int b = 0; b <= RangeEdges.Length; b++)
                    {
                        float pr = Predict(p, "classify", cls, light, b);
                        float score = Mathf.Abs(pr - TargetSuccess) + 0.05f * pr;   // ties go to the harder cell
                        if (score < best) { best = score; plan.focusClass = cls; plan.light = (TimeOfDay)Array.IndexOf(Lights, light); plan.rangeBin = b; plan.predicted = pr; }
                    }
            plan.spawnRadius = BinCentre(plan.rangeBin);
            float overall = Predict(p, "classify", "HostileQuad", "Day", 1);
            plan.difficultyStep = overall > 0.9f ? 1 : overall < 0.55f ? -1 : 0;
            plan.text = $"Next run: {Pretty(plan.focusClass)} at {plan.light}, {RangeNames[plan.rangeBin]} (predicted right call {plan.predicted * 100f:F0} %, target 75 %)"
                + (plan.difficultyStep > 0 ? ", one level harder" : plan.difficultyStep < 0 ? ", one level easier" : "") + ".";
            return plan;
        }

        public static float BinCentre(int bin)
        {
            switch (bin)
            {
                case 0: return 400f;     // the ring must sit beyond the bin: contacts are first seen inbound
                case 1: return 700f;
                case 2: return 1000f;
                default: return 1400f;
            }
        }

        // Applies a plan to a scenario: the light, the ring radius, and two more of the focus class.
        public static void Apply(LearnerPlan plan, ScenarioConfig cfg)
        {
            cfg.timeOfDay = plan.light;
            cfg.spawnRingRadius = plan.spawnRadius;
            switch (plan.focusClass)
            {
                case "HostileQuad":
                case "HostileFixedWing": cfg.hostileCount += 2; break;
                case "FriendlyISR": cfg.friendlyCount += 2; break;
                case "CivilianQuad": cfg.civilianCount += 2; break;
                default: cfg.birdCount += 2; break;
            }
            cfg.presetName = "Learner plan";
        }

        public static string Pretty(string cls)
        {
            switch (cls)
            {
                case "HostileQuad": return "hostile quad";
                case "HostileFixedWing": return "hostile fixed-wing";
                case "FriendlyISR": return "own ISR drone";
                case "CivilianQuad": return "civil drone";
                case "Bird": return "bird";
                default: return cls;
            }
        }

        public static string Describe(LearnerProfile p)
        {
            var sb = new StringBuilder();
            sb.Append($"[Learner] {p.player}: {p.sessions} sessions, {p.observations.Count} graded events, ability {p.theta:+0.00;-0.00;0.00}");
            foreach (var c in Weakest(p, 3)) sb.Append($"\n[Learner]   weak: {Pretty(c.cls)}, {c.light}, {RangeNames[c.rangeBin]}: predicted {c.p * 100f:F0} % ({c.seen} seen)");
            sb.Append("\n[Learner] " + Plan(p).text);
            return sb.ToString();
        }

        // --- Storage.
        static string FileFor(string player) => Path.Combine(Folder, Safe(player) + ".json");

        static string Safe(string name)
        {
            var sb = new StringBuilder();
            foreach (char ch in name ?? "player") sb.Append(char.IsLetterOrDigit(ch) ? ch : '_');
            return sb.Length > 0 ? sb.ToString() : "player";
        }

        public static LearnerProfile Load(string player, string folder = null)
        {
            string path = folder != null ? Path.Combine(folder, Safe(player) + ".json") : FileFor(player);
            try
            {
                if (File.Exists(path))
                {
                    var p = JsonUtility.FromJson<LearnerProfile>(File.ReadAllText(path));
                    if (p != null) return p;
                }
            }
            catch (Exception e) { Debug.LogWarning("[Learner] Could not read " + path + ": " + e.Message); }
            return new LearnerProfile { player = player };
        }

        public static void Save(LearnerProfile p, string folder = null)
        {
            try
            {
                string dir = folder ?? Folder;
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, Safe(p.player) + ".json"), JsonUtility.ToJson(p));
            }
            catch (Exception e) { Debug.LogWarning("[Learner] Could not save: " + e.Message); }
        }

        public static List<LearnerProfile> LoadOthers(string player, string folder = null)
        {
            var list = new List<LearnerProfile>();
            string dir = folder ?? Folder;
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                if (Path.GetFileNameWithoutExtension(f) == Safe(player)) continue;
                try { var p = JsonUtility.FromJson<LearnerProfile>(File.ReadAllText(f)); if (p != null) list.Add(p); } catch { }
            }
            return list;
        }

        // The whole step after a run: add the run's events, refit against the pooled cohort, save.
        public static LearnerProfile Update(string player, SessionResult s, IList<ContactRecord> records, RuleSet rules, string folder = null)
        {
            var p = Load(player, folder);
            Add(p, Observe(s, records, rules));
            Fit(p, Pooled(LoadOthers(player, folder)));
            Save(p, folder);
            return p;
        }
    }
}
