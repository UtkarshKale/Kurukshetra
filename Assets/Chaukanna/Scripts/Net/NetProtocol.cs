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

    [Serializable]
    public class NetPlayer
    {
        public ulong clientId;
        public string name = "";
        public bool ready;
        public bool host;
        public Vector2 post;                 // metres east / north of the terrain centre
        public string state = "";            // in lobby / loading / RUNNING mm:ss / COMPLETE
    }

    [Serializable]
    public class NetRoster
    {
        public NetMode mode;
        public List<NetPlayer> players = new List<NetPlayer>();

        public NetPlayer Find(ulong id) => players.Find(p => p.clientId == id);

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
    // the post (about 12 cm), height 0-2048 m (about 3 cm), heading in 8 bits.
    public static class Snapshot
    {
        public const float Half = 4096f, Height = 2048f;
        public const int BytesPerContact = 4 + 2 + 2 + 2 + 1;
        public const int MaxPerMessage = 90;     // keeps one message about 1 KB, under the transport's unfragmented size

        public static byte[] Pack(float time, Vector3 origin, IList<SnapContact> list, int start, int count)
        {
            using (var ms = new MemoryStream(8 + count * BytesPerContact))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(time);
                w.Write((ushort)count);
                for (int i = start; i < start + count; i++)
                {
                    var c = list[i];
                    Vector3 d = c.position - origin;
                    w.Write(c.id);
                    w.Write(Q(d.x, -Half, Half));
                    w.Write(Q(d.z, -Half, Half));
                    w.Write(Q(c.position.y, -64f, Height - 64f));
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
                for (int i = 0; i < n; i++)
                {
                    int id = r.ReadInt32();
                    float x = D(r.ReadUInt16(), -Half, Half), z = D(r.ReadUInt16(), -Half, Half), y = D(r.ReadUInt16(), -64f, Height - 64f);
                    float yaw = r.ReadByte() / 255f * 360f;
                    into.Add(new SnapContact { id = id, position = new Vector3(origin.x + x, y, origin.z + z), yaw = yaw });
                }
                return time;
            }
        }

        static ushort Q(float v, float lo, float hi) => (ushort)Mathf.RoundToInt(Mathf.Clamp01((v - lo) / (hi - lo)) * 65535f);
        static float D(ushort q, float lo, float hi) => lo + q / 65535f * (hi - lo);
    }

    // Message names. Everything goes as NGO named messages: no networked objects, no scene sync, no relay or internet.
    public static class Msg
    {
        public const string Hello = "chk.hello", Roster = "chk.roster", Ready = "chk.ready", Start = "chk.start", End = "chk.end";
        public const string Lobby = "chk.lobby";
        public const string NeedArea = "chk.needarea", AreaChunk = "chk.areachunk";
        public const string Spawn = "chk.spawn", Despawn = "chk.despawn", Snap = "chk.snap", Anomaly = "chk.anomaly";
        public const string Decide = "chk.decide", Effect = "chk.effect", Live = "chk.live", Result = "chk.result";
    }
}
