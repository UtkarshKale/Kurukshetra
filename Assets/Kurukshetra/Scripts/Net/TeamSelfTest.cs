using System.Collections.Generic;
using Kurukshetra.Player;
using Kurukshetra.Scenario;
using Kurukshetra.Scoring;
using UnityEngine;

namespace Kurukshetra.Net
{
    // Hand-built cases for LAN team play (MP-1) that need no network: the sentry > commander scoring chain, a teammate's kill
    // not counting as a clock miss, the hand-off flag, the sentry's refusal in the decision loop, the new messages and the seat
    // roles in the start packet. ScoreSelfTest and FullScoringSelfTest are untouched: role scoring only applies when the seat
    // has a role. CLI: Kurukshetra.Net.TeamSelfTest.Run()
    public static class TeamSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var rules = RuleSet.Defaults();
            Check("the commander's minimum time to act on a report defaults to 8 s", Mathf.Approximately(rules.commanderMinSeconds, 8f));

            // --- (a) The sentry seat: the job is to report. A strike quad's deadline is 30 s (inside 150 m of the post).
            var inTime = One(Approach(Rec(ContactClass.HostileQuad, PlayerAction.Report, RoeCard.CardA, 20f)), rules, SeatRole.Sentry);
            Check("sentry, hostile reported at 20 s (deadline 30 s): the node is REPORT, sound (+2), no miss, lock-to-report 8 s",
                inTime.nodes[2].label == "REPORT" && inTime.nodes[2].points == 2 && inTime.severity == "Sound" && !inTime.clockMiss && Mathf.Approximately(inTime.sentryReportLatency, 8f));
            var late = One(Approach(Rec(ContactClass.HostileQuad, PlayerAction.Report, RoeCard.CardA, 40f)), rules, SeatRole.Sentry);
            Check("sentry, hostile reported at 40 s, after the deadline: a clock miss priced as a hold (Critical under Card A, -5)",
                late.clockMiss && late.severity == "Critical" && late.nodes[2].points == -5 && late.nodes[2].reason.Contains("too late"));
            var missing = One(Approach(Rec(ContactClass.HostileQuad, PlayerAction.None, RoeCard.CardA, -1f)), rules, SeatRole.Sentry);
            Check("sentry, hostile never reported: a clock miss (-5), branch 'Clock ran out'", missing.clockMiss && missing.nodes[2].points == -5 && missing.actBranch == "Clock ran out");
            var ownReported = One(Rec(ContactClass.FriendlyISR, PlayerAction.Report, RoeCard.CardA, 20f, PlayerCall.OwnIsrDrone), rules, SeatRole.Sentry);
            var falseReport = One(Rec(ContactClass.FriendlyISR, PlayerAction.Report, RoeCard.CardA, 20f, PlayerCall.HostileQuad), rules, SeatRole.Sentry);
            Check("sentry, own drone reported as an own drone: sound (+2); reported with a HOSTILE call: a false report, Minor (0)",
                ownReported.nodes[2].points == 2 && ownReported.severity == "Sound" && falseReport.severity == "Minor" && falseReport.nodes[2].points == 0 && falseReport.nodes[2].reason.Contains("FALSE REPORT") && !falseReport.correctRejection);
            var classic = One(Approach(Rec(ContactClass.HostileQuad, PlayerAction.Report, RoeCard.CardA, 20f)), rules, SeatRole.Any);
            Check("the same report with no role is scored the classic way (a hostile reported under Card A is Critical, -5): role scoring is off", classic.nodes[2].label == "ACT" && classic.nodes[2].points == -5);

            // --- (b) The commander seat.
            var jam = Approach(Rec(ContactClass.HostileQuad, PlayerAction.Jam, RoeCard.CardA, 25f));
            jam.actionLocked = true; jam.reportReceivedTime = 15f; jam.reportedBy = "S-014"; jam.reportedCall = PlayerCall.HostileQuad;
            var jamSession = Session(jam, rules, SeatRole.Commander, RoeCard.CardA);
            var cj = jamSession.contacts[0];
            Check("commander, report at 15 s then Jam at 25 s: report-to-decision 10 s, sound (+2); the session mean and count carry it",
                Mathf.Approximately(cj.commanderDelay, 10f) && cj.nodes[2].points == 2 && Mathf.Approximately(jamSession.commanderDelayMean, 10f) && jamSession.commanderDelayCount == 1 && jamSession.seatRole == "COMMANDER");

