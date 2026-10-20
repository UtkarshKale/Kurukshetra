using System;
using System.Collections.Generic;
using System.IO;
using Chaukanna.Net;
using Chaukanna.Player;
using Chaukanna.Scenario;
using Chaukanna.Scoring;
using UnityEngine;

namespace Chaukanna.Replay
{
    // Samples the running field scene into a BlackBoxData at 10 Hz of scenario time (solo, and the LAN host for every seat), keeps
    // it in memory for the run, and writes <persistentDataPath>/sessions/blackbox_<time>.bin + .json when the run ends.
    // ScoreKeeper creates it and asks it to save when it writes the session file (SessionResult.blackBox).
    public class BlackBoxRecorder : MonoBehaviour
    {
        public static BlackBoxRecorder Instance { get; private set; }
        // The latest finished (or running) run, for the 3D replay. Survives the recorder.
        public static BlackBoxData Latest { get; private set; }
        public static string LatestPath { get; private set; } = "";
        public static string SessionsFolder => Path.Combine(Application.persistentDataPath, "sessions");

        ScenarioSpawner spawner;
        int run = -1, lastTick = -1, lastGroupTick = int.MinValue, evIndex, roeIndex, dirIndex, groupCallIndex;
        BlackBoxData data;
        bool saved;
        ContactSensor sensor;
        readonly Dictionary<int, bool> wasTalking = new Dictionary<int, bool>();
        readonly Dictionary<int, Vector3[]> repPos = new Dictionary<int, Vector3[]>();
        readonly Dictionary<int, bool[]> repAlive = new Dictionary<int, bool[]>();
        public int SamplesTaken { get; private set; }

