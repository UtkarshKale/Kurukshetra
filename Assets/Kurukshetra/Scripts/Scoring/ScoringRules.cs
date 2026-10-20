using System;
using System.Collections.Generic;
using Kurukshetra.Scenario;
using UnityEngine;

namespace Kurukshetra.Scoring
{
    // What a truth class counts as for the action rules. Manned has no contact class yet; the rule is supported in data.
    public enum ContactCategory
    {
        Hostile,
        Own,
        Civilian,
        Bird,
        Manned,
    }

    // How bad a decision is under a lettered card (solution doc §5.4.4). Placeholder values.
    public enum Severity { Sound, Minor, Major, Critical }

    // Who a contact is, for the card matrix: strike and recce hostiles are judged differently.
    public enum ThreatGroup { Strike, Recce, Own, Civilian, Bird, Manned }

    [Serializable]
    public class CardRow
    {
        public ThreatGroup truth;
        [Tooltip("Held, or no decision before the deadline.")]
        public Severity hold;
        public Severity report, jam, engage;

        public Severity For(PlayerAction a)
        {
            switch (a)
            {
                case PlayerAction.Report: return report;
                case PlayerAction.Jam: return jam;
                case PlayerAction.Engage: return engage;
                default: return hold;
            }
        }
    }

    [Serializable]
    public class CardDefinition
    {
        public RoeCard card;
        public string title;
        [TextArea(2, 4)] public string wording;
        public List<CardRow> rows = new List<CardRow>();

        public Severity Lookup(ThreatGroup truth, PlayerAction action)
        {
            foreach (var r in rows) if (r.truth == truth) return r.For(action);
            return Severity.Sound;
        }

        // The best (least bad) action for this truth under this card.
        public Severity Best(ThreatGroup truth)
        {
            var best = Severity.Critical;
            foreach (var a in new[] { PlayerAction.Hold, PlayerAction.Report, PlayerAction.Jam, PlayerAction.Engage })
            {
                var s = Lookup(truth, a);
                if (s < best) best = s;
            }
            return best;
        }
    }

    [Serializable]
    public class NearMissRule
    {
        public ContactClass truth;
        public PlayerCall call;
        [Tooltip("Only a near-miss when the call was made beyond this range (metres). 0 = at any range.")]
        public float minRange;
    }

    // Where a hostile's decision clock runs out (doc §5.4.1, placeholder per class).
    [Serializable]
    public class ReleasePoint
    {
        public ContactClass truth;
        [Tooltip("Ground range to the post, metres, at which it releases its payload or overflies the post.")]
        public float range = 150f;
        [Tooltip("If it never comes that close (loitering, orbiting), the clock runs out this many seconds after it opened.")]
        public float fallbackSeconds = 90f;
    }

    [Serializable]
    public class ActionRule
    {
        [Tooltip("On: applies under every ROE card unless a card-specific rule exists for the same category and action.")]
        public bool anyCard;
        public RoeCard card;
        public ContactCategory category;
        public PlayerAction action;
        public int points;
        public bool correct;
        [Tooltip("Optional note shown on the contact's score line, e.g. \"engaged under hold\".")]
        public string flag;
    }

    // Plain data so ScoreCalculator stays free of Unity object dependencies and can be tested directly.
    [Serializable]
    public class RuleSet
    {
        [Header("Node 1: detection (seconds from first detectable to first in view)")]
        public float fastSeconds = 5f;
        public float mediumSeconds = 10f;
        public int fastPoints = 3;
        public int mediumPoints = 2;
        public int slowPoints = 1;

        [Header("Node 2: classification")]
        public int rightPoints = 3;
        public int nearMissPoints = 1;
        public int wrongPoints = 0;
        public List<NearMissRule> nearMisses = new List<NearMissRule>();

        [Header("Node 3: action for the ROE card in force")]
        public List<ActionRule> actionRules = new List<ActionRule>();

        [Header("Hold delay (hostiles only)")]
        [Tooltip("Holding a hostile while this card is in force, before finally acting on it, costs points.")]
        public RoeCard holdDelayCard = RoeCard.UnderAttack;
        [Tooltip("Each full block of this many seconds held under that card costs holdDelayPenalty.")]
        public float holdDelaySeconds = 15f;
        public int holdDelayPenalty = -1;
        [Tooltip("Most a single contact can lose to hold delay.")]
        public int holdDelayCap = -3;

