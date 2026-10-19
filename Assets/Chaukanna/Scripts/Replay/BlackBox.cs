using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Chaukanna.Replay
{
    // The black box of one run: every seat's pose, every contact's position and state, sampled positions of the big groups, and
    // the events, at 10 Hz (groups at 5 Hz). Pure data: no scene access (BlackBoxRecorder fills it, Replay3D plays it back).
    // Positions are snapped to 0.25 m and angles to 16 bit when they are RECORDED, so what is in memory is exactly what the
    // file holds and a save / load round trip is lossless.

    public enum BbEventType : byte { Spawn, Despawn, Lock, Unlock, Call, Action, Card, Director, GroupCall, Talk, Note }

    public struct BbSeatSample { public int tick; public Vector3 pos; public float yaw, pitch, fov; public byte flags; public short lockId; }
    public struct BbContactSample { public int tick; public Vector3 pos; public byte flags, call, action; public sbyte lockSeat; }
    public class BbGroupSample { public int tick; public Vector3 centroid; public int count; public Vector3[] agents; public bool[] alive; }

    public class BbEvent
    {
        public int tick;
        public BbEventType type;
        public short seat = -1;
        public int contact = -1;
        public string text = "";
        public float Time => tick * BlackBoxData.TickSeconds;
    }

    public class BbSeat { public int id; public string name = "", role = ""; public List<BbSeatSample> s = new List<BbSeatSample>(); }
    public class BbContact { public int id; public byte truth, look; public List<BbContactSample> s = new List<BbContactSample>(); }
    public class BbGroup { public int id; public byte kind; public int total, reps; public List<BbGroupSample> s = new List<BbGroupSample>(); }

    [Serializable] public class BbSeatInfo { public int id; public string name = "", role = ""; }

    // The small JSON next to the .bin.
    [Serializable]
    public class BbHeader
    {
        public int version = 2;
        public string started = "", mode = "SOLO", area = "", binFile = "", voiceFolder = "";
        public int seed;
        public float duration, hz = 10f, originX, originY, originZ, sectorYaw, spawnRadius;
        public List<BbSeatInfo> seats = new List<BbSeatInfo>();
        public int contacts, groups, events;
        public long bytes;
        public float bytesPerMinute;
        public string ToJson() => JsonUtility.ToJson(this, true);
    }

    public class BbSeatState
    {
        public int id; public string name = "", role = "";
        public Vector3 pos; public float yaw, pitch, fov; public byte flags; public int lockId;
        public bool Talking => (flags & 3) != 0;
        public bool Binoculars => (flags & 4) != 0;
    }

    public class BbContactState
    {
        public int id; public byte truth, look;
        public Vector3 pos, vel; public bool inView; public byte call, action; public int lockSeat;
    }

    public class BbGroupState
    {
        public int id, kind, total, count, n;
        public Vector3 centroid; public Vector3[] agents = new Vector3[0]; public bool[] alive = new bool[0];
    }

    public class BbState
    {
        public float t;
        public readonly List<BbSeatState> seats = new List<BbSeatState>();
        public readonly List<BbContactState> contacts = new List<BbContactState>();
        public readonly List<BbGroupState> groups = new List<BbGroupState>();
        int sUsed, cUsed, gUsed;
        public void Begin(float time) { t = time; sUsed = cUsed = gUsed = 0; }
        public BbSeatState NextSeat() { if (sUsed == seats.Count) seats.Add(new BbSeatState()); return seats[sUsed++]; }
        public BbContactState NextContact() { if (cUsed == contacts.Count) contacts.Add(new BbContactState()); return contacts[cUsed++]; }
        public BbGroupState NextGroup() { if (gUsed == groups.Count) groups.Add(new BbGroupState()); return groups[gUsed++]; }
        public int SeatCount => sUsed;
        public int ContactCount => cUsed;
        public int GroupCount => gUsed;
        public BbSeatState Seat(int i) => seats[i];
        public BbContactState Contact(int i) => contacts[i];
        public BbGroupState Group(int i) => groups[i];
        public BbSeatState FindSeat(int id) { for (int i = 0; i < sUsed; i++) if (seats[i].id == id) return seats[i]; return null; }
        public BbContactState FindContact(int id) { for (int i = 0; i < cUsed; i++) if (contacts[i].id == id) return contacts[i]; return null; }
    }

    public class BlackBoxData
    {
        public const float Hz = 10f, TickSeconds = 1f / Hz, Quantum = 0.25f;
        public const int GroupEvery = 2;            // groups are sampled every 2nd tick (5 Hz)
        public const int MaxReps = 64;              // representative agents per group
        const string Magic = "KBB2";

        public string started = "", mode = "SOLO", area = "", voiceFolder = "";
        public int seed;
        public float duration, sectorYaw, spawnRadius;
        public Vector3 origin;
        public readonly List<BbSeat> seats = new List<BbSeat>();
        public readonly List<BbContact> contacts = new List<BbContact>();
        public readonly List<BbGroup> groups = new List<BbGroup>();
        public readonly List<BbEvent> events = new List<BbEvent>();
        readonly Dictionary<int, BbSeat> seatById = new Dictionary<int, BbSeat>();
        readonly Dictionary<int, BbContact> contactById = new Dictionary<int, BbContact>();
        readonly Dictionary<int, BbGroup> groupById = new Dictionary<int, BbGroup>();
        public long LastSavedBytes;

        // ---- Recording ----

        public static float SnapPos(float v) => Mathf.Round(v / Quantum) * Quantum;
        public static float SnapYaw(float yaw) => Mathf.Round(Mathf.Repeat(yaw, 360f) / 360f * 65535f) * 360f / 65535f;
        public static float SnapPitch(float p) => Mathf.Round(Mathf.Clamp01((p + 90f) / 180f) * 65535f) / 65535f * 180f - 90f;
        public static float SnapFov(float f) => Mathf.Clamp(Mathf.Round(f * 100f), 0f, 65535f) / 100f;
        Vector3 SnapV(Vector3 w) => new Vector3(SnapPos(w.x - origin.x), SnapPos(w.y - origin.y), SnapPos(w.z - origin.z)) + origin;
        static short Q(float v, float o) => (short)Mathf.Clamp(Mathf.Round((v - o) / Quantum), -32767, 32767);

        public BbSeat Seat(int id, string name = null, string role = null)
        {
            if (!seatById.TryGetValue(id, out var s)) { s = new BbSeat { id = id }; seats.Add(s); seatById[id] = s; }
            if (name != null) s.name = name;
            if (role != null) s.role = role;
            return s;
        }

        public void AddSeat(int tick, int id, Vector3 pos, float yaw, float pitch, float fov, byte flags, int lockId)
        {
            var s = Seat(id);
            if (s.s.Count > 0 && s.s[s.s.Count - 1].tick >= tick) return;
            s.s.Add(new BbSeatSample { tick = tick, pos = SnapV(pos), yaw = SnapYaw(yaw), pitch = SnapPitch(pitch), fov = SnapFov(fov), flags = flags, lockId = (short)Mathf.Clamp(lockId, -1, short.MaxValue) });
        }

        public void AddContact(int tick, int id, int truth, int look, Vector3 pos, bool inView, int call, int action, int lockSeat)
        {
            if (!contactById.TryGetValue(id, out var c)) { c = new BbContact { id = id, truth = (byte)truth, look = (byte)look }; contacts.Add(c); contactById[id] = c; }
            if (c.s.Count > 0 && c.s[c.s.Count - 1].tick >= tick) return;
            c.s.Add(new BbContactSample { tick = tick, pos = SnapV(pos), flags = (byte)(inView ? 1 : 0), call = (byte)call, action = (byte)action, lockSeat = (sbyte)Mathf.Clamp(lockSeat, -1, 127) });
        }

        public void AddGroup(int tick, int id, int kind, int total, int count, Vector3 centroid, Vector3[] agents, bool[] alive, int n)
        {
            if (!groupById.TryGetValue(id, out var g)) { g = new BbGroup { id = id, kind = (byte)kind, total = total, reps = Mathf.Min(n, MaxReps) }; groups.Add(g); groupById[id] = g; }
            if (g.s.Count > 0 && g.s[g.s.Count - 1].tick >= tick) return;
            n = Mathf.Min(n, g.reps);
            var smp = new BbGroupSample { tick = tick, count = count, centroid = SnapV(centroid), agents = new Vector3[g.reps], alive = new bool[g.reps] };
            for (int i = 0; i < n; i++) { smp.agents[i] = SnapV(agents[i]); smp.alive[i] = alive[i]; }
            g.s.Add(smp);
        }

        public void AddEvent(int tick, BbEventType type, int seat, int contact, string text)
        {
            events.Add(new BbEvent { tick = tick, type = type, seat = (short)seat, contact = contact, text = text ?? "" });
        }

        public void Finish(float durationSeconds)
        {
            duration = durationSeconds;
            var idx = new List<KeyValuePair<int, BbEvent>>();
            for (int i = 0; i < events.Count; i++) idx.Add(new KeyValuePair<int, BbEvent>(i, events[i]));
            idx.Sort((a, b) => a.Value.tick != b.Value.tick ? a.Value.tick.CompareTo(b.Value.tick) : a.Key.CompareTo(b.Key));   // stable
            events.Clear();
            foreach (var kv in idx) events.Add(kv.Value);
        }

        // ---- Query ----

        public float EndTime
        {
            get
            {
                int last = 0;
                foreach (var s in seats) if (s.s.Count > 0) last = Mathf.Max(last, s.s[s.s.Count - 1].tick);
                foreach (var c in contacts) if (c.s.Count > 0) last = Mathf.Max(last, c.s[c.s.Count - 1].tick);
                if (events.Count > 0) last = Mathf.Max(last, events[events.Count - 1].tick);
                return Mathf.Max(duration, last * TickSeconds);
            }
        }

        static int FloorIndex<T>(List<T> list, float tickF, Func<T, int> tick)
        {
            int lo = 0, hi = list.Count - 1, ans = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) >> 1;
                if (tick(list[mid]) <= tickF) { ans = mid; lo = mid + 1; } else hi = mid - 1;
            }
            return ans;
        }

        // The whole picture at time t: seats and contacts interpolated between the two samples around t, groups likewise.
        public void StateAt(float t, BbState st)
        {
            st.Begin(t);
            float tf = t * Hz;
            foreach (var s in seats)
            {
                if (s.s.Count == 0) continue;
                int i = FloorIndex(s.s, tf, x => x.tick);
                var o = st.NextSeat();
                o.id = s.id; o.name = s.name; o.role = s.role;
                var a = s.s[Mathf.Max(i, 0)];
                var b = i + 1 < s.s.Count && i >= 0 ? s.s[i + 1] : a;
                float k = i >= 0 && b.tick > a.tick && b.tick - a.tick <= 20 ? Mathf.Clamp01((tf - a.tick) / (b.tick - a.tick)) : 0f;
                o.pos = Vector3.Lerp(a.pos, b.pos, k);
                o.yaw = Mathf.LerpAngle(a.yaw, b.yaw, k);
                o.pitch = Mathf.Lerp(a.pitch, b.pitch, k);
                o.fov = Mathf.Lerp(a.fov, b.fov, k);
                o.flags = a.flags; o.lockId = a.lockId;
            }
            foreach (var c in contacts)
            {
                if (c.s.Count == 0) continue;
                int i = FloorIndex(c.s, tf, x => x.tick);
                if (i < 0) continue;
                var a = c.s[i];
                if (i == c.s.Count - 1 && tf > a.tick + 1.01f) continue;      // gone (one tick of grace after the last sample)
                var b = i + 1 < c.s.Count ? c.s[i + 1] : a;
                float k = b.tick > a.tick && b.tick - a.tick <= 5 ? Mathf.Clamp01((tf - a.tick) / (b.tick - a.tick)) : 0f;
                var o = st.NextContact();
                o.id = c.id; o.truth = c.truth; o.look = c.look;
                o.pos = Vector3.Lerp(a.pos, b.pos, k);
                o.vel = b.tick > a.tick ? (b.pos - a.pos) / ((b.tick - a.tick) * TickSeconds)
                    : (i > 0 ? (a.pos - c.s[i - 1].pos) / Mathf.Max(TickSeconds, (a.tick - c.s[i - 1].tick) * TickSeconds) : Vector3.zero);
                o.inView = (a.flags & 1) != 0; o.call = a.call; o.action = a.action; o.lockSeat = a.lockSeat;
            }
            foreach (var g in groups)
            {
                if (g.s.Count == 0) continue;
                int i = FloorIndex(g.s, tf, x => x.tick);
                if (i < 0) continue;
                var a = g.s[i];
                if (i == g.s.Count - 1 && tf > a.tick + GroupEvery + 0.01f) continue;
                var b = i + 1 < g.s.Count ? g.s[i + 1] : a;
                float k = b.tick > a.tick && b.tick - a.tick <= 8 ? Mathf.Clamp01((tf - a.tick) / (b.tick - a.tick)) : 0f;
                var o = st.NextGroup();
                o.id = g.id; o.kind = g.kind; o.total = g.total; o.count = a.count; o.n = g.reps;
                o.centroid = Vector3.Lerp(a.centroid, b.centroid, k);
                if (o.agents.Length != g.reps) { o.agents = new Vector3[g.reps]; o.alive = new bool[g.reps]; }
                for (int r = 0; r < g.reps; r++)
                {
                    o.alive[r] = a.alive[r];
                    o.agents[r] = a.alive[r] && b.alive[r] ? Vector3.Lerp(a.agents[r], b.agents[r], k) : a.agents[r];
                }
            }
        }

        public static bool IsMajor(BbEventType t) => t != BbEventType.Spawn && t != BbEventType.Despawn && t != BbEventType.Unlock;

        // Time of the next / previous major event strictly after / before t (seconds). -1 = none.
        public float NextEventTime(float t)
        {
            foreach (var e in events) if (IsMajor(e.type) && e.Time > t + 0.05f) return e.Time;
            return -1f;
        }

        public float PrevEventTime(float t)
        {
            for (int i = events.Count - 1; i >= 0; i--) if (IsMajor(events[i].type) && events[i].Time < t - 0.15f) return events[i].Time;
            return -1f;
        }

        public void EventsIn(float t0, float t1, List<BbEvent> into)
        {
            into.Clear();
            foreach (var e in events) { if (e.Time > t1) break; if (e.Time >= t0) into.Add(e); }
        }

        public bool EventsAreSorted()
        {
            for (int i = 1; i < events.Count; i++) if (events[i].tick < events[i - 1].tick) return false;
            return true;
        }

        // ---- Binary file ----

        static void WriteDelta(BinaryWriter w, ref int prev, int tick, bool first)
        {
            int d = tick - prev;
            if (first || d < 1 || d > 255) { w.Write((byte)0); w.Write(tick); }
            else w.Write((byte)d);
            prev = tick;
        }

        static int ReadDelta(BinaryReader r, ref int prev)
        {
            int d = r.ReadByte();
            prev = d == 0 ? r.ReadInt32() : prev + d;
            return prev;
        }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream(1 << 16))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Magic.ToCharArray());
                w.Write(started ?? ""); w.Write(mode ?? ""); w.Write(area ?? ""); w.Write(voiceFolder ?? "");
                w.Write(seed); w.Write(duration); w.Write(sectorYaw); w.Write(spawnRadius);
                w.Write(origin.x); w.Write(origin.y); w.Write(origin.z);
                w.Write(seats.Count);
                foreach (var s in seats)
                {
                    w.Write(s.id); w.Write(s.name ?? ""); w.Write(s.role ?? ""); w.Write(s.s.Count);
                    int prev = 0;
                    for (int i = 0; i < s.s.Count; i++)
                    {
                        var p = s.s[i];
                        WriteDelta(w, ref prev, p.tick, i == 0);
                        w.Write(Q(p.pos.x, origin.x)); w.Write(Q(p.pos.y, origin.y)); w.Write(Q(p.pos.z, origin.z));
                        w.Write((ushort)Mathf.Round(Mathf.Repeat(p.yaw, 360f) / 360f * 65535f));
                        w.Write((ushort)Mathf.Round(Mathf.Clamp01((p.pitch + 90f) / 180f) * 65535f));
                        w.Write((ushort)Mathf.Round(p.fov * 100f));
                        w.Write(p.flags); w.Write(p.lockId);
                    }
                }
                w.Write(contacts.Count);
                foreach (var c in contacts)
                {
                    w.Write(c.id); w.Write(c.truth); w.Write(c.look); w.Write(c.s.Count);
                    int prev = 0;
                    for (int i = 0; i < c.s.Count; i++)
                    {
                        var p = c.s[i];
                        WriteDelta(w, ref prev, p.tick, i == 0);
                        w.Write(Q(p.pos.x, origin.x)); w.Write(Q(p.pos.y, origin.y)); w.Write(Q(p.pos.z, origin.z));
                        w.Write(p.flags); w.Write(p.call); w.Write(p.action); w.Write(p.lockSeat);
                    }
                }
                w.Write(groups.Count);
                foreach (var g in groups)
                {
                    w.Write(g.id); w.Write(g.kind); w.Write(g.total); w.Write((byte)g.reps); w.Write(g.s.Count);
                    int prev = 0;
                    for (int i = 0; i < g.s.Count; i++)
                    {
                        var p = g.s[i];
                        WriteDelta(w, ref prev, p.tick, i == 0);
                        w.Write(p.count);
                        w.Write(Q(p.centroid.x, origin.x)); w.Write(Q(p.centroid.y, origin.y)); w.Write(Q(p.centroid.z, origin.z));
                        for (int r = 0; r < g.reps; r++)
                        {
                            w.Write(Q(p.agents[r].x, origin.x));
                            w.Write(p.alive[r] ? Q(p.agents[r].y, origin.y) : short.MinValue);
                            w.Write(Q(p.agents[r].z, origin.z));
                        }
                    }
                }
                w.Write(events.Count);
                foreach (var e in events) { w.Write(e.tick); w.Write((byte)e.type); w.Write(e.seat); w.Write(e.contact); w.Write(e.text ?? ""); }
                w.Flush();
                return ms.ToArray();
            }
        }

        public static BlackBoxData FromBytes(byte[] bytes)
        {
            var d = new BlackBoxData();
            using (var ms = new MemoryStream(bytes))
            using (var r = new BinaryReader(ms))
            {
                if (new string(r.ReadChars(4)) != Magic) throw new InvalidDataException("Not a black box file.");
                d.started = r.ReadString(); d.mode = r.ReadString(); d.area = r.ReadString(); d.voiceFolder = r.ReadString();
                d.seed = r.ReadInt32(); d.duration = r.ReadSingle(); d.sectorYaw = r.ReadSingle(); d.spawnRadius = r.ReadSingle();
                d.origin = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                Vector3 o = d.origin;
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var s = d.Seat(r.ReadInt32(), r.ReadString(), r.ReadString());
                    int cnt = r.ReadInt32(), prev = 0;
                    for (int k = 0; k < cnt; k++)
                    {
                        int tick = ReadDelta(r, ref prev);
                        var pos = new Vector3(r.ReadInt16() * Quantum + o.x, r.ReadInt16() * Quantum + o.y, r.ReadInt16() * Quantum + o.z);
                        float yaw = r.ReadUInt16() * 360f / 65535f, pitch = r.ReadUInt16() / 65535f * 180f - 90f, fov = r.ReadUInt16() / 100f;
                        byte fl = r.ReadByte(); short lk = r.ReadInt16();
                        s.s.Add(new BbSeatSample { tick = tick, pos = pos, yaw = yaw, pitch = pitch, fov = fov, flags = fl, lockId = lk });
                    }
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var c = new BbContact { id = r.ReadInt32(), truth = r.ReadByte(), look = r.ReadByte() };
                    d.contacts.Add(c); d.contactById[c.id] = c;
                    int cnt = r.ReadInt32(), prev = 0;
                    for (int k = 0; k < cnt; k++)
                    {
                        int tick = ReadDelta(r, ref prev);
                        var pos = new Vector3(r.ReadInt16() * Quantum + o.x, r.ReadInt16() * Quantum + o.y, r.ReadInt16() * Quantum + o.z);
                        c.s.Add(new BbContactSample { tick = tick, pos = pos, flags = r.ReadByte(), call = r.ReadByte(), action = r.ReadByte(), lockSeat = r.ReadSByte() });
                    }
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    var g = new BbGroup { id = r.ReadInt32(), kind = r.ReadByte(), total = r.ReadInt32(), reps = r.ReadByte() };
                    d.groups.Add(g); d.groupById[g.id] = g;
                    int cnt = r.ReadInt32(), prev = 0;
                    for (int k = 0; k < cnt; k++)
                    {
                        var p = new BbGroupSample { tick = ReadDelta(r, ref prev), count = r.ReadInt32(), agents = new Vector3[g.reps], alive = new bool[g.reps] };
                        p.centroid = new Vector3(r.ReadInt16() * Quantum + o.x, r.ReadInt16() * Quantum + o.y, r.ReadInt16() * Quantum + o.z);
                        for (int a = 0; a < g.reps; a++)
                        {
                            short x = r.ReadInt16(), y = r.ReadInt16(), z = r.ReadInt16();
                            p.alive[a] = y != short.MinValue;
                            p.agents[a] = new Vector3(x * Quantum + o.x, (p.alive[a] ? y : 0) * Quantum + o.y, z * Quantum + o.z);
                        }
                        g.s.Add(p);
                    }
                }
                n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                    d.events.Add(new BbEvent { tick = r.ReadInt32(), type = (BbEventType)r.ReadByte(), seat = r.ReadInt16(), contact = r.ReadInt32(), text = r.ReadString() });
            }
            return d;
        }

        public BbHeader MakeHeader(string binFile, long bytes)
        {
            var h = new BbHeader
            {
                started = started, mode = mode, area = area, binFile = binFile, voiceFolder = voiceFolder, seed = seed, duration = duration,
                originX = origin.x, originY = origin.y, originZ = origin.z, sectorYaw = sectorYaw, spawnRadius = spawnRadius,
                contacts = contacts.Count, groups = groups.Count, events = events.Count, bytes = bytes,
                bytesPerMinute = duration > 1f ? bytes / (duration / 60f) : bytes,
            };
            foreach (var s in seats) h.seats.Add(new BbSeatInfo { id = s.id, name = s.name, role = s.role });
            return h;
        }

        // Writes <folder>/<stem>.bin and <stem>.json; returns the .bin path.
        public string Save(string folder, string stem)
        {
            Directory.CreateDirectory(folder);
            byte[] bytes = ToBytes();
            string bin = Path.Combine(folder, stem + ".bin");
            File.WriteAllBytes(bin, bytes);
            LastSavedBytes = bytes.Length;
            File.WriteAllText(Path.Combine(folder, stem + ".json"), MakeHeader(stem + ".bin", bytes.Length).ToJson());
            return bin;
        }

        public static BlackBoxData Load(string binPath) => FromBytes(File.ReadAllBytes(binPath));
    }
}
