using Chaukanna.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Player
{
    // Classify the locked contact (1-5), then take one final action on it (H / R / J / F).
    // No scoring here: every decision goes to ContactRegistry.Records for the AAR, and a [Decision] summary is printed at the end.
    [RequireComponent(typeof(ContactSensor))]
    public class DecisionLoop : MonoBehaviour
    {
        [Header("Engage")]
        [Tooltip("Hit probability at point-blank range.")]
        [Range(0f, 1f)] public float hitChance = 0.7f;
        [Tooltip("Hit probability falls linearly to hitChance x farRangeFactor at this range (metres) and beyond.")]
        public float farRange = 1000f;
        [Range(0f, 1f)] public float farRangeFactor = 0.3f;
        [Tooltip("Seconds between shots. A miss can be followed by another shot at the same contact.")]
        public float engageCooldown = 1.5f;

        [Header("Key panel")]
        [Tooltip("Text size at 720p; scales with screen height.")]
        public int fontSize = 18;

        ContactSensor sensor;
        ScenarioSpawner spawner;
        System.Random rng;
        int run = -1;
        bool summaryPrinted;
        float lastShotTime = -999f;
        GUIStyle style;

        void Awake()
        {
            sensor = GetComponent<ContactSensor>();
        }

        void Update()
        {
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner == null) return;
            if (spawner.RunNumber != run)
            {
                run = spawner.RunNumber;
                rng = new System.Random(spawner.ActiveSeed ^ 0x5EED);   // hit rolls repeat with the scenario seed
                summaryPrinted = false;
            }
            Iff.Tick(ContactRegistry.ScenarioTime);      // IFF / Remote ID replies that are due
            if (spawner.Finished)
            {
                if (!summaryPrinted && ContactRegistry.Live.Count == 0) PrintSummary();   // wait for end-of-run despawns to be logged
                return;
            }

            var kb = Keyboard.current;
            if (kb == null || ContactRegistry.InputBlocked) return;
            if (kb.digit1Key.wasPressedThisFrame) Classify(PlayerCall.HostileQuad);
            if (kb.digit2Key.wasPressedThisFrame) Classify(PlayerCall.HostileFixedWing);
            if (kb.digit3Key.wasPressedThisFrame) Classify(PlayerCall.OwnIsrDrone);
            if (kb.digit4Key.wasPressedThisFrame) Classify(PlayerCall.CivilianDrone);
            if (kb.digit5Key.wasPressedThisFrame) Classify(PlayerCall.Bird);
            if (kb.hKey.wasPressedThisFrame) Act(PlayerAction.Hold);
            if (kb.rKey.wasPressedThisFrame) Act(PlayerAction.Report);
            if (kb.jKey.wasPressedThisFrame) Act(PlayerAction.Jam);
            if (kb.fKey.wasPressedThisFrame) Act(PlayerAction.Engage);
            if (kb.iKey.wasPressedThisFrame) Challenge();
        }

        // I: challenge the locked contact, or every selected one, for IFF / Remote ID. The answer comes back 0.6 to 1.4 s later.
        public bool Challenge()
        {
            var targets = sensor.Targets;
            if (targets.Count == 0) return false;
            if (!RunConditions.IffAid) { sensor.Say(Iff.NotAvailable); return false; }
            bool any = false;
            foreach (var c in targets) if (Iff.Ask(c)) any = true;
            return any;
        }

        // Both apply to every contact in the sensor's selection, or to the locked contact when nothing is selected.
        // Each contact is still recorded and scored on its own. Returns true if at least one contact took it.
        public bool Classify(PlayerCall call)
        {
            var targets = sensor.Targets;
            bool group = targets.Count > 1, any = false;
            foreach (var c in targets)
                if (c != null && c.Classify(call, Vector3.Distance(transform.position, c.transform.position), group)) any = true;
            return any;
        }

        // LAN team mode: a sentry reports, the commander decides. Everywhere else every seat may do everything.
        public const string SentryRefusal = "SENTRY: report it (R). The commander decides.";
        public static bool RoleAllows(Chaukanna.Net.SeatRole role, PlayerAction action)
        {
            return role != Chaukanna.Net.SeatRole.Sentry || action == PlayerAction.Report || action == PlayerAction.None;
        }

        public bool Act(PlayerAction action)
        {
            if (sensor == null) sensor = GetComponent<ContactSensor>();     // Awake has not run on a scratch object (self-tests)
            if (!RoleAllows(RunConditions.SeatRole, action)) { sensor.Say(SentryRefusal, 2f); return false; }
            var targets = sensor.Targets;
            if (targets.Count == 0) return false;
            // One trigger pull is one salvo: every contact in the group gets a shot, then the cooldown runs.
            if (action == PlayerAction.Engage)
            {
                if (Time.time - lastShotTime < engageCooldown) return false;
            }
            bool group = targets.Count > 1, any = false;
            foreach (var c in targets.ToArray())
                if (c != null && ActOn(c, action, group)) any = true;
            if (any && action == PlayerAction.Engage) lastShotTime = Time.time;
            return any;
        }

        // LAN team mode, on the host: a trainee's jam or shot on the shared sky. The host's effectors decide what happens
        // (whether the jam works, whether the shot hits). Only the physical effect is applied: the host seat's own record never
        // gets a call or an action for it (that would score the host for the trainee's decision). When the effect lands the
        // record is marked handled instead (handledBy / handledTime / handledAction, a history line), and every PC is told so
        // that no other seat's clock runs out on a contact a teammate already took out. Returns false when there was nothing to act on.
        public bool RemoteAct(Contact c, PlayerCall call, PlayerAction action, string seat, ulong fromId, out bool locked, out bool hit, out string result)
        {
            locked = hit = false;
            result = "";
            if (c == null) return false;
            if (string.IsNullOrEmpty(seat)) seat = "Trainee";
            if (action == PlayerAction.Jam)
            {
                var motion = c.GetComponent<ContactMotion>();
                if (motion != null && motion.Jammed) { result = "already jammed"; locked = true; return true; }
                bool affected = c.Definition.silhouette == Silhouette.Quad;
                result = affected ? "link lost, descending" : "no effect";
                locked = affected;
                c.Data.Log($"{seat}: Jam, {result}");
                if (affected && motion != null) { motion.Jam(); MarkHandled(c, seat, action, fromId); }
                return true;
            }
            if (action == PlayerAction.Engage)
            {
                if (rng == null) rng = new System.Random(spawner != null ? spawner.ActiveSeed ^ 0x5EED : 1);
                Vector3 target = c.transform.position;
                float range = Vector3.Distance(transform.position, target);
                float p = hitChance * Mathf.Lerp(1f, farRangeFactor, Mathf.Clamp01(range / farRange));
                hit = rng.NextDouble() < p;
                result = $"{(hit ? "hit" : "miss")} (p {p:F2}, {range:F0} m)";
                locked = true;
                c.Data.Log($"{seat}: Engage, {result}");
                PlaceholderFx.Tracer(transform.position + Vector3.down * 0.3f, target);
                if (hit)
                {
                    PlaceholderFx.Burst(target, c.Definition.size * 4f);
                    MarkHandled(c, seat, action, fromId);
                    c.Despawn("EngagedHit");
                }
                return true;
            }
            return false;
        }

        // First seat to take a contact out gets the mark; everyone is told right away.
        void MarkHandled(Contact c, string seat, PlayerAction action, ulong fromId)
        {
            if (c.Data.handledTime < 0f)
            {
                c.Data.handledBy = seat;
                c.Data.handledTime = ContactRegistry.ScenarioTime;
                c.Data.handledAction = action.ToString();
            }
            Chaukanna.Net.LanSession.HostHandled(c, seat, action, fromId);
        }

        bool ActOn(Contact c, PlayerAction action, bool group)
        {
            if (c.Call == PlayerCall.None) return false;
            // LAN team mode on a trainee's PC: jams and shots go to the host, which owns the sky; the answer comes back.
            if (Chaukanna.Net.LanSession.TeamClient && (action == PlayerAction.Jam || action == PlayerAction.Engage))
            {
                Chaukanna.Net.LanSession.SendDecision(c, action);
                if (action == PlayerAction.Jam) return c.Act(action, "sent to the jammer", group, false);
                return true;    // the shot is recorded when the host says whether it hit
            }
            // Swarm triage: how near the post this member was, among its swarm, when it was first acted on.
            if (c.Data.swarmId >= 0 && c.Data.swarmRank < 0 && (action == PlayerAction.Jam || action == PlayerAction.Engage))
            {
                var motion = c.GetComponent<ContactMotion>();
                if (motion != null && motion.Group != null) c.Data.swarmRank = motion.Group.RankByDistance(motion);
            }
            switch (action)
            {
                case PlayerAction.Hold:
                case PlayerAction.Report:
                    bool acted = c.Act(action, null, group);
                    // LAN team mode: the report goes up to the commander(s). A repeat of the same call is ignored there, a changed call is sent again.
                    if (action == PlayerAction.Report && (acted || c.Data.action == PlayerAction.Report)) Chaukanna.Net.LanSession.SendReport(c, c.Call);
                    return acted;

                case PlayerAction.Jam:
                    // Only the RF-controlled quads respond; fixed-wings and birds carry on. A jam that worked cannot be taken back.
                    bool affected = c.Definition.silhouette == Silhouette.Quad;
                    if (!c.Act(action, affected ? "link lost, descending" : "no effect", group, affected)) return false;
                    if (affected)
                    {
                        c.GetComponent<ContactMotion>().Jam();
                        Chaukanna.Net.LanSession.HostHandled(c, Chaukanna.Scoring.ScoreKeeper.PlayerName, action);
                    }
                    return true;

                case PlayerAction.Engage:
                    Vector3 target = c.transform.position;
                    float range = Vector3.Distance(transform.position, target);
                    float p = hitChance * Mathf.Lerp(1f, farRangeFactor, Mathf.Clamp01(range / farRange));
                    bool hit = rng.NextDouble() < p;
                    if (!c.Shot(hit, $"{(hit ? "hit" : "miss")} (p {p:F2}, {range:F0} m)", group)) return false;
                    PlaceholderFx.Tracer(transform.position + Vector3.down * 0.3f, target);
                    if (hit)
                    {
                        PlaceholderFx.Burst(target, c.Definition.size * 4f);
                        Chaukanna.Net.LanSession.HostHandled(c, Chaukanna.Scoring.ScoreKeeper.PlayerName, action);
                        c.Despawn("EngagedHit");
                    }
                    return true;
            }
            return false;
        }

        void PrintSummary()
        {
            summaryPrinted = true;
            int unresolved = 0;
            foreach (var r in ContactRegistry.Records)
            {
                if (!r.Actioned) unresolved++;
                Debug.Log($"[Decision] #{r.id:00} true={r.truthClass} call={r.call.Label()} action={(r.Actioned ? r.action.ToString() : "UNRESOLVED AT END")}"
                    + (string.IsNullOrEmpty(r.actionResult) ? "" : $" ({r.actionResult})")
                    + $" | spawn {T(r.spawnTime)} inView {T(r.firstInViewTime)} lock {T(r.firstLockTime)} call {T(r.callTime)} action {T(r.actionTime)} gone {T(r.despawnTime)} {r.despawnReason}");
            }
            Debug.Log($"[Decision] {ContactRegistry.Records.Count} contacts, {ContactRegistry.Records.Count - unresolved} resolved, {unresolved} unresolved at end.");
        }

        static string T(float seconds)
        {
            return seconds < 0f ? "-" : seconds.ToString("F1");
        }

        void OnGUI()
        {
            if (PlayerView.HideOverlays) return;
            GUI.BeginGroup(PlayerView.Rect);
            DrawGui();
            GUI.EndGroup();
        }

        void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || spawner == null || spawner.Finished) return;
            // The call can always be changed. The action can be changed unless it physically happened.
            var targets = sensor.Targets;
            var c = targets.Count == 1 ? targets[0] : null;
            bool many = targets.Count > 1;
            bool canClassify = targets.Count > 0;
            bool canAct = many || (c != null && c.Call != PlayerCall.None && c.CanChangeAction);
            bool canFire = many || (c != null && c.Call != PlayerCall.None && (c.CanChangeAction || c.CanFireAgain));
            float ui = PlayerView.Height / 720f;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = false, richText = true };
            style.fontSize = Mathf.RoundToInt(fontSize * ui);
            style.normal.textColor = Color.white;

            string on = "#FFFFFF", off = "#FFFFFF55";
            var seat = RunConditions.SeatRole;
            bool sentrySeat = seat == Chaukanna.Net.SeatRole.Sentry;
            string text =
                $"<color={(canClassify ? on : off)}>CLASSIFY\n1 Hostile quad\n2 Hostile fixed-wing\n3 Own ISR drone\n4 Civilian drone\n5 Bird</color>\n\n" +
                (sentrySeat
                    // A sentry reports; hold, jam and engage are the commander's (greyed).
                    ? $"ACTION\n<color={off}>H Hold</color>\n<color={(canAct ? on : off)}>R Report</color>\n<color={off}>J Jam\nF Engage</color>"
                    : $"<color={(canAct ? on : off)}>ACTION\nH Hold\nR Report\nJ Jam</color>\n" +
                      $"<color={(canFire ? on : off)}>F Engage{(c != null && c.CanFireAgain ? " (fire again)" : "")}</color>");
            if (many) text += $"\n\nGROUP of {targets.Count}";
            else if (c != null && c.Resolved) text += $"\n\n#{c.Id:00} resolved\n" + (c.CanChangeAction ? "any key changes it" : "1-5 changes the call");
            else if (c != null && c.OnHold) text += $"\n\n#{c.Id:00} held: R / J / F";
            text += "\n\n<color=#FFFFFFAA>I check IFF / Remote ID\nHold Tab + drag: lasso\nCtrl+click add to group\nG all in view   X clear</color>";
            if (seat == Chaukanna.Net.SeatRole.Commander) text += "\n<color=#FFD060>T slew to the newest report\nShift+T the next one</color>";
            if (seat != Chaukanna.Net.SeatRole.Any) text = $"<b>ROLE: {Chaukanna.Net.NetPlayer.RoleName(seat)}</b>\n" + text;
            if (ViewRigSwitch.HeadsetActive) text = "<b>HEADSET</b>  (F7 desktop)\nRight trigger: lock\nLeft trigger: binoculars\nStick click / A: call menu\nX: read tablet   Menu: pause\n\n" + text;
            else if (ViewRigSwitch.VrActive) text = "<b>VR MODE</b>  (F7 desktop)\nB or both buttons: binoculars\nQ: read the tablet\nHold E: call / action menu\nLeft click: lock\n\n" + text;

            var content = new GUIContent(text);
            Vector2 size = style.CalcSize(content);
            float pad = 8f * ui;
            var box = new Rect(PlayerView.Width - size.x - pad * 3f, PlayerView.Height - size.y - pad * 3f, size.x + pad * 2f, size.y + pad * 2f);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(new Rect(box.x + pad, box.y + pad, size.x, size.y), content, style);
        }
    }
}
