using System.Collections.Generic;
using Kurukshetra.Scenario;
using UnityEngine;

namespace Kurukshetra.Scoring
{
    // Hand-built cases for ScoreCalculator. Stands in for EditMode tests because the Test Framework package is not in the project.
    // Run from the ScoreKeeper component's context menu, or via the CLI: Kurukshetra.Scoring.ScoreSelfTest.Run()
    public static class ScoreSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();
            var rules = RuleSet.Defaults();
            rules.fullScoring = false;   // these are the classic tree (detect, classify, action); FullScoringSelfTest covers the rest

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

            // 19. Changing a call. Before any final action it is free: only the latest call counts.
            var freeChange = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.WeaponsHold);
            freeChange.callChanges = 1; freeChange.callBeforeChange = PlayerCall.Bird;
            Check("changing the call before acting costs nothing", One(freeChange, rules).nodes[1].points == 3 && One(freeChange, rules).changePenalty == 0);
            // Wrong to right after a final action, nothing new to go on: full credit minus the second-guess penalty.
            var secondGuess = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Report, RoeCard.WeaponsHold);
            secondGuess.callChanges = 1; secondGuess.callRevisions = 1; secondGuess.lastCallChangeWasRevision = true; secondGuess.callBeforeChange = PlayerCall.CivilianDrone;
            var secondGuessResult = One(secondGuess, rules);
            Check("wrong to right on a second guess = 3 - 1", secondGuessResult.nodes[1].points == 2 && secondGuessResult.nodes[1].passed && secondGuessResult.changePenalty == -1 && secondGuessResult.classBranch == "Second guess");
            // Right to wrong after a final action: loses the credit and takes the penalty.
            var toWrong = Rec(ContactClass.Bird, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Report, RoeCard.WeaponsHold);
            toWrong.callChanges = 1; toWrong.callRevisions = 1; toWrong.lastCallChangeWasRevision = true; toWrong.callBeforeChange = PlayerCall.Bird;
            var toWrongResult = One(toWrong, rules);
            Check("right to wrong = 0 - 1", toWrongResult.nodes[1].points == -1 && !toWrongResult.nodes[1].passed && toWrongResult.classBranch == "Changed to wrong");
            // Wrong to another wrong: just wrong.
            var stillWrong = Rec(ContactClass.Bird, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Report, RoeCard.WeaponsHold);
            stillWrong.callChanges = 1; stillWrong.callRevisions = 1; stillWrong.lastCallChangeWasRevision = true; stillWrong.callBeforeChange = PlayerCall.CivilianDrone;
            Check("wrong to another wrong = 0, no extra penalty", One(stillWrong, rules).nodes[1].points == 0);

            // 20. Blend-in hostile. Called own drone, then re-called hostile after it broke pattern: full credit plus the bonus.
            var caught = Rec(ContactClass.HostileFixedWing, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Report, RoeCard.WeaponsHold);
            caught.deceptive = true; caught.anomalyTime = 40f; caught.firstCall = PlayerCall.OwnIsrDrone;
            caught.callChanges = 1; caught.callRevisions = 1; caught.lastCallChangeWasRevision = true; caught.callBeforeChange = PlayerCall.OwnIsrDrone; caught.callChangeInformed = true;
            var caughtResult = One(caught, rules);
            Check("blend-in caught after the anomaly = 3 + 1, no second-guess penalty", caughtResult.nodes[1].points == 4 && caughtResult.deceptionCaught && caughtResult.changePenalty == 0 && caughtResult.classBranch == "Caught deception");
            // The same correction made on a held contact (no final action yet) earns the bonus too.
            var caughtOnHold = Rec(ContactClass.HostileFixedWing, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Jam, RoeCard.WeaponsHold);
            caughtOnHold.deceptive = true; caughtOnHold.anomalyTime = 40f; caughtOnHold.callChanges = 1; caughtOnHold.callBeforeChange = PlayerCall.OwnIsrDrone; caughtOnHold.callChangeInformed = true;
            Check("blend-in watched on hold and re-called after the anomaly = 3 + 1", One(caughtOnHold, rules).nodes[1].points == 4);
            // Re-called hostile BEFORE it gave itself away: a second guess that happened to be right.
            var guessed = Rec(ContactClass.HostileFixedWing, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Report, RoeCard.WeaponsHold);
            guessed.deceptive = true; guessed.callChanges = 1; guessed.callRevisions = 1; guessed.lastCallChangeWasRevision = true; guessed.callBeforeChange = PlayerCall.OwnIsrDrone;
            Check("blend-in re-called with no anomaly yet = 3 - 1", One(guessed, rules).nodes[1].points == 2 && !One(guessed, rules).deceptionCaught);
            // Never corrected: wrong, and flagged as deceived.
            var fooled = Rec(ContactClass.HostileFixedWing, 10f, 12f, PlayerCall.OwnIsrDrone, PlayerAction.Report, RoeCard.WeaponsHold);
            fooled.deceptive = true; fooled.anomalyTime = 40f;
            var fooledResult = One(fooled, rules);
            Check("blend-in left as own drone = 0 and flagged deceived", fooledResult.nodes[1].points == 0 && fooledResult.classBranch == "Deceived" && fooledResult.nodes[1].reason.Contains("not corrected"));
            // Called right the first time: plain full credit.
            var sharp = Rec(ContactClass.HostileFixedWing, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Report, RoeCard.WeaponsHold);
            sharp.deceptive = true;
            Check("blend-in called hostile first time = 3", One(sharp, rules).nodes[1].points == 3);
            var deceptionSession = ScoreCalculator.Calculate(new[] { caught, fooled, sharp }, rules, 300f);
            Check("session counts blend-ins and catches", deceptionSession.deceptiveContacts == 3 && deceptionSession.deceptionsCaught == 1 && deceptionSession.decisionChanges == 1);

            // 21. Replacing a final action. With nothing new to go on it costs a point each time, capped.
            var swapped = Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.WeaponsHold);
            swapped.actionRevisions = 1; swapped.uninformedActionRevisions = 1;
            var swappedResult = One(swapped, rules);
            Check("report replaced by jam with nothing new = +2 - 1", swappedResult.nodes[2].points == 1 && swappedResult.nodes[2].passed && swappedResult.actionChanges == 1 && swappedResult.changePenalty == -1);
            swapped.actionRevisions = 5; swapped.uninformedActionRevisions = 5;
            Check("action-change penalty is capped at -2", One(swapped, rules).nodes[2].points == 0);
            // After new information (the anomaly, or a new ROE card) the change is a reaction: no penalty.
            var reacted = Rec(ContactClass.HostileFixedWing, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Engage, RoeCard.UnderAttack);
            reacted.actionRevisions = 1; reacted.uninformedActionRevisions = 0; reacted.shots = 1; reacted.engageHit = true;
            var reactedResult = One(reacted, rules);
            Check("action changed after new information = no penalty", reactedResult.nodes[2].points == 2 && reactedResult.changePenalty == 0 && reactedResult.nodes[2].reason.Contains("no penalty"));

            // 22. The contact itself: what can and cannot be changed.
            var go = new GameObject("SelfTestContact");
            var def = ScriptableObject.CreateInstance<ContactDefinition>();
            def.contactClass = ContactClass.HostileQuad;
            // The registry is shared with a run in progress: remember its state and put it back afterwards.
            float savedTime = ContactRegistry.ScenarioTime, savedRoeSince = ContactRegistry.RoeSince;
            int savedEvents = ContactRegistry.Events.Count, savedFeed = ContactRegistry.Feed.Count;
            ContactRegistry.RoeSince = 0f;
            ContactRegistry.ScenarioTime = 10f;
            var contact = go.AddComponent<Contact>();
            contact.Initialise(1, def, null);
            Check("no action is possible before a call", !contact.Act(PlayerAction.Report, null));
            Check("first call is taken and remembered", contact.Classify(PlayerCall.Bird, 300f) && contact.Data.firstCall == PlayerCall.Bird && contact.Data.callChanges == 0);
            Check("the same call again does nothing", !contact.Classify(PlayerCall.Bird, 300f));
            Check("report is taken", contact.Act(PlayerAction.Report, null) && contact.Resolved && contact.CanChangeAction);
            ContactRegistry.ScenarioTime = 20f;
            Check("call can be changed after acting, and counts as a revision",
                contact.Classify(PlayerCall.HostileQuad, 250f) && contact.Data.callRevisions == 1 && contact.Data.callBeforeChange == PlayerCall.Bird && contact.Data.lastCallChangeWasRevision && !contact.Data.callChangeInformed);
            Check("report can be replaced by a jam, counted as a second guess", contact.Act(PlayerAction.Jam, "link lost, descending", false, true) && contact.Action == PlayerAction.Jam && contact.Data.actionRevisions == 1 && contact.Data.uninformedActionRevisions == 1);
            Check("a jam that worked cannot be replaced", !contact.CanChangeAction && !contact.Act(PlayerAction.Report, null) && !contact.Shot(true, "hit"));
            Check("the call can still be corrected for the record", contact.Classify(PlayerCall.HostileFixedWing, 200f) && contact.Call == PlayerCall.HostileFixedWing);
            Check("every step is in the history", contact.Data.history.Count == 5 && contact.Data.history[2].Contains("changed call"));

            // A blend-in: the anomaly makes the later change informed, for the call and for the action.
            var go2 = new GameObject("SelfTestBlendIn");
            ContactRegistry.ScenarioTime = 5f;
            var blend = go2.AddComponent<Contact>();
            blend.Initialise(2, def, null, true);
            blend.Classify(PlayerCall.OwnIsrDrone, 800f, true);
            blend.Act(PlayerAction.Report, null, true);
            ContactRegistry.ScenarioTime = 30f;
            blend.MarkAnomaly("left its patrol orbit");
            ContactRegistry.ScenarioTime = 35f;
            blend.Classify(PlayerCall.HostileQuad, 500f);
            blend.Shot(false, "miss");
            Check("blend-in: call change after the anomaly is informed", blend.Data.deceptive && blend.Data.anomalyTime == 30f && blend.Data.callChangeInformed && blend.Data.group);
            Check("blend-in: report replaced by engage after the anomaly is not a second guess", blend.Action == PlayerAction.Engage && blend.Data.actionRevisions == 1 && blend.Data.uninformedActionRevisions == 0);
            Check("an engage that missed can be fired again but not replaced", blend.CanFireAgain && !blend.Act(PlayerAction.Report, null) && blend.Shot(true, "hit") && blend.Data.shots == 2);
            blend.Data.firstDetectableTime = 5f; blend.Data.firstInViewTime = 6f;      // it was seen
            var blendScore = ScoreCalculator.Calculate(new[] { blend.Data }, rules, 300f).contacts[0];
            Check("blend-in end to end: caught, with its history in the result", blendScore.deceptionCaught && blendScore.history.Count == 6 && blendScore.group && blendScore.firstCall == PlayerCall.OwnIsrDrone.Label());
            ContactRegistry.Live.Remove(contact);
            ContactRegistry.Live.Remove(blend);
            ContactRegistry.Records.Remove(contact.Data);
            ContactRegistry.Records.Remove(blend.Data);
            ContactRegistry.Events.RemoveRange(savedEvents, ContactRegistry.Events.Count - savedEvents);
            ContactRegistry.Feed.RemoveRange(savedFeed, ContactRegistry.Feed.Count - savedFeed);
            ContactRegistry.ScenarioTime = savedTime;
            ContactRegistry.RoeSince = savedRoeSince;
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(go2);
            Object.DestroyImmediate(def);

            // 23. Never detectable (out of range, behind terrain or too small all its life): listed, not in the max.
            var hidden = Rec(ContactClass.HostileQuad, 10f, -1f);
            hidden.firstDetectableTime = -1f;
            var hiddenResult = One(hidden, rules);
            Check("never-detectable contact is not scored and says why", !hiddenResult.scored && hiddenResult.notScoredReason == "not detectable" && hiddenResult.detBranch == "Not detectable" && hiddenResult.path.Contains("not detectable"));
            var mixed = ScoreCalculator.Calculate(new[] { hidden, Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.WeaponsHold), Rec(ContactClass.Bird, 10f, -1f) }, rules, 300f);
            Check("never-detectable is left out of the max; detectable-but-missed still counts", mixed.maxPoints == 16 && mixed.totalPoints == 8 && mixed.notDetectable == 1 && mixed.missed == 1 && mixed.scoredContacts == 2);
            Check("a never-detectable hostile is not a missed hostile", mixed.missedHostiles == 0);
            var strict = RuleSet.Defaults();
            strict.fullScoring = false;
            strict.excludeNeverDetectable = false;
            var strictResult = ScoreCalculator.Calculate(new[] { hidden }, strict, 300f);
            Check("with the rule off it counts as missed", strictResult.maxPoints == 8 && strictResult.missed == 1 && strictResult.notDetectable == 0);
            Check("seen without the detectable time set is still scored (clock from spawn)", One(Rec(ContactClass.Bird, 10f, 15f), rules).scored);

            // 24. Cards A / B / C: the severity matrix (doc §5.4.4).
            var ownUnderB = One(Rec(ContactClass.FriendlyISR, 10f, 12f, PlayerCall.OwnIsrDrone, PlayerAction.Engage, RoeCard.CardB), rules);
            Check("engaging an own drone under Card B is Critical (-5)", ownUnderB.severity == "Critical" && ownUnderB.nodes[2].points == -5 && !ownUnderB.nodes[2].passed);
            var heldStrikeA = One(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Hold, RoeCard.CardA), rules);
            Check("holding a strike drone under Card A is Critical", heldStrikeA.severity == "Critical" && heldStrikeA.nodes[2].points == -5);
            var reconEngagedC = One(Rec(ContactClass.HostileFixedWing, 10f, 12f, PlayerCall.HostileFixedWing, PlayerAction.Engage, RoeCard.CardC), rules);
            Check("engaging a recce drone under Card C is Major (escalation, -3)", reconEngagedC.severity == "Major" && reconEngagedC.nodes[2].points == -3);
            var jamStrikeB = One(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.CardB), rules);
            Check("jamming a strike drone under Card B is sound (+2)", jamStrikeB.severity == "Sound" && jamStrikeB.nodes[2].passed && jamStrikeB.nodes[2].points == 2 && jamStrikeB.total == 8);
            Check("every lettered card has a row for every threat group", rules.cards.Count == 3 && rules.cards.TrueForAll(cd => cd.rows.Count == 6));
            Check("legacy cards still score as before", One(Rec(ContactClass.HostileQuad, 10f, 12f, PlayerCall.HostileQuad, PlayerAction.Jam, RoeCard.WeaponsHold), rules).nodes[2].points == 2);

            // 25. Card-change node: B > A at 100 s. Reporting a recce drone is sound under B but Major under A: stale.
            var change = new RoeChange { time = 100f, from = RoeCard.CardB, to = RoeCard.CardA };
            var beforeChange = Rec(ContactClass.HostileFixedWing, 10f, 50f, PlayerCall.HostileFixedWing, PlayerAction.Report, RoeCard.CardB);       // before: not counted
            var withinGrace = Rec(ContactClass.HostileFixedWing, 10f, 100.5f, PlayerCall.HostileFixedWing, PlayerAction.Report, RoeCard.CardB);   // acted at 102.5: inside the 3 s grace
            var stale = Rec(ContactClass.HostileFixedWing, 10f, 118f, PlayerCall.HostileFixedWing, PlayerAction.Report, RoeCard.CardA);          // acted at 120
            var adapted = Rec(ContactClass.HostileFixedWing, 10f, 128f, PlayerCall.HostileFixedWing, PlayerAction.Engage, RoeCard.CardA);        // acted at 130
            stale.id = 3; adapted.id = 4;
            var cc = ScoreCalculator.CardChange(new[] { beforeChange, withinGrace, stale, adapted }, rules, change);
            Check("card change: 1 stale-card error, 2 decisions after the grace", cc.staleErrors == 1 && cc.decisionsAfter == 2);
            Check("card change: adapted 30 s after the change (first decision right under the new card)", Mathf.Approximately(cc.adaptationSeconds, 30f));
            Check("card change: 3 - 1 stale = 2 points of 3", cc.points == 2 && cc.maxPoints == 3);
            var noAdapt = ScoreCalculator.CardChange(new[] { stale }, rules, change);
            Check("card change: no decision under the new card costs more", noAdapt.adaptationSeconds < 0f && noAdapt.points == 0);
            var session3 = ScoreCalculator.Calculate(new[] { stale, adapted }, rules, 300f, 0, new[] { change });
            Check("card change is part of the session score and marks the stale contact", session3.cardChange.happened && session3.maxPoints == 2 * 8 + 3 && session3.contacts.Exists(x => x.id == 3 && x.staleCard));
            float r1 = Kurukshetra.Player.RoeBanner.RandomFlipTime(1234, 300f), r2 = Kurukshetra.Player.RoeBanner.RandomFlipTime(1234, 300f), r3 = Kurukshetra.Player.RoeBanner.RandomFlipTime(99, 300f);
            Check("random card change: same seed same time, within 30-70 % of the run", r1 == r2 && r1 >= 90f && r1 <= 210f && r3 >= 90f && r3 <= 210f && r1 != r3);

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