        [Header("Changing a decision")]
        [Tooltip("Added to the classification points when a wrong call is corrected AFTER a final action, with nothing new to go on. Usually negative.")]
        public int secondGuessPenalty = -1;
        [Tooltip("Added when a right call is changed to a wrong one after a final action. Usually negative.")]
        public int changedToWrongPenalty = -1;
        [Tooltip("Added to the action points for each time a final action is replaced with nothing new to go on (no anomaly, no new ROE card).")]
        public int actionChangePenalty = -1;
        [Tooltip("Most a single contact can lose to replacing its action.")]
        public int actionChangeCap = -2;

        [Header("Blend-in hostiles")]
        [Tooltip("Bonus on top of full classification credit when a blend-in hostile is re-called correctly after it gave itself away.")]
        public int deceptionCaughtBonus = 1;

        [Header("Cards A / B / C (placeholder wording and severities, doc §5.4.4)")]
        public List<CardDefinition> cards = DefaultCards();
        [Tooltip("Points for a decision of each severity under a lettered card.")]
        public int soundPoints = 2, minorPoints = 0, majorPoints = -3, criticalPoints = -5;

        [Header("Full scoring (doc §5.4): decision clock, correct rejection, unverified engagement, light-dependent near-miss")]
        [Tooltip("ON (default): the decision clock, correct rejections, the unverified-engagement penalty and light-dependent near-misses change the points. OFF: the classic tree only (detect, classify, action); the extra nodes are still computed and shown.")]
        public bool fullScoring = true;
        [Tooltip("Decision clock, hostiles: the deadline is the moment the contact first comes within this ground range of the post (payload release / overflight). Placeholder values per class, to be set with an instructor.")]
        public List<ReleasePoint> releasePoints = DefaultReleasePoints();
        [Tooltip("Decision clock, non-hostiles: the deadline is when it leaves, or this many seconds after the clock opened. Hold or report by then = correct rejection.")]
        public float nonHostileDeadlineSeconds = 30f;
        [Tooltip("N2 near-miss ranges are multiplied by this at dusk / at night (look-alikes are confusable closer in the dark).")]
        public float duskNearMissRangeFactor = 0.75f, nightNearMissRangeFactor = 0.5f;
        [Tooltip("Report node: seconds from lock (mark) to call for full / partial; beyond = late.")]
        public float reportFullSeconds = 5f, reportPartialSeconds = 12f;
        [Tooltip("N1b: seconds from first in view to lock for full / partial credit band (shown, not scored).")]
        public float perceptionFullSeconds = 4f, perceptionPartialSeconds = 10f;
        [Tooltip("Engaging with the AI cue as the only evidence (never looked at through the binoculars): Minor under Card A and the old cards, Major under B and C.")]
        public bool flagUnverifiedEngagement = true;
        [Tooltip("Process penalty units per severity (doc §5.4.4).")]
        public int penaltyMinor = 10, penaltyMajor = 40, penaltyCritical = 100;
        [Tooltip("d' and c are shown only with at least this many signal and noise trials.")]
        public int sdtMinimumTrials = 20;

        [Header("Team roles (LAN, doc §5.4.7): the sentry > commander chain")]
        [Tooltip("A commander's clock miss is waived when the sentry's report reached them later than (deadline minus this many seconds) and they had not locked the contact themselves before it came. Placeholder.")]
        public float commanderMinSeconds = 8f;

        [Header("Swarm triage (prototype)")]
        [Tooltip("Best triage score: 2 for acting on the nearest members first, 1 for letting none reach the post.")]
        public int swarmTriageMaxPoints = 3;

        [Header("Card change node")]
        [Tooltip("Decisions already committed within this many seconds of a change are judged on the old card.")]
        public float cardChangeGraceSeconds = 3f;
        [Tooltip("Best score for adapting to a card change; each stale-card decision costs one point, a slow or missing adaptation more.")]
        public int cardChangeMaxPoints = 3;
        public float cardChangeSlowSeconds = 60f;

