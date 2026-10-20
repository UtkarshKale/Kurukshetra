using System.Collections.Generic;
using Kurukshetra.Scenario;
using UnityEngine;

namespace Kurukshetra.Scoring
{
    // Hand-built cases for the full scoring tree (doc §5.4): the decision clock and its release points, correct rejection,
    // search / perception failures, N1b and report bands, light-dependent near-miss, unverified engagement, process judged on
    // the player's belief, outcome, the process x outcome grid, and the two signal-detection analyses.
    // ScoreSelfTest covers the classic tree. CLI: Kurukshetra.Scoring.FullScoringSelfTest.Run()
    public static class FullScoringSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var rules = RuleSet.Defaults();
            Check("full scoring is on by default, with release points for both hostile classes",
                rules.fullScoring && rules.Release(ContactClass.HostileQuad) != null && rules.Release(ContactClass.HostileFixedWing) != null);

            // --- The clock. A strike quad runs in: 900 m, 400 m, 140 m (inside the 150 m release range at 30 s), 50 m.
            var jamInTime = Approach(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.CardA));
            jamInTime.actionTime = 25f; jamInTime.actionLocked = true;   // the jam took effect
            var a = One(jamInTime, rules);
            Check("clock opens when it comes into view (12 s), runs out at the release point (30 s)", Mathf.Approximately(a.clockOpen, 12f) && Mathf.Approximately(a.deadline, 30f) && a.deadlineReason.Contains("release"));
            Check("jammed at 25 s, before release: no miss, sound (+2)", !a.clockMiss && a.nodes[2].points == 2 && a.severity == "Sound");

            var jamLate = Approach(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.CardA));
            jamLate.actionTime = 35f; jamLate.actionLocked = true;
            var b = One(jamLate, rules, RoeCard.CardA);   // the miss is judged on the card in force at the deadline
            Check("jammed at 35 s, after release: a MISS, Critical under Card A (-5), says it was too late", b.clockMiss && b.severity == "Critical" && b.nodes[2].points == -5 && b.nodes[2].reason.Contains("too late"));
            Check("a miss is judged as a Critical process (score 0) and an unsound decision", b.processSeverity == "Critical" && b.processScore == 0 && (b.grid == "Unsound" || b.grid == "Lucky"));

            var held = Approach(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Hold, RoeCard.CardB));
            var c1 = ScoreCalculator.Calculate(new[] { held }, rules, 300f, 0, null, RoeCard.CardB).contacts[0];
            Check("holding a strike quad past release under Card B: a MISS, Major (-3)", c1.clockMiss && c1.severity == "Major" && c1.nodes[2].points == -3 && c1.actBranch == "Clock ran out");

            var flip = new[] { new RoeChange { time = 28f, from = RoeCard.CardB, to = RoeCard.CardA } };
            var c2 = ScoreCalculator.Calculate(new[] { Approach(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Hold, RoeCard.CardB)) }, rules, 300f, 0, flip, RoeCard.CardB).contacts[0];
            Check("the miss costs what the card in force at the deadline says (B > A at 28 s, deadline 30 s: Critical)", c2.clockMiss && c2.severity == "Critical");

            var reported = Approach(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Report, RoeCard.CardC));
            reported.actionTime = 20f;
            var c3 = One(reported, rules);
            Check("reporting a strike quad before release under Card C is a decision, and sound", !c3.clockMiss && c3.severity == "Sound");

            // A recce drone that loiters and never comes near: the clock runs out 120 s after it opened.
            var loiter = Rec(ContactClass.HostileFixedWing, 10f, 12f, PlayerCall.HostileFixedWing);
            loiter.rangeTrack.Add(new Vector2(12f, 900f)); loiter.rangeTrack.Add(new Vector2(200f, 800f));
            var c4 = One(loiter, rules);
            Check("loitering recce: deadline 120 s after the clock opened (132 s), then a MISS", Mathf.Approximately(c4.deadline, 132f) && c4.clockMiss && c4.deadlineReason.Contains("loiter"));
            var c5 = ScoreCalculator.Calculate(new[] { loiter }, rules, 100f).contacts[0];
            Check("...but before the deadline (run at 100 s) it is still unresolved, not a miss", !c5.clockMiss && c5.nodes[2].reason == "UNRESOLVED" && c5.grid == "No decision");

            var reachedPost = Rec(ContactClass.HostileFixedWing, 10f, 12f);
            reachedPost.despawnTime = 60f; reachedPost.despawnReason = "ReachedDefendedPoint";
            Check("a hostile with no range samples that reached the post: deadline is when it got there", Mathf.Approximately(One(reachedPost, rules).deadline, 60f));

            var called = Rec(ContactClass.Bird, 10f, 12f);
            called.callTime = 8f;
            Check("the clock opens at the call when the call came first", Mathf.Approximately(ScoreCalculator.ClockOpen(called), 8f));

            // --- Non-hostiles: correct rejection.
            var own = One(Rec(ContactClass.FriendlyISR, 10f, 12f, PlayerCall.OwnIsrDrone), rules);
            Check("own drone left alone for 30 s in view: correct rejection, +2, 'Left alone', sound and successful",
                own.correctRejection && own.nodes[2].points == 2 && own.actBranch == "Left alone" && own.grid == "Sound, successful" && own.outcome == "Clear" && Mathf.Approximately(own.deadline, 42f));
            var leftEarly = Rec(ContactClass.CivilianQuad, 10f, 12f);
            leftEarly.despawnTime = 20f; leftEarly.despawnReason = "ExitedSector";
            var c6 = One(leftEarly, rules);
            Check("civil drone that left at 20 s: deadline 20 s ('it left'), correct rejection", Mathf.Approximately(c6.deadline, 20f) && c6.deadlineReason == "it left" && c6.correctRejection);
            var birdReported = One(Rec(ContactClass.Bird, 10f, 12f, PlayerCall.Bird, PlayerAction.Report), rules);
            Check("bird reported: also a correct rejection, scored by the report rule (+2), branch stays 'Report'", birdReported.correctRejection && birdReported.nodes[2].points == 2 && birdReported.actBranch == "Report");
            var ownEngaged = One(Rec(ContactClass.FriendlyISR, 10f, 12f, PlayerCall.OwnIsrDrone, PlayerAction.Engage, RoeCard.CardB), rules);
            Check("own drone engaged: never a correct rejection", !ownEngaged.correctRejection && ownEngaged.nodes[2].points == -5);

            // --- N1: search and perception failures, bands.
            var unseen = Rec(ContactClass.HostileQuad, 10f, -1f);
            var c7 = One(unseen, rules);
            Check("detectable but never brought into view: search failure", c7.missKind.StartsWith("search") && c7.detBranch == "Missed");
            var unmarked = Rec(ContactClass.HostileQuad, 10f, 12f);
            unmarked.firstLockTime = -1f;
            Check("in view, never marked by the deadline: perception failure", One(Approach(unmarked), rules).missKind.StartsWith("perception"));
            Check("N1b band: marked 3 s after in view = full, 7 s = partial, 15 s = late",
                Band(3f, rules) == "full" && Band(7f, rules) == "partial" && Band(15f, rules) == "late");
            var rep = Rec(ContactClass.Bird, 10f, 12f, PlayerCall.Bird);
            rep.callTime = 12f + 10f;
            Check("report band: called 10 s after the mark = partial", One(rep, rules).reportBand == "partial" && Mathf.Approximately(One(rep, rules).reportLatency, 10f));

            // --- N2 near-miss depends on the light.
            var nightCall = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.Bird);
            nightCall.callRange = 400f; nightCall.callLight = (int)TimeOfDay.Night;
            var dayCall = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.Bird);
            dayCall.callRange = 400f; dayCall.callLight = (int)TimeOfDay.Day;
            var classic = RuleSet.Defaults(); classic.fullScoring = false;
            Check("quad called bird at 400 m: near-miss at night (1), wrong by day (0), wrong with full scoring off",
                One(nightCall, rules).nodes[1].points == 1 && One(dayCall, rules).nodes[1].points == 0 && One(nightCall, classic).nodes[1].points == 0);

            // --- Unverified engagement: the AI cue was the only evidence.
            var cueOnlyA = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.CardA);
            cueOnlyA.aiHint = PlayerCall.HostileQuad; cueOnlyA.actionTime = 15f; cueOnlyA.shots = 1; cueOnlyA.engageHit = true;
            var cueOnlyB = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.CardB);
            cueOnlyB.aiHint = PlayerCall.HostileQuad; cueOnlyB.actionTime = 15f;
            var looked = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.CardB);
            looked.aiHint = PlayerCall.HostileQuad; looked.opticsVerified = true; looked.actionTime = 15f;
            var uA = One(cueOnlyA, rules); var uB = One(cueOnlyB, rules); var uL = One(looked, rules);
            Check("unverified engagement: Minor (-1) under Card A, Major (-3) under Card B, none after a look through the binoculars",
                uA.unverified && uA.nodes[2].points == 2 - 1 && uB.unverified && uB.nodes[2].points == 2 - 3 && uB.processSeverity == "Major" && !uL.unverified && uL.nodes[2].points == 2);

            // --- N3 process on the player's belief, N4 outcome, the grid.
            var friendlyFire = Rec(ContactClass.FriendlyISR, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.CardA);
            friendlyFire.shots = 1; friendlyFire.engageHit = true; friendlyFire.despawnTime = 15f; friendlyFire.despawnReason = "EngagedHit";
            var ff = One(friendlyFire, rules);
            Check("own drone the player believed was a strike quad, engaged under Card A: process sound (on belief), outcome fratricide = 'Sound, unlucky'",
                ff.processSeverity == "Sound" && ff.processScore == 100 && ff.outcome == "Fratricide" && ff.grid == "Sound, unlucky" && ff.outcomeBranch == "False engagement");
            var lucky = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.Bird, PlayerAction.Engage, RoeCard.CardB);
            lucky.shots = 1; lucky.engageHit = true; lucky.actionTime = 15f;
            var lk = One(lucky, rules);
            Check("believed a bird, engaged it under Card B (Major) and it was a hostile, now down: 'Lucky'", lk.processSeverity == "Major" && lk.outcome == "Neutralised" && lk.grid == "Lucky");
            var leaked = Approach(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Hold, RoeCard.CardA));
            leaked.despawnTime = 41f; leaked.despawnReason = "ReachedDefendedPoint";
            var lkd = One(leaked, rules, RoeCard.CardA);
            Check("held a strike quad until it reached the post: miss, outcome 'Leaked', 'Unsound'", lkd.clockMiss && lkd.outcome == "Leaked" && lkd.grid == "Unsound");

            var noEffect = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.CardA);
            noEffect.actionResult = "no effect";
            Check("jammed with no effect and still flying: result not known yet ('Pending'), not counted as luck", One(noEffect, rules, RoeCard.CardA).grid == "Pending");

            // --- Signal detection (log-linear correction, 20-trial gate).
            var sdt = SdtResult.From(15, 20, 5, 20, 20);
            Check("d' and c: 15/20 hits, 5/20 false alarms gives d' 1.27, c 0.00, shown", Mathf.Abs(sdt.dPrime - 1.274f) < 0.01f && Mathf.Abs(sdt.c) < 0.001f && sdt.gated);
            var trigger = SdtResult.From(18, 20, 10, 20, 20);
            Check("many false alarms gives a negative c (trigger-happy)", trigger.c < -0.3f);
            Check("fewer than 20 of either: not shown (insufficient data)", !SdtResult.From(15, 19, 5, 20, 20).gated && SdtResult.From(15, 19, 5, 20, 20).Describe("x").Contains("insufficient"));
            Check("probit: z(0.5) = 0, z(0.975) = 1.96", Mathf.Abs(SdtResult.Probit(0.5)) < 1e-4f && Mathf.Abs(SdtResult.Probit(0.975) - 1.95996f) < 1e-3f);

            // --- Session roll-up.
            var session = ScoreCalculator.Calculate(new[] { jamInTime, jamLate, OwnAlone(), friendlyFire, cueOnlyA, unseen }, rules, 300f);
            Check("session: 1 clock miss, 1 correct rejection, 1 unverified engagement, 1 search failure",
                session.clockMisses == 1 && session.correctRejections == 1 && session.unverifiedEngagements == 1 && session.searchFailures == 1);
            Check("session: grid counts add up and the process score is the mean of the decided contacts",
                session.gridSoundSuccessful + session.gridSoundUnlucky + session.gridLucky + session.gridUnsound == 5 && session.processScore > 0f && session.processScore < 100f);
            Check("the maximum per contact is unchanged by full scoring (8)", session.maxPoints == 6 * 8);

            // --- Off switch: the classic tree is untouched.
            var offRec = Approach(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad));
            var off = One(offRec, classic);
            Check("full scoring off: a hostile past release with no action is just UNRESOLVED (0), no miss", !off.clockMiss && off.nodes[2].reason == "UNRESOLVED" && off.nodes[2].points == 0);
            Check("full scoring off: own drone left alone is unresolved, not a correct rejection", !One(Rec(ContactClass.FriendlyISR, 10f, 12f, PlayerCall.OwnIsrDrone), classic).correctRejection);

            // --- The range track itself.
            var track = new ContactRecord();
            track.SampleRange(1f, new Vector3(300f, 50f, 400f), Vector3.zero);
            track.SampleRange(2f, new Vector3(60f, 80f, 80f), Vector3.zero);
            Check("range samples are ground range (500 m, 100 m); first within 150 m at 2 s", Mathf.Approximately(track.rangeTrack[0].y, 500f) && Mathf.Approximately(track.closestRange, 100f) && Mathf.Approximately(track.FirstWithin(150f), 2f) && track.FirstWithin(50f) < 0f);

            string summary = $"[FullScoringSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static ContactRecord OwnAlone() => Rec(ContactClass.FriendlyISR, 10f, 12f, PlayerCall.OwnIsrDrone);

        static string Band(float lockAfter, RuleSet rules)
        {
            var r = Rec(ContactClass.Bird, 10f, 12f, PlayerCall.Bird);
            r.firstLockTime = 12f + lockAfter;
            r.callTime = r.firstLockTime + 1f;
            return One(r, rules).perceptionBand;
        }

        // A run-in on the post: inside the 150 m release range at 30 s.
        static ContactRecord Approach(ContactRecord r)
        {
            r.rangeTrack.Add(new Vector2(10f, 900f));
            r.rangeTrack.Add(new Vector2(20f, 400f));
            r.rangeTrack.Add(new Vector2(30f, 140f));
            r.rangeTrack.Add(new Vector2(40f, 50f));
            return r;
        }

        static ContactResult One(ContactRecord r, RuleSet rules, RoeCard startCard = RoeCard.WeaponsHold)
        {
            return ScoreCalculator.Calculate(new[] { r }, rules, 300f, 0, null, startCard).contacts[0];
        }

        static ContactRecord Rec(ContactClass truth, float detectable, float inView,
            PlayerCall call = PlayerCall.None, PlayerAction action = PlayerAction.None, RoeCard roe = RoeCard.WeaponsHold)
        {
            return new ContactRecord
            {
                id = 1,
                truthClass = truth,
                spawnTime = detectable,
                firstDetectableTime = detectable,
                firstInViewTime = inView,
                firstLockTime = inView,
                call = call,
                callTime = call == PlayerCall.None ? -1f : inView + 1f,
                callRange = 400f,
                action = action,
                actionTime = action == PlayerAction.None ? -1f : inView + 2f,
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
