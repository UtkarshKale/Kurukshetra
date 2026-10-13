using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scenario
{
    public enum ContactEventType { Spawn, Despawn, FirstInView, Lock, Unlock, Classify, Action, AiHint }

    // One row per contact for the AAR. Outlives the contact's GameObject. Times are scenario seconds; negative = never.
    public class ContactRecord
    {
        public int id;
        public ContactClass truthClass;
        public float spawnTime;
        public float despawnTime = -1f;
        public string despawnReason;
        public float firstDetectableTime = -1f;     // could have been seen through binoculars, wherever the player was looking
        public float firstInViewTime = -1f;
        public float firstLockTime = -1f;
        public PlayerCall call = PlayerCall.None;   // latest call
        public float callTime = -1f;
        public float callRange = -1f;               // metres, at the latest call
        public PlayerAction action = PlayerAction.None;
        public float actionTime = -1f;
        public string actionResult;                 // e.g. "hit", "miss", "no effect"
        public RoeCard roeAtAction;                 // card in force when the action was taken

        public PlayerCall aiHint = PlayerCall.None; // what the AI assist said at first lock; None = never locked
        public int aiConfidence;                    // percent, as shown to the player
        public bool aiHintWrong;                    // never shown to the player
        public int holds;                           // times the player chose Hold on this contact
        public float holdStartTime = -1f;           // first Hold
        public float holdSeconds;                   // from first Hold to the final non-Hold action (0 while still holding)
        public float holdUnderAttackSeconds;        // the part of that spent with the ROE card at Under Attack
        public int shots;                           // Engage only
        public bool engageHit;

        // A decision has been made. This is what scoring looks at.
        public bool Actioned => action != PlayerAction.None;
        // Nothing more can be done. An Engage that has only missed so far is not resolved: the player may fire again.
        // Hold means "wait and watch": the player can change the call or act again, and the latest action is what gets scored.
        public bool Resolved => Actioned && action != PlayerAction.Hold && (action != PlayerAction.Engage || engageHit);
        public bool OnHold => action == PlayerAction.Hold;
        public bool Decided => Actioned && !OnHold;   // committed to something other than Hold
    }

    public struct ContactEvent
    {
        public float time;          // scenario seconds
        public ContactEventType type;
        public int contactId;
        public ContactClass truthClass;
        public Vector3 position;
        public string note;         // despawn reason

        public override string ToString()
        {
            return $"[{time,6:F1}s] {type} #{contactId} {truthClass} at {position:F0}"
                + (string.IsNullOrEmpty(note) ? "" : $" ({note})");
        }
    }

    // Ground truth for the AAR and truth map: live contacts plus a spawn/despawn log.
    public static class ContactRegistry
    {
        public static readonly List<Contact> Live = new List<Contact>();
        public static readonly List<ContactEvent> Events = new List<ContactEvent>();
        public static readonly List<ContactRecord> Records = new List<ContactRecord>();
        public static readonly List<string> Feed = new List<string>();   // every event as text, plus notes, for the trainer's live feed

        public static float ScenarioTime;
        public static bool TruthColours;
        public static RoeCard CurrentRoe;           // set by the ROE banner
        public static float RoeSince;               // scenario time the current card came into force
        public static bool InputBlocked;            // true while the trainer's AAR overlay is open; player controls ignore input

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Live.Clear();
            Events.Clear();
            Records.Clear();
            Feed.Clear();
            ScenarioTime = 0f;
            TruthColours = false;
        }

        public static void Register(Contact c)
        {
            Live.Add(c);
            Records.Add(c.Data);
            Record(ContactEventType.Spawn, c, null);
        }

        public static void Unregister(Contact c, string reason)
        {
            if (!Live.Remove(c)) return;
            c.Data.despawnTime = ScenarioTime;
            c.Data.despawnReason = reason;
            Record(ContactEventType.Despawn, c, reason);
        }

        // Something that is not about one contact, e.g. an ROE change.
        public static void Note(string message)
        {
            string text = $"[{ScenarioTime,6:F1}s] {message}";
            Feed.Add(text);
            Debug.Log("[Scenario] " + text);
        }

        public static void SetTruthColours(bool on)
        {
            TruthColours = on;
            foreach (var c in Live) c.RefreshColour();
        }

        public static void Record(ContactEventType type, Contact c, string note)
        {
            var e = new ContactEvent
            {
                time = ScenarioTime,
                type = type,
                contactId = c.Id,
                truthClass = c.TruthClass,
                position = c.transform.position,
                note = note,
            };
            Events.Add(e);
            string text = e.ToString();
        }
    }
}
