using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // What happens at the end of a path: fly it once and leave, fly it round and round, or fly it there and back.
    public enum PathMode { OneWay, Loop, PingPong }

    // Who flies a path. Drone = single drones (any class, or the class in FlightPath.cls); Bird = single birds;
    // Swarm = the drone swarm (one centre, the members flock round it); Flock = bird flocks (one path per flock, in turn).
    public enum PathUse { Drone, Bird, Swarm, Flock }

    // How a single drone or bird of one type flies from its spawn. Default = what its contact definition says. CustomPath = it flies a path
    // drawn on the 2D map (a drone path for drones, a bird path for birds) and, if none fits, flies as Default. Any other pattern is
    // flown as named and ignores drawn paths for that type.
    public enum FlightPattern { Default, StraightIn, Orbit, ZigZag, PopUp, TerrainFollow, Loiter, CustomPath }

    public static class FlightPatterns
    {
        public static string Label(FlightPattern p)
        {
            switch (p)
            {
                case FlightPattern.StraightIn: return "STRAIGHT IN";
                case FlightPattern.Orbit: return "ORBIT THE POST";
                case FlightPattern.ZigZag: return "ZIG-ZAG";
                case FlightPattern.PopUp: return "POP-UP";
                case FlightPattern.TerrainFollow: return "TERRAIN-FOLLOWING";
                case FlightPattern.Loiter: return "LOITER";
                case FlightPattern.CustomPath: return "CUSTOM PATH";
                default: return "DEFAULT";
            }
        }

        public static FlightPattern Next(FlightPattern p) => (FlightPattern)(((int)p + 1) % 8);
    }

    // One point of a custom path. x, z are world metres (east / north of the terrain centre, the same numbers as the Map tab).
    [Serializable]
    public struct Waypoint
    {
        public float x, z;
        public float alt;       // metres above the ground at that point (not above sea level)
        public float speed;     // m/s on the leg that ENDS at this point; 0 = the contact's own speed
        public Vector2 Xz => new Vector2(x, z);
    }

    // A path drawn by the trainer on the 2D map. Waypoint 0 is where the contact(s) appear; the rest are flown in order.
    // Saved inside ScenarioConfig / ScenarioSpec (JSON), so it travels with the scenario, the package and the LAN start packet.
    [Serializable]
    public class FlightPath
    {
        public string name = "Path";
        public PathUse use;
        [Tooltip("Single drones only: the ContactClass this path is for (0 hostile quad, 1 hostile fixed-wing, 2 own ISR, 3 civilian quad); -1 = any drone.")]
        public int cls = -1;
        public PathMode mode;
        public List<Waypoint> points = new List<Waypoint>();

        public const float DefaultDroneAlt = 100f, DefaultBirdAlt = 60f;

        public bool Usable => points != null && points.Count >= 2;

        public FlightPath Clone()
        {
            var c = (FlightPath)MemberwiseClone();
            c.points = points != null ? new List<Waypoint>(points) : new List<Waypoint>();
            return c;
        }

        public static List<FlightPath> CloneAll(List<FlightPath> list)
        {
            var copy = new List<FlightPath>();
            if (list != null) foreach (var p in list) if (p != null) copy.Add(p.Clone());
            return copy;
        }

        // Only what cannot physically be flown is corrected: a height below the ground, a negative speed, a number that is not a number.
        public void Sanitise()
        {
            if (points == null) points = new List<Waypoint>();
            for (int i = 0; i < points.Count; i++)
            {
                var w = points[i];
                if (float.IsNaN(w.x) || float.IsInfinity(w.x)) w.x = 0f;
                if (float.IsNaN(w.z) || float.IsInfinity(w.z)) w.z = 0f;
                if (float.IsNaN(w.alt) || float.IsInfinity(w.alt) || w.alt < 0f) w.alt = 0f;
                if (float.IsNaN(w.speed) || float.IsInfinity(w.speed) || w.speed < 0f) w.speed = 0f;
                points[i] = w;
            }
            if (cls < -1 || cls > 4) cls = -1;
            if (use != PathUse.Drone) cls = -1;
        }

        // Horizontal length in metres, including the closing leg of a loop.
        public float Length()
        {
            float total = 0f;
            int n = points.Count;
            for (int i = 1; i < n; i++) total += Vector2.Distance(points[i - 1].Xz, points[i].Xz);
            if (mode == PathMode.Loop && n > 2) total += Vector2.Distance(points[n - 1].Xz, points[0].Xz);
            return total;
        }

        public static string UseLabel(PathUse u) => u == PathUse.Drone ? "DRONE" : u == PathUse.Bird ? "BIRD" : u == PathUse.Swarm ? "SWARM" : "FLOCK";
        public static string ModeLabel(PathMode m) => m == PathMode.Loop ? "LOOP" : m == PathMode.PingPong ? "PING-PONG" : "ONE WAY";

        public static string ClassLabel(int cls) => cls < 0 ? "any drone" : cls == 0 ? "hostile quad" : cls == 1 ? "hostile fixed-wing" : cls == 2 ? "own ISR" : cls == 3 ? "civilian" : "bird";

        // Ground height at a world point; 0 where there is no terrain.
        public static float Ground(float x, float z)
        {
            float g = Chaukanna.TerrainGen.TerrainBuilder.GroundHeight(new Vector3(x, 0f, z));
            return float.IsNaN(g) ? 0f : g;
        }

        // A waypoint as a world position: the ground there plus its height above ground.
        public Vector3 World(int i)
        {
            var w = points[Mathf.Clamp(i, 0, points.Count - 1)];
            return new Vector3(w.x, Ground(w.x, w.z) + w.alt, w.z);
        }

        // Where a one-way path ends (its last point); a loop or ping-pong has no end, so its first leg's target stands in.
        public Vector3 EndWorld() => World(mode == PathMode.OneWay ? points.Count - 1 : 1);
    }

    // Which waypoint a path-follower is flying towards. Pure bookkeeping, shared by single contacts, small groups and the big swarm.
    public struct PathCursor
    {
        public int index;       // the waypoint being flown to
        public int from;        // the waypoint the current leg started at
        public int dir;         // +1 forwards, -1 back (ping-pong)
        public bool done;       // a one-way path has been flown to its last point

        public static PathCursor Start() => new PathCursor { index = 1, from = 0, dir = 1 };

        // Called on reaching waypoint `index` of a path with n points.
        public void Arrive(int n, PathMode mode)
        {
            if (n < 2) { done = true; return; }
            from = index;
            switch (mode)
            {
                case PathMode.OneWay:
                    if (index >= n - 1) done = true; else index++;
                    break;
                case PathMode.Loop:
                    index = (index + 1) % n;
                    break;
                default:
                    if (dir > 0 && index >= n - 1) { dir = -1; index = n - 2; }
                    else if (dir < 0 && index <= 0) { dir = 1; index = 1; }
                    else index += dir;
                    break;
            }
        }
    }
}
