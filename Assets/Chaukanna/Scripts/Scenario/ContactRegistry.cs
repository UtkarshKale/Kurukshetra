using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scenario
{
    public enum ContactEventType { Spawn, Despawn, FirstInView, Lock, Unlock, Classify, Action, AiHint, Anomaly }

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

        // Changing one's mind. A "revision" is a change made after a final action had already been taken.
        public PlayerCall firstCall = PlayerCall.None;
        public int callChanges;                     // every change of call, before or after deciding
        public int callRevisions;                   // ...of which made after a final action
        public PlayerCall callBeforeChange = PlayerCall.None;   // the call that the latest change replaced
        public bool lastCallChangeWasRevision;
        public bool callChangeInformed;             // the anomaly had appeared between the replaced call and the new one
        public int actionRevisions;                 // a final action replaced by another
        public int uninformedActionRevisions;       // ...with nothing new to go on (these cost points)
        public bool actionLocked;                   // it physically happened (shots fired, drone jammed): cannot be replaced
        public bool group;                          // at least one decision was made on several contacts at once

        // Swarm triage: which swarm it belonged to, and how near to the post it was among the swarm when first acted on (0 = nearest).
        public int swarmId = -1;
        public int swarmRank = -1;
        // The swarm or flock (planned group, big swarm or big bird flock) it flew in; -1 = on its own. Used by the group call (K key).
        public int groupId = -1;

        // Evidence and conditions at decision time (for the process node and N2's light rule).
        public bool opticsVerified;                 // seen through the binoculars (zoomed in) while in view
        public int callLight = -1;                  // TimeOfDay when the latest call was made; -1 = unknown

        // A hostile flying an own-drone patrol to blend in. The anomaly is the moment it gives itself away.
        public bool deceptive;
        public float anomalyTime = -1f;
        public string anomalyCue;

        // Dense sky (DS-1). fromCrowd: this contact was a crowd drone that the soldier picked out and promoted.
        // IFF / Remote ID (the I key): whether it was challenged, when, and what came back. iffNoReply / iffSpoof are the
        // truth side (a cooperative drone with its transponder off; a hostile that answers as an own drone).
        public bool fromCrowd;
        public int crowdPattern = -1;
        public bool iffNoReply, iffSpoof;
        public bool iffChecked;
        public float iffAskedTime = -1f, iffTime = -1f;     // iffTime = when the reply came
        public string iffReply = "";

        public int crowdIndex = -1;                         // the crowd agent it was promoted from (LAN: every PC removes that agent)

        // LAN team play (MP-1). handled*: another seat's jam or hit took it out (written on the host by RemoteAct, on a team
        // client from the host's Handled message), so this seat's clock must not run out on it. Times are this PC's scenario
        // clock. reported*: the sentry's report as it reached this (commander) seat; reportReceivedTime = the first one.
        public string handledBy = "", handledAction = "";
        public float handledTime = -1f;
        public string reportedBy = "";
        public PlayerCall reportedCall = PlayerCall.None;
        public float reportReceivedTime = -1f;

        // Every call, action and anomaly in order, as text, for the session file and the dashboard.
        public List<string> history = new List<string>();

        public void Log(string what)
        {
            history.Add($"{ContactRegistry.ScenarioTime:F1}s {what}");
        }

        // Ground range to the defended post over time (x = scenario s, y = metres), sampled by the spawner. The scoring's
        // decision clock reads the moment a contact first came within its class's release range from this.
        public List<Vector2> rangeTrack = new List<Vector2>();
        public float closestRange = -1f;
        // Where it was and whether it was on the player's screen, at the same samples: the AAR replay is built from this
        // logged state, not re-run from the seed.
        public List<TrackPoint> track = new List<TrackPoint>();
        // What it looks like and the seed its model was picked from (a blend-in hostile looks like an own drone): what a
        // networked trainee's PC needs to build the same-looking proxy.
        public ContactClass lookClass;
        public int lookSeed;

        public void SampleRange(float time, Vector3 position, Vector3 post)
        {
            float range = Vector2.Distance(new Vector2(position.x, position.z), new Vector2(post.x, post.z));
            rangeTrack.Add(new Vector2(time, range));
            if (closestRange < 0f || range < closestRange) closestRange = range;
        }

        public void SampleTrack(float time, Vector3 position, bool inView)
        {
            track.Add(new TrackPoint { t = time, x = position.x, y = position.y, z = position.z, inView = inView });
        }

        // First sampled time within `range` metres of the post; -1 = never.
        public float FirstWithin(float range)
        {
            foreach (var s in rangeTrack) if (s.y <= range) return s.x;
            return -1f;
        }

        // A decision has been made. This is what scoring looks at.
        public bool Actioned => action != PlayerAction.None;
        // Nothing more can be done. An Engage that has only missed so far is not resolved: the player may fire again.
        // Hold means "wait and watch": the player can change the call or act again, and the latest action is what gets scored.
        public bool Resolved => Actioned && action != PlayerAction.Hold && (action != PlayerAction.Engage || engageHit);
        public bool OnHold => action == PlayerAction.Hold;
        public bool Decided => Actioned && !OnHold;   // committed to something other than Hold
    }

    [System.Serializable]
    public struct TrackPoint
    {
        public float t, x, y, z;
        public bool inView;     // inside the player's view and passing the detection test at this sample
    }

    // The run as logged for the replay: the post, the sector, and where the player was looking, twice a second.
    public static class RunLog
    {
        public static Vector3 post;
        public static float sectorYaw, spawnRadius;
        public static readonly List<ViewPoint> view = new List<ViewPoint>();

        public static void Clear()
        {
            view.Clear();
        }
    }

    [System.Serializable]
    public struct ViewPoint
    {
        public float t, yaw, pitch, fov;    // fov = horizontal degrees
        public float x, z;                  // where the soldier stood
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
        public static readonly List<RoeChange> RoeChanges = new List<RoeChange>();   // every change of card this run
        public static RoeCard RoeAtStart;                                             // card at the start of this run
        public static bool InputBlocked;            // true while the trainer's AAR overlay is open; player controls ignore input

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reset()
        {
            Live.Clear();
            Events.Clear();
            Records.Clear();
            Chaukanna.Scoring.GroupCalls.Reset();
            Feed.Clear();
            RoeChanges.Clear();
            RunLog.Clear();
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
