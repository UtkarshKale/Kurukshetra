using UnityEngine;

namespace Chaukanna.Scenario
{
    // Truth data for one spawned object.
    public class Contact : MonoBehaviour
    {
        public int Id { get; private set; }
        public ContactClass TruthClass { get; private set; }
        public float SpawnTime { get; private set; }
        public ContactDefinition Definition { get; private set; }

        // Player-side truth for the AAR. Scenario seconds; negative means "never".
        public ContactRecord Data { get; private set; }
        public float FirstInViewTime => Data.firstInViewTime;
        public float FirstLockTime => Data.firstLockTime;
        public PlayerCall Call => Data.call;
        public PlayerAction Action => Data.action;
        public bool Resolved => Data.Resolved;
        public bool Actioned => Data.Actioned;
        public bool CanFireAgain => Data.action == PlayerAction.Engage && !Data.engageHit;
        public bool OnHold => Data.OnHold;
        public bool Decided => Data.Decided;

        Material[] materials = new Material[0];
        Color[] ownColours = new Color[0];      // each material's own colour, restored when the debug tint is off
        string despawnReason;

        public void Initialise(int id, ContactDefinition definition, Material[] contactMaterials, bool deceptive = false)
        {
            Id = id;
            Definition = definition;
            TruthClass = definition.contactClass;
            SpawnTime = ContactRegistry.ScenarioTime;
            materials = contactMaterials ?? new Material[0];
            ownColours = new Color[materials.Length];
            for (int i = 0; i < materials.Length; i++) ownColours[i] = materials[i].color;
            Data = new ContactRecord { id = id, truthClass = TruthClass, spawnTime = SpawnTime, deceptive = deceptive };
            RefreshColour();
            ContactRegistry.Register(this);
        }

        public void MarkDetectable()
        {
            if (Data.firstDetectableTime < 0f) Data.firstDetectableTime = ContactRegistry.ScenarioTime;
        }

        public void MarkFirstInView(string note)
        {
            if (FirstInViewTime >= 0f) return;
            Data.firstInViewTime = ContactRegistry.ScenarioTime;
            ContactRegistry.Record(ContactEventType.FirstInView, this, note);
        }

        public void MarkLocked(string note)
        {
            if (FirstLockTime < 0f) Data.firstLockTime = ContactRegistry.ScenarioTime;
            ContactRegistry.Record(ContactEventType.Lock, this, note);
        }

        // True when the action can still be replaced: nothing final yet, or a final action that did not physically happen
        // (a Report, or a Jam that had no effect). Shots fired and a drone jammed down cannot be taken back.
        public bool CanChangeAction => !Data.Decided || !Data.actionLocked;
        public bool Deceptive => Data.deceptive;
        public bool AnomalyShown => Data.anomalyTime >= 0f;

        // The player's call. It can be changed at any time, also after the contact has been acted on: the latest call is
        // the one scored, and a change after a final action is recorded as a revision (see ScoringRules).
        public bool Classify(PlayerCall call, float range, bool group = false)
        {
            if (call == PlayerCall.None || call == Data.call) return false;
            string note = "called " + call.Label();
            if (Data.call == PlayerCall.None) Data.firstCall = call;
            else
            {
                Data.callChanges++;
                Data.lastCallChangeWasRevision = Data.Decided;
                if (Data.Decided) Data.callRevisions++;
                Data.callBeforeChange = Data.call;
                Data.callChangeInformed = Data.anomalyTime >= 0f && Data.callTime < Data.anomalyTime;
                note = $"changed call {Data.call.Label()} > {call.Label()}" + (Data.Decided ? " (after acting)" : "") + (Data.callChangeInformed ? " (after the anomaly)" : "");
            }
            Data.call = call;
            Data.callTime = ContactRegistry.ScenarioTime;
            Data.callRange = range;
            Data.callLight = (int)RunConditions.Time;
            if (group) { Data.group = true; note += " [group]"; }
            Data.Log(note);
            ContactRegistry.Record(ContactEventType.Classify, this, note);
            return true;
        }