            var lateReport = Approach(Rec(ContactClass.HostileQuad, PlayerAction.None, RoeCard.CardA, -1f));
            lateReport.reportReceivedTime = 28f; lateReport.firstLockTime = 29f; lateReport.reportedBy = "S-014";   // arrived 2 s before the deadline, locked after it
            var waived = One(lateReport, rules, SeatRole.Commander);
            Check("commander, report reached them at 28 s (deadline 30 s, minimum 8 s) and no own lock before it: miss waived, 0 points, first fault the sentry",
                waived.upstreamWaiver && !waived.clockMiss && waived.nodes[2].points == 0 && waived.firstFault == "sentry" && waived.nodes[2].reason.Contains("upstream: report came too late (S-014)") && waived.actBranch == "Report too late");
            var ownLock = Approach(Rec(ContactClass.HostileQuad, PlayerAction.None, RoeCard.CardA, -1f));
            ownLock.reportReceivedTime = 28f; ownLock.firstLockTime = 12f;      // they had it locked at 12 s: their own view
            var notWaived = One(ownLock, rules, SeatRole.Commander);
            Check("commander with their own lock from before the report: no waiver, the miss stands (-5), first fault the commander",
                !notWaived.upstreamWaiver && notWaived.clockMiss && notWaived.nodes[2].points == -5 && notWaived.firstFault == "commander");
            var earlyReport = Approach(Rec(ContactClass.HostileQuad, PlayerAction.None, RoeCard.CardA, -1f));
            earlyReport.reportReceivedTime = 15f; earlyReport.firstLockTime = 16f;
            Check("commander, report at 15 s (15 s to act) and no decision: no waiver, a miss, first fault the commander", One(earlyReport, rules, SeatRole.Commander).clockMiss && One(earlyReport, rules, SeatRole.Commander).firstFault == "commander");

            var alone = EngageOnReport(RoeCard.CardB, rules, SeatRole.Commander);
            var aloneC = EngageOnReport(RoeCard.CardC, rules, SeatRole.Commander);
            Check("commander engaged a hostile on a report alone: Minor under Card B (0 instead of +2), Major under Card C (-3), says so",
                alone.reportAlone && alone.severity == "Minor" && alone.nodes[2].points == 0 && alone.nodes[2].reason.Contains("report alone") && aloneC.reportAlone && aloneC.severity == "Major" && aloneC.nodes[2].points == -3);
            var verified = EngageOnReport(RoeCard.CardB, rules, SeatRole.Commander, optics: true);
            var iffd = EngageOnReport(RoeCard.CardC, rules, SeatRole.Commander, iff: true);
            var ownFirst = EngageOnReport(RoeCard.CardC, rules, SeatRole.Commander, lockBefore: true);
            var underA = EngageOnReport(RoeCard.CardA, rules, SeatRole.Commander);
            Check("...but not with a look through the binoculars, an IFF challenge, an own lock from before the report, or under Card A (all sound, +2)",
                !verified.reportAlone && verified.nodes[2].points == 2 && !iffd.reportAlone && iffd.nodes[2].points == 2 && !ownFirst.reportAlone && ownFirst.nodes[2].points == 2 && !underA.reportAlone && underA.nodes[2].points == 2);

