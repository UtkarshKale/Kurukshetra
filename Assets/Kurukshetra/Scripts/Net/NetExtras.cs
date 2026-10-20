using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace Kurukshetra.Net
{
    // One seat's state at 10 Hz: where it stands, where it looks, how far it is zoomed, and whether it is talking.
    // On the wire x / z are metres from the run's origin (the defended point) like the snapshot, y is absolute.
    public struct PoseMsg
    {
        public ushort seat;
        public float time;                   // host scenario time (the sender's clock mapped to the host's)
        public Vector3 rel;
        public float yaw, pitch, fov;
        public byte flags;                   // bit 0: talking on voice, bit 1: talking on radio, bit 2: binoculars up

        public const int Bytes = 2 + 4 + 12 + 2 + 2 + 2 + 1;
        public bool Voice => (flags & 1) != 0;
        public bool Radio => (flags & 2) != 0;
        public bool Binoculars => (flags & 4) != 0;
    }

    // A seat's pose in world coordinates on this PC.
    public class SeatPose
    {
        public Vector3 position;
        public float yaw, pitch, fov;
        public byte flags;
        public float time;
        public float receivedAt;
    }

    public static class PoseCodec
    {
        public static byte[] Pack(PoseMsg m)
        {
            using (var ms = new MemoryStream(PoseMsg.Bytes))
            using (var w = new BinaryWriter(ms))
            {
                w.Write(m.seat);
                w.Write(m.time);
                w.Write(m.rel.x); w.Write(m.rel.y); w.Write(m.rel.z);
                w.Write((ushort)Mathf.RoundToInt(Mathf.Repeat(m.yaw, 360f) / 360f * 65535f));
                w.Write((ushort)Mathf.RoundToInt(Mathf.Clamp01((m.pitch + 90f) / 180f) * 65535f));
                w.Write((ushort)Mathf.Clamp(Mathf.RoundToInt(m.fov * 100f), 0, 65535));
                w.Write(m.flags);
                return ms.ToArray();
            }
        }

        public static bool TryUnpack(byte[] data, out PoseMsg m)
        {
            m = default;
            if (data == null || data.Length < PoseMsg.Bytes) return false;
            using (var ms = new MemoryStream(data))
            using (var r = new BinaryReader(ms))
            {
                m.seat = r.ReadUInt16();
                m.time = r.ReadSingle();
                m.rel = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                m.yaw = r.ReadUInt16() / 65535f * 360f;
                m.pitch = r.ReadUInt16() / 65535f * 180f - 90f;
                m.fov = r.ReadUInt16() / 100f;
                m.flags = r.ReadByte();
                return true;
            }
        }
    }

    // Trainee clock vs the host's scenario clock from ping / pong round trips: each sample says "the host read H when my clock was
    // about (t0 + t1) / 2", so offset = H - mid. The median of the last few samples ignores a stray slow reply.
    public class ClockSync
    {
        readonly List<float> offsets = new List<float>();
        public const int Keep = 5;
        public bool Valid { get; private set; }
        public float Offset { get; private set; }
        public float LastRtt { get; private set; }

        public void Reset() { offsets.Clear(); Valid = false; Offset = 0f; LastRtt = 0f; }

        public void AddSample(float t0, float t1, float hostTime)
        {
            float rtt = Mathf.Max(0f, t1 - t0);
            LastRtt = rtt;
            offsets.Add(hostTime - (t0 + t1) * 0.5f);
            if (offsets.Count > Keep) offsets.RemoveAt(0);
            Offset = Median(offsets);
            Valid = true;
        }

        public float HostNow(float localNow) => localNow + Offset;

        public static float Median(List<float> v)
        {
            if (v.Count == 0) return 0f;
            var s = new List<float>(v);
            s.Sort();
            int n = s.Count;
            return n % 2 == 1 ? s[n / 2] : 0.5f * (s[n / 2 - 1] + s[n / 2]);
        }
    }

    // A host the trainee's Lobby found on the network.
    public class FoundHost
    {
        public string name = "", address = "", state = "";
        public ushort port;
        public NetMode mode;
        public float seenAt;
    }

    // LAN discovery over UDP broadcast (no package, no internet): a trainee broadcasts "KRK1?" on port 47777; a host that is
    // listening answers the sender directly with "KRK1!|name|port|mode|state". The trainee lists what answered; typing an IP
    // still works as the fallback (a router may block broadcasts between Wi-Fi and wired segments).
    public class LanDiscovery
    {
        public const int DiscoveryPort = 47777;
        public const string Request = "KRK1?", ReplyTag = "KRK1!";
        public readonly List<FoundHost> Found = new List<FoundHost>();
        public Func<string> DescribeHost;              // host: "name|port|mode|state"

        UdpClient responder, scanner;
        float nextScan;

        public static byte[] MakeReply(string name, ushort port, NetMode mode, string state)
            => Encoding.UTF8.GetBytes($"{ReplyTag}|{name.Replace('|', '/')}|{port}|{(int)mode}|{state.Replace('|', '/')}");

        public static bool IsRequest(byte[] data) => data != null && Encoding.UTF8.GetString(data) == Request;

        public static bool TryParseReply(byte[] data, string fromAddress, out FoundHost host)
        {
            host = null;
            if (data == null || data.Length < 8 || data.Length > 512) return false;
            var parts = Encoding.UTF8.GetString(data).Split('|');
            if (parts.Length < 5 || parts[0] != ReplyTag || !ushort.TryParse(parts[2], out ushort port) || !int.TryParse(parts[3], out int mode)) return false;
            host = new FoundHost { name = parts[1], address = fromAddress, port = port, mode = (NetMode)mode, state = parts[4] };
            return true;
        }

        public bool Responding => responder != null;

        public void StartResponder()
        {
            if (responder != null) return;
            try
            {
                responder = new UdpClient(AddressFamily.InterNetwork);
                responder.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                responder.Client.Bind(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            }
            catch (Exception e) { Debug.LogWarning("[LAN] Discovery responder could not start (trainees can still type the IP): " + e.Message); StopResponder(); }
        }

        public void StopResponder() { try { responder?.Close(); } catch { } responder = null; }

        public void StopScanner() { try { scanner?.Close(); } catch { } scanner = null; Found.Clear(); }

        // Call every frame. `scanning` = the Lobby wants the list (no session yet).
        public void Tick(float now, bool scanning)
        {
            if (responder != null)
            {
                try
                {
                    int guard = 0;
                    while (responder.Available > 0 && guard++ < 16)
                    {
                        IPEndPoint ep = null;
                        var data = responder.Receive(ref ep);
                        if (IsRequest(data) && DescribeHost != null)
                        {
                            var p = DescribeHost().Split('|');       // name|port|mode|state
                            if (p.Length >= 4 && ushort.TryParse(p[1], out ushort port) && int.TryParse(p[2], out int mode))
                            {
                                var reply = MakeReply(p[0], port, (NetMode)mode, p[3]);
                                responder.Send(reply, reply.Length, ep);
                            }
                        }
                    }
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { }
            }
            if (!scanning) { if (scanner != null) StopScanner(); return; }
            try
            {
                if (scanner == null)
                {
                    scanner = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
                    nextScan = 0f;
                }
                if (now >= nextScan)
                {
                    nextScan = now + 2f;
                    var req = Encoding.UTF8.GetBytes(Request);
                    foreach (var target in BroadcastTargets()) { try { scanner.Send(req, req.Length, new IPEndPoint(target, DiscoveryPort)); } catch (SocketException) { } }
                }
                int guard = 0;
                while (scanner.Available > 0 && guard++ < 16)
                {
                    IPEndPoint ep = null;
                    var data = scanner.Receive(ref ep);
                    if (!TryParseReply(data, ep.Address.ToString(), out var h)) continue;
                    h.seenAt = now;
                    int i = Found.FindIndex(x => x.address == h.address && x.port == h.port);
                    if (i >= 0) Found[i] = h; else Found.Add(h);
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { scanner = null; }
            Found.RemoveAll(h => now - h.seenAt > 7f);
        }

        // 255.255.255.255 plus each adapter's own subnet broadcast (Windows sends the global one out of one adapter only).
        public static List<IPAddress> BroadcastTargets()
        {
            var list = new List<IPAddress> { IPAddress.Broadcast };
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork || ua.IPv4Mask == null) continue;
                        var a = ua.Address.GetAddressBytes();
                        var m = ua.IPv4Mask.GetAddressBytes();
                        var b = new byte[4];
                        for (int i = 0; i < 4; i++) b[i] = (byte)(a[i] | ~m[i]);
                        var ip = new IPAddress(b);
                        if (!list.Contains(ip)) list.Add(ip);
                    }
                }
            }
            catch { }
            return list;
        }
    }
}
