using System;
using System.Collections.Generic;
using Chaukanna.Scenario;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Chaukanna.Net
{
    // LAN multiplayer (module 15): the trainer's PC hosts, trainees join by IP. LAN only: no relay, no lobby service, no
    // internet; it works air-gapped. Built on Netcode for GameObjects named messages only (no networked objects and no
    // scene sync), so each PC loads the field itself from the host's start packet.
    // Host-authoritative: in Team mode the host's sky (contacts, clock, jam and hit results) is the truth; trainees send
    // decisions. Every PC scores its own trainee and sends the result to the host's score dashboard.
    public class LanSession : MonoBehaviour
    {
        public enum Role { None, Host, Client }

        public static LanSession Instance { get; private set; }
        public static Role Current => Instance != null ? Instance.role : Role.None;
        public static bool Active => Current != Role.None;
        public static bool TeamClient => Instance != null && Instance.role == Role.Client && Instance.inField && Instance.roster.mode == NetMode.Team;
        public static bool TeamHost => Instance != null && Instance.role == Role.Host && Instance.inField && Instance.roster.mode == NetMode.Team;

        public const ushort DefaultPort = 7777;

        public readonly NetRoster roster = new NetRoster();
        public string Status { get; private set; } = "Not connected.";
        public readonly List<string> Log = new List<string>();
        // Host: each trainee's latest result (live during the run, final at the end), by client id.
        public readonly Dictionary<ulong, Chaukanna.Scoring.SessionResult> Results = new Dictionary<ulong, Chaukanna.Scoring.SessionResult>();
        // Set on a trainee's PC when the host starts; the trainer menu picks it up and loads the field.
        public StartPacket PendingStart;
        // A trainee PC waiting for the host's terrain area before it can start; progress for the Lobby.
        public StartPacket WaitingForArea { get; private set; }
        public string AreaProgress { get; private set; } = "";
        long areaBytes, areaTotal;
        string hostAreaFolder = "";             // host: where the files of the run's area are

        // This PC lost the trainer's PC during a run.
        public bool HostLost { get; private set; }

        Role role;
        NetworkManager nm;
        bool inField;
        ulong LocalId => nm != null ? nm.LocalClientId : 0;

        public static LanSession Ensure()
        {
            if (Instance != null) return Instance;
            var go = new GameObject("LAN session");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<LanSession>();
            return Instance;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }

        // Command line for unattended tests and classroom set-up: -chkName S-014 -chkJoin 192.168.1.10 [-chkPort 7777]
        // [-chkReady] [-chkTestBot]. The test bot (testing only) calls and jams what it can see, so the decision path
        // can be checked without a person at that PC.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void FromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            string Arg(string key) { int i = Array.IndexOf(args, key); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            string join = Arg("-chkJoin"), name = Arg("-chkName"), port = Arg("-chkPort");
            if (name != null) Chaukanna.Scoring.ScoreKeeper.PlayerName = name;
            if (join == null) return;
            var lan = Ensure();
            lan.autoReady = Array.IndexOf(args, "-chkReady") >= 0;
            lan.testBot = Array.IndexOf(args, "-chkTestBot") >= 0;
            lan.Join(Chaukanna.Scoring.ScoreKeeper.PlayerName, join, port != null && ushort.TryParse(port, out ushort p) ? p : DefaultPort);
        }

        bool autoReady, testBot, autoReadySent;
        float nextBot;

        void TestBot()
        {
            if (!testBot || Time.time < nextBot) return;
            nextBot = Time.time + 2f;
            var cam = Camera.main;
            var sensor = cam != null ? cam.GetComponent<Chaukanna.Player.ContactSensor>() : null;
            var loop = cam != null ? cam.GetComponent<Chaukanna.Player.DecisionLoop>() : null;
            if (sensor == null || loop == null) return;
            foreach (var c in ContactRegistry.Live)
            {
                if (c == null || c.Actioned) continue;
                // Testing only: face it, select it, call it right, and jam a hostile / report the rest.
                cam.transform.rotation = Quaternion.LookRotation(c.transform.position - cam.transform.position);
                sensor.ClearSelection();
                sensor.Select(c);
                loop.Classify(Chaukanna.Scoring.ScoreCalculator.RightCall(c.TruthClass));
                loop.Act(c.TruthClass.IsHostile() ? PlayerAction.Jam : PlayerAction.Report);
                Note($"test bot: {c.TruthClass.ToString()} #{c.Id} -> {(c.TruthClass.IsHostile() ? "Jam" : "Report")}");
                return;
            }
        }

        void Note(string s)
        {
            Status = s;
            Log.Add($"{DateTime.Now:HH:mm:ss} {s}");
            if (Log.Count > 40) Log.RemoveAt(0);
            Debug.Log("[LAN] " + s);
        }

        NetworkManager MakeManager(string address, ushort port, string listen)
        {
            if (nm == null)
            {
                var go = new GameObject("NetworkManager");     // must be a root object: NGO refuses a nested manager
                DontDestroyOnLoad(go);
                nm = go.AddComponent<NetworkManager>();
                var utp = go.AddComponent<UnityTransport>();
                nm.NetworkConfig = new NetworkConfig { NetworkTransport = utp, EnableSceneManagement = false, ConnectionApproval = false };
            }
            ((UnityTransport)nm.NetworkConfig.NetworkTransport).SetConnectionData(address, port, listen);
            return nm;
        }

        // --- Lobby.

        public bool Host(string name, ushort port = DefaultPort)
        {
            if (Active) return false;
            MakeManager("127.0.0.1", port, "0.0.0.0");
            nm.OnClientConnectedCallback += OnConnected;
            nm.OnClientDisconnectCallback += OnDisconnected;
            if (!nm.StartHost()) { Note($"Could not host on port {port} (is it in use, or blocked by the firewall?)"); return false; }
            role = Role.Host;
            Register();
            roster.players.Clear();
            roster.players.Add(new NetPlayer { clientId = LocalId, name = name, host = true, ready = true, state = "in lobby" });
            Note($"Hosting on port {port}. Trainees join with this PC's LAN address ({LocalAddresses()}).");
            return true;
        }

        public bool Join(string name, string address, ushort port = DefaultPort)
        {
            if (Active) return false;
            MakeManager(address.Trim(), port, null);
            pendingName = name;
            nm.OnClientConnectedCallback += OnConnected;
            nm.OnClientDisconnectCallback += OnDisconnected;
            if (!nm.StartClient()) { Note($"Could not start joining {address}:{port}."); return false; }
            role = Role.Client;
            Register();
            Note($"Joining {address}:{port} ...");
            return true;
        }

        string pendingName = "";

        public void Leave()
        {
            if (nm != null)
            {
                nm.OnClientConnectedCallback -= OnConnected;
                nm.OnClientDisconnectCallback -= OnDisconnected;
                if (nm.IsListening) nm.Shutdown();
            }
            role = Role.None;
            inField = false;
            RunConditions.NetTeamClient = false;
            roster.players.Clear();
            Results.Clear();
            Note("Left the session.");
        }

        public void SetMode(NetMode mode)
        {
            if (role != Role.Host) return;
            roster.mode = mode;
            BroadcastRoster();
        }

        public void SetReady(bool ready)
        {
            if (role != Role.Client) return;
            SendJson(Msg.Ready, NetworkManager.ServerClientId, new NetPlayer { ready = ready }, NetworkDelivery.ReliableSequenced);
        }

        public bool LocalReady => roster.Find(LocalId)?.ready ?? false;

        void OnConnected(ulong id)
        {
            if (role == Role.Client && id == LocalId)
            {
                Note("Connected to the host.");
                SendJson(Msg.Hello, NetworkManager.ServerClientId, new NetPlayer { name = pendingName }, NetworkDelivery.ReliableSequenced);
            }
            else if (role == Role.Host && id != LocalId) Note($"Trainee {id} connected.");
        }

        void OnDisconnected(ulong id)
        {
            if (role == Role.Host)
            {
                var p = roster.Find(id);
                if (p != null) { roster.players.Remove(p); Note($"{p.name} left."); BroadcastRoster(); }
            }
            else if (role == Role.Client)
            {
                Note("Disconnected from the host" + (nm != null && !string.IsNullOrEmpty(nm.DisconnectReason) ? ": " + nm.DisconnectReason : "."));
                // Lost the trainer's PC mid-run: keep this trainee's result. A Team sky is gone, so that run ends here and is
                // scored and saved on this PC; a Solo run simply carries on.
                if (inField)
                {
                    HostLost = true;
                    if (roster.mode == NetMode.Team) { var sp = FindAnyObjectByType<ScenarioSpawner>(); if (sp != null) sp.EndNow(); }
                }
                role = Role.None;
                inField = false;
                RunConditions.NetTeamClient = false;
            }
        }

        void BroadcastRoster()
        {
            foreach (var p in roster.players) if (!p.host) SendJson(Msg.Roster, p.clientId, roster, NetworkDelivery.ReliableSequenced);
        }

        // --- Start, end, results.

        // Host: START in the trainer menu sends this to everyone, then the host loads the field like a solo run.
        public void HostStart(StartPacket packet, string areaFolder = null)
        {
            if (role != Role.Host) return;
            hostAreaFolder = areaFolder ?? "";
            packet.mode = roster.mode;
            packet.players = roster.players;
            Results.Clear();
            foreach (var p in roster.players) { p.state = "loading"; if (!p.host) SendJson(Msg.Start, p.clientId, packet, NetworkDelivery.ReliableFragmentedSequenced); }
            EnterField();
            Note($"Started a {roster.mode} run for {roster.players.Count} seat(s).");
        }

        // Host: everyone back to the menu's Lobby for another run, without restarting the game.
        public void BackToLobby()
        {
            if (role != Role.Host) return;
            foreach (var p in roster.players)
            {
                p.state = "in lobby";
                if (!p.host) { p.ready = false; Send(Msg.Lobby, p.clientId, w => { }, NetworkDelivery.ReliableSequenced); }
            }
            BroadcastRoster();
            LeaveField();
            Note("Everyone back to the lobby.");
        }

        void LeaveField()
        {
            inField = false;
            endSent = false;
            RunConditions.NetTeamClient = false;
            ContactRegistry.InputBlocked = false;
            if (!string.IsNullOrEmpty(Chaukanna.Session.SessionSetup.menuScenePath)) Chaukanna.Session.SessionSetup.Load(Chaukanna.Session.SessionSetup.menuScenePath);
        }

        public void EnterField()
        {
            HostLost = false;
            inField = true;
            announced.Clear();
            anomalySent.Clear();
            proxies.Clear();
            RunConditions.NetTeamClient = role == Role.Client && roster.mode == NetMode.Team;
        }

        public void SendResult(Chaukanna.Scoring.SessionResult s, bool final)
        {
            if (role != Role.Client || s == null) return;
            SendJson(final ? Msg.Result : Msg.Live, NetworkManager.ServerClientId, s, NetworkDelivery.ReliableFragmentedSequenced);
        }

        public static void SendDecision(Contact c, PlayerAction action)
        {
            if (Instance == null || Instance.role != Role.Client) return;
            Instance.SendJson(Msg.Decide, NetworkManager.ServerClientId, new DecisionMsg { id = c.Id, call = (int)c.Call, action = (int)action }, NetworkDelivery.ReliableSequenced);
        }

        // --- Messages.

        void Register()
        {
            var m = nm.CustomMessagingManager;
            m.RegisterNamedMessageHandler(Msg.Hello, (from, r) => { var p = Read<NetPlayer>(r); OnHello(from, p); });
            m.RegisterNamedMessageHandler(Msg.Ready, (from, r) => { var p = Read<NetPlayer>(r); var me = roster.Find(from); if (me != null) { me.ready = p.ready; BroadcastRoster(); } });
            m.RegisterNamedMessageHandler(Msg.Roster, (from, r) => { var ro = Read<NetRoster>(r); roster.mode = ro.mode; roster.players.Clear(); roster.players.AddRange(ro.players); });
            m.RegisterNamedMessageHandler(Msg.Start, (from, r) => { PendingStart = Read<StartPacket>(r); roster.mode = PendingStart.mode; Note("The host started the run."); });
            m.RegisterNamedMessageHandler(Msg.End, (from, r) => { var sp = FindAnyObjectByType<ScenarioSpawner>(); if (sp != null) sp.EndNow(); Note("The host ended the run."); });
            m.RegisterNamedMessageHandler(Msg.NeedArea, (from, r) => { r.ReadValueSafe(out string area); SendArea(from, area); });
            m.RegisterNamedMessageHandler(Msg.AreaChunk, (from, r) =>
            {
                r.ReadValueSafe(out string area); r.ReadValueSafe(out string file); r.ReadValueSafe(out long offset); r.ReadValueSafe(out long total); r.ReadValueSafe(out int len);
                var bytes = new byte[len];
                r.ReadBytesSafe(ref bytes, len);
                OnAreaChunk(area, file, offset, total, bytes);
            });
            m.RegisterNamedMessageHandler(Msg.Lobby, (from, r) => { if (role == Role.Client) { LeaveField(); autoReadySent = false; Note("The trainer brought everyone back to the lobby. Press I'm ready for the next run."); } });
            m.RegisterNamedMessageHandler(Msg.Live, (from, r) => StoreResult(from, Read<Chaukanna.Scoring.SessionResult>(r), false));
            m.RegisterNamedMessageHandler(Msg.Result, (from, r) => StoreResult(from, Read<Chaukanna.Scoring.SessionResult>(r), true));
            m.RegisterNamedMessageHandler(Msg.Spawn, (from, r) => OnSpawn(Read<SpawnMsg>(r)));
            m.RegisterNamedMessageHandler(Msg.Despawn, (from, r) => { r.ReadValueSafe(out int id); r.ReadValueSafe(out string reason); OnDespawn(id, reason); });
            m.RegisterNamedMessageHandler(Msg.Anomaly, (from, r) => { r.ReadValueSafe(out int id); r.ReadValueSafe(out string cue); if (proxies.TryGetValue(id, out var c) && c != null) c.MarkAnomaly(cue); });
            m.RegisterNamedMessageHandler(Msg.Snap, (from, r) => { r.ReadValueSafe(out int len); var bytes = new byte[len]; r.ReadBytesSafe(ref bytes, len); OnSnapshot(bytes); });
            m.RegisterNamedMessageHandler(Msg.Decide, (from, r) => OnDecision(from, Read<DecisionMsg>(r)));
            m.RegisterNamedMessageHandler(Msg.Effect, (from, r) => OnEffect(Read<EffectMsg>(r)));
        }

        void OnHello(ulong from, NetPlayer p)
        {
            if (role != Role.Host) return;
            var me = roster.Find(from);
            string name = string.IsNullOrEmpty(p.name) ? "Trainee " + from : p.name;
            if (me == null) roster.players.Add(new NetPlayer { clientId = from, name = name, state = "in lobby", post = new Vector2(150f * roster.players.Count, 0f) });
            else me.name = name;
            Note($"{name} joined.");
            BroadcastRoster();
        }

        void StoreResult(ulong from, Chaukanna.Scoring.SessionResult s, bool final)
        {
            if (role != Role.Host || s == null) return;
            Results[from] = s;
            var p = roster.Find(from);
            if (p != null) p.state = final ? "COMPLETE" : $"RUNNING {(int)s.duration / 60:00}:{(int)s.duration % 60:00}";
        }

        void SendJson(string msg, ulong to, object payload, NetworkDelivery delivery)
        {
            if (nm == null || !nm.IsListening) return;
            string json = JsonUtility.ToJson(payload);
            int size = System.Text.Encoding.UTF8.GetByteCount(json) * 2 + 64;
            using (var w = new FastBufferWriter(size, Allocator.Temp, Math.Max(size, 1 << 22)))
            {
                w.WriteValueSafe(json);
                nm.CustomMessagingManager.SendNamedMessage(msg, to, w, delivery);
            }
        }

        static T Read<T>(FastBufferReader r)
        {
            r.ReadValueSafe(out string json);
            return JsonUtility.FromJson<T>(json);
        }

        public static string LocalAddresses()
        {
            var list = new List<string>();
            try
            {
                foreach (var a in System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName()).AddressList)
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(a)) list.Add(a.ToString());
            }
            catch { }
            return list.Count > 0 ? string.Join(", ", list) : "see ipconfig";
        }

        // --- Real-terrain areas over the LAN.

        // Trainee: true when every file of the packet's area is already on this PC.
        public static bool HasArea(StartPacket p)
        {
            if (p == null || string.IsNullOrEmpty(p.areaName)) return true;
            foreach (var f in p.areaFiles)
            {
                string local = System.IO.Path.Combine(Chaukanna.TerrainGen.ElevationDownloader.ReceivedAreasFolder, p.areaName, f.name);
                string resolved = Chaukanna.TerrainGen.ElevationDownloader.Resolve(Chaukanna.TerrainGen.ElevationDownloader.ProjectAreasPrefix + p.areaName + "/" + f.name);
                bool here = (System.IO.File.Exists(local) && new System.IO.FileInfo(local).Length == f.size) || (System.IO.File.Exists(resolved) && new System.IO.FileInfo(resolved).Length == f.size);
                if (!here) return false;
            }
            return true;
        }

        // Trainee: ask the host for the area; the start goes on when it has all arrived.
        public void RequestArea(StartPacket p)
        {
            WaitingForArea = p;
            areaBytes = 0;
            areaTotal = 0;
            foreach (var f in p.areaFiles) areaTotal += f.size;
            AreaProgress = $"Receiving the terrain area '{p.areaName}' from the trainer (0 of {areaTotal / 1048576f:F1} MB) ...";
            Note(AreaProgress);
            Send(Msg.NeedArea, NetworkManager.ServerClientId, w => w.WriteValueSafe(p.areaName), NetworkDelivery.ReliableSequenced);
        }

        const int ChunkBytes = 256 * 1024;

        void SendArea(ulong to, string area)
        {
            if (role != Role.Host || string.IsNullOrEmpty(hostAreaFolder) || System.IO.Path.GetFileName(hostAreaFolder) != area) return;
            StartCoroutine(SendAreaFiles(to, area));
        }

        System.Collections.IEnumerator SendAreaFiles(ulong to, string area)
        {
            foreach (var path in System.IO.Directory.GetFiles(hostAreaFolder))
            {
                if (path.EndsWith(".meta") || path.EndsWith(".key")) continue;
                byte[] all = System.IO.File.ReadAllBytes(path);
                string name = System.IO.Path.GetFileName(path);
                for (long off = 0; off < all.Length || (all.Length == 0 && off == 0); off += ChunkBytes)
                {
                    int len = (int)Math.Min(ChunkBytes, all.Length - off);
                    var chunk = new byte[len];
                    Array.Copy(all, off, chunk, 0, len);
                    using (var w = new FastBufferWriter(len + 1024, Allocator.Temp, len + 4096))
                    {
                        w.WriteValueSafe(area); w.WriteValueSafe(name); w.WriteValueSafe(off); w.WriteValueSafe((long)all.Length); w.WriteValueSafe(len);
                        w.WriteBytesSafe(chunk, len);
                        nm.CustomMessagingManager.SendNamedMessage(Msg.AreaChunk, to, w, NetworkDelivery.ReliableFragmentedSequenced);
                    }
                    yield return null;      // one chunk per frame keeps the host's game running
                    if (all.Length == 0) break;
                }
            }
            Note($"Sent the area '{area}' to trainee {to}.");
        }

        void OnAreaChunk(string area, string file, long offset, long total, byte[] bytes)
        {
            if (WaitingForArea == null || WaitingForArea.areaName != area) return;
            string dir = System.IO.Path.Combine(Chaukanna.TerrainGen.ElevationDownloader.ReceivedAreasFolder, area);
            System.IO.Directory.CreateDirectory(dir);
            using (var fs = new System.IO.FileStream(System.IO.Path.Combine(dir, System.IO.Path.GetFileName(file)), offset == 0 ? System.IO.FileMode.Create : System.IO.FileMode.OpenOrCreate))
            {
                fs.Seek(offset, System.IO.SeekOrigin.Begin);
                fs.Write(bytes, 0, bytes.Length);
            }
            areaBytes += bytes.Length;
            AreaProgress = $"Receiving the terrain area '{area}' from the trainer ({areaBytes / 1048576f:F1} of {areaTotal / 1048576f:F1} MB) ...";
            if (HasArea(WaitingForArea))
            {
                Note($"The terrain area '{area}' has arrived.");
                AreaProgress = "";
                PendingStart = WaitingForArea;     // the trainer menu picks it up again and starts
                WaitingForArea = null;
            }
        }

        // --- Team mode: the shared sky.

        readonly HashSet<int> announced = new HashSet<int>();
        readonly HashSet<int> anomalySent = new HashSet<int>();
        readonly Dictionary<int, Contact> proxies = new Dictionary<int, Contact>();
        readonly List<SnapContact> snap = new List<SnapContact>();
        readonly Dictionary<int, (Vector3 from, Vector3 to, float yawFrom, float yawTo)> targets = new Dictionary<int, (Vector3, Vector3, float, float)>();
        float nextSnap, lastSnapAt, snapInterval = 0.1f;
        bool endSent;

        void Update()
        {
            if (role == Role.Client && autoReady && !autoReadySent && nm != null && nm.IsConnectedClient && roster.Find(LocalId) != null) { autoReadySent = true; SetReady(true); }
            if (!inField || nm == null || !nm.IsListening) return;
            if (role == Role.Host) HostTick();
            else if (role == Role.Client) ClientTick();
        }

        void HostTick()
        {
            var spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner == null) return;
            var me = roster.Find(LocalId);
            if (me != null) me.state = spawner.Finished ? "COMPLETE" : $"RUNNING {(int)spawner.ScenarioTime / 60:00}:{(int)spawner.ScenarioTime % 60:00}";
            if (spawner.Finished)
            {
                if (!endSent) { endSent = true; foreach (var p in roster.players) if (!p.host) Send(Msg.End, p.clientId, w => { }, NetworkDelivery.ReliableSequenced); }
                return;
            }
            endSent = false;
            if (roster.mode != NetMode.Team) return;

            // New contacts, gone contacts, anomalies: reliable. Positions: one batched snapshot ten times a second.
            foreach (var c in ContactRegistry.Live)
            {
                if (c == null || announced.Contains(c.Id)) continue;
                announced.Add(c.Id);
                var m = new SpawnMsg { id = c.Id, truth = (int)c.TruthClass, look = (int)c.Data.lookClass, seed = c.Data.lookSeed, deceptive = c.Deceptive, position = c.transform.position };
                foreach (var p in roster.players) if (!p.host) SendJson(Msg.Spawn, p.clientId, m, NetworkDelivery.ReliableSequenced);
            }
            var gone = new List<int>();
            foreach (int id in announced) if (!ContactRegistry.Live.Exists(c => c != null && c.Id == id)) gone.Add(id);
            foreach (int id in gone)
            {
                announced.Remove(id);
                var rec = ContactRegistry.Records.Find(x => x.id == id);
                string reason = rec != null ? rec.despawnReason ?? "Destroyed" : "Destroyed";
                foreach (var p in roster.players) if (!p.host) Send(Msg.Despawn, p.clientId, w => { w.WriteValueSafe(id); w.WriteValueSafe(reason); }, NetworkDelivery.ReliableSequenced);
            }
            foreach (var c in ContactRegistry.Live)
                if (c != null && c.AnomalyShown && anomalySent.Add(c.Id))
                {
                    string cue = c.Data.anomalyCue ?? "left its pattern";
                    int id = c.Id;
                    foreach (var p in roster.players) if (!p.host) Send(Msg.Anomaly, p.clientId, w => { w.WriteValueSafe(id); w.WriteValueSafe(cue); }, NetworkDelivery.ReliableSequenced);
                }

            if (Time.time < nextSnap) return;
            nextSnap = Time.time + snapInterval;
            snap.Clear();
            foreach (var c in ContactRegistry.Live) if (c != null) snap.Add(new SnapContact { id = c.Id, position = c.transform.position, yaw = c.transform.eulerAngles.y });
            Vector3 origin = spawner.defendedPoint != null ? spawner.defendedPoint.position : spawner.transform.position;
            for (int start = 0; start < snap.Count || start == 0; start += Snapshot.MaxPerMessage)
            {
                var bytes = Snapshot.Pack(spawner.ScenarioTime, origin, snap, start, Mathf.Min(Snapshot.MaxPerMessage, snap.Count - start));
                foreach (var p in roster.players) if (!p.host) Send(Msg.Snap, p.clientId, w => { w.WriteValueSafe(bytes.Length); w.WriteBytesSafe(bytes, bytes.Length); }, NetworkDelivery.UnreliableSequenced);
                if (snap.Count == 0) break;
            }
        }

        void Send(string msg, ulong to, Action<FastBufferWriter> write, NetworkDelivery delivery)
        {
            using (var w = new FastBufferWriter(1400, Allocator.Temp, 1 << 16))
            {
                write(w);
                nm.CustomMessagingManager.SendNamedMessage(msg, to, w, delivery);
            }
        }

        void OnDecision(ulong from, DecisionMsg m)
        {
            if (role != Role.Host) return;
            var c = ContactRegistry.Live.Find(x => x != null && x.Id == m.id);
            var loop = FindAnyObjectByType<Chaukanna.Player.DecisionLoop>();
            var reply = new EffectMsg { id = m.id, action = m.action };
            if (c != null && loop != null && loop.RemoteAct(c, (PlayerCall)m.call, (PlayerAction)m.action, out bool locked, out bool hit, out string result))
            {
                reply.locked = locked; reply.hit = hit; reply.result = result;
            }
            else reply.result = "no effect (already gone)";
            var who = roster.Find(from);
            ContactRegistry.Note($"{(who != null ? who.name : "Trainee")}: {(PlayerAction)m.action} on #{m.id:00}, {reply.result}");
            SendJson(Msg.Effect, from, reply, NetworkDelivery.ReliableSequenced);
        }

        void OnGUI()
        {
            if (!HostLost || Event.current.type != EventType.Repaint) return;
            float ui = Screen.height / 720f;
            var r = new Rect(Screen.width * 0.5f - 380f * ui, Screen.height * 0.12f, 760f * ui, 56f * ui);
            var old = GUI.color;
            GUI.color = new Color(0.55f, 0.12f, 0.1f, 0.9f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
            var st = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = Mathf.RoundToInt(16f * ui), fontStyle = FontStyle.Bold, wordWrap = true };
            st.normal.textColor = Color.white;
            GUI.Label(r, roster.mode == NetMode.Team ? "Lost the trainer's PC. The shared run has ended here; your result is saved on this PC." : "Lost the trainer's PC. Your run carries on here and your result is saved on this PC.", st);
        }

        void ClientTick()
        {
            TestBot();
            // Interpolate proxies between the last two snapshots.
            float k = Mathf.Clamp01((Time.time - lastSnapAt) / snapInterval);
            foreach (var kv in targets)
                if (proxies.TryGetValue(kv.Key, out var c) && c != null)
                {
                    c.transform.position = Vector3.Lerp(kv.Value.from, kv.Value.to, k);
                    c.transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(kv.Value.yawFrom, kv.Value.yawTo, k), 0f);
                }
            // Live result to the host every 5 s.
            if (Time.time >= nextLive)
            {
                nextLive = Time.time + 5f;
                var keeper = FindAnyObjectByType<Chaukanna.Scoring.ScoreKeeper>();
                if (keeper != null && keeper.Last == null) SendResult(keeper.Current(), false);
            }
        }

        float nextLive;

        void OnSpawn(SpawnMsg m)
        {
            if (role != Role.Client || !inField || roster.mode != NetMode.Team) return;
            var spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner == null || proxies.ContainsKey(m.id)) return;
            var c = spawner.SpawnSingle((ContactClass)m.truth, (ContactClass)m.look, m.position, m.position, 0f, m.seed, m.id, m.deceptive);
            if (c == null) return;
            var motion = c.GetComponent<ContactMotion>();
            if (motion != null) motion.enabled = false;     // the host moves it
            proxies[m.id] = c;
            targets[m.id] = (m.position, m.position, 0f, 0f);
        }

        void OnDespawn(int id, string reason)
        {
            if (proxies.TryGetValue(id, out var c) && c != null) c.Despawn(reason);
            proxies.Remove(id);
            targets.Remove(id);
        }

        void OnSnapshot(byte[] bytes)
        {
            if (role != Role.Client || !inField) return;
            var spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner == null) return;
            Vector3 origin = spawner.defendedPoint != null ? spawner.defendedPoint.position : spawner.transform.position;
            Snapshot.Unpack(bytes, origin, snap);
            foreach (var s in snap)
                if (proxies.TryGetValue(s.id, out var c) && c != null)
                    targets[s.id] = (c.transform.position, s.position, c.transform.eulerAngles.y, s.yaw);
            lastSnapAt = Time.time;
        }

        void OnEffect(EffectMsg m)
        {
            if (!proxies.TryGetValue(m.id, out var c) || c == null) return;
            if ((PlayerAction)m.action == PlayerAction.Jam)
            {
                c.Data.actionLocked = m.locked;
                c.Data.actionResult = m.result;
            }
            else if ((PlayerAction)m.action == PlayerAction.Engage) c.Shot(m.hit, m.result, false);
        }
    }
}