            // --- (c) A teammate's kill is not my clock miss.
            var taken = Approach(Rec(ContactClass.HostileQuad, PlayerAction.None, RoeCard.CardA, -1f));
            taken.handledBy = "S-014"; taken.handledTime = 20f; taken.handledAction = "Jam"; taken.despawnTime = 35f; taken.despawnReason = "JammedDown";
            var tSession = Session(taken, rules, SeatRole.Any, RoeCard.CardA);
            var t = tSession.contacts[0];
            Check("hostile jammed by a teammate at 20 s (deadline 30 s), this seat did nothing: not a miss, 0 points, 'handled by S-014 at 20.0s', branch 'Handled by team', Neutralised",
                t.handled && !t.clockMiss && t.nodes[2].points == 0 && t.nodes[2].reason.Contains("handled by S-014 at 20.0s") && t.actBranch == "Handled by team" && t.outcome == "Neutralised" && t.grid == "Sound, successful");
            Check("...and it is not counted as unresolved or as a missed hostile; the session counts it as handled by the team", tSession.unresolved == 0 && tSession.missedHostiles == 0 && tSession.handledByTeam == 1);
            var tooLate = Approach(Rec(ContactClass.HostileQuad, PlayerAction.None, RoeCard.CardA, -1f));
            tooLate.handledBy = "S-014"; tooLate.handledTime = 32f; tooLate.handledAction = "Jam";
            Check("...but a teammate's jam AFTER the deadline (32 s) does not undo this seat's miss", One(tooLate, rules, SeatRole.Any).clockMiss);
            var own = Rec(ContactClass.FriendlyISR, PlayerAction.None, RoeCard.CardA, -1f, PlayerCall.OwnIsrDrone);
            own.handledBy = "S-020"; own.handledTime = 20f; own.handledAction = "Engage"; own.despawnTime = 21f; own.despawnReason = "EngagedHit";
            var ownShot = One(own, rules, SeatRole.Sentry);
            Check("own drone shot down by another seat: 0 points for this seat (its fault is not mine), outcome stays 'Fratricide' for the team AAR", ownShot.handled && ownShot.nodes[2].points == 0 && ownShot.outcome == "Fratricide");
            var decidedAnyway = Approach(Rec(ContactClass.HostileQuad, PlayerAction.Jam, RoeCard.CardA, 25f));
            decidedAnyway.actionLocked = true; decidedAnyway.handledBy = "S-014"; decidedAnyway.handledTime = 20f;
            Check("a seat that did decide in time is scored on its own decision, not marked handled", !One(decidedAnyway, rules, SeatRole.Any).handled && One(decidedAnyway, rules, SeatRole.Any).nodes[2].points == 2);

            // --- (d) Hand-off fratricide.
            var shot = Rec(ContactClass.FriendlyISR, PlayerAction.Engage, RoeCard.CardA, 20f, PlayerCall.HostileQuad);
            shot.shots = 1; shot.engageHit = true; shot.actionLocked = true; shot.reportedCall = PlayerCall.HostileQuad; shot.reportedBy = "S-014"; shot.reportReceivedTime = 15f;
            var f = Session(shot, rules, SeatRole.Commander, RoeCard.CardA);
            Check("commander engaged an own drone that a sentry reported as hostile: hand-off fratricide flagged, named in the reason, counted",
                f.contacts[0].handoffFratricide && f.contacts[0].nodes[2].reason.Contains("HAND-OFF FRATRICIDE") && f.handoffFratricides == 1 && !One(shot, rules, SeatRole.Any).handoffFratricide);

            // --- (e) Role refusal in the decision loop.
            Check("a sentry may Report but not Hold, Jam or Engage; commander and any seat may do everything",
                DecisionLoop.RoleAllows(SeatRole.Sentry, PlayerAction.Report) && !DecisionLoop.RoleAllows(SeatRole.Sentry, PlayerAction.Hold) && !DecisionLoop.RoleAllows(SeatRole.Sentry, PlayerAction.Jam) && !DecisionLoop.RoleAllows(SeatRole.Sentry, PlayerAction.Engage)
                && DecisionLoop.RoleAllows(SeatRole.Commander, PlayerAction.Jam) && DecisionLoop.RoleAllows(SeatRole.Any, PlayerAction.Engage));
            Check("a sentry's H / J / F show the refusal 'SENTRY: report it (R). The commander decides.' for 2 s; a commander's J does not", RefusalShown());

