using UnityEngine;

namespace Chaukanna.Scenario
{
    // Simple scripted motion: no physics, no navmesh.
    public class ContactMotion : MonoBehaviour
    {
        const float ArriveDistance = 12f;

        FlightBehaviour behaviour;
        float speed;
        Vector3 goal;           // destination, or orbit centre for Loiter
        float orbitRadius;
        System.Random rng;
        Contact contact;

        bool orbiting;
        float orbitAngle;
        float orbitDir;

        float baseAltitude, bobPhase;
        float wobble, wobbleTarget, nextWobbleTime;
        float age;
        Transform wingL, wingR;

        public void Initialise(Contact contact, FlightBehaviour behaviour, float speed, Vector3 goal,
            float orbitRadius, int seed, Transform wingL, Transform wingR)
        {
            this.contact = contact;
            this.behaviour = behaviour;
            this.speed = speed;
            this.goal = goal;
            this.orbitRadius = orbitRadius;
            this.wingL = wingL;
            this.wingR = wingR;
            rng = new System.Random(seed);
            orbitDir = rng.Next(2) == 0 ? -1f : 1f;
            bobPhase = (float)rng.NextDouble() * Mathf.PI * 2f;
            baseAltitude = transform.position.y;
            Face(goal - transform.position);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            age += dt;
            for (int i = 0; i < rotors.Length; i++)
                if (rotors[i] != null) rotors[i].Rotate(Vector3.up, RotorDegreesPerSecond * (jammed ? 0.25f : 1f) * ((i & 1) == 0 ? 1f : -1f) * dt, Space.World);
            if (jammed) { JammedDescent(dt); return; }
            if (Frozen) return;
            lateClamp = true;
            if (Group != null) return;          // a swarm or flock member: its group moves it (SwarmGroup.Step)
            if (breakOffAfter >= 0f && age >= breakOffAfter)
            {
                // The blend-in hostile drops the act: it leaves the patrol orbit and runs in on the post.
                breakOffAfter = -1f;
                behaviour = FlightBehaviour.Approach;
                goal = breakOffGoal;
                speed *= BreakOffSpeedUp;
                contact.MarkAnomaly("left its patrol orbit and turned for the post");
            }
            switch (behaviour)
            {
                case FlightBehaviour.Approach:
                    if (FlyStraight(dt)) contact.Despawn("ReachedDefendedPoint");
                    break;
                case FlightBehaviour.Transit:
                    if (FlyStraight(dt)) contact.Despawn("ExitedSector");
                    break;
                case FlightBehaviour.Loiter:
                    Loiter(dt);
                    break;
                case FlightBehaviour.Erratic:
                    if (Erratic(dt)) contact.Despawn("ExitedSector");
                    break;
            }
        }

        // Blend-in hostiles only: after this many seconds in the air, stop patrolling and attack `target`.
        float breakOffAfter = -1f;
        Vector3 breakOffGoal;
        const float BreakOffSpeedUp = 1.35f;

        public void SetDeception(float secondsUntilBreakOff, Vector3 target)
        {
            breakOffAfter = Mathf.Max(0f, secondsUntilBreakOff);
            breakOffGoal = target;
        }

        // Rotor parts found on the model. They spin about the vertical; slower once the drone has been jammed.
        [System.NonSerialized] public Transform[] rotors = new Transform[0];
        const float RotorDegreesPerSecond = 2400f;

        bool jammed, lateClamp;
        const float MinGroundClearance = 8f;

        // Altitudes are heights above the ground: over generated terrain a contact rides up over hills instead of flying into them.
        void LateUpdate()
        {
            if (!lateClamp || jammed) return;
            float ground = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(transform.position);
            if (float.IsNaN(ground)) return;
            Vector3 p = transform.position;
            float floor = ground + MinGroundClearance;
            if (behaviour == FlightBehaviour.Approach && Vector3.Distance(p, goal) < 60f) return;   // let it come down onto the post
            if (p.y < floor) { p.y = floor; transform.position = p; }
        }