        public static List<ReleasePoint> DefaultReleasePoints() => new List<ReleasePoint>
        {
            new ReleasePoint { truth = ContactClass.HostileQuad, range = 150f, fallbackSeconds = 90f },        // strike: payload release over the post
            new ReleasePoint { truth = ContactClass.HostileFixedWing, range = 300f, fallbackSeconds = 120f },  // recce: overflight
        };

        public ReleasePoint Release(ContactClass c)
        {
            foreach (var p in releasePoints) if (p.truth == c) return p;
            return null;
        }

        public CardDefinition Card(RoeCard c)
        {
            foreach (var d in cards) if (d.card == c) return d;
            return null;
        }

        public int Points(Severity s)
        {
            switch (s)
            {
                case Severity.Sound: return soundPoints;
                case Severity.Minor: return minorPoints;
                case Severity.Major: return majorPoints;
                default: return criticalPoints;
            }
        }

        public static ThreatGroup Group(ContactClass c)
        {
            switch (c)
            {
                case ContactClass.HostileQuad: return ThreatGroup.Strike;        // the payload quad runs in on the post
                case ContactClass.HostileFixedWing: return ThreatGroup.Recce;
                case ContactClass.FriendlyISR: return ThreatGroup.Own;
                case ContactClass.CivilianQuad: return ThreatGroup.Civilian;
                case ContactClass.Bird: return ThreatGroup.Bird;
                default: return ThreatGroup.Manned;
            }
        }

        static CardRow Row(ThreatGroup g, Severity hold, Severity report, Severity jam, Severity engage) => new CardRow { truth = g, hold = hold, report = report, jam = jam, engage = engage };

        // The doc's matrix, rows = truth, columns = hold (past deadline) / report-up / jam / engage.
        public static List<CardDefinition> DefaultCards()
        {
            const Severity S = Severity.Sound, Mi = Severity.Minor, Ma = Severity.Major, C = Severity.Critical;
            return new List<CardDefinition>
            {
                new CardDefinition { card = RoeCard.CardA, title = "Card A - attack in progress", wording = "Attack in progress: neutralise hostile drones. Holding a strike drone is the worst error.",
                    rows = { Row(ThreatGroup.Strike, C, C, S, S), Row(ThreatGroup.Recce, Ma, Ma, S, S), Row(ThreatGroup.Own, S, S, Mi, Ma), Row(ThreatGroup.Civilian, S, S, Mi, Ma), Row(ThreatGroup.Bird, S, S, Mi, Mi), Row(ThreatGroup.Manned, S, S, Ma, C) } },
                new CardDefinition { card = RoeCard.CardB, title = "Card B - engage only positively identified hostile", wording = "Alert: jam or engage only a positively identified hostile. Report everything else.",
                    rows = { Row(ThreatGroup.Strike, Ma, Ma, S, S), Row(ThreatGroup.Recce, Mi, S, S, S), Row(ThreatGroup.Own, S, S, Ma, C), Row(ThreatGroup.Civilian, S, S, Ma, C), Row(ThreatGroup.Bird, S, Mi, Mi, Ma), Row(ThreatGroup.Manned, S, S, Ma, C) } },
                new CardDefinition { card = RoeCard.CardC, title = "Card C - own drones up: report and track", wording = "Own drones up: report and track. Engage only in self-defence or on order.",
                    rows = { Row(ThreatGroup.Strike, Ma, S, S, S), Row(ThreatGroup.Recce, Mi, S, Mi, Ma), Row(ThreatGroup.Own, S, S, Ma, C), Row(ThreatGroup.Civilian, S, S, Ma, C), Row(ThreatGroup.Bird, S, Mi, Mi, Ma), Row(ThreatGroup.Manned, S, S, Ma, C) } },
            };
        }

        [Header("Exclusions")]
        [Tooltip("Contacts that spawned within this many seconds of the end and were never detected are not scored.")]
        public float lateSpawnSeconds = 30f;
        [Tooltip("On: a contact that was never detectable at all (out of range, hidden behind terrain, or too small to pick out even through binoculars, for its whole time in the air) is listed as \"not detectable\" and left out of the maximum score. Off: it counts as missed.")]
        public bool excludeNeverDetectable = true;

