using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // A scenario as the instructor writes it in the editor form (doc §5.2, module 11): only enums, whole numbers and
    // switches, no free text that reaches the world (so no real place or site can be named). The expander turns it into a
    // ScenarioConfig; every float (paths, speeds, timings) comes from the seed, never from the form. The same shape is what
    // an offline LLM builder would fill in later (needs the owner's go-ahead for a local model runtime).
    [Serializable]
    public class ScenarioSpec
    {
        public string name = "New package";      // a label for the list only; never shown in the world
        public int seed = 1001;
        public int durationMinutes = 5;
        public TimeOfDay light = TimeOfDay.Day;
        public Visibility weather = Visibility.Clear;
        public SiteType site = SiteType.None;
        public RoeCard roeStart = RoeCard.CardB;
        public int roeChangeSecond = -1;          // -1 = no change
        public RoeCard roeAfter = RoeCard.CardA;
        public Prevalence prevalence = Prevalence.Custom;
        public int prevalenceTotal = 12;
        public int hostiles = 3, ownDrones = 2, civilDrones = 2, birds = 6;
        public int swarmSize, swarmSecond = 60, birdFlocks;
        public SwarmTactic swarmTactic;
        public int ringMetres = 1000;
        public bool sensorsDegraded;
        public bool quietWatch;
        public int watchMinutes = 45, contactsPerHour = 3;
        public bool director;
        public bool reference;                    // a locked reference package: only these count on the board
        public string fairnessId = "";

        public ScenarioSpec Clone() => (ScenarioSpec)MemberwiseClone();
    }

    [Serializable]
    public class SpecIssue
    {
        public string code;        // V1..V10
        public string message;
        public bool blocking;      // false = a warning the instructor may accept
    }

    public static class SpecValidator
    {
        public const int MaxHostile = 40, MaxObjects = 60;

        // Each rule returns a reason code the instructor sees (doc §5.2). V5 (flight envelopes per class) and V10 (seed
        // family balance) need data we do not have yet; they are reported as not checked.
        public static List<SpecIssue> Validate(ScenarioSpec s)
        {
            var list = new List<SpecIssue>();
            void Add(string code, string msg, bool blocking = true) => list.Add(new SpecIssue { code = code, message = msg, blocking = blocking });

            // V1: bounds.
            if (s.durationMinutes < 1 || s.durationMinutes > 120) Add("V1", $"Length {s.durationMinutes} min is outside 1-120 min");
            foreach (var (n, what) in new[] { (s.hostiles, "hostiles"), (s.ownDrones, "own drones"), (s.civilDrones, "civil drones"), (s.birds, "birds"), (s.swarmSize, "swarm size"), (s.birdFlocks, "bird flocks") })
                if (n < 0 || n > MaxHostile) Add("V1", $"{what} = {n} is outside 0-{MaxHostile}");
            if (s.ringMetres < 200 || s.ringMetres > 5000) Add("V1", $"Ring {s.ringMetres} m is outside 200-5000 m");
            if (s.roeChangeSecond >= s.durationMinutes * 60) Add("V1", $"Card change at {s.roeChangeSecond} s is after the end ({s.durationMinutes * 60} s)");
            if (s.swarmSize > 0 && s.swarmSecond >= s.durationMinutes * 60) Add("V1", $"Swarm at {s.swarmSecond} s is after the end");

            // V2: totals.
            int hostileTotal = s.hostiles + s.swarmSize;
            int objects = s.hostiles + s.ownDrones + s.civilDrones + s.birds + s.swarmSize + s.birdFlocks * 8;
            if (s.prevalence != Prevalence.Custom && !s.quietWatch) objects = s.prevalenceTotal + s.swarmSize + s.birdFlocks * 8;
            if (hostileTotal > MaxHostile) Add("V2", $"Hostiles {hostileTotal} exceed the {MaxHostile} limit");
            if (objects > MaxObjects) Add("V2", $"Total objects {objects} exceed the {MaxObjects} limit");

            // V3: card schedule.
            if (s.roeChangeSecond >= 0 && s.roeAfter == s.roeStart) Add("V3", "Card change to the same card");

            // V4: solvability (simplified: there is no effector model yet). A swarm needs time to be dealt with before the end,
            // and a large swarm at night with degraded sensors leaves no sound play.
            if (s.swarmSize > 0 && s.swarmSecond + 60 > s.durationMinutes * 60) Add("V4", "The run ends less than 60 s after the swarm arrives: no time for a sound response");
            if (s.swarmSize > 24 && s.light == TimeOfDay.Night && s.sensorsDegraded) Add("V4", $"{s.swarmSize}-drone swarm at night with degraded sensors: no sound play exists");

            // V6: some way to detect.
            if (s.light == TimeOfDay.Night && s.sensorsDegraded && s.weather == Visibility.Foggy) Add("V6", "Night, fog and degraded sensors: nothing could detect a drone");

            // V7: a prevalence band that the contact count can actually hold.
            if (s.prevalence != Prevalence.Custom && !s.quietWatch)
            {
                float share = ScenarioConfig.Share(s.prevalence);
                if (s.prevalenceTotal * share < 0.5f) Add("V7", $"{s.prevalence} prevalence with {s.prevalenceTotal} contacts will often have no hostile at all; use at least {Mathf.CeilToInt(0.5f / share)}", false);
            }

            // V8: Quiet watch.
            if (s.quietWatch && s.contactsPerHour > 4) Add("V8", $"A Quiet watch has at most 4 contacts per hour ({s.contactsPerHour} set)");
            if (s.quietWatch && (s.watchMinutes < 20 || s.watchMinutes > 90)) Add("V8", $"A Quiet watch lasts 20-90 min ({s.watchMinutes} set)");

            // V9: fairness for reference packages.
            if (s.reference && s.director) Add("V9", "A reference package must have the Director off");
            if (s.reference && string.IsNullOrEmpty(s.fairnessId)) Add("V9", "A reference package needs a fairness profile id");

            Add("V5", "Flight envelopes per class: not checked yet (no trajectory library)", false);
            Add("V10", "Seed-family balance: not checked yet (needs the learner model over many soldiers)", false);
            return list;
        }

        public static bool Ok(List<SpecIssue> issues) => !issues.Exists(i => i.blocking);
    }

    public static class SpecExpander
    {
        // Spec -> config. Deterministic: same spec, same config.
        public static ScenarioConfig Expand(ScenarioSpec s)
        {
            var c = new ScenarioConfig
            {
                presetName = s.name,
                seed = s.seed,
                duration = s.durationMinutes * 60f,
                hostileCount = s.hostiles,
                friendlyCount = s.ownDrones,
                civilianCount = s.civilDrones,
                birdCount = s.birds,
                swarmSize = s.swarmSize,
                swarmTime = s.swarmSecond,
                swarmTactic = s.swarmTactic,
                birdFlocks = s.birdFlocks,
                spawnRingRadius = s.ringMetres,
                timeOfDay = s.light,
                visibility = s.weather,
                site = s.site,
                roeAtStart = s.roeStart,
                roeFlip = s.roeChangeSecond >= 0,
                roeFlipTime = Mathf.Max(0, s.roeChangeSecond),
                roeFlipTo = s.roeAfter,
                prevalence = s.prevalence,
                prevalenceTotal = s.prevalenceTotal,
                sensorsDegraded = s.sensorsDegraded,
                quietWatch = s.quietWatch,
                watchMinutes = s.watchMinutes,
                contactsPerHour = s.contactsPerHour,
                director = s.director && !s.reference,
                referencePackage = s.reference,
                packageId = s.name,
                fairnessId = s.fairnessId,
            };
            c.spawnWindowStart = 3f;
            c.spawnWindowEnd = c.duration * 0.8f;
            return c.Validate();
        }
    }

    // Packages on disk: <persistentDataPath>/packages/<name>.json.
    public static class PackageStore
    {
        public static string Folder => Path.Combine(Application.persistentDataPath, "packages");

        public static string FileName(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char ch in string.IsNullOrEmpty(name) ? "package" : name) sb.Append(char.IsLetterOrDigit(ch) || ch == '-' ? ch : '_');
            return sb.ToString() + ".json";
        }

        public static void Save(ScenarioSpec s, string folder = null)
        {
            string dir = folder ?? Folder;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, FileName(s.name)), JsonUtility.ToJson(s, true));
        }

        public static List<ScenarioSpec> LoadAll(string folder = null)
        {
            var list = new List<ScenarioSpec>();
            string dir = folder ?? Folder;
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir, "*.json"))
            {
                try { var s = JsonUtility.FromJson<ScenarioSpec>(File.ReadAllText(f)); if (s != null) list.Add(s); }
                catch (Exception e) { Debug.LogWarning("[Packages] Could not read " + f + ": " + e.Message); }
            }
            list.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        public static void Delete(string name, string folder = null)
        {
            string path = Path.Combine(folder ?? Folder, FileName(name));
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
