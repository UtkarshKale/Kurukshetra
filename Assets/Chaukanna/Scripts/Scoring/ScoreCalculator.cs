using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using Chaukanna.Scenario;

namespace Chaukanna.Scoring
{
    [Serializable]
    public class NodeResult
    {
        public string label;    // DET / CLASS / ACT
        public int points;
        public bool passed;
        public string reason;
    }

    [Serializable]
    public class ContactResult
    {
        public int id;
        public string truthClass;
        public string call;
        public string action;
        public string roeAtAction;
        public bool scored = true;
        public string notScoredReason;
        public bool detected;
        public float detectionSeconds = -1f;
        public bool unresolved;
        public string aiHint;                       // empty = no hint was shown
        public int aiConfidence;
        public bool aiHintWrong;
        public bool followedAi;                     // final call matched the hint
        public bool followedAiRight;                // ...and the hint was right
        public string aiText;                       // AAR column
        public int holds;
        public float holdSeconds;                   // total time on hold; still counting if the run ended on hold
        public int holdPenalty;                     // hold-delay points, 0 or negative, already inside the ACT node
        public float decisionLatency = -1f;         // call to final action, seconds; -1 = no action
        public int engageAttempts;

        // Changing one's mind, and blend-in hostiles.
        public string firstCall = "";
        public int callChanges;
        public int actionChanges;
        public int changePenalty;                   // points lost to second-guessing, already inside the nodes
        public bool group;                          // decided as part of a multi-select
        public bool deceptive;                      // a hostile flying an own-drone patrol
        public float anomalyTime = -1f;             // when it gave itself away; -1 = it never did
        public bool deceptionCaught;                // re-called correctly after the anomaly
        public string severity = "";                // Sound / Minor / Major / Critical under a lettered card
        public bool staleCard;                      // right under the card before a change, wrong under the one in force

        // Full scoring (doc §5.4).
        public float n1aSeconds = -1f;              // search: could be seen > came into view
        public float n1bSeconds = -1f;              // perception: in view > locked (marked)
        public string perceptionBand = "";          // full / partial / late
        public float reportLatency = -1f;           // locked > called
        public string reportBand = "";
        public float clockOpen = -1f, deadline = -1f;   // decision clock, scenario seconds; -1 = no clock (never in view)
        public string deadlineReason = "";
        public string missKind = "";                // search failure (never in view) / perception failure (in view, never marked)
        public bool clockMiss;                      // hostile with no active decision by its release point
        public bool correctRejection;               // non-hostile left alone until it left
        public bool unverified;                     // engaged with the AI cue as the only evidence
        public string processSeverity = "";         // judged on what the player believed (their own call), not on truth
        public int processScore = -1;               // 100 minus penalty units; -1 = no decision
        public string outcome = "";                 // N4: Neutralised / Leaked / Not stopped / Tracked / Clear / Fratricide / Collateral / Pending
        public string grid = "";                    // Sound, successful / Sound, unlucky / Lucky / Unsound / Pending / No decision
        // Your call vs the sound call: the reference choice and its modelled consequence (an annotated replay, not a re-run).
        public string yourCall = "", soundCall = "", soundAction = "", consequence = "";
        public bool matchedSound;
        public List<string> history = new List<string>();

        // Which branch of the decision tree this contact took at each checkpoint (see DecisionTreeData).
        public string detBranch = "", classBranch = "", actBranch = "", outcomeBranch = "";

        // Timeline, scenario seconds; negative = never. For the dashboard.
        public float spawnTime = -1f, firstInViewTime = -1f, lockTime = -1f, callTime = -1f, actionTime = -1f, despawnTime = -1f;
        public bool engageHit;
        public List<NodeResult> nodes = new List<NodeResult>();
        public int total;
        public string path;     // one-line tree path for the log

        // LAN team play (MP-1). handled: a teammate's jam or hit took it out before this seat's clock ran out (0 points, not a
        // miss). Commander seat: report-to-decision delay, who was at fault for a miss, hand-off flags. Sentry seat: lock-to-report.
        public bool handled;
        public string handledBy = "";
        public string reportedBy = "", reportedCall = "";
        public float reportReceived = -1f;          // commander: when the sentry's report reached this seat
        public float commanderDelay = -1f;          // commander: report received > action; -1 = no report before an action
        public float sentryReportLatency = -1f;     // sentry: lock > report (R)
        public string firstFault = "";              // commander clock miss: "sentry" (report too late, waived) or "commander"
        public bool upstreamWaiver;                 // commander: the clock miss was waived, the report came too late
        public bool reportAlone;                    // commander: engaged under Card B / C on a report alone (no own lock, optics or IFF)
        public bool handoffFratricide;              // commander: engaged an own drone that a sentry had reported as hostile
    }

    [Serializable]
    public class SessionResult
    {
        public string timestamp;
        public int seed;
        public float duration;

        // Conditions the run was played under.
        public string timeOfDay = "Day";
        public string visibility = "Clear";
        public float nightVisionPercent;            // share of the run with night vision on
        public string difficulty = "";              // empty = no difficulty level applied
        public bool allowMovement;                  // the soldier could walk
        public string displayTier = "desktop";      // desktop, vr-sim or mixed: never pooled with each other
        public string terrainLayers = "";           // trees / grass / buildings: the mode and the data actually used (OSM, satellite estimate, procedural, off)
        public string blackBox = "";                // path of the black box (.bin) of this run, for the 3D replay
        public string voiceFolder = "";             // folder of the host's voice recording, if any
        public string mapCredit = "";               // set when OpenStreetMap data was shown
        public int comfortRating = -1;              // VR only: 1-5 from the after-run check, 0 = skipped, -1 = not asked
        public string comfortSickness = "";

        public int totalPoints;
        public int maxPoints;                       // best case for the scored contacts: 8 each (3 detect + 3 classify + 2 action)
        public float ScorePercent => maxPoints > 0 ? Mathf.Clamp(100f * totalPoints / maxPoints, 0f, 100f) : 0f;
        public float averageDetectionSeconds = -1f; // detected contacts only; -1 = none detected
        public float classificationAccuracy;        // % of detected contacts classified right
        public float engagementDecisionScore;       // % of actioned contacts with a correct action

        public int scoredContacts;
        public int notScored;
        public int notDetectable;                   // never detectable at all: listed, not in the max (see ScoringRules)
        public int detected;
        public int missed;
        public int unresolved;
        public int falseEngagements;                // engaged something that was not hostile
        public int missedHostiles;                  // hostiles with no action taken
        public int decisionChanges;                 // calls and actions changed after a final action
        public int groupDecisions;                  // contacts decided as part of a multi-select
        public int deceptiveContacts;               // blend-in hostiles in the run
        public int deceptionsCaught;                // ...re-called correctly after giving themselves away

        // AI assist, data only: no points attached.
        public int aiHintsShown;
        public int wrongAiHints;
        public int followedWrongHint;               // player's final call matched a wrong hint
        public int overrodeWrongHintCorrectly;      // hint was wrong, player's final call was right

        // Rows = truth, columns = call. Flattened row by row; the last column is "no call".
        public string[] confusionRows;
        public string[] confusionColumns;
        public int[] confusion;

        public List<ContactResult> contacts = new List<ContactResult>();
        public CardChangeResult cardChange = new CardChangeResult();

        public SwarmTriageResult swarm = new SwarmTriageResult();
        public GroupCallResult groupCall = new GroupCallResult();   // K key: the trainee's call on whole swarms / flocks

        // Full scoring roll-up.
        public float processScore = -1f;            // mean of the decided contacts' process scores
        public int gridSoundSuccessful, gridSoundUnlucky, gridLucky, gridUnsound;
        public int clockMisses, correctRejections, unverifiedEngagements;
        public int searchFailures, perceptionFailures;   // never brought into view / in view but never marked before the deadline
        public SdtResult reportSdt = new SdtResult(), engageSdt = new SdtResult();

        // Prevalence (module 5): what was asked for and what the run really had (doc V7), and Quiet-watch vigilance.
        // Who and what (module 12) and the Director (module 10): only reference packages with the Director off go on the board.
        public string playerId = "";
        public string packageId = "";
        public bool referencePackage, directorOn;
        public int sandbagFlags;
        public List<string> directorLog = new List<string>();
        // True-size display (module 6): the angular resolution this run was played at, and whether it is comparable.
        public float arcminPerPixel = -1f;
        public bool displayComparable;
        public string prevalence = "Custom";
        public float realisedHostileShare = -1f;
        public string prevalenceNote = "";
        public bool quietWatch, nullMission;
        public VigilanceResult vigilance = new VigilanceResult();

