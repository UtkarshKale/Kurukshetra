using UnityEngine;

namespace Chaukanna.Scenario
{
    // A promoted crowd drone keeps flying its closed-form path: every frame the transform is set from CrowdMath at the scenario
    // time, until the drone is jammed (then ContactMotion sinks it) or its time is up (it leaves the sector).
    // The contact's ContactMotion is Frozen so it does not fly by itself; jamming still works because ContactMotion checks
    // jammed before Frozen.
    public class CrowdFollower : MonoBehaviour
    {
        CrowdSky sky;
        CrowdAgent agent;
        Contact contact;
        ContactMotion motion;

        public void Init(CrowdSky owner, CrowdAgent a)
        {
            sky = owner;
            agent = a;
            contact = GetComponent<Contact>();
            motion = GetComponent<ContactMotion>();
        }

        void Update()
        {
            if (contact == null || sky == null) return;
            if (motion != null && motion.Jammed) { enabled = false; return; }
            float t = ContactRegistry.ScenarioTime;
            if (t >= agent.endT) { contact.Despawn("ExitedSector"); return; }
            if (!sky.Agents.IsCreated) { enabled = false; return; }     // the crowd was torn down (restart): this contact goes with the run
            transform.position = sky.WorldPos(agent, t);
            transform.rotation = Quaternion.Euler(0f, CrowdMath.Yaw(agent, t) * Mathf.Rad2Deg, 0f);
        }
    }
}