        // Hold is never final. Report can be replaced later, and so can a Jam that had no effect; `locks` marks an action
        // that physically happened and cannot be replaced. Engage goes through Shot() so a miss can be followed up.
        public bool Act(PlayerAction action, string result, bool group = false, bool locks = false)
        {
            if (Data.call == PlayerCall.None || action == PlayerAction.None || action == PlayerAction.Engage) return false;
            if (action == Data.action) return false;            // the same again: nothing to do
            string was = "";
            if (Data.Decided)
            {
                if (Data.actionLocked) return false;
                was = NoteActionRevision();
            }
            if (action == PlayerAction.Hold)
            {
                Data.holds++;
                if (Data.holdStartTime < 0f) Data.holdStartTime = ContactRegistry.ScenarioTime;
            }
            else CloseHold();
            Data.action = action;
            Data.actionTime = ContactRegistry.ScenarioTime;
            Data.actionResult = result;
            Data.roeAtAction = ContactRegistry.CurrentRoe;
            Data.actionLocked = locks;
            string note = was + action + (string.IsNullOrEmpty(result) ? "" : ": " + result);
            if (group) { Data.group = true; note += " [group]"; }
            Data.Log(note);
            ContactRegistry.Record(ContactEventType.Action, this, note);
            return true;
        }

        // One Engage shot. The first shot is the decision (time and ROE card are taken from it); after a miss the player may fire again.
        public bool Shot(bool hit, string result, bool group = false)
        {
            if (Data.call == PlayerCall.None) return false;
            string was = "";
            if (Data.action != PlayerAction.Engage)
            {
                if (Data.Decided)
                {
                    if (Data.actionLocked) return false;
                    was = NoteActionRevision();
                }
                CloseHold();
                Data.action = PlayerAction.Engage;
                Data.actionTime = ContactRegistry.ScenarioTime;
                Data.roeAtAction = ContactRegistry.CurrentRoe;
            }
            else if (!CanFireAgain) return false;

            Data.actionLocked = true;
            Data.shots++;
            Data.engageHit = hit;
            Data.actionResult = result + (Data.shots > 1 ? $", shot {Data.shots}" : "");
            string note = was + "Engage: " + Data.actionResult;
            if (group) { Data.group = true; note += " [group]"; }
            Data.Log(note);
            ContactRegistry.Record(ContactEventType.Action, this, note);
            return true;
        }

        // A final action is being replaced. New information since it was taken (the contact's anomaly, or a new ROE card)
        // makes the change a reaction rather than a second guess.
        string NoteActionRevision()
        {
            Data.actionRevisions++;
            bool informed = (Data.anomalyTime >= 0f && Data.actionTime < Data.anomalyTime) || ContactRegistry.RoeSince > Data.actionTime;
            if (!informed) Data.uninformedActionRevisions++;
            return $"changed action {Data.action} > ";
        }

        // A blend-in hostile gives itself away. Called once by its motion.
        public void MarkAnomaly(string cue)
        {
            if (Data.anomalyTime >= 0f) return;
            Data.anomalyTime = ContactRegistry.ScenarioTime;
            Data.anomalyCue = cue;
            Data.Log("ANOMALY: " + cue);
            ContactRegistry.Record(ContactEventType.Anomaly, this, cue);
            ContactRegistry.Note($"#{Id:00} (blend-in hostile) {cue}");      // trainer's feed only; the player sees a sensor cue on the lock label
        }
        // A held contact is being finally acted on: fix how long it was held, and how much of that was under Under Attack.
        void CloseHold()
        {
            if (!Data.OnHold || Data.holdStartTime < 0f) return;
            float now = ContactRegistry.ScenarioTime;
            Data.holdSeconds = now - Data.holdStartTime;
            if (RoeCards.IsAttack(ContactRegistry.CurrentRoe))
                Data.holdUnderAttackSeconds = Mathf.Max(0f, now - Mathf.Max(Data.holdStartTime, ContactRegistry.RoeSince));
        }

        // Despawn reason for a contact the trainer deleted from the truth map. Not held against the player unless they had acted.
        public const string RemovedByTrainer = "Removed by trainer";

        public void Despawn(string reason)
        {
            despawnReason = reason;
            Destroy(gameObject);
        }

        public void RefreshColour()
        {
            // Debug tint (F1) paints the whole contact in its class colour; otherwise it wears its own colours.
            for (int i = 0; i < materials.Length; i++)
                if (materials[i] != null) materials[i].color = ContactRegistry.TruthColours ? ContactVisuals.TruthColour(TruthClass) : ownColours[i];
        }

        void OnDestroy()
        {
            if (Data != null) ContactRegistry.Unregister(this, despawnReason ?? "Destroyed");
            foreach (var m in materials) if (m != null) Destroy(m);
        }
    }
}