        // Dense sky (DS-1), data only: no points attached.
        public int crowdTracks, crowdPeak, crowdChecked, iffChallenges, crowdFriendliesHit;

        // LAN team play (MP-1). seatRole is empty when the run was not played with roles (solo, or a team seat with no role).
        public string seatRole = "";
        public int handledByTeam;                   // contacts a teammate took out before this seat's clock ran out
        public int sentryReports;                   // sentry: contacts reported
        public float sentryReportLatencyMean = -1f; // sentry: mean lock > report, seconds
        public int commanderDelayCount;             // commander: decisions that followed a report
        public float commanderDelayMean = -1f;      // commander: mean report received > decision, seconds ("Report to decision")
        public int upstreamWaivers, reportAloneEngagements, handoffFratricides;
    }

    // Vigilance over a watch (doc §5.4.8): the watch cut into thirds; hit rate (brought into view in time) and N1b
    // (in view to marked) per third, and their change from the first third to the last. Few contacts per third in one
    // session, so the trend is shown as "too few to say" until there are at least 2 contacts in every third.
    [Serializable]
    public class VigilanceResult
    {
        public int[] contacts = new int[3], hits = new int[3];
        public float[] hitRate = { -1f, -1f, -1f }, n1b = { -1f, -1f, -1f };
        public float hitChange, n1bChange;          // last third minus first third
        public bool enough;
        public string summary = "";
    }

    // Signal detection for one kind of decision: hit and false-alarm counts, d' (can tell them apart) and c (trigger-happy
    // below 0, hesitant above 0), with the log-linear correction; "gated" = enough trials to show them (doc §5.4.3).
    [Serializable]
    public class SdtResult
    {
        public int hits, signals, falseAlarms, noise;
        public float dPrime, c;
        public bool gated;

        public static SdtResult From(int hits, int signals, int falseAlarms, int noise, int minimumTrials)
        {
            var r = new SdtResult { hits = hits, signals = signals, falseAlarms = falseAlarms, noise = noise };
            float h = (hits + 0.5f) / (signals + 1f), f = (falseAlarms + 0.5f) / (noise + 1f);
            float zh = Probit(h), zf = Probit(f);
            r.dPrime = zh - zf;
            r.c = -0.5f * (zh + zf);
            r.gated = signals >= minimumTrials && noise >= minimumTrials;
            return r;
        }

