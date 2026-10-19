using Unity.Mathematics;

namespace Chaukanna.Scenario
{
    // How a crowd drone moves. All six are closed-form: the position is a pure function of the agent and the scenario time.
    public enum CrowdPattern : byte { Orbit = 0, Corridor = 1, Grid = 2, Station = 3, Cluster = 4, Flock = 5 }

    // One cooperative drone or bird in the crowd (dense sky, DS-1). Plain blittable data, no GameObject, so thousands of
    // them can be moved by a Burst job. cls is a ContactClass value (FriendlyISR, CivilianQuad or Bird only; never hostile).
    //   centre.y is the altitude above the ground at the centre (m), not a world height.
    //   offset: Corridor = x is the lane offset across the corridor; Cluster / Flock = the member's place round the anchor
    //           (x right, y up, z forward of the anchor heading).
    //   Orbit / Cluster: radius, speed (m/s along the circle), dir (+1 / -1), phase (start angle).
    //   Corridor: axis, length (m), speed (m/s), phase (metres along the 2 x length round trip).
    //   Grid: axis, length (one leg), radius = lane spacing, lanes = number of legs, speed, phase (metres along the path).
    //   Station: radius = amplitude (m), speed = angular rate (rad/s), phase.
    //   Flock: radius = area of the anchor wander (m), speed (m/s at most), phase, memberPhase.
    // iff: 0 = answers IFF / Remote ID as its class should, 1 = transponder off / no Remote ID (birds are always 1).
    public struct CrowdAgent
    {
        public float3 centre;
        public float3 offset;
        public float2 axis;
        public float radius, length, lanes, speed, phase, dir, spawnT, endT, size, memberPhase;
        public byte pattern, cls, variant, iff;
    }
}