            // --- (f) Messages.
            var rm = JsonUtility.FromJson<ReportMsg>(JsonUtility.ToJson(new ReportMsg { id = 14, call = (int)PlayerCall.HostileQuad, seat = "S-014", seatId = 2, time = 12.5f, range = 640f, bearing = 40f }));
            Check("ReportMsg round trip (contact, call, seat, host time, range, bearing)", rm.id == 14 && rm.call == (int)PlayerCall.HostileQuad && rm.seat == "S-014" && rm.seatId == 2 && Mathf.Approximately(rm.time, 12.5f) && Mathf.Approximately(rm.range, 640f) && Mathf.Approximately(rm.bearing, 40f));
            var cp = JsonUtility.FromJson<CrowdPickMsg>(JsonUtility.ToJson(new CrowdPickMsg { index = 2301 }));
            var hm = JsonUtility.FromJson<HandledMsg>(JsonUtility.ToJson(new HandledMsg { id = 9, action = (int)PlayerAction.Jam, by = "S-020", time = 31.5f }));
            Check("CrowdPickMsg and HandledMsg round trip", cp.index == 2301 && hm.id == 9 && hm.action == (int)PlayerAction.Jam && hm.by == "S-020" && Mathf.Approximately(hm.time, 31.5f));
            var sm = JsonUtility.FromJson<SpawnMsg>(JsonUtility.ToJson(new SpawnMsg { id = 7, truth = (int)ContactClass.FriendlyISR, crowdIndex = 1234 }));
            var planned = JsonUtility.FromJson<SpawnMsg>(JsonUtility.ToJson(new SpawnMsg { id = 8 }));
            Check("SpawnMsg carries the crowd index (1234), and a planned contact's is -1", sm.crowdIndex == 1234 && planned.crowdIndex == -1);

            // --- (g) The seat roles travel with the roster and the start packet.
            var roster = new NetRoster { mode = NetMode.Team };
            roster.players.Add(new NetPlayer { clientId = 0, name = "Trainer", host = true, role = (int)SeatRole.Commander });
            roster.players.Add(new NetPlayer { clientId = 1, name = "S-014", role = (int)SeatRole.Sentry });
            roster.players.Add(new NetPlayer { clientId = 2, name = "S-020", role = (int)SeatRole.Any });
            var packet = new StartPacket { mode = NetMode.Team, players = roster.players };
            var back = JsonUtility.FromJson<StartPacket>(JsonUtility.ToJson(packet));
            Check("seat roles survive the start packet (commander, sentry, any)", back.players.Count == 3 && back.players[0].Role == SeatRole.Commander && back.players[1].Role == SeatRole.Sentry && back.players[2].Role == SeatRole.Any && back.mode == NetMode.Team);
            var noCommander = new NetRoster { mode = NetMode.Team };
            noCommander.players.Add(new NetPlayer { clientId = 0, host = true, role = (int)SeatRole.Sentry });
            noCommander.players.Add(new NetPlayer { clientId = 1, role = (int)SeatRole.Sentry });
            Check("the Lobby's warning: a team with only sentries has no commander, one commander (or an ANY seat) is enough", !noCommander.HasCommander() && roster.HasCommander() && NetPlayer.RoleName(SeatRole.Sentry) == "SENTRY");

            // --- (h) A report is stamped on the record; the board keeps one line per contact.
            var rec = new ContactRecord { id = 14 };
            LanSession.StampReport(rec, new ReportMsg { id = 14, call = (int)PlayerCall.HostileQuad, seat = "S-014" }, 18f);
            LanSession.StampReport(rec, new ReportMsg { id = 14, call = (int)PlayerCall.HostileFixedWing, seat = "S-020" }, 25f);
            Check("a record keeps the FIRST report's time (18 s) and the latest call and seat", Mathf.Approximately(rec.reportReceivedTime, 18f) && rec.reportedCall == PlayerCall.HostileFixedWing && rec.reportedBy == "S-020");
            ReportBoard.Clear();
            ReportBoard.Add(new ReportMsg { id = 1, call = (int)PlayerCall.Bird, seat = "A" });
            ReportBoard.Add(new ReportMsg { id = 2, call = (int)PlayerCall.HostileQuad, seat = "B" });
            ReportBoard.Add(new ReportMsg { id = 1, call = (int)PlayerCall.HostileQuad, seat = "A" });
            Check("the board has one line per contact, a re-report updates it and moves it up", ReportBoard.Entries.Count == 2 && ReportBoard.Entries[1].id == 1 && ReportBoard.Entries[1].call == (int)PlayerCall.HostileQuad);
            ReportBoard.Clear();

