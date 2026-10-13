using System;
using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Scoring
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

    [Serializable]
    public class NearMissRule
    {
        public ContactClass truth;
        public PlayerCall call;
        [Tooltip("Only a near-miss when the call was made beyond this range (metres). 0 = at any range.")]
        public float minRange;
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

        [Header("Exclusions")]
        [Tooltip("Contacts that spawned within this many seconds of the end and were never detected are not scored.")]
        public float lateSpawnSeconds = 30f;

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

    [CreateAssetMenu(menuName = "Chaukanna/Scoring Rules", fileName = "ScoringRules")]
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
