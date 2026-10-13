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

        public void Initialise(int id, ContactDefinition definition, Material[] contactMaterials)
        {
            Id = id;
            Definition = definition;
            TruthClass = definition.contactClass;
            SpawnTime = ContactRegistry.ScenarioTime;
            materials = contactMaterials ?? new Material[0];
            ownColours = new Color[materials.Length];
            for (int i = 0; i < materials.Length; i++) ownColours[i] = materials[i].color;
            Data = new ContactRecord { id = id, truthClass = TruthClass, spawnTime = SpawnTime };
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

        // The player's call. Can be changed until an action other than Hold is taken.
        public bool Classify(PlayerCall call, float range)
        {
            if (Data.Decided || call == PlayerCall.None) return false;
            Data.call = call;
            Data.callTime = ContactRegistry.ScenarioTime;
            Data.callRange = range;
            ContactRegistry.Record(ContactEventType.Classify, this, "called " + call.Label());
            return true;
        }

        // Report and Jam are final. Hold is not: the player can act again later and the latest action is the one scored.
        // Engage goes through Shot() so a miss can be followed up.
        public bool Act(PlayerAction action, string result)
        {
            if (Data.Decided || Data.call == PlayerCall.None || action == PlayerAction.None || action == PlayerAction.Engage) return false;
            if (action == PlayerAction.Hold)
            {
                if (Data.OnHold) return false;   // already holding: nothing to do
                Data.holds++;
                if (Data.holdStartTime < 0f) Data.holdStartTime = ContactRegistry.ScenarioTime;
            }
            else CloseHold();
            Data.action = action;
            Data.actionTime = ContactRegistry.ScenarioTime;
            Data.actionResult = result;
            Data.roeAtAction = ContactRegistry.CurrentRoe;
            ContactRegistry.Record(ContactEventType.Action, this, action + (string.IsNullOrEmpty(result) ? "" : ": " + result));
            return true;
        }
        // One Engage shot. The first shot is the decision (time and ROE card are taken from it); after a miss the player may fire again.
        public bool Shot(bool hit, string result)
        {
            if (Data.call == PlayerCall.None) return false;
            if (!Data.Decided)
            {
                CloseHold();
                Data.action = PlayerAction.Engage;
                Data.actionTime = ContactRegistry.ScenarioTime;
                Data.roeAtAction = ContactRegistry.CurrentRoe;
            }
            else if (!CanFireAgain) return false;

            Data.shots++;
            Data.engageHit = hit;
            Data.actionResult = result + (Data.shots > 1 ? $", shot {Data.shots}" : "");
            ContactRegistry.Record(ContactEventType.Action, this, "Engage: " + Data.actionResult);
            return true;
        }

        // A held contact is being finally acted on: fix how long it was held, and how much of that was under Under Attack.
        void CloseHold()
        {
            if (!Data.OnHold || Data.holdStartTime < 0f) return;
            float now = ContactRegistry.ScenarioTime;
            Data.holdSeconds = now - Data.holdStartTime;
            if (ContactRegistry.CurrentRoe == RoeCard.UnderAttack)
                Data.holdUnderAttackSeconds = Mathf.Max(0f, now - Mathf.Max(Data.holdStartTime, ContactRegistry.RoeSince));
        }

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