        void OnEnable() { Instance = this; }
        void OnDisable() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner == null || spawner.Waiting) return;
            try
            {
                if (spawner.RunNumber != run) Begin();
                if (data == null) return;
                if (spawner.Finished) { Save(); return; }
                int tick = Mathf.RoundToInt(spawner.ScenarioTime * BlackBoxData.Hz);
                if (tick > lastTick) { Sample(tick); lastTick = tick; }
            }
            catch (Exception e) { Debug.LogWarning("[BlackBox] " + e.Message); }
        }

        void Begin()
        {
            run = spawner.RunNumber;
            lastTick = -1; lastGroupTick = int.MinValue; evIndex = roeIndex = dirIndex = groupCallIndex = 0; saved = false; SamplesTaken = 0;
            wasTalking.Clear(); repPos.Clear(); repAlive.Clear();
            var lan = LanSession.Instance;
            bool team = lan != null && lan.InField;
            data = new BlackBoxData
            {
                started = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                mode = team ? (lan.IsHost ? "LAN HOST" : "LAN CLIENT") : "SOLO",
                seed = spawner.ActiveSeed,
                origin = spawner.defendedPoint != null ? spawner.defendedPoint.position : spawner.transform.position,
                sectorYaw = spawner.transform.eulerAngles.y,
                spawnRadius = spawner.ActiveRadius,
            };
            Latest = data;
            LatestPath = "";
        }

        int LocalSeat => LanSession.Active ? (int)LanSession.Instance.LocalClientId : 0;

        void Sample(int tick)
        {
            SamplesTaken++;
            if (sensor == null && Camera.main != null) sensor = Camera.main.GetComponent<ContactSensor>();
            int local = LocalSeat;
            var lan = LanSession.Instance;
            bool lanSeats = lan != null && lan.InField && lan.SeatPoses.Count > 0;

            // Seats.
            if (lanSeats)
            {
                foreach (var kv in lan.SeatPoses)
                {
                    int id = (int)kv.Key;
                    var np = lan.roster.Find(kv.Key);
                    data.Seat(id, np != null ? np.name : "seat " + id, np != null ? NetPlayer.RoleName(np.Role) : "");
                    var p = kv.Value;
                    byte flags = (byte)((p.flags & 3) | (p.fov < 20f ? 4 : 0));
                    data.AddSeat(tick, id, p.position, p.yaw, p.pitch, p.fov, flags, id == local && sensor != null && sensor.Locked != null ? sensor.Locked.Data.id : -1);
                    NoteTalk(tick, id, (p.flags & 3) != 0);
                }
            }
            else
            {
                var cam = sensor != null ? sensor.GetComponent<Camera>() : Camera.main;
                if (cam != null)
                {
                    data.Seat(local, ScoreKeeper.PlayerName, "");
                    float fov = Camera.VerticalToHorizontalFieldOfView(cam.fieldOfView, cam.aspect);
                    var e = cam.transform.eulerAngles;
                    data.AddSeat(tick, local, cam.transform.position, e.y, -Mathf.DeltaAngle(0f, e.x), fov, (byte)(fov < 20f ? 4 : 0), sensor != null && sensor.Locked != null ? sensor.Locked.Data.id : -1);
                }
            }

            // Contacts.
            foreach (var c in ContactRegistry.Live)
            {
                if (c == null) continue;
                var d = c.Data;
                bool inView = sensor != null && sensor.Visible.Contains(c);
                int lockSeat = sensor != null && sensor.Locked == c ? local : -1;
                data.AddContact(tick, c.Id, (int)c.TruthClass, (int)d.lookClass, c.transform.position, inView, (int)d.call, (int)d.action, lockSeat);
            }

            // Groups (5 Hz): representative agents, centroid and count.
            // Every second tick or later (not "tick % 2 == 0": a slow frame rate in a big sky could land only on odd ticks and never sample a group).
            if (tick - lastGroupTick >= BlackBoxData.GroupEvery) { SampleGroups(tick); lastGroupTick = tick; }

            Events();
        }

        void NoteTalk(int tick, int seat, bool talking)
        {
            wasTalking.TryGetValue(seat, out bool was);
            if (talking && !was) data.AddEvent(tick, BbEventType.Talk, seat, -1, "talking");
            wasTalking[seat] = talking;
        }

        void SampleGroups(int tick)
        {
            foreach (var sky in SwarmSky.All)
            {
                if (sky == null || !sky.Started) continue;
                int n = Mathf.Min(BlackBoxData.MaxReps, sky.Count);
                if (n <= 0) continue;
                var pos = Rep(sky.GroupId, n, out var alive);
                int stride = Mathf.Max(1, sky.Count / n);
                for (int k = 0; k < n; k++)
                {
                    int i = Mathf.Min(k * stride, sky.Count - 1);
                    // A promoted agent is a real contact now (sampled above, at the same place): not drawn a second time as a group member.
                    alive[k] = sky.Sim != null && i < sky.Sim.Count && sky.Sim.State[i] == SwarmSim.Flying;
                    pos[k] = sky.PositionOf(i);
                }
                Vector3 centroid = Vector3.zero; int cn = 0;
                int cs = Mathf.Max(1, sky.Count / 256);
                for (int i = 0; i < sky.Count; i += cs) if (sky.IsFlying(i)) { centroid += sky.PositionOf(i); cn++; }
                if (cn > 0) centroid /= cn;
                int flying = sky.Sim != null ? sky.Sim.FlyingCount + sky.Sim.PromotedFlying : 0;
                data.AddGroup(tick, sky.GroupId, sky.IsBirds ? 1 : 0, sky.Count, flying, centroid, pos, alive, n);
            }
            var crowd = CrowdSky.Active;
            if (crowd != null && crowd.Count > 0)
            {
                int n = Mathf.Min(BlackBoxData.MaxReps, crowd.Count);
                const int CrowdId = 100000;
                var pos = Rep(CrowdId, n, out var alive);
                int stride = Mathf.Max(1, crowd.Count / n);
                Vector3 centroid = Vector3.zero; int cn = 0;
                for (int k = 0; k < n; k++)
                {
                    int i = Mathf.Min(k * stride, crowd.Count - 1);
                    alive[k] = crowd.IsAlive(i) && !crowd.IsRemoved(i);
                    pos[k] = alive[k] ? crowd.PositionOf(i) : Vector3.zero;
                    if (alive[k]) { centroid += pos[k]; cn++; }
                }
                if (cn > 0) centroid /= cn;
                data.AddGroup(tick, CrowdId, 2, crowd.Count, crowd.AliveCount, centroid, pos, alive, n);
            }
        }

        Vector3[] Rep(int id, int n, out bool[] alive)
        {
            if (!repPos.TryGetValue(id, out var p) || p.Length != n) { p = new Vector3[n]; repPos[id] = p; repAlive[id] = new bool[n]; }
            alive = repAlive[id];
            return p;
        }

        static int Tick(float t) => Mathf.RoundToInt(t * BlackBoxData.Hz);

        void Events()
        {
            int local = LocalSeat;
            var ev = ContactRegistry.Events;
            for (; evIndex < ev.Count; evIndex++)
            {
                var e = ev[evIndex];
                BbEventType type;
                string text = "#" + e.contactId + " " + e.truthClass;
                switch (e.type)
                {
                    case ContactEventType.Spawn: type = BbEventType.Spawn; break;
                    case ContactEventType.Despawn: type = BbEventType.Despawn; text += " " + e.note; break;
                    case ContactEventType.Lock: type = BbEventType.Lock; text = "LOCK #" + e.contactId; break;
                    case ContactEventType.Unlock: type = BbEventType.Unlock; break;
                    case ContactEventType.Classify: type = BbEventType.Call; text = "CALL #" + e.contactId + " " + LatestCall(e.contactId); break;
                    case ContactEventType.Action: type = BbEventType.Action; text = "ACTION #" + e.contactId + " " + LatestAction(e.contactId); break;
                    case ContactEventType.Anomaly: type = BbEventType.Note; text = "ANOMALY #" + e.contactId + " " + e.note; break;
                    default: continue;
                }
                int seat = type == BbEventType.Spawn || type == BbEventType.Despawn ? -1 : local;
                data.AddEvent(Tick(e.time), type, seat, e.contactId, text);
            }
            var roe = ContactRegistry.RoeChanges;
            for (; roeIndex < roe.Count; roeIndex++) data.AddEvent(Tick(roe[roeIndex].time), BbEventType.Card, -1, -1, $"ROE {roe[roeIndex].from} > {roe[roeIndex].to}");
            var dir = Director.Log;
            for (; dirIndex < dir.Count; dirIndex++) data.AddEvent(Tick(dir[dirIndex].time), BbEventType.Director, -1, -1, $"DIRECTOR {dir[dirIndex].kind}: {dir[dirIndex].reason}");
            var gc = GroupCalls.Records;
            for (; groupCallIndex < gc.Count; groupCallIndex++) data.AddEvent(Tick(gc[groupCallIndex].time), BbEventType.GroupCall, local, -1, $"GROUP CALL {gc[groupCallIndex].call} x{gc[groupCallIndex].count} ({(gc[groupCallIndex].correct ? "right" : "wrong")})");
        }

        static string LatestCall(int id)
        {
            foreach (var r in ContactRegistry.Records) if (r.id == id) return r.call.ToString();
            return "";
        }

        static string LatestAction(int id)
        {
            foreach (var r in ContactRegistry.Records) if (r.id == id) return r.action + (string.IsNullOrEmpty(r.actionResult) ? "" : " (" + r.actionResult + ")");
            return "";
        }

        // Called by ScoreKeeper when the run is over; also called by Update. Safe to call twice.
        public static string FinishNow()
        {
            if (Instance == null) return LatestPath;
            Instance.Save();
            return LatestPath;
        }

        void Save()
        {
            if (saved || data == null) return;
            saved = true;
            try
            {
                var vc = VoiceChat.Instance;
                if (vc != null && vc.Recorder != null) vc.FinishRecording();
                data.voiceFolder = vc != null ? vc.LastRecordingFolder : "";
                data.MergeDuplicateSeats();
                float end = spawner != null ? Mathf.Min(spawner.ScenarioTime, spawner.Duration > 0f ? spawner.Duration : spawner.ScenarioTime) : 0f;
                data.Finish(end > 0f ? end : data.EndTime);
                string stem = "blackbox_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
                LatestPath = data.Save(SessionsFolder, stem);
                Debug.Log($"[BlackBox] Saved {LatestPath}: {data.LastSavedBytes / 1024} KB for {data.duration:F0} s ({data.LastSavedBytes / 1024f / Mathf.Max(0.1f, data.duration / 60f):F0} KB/min), {data.seats.Count} seats, {data.contacts.Count} contacts, {data.groups.Count} groups, {data.events.Count} events.");
            }
            catch (Exception e) { Debug.LogWarning("[BlackBox] Could not save: " + e.Message); }
        }
    }
}
