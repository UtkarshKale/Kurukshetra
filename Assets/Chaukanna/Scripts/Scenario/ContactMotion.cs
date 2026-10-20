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
            spawnFlat = Mathf.Max(1f, Flat(goal - transform.position).magnitude);
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
                path = null;                    // a blend-in hostile drops its route as well as its patrol
                behaviour = FlightBehaviour.Approach;
                goal = breakOffGoal;
                speed *= BreakOffSpeedUp;
                contact.MarkAnomaly("left its patrol orbit and turned for the post");
            }
            if (path != null)
            {
                if (FollowPath(dt)) contact.Despawn(pathEndReason);
                return;
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
                case FlightBehaviour.ZigZag:
                case FlightBehaviour.PopUp:
                case FlightBehaviour.TerrainFollow:
                    if (FlyPattern(dt)) contact.Despawn(patternEnd);
                    break;
            }
        }

        // --- Patterns picked in the scenario options (zig-zag, pop-up, terrain-following): fly to the goal over the ground. ---
        string patternEnd = "ExitedSector";
        float patternAgl = 30f, popRange = 350f, spawnFlat = 1f, zigPhase, zigPeriod = 4f;
        float popTargetAgl = 90f;
        const float ZigDegrees = 38f;

        // Called by the spawner right after Initialise. agl = height above ground to hold (terrain-following), the low run-in height and
        // the height reached (pop-up).
        public void SetPattern(string endReason, float agl)
        {
            patternEnd = endReason;
            patternAgl = Mathf.Max(2f, behaviour == FlightBehaviour.PopUp ? Mathf.Min(agl, 15f) : agl);
            popTargetAgl = Mathf.Max(agl, 60f);
            popRange = Mathf.Max(150f, spawnFlat * 0.3f);
            zigPeriod = Mathf.Lerp(3f, 6f, (float)rng.NextDouble());
            zigPhase = (float)rng.NextDouble() * Mathf.PI * 2f;
        }

        bool FlyPattern(float dt)
        {
            Vector3 pos = transform.position;
            Vector3 flat = Flat(goal - pos);
            float dist = flat.magnitude;
            if (dist <= ArriveDistance) return true;
            Vector3 dir = flat / dist;
            if (behaviour == FlightBehaviour.ZigZag) dir = Quaternion.AngleAxis(Mathf.Sin(age * Mathf.PI * 2f / zigPeriod + zigPhase) * ZigDegrees, Vector3.up) * dir;
            pos += dir * speed * dt;
            float ground = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(pos);
            if (float.IsNaN(ground)) ground = 0f;
            float targetY;
            switch (behaviour)
            {
                case FlightBehaviour.TerrainFollow: targetY = ground + patternAgl; break;
                case FlightBehaviour.PopUp: targetY = ground + (dist > popRange ? patternAgl : popTargetAgl); break;
                default: targetY = Mathf.Lerp(baseAltitude, goal.y, 1f - Mathf.Clamp01(dist / spawnFlat)); break;       // zig-zag: the usual glide to the goal height
            }
            pos.y = Mathf.MoveTowards(pos.y, targetY, (behaviour == FlightBehaviour.PopUp ? 18f : 25f) * dt);
            transform.position = pos;
            Face(dir);
            Flap(1f);
            return false;
        }

        // --- A custom path from the 2D editor (FlightPath): waypoints with their own height above ground and speed. ---
        FlightPath path;
        PathCursor cursor;
        string pathEndReason = "ExitedSector";
        float pathSpeedScale = 1f, segFromAlt, segLength = 1f;
        Vector2 segFrom;
        const float PathClearance = 2f;

        public bool OnPath => path != null;

        // From now on this contact flies `p` instead of its usual pattern. Waypoint 0 is where it is (the spawner puts it there).
        // endReason is what a one-way path logs when it is flown out (ReachedDefendedPoint for a hostile that ends at the post).
        public void SetPath(FlightPath p, string endReason, float speedScale)
        {
            if (p == null || !p.Usable) return;
            path = p;
            pathEndReason = endReason;
            pathSpeedScale = speedScale;
            cursor = PathCursor.Start();
            BeginLeg();
            var w = p.points[1];
            Face(new Vector3(w.x - transform.position.x, 0f, w.z - transform.position.z));
        }

        void BeginLeg()
        {
            var a = path.points[Mathf.Clamp(cursor.from, 0, path.points.Count - 1)];
            var b = path.points[Mathf.Clamp(cursor.index, 0, path.points.Count - 1)];
            segFrom = a.Xz;
            segFromAlt = a.alt;
            segLength = Mathf.Max(1f, Vector2.Distance(a.Xz, b.Xz));
        }

        // One frame along the path. True when a one-way path has been flown out.
        bool FollowPath(float dt)
        {
            var wp = path.points[cursor.index];
            Vector3 pos = transform.position;
            float dx = wp.x - pos.x, dz = wp.z - pos.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            float sp = wp.speed > 0f ? wp.speed * pathSpeedScale : speed;
            if (dist <= Mathf.Max(2f, sp * dt))
            {
                cursor.Arrive(path.points.Count, path.mode);
                if (cursor.done) return true;
                BeginLeg();
                return false;
            }
            float step = sp * dt;
            pos.x += dx / dist * step;
            pos.z += dz / dist * step;
            // Height above ground blends from the leg's first waypoint to its last; the ground under the contact is added.
            float t = Mathf.Clamp01(1f - dist / segLength);
            float ground = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(pos);
            float targetY = (float.IsNaN(ground) ? 0f : ground) + Mathf.Lerp(segFromAlt, wp.alt, t);
            if (behaviour == FlightBehaviour.Erratic) targetY += Mathf.Sin(age * 1.7f + bobPhase) * 2.5f;
            pos.y = Mathf.MoveTowards(pos.y, targetY, Mathf.Max(20f, sp) * dt);
            transform.position = pos;
            Face(new Vector3(dx, 0f, dz));
            if (behaviour == FlightBehaviour.Erratic) Flap(1f);
            return false;
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
            float floor = ground + (path != null ? PathClearance : MinGroundClearance);
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
