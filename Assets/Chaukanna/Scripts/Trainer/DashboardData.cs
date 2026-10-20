using System;
using System.Collections.Generic;
using System.IO;
using Chaukanna.Scenario;
using Chaukanna.Scoring;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // ---------------------------------------------------------------------------------------------
    // Data sources for the score dashboard.
    //
    // The dashboard only ever talks to these two interfaces, so the drawing code does not change when
    // real data arrives:
    //   IPlayerSource     who is playing and each player's current result and saved sessions.
    //                     Today: LocalPlayerSource, one entry ("Player 1") fed by the live ScoreKeeper.
    //                     Later: a multiplayer / LAN source returning one entry per connected trainee.
    //   IUnitStatsSource  the unit-level table. Today: SampleUnitStatsSource, generated sample data.
    //                     Later: a source built from every player's saved sessions.
    // To swap one in, assign it to ScoreDashboard.Players / ScoreDashboard.Unit.
    // ---------------------------------------------------------------------------------------------

    public class PlayerEntry
    {
        public string name;
        public string state;            // "RUNNING 01:23", "COMPLETE", "IDLE"
        public string role = "";        // LAN team run: "SENTRY" / "COMMANDER" / "ANY"; empty otherwise
        public SessionResult current;   // may be null
        public List<TrendPoint> history = new List<TrendPoint>();   // oldest first, real sessions only
    }

    public interface IPlayerSource
    {
        int Count { get; }
        PlayerEntry Get(int index);
    }

    public class UnitSoldier
    {
        public string name;
        public float latestScore;       // percent
        public float previousScore;     // percent
        public string weakestArea;
    }

    public interface IUnitStatsSource
    {
        string Title { get; }
        bool IsSample { get; }
        IList<UnitSoldier> Soldiers { get; }
    }

    // One session on a trend chart.
    public struct TrendPoint
    {
        public float detectionSeconds;
        public float classificationAccuracy;
        public float engagementScore;
        public float scorePercent;
        public bool sample;             // generated filler, never a real session
        public string label;            // when the session was played (shown in chart tooltips); may be empty
        public bool noData;             // the session had no scored contact: no score, no accuracy
        public bool noDetection;        // nothing was brought into view: no average detection time
    }

    public static class TrendData
    {
        public const int ChartSessions = 10;

        public static TrendPoint FromSession(SessionResult s)
        {
            return new TrendPoint
            {
                label = s.timestamp ?? "",
                noData = s.maxPoints <= 0,
                noDetection = s.averageDetectionSeconds < 0f,
                detectionSeconds = Mathf.Max(0f, s.averageDetectionSeconds),
                classificationAccuracy = s.classificationAccuracy,
                engagementScore = s.engagementDecisionScore,
                scorePercent = s.maxPoints > 0 ? 100f * s.totalPoints / s.maxPoints : 0f,
            };
        }

        // Newest `max` saved sessions, oldest first. Unreadable files are skipped.
        public static List<TrendPoint> LoadSessions(string folder, int max)
        {
            var points = new List<TrendPoint>();
            try
            {
                if (!Directory.Exists(folder)) return points;
                var files = Directory.GetFiles(folder, "session_*.json");
                Array.Sort(files, StringComparer.Ordinal);   // names start with the timestamp
                for (int i = Mathf.Max(0, files.Length - max); i < files.Length; i++)
                {
                    try { points.Add(FromSession(JsonUtility.FromJson<SessionResult>(File.ReadAllText(files[i])))); }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
            return points;
        }

        // Always returns `total` points: generated sample sessions on the left (flagged sample), then the real ones.
        // The samples are deterministic and show a gentle improvement, so the chart reads as a trend.
        public static List<TrendPoint> PadWithSamples(List<TrendPoint> real, int total, int seed = 7)
        {
            var result = new List<TrendPoint>(total);
            int realCount = Mathf.Min(real.Count, total);
            int samples = total - realCount;
            var rng = new System.Random(seed);
            for (int i = 0; i < samples; i++)
            {
                float t = total > 1 ? i / (float)(total - 1) : 0f;
                result.Add(new TrendPoint
                {
                    sample = true,
                    detectionSeconds = Mathf.Lerp(14f, 7f, t) + Jitter(rng, 1.5f),
                    classificationAccuracy = Mathf.Clamp(Mathf.Lerp(45f, 75f, t) + Jitter(rng, 6f), 0f, 100f),
                    engagementScore = Mathf.Clamp(Mathf.Lerp(40f, 80f, t) + Jitter(rng, 8f), 0f, 100f),
                    scorePercent = Mathf.Clamp(Mathf.Lerp(35f, 70f, t) + Jitter(rng, 6f), 0f, 100f),
                });
            }
            for (int i = real.Count - realCount; i < real.Count; i++) result.Add(real[i]);
            return result;
        }

        static float Jitter(System.Random rng, float amount)
        {
            return ((float)rng.NextDouble() * 2f - 1f) * amount;
        }

        // +1 improving, -1 getting worse, 0 flat (inside the dead band).
        public static int Trend(float previous, float latest, bool higherIsBetter = true, float deadBand = 2f)
        {
            float change = latest - previous;
            if (Mathf.Abs(change) < deadBand) return 0;
            return (change > 0f) == higherIsBetter ? 1 : -1;
        }

        public static string TrendWord(int trend)
        {
            return trend > 0 ? "UP" : trend < 0 ? "DOWN" : "FLAT";
        }
    }

    // What the optional XCharts panel is given, as JSON. Field names must match XChartsPanel.Payload in the Charts folder.
    [Serializable]
    public class ChartPayload
    {
        [Serializable] public class Named { public string name; public float value; }
        [Serializable] public class SessionPoint { public string label; public float scorePercent, detectionSeconds, classificationAccuracy, engagementScore; public bool sample; }
        [Serializable] public class ClassRow { public string name; public int right, wrong, noCall; }

        public string playerName = "";
        public List<SessionPoint> sessions = new List<SessionPoint>();
        public List<Named> counts = new List<Named>();
        public List<ClassRow> classes = new List<ClassRow>();
        public List<Named> detectionBins = new List<Named>();
        public List<Named> nodePoints = new List<Named>();

        // trend = the padded list the dashboard already shows (sample points first, then saved sessions).
        public static ChartPayload Build(string playerName, SessionResult s, List<TrendPoint> trend)
        {
            var p = new ChartPayload { playerName = playerName ?? "" };
            if (trend != null)
                for (int i = 0; i < trend.Count; i++)
                    p.sessions.Add(new SessionPoint { label = (i + 1) + (trend[i].sample ? "*" : ""), scorePercent = trend[i].scorePercent, detectionSeconds = trend[i].detectionSeconds, classificationAccuracy = trend[i].classificationAccuracy, engagementScore = trend[i].engagementScore, sample = trend[i].sample });
            if (s == null) return p;

            Add(p.counts, "Missed", s.missed); Add(p.counts, "Unresolved", s.unresolved); Add(p.counts, "False engage", s.falseEngagements);
            Add(p.counts, "Missed hostile", s.missedHostiles); Add(p.counts, "Wrong AI followed", s.followedWrongHint);

            // Calls per true class, from the confusion grid: the diagonal is right, the last column is "no call".
            int cols = s.confusionColumns != null ? s.confusionColumns.Length : 0;
            for (int row = 0; s.confusionRows != null && row < s.confusionRows.Length; row++)
            {
                var r = new ClassRow { name = s.confusionRows[row] };
                for (int col = 0; col < cols; col++)
                {
                    int n = s.confusion[row * cols + col];
                    if (col == cols - 1) r.noCall += n; else if (col == row) r.right += n; else r.wrong += n;
                }
                p.classes.Add(r);
            }

            // How long each scored contact took to be seen.
            int[] bins = new int[5];
            float points0 = 0f, points1 = 0f, points2 = 0f;
            foreach (var c in s.contacts)
            {
                if (!c.scored) continue;
                if (!c.detected) bins[4]++;
                else if (c.detectionSeconds < 5f) bins[0]++;
                else if (c.detectionSeconds < 10f) bins[1]++;
                else if (c.detectionSeconds < 20f) bins[2]++;
                else bins[3]++;
                if (c.nodes.Count > 0) points0 += c.nodes[0].points;
                if (c.nodes.Count > 1) points1 += c.nodes[1].points;
                if (c.nodes.Count > 2) points2 += c.nodes[2].points;
            }
            string[] names = { "under 5 s", "5-10 s", "10-20 s", "over 20 s", "never" };
            for (int i = 0; i < bins.Length; i++) Add(p.detectionBins, names[i], bins[i]);
            Add(p.nodePoints, "Detect", points0); Add(p.nodePoints, "Classify", points1); Add(p.nodePoints, "Action", points2);
            return p;
        }

        static void Add(List<Named> list, string name, float value)
        {
            list.Add(new Named { name = name, value = value });
        }
    }

    // The one live player on this machine.
    public class LocalPlayerSource : IPlayerSource
    {
        readonly ScoreKeeper keeper;
        readonly ScenarioSpawner spawner;
        readonly PlayerEntry entry = new PlayerEntry { name = "Player 1" };
        int loadedFor = -1;

        public LocalPlayerSource(ScoreKeeper keeper, ScenarioSpawner spawner)
        {
            this.keeper = keeper;
            this.spawner = spawner;
        }

        public int Count => 1;

        public PlayerEntry Get(int index)
        {
            entry.current = keeper != null ? keeper.Current() : null;
            if (spawner == null) entry.state = "IDLE";
            else if (spawner.Finished) entry.state = "COMPLETE";
            else
            {
                int t = (int)spawner.ScenarioTime;
                entry.state = $"RUNNING {t / 60:00}:{t % 60:00}";
            }
            // Re-read the saved sessions when a new one has been written.
            int saved = keeper != null ? keeper.SavedCount : 0;
            if (loadedFor != saved)
            {
                loadedFor = saved;
                entry.history = TrendData.LoadSessions(ScoreKeeper.SessionsFolder, TrendData.ChartSessions);
            }
            return entry;
        }
    }

    // Placeholder unit table. Everything here is made up and is labelled as sample data on screen.
    public class SampleUnitStatsSource : IUnitStatsSource
    {
        readonly List<UnitSoldier> soldiers = new List<UnitSoldier>
        {
            new UnitSoldier { name = "Soldier A", latestScore = 78f, previousScore = 70f, weakestArea = "night FPV" },
            new UnitSoldier { name = "Soldier B", latestScore = 64f, previousScore = 66f, weakestArea = "bird vs quad at range" },
            new UnitSoldier { name = "Soldier C", latestScore = 83f, previousScore = 74f, weakestArea = "ROE under attack" },
            new UnitSoldier { name = "Soldier D", latestScore = 51f, previousScore = 58f, weakestArea = "slow detection" },
            new UnitSoldier { name = "Soldier E", latestScore = 70f, previousScore = 69f, weakestArea = "over-trusts the AI hint" },
            new UnitSoldier { name = "Soldier F", latestScore = 59f, previousScore = 49f, weakestArea = "fixed-wing identification" },
        };

        public string Title => "UNIT (placeholder, sample data)";
        public bool IsSample => true;
        public IList<UnitSoldier> Soldiers => soldiers;
    }
}