        // A straight pass to `target` (Range drill items), whatever the class normally flies like; leaves as "ExitedSector".
        public void ForceTransit(Vector3 target, float newSpeed)
        {
            behaviour = FlightBehaviour.Transit;
            goal = target;
            speed = newSpeed;
            breakOffAfter = -1f;
            Face(goal - transform.position);
        }

        // Range drill freeze: the item holds still while frozen.
        public bool Frozen;

        // Link lost: stop and sink slowly to the ground.
        public void Jam()
        {
            jammed = true;
            Chaukanna.Audio.SoundEngine.JammerPulse(transform.position);
        }

        void JammedDescent(float dt)
        {
            const float sinkRate = 3f;
            Vector3 pos = transform.position;
            pos.y -= sinkRate * dt;
            pos += transform.right * Mathf.Sin(age * 0.9f) * 0.6f * dt;   // slight drift
            transform.position = pos;
            float ground = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(pos);
            if (pos.y <= (float.IsNaN(ground) ? 0f : ground) + 0.5f) contact.Despawn("JammedDown");
        }

        // Returns true on arrival.
        bool FlyStraight(float dt)
        {
            Vector3 to = goal - transform.position;
            if (to.magnitude <= ArriveDistance) return true;
            Face(to);
            transform.position += to.normalized * speed * dt;
            return false;
        }

        void Loiter(float dt)
        {
            Vector3 pos = transform.position;
            Vector3 flat = Flat(pos - goal);
            if (!orbiting)
            {
                if (flat.magnitude > orbitRadius)
                {
                    Vector3 dir = -flat.normalized;
                    Face(dir);
                    transform.position = pos + dir * speed * dt;
                    return;
                }
                orbiting = true;
                orbitAngle = Mathf.Atan2(flat.z, flat.x);
            }
            orbitAngle += orbitDir * speed / orbitRadius * dt;
            var radial = new Vector3(Mathf.Cos(orbitAngle), 0f, Mathf.Sin(orbitAngle));
            transform.position = new Vector3(goal.x, pos.y, goal.z) + radial * orbitRadius;
            Face(new Vector3(-radial.z, 0f, radial.x) * orbitDir);
        }

        bool Erratic(float dt)
        {
            Vector3 pos = transform.position;
            Vector3 toGoal = Flat(goal - pos);
            if (toGoal.magnitude <= ArriveDistance) return true;

            if (age >= nextWobbleTime)
            {
                wobbleTarget = Mathf.Lerp(-70f, 70f, (float)rng.NextDouble());
                nextWobbleTime = age + Mathf.Lerp(0.5f, 2.5f, (float)rng.NextDouble());
            }
            wobble = Mathf.MoveTowards(wobble, wobbleTarget, 120f * dt);

            // Wobble stays under 90 degrees, so the bird always makes net progress to its exit point.
            Vector3 dir = Quaternion.AngleAxis(wobble, Vector3.up) * toGoal.normalized;
            pos += dir * speed * dt;
            pos.y = baseAltitude + Mathf.Sin(age * 1.7f + bobPhase) * 2.5f;
            transform.position = pos;
            Face(dir);

            Flap(1f);
            return false;
        }

        void Flap(float rate)
        {
            if (wingL == null || wingR == null) return;
            float flap = Mathf.Sin(age * 22f * rate + bobPhase) * 40f;
            wingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
            wingR.localRotation = Quaternion.Euler(0f, 0f, flap);
        }

        // --- Swarm and flock members: the group decides where they go. ---
        [System.NonSerialized] public SwarmGroup Group;
        public bool Jammed => jammed;
        public float Speed => speed;

        public void FlyGroupStep(Vector3 position, Vector3 velocity, float flapRate)
        {
            transform.position = position;
            Face(velocity);
            Flap(flapRate);
        }

        public void ArriveFromGroup(string reason)
        {
            contact.Despawn(reason);
        }

        void Face(Vector3 dir)
        {
            dir = Flat(dir);
            if (dir.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
