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

        // The host's scenario clock as seen from here. On the host it is the scenario clock; on a team client it is the time of the
        // last snapshot plus the seconds since it arrived (a client's own ScenarioTime starts when its field finishes loading, so it
        // is not the host's). The shared crowd runs on it. Valid once the first snapshot has arrived.
        public static bool HostTimeValid => Instance != null && (Instance.role == Role.Host || Instance.hostTimeValid || Instance.clock.Valid);
        public static float HostTime => Instance != null && Instance.role == Role.Client && Instance.clock.Valid ? Instance.clock.HostNow(Time.realtimeSinceStartup)
            : Instance != null && Instance.role == Role.Client && Instance.hostTimeValid ? ClockFrom(Instance.hostSnapTime, Instance.hostSnapAt, Time.time) : ContactRegistry.ScenarioTime;
        public static float ClockFrom(float snapTime, float snapAt, float now) => snapTime + Mathf.Max(0f, now - snapAt);
        // A host scenario time on this PC's own scenario clock (what the records on this PC are written in).
        public static float HostToLocal(float hostTime) => Instance != null && Instance.role == Role.Client && (Instance.hostTimeValid || Instance.clock.Valid) ? hostTime + (ContactRegistry.ScenarioTime - HostTime) : hostTime;

        // Counters for the status line and the live test (host).
        public int ReportsRelayed, RemoteDecisions, CrowdPicksServed;
        public int ReportsSent;                  // trainee: reports this PC sent up
        public int HandledReceived;              // trainee: "a teammate took it out" messages

        float hostSnapTime, hostSnapAt, warnedDrift;
        bool hostTimeValid;

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

        // Every seat's latest pose in world coordinates (host and trainees; ours included), and on the host the whole run's
        // 10 Hz track per seat (for the black box and the 3D replay later).
        public readonly Dictionary<ulong, SeatPose> SeatPoses = new Dictionary<ulong, SeatPose>();
        public readonly Dictionary<ulong, List<PoseMsg>> SeatTracks = new Dictionary<ulong, List<PoseMsg>>();
        public const int MaxTrackSamples = 108000;      // 3 hours at 10 Hz per seat
        public int PosesSent, PosesReceived;
        // Trainee: host clock from ping / pong (the lowest-latency way to know the host's scenario time).
        public readonly ClockSync clock = new ClockSync();
        // Trainee Lobby: hosts found by UDP broadcast. Host: answers the broadcasts.
        public readonly LanDiscovery Discovery = new LanDiscovery();
        float scanRequestedAt = -10f;
        ushort hostPort = DefaultPort;
        public void RequestScan() { scanRequestedAt = Time.unscaledTime; }
        public bool Scanning => Time.unscaledTime - scanRequestedAt < 1f;
        public string RefusedMessage { get; private set; } = "";
        // Trainer comms degradation (range, radio noise, dropout, muffle), all seats plus per-seat overrides; edited in the Lobby and the F6 panel.
        public readonly CommsSettings Comms = new CommsSettings();
        public void NoteExternal(string s) => Note(s);

        internal void SendBytes(string msg, ulong to, byte[] bytes, NetworkDelivery delivery)
        {
            if (nm == null || !nm.IsListening) return;
            Send(msg, to, w => { w.WriteValueSafe(bytes.Length); w.WriteBytesSafe(bytes, bytes.Length); }, delivery);
        }

        internal void SendJsonTo(string msg, ulong to, object payload) => SendJson(msg, to, payload, NetworkDelivery.ReliableSequenced);

        // Metres between two seats as far as this PC knows (the host knows every seat's pose).
        public float DistanceBetween(ulong a, ulong b)
        {
            if (SeatPoses.TryGetValue(a, out var pa) && SeatPoses.TryGetValue(b, out var pb)) return Vector3.Distance(pa.position, pb.position);
            return 0f;
        }

        Vector3 RunOrigin(ScenarioSpawner sp) => sp.defendedPoint != null ? sp.defendedPoint.position : sp.transform.position;

        Role role;
        NetworkManager nm;
        bool inField;
        ulong LocalId => nm != null ? nm.LocalClientId : 0;
        public ulong LocalClientId => LocalId;
        public bool IsHost => role == Role.Host;
        public bool InField => inField;

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
        int botPicks;
        float nextBotPick;
        float nextBot;

        void TestBot()
        {
            if (!testBot || Time.time < nextBot) return;
            nextBot = Time.time + 2f;
            var cam = Camera.main;
            var sensor = cam != null ? cam.GetComponent<Chaukanna.Player.ContactSensor>() : null;
            var loop = cam != null ? cam.GetComponent<Chaukanna.Player.DecisionLoop>() : null;
            if (sensor == null || loop == null) return;
            var seat = RunConditions.SeatRole;
            // A COMMANDER bot waits for reports: it slews to the newest unhandled one (the T key), locks it and jams a hostile
            // (engages it only under card A), holds the rest. It never decides on what nobody reported.
            if (seat == SeatRole.Commander)
            {
                var e = Chaukanna.Player.ReportBoard.NextUnhandled();
                if (e == null) return;
                var rc = ContactRegistry.Live.Find(x => x != null && x.Id == e.id);
                if (rc == null) return;
                var board = cam.GetComponent<Chaukanna.Player.ReportBoard>();
                if (board != null) board.Slew(false);
                else sensor.LockContact(rc, "test bot");
                var reported = (PlayerCall)e.call;
                loop.Classify(reported);
                bool hostileCall = reported == PlayerCall.HostileQuad || reported == PlayerCall.HostileFixedWing;
                var act = !hostileCall ? PlayerAction.Hold : ContactRegistry.CurrentRoe == RoeCard.CardA ? PlayerAction.Engage : PlayerAction.Jam;
                loop.Act(act);
                Note($"test bot (commander): report #{e.id} from {e.seat} ({reported.Label()}) -> {act}");
                return;
            }
            foreach (var c in ContactRegistry.Live)
            {
                if (c == null || c.Actioned) continue;
                // Testing only: face it, select it, call it right, and jam a hostile / report the rest. A SENTRY bot reports (R)
                // everything, because that is all a sentry may do.
                cam.transform.rotation = Quaternion.LookRotation(c.transform.position - cam.transform.position);
                sensor.ClearSelection();
                sensor.LockContact(c, "test bot");      // not Select: that would add the previously locked (crowd-picked) contact to a group
                loop.Classify(Chaukanna.Scoring.ScoreCalculator.RightCall(c.TruthClass));
                var action = seat == SeatRole.Sentry || !c.TruthClass.IsHostile() ? PlayerAction.Report : PlayerAction.Jam;
                loop.Act(action);
                Note($"test bot{(seat == SeatRole.Sentry ? " (sentry)" : "")}: {c.TruthClass.ToString()} #{c.Id} -> {action}");
                return;
            }
            // Dense sky: with nothing left to report, ask the host to promote the nearest crowd drone (CrowdPick), so that round trip
            // is exercised too. It comes back as a contact, which the next tick reports like any other.
            var crowd = CrowdSky.Active;
            if (crowd != null && botPicks < 3 && pendingPick < 0 && Time.time >= nextBotPick && roster.mode == NetMode.Team)
            {
                int best = -1;
                float bestD = 1500f;
                for (int i = 0; i < crowd.Count; i++)
                {
                    if (!crowd.IsAlive(i) || crowd.IsRemoved(i)) continue;
                    float d = Vector3.Distance(cam.transform.position, crowd.PositionOf(i));
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0)
                {
                    botPicks++;
                    nextBotPick = Time.time + 6f;
                    Note($"test bot: picking crowd drone {best} ({bestD:F0} m)");
                    CrowdSky.RemotePromote(best);
                }
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
            roster.units.Clear(); roster.units.Add(NetRoster.DefaultUnit);
            roster.players.Add(new NetPlayer { clientId = LocalId, name = name, host = true, ready = true, state = "in lobby", role = (int)SeatRole.Commander });
            Note($"Hosting on port {port}. Trainees join with this PC's LAN address ({LocalAddresses()}).");
            hostPort = port;
            Discovery.DescribeHost = () => $"{(roster.players.Count > 0 ? roster.players[0].name : "Trainer")}|{hostPort}|{(int)roster.mode}|{(inField ? "run in progress" : "in lobby")}";
            Discovery.StartResponder();
            return true;
        }

        public bool Join(string name, string address, ushort port = DefaultPort)
        {
            if (Active) return false;
            MakeManager(address.Trim(), port, null);
            pendingName = name;
            RefusedMessage = "";
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
            VoiceChat.End();
            Discovery.StopResponder();
            role = Role.None;
            inField = false;
            RunConditions.NetTeamClient = false;
            RunConditions.SeatRole = SeatRole.Any;
            hostTimeValid = false;
            clock.Reset();
            SeatPoses.Clear();
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

        // Host, Lobby tab (Team mode): a seat's role. Travels with the roster, and in the start packet's players list.
        public void SetRole(ulong clientId, SeatRole r)
        {
            if (role != Role.Host) return;
            var p = roster.Find(clientId);
            if (p == null) return;
            p.role = (int)r;
            BroadcastRoster();
        }

        // Host, Lobby tab: put every listed seat into one unit / one role (the multi-select Assign buttons).
        public void AssignUnit(IEnumerable<ulong> ids, string unit)
        {
            if (role != Role.Host || !roster.units.Contains(unit)) return;
            foreach (var id in ids) { var p = roster.Find(id); if (p != null) p.unit = unit; }
            BroadcastRoster();
        }

        public void AssignRole(IEnumerable<ulong> ids, SeatRole r)
        {
            if (role != Role.Host) return;
            foreach (var id in ids) { var p = roster.Find(id); if (p != null) p.role = (int)r; }
            BroadcastRoster();
        }

        public void AddUnit() { if (role != Role.Host) return; for (int i = roster.units.Count + 1; ; i++) if (roster.AddUnit("Unit " + i)) break; BroadcastRoster(); }
        public void RenameUnit(string from, string to) { if (role == Role.Host && roster.RenameUnit(from, to)) BroadcastRoster(); }
        public void DeleteUnit(string name) { if (role == Role.Host && roster.DeleteUnit(name)) BroadcastRoster(); }

        // The Lobby's role button: SENTRY > COMMANDER > ANY > OBSERVER > SENTRY.
        public void CycleRole(ulong clientId)
        {
            var p = roster.Find(clientId);
            if (p == null) return;
            SetRole(clientId, p.Role == SeatRole.Sentry ? SeatRole.Commander : p.Role == SeatRole.Commander ? SeatRole.Any : p.Role == SeatRole.Any ? SeatRole.Observer : SeatRole.Sentry);
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
                RefusedMessage = nm != null ? nm.DisconnectReason ?? "" : "";
                nm.OnClientConnectedCallback -= OnConnected;
                nm.OnClientDisconnectCallback -= OnDisconnected;
                VoiceChat.End();
                // Lost the trainer's PC mid-run: keep this trainee's result. A Team sky is gone, so that run ends here and is
                // scored and saved on this PC; a Solo run simply carries on.
                if (inField)
                {
                    HostLost = true;
                    if (roster.mode == NetMode.Team) { var sp = Spawner; if (sp != null) sp.EndNow(); }
                }
                role = Role.None;
                inField = false;
                RunConditions.NetTeamClient = false;
                hostTimeValid = false;
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
            packet.units = new List<string>(roster.units);
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
            VoiceChat.End();
            inField = false;
            endSent = false;
            RunConditions.NetTeamClient = false;
            RunConditions.SeatRole = SeatRole.Any;
            spawnerCache = null;
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
            targets.Clear();
            pendingReports.Clear();
            lastReportCall.Clear();
            pickSentAt.Clear();
            pendingPick = -1;
            hostTimeValid = false;
            warnedDrift = 0f;
            spawnerCache = null;
            Chaukanna.Player.ReportBoard.Clear();
            clock.Reset();
            SeatPoses.Clear();
            SeatTracks.Clear();
            pingBurst = 4;
            nextPing = 0f;
            nextPose = 0f;
            PosesSent = PosesReceived = 0;
            ReportsRelayed = RemoteDecisions = CrowdPicksServed = ReportsSent = HandledReceived = 0;
            RunConditions.NetTeamClient = role == Role.Client && roster.mode == NetMode.Team;
            // The seat's role applies in a team run only; Solo mode and a single PC are always Any.
            var me = roster.Find(LocalId);
            RunConditions.SeatId = me != null ? me.clientId.ToString() : "Seat 1";
            RunConditions.UnitId = me != null && !string.IsNullOrEmpty(me.unit) ? me.unit : "Unit 1";
            RunConditions.SeatRole = role != Role.None && roster.mode == NetMode.Team && me != null ? me.Role : SeatRole.Any;
            Note($"Entering the field as {(RunConditions.SeatRole == SeatRole.Any ? "ANY" : NetPlayer.RoleName(RunConditions.SeatRole))} ({roster.mode}).");
            // Proximity voice is for TEAM runs (one shared place); the host records it for the after-action review.
            VoiceChat.Begin(this, roster.mode == NetMode.Team);
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
            m.RegisterNamedMessageHandler(Msg.Roster, (from, r) => { var ro = Read<NetRoster>(r); roster.mode = ro.mode; roster.players.Clear(); roster.players.AddRange(ro.players); if (ro.units != null && ro.units.Count > 0) { roster.units.Clear(); roster.units.AddRange(ro.units); } });
            m.RegisterNamedMessageHandler(Msg.Start, (from, r) => { PendingStart = Read<StartPacket>(r); AdoptStartRoster(PendingStart); Note("The host started the run."); });
            m.RegisterNamedMessageHandler(Msg.End, (from, r) => { var sp = Spawner; if (sp != null) sp.EndNow(); Note("The host ended the run."); });
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
            m.RegisterNamedMessageHandler(Msg.Report, (from, r) => OnReportMessage(from, Read<ReportMsg>(r)));
            m.RegisterNamedMessageHandler(Msg.CrowdPick, (from, r) => OnCrowdPick(from, Read<CrowdPickMsg>(r)));
            m.RegisterNamedMessageHandler(Msg.Handled, (from, r) => OnHandled(Read<HandledMsg>(r)));
            m.RegisterNamedMessageHandler(Msg.Voice, (from, r) => VoiceChat.OnMessage(from, r));
            m.RegisterNamedMessageHandler(Msg.Comms, (from, r) => VoiceChat.OnCommsMessage(r));
            m.RegisterNamedMessageHandler(Msg.Pose, (from, r) => { r.ReadValueSafe(out int len); var bytes = new byte[len]; r.ReadBytesSafe(ref bytes, len); OnPoseMessage(from, bytes); });
            m.RegisterNamedMessageHandler(Msg.Ping, (from, r) => { r.ReadValueSafe(out float t0); if (role == Role.Host) Send(Msg.Pong, from, w => { w.WriteValueSafe(t0); w.WriteValueSafe(ContactRegistry.ScenarioTime); }, NetworkDelivery.Unreliable); });
            m.RegisterNamedMessageHandler(Msg.Pong, (from, r) => { r.ReadValueSafe(out float t0); r.ReadValueSafe(out float hostTime); if (role == Role.Client) clock.AddSample(t0, Time.realtimeSinceStartup, hostTime); });
        }

        // A trainee PC: the start packet carries the seat list with the roles the trainer chose.
        void AdoptStartRoster(StartPacket packet)
        {
            roster.mode = packet.mode;
            if (packet.units != null && packet.units.Count > 0) { roster.units.Clear(); roster.units.AddRange(packet.units); }
            if (packet.players == null || packet.players.Count == 0) return;
            roster.players.Clear();
            roster.players.AddRange(packet.players);
        }

        void OnHello(ulong from, NetPlayer p)
        {
            if (role != Role.Host) return;
            var me = roster.Find(from);
            string name = string.IsNullOrEmpty(p.name) ? "Trainee " + from : p.name;
            // A run is under way: a late joiner would see a different sky and miss the start packet. Refuse with a reason the
            // trainee's Lobby shows ("Disconnected from the host: ...").
            if (inField && me == null)
            {
                const string why = "The run has already started. Try again when the trainer brings everyone back to the lobby.";
                Note($"{name} tried to join during the run and was refused.");
                if (nm != null) nm.DisconnectClient(from, why);
                return;
            }
            if (me == null) roster.players.Add(new NetPlayer { clientId = from, name = name, state = "in lobby", post = new Vector2(150f * roster.players.Count, 0f), role = (int)SeatRole.Sentry });
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

        internal void SendJson(string msg, ulong to, object payload, NetworkDelivery delivery)
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
                if (path.EndsWith(".meta") || path.EndsWith(".key") || path.EndsWith("_osm.json") || path.EndsWith("_osm_surround.json")) continue;     // the raw OpenStreetMap answers stay on the trainer PC; _layers.json is what a trainee needs
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
        readonly HashSet<int> liveIds = new HashSet<int>();         // host: ids in the sky this tick, built once
        readonly List<int> goneScratch = new List<int>();
        readonly Dictionary<int, Contact> proxies = new Dictionary<int, Contact>();
        readonly List<SnapContact> snap = new List<SnapContact>();
        readonly Dictionary<int, (Vector3 from, Vector3 to, float yawFrom, float yawTo)> targets = new Dictionary<int, (Vector3, Vector3, float, float)>();
        float nextSnap, lastSnapAt, snapInterval = 0.1f;
        bool endSent;

        // The spawner and the decision loop, found once per field load (not every frame). Null while the field scene is loading.
        ScenarioSpawner spawnerCache;
        Chaukanna.Player.DecisionLoop loopCache;
        float nextSpawnerFind;
        ScenarioSpawner Spawner
        {
            get
            {
                if (spawnerCache == null && Time.unscaledTime >= nextSpawnerFind)
                {
                    spawnerCache = FindAnyObjectByType<ScenarioSpawner>();
                    nextSpawnerFind = Time.unscaledTime + 0.5f;
                }
                return spawnerCache;
            }
        }
        Chaukanna.Player.DecisionLoop Loop => loopCache != null ? loopCache : (loopCache = FindAnyObjectByType<Chaukanna.Player.DecisionLoop>());

        // Ids announced to the trainees that are no longer in the sky: one set lookup each, not a list search per id.
        public static void FindGone(HashSet<int> announcedIds, HashSet<int> live, List<int> into)
        {
            into.Clear();
            foreach (int id in announcedIds) if (!live.Contains(id)) into.Add(id);
        }

        void Update()
        {
            Discovery.Tick(Time.unscaledTime, role == Role.None && Scanning);
            if (role == Role.Client && autoReady && !autoReadySent && nm != null && nm.IsConnectedClient && roster.Find(LocalId) != null) { autoReadySent = true; SetReady(true); }
            if (!inField || nm == null || !nm.IsListening) return;
            EnsureBoard();
            PoseTick();
            if (role == Role.Host) HostTick();
            else if (role == Role.Client) ClientTick();
        }

        // A commander seat gets its report board once the field is up.
        Camera boardCam;
        void EnsureBoard()
        {
            if (RunConditions.SeatRole != SeatRole.Commander) return;
            var sp = Spawner;
            var cam = Camera.main;
            if (sp == null || sp.Waiting || cam == null || cam == boardCam) return;
            if (cam.GetComponent<Chaukanna.Player.ReportBoard>() == null) cam.gameObject.AddComponent<Chaukanna.Player.ReportBoard>();
            boardCam = cam;
        }

        // --- Player poses (10 Hz) and the clock ping.

        float nextPose, nextPing;
        int pingBurst;

        // Every seat sends its pose to the host ten times a second; the host stores it (the per-seat list the replay will use),
        // shows it on the truth map and relays it to the other seats (voice needs the speaker's position).
        void PoseTick()
        {
            if (Time.time < nextPose) return;
            nextPose = Time.time + 0.1f;
            var sp = Spawner;
            var cam = Camera.main;
            if (sp == null || sp.Waiting || cam == null) return;
            var v = VoiceChat.Instance;
            byte flags = (byte)(v != null && v.Transmitting ? (v.TxChannel == 1 ? 2 : 1) : 0);
            Vector3 origin = RunOrigin(sp), p = cam.transform.position;
            var m = new PoseMsg
            {
                seat = (ushort)LocalId,
                time = HostTimeValid ? HostTime : ContactRegistry.ScenarioTime,
                rel = new Vector3(p.x - origin.x, p.y, p.z - origin.z),
                yaw = cam.transform.eulerAngles.y,
                pitch = -Mathf.DeltaAngle(0f, cam.transform.eulerAngles.x),
                fov = cam.fieldOfView,
                flags = flags,
            };
            StorePose(m, origin);
            PosesSent++;
            var bytes = PoseCodec.Pack(m);
            if (role == Role.Client) SendBytes(Msg.Pose, NetworkManager.ServerClientId, bytes, NetworkDelivery.UnreliableSequenced);
            else if (role == Role.Host) RelayPose(bytes, LocalId);
        }

        void StorePose(PoseMsg m, Vector3 origin)
        {
            ulong seat = m.seat;
            if (!SeatPoses.TryGetValue(seat, out var sp)) { sp = new SeatPose(); SeatPoses[seat] = sp; }
            sp.position = new Vector3(origin.x + m.rel.x, m.rel.y, origin.z + m.rel.z);
            sp.yaw = m.yaw; sp.pitch = m.pitch; sp.fov = m.fov; sp.flags = m.flags; sp.time = m.time; sp.receivedAt = Time.time;
            if (role == Role.Host)
            {
                if (!SeatTracks.TryGetValue(seat, out var list)) { list = new List<PoseMsg>(); SeatTracks[seat] = list; }
                if (list.Count < MaxTrackSamples) list.Add(m);
            }
        }

        void RelayPose(byte[] bytes, ulong exceptId)
        {
            foreach (var p in roster.players) if (!p.host && p.clientId != exceptId) SendBytes(Msg.Pose, p.clientId, bytes, NetworkDelivery.UnreliableSequenced);
        }

        void OnPoseMessage(ulong from, byte[] bytes)
        {
            if (!inField || !PoseCodec.TryUnpack(bytes, out var m)) return;
            var sp = Spawner;
            if (sp == null) return;
            if (role == Role.Host)
            {
                m.seat = (ushort)from;              // the transport says who sent it
                if (roster.Find(from) == null) return;
                StorePose(m, RunOrigin(sp));
                PosesReceived++;
                RelayPose(PoseCodec.Pack(m), from);
            }
            else if (role == Role.Client && m.seat != LocalId)
            {
                StorePose(m, RunOrigin(sp));
                PosesReceived++;
            }
        }

        // Trainee: four pings 0.3 s apart when the field starts, then one every 10 s. The host answers with its scenario time.
        void PingTick()
        {
            if (Time.time < nextPing) return;
            nextPing = Time.time + (pingBurst > 0 ? 0.3f : 10f);
            if (pingBurst > 0) pingBurst--;
            float t0 = Time.realtimeSinceStartup;
            Send(Msg.Ping, NetworkManager.ServerClientId, w => w.WriteValueSafe(t0), NetworkDelivery.Unreliable);
        }

        void HostTick()
        {
            var spawner = Spawner;
            if (spawner == null) return;
            var me = roster.Find(LocalId);
            if (me != null) me.state = spawner.Finished ? "COMPLETE" : $"RUNNING {(int)spawner.ScenarioTime / 60:00}:{(int)spawner.ScenarioTime % 60:00}";
            if (spawner.Finished)
            {
                if (!endSent) { endSent = true; if (VoiceChat.Instance != null) VoiceChat.Instance.FinishRecording(); foreach (var p in roster.players) if (!p.host) Send(Msg.End, p.clientId, w => { }, NetworkDelivery.ReliableSequenced); }
                return;
            }
            endSent = false;
            if (roster.mode != NetMode.Team) return;

            // New contacts, gone contacts, anomalies: reliable. Positions: one batched snapshot ten times a second.
            liveIds.Clear();
            foreach (var c in ContactRegistry.Live) if (c != null) liveIds.Add(c.Id);
            foreach (var c in ContactRegistry.Live)
            {
                if (c == null || announced.Contains(c.Id)) continue;
                announced.Add(c.Id);
                var m = new SpawnMsg { id = c.Id, truth = (int)c.TruthClass, look = (int)c.Data.lookClass, seed = c.Data.lookSeed, deceptive = c.Deceptive, position = c.transform.position, crowdIndex = c.Data.crowdIndex };
                foreach (var p in roster.players) if (!p.host) SendJson(Msg.Spawn, p.clientId, m, NetworkDelivery.ReliableSequenced);
            }
            FindGone(announced, liveIds, goneScratch);
            foreach (int id in goneScratch)
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

        internal void Send(string msg, ulong to, Action<FastBufferWriter> write, NetworkDelivery delivery)
        {
            using (var w = new FastBufferWriter(1400, Allocator.Temp, 1 << 16))
            {
                write(w);
                nm.CustomMessagingManager.SendNamedMessage(msg, to, w, delivery);
            }
        }

        // A trainee's jam or shot. The host's effectors decide what happens; the host seat's own record is not given the call or
        // the action (see DecisionLoop.RemoteAct). A sentry may not decide at all: the host refuses it as well as the sentry's PC.
        void OnDecision(ulong from, DecisionMsg m)
        {
            if (role != Role.Host) return;
            var c = ContactRegistry.Live.Find(x => x != null && x.Id == m.id);
            var loop = Loop;
            var who = roster.Find(from);
            string seat = who != null ? who.name : "Trainee";
            var reply = new EffectMsg { id = m.id, action = m.action };
            if (roster.mode == NetMode.Team && who != null && !Chaukanna.Player.DecisionLoop.RoleAllows(who.Role, (PlayerAction)m.action))
                reply.result = who.Role == SeatRole.Observer ? "refused: an observer takes no action" : "refused: a sentry reports, the commander decides";
            else if (c != null && loop != null && loop.RemoteAct(c, (PlayerCall)m.call, (PlayerAction)m.action, seat, from, out bool locked, out bool hit, out string result))
            {
                reply.locked = locked; reply.hit = hit; reply.result = result;
                RemoteDecisions++;
            }
            else reply.result = "no effect (already gone)";
            ContactRegistry.Note($"{seat}: {(PlayerAction)m.action} on #{m.id:00}, {reply.result}");
            SendJson(Msg.Effect, from, reply, NetworkDelivery.ReliableSequenced);
        }

        // --- Reports: sentry > host > commander seat(s).

        // R on a contact (DecisionLoop): the sentry's report goes up to the host, which keeps it and forwards it to every commander.
        // Only in a team run; a repeat of the same call on the same contact is not sent twice.
        public static void SendReport(Contact c, PlayerCall call)
        {
            var s = Instance;
            if (s == null || c == null || !s.inField || s.roster.mode != NetMode.Team) return;
            if (s.lastReportCall.TryGetValue(c.Id, out var last) && last == call) return;
            s.lastReportCall[c.Id] = call;
            var cam = Camera.main;
            Vector3 d = cam != null ? c.transform.position - cam.transform.position : Vector3.zero;
            var m = new ReportMsg { id = c.Id, call = (int)call, range = d.magnitude, bearing = Mathf.Repeat(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, 360f) };
            s.ReportsSent++;
            if (s.role == Role.Client) s.Note($"report sent: #{c.Id:00} as {call.Label()} ({m.range:F0} m)");
            if (s.role == Role.Client) s.SendJson(Msg.Report, NetworkManager.ServerClientId, m, NetworkDelivery.ReliableSequenced);
            else if (s.role == Role.Host) s.HandleReport(s.LocalId, m);
        }

        readonly Dictionary<int, PlayerCall> lastReportCall = new Dictionary<int, PlayerCall>();
        readonly Dictionary<int, (ReportMsg msg, float localTime)> pendingReports = new Dictionary<int, (ReportMsg, float)>();

        void OnReportMessage(ulong from, ReportMsg m)
        {
            if (role == Role.Host) HandleReport(from, m);        // a trainee's report arrives
            else if (role == Role.Client && inField) DeliverReport(m);   // the host forwarded it: this seat is a commander
        }

        // Host: stamp who and when, keep it, and forward it to every commander seat except the one that sent it.
        void HandleReport(ulong from, ReportMsg m)
        {
            var who = roster.Find(from);
            m.seat = who != null ? who.name : "Seat " + from;
            m.seatId = from;
            m.time = ContactRegistry.ScenarioTime;
            ReportsRelayed++;
            ContactRegistry.Note($"{m.seat} reports #{m.id:00}: {((PlayerCall)m.call).Label()} ({m.range:F0} m, {m.bearing:000} deg)");
            DeliverReport(m);       // this PC's copy: the record, and the board if the host seat is a commander
            foreach (var p in roster.players)
                if (!p.host && p.clientId != from && p.Role == SeatRole.Commander) SendJson(Msg.Report, p.clientId, m, NetworkDelivery.ReliableSequenced);
        }

        // A report reaches this PC: the contact's record notes it (the first one sets reportReceivedTime), and a commander's board
        // lists it. A report that beats its proxy here waits in pendingReports until the proxy spawns.
        void DeliverReport(ReportMsg m)
        {
            float now = ContactRegistry.ScenarioTime;
            var rec = ContactRegistry.Records.Find(x => x.id == m.id);
            if (rec != null) StampReport(rec, m, now);
            else pendingReports[m.id] = (m, now);
            if (RunConditions.SeatRole == SeatRole.Commander && m.seatId != LocalId) Chaukanna.Player.ReportBoard.Add(m);
        }

        public static void StampReport(ContactRecord rec, ReportMsg m, float now)
        {
            if (rec.reportReceivedTime < 0f) rec.reportReceivedTime = now;
            rec.reportedBy = m.seat;
            rec.reportedCall = (PlayerCall)m.call;
            rec.Log($"report from {m.seat}: {((PlayerCall)m.call).Label()}");
        }

        // --- A teammate's jam or hit.

        // Host: the effect landed (a jam that worked, a hit). Every trainee PC is told, except the one that asked for it (it hears
        // from the Effect reply), so no other seat's clock runs out on a contact that is already coming down.
        public static void HostHandled(Contact c, string seat, PlayerAction action, ulong exceptId = ulong.MaxValue)
        {
            var s = Instance;
            if (s == null || c == null || s.role != Role.Host || !s.inField || s.roster.mode != NetMode.Team) return;
            var m = new HandledMsg { id = c.Id, action = (int)action, by = seat, time = ContactRegistry.ScenarioTime };
            foreach (var p in s.roster.players) if (!p.host && p.clientId != exceptId) s.SendJson(Msg.Handled, p.clientId, m, NetworkDelivery.ReliableSequenced);
        }

        void OnHandled(HandledMsg m)
        {
            if (role != Role.Client || !inField || !proxies.TryGetValue(m.id, out var c) || c == null) return;
            HandledReceived++;
            Note($"#{m.id:00} handled by {m.by} ({(PlayerAction)m.action})");
            var d = c.Data;
            if (d.action == PlayerAction.Jam || d.action == PlayerAction.Engage || d.handledTime >= 0f) return;    // this seat acted itself / already marked
            d.handledBy = m.by;
            d.handledTime = HostToLocal(m.time);
            d.handledAction = ((PlayerAction)m.action).ToString();
            d.Log($"handled by {m.by}: {d.handledAction}");
        }

        // --- Dense sky.

        int pendingPick = -1;
        float pendingPickAt;
        Vector3 pendingAim;
        readonly Dictionary<int, float> pickSentAt = new Dictionary<int, float>();

        // Team client (CrowdSky.RemotePromote): ask the host to promote this crowd drone. The pick and the aim are remembered, so
        // when the host's SpawnMsg for it arrives this seat locks the proxy.
        public static void SendCrowdPick(int index)
        {
            var s = Instance;
            if (s == null || s.role != Role.Client || !s.inField || s.roster.mode != NetMode.Team) return;
            var cam = Camera.main;
            s.pendingPick = index;
            s.pendingPickAt = Time.time;
            s.pendingAim = cam != null ? cam.transform.forward : Vector3.forward;
            if (s.pickSentAt.TryGetValue(index, out float at) && Time.time - at < 3f) return;     // already asked
            s.pickSentAt[index] = Time.time;
            s.SendJson(Msg.CrowdPick, NetworkManager.ServerClientId, new CrowdPickMsg { index = index }, NetworkDelivery.ReliableSequenced);
        }

        // Host: promote the agent as if its own player had locked it, but without locking it on the host. Its SpawnMsg (next tick)
        // carries the crowd index, which makes every PC take that agent out of its own crowd.
        void OnCrowdPick(ulong from, CrowdPickMsg m)
        {
            if (role != Role.Host || !inField) return;
            var sp = Spawner;
            var crowd = sp != null ? sp.Crowd : null;
            if (crowd == null) return;
            var c = crowd.Promote(m.index);
            var who = roster.Find(from);
            if (c != null)
            {
                CrowdPicksServed++;
                ContactRegistry.Note($"{(who != null ? who.name : "Trainee")}: picked crowd drone {m.index}, now track #{c.Id:00}");
            }
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
            PingTick();
            if (pendingPick >= 0 && Time.time - pendingPickAt > 5f) pendingPick = -1;      // the host never promoted it (taken, or out of its sky)
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
            var spawner = Spawner;
            if (spawner == null || proxies.ContainsKey(m.id)) return;
            // A promoted crowd drone: this PC's own copy of the agent leaves its crowd, and the proxy starts where the agent is on
            // the host's clock.
            Vector3 position = m.position;
            CrowdAgent agent = default;
            bool fromCrowd = false;
            if (m.crowdIndex >= 0 && spawner.Crowd != null && spawner.Crowd.TakeForRemote(m.crowdIndex, out agent, out Vector3 crowdPos)) { fromCrowd = true; position = crowdPos; }
            var c = spawner.SpawnSingle((ContactClass)m.truth, (ContactClass)m.look, position, position, 0f, m.seed, m.id, m.deceptive);
            if (c == null) return;
            var motion = c.GetComponent<ContactMotion>();
            if (motion != null) motion.enabled = false;     // the host moves it
            proxies[m.id] = c;
            targets[m.id] = (position, position, 0f, 0f);
            c.Data.crowdIndex = m.crowdIndex;
            if (fromCrowd)
            {
                c.Data.fromCrowd = true;
                c.Data.crowdPattern = agent.pattern;
                c.Data.iffNoReply = agent.iff == 1;
            }
            if (fromCrowd) Note($"crowd drone {m.crowdIndex} promoted by the host as #{m.id:00}: taken out of this PC's crowd");
            if (pendingReports.TryGetValue(m.id, out var pr)) { StampReport(c.Data, pr.msg, pr.localTime); pendingReports.Remove(m.id); }
            if (pendingPick >= 0 && m.crowdIndex == pendingPick)
            {
                var cam = Camera.main;
                var sensor = cam != null ? cam.GetComponent<Chaukanna.Player.ContactSensor>() : null;
                if (sensor != null) sensor.LockContact(c, "crowd pick");
                pendingPick = -1;
            }
        }

        void OnDespawn(int id, string reason)
        {
            if (proxies.TryGetValue(id, out var c) && c != null)
            {
                // Taken out by a teammate and no Handled message got here first: it still was not this seat's miss.
                var d = c.Data;
                if ((reason == "JammedDown" || reason == "EngagedHit") && d.handledTime < 0f && d.action != PlayerAction.Jam && d.action != PlayerAction.Engage)
                {
                    d.handledBy = "the team";
                    d.handledTime = ContactRegistry.ScenarioTime;
                    d.handledAction = reason == "JammedDown" ? "Jam" : "Engage";
                    d.Log("handled by the team (" + reason + ")");
                }
                c.Despawn(reason);
            }
            proxies.Remove(id);
            targets.Remove(id);
        }

        void OnSnapshot(byte[] bytes)
        {
            if (role != Role.Client || !inField) return;
            var spawner = Spawner;
            if (spawner == null) return;
            Vector3 origin = spawner.defendedPoint != null ? spawner.defendedPoint.position : spawner.transform.position;
            float hostT = Snapshot.Unpack(bytes, origin, snap);
            // The host's clock: the newest snapshot time, plus the seconds since it arrived (HostTime).
            if (!hostTimeValid || hostT >= hostSnapTime - 0.5f) { hostSnapTime = hostT; hostSnapAt = Time.time; hostTimeValid = true; }
            float drift = spawner.ScenarioTime - hostT;
            if (Mathf.Abs(drift) > 2f && Mathf.Abs(drift - warnedDrift) > 2f)
            {
                warnedDrift = drift;
                Note($"Clock drift: this PC's scenario clock is {Mathf.Abs(drift):F1} s {(drift < 0f ? "behind" : "ahead of")} the host's (local {spawner.ScenarioTime:F1} s, host {hostT:F1} s). The shared crowd follows the host's clock.");
            }
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