        public static RuleSet Defaults()
        {
            var r = new RuleSet();

            // A quad and a bird are easy to confuse as specks at long range.
            r.nearMisses.Add(new NearMissRule { truth = ContactClass.HostileQuad, call = PlayerCall.Bird, minRange = 600f });
            r.nearMisses.Add(new NearMissRule { truth = ContactClass.CivilianQuad, call = PlayerCall.Bird, minRange = 600f });
            r.nearMisses.Add(new NearMissRule { truth = ContactClass.Bird, call = PlayerCall.HostileQuad, minRange = 600f });
            r.nearMisses.Add(new NearMissRule { truth = ContactClass.Bird, call = PlayerCall.CivilianDrone, minRange = 600f });

            // Non-hostile contacts, any card.
            foreach (var cat in new[] { ContactCategory.Own, ContactCategory.Civilian, ContactCategory.Bird })
            {
                r.actionRules.Add(Any(cat, PlayerAction.Report, 2, true, null));
                r.actionRules.Add(Any(cat, PlayerAction.Hold, 2, true, null));
                r.actionRules.Add(Any(cat, PlayerAction.Jam, 0, false, null));
                r.actionRules.Add(Any(cat, PlayerAction.Engage, -5, false, "false engagement"));
            }

            // Hostiles under Weapons Hold.
            r.actionRules.Add(On(RoeCard.WeaponsHold, ContactCategory.Hostile, PlayerAction.Report, 2, true, null));
            r.actionRules.Add(On(RoeCard.WeaponsHold, ContactCategory.Hostile, PlayerAction.Jam, 2, true, null));
            r.actionRules.Add(On(RoeCard.WeaponsHold, ContactCategory.Hostile, PlayerAction.Engage, 0, false, "engaged under hold"));
            r.actionRules.Add(On(RoeCard.WeaponsHold, ContactCategory.Hostile, PlayerAction.Hold, 0, false, null));

            // Hostiles under Under Attack.
            r.actionRules.Add(On(RoeCard.UnderAttack, ContactCategory.Hostile, PlayerAction.Jam, 2, true, null));
            r.actionRules.Add(On(RoeCard.UnderAttack, ContactCategory.Hostile, PlayerAction.Engage, 2, true, null));
            r.actionRules.Add(On(RoeCard.UnderAttack, ContactCategory.Hostile, PlayerAction.Hold, -5, false, "held fire on a hostile under attack"));
            r.actionRules.Add(On(RoeCard.UnderAttack, ContactCategory.Hostile, PlayerAction.Report, 0, false, null));

            // Manned aircraft, any card.
            r.actionRules.Add(Any(ContactCategory.Manned, PlayerAction.Engage, -10, false, "engaged a manned aircraft"));
            return r;
        }

        static ActionRule Any(ContactCategory category, PlayerAction action, int points, bool correct, string flag)
        {
            return new ActionRule { anyCard = true, category = category, action = action, points = points, correct = correct, flag = flag };
        }

        static ActionRule On(RoeCard card, ContactCategory category, PlayerAction action, int points, bool correct, string flag)
        {
            return new ActionRule { card = card, category = category, action = action, points = points, correct = correct, flag = flag };
        }

        // Card-specific rule wins over an any-card rule. Null = no rule.
        public ActionRule Find(RoeCard card, ContactCategory category, PlayerAction action)
        {
            ActionRule fallback = null;
            foreach (var rule in actionRules)
            {
                if (rule.category != category || rule.action != action) continue;
                if (!rule.anyCard && rule.card == card) return rule;
                if (rule.anyCard && fallback == null) fallback = rule;
            }
            return fallback;
        }
    }

    [CreateAssetMenu(menuName = "Kurukshetra/Scoring Rules", fileName = "ScoringRules")]
    public class ScoringRules : ScriptableObject
    {
        public RuleSet rules = RuleSet.Defaults();

        [ContextMenu("Reset To Defaults")]
        void ResetToDefaults()
        {
            rules = RuleSet.Defaults();
        }
    }
}
