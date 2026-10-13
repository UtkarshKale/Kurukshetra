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
    }

    public static class TrendData
    {
        public const int ChartSessions = 10;

        public static TrendPoint FromSession(SessionResult s)
        {
            return new TrendPoint
            {
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
