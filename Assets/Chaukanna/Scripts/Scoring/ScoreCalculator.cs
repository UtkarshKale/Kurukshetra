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

        // Timeline, scenario seconds; negative = never. For the dashboard.
        public float spawnTime = -1f, firstInViewTime = -1f, lockTime = -1f, callTime = -1f, actionTime = -1f, despawnTime = -1f;
        public bool engageHit;
        public List<NodeResult> nodes = new List<NodeResult>();
        public int total;
        public string path;     // one-line tree path for the log
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

        public int totalPoints;
        public int maxPoints;                       // best case for the scored contacts: 8 each (3 detect + 3 classify + 2 action)
        public float ScorePercent => maxPoints > 0 ? Mathf.Clamp(100f * totalPoints / maxPoints, 0f, 100f) : 0f;
        public float averageDetectionSeconds = -1f; // detected contacts only; -1 = none detected
        public float classificationAccuracy;        // % of detected contacts classified right
        public float engagementDecisionScore;       // % of actioned contacts with a correct action

        public int scoredContacts;
        public int notScored;
        public int detected;
        public int missed;
        public int unresolved;
        public int falseEngagements;                // engaged something that was not hostile
        public int missedHostiles;                  // hostiles with no action taken

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

        public static SessionResult Calculate(IList<ContactRecord> records, RuleSet rules, float runEndTime, int seed = 0)
        {
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
            int maxPerContact = rules.fastPoints + rules.rightPoints + BestActionPoints(rules);

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
                if (!c.scored) { s.notScored++; continue; }

                s.scoredContacts++;
                s.totalPoints += c.total;
                s.maxPoints += maxPerContact;
                bool hostile = Category(r.truthClass) == ContactCategory.Hostile;
                if (hostile && !r.Actioned) s.missedHostiles++;

                if (!c.detected) { s.missed++; continue; }
                s.detected++;
                detectionSum += c.detectionSeconds;

                int row = Array.IndexOf(Rows, r.truthClass), col = Array.IndexOf(Columns, r.call);
                if (row >= 0 && col >= 0) s.confusion[row * Columns.Length + col]++;
                if (r.call != PlayerCall.None && r.call == RightCall(r.truthClass)) right++;

                if (!r.Actioned) { s.unresolved++; continue; }
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
            return s;
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
            };
            string head = $"#{r.id:00} {r.truthClass}";

            if (!c.detected && r.spawnTime > runEndTime - rules.lateSpawnSeconds)
            {
                c.scored = false;
                c.notScoredReason = "spawned too late";
                c.path = head + " | not scored: spawned too late";
                return c;
            }

            // Node 1: detected?
            if (!c.detected)
            {
                c.nodes.Add(new NodeResult { label = "DET", points = 0, passed = false, reason = "never in view" });
                c.path = head + " | MISSED = 0";
                return c;
            }
            float from = r.firstDetectableTime >= 0f ? Math.Min(r.firstDetectableTime, r.firstInViewTime) : r.spawnTime;
            c.detectionSeconds = Math.Max(0f, r.firstInViewTime - from);
            int detPoints = c.detectionSeconds < rules.fastSeconds ? rules.fastPoints
                : c.detectionSeconds <= rules.mediumSeconds ? rules.mediumPoints : rules.slowPoints;
            c.nodes.Add(new NodeResult { label = "DET", points = detPoints, passed = true, reason = F(c.detectionSeconds) + "s" });

            // Node 2: classified right?
            var cls = new NodeResult { label = "CLASS" };
            if (r.call == PlayerCall.None) { cls.points = 0; cls.reason = "no call"; }
            else if (r.call == RightCall(r.truthClass)) { cls.points = rules.rightPoints; cls.passed = true; cls.reason = "right"; }
            else if (IsNearMiss(r, rules)) { cls.points = rules.nearMissPoints; cls.reason = $"near-miss, called {r.call.Label()}"; }
            else { cls.points = rules.wrongPoints; cls.reason = $"wrong, called {r.call.Label()}"; }
            c.nodes.Add(cls);

            // Node 3: right action for the card in force when it was taken?
            var act = new NodeResult { label = "ACT" };
            if (!r.Actioned) { act.points = 0; act.reason = "UNRESOLVED"; }
            else
            {
                var rule = rules.Find(r.roeAtAction, Category(r.truthClass), r.action);
                string under = r.action == PlayerAction.Engage && r.shots > 0
                    ? $"Engage x{r.shots} {(r.engageHit ? "hit" : "no hit")} under {r.roeAtAction}"
                    : $"{r.action} under {r.roeAtAction}";
                if (r.holds > 0 && r.action != PlayerAction.Hold) under += $" (after {r.holds} hold{(r.holds > 1 ? "s" : "")}, {F(r.holdSeconds)}s)";
                // Dithering on a hostile under attack costs points even if the final action was right.
                if (r.action != PlayerAction.Hold && Category(r.truthClass) == ContactCategory.Hostile && rules.holdDelaySeconds > 0f)
                {
                    int blocks = (int)(r.holdUnderAttackSeconds / rules.holdDelaySeconds);
                    c.holdPenalty = Math.Max(rules.holdDelayCap, blocks * rules.holdDelayPenalty);
                }
                if (rule == null) { act.points = 0; act.reason = under + ", no rule"; }
                else
                {
                    act.points = rule.points + c.holdPenalty;
                    act.passed = rule.correct;
                    act.reason = under + (string.IsNullOrEmpty(rule.flag) ? "" : ", " + rule.flag)
                        + (c.holdPenalty != 0 ? $", held {F(r.holdUnderAttackSeconds)}s under attack ({c.holdPenalty})" : "");
                }
            }
            c.nodes.Add(act);

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

        static bool IsNearMiss(ContactRecord r, RuleSet rules)
        {
            foreach (var n in rules.nearMisses)
                if (n.truth == r.truthClass && n.call == r.call && r.callRange >= n.minRange) return true;
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
            sb.AppendLine($"[Score] TOTAL {s.totalPoints} / {s.maxPoints} over {s.scoredContacts} scored contacts ({s.notScored} not scored)");
            sb.AppendLine($"[Score] Average detection time: {(s.averageDetectionSeconds < 0f ? "-" : F(s.averageDetectionSeconds) + " s")} ({s.detected} detected, {s.missed} missed)");
            sb.AppendLine($"[Score] Classification accuracy: {F(s.classificationAccuracy)} % of detected contacts");
            sb.AppendLine($"[Score] Engagement decision score: {F(s.engagementDecisionScore)} % of actioned contacts");
            sb.AppendLine($"[Score] Unresolved {s.unresolved}, false engagements {s.falseEngagements}, missed hostiles {s.missedHostiles}");
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