            string summary = $"[TeamSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        // A sentry's J: the loop refuses and the sensor says why; a commander's J says nothing. Built on a scratch camera.
        static bool RefusalShown()
        {
            var go = new GameObject("TeamSelfTest camera");
            var saved = RunConditions.SeatRole;
            try
            {
                go.AddComponent<Camera>().enabled = false;
                var sensor = go.AddComponent<ContactSensor>();
                var loop = go.AddComponent<DecisionLoop>();
                RunConditions.SeatRole = SeatRole.Sentry;
                bool refusedH = !loop.Act(PlayerAction.Hold);
                bool saidH = sensor.Said == DecisionLoop.SentryRefusal;
                bool refusedJ = !loop.Act(PlayerAction.Jam);
                bool refusedF = !loop.Act(PlayerAction.Engage);
                RunConditions.SeatRole = SeatRole.Commander;
                sensor.Say("", 0f);
                loop.Act(PlayerAction.Jam);
                bool commanderQuiet = sensor.Said != DecisionLoop.SentryRefusal;
                return refusedH && saidH && refusedJ && refusedF && commanderQuiet && DecisionLoop.SentryRefusal == "SENTRY: report it (R). The commander decides.";
            }
            finally
            {
                RunConditions.SeatRole = saved;
                Object.DestroyImmediate(go);
            }
        }

        // Engaged (hit) at 25 s on a report that reached the commander at 15 s. lockBefore = they had it locked at 14 s already.
        static ContactResult EngageOnReport(RoeCard card, RuleSet rules, SeatRole role, bool optics = false, bool iff = false, bool lockBefore = false)
        {
            var r = Approach(Rec(ContactClass.HostileQuad, PlayerAction.Engage, card, 25f));
            r.shots = 1; r.engageHit = true; r.actionLocked = true;
            r.reportReceivedTime = 15f; r.reportedBy = "S-014"; r.reportedCall = PlayerCall.HostileQuad;
            r.firstLockTime = lockBefore ? 14f : 16f;
            r.opticsVerified = optics; r.iffChecked = iff;
            return One(r, rules, role, card);
        }

        // Ground range to the post: inside the 150 m release range at 30 s, so a strike quad's deadline is 30 s.
        static ContactRecord Approach(ContactRecord r)
        {
            r.rangeTrack.Add(new Vector2(10f, 900f));
            r.rangeTrack.Add(new Vector2(20f, 400f));
            r.rangeTrack.Add(new Vector2(30f, 140f));
            r.rangeTrack.Add(new Vector2(40f, 50f));
            return r;
        }

        static SessionResult Session(ContactRecord r, RuleSet rules, SeatRole role, RoeCard startCard = RoeCard.CardA)
        {
            return ScoreCalculator.Calculate(new[] { r }, rules, 300f, 0, null, startCard, role);
        }

        static ContactResult One(ContactRecord r, RuleSet rules, SeatRole role, RoeCard startCard = RoeCard.CardA)
        {
            return Session(r, rules, role, startCard).contacts[0];
        }

        // Detectable at 10 s, in view and locked at 12 s, called right at 13 s (unless call is given), acted at actionTime (-1 = never).
        static ContactRecord Rec(ContactClass truth, PlayerAction action, RoeCard roe, float actionTime, PlayerCall call = PlayerCall.None)
        {
            if (call == PlayerCall.None) call = ScoreCalculator.RightCall(truth);
            return new ContactRecord
            {
                id = 1,
                truthClass = truth,
                spawnTime = 10f,
                firstDetectableTime = 10f,
                firstInViewTime = 12f,
                firstLockTime = 12f,
                call = call,
                callTime = 13f,
                callRange = 400f,
                action = action,
                actionTime = action == PlayerAction.None ? -1f : actionTime,
                roeAtAction = roe,
            };
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
