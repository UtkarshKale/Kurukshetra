using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Chaukanna.Net
{
    // Solo = every trainee flies the same scenario (same seed) in their own sky; the host starts and ends it and collects
    // the results. Team = one shared sky run by the host: drones, clock and effects (jam, hits) are the host's; trainees
    // send their decisions; each trainee is scored on their own calls and actions.
    public enum NetMode { Solo, Team }

    // Team mode: what a seat may do. Sentry = lock, classify, report (R), challenge IFF; the commander decides (Hold / Jam /
    // Engage) on the reports that arrive. Any = everything, and the only role in Solo mode and on a single PC. Observer = watches
    // and listens, takes no action at all (the host refuses it as well as the PC). Observer is appended: saved rosters keep their numbers.
    public enum SeatRole { Any, Sentry, Commander, Observer }

    [Serializable]
    public class NetPlayer
    {
        public ulong clientId;
        public string name = "";
        public bool ready;
        public bool host;
        public Vector2 post;                 // metres east / north of the terrain centre
        public string state = "";            // in lobby / loading / RUNNING mm:ss / COMPLETE
        public int role;                     // SeatRole as an int (JsonUtility); Team mode only
        public string unit = "Unit 1";       // unit the seat belongs to; the lobby-units feature will set it, until then everyone is Unit 1

        public SeatRole Role => (SeatRole)role;
        public static string RoleName(SeatRole r) => r == SeatRole.Sentry ? "SENTRY" : r == SeatRole.Commander ? "COMMANDER" : r == SeatRole.Observer ? "OBSERVER" : "ANY";
    }

    [Serializable]
    public class NetRoster
    {
        public NetMode mode;
        public List<NetPlayer> players = new List<NetPlayer>();
        public List<string> units = new List<string> { DefaultUnit };     // the trainer's units (sections); every seat belongs to one
        public const string DefaultUnit = "Unit 1";

        // The unit list the lobby shows; never empty.
        public string UnitOrDefault(string unit) => !string.IsNullOrEmpty(unit) && units.Contains(unit) ? unit : (units.Count > 0 ? units[0] : DefaultUnit);

        // Unit editing (host only; LanSession broadcasts after). A name must be non-empty and not already used.
        public bool AddUnit(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || units.Contains(name)) return false;
            units.Add(name);
            return true;
        }

        public bool RenameUnit(string from, string to)
        {
            to = (to ?? "").Trim();
            int i = units.IndexOf(from);
            if (i < 0 || to.Length == 0 || (to != from && units.Contains(to))) return false;
            units[i] = to;
            foreach (var p in players) if (p.unit == from) p.unit = to;
            return true;
        }

        // The last unit cannot be deleted; its seats move to the first remaining unit.
        public bool DeleteUnit(string name)
        {
            if (units.Count <= 1 || !units.Remove(name)) return false;
            foreach (var p in players) if (p.unit == name) p.unit = units[0];
            return true;
        }

        public NetPlayer Find(ulong id) => players.Find(p => p.clientId == id);

        // Team mode with nobody who can jam or fire (every seat a sentry): the Lobby warns about it.
        public bool HasCommander()
        {
            foreach (var p in players) if (p.Role == SeatRole.Commander || p.Role == SeatRole.Any) return true;
            return false;
        }

        // Every trainee who is not the host has pressed Ready (the host's own seat starts when the host presses START).
        public bool AllReady()
        {
            foreach (var p in players) if (!p.host && !p.ready) return false;
            return true;
        }
    }

    // Everything a trainee's PC needs to build the same run. Terrain goes as its settings, not as data: a real-terrain area
    // must be on every PC (same file path) or that PC falls back to procedural terrain from the same seed.
    [Serializable]
    public class StartPacket
    {
        public NetMode mode;
        public string scenarioJson;
        public string terrainJson;
        public int difficulty;
        public List<NetPlayer> players = new List<NetPlayer>();
        public List<string> units = new List<string>();
        public List<Vector2> spawnPoints = new List<Vector2>();
        // A real-terrain run: the area folder's name and its files, so a trainee PC without it can ask the host for it.
        public string areaName = "";
        public string demFile = "";                  // the elevation file's name inside the area folder
        public List<AreaFile> areaFiles = new List<AreaFile>();
    }

    [Serializable]
    public class AreaFile
    {
        public string name;
        public long size;
    }

    // Team mode: a contact appears in the shared sky.
    [Serializable]
    public class SpawnMsg
    {
        public int id, truth, look, seed;
        public bool deceptive;
        public Vector3 position;
        public int crowdIndex = -1;          // dense sky: the crowd agent this contact was promoted from; -1 = a planned contact
    }

    // Team mode: a sentry's report (R) on its way to the commander(s). seat / time are filled in by the host.
    [Serializable]
    public class ReportMsg
    {
        public int id, call;
        public string seat = "";
        public ulong seatId;
        public float time;                   // host scenario time
        public float range, bearing;         // as seen from the reporting seat: metres, degrees from north
    }

    // Dense sky on a team client: "promote this crowd drone for me" (the host owns the sky).
    [Serializable]
    public class CrowdPickMsg
    {
        public int index;
    }

    // Team mode: a teammate's jam or hit took this contact out (so another seat's clock must not run out on it).
    [Serializable]
    public class HandledMsg
    {
        public int id, action;
        public string by = "";
        public float time;                   // host scenario time of the action
    }

    // Team mode: a trainee's decision, sent to the host.
    [Serializable]
    public class DecisionMsg
    {
        public int id, call, action;
    }

    // Team mode: what the host's effector did with it.
    [Serializable]
    public class EffectMsg
    {
        public int id, action;
        public bool locked, hit;
        public string result = "";
    }

    public struct SnapContact
    {
        public int id;
        public Vector3 position;
        public float yaw;
    }

    // One batched snapshot for all contacts (doc §5.6): positions quantised to 16 bits per axis inside an 8 km box around
    // the post (about 12 cm), height -2048..+2048 m (about 6 cm; mountain areas sit well below and above the post), heading in 8 bits.
    public static class Snapshot
    {
        public const float Half = 4096f, Height = 2048f;
        public const int BytesPerContact = 4 + 2 + 2 + 2 + 1;
        public const int MaxPerMessage = 90;     // keeps one message about 1 KB, under the transport's unfragmented size

        public static byte[] Pack(float time, Vector3 origin, IList<SnapContact> list, int start, int count)
        {
            using (var ms = new MemoryStream(16 + count * BytesPerContact))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(time);
                w.Write((ushort)count);
                w.Write(origin.x); w.Write(origin.z);          // the host's frame travels with the data: every PC has its own post, so "origin = my post" shifted the sky
                for (int i = start; i < start + count; i++)
                {
                    var c = list[i];
                    Vector3 d = c.position - origin;
                    w.Write(c.id);
                    w.Write(Q(d.x, -Half, Half));
                    w.Write(Q(d.z, -Half, Half));
                    w.Write(Q(c.position.y, -Height, Height));
                    w.Write((byte)Mathf.RoundToInt(Mathf.Repeat(c.yaw, 360f) / 360f * 255f));
                }
                return ms.ToArray();
            }
        }

        public static float Unpack(byte[] data, Vector3 origin, List<SnapContact> into)
        {
            into.Clear();
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                float time = r.ReadSingle();
                int n = r.ReadUInt16();
                origin = new Vector3(r.ReadSingle(), origin.y, r.ReadSingle());
                for (int i = 0; i < n; i++)
                {
                    int id = r.ReadInt32();
                    float x = D(r.ReadUInt16(), -Half, Half), z = D(r.ReadUInt16(), -Half, Half), y = D(r.ReadUInt16(), -Height, Height);
                    float yaw = r.ReadByte() / 255f * 360f;
                    into.Add(new SnapContact { id = id, position = new Vector3(origin.x + x, y, origin.z + z), yaw = yaw });
                }
                return time;
            }
        }

        static ushort Q(float v, float lo, float hi) => (ushort)Mathf.RoundToInt(Mathf.Clamp01((v - lo) / (hi - lo)) * 65535f);
        static float D(ushort q, float lo, float hi) => lo + q / 65535f * (hi - lo);
    }

    // A big swarm or bird flock (SwarmSky, thousands of agents, no GameObject each): the host sends every agent's position (16 bits
    // per axis in the same 8 km box as Snapshot, about 12 cm) and state (flying / arrived / picked out / gone), in chunks, at a rate
    // that keeps the stream near a fixed budget however big the swarm is. The trainee's copy draws what arrives; it does not simulate.
    public static class SwarmSnapshot
    {
        public const int BytesPerAgent = 7;
        public const int HeaderBytes = 4 + 4 + 4 + 4 + 2 + 4 + 4;
        public const int MaxPerMessage = 140;           // about 1 KB a message
        public const float BudgetBytesPerSecond = 80000f;   // per trainee, per swarm; a bigger swarm is sent less often, not thinned

        // Seconds between two snapshots of a swarm of this many agents (0.2 s up to 3 s).
        public static float Interval(int agents) => Mathf.Clamp(agents * (float)BytesPerAgent / BudgetBytesPerSecond, 0.2f, 3f);

        public static byte[] Pack(int group, float time, Vector3 origin, int total, int start, int count, Unity.Collections.NativeArray<Unity.Mathematics.float3> pos, Unity.Collections.NativeArray<byte> state)
        {
            using (var ms = new MemoryStream(HeaderBytes + count * BytesPerAgent))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(group); w.Write(time); w.Write(total); w.Write(start); w.Write((ushort)count);
                w.Write(origin.x); w.Write(origin.z);
                for (int i = start; i < start + count; i++)
                {
                    var p = pos[i];
                    w.Write(Q(p.x - origin.x, -Snapshot.Half, Snapshot.Half));
                    w.Write(Q(p.z - origin.z, -Snapshot.Half, Snapshot.Half));
                    w.Write(Q(p.y, -Snapshot.Height, Snapshot.Height));
                    w.Write(state[i]);
                }
                return ms.ToArray();
            }
        }

        // Reads a chunk into the scratch arrays (grown as needed); returns false if the bytes are not a valid chunk.
        public static bool Unpack(byte[] data, out int group, out float time, out int total, out int start, out int count, ref Vector3[] pos, ref byte[] state)
        {
            group = total = start = count = 0; time = 0f;
            if (data == null || data.Length < HeaderBytes) return false;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                group = r.ReadInt32(); time = r.ReadSingle(); total = r.ReadInt32(); start = r.ReadInt32(); count = r.ReadUInt16();
                float ox = r.ReadSingle(), oz = r.ReadSingle();
                if (count < 0 || data.Length < HeaderBytes + count * BytesPerAgent || total < 0 || start < 0) return false;
                if (pos == null || pos.Length < count) pos = new Vector3[count];
                if (state == null || state.Length < count) state = new byte[count];
                for (int i = 0; i < count; i++)
                {
                    float x = D(r.ReadUInt16(), -Snapshot.Half, Snapshot.Half), z = D(r.ReadUInt16(), -Snapshot.Half, Snapshot.Half), y = D(r.ReadUInt16(), -Snapshot.Height, Snapshot.Height);
                    pos[i] = new Vector3(ox + x, y, oz + z);
                    state[i] = r.ReadByte();
                }
                return true;
            }
        }

        static ushort Q(float v, float lo, float hi) => (ushort)Mathf.RoundToInt(Mathf.Clamp01((v - lo) / (hi - lo)) * 65535f);
        static float D(ushort q, float lo, float hi) => lo + q / 65535f * (hi - lo);
    }

    // A trainee's PC reports that its field is built and its scenario is running: from then on the host sends it the sky.
    // SwarmPick: a trainee aimed at a drone of a big swarm; the host promotes it to a real contact.
    [Serializable]
    public class SwarmPickMsg
    {
        public int group, index;
    }

    // Message names. Everything goes as NGO named messages: no networked objects, no scene sync, no relay or internet.
    public static class Msg
    {
        public const string Hello = "chk.hello", Roster = "chk.roster", Ready = "chk.ready", Start = "chk.start", End = "chk.end";
        public const string Lobby = "chk.lobby";
        public const string NeedArea = "chk.needarea", AreaChunk = "chk.areachunk";
        public const string Spawn = "chk.spawn", Despawn = "chk.despawn", Snap = "chk.snap", Anomaly = "chk.anomaly";
        public const string Decide = "chk.decide", Effect = "chk.effect", Live = "chk.live", Result = "chk.result";
        public const string Report = "chk.report", CrowdPick = "chk.crowdpick", Handled = "chk.handled";
        public const string Waiting = "chk.waiting", Restart = "chk.restart";
        public const string FieldReady = "chk.fieldready", Swarm = "chk.swarm", SwarmPick = "chk.swarmpick";
        public const string Pose = "chk.pose", Ping = "chk.ping", Pong = "chk.pong", Voice = "chk.voice", Comms = "chk.comms";
    }
}