        // Inverse of the standard normal distribution (Acklam's rational approximation, error below 1.2e-9).
        public static float Probit(double p)
        {
            p = Math.Max(1e-9, Math.Min(1 - 1e-9, p));
            double[] a = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
            double[] b = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
            double[] c = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
            double[] d = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };
            double q, x;
            if (p < 0.02425) { q = Math.Sqrt(-2 * Math.Log(p)); x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1); }
            else if (p > 1 - 0.02425) { q = Math.Sqrt(-2 * Math.Log(1 - p)); x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1); }
            else { q = p - 0.5; double rr = q * q; x = (((((a[0] * rr + a[1]) * rr + a[2]) * rr + a[3]) * rr + a[4]) * rr + a[5]) * q / (((((b[0] * rr + b[1]) * rr + b[2]) * rr + b[3]) * rr + b[4]) * rr + 1); }
            return (float)x;
        }

        public string Describe(string name)
        {
            string counts = $"{hits}/{signals} hits, {falseAlarms}/{noise} false alarms";
            return gated ? $"{name}: d' {dPrime:F2}, c {c:+0.00;-0.00;0.00} ({counts})" : $"{name}: insufficient data for d'/c ({counts}; needs 20 of each)";
        }
    }

    // Swarm triage at prototype size (doc §5.4.6): did the player act on the members nearest the post first, and how
    // many got through.
    [Serializable]
    public class SwarmTriageResult
    {
        public bool happened;
        public int size, seen, actedOn, nearestFirst, leakers;
        public float nearestFirstRate = -1f;    // share of first actions aimed at one of the two nearest members
        public float firstActionSeconds = -1f;  // first member in view > first jam / engage on any member
        public int points, maxPoints;
        public string summary = "";
    }

    // The card-change node (doc §5.4.5): how the player adapted when the ROE card changed mid-run.
    [Serializable]
    public class CardChangeResult
    {
        public bool happened;
        public string from = "", to = "";
        public float time = -1f;
        public float adaptationSeconds = -1f;   // first decision that was right under the new card where the cards disagree; -1 = none
        public int staleErrors;                 // decisions right under the old card but wrong under the new one
        public int decisionsAfter;
        public int points, maxPoints;
        public string summary = "";
    }

    // Decision-tree scorer: DETECTED? > CLASSIFIED RIGHT? > RIGHT ACTION FOR THE ROE CARD?
    // Pure data in, pure data out.
    public static class ScoreCalculator
    {
        static readonly ContactClass[] Rows =
        {
            ContactClass.HostileQuad, ContactClass.HostileFixedWing, ContactClass.FriendlyISR, ContactClass.CivilianQuad, ContactClass.Bird,
        };
        static readonly PlayerCall[] Columns =
        {
            PlayerCall.HostileQuad, PlayerCall.HostileFixedWing, PlayerCall.OwnIsrDrone, PlayerCall.CivilianDrone, PlayerCall.Bird, PlayerCall.None,
        };

        public static ContactCategory Category(ContactClass c)
        {
            switch (c)
            {
                case ContactClass.HostileQuad:
                case ContactClass.HostileFixedWing: return ContactCategory.Hostile;
                case ContactClass.FriendlyISR: return ContactCategory.Own;
                case ContactClass.CivilianQuad: return ContactCategory.Civilian;
                case ContactClass.Bird: return ContactCategory.Bird;
                default: return ContactCategory.Manned;   // reserved classes
            }
        }

        public static PlayerCall RightCall(ContactClass c)
        {
            switch (c)
            {
                case ContactClass.HostileQuad: return PlayerCall.HostileQuad;
                case ContactClass.HostileFixedWing: return PlayerCall.HostileFixedWing;
                case ContactClass.FriendlyISR: return PlayerCall.OwnIsrDrone;
                case ContactClass.CivilianQuad: return PlayerCall.CivilianDrone;
                case ContactClass.Bird: return PlayerCall.Bird;
                default: return PlayerCall.None;
            }
        }

        // The card in force at a moment, from the start card and the recorded changes.
        public static RoeCard CardAt(float time, RoeCard start, IList<RoeChange> changes)
        {
            var card = start;
            if (changes != null) foreach (var ch in changes) if (ch.time <= time) card = ch.to;
            return card;
        }

        static RoeCard startCardForRun;
        static IList<RoeChange> changesForRun;
        static Chaukanna.Net.SeatRole roleForRun;      // Any = classic scoring; Sentry / Commander = the team chain (doc §5.4.7)

        public static SessionResult Calculate(IList<ContactRecord> records, RuleSet rules, float runEndTime, int seed = 0, IList<RoeChange> roeChanges = null, RoeCard startCard = RoeCard.WeaponsHold,
            Chaukanna.Net.SeatRole role = Chaukanna.Net.SeatRole.Any, IList<GroupCallRecord> groupCalls = null)
        {
            startCardForRun = startCard;
            changesForRun = roeChanges;
            roleForRun = role;
            var s = new SessionResult
            {
                seed = seed,
                duration = runEndTime,
                confusionRows = Array.ConvertAll(Rows, r => r.ToString()),
                confusionColumns = Array.ConvertAll(Columns, c => c == PlayerCall.None ? "NoCall" : c.ToString()),
                confusion = new int[Rows.Length * Columns.Length],
            };

            float detectionSum = 0f;
            int right = 0, actioned = 0, correctActions = 0;
            int maxPerContact = rules.fastPoints + rules.rightPoints + Math.Max(BestActionPoints(rules), rules.soundPoints);

            foreach (var r in records)
            {
                var c = Score(r, rules, runEndTime);
                s.contacts.Add(c);

                // AI assist bookkeeping covers every contact that was shown a hint, scored or not.
                if (r.aiHint != PlayerCall.None)
                {
                    c.aiHint = r.aiHint.Label();
                    c.aiConfidence = r.aiConfidence;
                    c.aiHintWrong = r.aiHintWrong;
                    c.followedAi = r.call != PlayerCall.None && r.call == r.aiHint;
                    c.followedAiRight = c.followedAi && !r.aiHintWrong;
                    bool calledRight = r.call != PlayerCall.None && r.call == RightCall(r.truthClass);
                    c.aiText = $"AI {c.aiHint} {r.aiConfidence}% {(r.aiHintWrong ? "WRONG" : "right")}, "
                        + (r.call == PlayerCall.None ? "no call" : c.followedAi ? "followed" : "overrode" + (calledRight ? " correctly" : ""));
                    s.aiHintsShown++;
                    if (r.aiHintWrong)
                    {
                        s.wrongAiHints++;
                        if (c.followedAi) s.followedWrongHint++;
                        if (calledRight) s.overrodeWrongHintCorrectly++;
                    }
                }
                else c.aiText = "AI -";
                if (r.group) s.groupDecisions++;
                if (r.deceptive) s.deceptiveContacts++;
                if (c.deceptionCaught) s.deceptionsCaught++;
                s.decisionChanges += r.callRevisions + r.actionRevisions;
                if (!c.scored) { s.notScored++; if (c.notScoredReason == NotDetectableReason) s.notDetectable++; continue; }

                s.scoredContacts++;
                s.totalPoints += c.total;
                s.maxPoints += maxPerContact;
                bool hostile = Category(r.truthClass) == ContactCategory.Hostile;
                if (hostile && !r.Actioned && !c.handled) s.missedHostiles++;

                if (!c.detected) { s.missed++; continue; }
                s.detected++;
                detectionSum += c.detectionSeconds;

                int row = Array.IndexOf(Rows, r.truthClass), col = Array.IndexOf(Columns, r.call);
                if (row >= 0 && col >= 0) s.confusion[row * Columns.Length + col]++;
                if (r.call != PlayerCall.None && r.call == RightCall(r.truthClass)) right++;

                if (!r.Actioned) { if (!c.handled) s.unresolved++; continue; }
                actioned++;
                if (c.nodes[2].passed) correctActions++;
                if (r.action == PlayerAction.Engage && !hostile) s.falseEngagements++;
            }

            if (s.detected > 0)
            {
                s.averageDetectionSeconds = detectionSum / s.detected;
                s.classificationAccuracy = 100f * right / s.detected;
            }
            if (actioned > 0) s.engagementDecisionScore = 100f * correctActions / actioned;
            RollUp(s, records, rules);
            TeamRollUp(s, role);
            PrevalenceAndVigilance(s, records, runEndTime, rules);
            s.swarm = Triage(records, rules);
            if (s.swarm.happened) { s.totalPoints += s.swarm.points; s.maxPoints += s.swarm.maxPoints; }
            s.groupCall = GroupCalls.Evaluate(groupCalls);
            if (s.groupCall.happened) { s.totalPoints += s.groupCall.points; s.maxPoints += s.groupCall.maxPoints; }
            if (roeChanges != null && roeChanges.Count > 0)
            {
                s.cardChange = CardChange(records, rules, roeChanges[0], s.contacts);
                s.totalPoints += s.cardChange.points;
                s.maxPoints += s.cardChange.maxPoints;
            }
            return s;
        }

        static void PrevalenceAndVigilance(SessionResult s, IList<ContactRecord> records, float runEndTime, RuleSet rules)
        {
            int all = 0, hostile = 0;
            foreach (var r in records) { all++; if (Category(r.truthClass) == ContactCategory.Hostile) hostile++; }
            s.realisedHostileShare = all > 0 ? (float)hostile / all : -1f;

            var v = s.vigilance = new VigilanceResult();
            float third = Mathf.Max(1f, runEndTime) / 3f;
            var n1bSum = new float[3];
            var n1bCount = new int[3];
            foreach (var c in s.contacts)
            {
                if (!c.scored) continue;
                int k = Mathf.Clamp((int)(c.spawnTime / third), 0, 2);
                v.contacts[k]++;
                if (c.detected && c.detectionSeconds <= rules.mediumSeconds) v.hits[k]++;
                if (c.n1bSeconds >= 0f) { n1bSum[k] += c.n1bSeconds; n1bCount[k]++; }
            }
            v.enough = true;
            for (int k = 0; k < 3; k++)
            {
                if (v.contacts[k] > 0) v.hitRate[k] = (float)v.hits[k] / v.contacts[k];
                if (n1bCount[k] > 0) v.n1b[k] = n1bSum[k] / n1bCount[k];
                if (v.contacts[k] < 2) v.enough = false;
            }
            v.hitChange = v.hitRate[0] >= 0f && v.hitRate[2] >= 0f ? v.hitRate[2] - v.hitRate[0] : 0f;
            v.n1bChange = v.n1b[0] >= 0f && v.n1b[2] >= 0f ? v.n1b[2] - v.n1b[0] : 0f;
            string Rate(int k) => v.hitRate[k] < 0f ? "-" : (100f * v.hitRate[k]).ToString("F0") + " %";
            v.summary = $"thirds: hits {Rate(0)} / {Rate(1)} / {Rate(2)} ({v.contacts[0]}/{v.contacts[1]}/{v.contacts[2]} contacts)"
                + (v.enough ? $", change first to last {100f * v.hitChange:+0;-0;0} points, N1b {v.n1bChange:+0.0;-0.0;0.0} s" : ", too few contacts per third to call a trend (pooled over sessions later)");
        }

        // Called by the keeper once the run's conditions are known: the requested band against what was realised (doc V7).
        public static void StampPrevalence(SessionResult s, Prevalence requested, bool quietWatch, bool nullMission)
        {
            s.prevalence = requested.ToString();
            s.quietWatch = quietWatch;
            s.nullMission = nullMission;
            s.prevalenceNote = "";
            if (requested == Chaukanna.Scenario.Prevalence.Custom || s.realisedHostileShare < 0f) return;
            var band = ScenarioConfig.Band(requested);
            if (nullMission) s.prevalenceNote = "null mission: no hostile this run (planned)";
            else if (s.realisedHostileShare < band.x || s.realisedHostileShare > band.y)
                s.prevalenceNote = $"{requested} prevalence requested ({band.x * 100f:F0}-{band.y * 100f:F0} %), realised {s.realisedHostileShare * 100f:F0} %";
        }

        // The dense-sky facts of a run, from its records. Scoring is not changed by them.
        public static void StampCrowd(SessionResult s, IList<ContactRecord> records, int tracks, int peak)
        {
            var c = CrowdSky.Summarize(records, tracks, peak);
            s.crowdTracks = c.tracks; s.crowdPeak = c.peak; s.crowdChecked = c.checkedCount;
            s.iffChallenges = c.iffChallenges; s.crowdFriendliesHit = c.friendliesHit;
        }

        public static SwarmTriageResult Triage(IList<ContactRecord> records, RuleSet rules)
        {
            var t = new SwarmTriageResult { maxPoints = rules.swarmTriageMaxPoints };
            float firstSeen = float.MaxValue, firstAction = float.MaxValue;
            foreach (var r in records)
            {
                if (r.swarmId < 0) continue;
                t.happened = true;
                t.size++;
                if (r.firstInViewTime >= 0f) { t.seen++; firstSeen = Mathf.Min(firstSeen, r.firstInViewTime); }
                if (r.despawnReason == "ReachedDefendedPoint") t.leakers++;
                if (r.swarmRank >= 0)
                {
                    t.actedOn++;
                    if (r.swarmRank <= 1) t.nearestFirst++;
                    if (r.actionTime >= 0f) firstAction = Mathf.Min(firstAction, r.actionTime);
                }
            }
            if (!t.happened) return t;
            if (t.actedOn > 0) t.nearestFirstRate = (float)t.nearestFirst / t.actedOn;
            if (firstSeen < float.MaxValue && firstAction < float.MaxValue) t.firstActionSeconds = Math.Max(0f, firstAction - firstSeen);
            // Up to 2 points for going after the nearest first, 1 for letting none through.
            t.points = (t.nearestFirstRate > 0f ? Mathf.RoundToInt(2f * t.nearestFirstRate) : 0) + (t.leakers == 0 && t.actedOn > 0 ? 1 : 0);
            t.points = Math.Min(t.points, t.maxPoints);
            t.summary = $"swarm of {t.size}: {t.seen} seen, {t.actedOn} acted on, {(t.nearestFirstRate < 0f ? "-" : (100f * t.nearestFirstRate).ToString("F0") + " %")} nearest-first, {t.leakers} reached the post"
                + (t.firstActionSeconds >= 0f ? $", first action {F(t.firstActionSeconds)} s after the first was seen" : "") + $" ({t.points} of {t.maxPoints})";
            return t;
        }

        // Process score, grid, clock and the two signal-detection analyses over the scored contacts.
        static void RollUp(SessionResult s, IList<ContactRecord> records, RuleSet rules)
        {
            int processSum = 0, processN = 0;
            int rHits = 0, rSignals = 0, rFa = 0, rNoise = 0, eHits = 0, eSignals = 0, eFa = 0, eNoise = 0;
            for (int i = 0; i < records.Count && i < s.contacts.Count; i++)
            {
                var r = records[i];
                var c = s.contacts[i];
                if (!c.scored) continue;
                if (c.processScore >= 0) { processSum += c.processScore; processN++; }
                switch (c.grid)
                {
                    case "Sound, successful": s.gridSoundSuccessful++; break;
                    case "Sound, unlucky": s.gridSoundUnlucky++; break;
                    case "Lucky": s.gridLucky++; break;
                    case "Unsound": s.gridUnsound++; break;
                }
                if (c.clockMiss) s.clockMisses++;
                if (c.missKind.StartsWith("search")) s.searchFailures++;
                if (c.missKind.StartsWith("perception")) s.perceptionFailures++;
                if (c.correctRejection) s.correctRejections++;
                if (c.unverified) s.unverifiedEngagements++;

                // Report: is there a drone? Signal = any drone, noise = birds. "Yes" = called it a drone.
                bool drone = r.truthClass != ContactClass.Bird;
                bool saidDrone = r.call != PlayerCall.None && r.call != PlayerCall.Bird;
                if (drone) { rSignals++; if (saidDrone) rHits++; }
                else { rNoise++; if (saidDrone) rFa++; }

                // Engage: is it hostile? Signal = hostiles that came into view, noise = the rest that came into view. "Yes" = jam or engage.
                if (!c.detected) continue;
                bool hostile = Category(r.truthClass) == ContactCategory.Hostile;
                bool fired = r.action == PlayerAction.Jam || r.action == PlayerAction.Engage;
                if (hostile) { eSignals++; if (fired) eHits++; }
                else { eNoise++; if (fired) eFa++; }
            }
            s.processScore = processN > 0 ? (float)processSum / processN : -1f;
            s.reportSdt = SdtResult.From(rHits, rSignals, rFa, rNoise, rules.sdtMinimumTrials);
            s.engageSdt = SdtResult.From(eHits, eSignals, eFa, eNoise, rules.sdtMinimumTrials);
        }

        // LAN team play: the counts and the two seat-specific means. Handled-by-team applies on any seat in a team run.
        static void TeamRollUp(SessionResult s, Chaukanna.Net.SeatRole role)
        {
            s.seatRole = role == Chaukanna.Net.SeatRole.Any ? "" : Chaukanna.Net.NetPlayer.RoleName(role);
            float delaySum = 0f, latSum = 0f;
            int delayN = 0, latN = 0;
            foreach (var c in s.contacts)
            {
                if (!c.scored) continue;
                if (c.handled) s.handledByTeam++;
                if (c.upstreamWaiver) s.upstreamWaivers++;
                if (c.reportAlone) s.reportAloneEngagements++;
                if (c.handoffFratricide) s.handoffFratricides++;
                if (c.commanderDelay >= 0f) { delaySum += c.commanderDelay; delayN++; }
                if (c.sentryReportLatency >= 0f) { latSum += c.sentryReportLatency; latN++; }
            }
            if (role == Chaukanna.Net.SeatRole.Commander && delayN > 0) { s.commanderDelayCount = delayN; s.commanderDelayMean = delaySum / delayN; }
            if (role == Chaukanna.Net.SeatRole.Sentry) { s.sentryReports = latN; s.sentryReportLatencyMean = latN > 0 ? latSum / latN : -1f; }
        }

        static bool IsHostileCall(PlayerCall call) => call == PlayerCall.HostileQuad || call == PlayerCall.HostileFixedWing;

        // Commander: engaged under Card B / C with only the sentry's report to go on. An own lock from before the report, a look
        // through the binoculars or an IFF challenge is the commander's own check; a lock made after the report (the T key) is not.
        static bool ReportAlone(ContactRecord r)
        {
            if (r.action != PlayerAction.Engage || r.reportReceivedTime < 0f || r.opticsVerified || r.iffChecked) return false;
            bool ownViewFirst = r.firstLockTime >= 0f && r.firstLockTime < r.reportReceivedTime;
            return !ownViewFirst && (r.roeAtAction == RoeCard.CardB || r.roeAtAction == RoeCard.CardC);
        }

        // The class the player's call names (their belief), if any.
        public static bool CallClass(PlayerCall call, out ContactClass believed)
        {
            switch (call)
            {
                case PlayerCall.HostileQuad: believed = ContactClass.HostileQuad; return true;
                case PlayerCall.HostileFixedWing: believed = ContactClass.HostileFixedWing; return true;
                case PlayerCall.OwnIsrDrone: believed = ContactClass.FriendlyISR; return true;
                case PlayerCall.CivilianDrone: believed = ContactClass.CivilianQuad; return true;
                case PlayerCall.Bird: believed = ContactClass.Bird; return true;
                default: believed = ContactClass.Bird; return false;
            }
        }
        static int Units(Severity s, RuleSet rules) => s == Severity.Sound ? 0 : s == Severity.Minor ? rules.penaltyMinor : s == Severity.Major ? rules.penaltyMajor : rules.penaltyCritical;

        // Severity of a decision for a class under a card: the matrix for lettered cards, the old rules otherwise.
        static Severity Judge(RuleSet rules, RoeCard card, ContactClass truth, PlayerAction action)
        {
            var lettered = rules.Card(card);
            if (lettered != null) return lettered.Lookup(RuleSet.Group(truth), action);
            var rule = rules.Find(card, Category(truth), action);
            if (rule == null) return Severity.Minor;
            return rule.correct ? Severity.Sound : rule.points <= -5 ? Severity.Critical : rule.points < 0 ? Severity.Major : Severity.Minor;
        }

        // Grace: decisions made within a few seconds of the change were committed under the old card.
        public static CardChangeResult CardChange(IList<ContactRecord> records, RuleSet rules, RoeChange change, List<ContactResult> results = null)
        {
            var cc = new CardChangeResult { happened = true, from = change.from.ToString(), to = change.to.ToString(), time = change.time, maxPoints = rules.cardChangeMaxPoints };
            var oldCard = rules.Card(change.from);
            var newCard = rules.Card(change.to);
            float first = float.MaxValue;
            foreach (var r in records)
            {
                if (!r.Actioned || r.actionTime < change.time + rules.cardChangeGraceSeconds) continue;
                cc.decisionsAfter++;
                var g = RuleSet.Group(r.truthClass);
                Severity now = newCard != null ? newCard.Lookup(g, r.action) : (OldRuleCorrect(rules, change.to, r) ? Severity.Sound : Severity.Major);
                Severity before = oldCard != null ? oldCard.Lookup(g, r.action) : (OldRuleCorrect(rules, change.from, r) ? Severity.Sound : Severity.Major);
                if (before == Severity.Sound && now != Severity.Sound)
                {
                    cc.staleErrors++;
                    if (results != null) foreach (var c in results) if (c.id == r.id) c.staleCard = true;
                }
                // Adaptation shows on a contact the two cards treat differently, decided right under the new one.
                bool disagree = oldCard != null && newCard != null && CardsDiffer(oldCard, newCard, g);
                if (now == Severity.Sound && (disagree || oldCard == null || newCard == null)) first = Mathf.Min(first, r.actionTime);
            }
            if (first < float.MaxValue) cc.adaptationSeconds = first - change.time;
            int p = rules.cardChangeMaxPoints - cc.staleErrors;
            if (cc.adaptationSeconds < 0f) p -= 2;
            else if (cc.adaptationSeconds > rules.cardChangeSlowSeconds) p -= 1;
            cc.points = Math.Max(-rules.cardChangeMaxPoints, Math.Min(rules.cardChangeMaxPoints, p));
            cc.summary = $"{RoeCards.Short(change.from)} > {RoeCards.Short(change.to)} at {F(change.time)} s: "
                + (cc.adaptationSeconds >= 0f ? $"adapted in {F(cc.adaptationSeconds)} s" : "no decision showed the new card")
                + $", {cc.staleErrors} stale-card error{(cc.staleErrors == 1 ? "" : "s")} in {cc.decisionsAfter} decision{(cc.decisionsAfter == 1 ? "" : "s")} after the change ({cc.points:+0;-0;0} of {cc.maxPoints})";
            return cc;
        }

        // The two cards treat this kind of contact differently for at least one action.
        static bool CardsDiffer(CardDefinition a, CardDefinition b, ThreatGroup g)
        {
            foreach (var act in new[] { PlayerAction.Hold, PlayerAction.Report, PlayerAction.Jam, PlayerAction.Engage }) if (a.Lookup(g, act) != b.Lookup(g, act)) return true;
            return false;
        }

        static bool OldRuleCorrect(RuleSet rules, RoeCard card, ContactRecord r)
        {
            var rule = rules.Find(card, Category(r.truthClass), r.action);
            return rule != null && rule.correct;
        }

        static int BestActionPoints(RuleSet rules)
        {
            int best = 0;
            foreach (var rule in rules.actionRules) if (rule.points > best) best = rule.points;
            return best;
        }

        static ContactResult Score(ContactRecord r, RuleSet rules, float runEndTime)
        {
            var c = new ContactResult
            {
                id = r.id,
                truthClass = r.truthClass.ToString(),
                call = r.call.Label(),
                action = r.Actioned ? r.action.ToString() : "none",
                roeAtAction = r.Actioned ? r.roeAtAction.ToString() : "",
                detected = r.firstInViewTime >= 0f,
                unresolved = !r.Actioned,
                holds = r.holds,
                holdSeconds = r.OnHold && r.holdStartTime >= 0f ? Math.Max(0f, runEndTime - r.holdStartTime) : r.holdSeconds,
                decisionLatency = r.Actioned && r.callTime >= 0f ? Math.Max(0f, r.actionTime - r.callTime) : -1f,
                spawnTime = r.spawnTime,
                firstInViewTime = r.firstInViewTime,
                lockTime = r.firstLockTime,
                callTime = r.callTime,
                actionTime = r.actionTime,
                despawnTime = r.despawnTime,
                engageAttempts = r.shots,
                engageHit = r.engageHit,
                firstCall = r.firstCall.Label(),
                callChanges = r.callChanges,
                actionChanges = r.actionRevisions,
                group = r.group,
                deceptive = r.deceptive,
                anomalyTime = r.anomalyTime,
                history = new List<string>(r.history),
            };
            string head = $"#{r.id:00} {r.truthClass}";

            // LAN team play: who reported it, and the two links of the chain this seat can be measured on.
            bool sentry = roleForRun == Chaukanna.Net.SeatRole.Sentry, commander = roleForRun == Chaukanna.Net.SeatRole.Commander;
            c.handledBy = r.handledBy ?? "";
            c.reportedBy = r.reportedBy ?? "";
            c.reportedCall = r.reportedCall == PlayerCall.None ? "" : r.reportedCall.Label();
            c.reportReceived = r.reportReceivedTime;
            if (sentry && r.action == PlayerAction.Report && r.actionTime >= 0f && r.firstLockTime >= 0f) c.sentryReportLatency = Math.Max(0f, r.actionTime - r.firstLockTime);
            if (commander && r.reportReceivedTime >= 0f && r.Actioned && r.action != PlayerAction.Hold && r.actionTime >= r.reportReceivedTime) c.commanderDelay = r.actionTime - r.reportReceivedTime;
            c.handoffFratricide = commander && Category(r.truthClass) == ContactCategory.Own && r.action == PlayerAction.Engage && IsHostileCall(r.reportedCall);

            if (!c.detected && r.spawnTime > runEndTime - rules.lateSpawnSeconds)
            {
                c.scored = false;
                c.notScoredReason = "spawned too late";
                c.path = head + " | not scored: spawned too late";
                c.detBranch = "Not scored";
                return c;
            }

            // Taken out by the trainer before the player had decided anything: the player was never given the whole job.
            if (r.despawnReason == Contact.RemovedByTrainer && r.action == PlayerAction.None)
            {
                c.scored = false;
                c.notScoredReason = "removed by trainer";
                c.path = head + " | not scored: removed by the trainer";
                c.detBranch = "Not scored";
                return c;
            }

            // Never detectable: out of range, behind terrain or too small for its whole life, wherever the player looked.
            // Nobody could have seen it, so it says so in the AAR and does not count against the player.
            if (!c.detected && rules.excludeNeverDetectable && r.firstDetectableTime < 0f)
            {
                c.scored = false;
                c.notScoredReason = NotDetectableReason;
                c.path = head + " | not scored: not detectable (never in range, line of sight and size at once)";
                c.detBranch = "Not detectable";
                return c;
            }

            // Node 1: detected?
            if (!c.detected)
            {
                c.nodes.Add(new NodeResult { label = "DET", points = 0, passed = false, reason = "never in view" });
                c.path = head + " | MISSED = 0";
                c.detBranch = "Missed";
                c.missKind = "search failure: could have been seen, never brought into view";
                c.outcome = r.despawnReason == "ReachedDefendedPoint" && Category(r.truthClass) == ContactCategory.Hostile ? "Leaked" : "Pending";
                Counterfactual(r, rules, c, Category(r.truthClass) == ContactCategory.Hostile, false, runEndTime);
                return c;
            }
            float from = r.firstDetectableTime >= 0f ? Math.Min(r.firstDetectableTime, r.firstInViewTime) : r.spawnTime;
            c.detectionSeconds = Math.Max(0f, r.firstInViewTime - from);
            int detPoints = c.detectionSeconds < rules.fastSeconds ? rules.fastPoints
                : c.detectionSeconds <= rules.mediumSeconds ? rules.mediumPoints : rules.slowPoints;
            c.nodes.Add(new NodeResult { label = "DET", points = detPoints, passed = true, reason = F(c.detectionSeconds) + "s" });
            c.detBranch = c.detectionSeconds < rules.fastSeconds ? "Fast" : c.detectionSeconds <= rules.mediumSeconds ? "Medium" : "Slow";
            // N1a = search (could be seen > came into view; the DET points above), N1b = perception (in view > locked).
            c.n1aSeconds = c.detectionSeconds;
            if (r.firstLockTime >= 0f)
            {
                c.n1bSeconds = Math.Max(0f, r.firstLockTime - r.firstInViewTime);
                c.perceptionBand = c.n1bSeconds <= rules.perceptionFullSeconds ? "full" : c.n1bSeconds <= rules.perceptionPartialSeconds ? "partial" : "late";
            }
            else c.perceptionBand = "never locked";

            // Node 2: classified right? The latest call is the one judged; how the player got there adjusts it.
            var cls = new NodeResult { label = "CLASS" };
            bool rightNow = r.call != PlayerCall.None && r.call == RightCall(r.truthClass);
            bool changed = r.callChanges > 0;
            bool wasRight = changed && r.callBeforeChange == RightCall(r.truthClass);
            if (r.call == PlayerCall.None) { cls.points = 0; cls.reason = "no call"; c.classBranch = "No call"; }
            else if (rightNow)
            {
                cls.points = rules.rightPoints;
                cls.passed = true;
                cls.reason = "right";
                c.classBranch = "Right";
                if (changed && !wasRight && r.deceptive && r.callChangeInformed)
                {
                    // Watched it, saw it break pattern, and re-called it: the best outcome for a blend-in.
                    cls.points += rules.deceptionCaughtBonus;
                    cls.reason = $"right after the anomaly, was {r.callBeforeChange.Label()}: caught the deception";
                    c.deceptionCaught = true;
                    c.classBranch = "Caught deception";
                }
                else if (changed && !wasRight && r.lastCallChangeWasRevision)
                {
                    c.changePenalty += rules.secondGuessPenalty;
                    cls.points += rules.secondGuessPenalty;
                    cls.reason = $"right on a second guess, was {r.callBeforeChange.Label()}";
                    c.classBranch = "Second guess";
                }
            }
            else
            {
                bool nearMiss = IsNearMiss(r, rules);
                cls.points = nearMiss ? rules.nearMissPoints : rules.wrongPoints;
                cls.reason = (nearMiss ? "near-miss" : "wrong") + $", called {r.call.Label()}";
                c.classBranch = nearMiss ? "Near miss" : "Wrong";
                if (wasRight && r.lastCallChangeWasRevision)
                {
                    c.changePenalty += rules.changedToWrongPenalty;
                    cls.points += rules.changedToWrongPenalty;
                    cls.reason += $", changed from the right call {r.callBeforeChange.Label()}";
                    c.classBranch = "Changed to wrong";
                }
                else if (r.deceptive && r.call == PlayerCall.OwnIsrDrone)
                {
                    cls.reason += r.anomalyTime >= 0f ? ", deceived and not corrected after the anomaly" : ", deceived (it never broke pattern)";
                    c.classBranch = "Deceived";
                }
            }
            c.nodes.Add(cls);
            // Report node: lock (mark) to call; the call's accuracy is the CLASS node above.
            if (r.callTime >= 0f && r.firstLockTime >= 0f)
            {
                c.reportLatency = Math.Max(0f, r.callTime - r.firstLockTime);
                c.reportBand = c.reportLatency <= rules.reportFullSeconds ? "full" : c.reportLatency <= rules.reportPartialSeconds ? "partial" : "late";
            }
            // Node 3: right action for the card in force when it was taken?
            var act = new NodeResult { label = sentry ? "REPORT" : "ACT" };
            bool hostileTruth = Category(r.truthClass) == ContactCategory.Hostile;
            // The decision clock (doc §5.4.1): opens when the contact is in view (or when it was called, if earlier), runs out at
            // the class's release point for a hostile, or when a non-hostile leaves / after a fixed time.
            c.clockOpen = ClockOpen(r);
            c.deadline = Deadline(r, rules, c.clockOpen, out c.deadlineReason);
            bool deadlinePassed = c.deadline >= 0f && c.deadline <= runEndTime;
            bool decidedInTime = r.Actioned && r.action != PlayerAction.Hold && r.actionTime <= c.deadline + 0.01f;
            if (deadlinePassed && r.firstLockTime < 0f) c.missKind = "perception failure: in view, never marked";
            bool rejected = r.action == PlayerAction.None || r.action == PlayerAction.Hold || r.action == PlayerAction.Report;

            // LAN team play (all of it needs a role or a teammate's mark; solo records never take these branches).
            // Handled: a teammate's jam or hit took it out before the deadline and this seat had not decided in time.
            c.handled = !decidedInTime && r.handledTime >= 0f && c.deadline >= 0f && r.handledTime <= c.deadline + 0.01f;
            // Sentry: the job is to report. A hostile reported in time, or a non-hostile reported, is sound; a non-hostile reported
            // with a hostile call is a false report (Minor). A hostile not reported by its deadline is the usual clock miss below.
            bool sentryReport = sentry && !c.handled && r.action == PlayerAction.Report && (!hostileTruth || decidedInTime);
            bool falseReport = sentryReport && !hostileTruth && IsHostileCall(r.call);
            // Commander first-fault rule: the sentry's report reached them too late to act on, and they had no view of their own
            // before it came. Their miss is waived and charged to the link upstream.
            bool commanderLate = commander && rules.fullScoring && deadlinePassed && hostileTruth && !decidedInTime && !c.handled && r.reportReceivedTime >= 0f
                && r.reportReceivedTime > c.deadline - rules.commanderMinSeconds && !(r.firstLockTime >= 0f && r.firstLockTime < r.reportReceivedTime);
            if (c.handled)
            {
                string who = string.IsNullOrEmpty(r.handledBy) ? "the team" : r.handledBy;
                act.passed = true;
                act.reason = $"handled by {who} at {F(r.handledTime)}s ({(string.IsNullOrEmpty(r.handledAction) ? "action" : r.handledAction)}), before {c.deadlineReason} at {F(c.deadline)}s";
            }
            else if (sentryReport)
            {
                var sev = falseReport ? Severity.Minor : Severity.Sound;
                c.severity = sev.ToString();
                act.points = rules.Points(sev);
                act.passed = !falseReport;
                act.reason = hostileTruth ? $"reported at {F(r.actionTime)}s, before {c.deadlineReason} at {F(c.deadline)}s, sound"
                    : falseReport ? $"FALSE REPORT: called it {r.call.Label()} (Minor)" : "reported, correct rejection, sound";
            }
            else if (commanderLate)
            {
                c.upstreamWaiver = true;
                c.firstFault = "sentry";
                string who = string.IsNullOrEmpty(r.reportedBy) ? "the sentry" : r.reportedBy;
                act.reason = $"upstream: report came too late ({who}): it reached the commander at {F(r.reportReceivedTime)}s, the deadline ({c.deadlineReason}) was {F(c.deadline)}s, no clock miss charged";
            }
            else if (rules.fullScoring && deadlinePassed && hostileTruth && !decidedInTime)
            {
                var card = CardAt(c.deadline, startCardForRun, changesForRun);
                var sev = Judge(rules, card, r.truthClass, PlayerAction.Hold);
                c.clockMiss = true;
                c.severity = sev.ToString();
                if (commander) c.firstFault = "commander";
                act.points = rules.Card(card) != null ? rules.Points(sev) : Math.Min(0, rules.Find(card, ContactCategory.Hostile, PlayerAction.Hold)?.points ?? 0);
                string late = r.Actioned && r.action != PlayerAction.Hold ? $", {r.action} came at {F(r.actionTime)}s, too late" : r.OnHold ? ", still on hold" : "";
                act.reason = $"MISS: {(sentry ? "not reported" : "no decision")} before {c.deadlineReason} at {F(c.deadline)}s{late} ({sev} under {RoeCards.Short(card)})";
            }
            else if (rules.fullScoring && deadlinePassed && !hostileTruth && rejected && !r.Actioned)
            {
                c.correctRejection = true;
                act.points = rules.soundPoints;
                act.passed = true;
                act.reason = $"correct rejection: left alone until {c.deadlineReason}";
            }
            else if (!r.Actioned) { act.points = 0; act.reason = "UNRESOLVED"; }
            else
            {
                var rule = rules.Find(r.roeAtAction, Category(r.truthClass), r.action);
                string under = r.action == PlayerAction.Engage && r.shots > 0
                    ? $"Engage x{r.shots} {(r.engageHit ? "hit" : "no hit")} under {r.roeAtAction}"
                    : $"{r.action} under {r.roeAtAction}";
                if (r.holds > 0 && r.action != PlayerAction.Hold) under += $" (after {r.holds} hold{(r.holds > 1 ? "s" : "")}, {F(r.holdSeconds)}s)";
                // Dithering on a hostile under attack costs points even if the final action was right.
                if (r.action != PlayerAction.Hold && Category(r.truthClass) == ContactCategory.Hostile && rules.holdDelaySeconds > 0f && (RoeCards.IsAttack(r.roeAtAction) || r.holdUnderAttackSeconds > 0f))
                {
                    int blocks = (int)(r.holdUnderAttackSeconds / rules.holdDelaySeconds);
                    c.holdPenalty = Math.Max(rules.holdDelayCap, blocks * rules.holdDelayPenalty);
                }
                // Replacing a final action with nothing new to go on is second-guessing, and costs a little each time.
                int actionChange = Math.Max(rules.actionChangeCap, r.uninformedActionRevisions * rules.actionChangePenalty);
                c.changePenalty += actionChange;
                var lettered = rules.Card(r.roeAtAction);
                if (lettered != null)
                {
                    // Cards A / B / C: the severity matrix decides (doc §5.4.4).
                    var sev = lettered.Lookup(RuleSet.Group(r.truthClass), r.action);
                    // Commander verification lapse (doc rule 1): engaged on a report alone is at least Minor under B, Major under C.
                    bool lapse = commander && ReportAlone(r) && sev < ReportAloneFloor(r.roeAtAction);
                    if (lapse) sev = ReportAloneFloor(r.roeAtAction);
                    c.reportAlone = commander && ReportAlone(r);
                    c.severity = sev.ToString();
                    act.points = rules.Points(sev) + c.holdPenalty + actionChange;
                    act.passed = sev == Severity.Sound;
                    act.reason = under + (sev == Severity.Sound ? ", sound" : $", {sev.ToString().ToUpperInvariant()} under {RoeCards.Short(r.roeAtAction)}")
                        + (c.holdPenalty != 0 ? $", held {F(r.holdUnderAttackSeconds)}s under attack ({c.holdPenalty})" : "")
                        + (c.reportAlone ? ", engaged on a report alone (no own lock, optics or IFF)" : "");
                }
                else if (rule == null) { act.points = actionChange; act.reason = under + ", no rule"; }
                else
                {
                    act.points = rule.points + c.holdPenalty + actionChange;
                    act.passed = rule.correct;
                    act.reason = under + (string.IsNullOrEmpty(rule.flag) ? "" : ", " + rule.flag)
                        + (c.holdPenalty != 0 ? $", held {F(r.holdUnderAttackSeconds)}s under attack ({c.holdPenalty})" : "");
                }
                if (r.actionRevisions > 0)
                    act.reason += $", action changed {r.actionRevisions}x" + (actionChange != 0 ? $" ({actionChange})" : " (new information, no penalty)");

                // Unverified engagement: the AI cue was the only evidence (the player followed it and never looked through the binoculars).
                if (rules.fullScoring && rules.flagUnverifiedEngagement && r.action == PlayerAction.Engage && r.aiHint != PlayerCall.None && r.call == r.aiHint && !r.opticsVerified)
                {
                    c.unverified = true;
                    bool strict = r.roeAtAction == RoeCard.CardB || r.roeAtAction == RoeCard.CardC;
                    int penalty = strict ? rules.majorPoints : -1;
                    act.points += penalty;
                    act.reason += $", UNVERIFIED: AI cue was the only evidence ({(strict ? "Major" : "Minor")}, {penalty})";
                }
            }
            // Hold or report on a non-hostile by its deadline is also a correct rejection (scored by the action rules above).
            if (rules.fullScoring && deadlinePassed && !hostileTruth && rejected && r.Actioned && !falseReport && !c.handled) c.correctRejection = true;
            if (c.handoffFratricide) act.reason += $", HAND-OFF FRATRICIDE: {(string.IsNullOrEmpty(r.reportedBy) ? "a sentry" : r.reportedBy)} had reported this own drone as {r.reportedCall.Label()}";
            c.nodes.Add(act);
            c.actBranch = c.handled ? "Handled by team" : c.upstreamWaiver ? "Report too late"
                : c.clockMiss ? "Clock ran out" : c.correctRejection && !r.Actioned ? "Left alone" : r.Actioned ? r.action.ToString() : "No action";
            c.outcomeBranch = c.handled ? "Right action" : c.upstreamWaiver ? "Unresolved" : c.clockMiss ? "Wrong action" : c.correctRejection ? "Right action" : !r.Actioned ? "Unresolved"
                : r.action == PlayerAction.Engage && Category(r.truthClass) != ContactCategory.Hostile ? "False engagement"
                : act.passed ? "Right action" : "Wrong action";

            // N3 process: judged on what the player believed (their own call), not on the truth they could not know.
            // N4 outcome: what actually happened. The two together give the process x outcome grid.
            if (c.clockMiss || c.correctRejection || r.Actioned || c.handled || c.upstreamWaiver)
            {
                Severity process;
                if (c.handled || c.upstreamWaiver) process = Severity.Sound;       // not this seat's fault: judged sound
                else if (sentryReport) process = falseReport ? Severity.Minor : Severity.Sound;
                else if (c.correctRejection) process = Severity.Sound;
                else if (c.clockMiss) process = (Severity)Enum.Parse(typeof(Severity), c.severity);
                else
                {
                    var card = r.roeAtAction;
                    process = CallClass(r.call, out var believed) ? Judge(rules, card, believed, r.action) : Judge(rules, card, r.truthClass, r.action);
                    if (c.unverified && process < Severity.Major) process = (card == RoeCard.CardB || card == RoeCard.CardC) ? Severity.Major : (Severity)Math.Max((int)process, (int)Severity.Minor);
                    if (c.reportAlone && process < ReportAloneFloor(card)) process = ReportAloneFloor(card);
                }
                c.processSeverity = process.ToString();
                c.processScore = Math.Max(0, 100 - Units(process, rules));
                c.outcome = c.handled ? HandledOutcome(r, hostileTruth) : Outcome(r, hostileTruth, c);
                bool soundProcess = process <= Severity.Minor;
                bool goodOutcome = c.outcome == "Neutralised" || c.outcome == "Clear" || c.outcome == "Tracked";
                // A result not known yet (still in the air, nothing decisive happened) is not luck either way.
                c.grid = c.outcome == "Pending" ? "Pending" : soundProcess ? (goodOutcome ? "Sound, successful" : "Sound, unlucky") : (goodOutcome ? "Lucky" : "Unsound");
            }
            else { c.outcome = "Pending"; c.grid = "No decision"; }
            Counterfactual(r, rules, c, hostileTruth, decidedInTime, runEndTime);

            var sb = new StringBuilder(head).Append(" | ");
            for (int i = 0; i < c.nodes.Count; i++)
            {
                var n = c.nodes[i];
                c.total += n.points;
                if (i > 0) sb.Append(" > ");
                sb.Append(n.label).Append(' ').Append(n.reason).Append('(').Append(n.points.ToString("+0;-0;0", CultureInfo.InvariantCulture)).Append(')');
            }
            c.path = sb.Append(" = ").Append(c.total).ToString();
            return c;
        }

        public const string NotDetectableReason = "not detectable";

        // The decision clock opens when the contact is in view, or when it was called if that came first; -1 = never in view.
        public static float ClockOpen(ContactRecord r)
        {
            if (r.firstInViewTime < 0f) return -1f;
            return r.callTime >= 0f ? Math.Min(r.firstInViewTime, r.callTime) : r.firstInViewTime;
        }

        // When the clock runs out; -1 = no clock (never in view). Hostile: first within its class's release range of the post,
        // else when it reached the post, else a fixed time after the clock opened (it loitered). Non-hostile: when it left,
        // or a fixed time after the clock opened, whichever is first.
        public static float Deadline(ContactRecord r, RuleSet rules, float open, out string why)
        {
            why = "";
            if (open < 0f) return -1f;
            bool left = r.despawnTime >= 0f && (r.despawnReason == "ReachedDefendedPoint" || r.despawnReason == "ExitedSector");
            if (Category(r.truthClass) == ContactCategory.Hostile)
            {
                var release = rules.Release(r.truthClass);
                float range = release != null ? release.range : 150f;
                float within = r.FirstWithin(range);
                if (within >= 0f) { why = $"its release point ({range:F0} m from the post)"; return Math.Max(within, open); }
                if (left && r.despawnReason == "ReachedDefendedPoint") { why = "it reached the post"; return r.despawnTime; }
                why = "the loiter limit";
                return open + (release != null ? release.fallbackSeconds : 90f);
            }
            float limit = open + rules.nonHostileDeadlineSeconds;
            if (left && r.despawnTime < limit) { why = "it left"; return r.despawnTime; }
            why = $"{rules.nonHostileDeadlineSeconds:F0} s in view";
            return limit;
        }

        // "Your call" vs "the sound call" (doc §1.5, chairman change 8). This is the doc's fallback (K10): an annotated replay
        // of the logged run plus the reference choice, not a re-simulation. The sound call is the right class and the least
        // escalatory action the card rates Sound, made before the clock ran out.
        static void Counterfactual(ContactRecord r, RuleSet rules, ContactResult c, bool hostile, bool decidedInTime, float runEndTime)
        {
            var right = RightCall(r.truthClass);
            if (!c.detected)
            {
                c.yourCall = "never brought into view";
                c.soundCall = r.firstDetectableTime >= 0f ? $"search the sector: it could be seen from {F(r.firstDetectableTime)} s" : "search the sector";
                c.consequence = hostile ? (c.outcome == "Leaked" ? "it reached the post unseen" : "a hostile went unseen") : "no harm done: it was not a threat";
                return;
            }
            float decideBy = c.deadline >= 0f ? c.deadline : runEndTime;
            var card = r.Actioned && decidedInTime ? r.roeAtAction : CardAt(Math.Min(decideBy, runEndTime), startCardForRun, changesForRun);
            var sound = SoundAction(rules, card, r.truthClass);
            c.soundAction = sound.ToString();
            c.yourCall = (r.call != PlayerCall.None ? r.call.Label() : "no call")
                + (r.Actioned ? $", {r.action} at {F(r.actionTime)} s" + (c.deadline >= 0f && r.actionTime > c.deadline + 0.01f ? " (after the deadline)" : "") : ", no action");
            c.soundCall = $"{right.Label()}, {sound}" + (c.deadline >= 0f ? $" by {F(c.deadline)} s ({c.deadlineReason})" : "") + $" under {RoeCards.Short(card)}";

            bool rightCall = r.call == right;
            bool soundAction = r.Actioned && Judge(rules, r.roeAtAction, r.truthClass, r.action) == Severity.Sound;
            bool inTime = !hostile || decidedInTime;
            c.matchedSound = rightCall && (soundAction || (!hostile && !r.Actioned && c.correctRejection)) && inTime;
            if (c.matchedSound) { c.consequence = "your call was the sound call"; return; }
            if (hostile)
            {
                string instead = c.outcome == "Leaked" ? "instead it reached the post" : c.outcome == "Neutralised" ? "it came down anyway, but later or for the wrong reason" : c.outcome == "Pending" ? "instead it was still flying at the end" : $"instead: {c.outcome.ToLowerInvariant()}";
                c.consequence = sound == PlayerAction.Report ? $"reported and tracked before {c.deadlineReason}; {instead}"
                    : $"{(sound == PlayerAction.Jam ? "jammed" : "engaged")} before {c.deadlineReason}, it would not have reached its release point; {instead}";
            }
            else
            {
                c.consequence = c.outcome == "Fratricide" ? "the own drone would still be flying: you shot it down"
                    : c.outcome == "Collateral" ? "the civil drone would still be flying: you shot it down"
                    : c.outcome.StartsWith("Fired") ? "no round would have been fired at it"
                    : r.action == PlayerAction.Jam ? "it would not have been jammed" : "same result: no harm done";
            }
        }

        // The least escalatory action the card rates Sound for this truth; if none is Sound, the least bad.
        public static PlayerAction SoundAction(RuleSet rules, RoeCard card, ContactClass truth)
        {
            var order = new[] { PlayerAction.Report, PlayerAction.Jam, PlayerAction.Engage, PlayerAction.Hold };
            PlayerAction best = PlayerAction.Report;
            var bestSev = Severity.Critical;
            foreach (var a in order)
            {
                var sev = Judge(rules, card, truth, a);
                if (sev == Severity.Sound) return a;
                if (sev < bestSev) { bestSev = sev; best = a; }
            }
            return best;
        }

        // N4: what happened in the sim.
        static Severity ReportAloneFloor(RoeCard card) => card == RoeCard.CardB ? Severity.Minor : Severity.Major;

        // A teammate's shot down is also "EngagedHit" on this seat's contact; only team play counts it.
        static bool TeamPlay(ContactRecord r) => roleForRun != Chaukanna.Net.SeatRole.Any || r.handledTime >= 0f;

        // What a teammate's jam or hit did, for a contact this seat had no chance to decide on.
        static string HandledOutcome(ContactRecord r, bool hostile)
        {
            if (hostile) return "Neutralised";
            return r.truthClass == ContactClass.FriendlyISR ? "Fratricide" : "Collateral";
        }

        static string Outcome(ContactRecord r, bool hostile, ContactResult c)
        {
            bool fired = r.action == PlayerAction.Engage && r.shots > 0;
            if (!hostile && !fired && r.despawnReason == "EngagedHit" && TeamPlay(r))
                return r.truthClass == ContactClass.FriendlyISR ? "Fratricide" : "Collateral";      // another seat shot it down
            if (hostile)
            {
                // Jammed with effect (link lost, coming down) counts as neutralised even before it reaches the ground.
                if ((fired && r.engageHit) || r.despawnReason == "JammedDown" || (r.action == PlayerAction.Jam && r.actionLocked) || (r.despawnReason == "EngagedHit" && TeamPlay(r))) return "Neutralised";
                if (r.despawnReason == "ReachedDefendedPoint") return "Leaked";
                if (r.action == PlayerAction.Report && r.truthClass == ContactClass.HostileFixedWing) return "Tracked";
                return r.despawnTime >= 0f ? "Not stopped" : "Pending";
            }
            if (fired && r.engageHit) return r.truthClass == ContactClass.FriendlyISR ? "Fratricide" : "Collateral";
            if (fired) return r.truthClass == ContactClass.FriendlyISR ? "Fired on own" : r.truthClass == ContactClass.Bird ? "Fired on a bird" : "Fired on civil";
            return "Clear";
        }

        static bool IsNearMiss(ContactRecord r, RuleSet rules)
        {
            // Look-alikes are confusable closer in at dusk and at night (N2 partial credit by range and light).
            float factor = !rules.fullScoring ? 1f : r.callLight == (int)TimeOfDay.Night ? rules.nightNearMissRangeFactor : r.callLight == (int)TimeOfDay.Dusk ? rules.duskNearMissRangeFactor : 1f;
            foreach (var n in rules.nearMisses)
                if (n.truth == r.truthClass && n.call == r.call && r.callRange >= n.minRange * factor) return true;
            return false;
        }

        static string F(float v)
        {
            return v.ToString("F1", CultureInfo.InvariantCulture);
        }

        // Multi-line text for the Console.
        public static string Describe(SessionResult s)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Score] TOTAL {s.totalPoints} / {s.maxPoints} over {s.scoredContacts} scored contacts ({s.notScored} not scored, of which {s.notDetectable} never detectable)");
            sb.AppendLine($"[Score] Average detection time: {(s.averageDetectionSeconds < 0f ? "-" : F(s.averageDetectionSeconds) + " s")} ({s.detected} detected, {s.missed} missed)");
            sb.AppendLine($"[Score] Classification accuracy: {F(s.classificationAccuracy)} % of detected contacts");
            sb.AppendLine($"[Score] Engagement decision score: {F(s.engagementDecisionScore)} % of actioned contacts");
            sb.AppendLine($"[Score] Unresolved {s.unresolved}, false engagements {s.falseEngagements}, missed hostiles {s.missedHostiles}");
            if (s.cardChange != null && s.cardChange.happened) sb.AppendLine("[Score] Card change: " + s.cardChange.summary);
            if (s.swarm != null && s.swarm.happened) sb.AppendLine("[Score] Swarm triage: " + s.swarm.summary);
            if (s.groupCall != null && s.groupCall.happened) sb.AppendLine("[Score] Group calls: " + s.groupCall.summary);
            sb.AppendLine($"[Score] Process {(s.processScore < 0f ? "-" : F(s.processScore))} / 100. Grid: sound-successful {s.gridSoundSuccessful}, sound-unlucky {s.gridSoundUnlucky}, lucky {s.gridLucky}, unsound {s.gridUnsound}. Clock misses {s.clockMisses}, correct rejections {s.correctRejections}, unverified engagements {s.unverifiedEngagements}, search failures {s.searchFailures}, perception failures {s.perceptionFailures}");
            sb.AppendLine("[Score] " + s.reportSdt.Describe("Report (is there a drone?)"));
            sb.AppendLine("[Score] " + s.engageSdt.Describe("Engage (is it hostile?)"));
            sb.AppendLine($"[Score] AI assist: {s.aiHintsShown} hints shown, {s.wrongAiHints} wrong, followed a wrong hint {s.followedWrongHint}, overrode a wrong hint correctly {s.overrodeWrongHintCorrectly}");
            sb.AppendLine("[Score] Confusion grid (rows = truth, columns = call): " + string.Join(" | ", s.confusionColumns));
            int cols = s.confusionColumns.Length;
            for (int row = 0; row < s.confusionRows.Length; row++)
            {
                sb.Append("[Score]   ").Append(s.confusionRows[row].PadRight(17));
                for (int col = 0; col < cols; col++) sb.Append(s.confusion[row * cols + col].ToString().PadLeft(4));
                sb.AppendLine();
            }
            foreach (var c in s.contacts) sb.AppendLine("[Score] " + c.path + "  |  " + c.aiText);
            return sb.ToString();
        }
    }
}
