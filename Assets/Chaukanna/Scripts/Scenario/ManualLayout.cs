using System;
using System.Collections.Generic;
using UnityEngine;

namespace Chaukanna.Scenario
{
    // What the 3D scenario editor adds to a scenario besides the paths: hand-placed trees, bushes, rocks and buildings, areas where the
    // generated layers are cleared, and drone / bird groups dropped at a point. Plain data inside ScenarioConfig / ScenarioSpec (JSON),
    // so it travels with the scenario, the package and the LAN start packet. A scenario saved before these lists existed has them empty.
    public enum ManualKind { Tree = 0, Bush = 1, Building = 2, Rock = 3 }

    // One hand-placed thing. kind / group / variant name a prefab of the terrain layer kit (group: tree class or building type, variant: the
    // entry in that list, taken modulo its length so a kit with fewer entries still works). x, z are world metres; it stands on the ground there.
    [Serializable]
    public struct ManualObject
    {
        public int kind, group, variant;
        public float x, z, yaw, scale;
        public Vector2 Xz => new Vector2(x, z);
    }

    // A round area where the generated trees, bushes and buildings are left out (the manual objects are not touched).
    [Serializable]
    public struct ClearArea
    {
        public float x, z, radius;
    }

    public enum PlacedKind { HostileQuad = 0, HostileFixedWing = 1, OwnIsr = 2, CivilianQuad = 3, Swarm = 4, Flock = 5, Bird = 6 }

    // A drone or bird group dropped at a point. It appears `time` seconds into the run (at the first waypoint of `path` when one is assigned;
    // -1 = no path: hostiles fly at the post, the rest loiter over the point, groups cross over it).
    [Serializable]
    public struct PlacedGroup
    {
        public int kind;                // PlacedKind
        public int count;
        public int species;             // birds: 0 kite, 1 pigeon, 2 sparrow
        public float x, z, alt;         // alt: metres above the ground
        public float time;
        public int path;                // index into the scenario's paths; -1 = none

        public PlacedKind Kind => (PlacedKind)Mathf.Clamp(kind, 0, 6);

        public static string Label(PlacedKind k)
        {
            switch (k)
            {
                case PlacedKind.HostileQuad: return "Hostile quad";
                case PlacedKind.HostileFixedWing: return "Hostile fixed-wing";
                case PlacedKind.OwnIsr: return "Own ISR";
                case PlacedKind.CivilianQuad: return "Civilian quad";
                case PlacedKind.Swarm: return "Drone swarm";
                case PlacedKind.Flock: return "Bird flock";
                default: return "Single bird";
            }
        }

        // The path use that fits this kind.
        public static PathUse UseOf(PlacedKind k) => k == PlacedKind.Swarm ? PathUse.Swarm : k == PlacedKind.Flock ? PathUse.Flock : k == PlacedKind.Bird ? PathUse.Bird : PathUse.Drone;
    }

    public static class ManualLayout
    {
        public static List<ManualObject> CloneObjects(List<ManualObject> l) => l != null ? new List<ManualObject>(l) : new List<ManualObject>();
        public static List<ClearArea> CloneAreas(List<ClearArea> l) => l != null ? new List<ClearArea>(l) : new List<ClearArea>();
        public static List<PlacedGroup> CloneGroups(List<PlacedGroup> l) => l != null ? new List<PlacedGroup>(l) : new List<PlacedGroup>();

        // The cleared areas as zones for the terrain layers (round, trees and buildings both kept out).
        public static List<Chaukanna.TerrainGen.SiteClearance.Zone> ClearZones(List<ClearArea> areas)
        {
            var zones = new List<Chaukanna.TerrainGen.SiteClearance.Zone>();
            if (areas != null)
                foreach (var a in areas)
                    zones.Add(new Chaukanna.TerrainGen.SiteClearance.Zone { centre = new Vector2(a.x, a.z), axis = Vector2.right, halfLong = a.radius, halfShort = a.radius, blocksTrees = true, round = true });
            return zones;
        }

        // Only what cannot physically exist is corrected: a number that is not a number, a size of nothing, a count below one.
        public static void Sanitise(ScenarioConfig c)
        {
            if (c.manualObjects == null) c.manualObjects = new List<ManualObject>();
            if (c.clearAreas == null) c.clearAreas = new List<ClearArea>();
            if (c.placedGroups == null) c.placedGroups = new List<PlacedGroup>();
            for (int i = 0; i < c.manualObjects.Count; i++)
            {
                var o = c.manualObjects[i];
                if (float.IsNaN(o.x) || float.IsInfinity(o.x)) o.x = 0f;
                if (float.IsNaN(o.z) || float.IsInfinity(o.z)) o.z = 0f;
                if (float.IsNaN(o.yaw) || float.IsInfinity(o.yaw)) o.yaw = 0f;
                if (float.IsNaN(o.scale) || o.scale < 0.05f) o.scale = 1f;
                o.kind = Mathf.Clamp(o.kind, 0, 3);
                if (o.group < 0) o.group = 0;
                if (o.variant < 0) o.variant = 0;
                c.manualObjects[i] = o;
            }
            for (int i = 0; i < c.clearAreas.Count; i++)
            {
                var a = c.clearAreas[i];
                if (float.IsNaN(a.x) || float.IsNaN(a.z)) { a.x = 0f; a.z = 0f; }
                if (float.IsNaN(a.radius) || a.radius < 1f) a.radius = 1f;
                c.clearAreas[i] = a;
            }
            for (int i = 0; i < c.placedGroups.Count; i++)
            {
                var g = c.placedGroups[i];
                g.kind = Mathf.Clamp(g.kind, 0, 6);
                g.count = Mathf.Max(1, g.count);
                g.species = Mathf.Clamp(g.species, 0, 2);
                if (float.IsNaN(g.x) || float.IsNaN(g.z)) { g.x = 0f; g.z = 0f; }
                if (float.IsNaN(g.alt) || g.alt < 0f) g.alt = 0f;
                if (float.IsNaN(g.time) || g.time < 0f) g.time = 0f;
                if (g.path >= c.paths.Count || g.path < -1) g.path = -1;
                c.placedGroups[i] = g;
            }
        }
    }
}
