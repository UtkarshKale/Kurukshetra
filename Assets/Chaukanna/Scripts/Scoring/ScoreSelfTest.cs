using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Scoring
{
    // Hand-built cases for ScoreCalculator. Stands in for EditMode tests because the Test Framework package is not in the project.
    // Run from the ScoreKeeper component's context menu, or via the CLI: Chaukanna.Scoring.ScoreSelfTest.Run()
    public static class ScoreSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var rules = RuleSet.Defaults();

            // 1. Never in view: missed, 0 points, later nodes skipped.
            var missed = One(Rec(ContactClass.HostileQuad, 10f, -1f), rules);
            Check("missed contact scores 0", missed.total == 0 && missed.nodes.Count == 1 && !missed.detected);

            // 2. Seen in 2 s, right call, Jam under Weapons Hold: 3 + 3 + 2.
            var good = One(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.WeaponsHold), rules);
            Check("fast + right call + right action = 8", good.total == 8);

            // 3. Detection bands: 7 s = 2, 15 s = 1.
            Check("7 s detection = 2", One(Rec(ContactClass.Bird, 10f, 17f), rules).nodes[0].points == 2);
            Check("15 s detection = 1", One(Rec(ContactClass.Bird, 10f, 25f), rules).nodes[0].points == 1);

            // 4. Wrong call: 0 for the class node; the action node is still judged on truth.
            var wrong = One(Rec(ContactClass.FriendlyISR, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Report, RoeCard.WeaponsHold), rules);
            Check("wrong call = 0, report on own = +2", wrong.nodes[1].points == 0 && wrong.nodes[2].points == 2 && wrong.total == 5);

            // 5. Near-miss only beyond 600 m.
            var far = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.Bird); far.callRange = 800f;
            var near = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.Bird); near.callRange = 300f;
            Check("quad called bird at 800 m = near-miss 1", One(far, rules).nodes[1].points == 1);
            Check("quad called bird at 300 m = wrong 0", One(near, rules).nodes[1].points == 0);

            // 6. Engage an own drone under Weapons Hold: -5.
            var own = One(Rec(ContactClass.FriendlyISR, 10f, 12f, PlayerCall.OwnIsrDrone, PlayerAction.Engage, RoeCard.WeaponsHold), rules);
            Check("engage own drone under hold = -5", own.nodes[2].points == -5 && !own.nodes[2].passed);

            // 7. Hold on a hostile under Under Attack: -5.
            var held = One(Rec(ContactClass.HostileFixedWing, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Hold, RoeCard.UnderAttack), rules);
            Check("hold on hostile under attack = -5", held.nodes[2].points == -5);

            // 8. ROE flip mid-run: the same action scores by the card in force when it was taken.
            var before = One(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.WeaponsHold), rules);
            var after = One(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.UnderAttack), rules);
            Check("engage hostile under hold = 0, flagged", before.nodes[2].points == 0 && before.nodes[2].reason.Contains("engaged under hold"));
            Check("engage hostile after flip to attack = +2", after.nodes[2].points == 2 && after.nodes[2].passed);

            // 9. Unresolved: 0 for the action node and flagged.
            var open = One(Rec(ContactClass.Bird, 10f, 12f, PlayerCall.Bird), rules);
            Check("no action = 0 and unresolved", open.nodes[2].points == 0 && open.unresolved && open.total == 6);

            // 10. Spawned in the last 30 s and never seen: not scored. Run ends at 300 s.
            var late = ScoreCalculator.Calculate(new[] { Rec(ContactClass.Bird, 285f, -1f) }, rules, 300f);
            Check("late spawn not scored", late.notScored == 1 && late.scoredContacts == 0 && late.missed == 0);

            // 11. Session metrics over a small mixed set.
            var session = ScoreCalculator.Calculate(new[]
            {
                Rec(ContactClass.HostileQuad, 0f, 2f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.WeaponsHold),         // 8
                Rec(ContactClass.CivilianQuad, 0f, 8f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.WeaponsHold),     // 2 + 0 - 5
                Rec(ContactClass.Bird, 0f, 20f, PlayerCall.Bird),                                                             // 1 + 3 + 0
                Rec(ContactClass.HostileFixedWing, 0f, -1f),                                                                  // missed
            }, rules, 300f);
            Check("session total = 9", session.totalPoints == 9);
            Check("average detection = 10 s", Mathf.Approximately(session.averageDetectionSeconds, 10f));
            Check("classification accuracy = 66.7 %", Mathf.Abs(session.classificationAccuracy - 66.67f) < 0.1f);
            Check("engagement decision score = 50 %", Mathf.Approximately(session.engagementDecisionScore, 50f));
            Check("counts: 1 missed, 1 unresolved, 1 false engagement, 1 missed hostile",
                session.missed == 1 && session.unresolved == 1 && session.falseEngagements == 1 && session.missedHostiles == 1);
            Check("confusion: civilian quad called hostile quad", session.confusion[3 * session.confusionColumns.Length + 0] == 1);

            // 13. Engage that missed twice and then hit: one decision, three attempts, judged by the card at the first shot.
            var multi = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.UnderAttack);
            multi.shots = 3; multi.engageHit = true;
            var multiResult = One(multi, rules);
            Check("engage x3 then hit = +2 with 3 attempts", multiResult.nodes[2].points == 2 && multiResult.engageAttempts == 3 && multiResult.engageHit && multiResult.path.Contains("Engage x3 hit"));

            // 14. Engage that only ever missed: the decision still counts (not unresolved), but the contact can be fired on again.
            var missedShot = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.UnderAttack);
            missedShot.shots = 2; missedShot.engageHit = false;
            var missedResult = One(missedShot, rules);
            Check("engage with only misses is actioned, not unresolved", !missedResult.unresolved && missedResult.nodes[2].points == 2 && missedShot.Actioned && !missedShot.Resolved);

            // 15. Report is final at once; Hold is not.
            var reportRec = Rec(ContactClass.Bird, 10f, 12f, PlayerCall.Bird, PlayerAction.Report);
            Check("report resolves immediately", reportRec.Actioned && reportRec.Resolved && reportRec.Decided);
            var holdRec = Rec(ContactClass.Bird, 10f, 12f, PlayerCall.Bird, PlayerAction.Hold);
            holdRec.holds = 1;
            Check("hold is actioned but not final", holdRec.Actioned && !holdRec.Resolved && holdRec.OnHold && !holdRec.Decided);

            // 17. Hold on a hostile, then Jam: the latest action is scored (+2, not the 0 a Hold would get).
            var heldThenJam = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.WeaponsHold);
            heldThenJam.holds = 1;
            var heldThenJamResult = One(heldThenJam, rules);
            Check("hold then jam scores the jam (+2) and notes the hold", heldThenJamResult.nodes[2].points == 2 && heldThenJamResult.holds == 1 && heldThenJamResult.path.Contains("after 1 hold"));
            // 18. Held 40 s under Under Attack, then engaged: +2 for the engage, -2 for two full 15 s blocks of delay.
            var lateEngage = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Engage, RoeCard.UnderAttack);
            lateEngage.holds = 1; lateEngage.holdStartTime = 14f; lateEngage.holdSeconds = 40f; lateEngage.holdUnderAttackSeconds = 40f; lateEngage.shots = 1; lateEngage.engageHit = true;
            var lateResult = One(lateEngage, rules);
            Check("late engage after 40 s hold under attack = +2 - 2", lateResult.nodes[2].points == 0 && lateResult.holdPenalty == -2 && lateResult.nodes[2].passed);
            lateEngage.holdUnderAttackSeconds = 200f;
            Check("hold-delay penalty is capped at -3", One(lateEngage, rules).holdPenalty == -3);
            // Same delay under Weapons Hold costs nothing: no time was spent under Under Attack.
            var calmHold = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.WeaponsHold);
            calmHold.holds = 1; calmHold.holdSeconds = 60f; calmHold.holdUnderAttackSeconds = 0f;
            Check("hold under weapons hold has no delay penalty", One(calmHold, rules).holdPenalty == 0 && One(calmHold, rules).nodes[2].points == 2);
            // Non-hostiles never get the delay penalty.
            var birdHold = Rec(ContactClass.Bird, 10f, 12f, PlayerCall.Bird, PlayerAction.Report, RoeCard.UnderAttack);
            birdHold.holds = 1; birdHold.holdUnderAttackSeconds = 60f;
            Check("no delay penalty on a non-hostile", One(birdHold, rules).holdPenalty == 0);
            Check("decision latency is call to action", Mathf.Approximately(lateResult.decisionLatency, 1f));

            var stillHeld = One(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Hold, RoeCard.UnderAttack), rules);
            Check("still holding a hostile under attack at the end = -5", stillHeld.nodes[2].points == -5);

            // 16. AI hint bookkeeping (data only, no points).
            var followedWrong = Rec(ContactClass.CivilianQuad, 10f, 12f, PlayerCall.HostileQuad);
            followedWrong.aiHint = PlayerCall.HostileQuad; followedWrong.aiConfidence = 90; followedWrong.aiHintWrong = true;
            var overrode = Rec(ContactClass.CivilianQuad, 10f, 12f, PlayerCall.CivilianDrone);
            overrode.aiHint = PlayerCall.Bird; overrode.aiConfidence = 80; overrode.aiHintWrong = true;
            var followedRight = Rec(ContactClass.Bird, 10f, 12f, PlayerCall.Bird);
            followedRight.aiHint = PlayerCall.Bird; followedRight.aiConfidence = 70;
            var noHint = Rec(ContactClass.Bird, 10f, -1f);
            var ai = ScoreCalculator.Calculate(new[] { followedWrong, overrode, followedRight, noHint }, rules, 300f);
            Check("AI counts: 3 shown, 2 wrong, 1 followed wrong, 1 overrode correctly",
                ai.aiHintsShown == 3 && ai.wrongAiHints == 2 && ai.followedWrongHint == 1 && ai.overrodeWrongHintCorrectly == 1);
            Check("AI per-contact flags", ai.contacts[0].followedAi && !ai.contacts[0].followedAiRight && !ai.contacts[1].followedAi && ai.contacts[2].followedAiRight);
            Check("AI hints do not change points", ai.contacts[0].total == One(Rec(ContactClass.CivilianQuad, 10f, 12f, PlayerCall.HostileQuad), rules).total);

            // 12. Data-only rule: engaging a manned aircraft is -10 under any card.
            var manned = rules.Find(RoeCard.UnderAttack, ContactCategory.Manned, PlayerAction.Engage);
            Check("manned aircraft engage rule = -10", manned != null && manned.points == -10);

            string summary = $"[ScoreSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static ContactResult One(ContactRecord r, RuleSet rules)
        {
            return ScoreCalculator.Calculate(new[] { r }, rules, 300f).contacts[0];
        }

        // detectable = when the detection clock starts; inView < 0 = never seen.
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
