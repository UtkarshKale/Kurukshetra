using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Trainer
{
    // The "Spawn and flight options" block of the scenario settings, shared by the trainer menu's SCENARIO tab and the F6 panel so both
    // edit the same ScenarioConfig fields. Every number is typeable with no upper limit (only values that cannot physically work are
    // corrected, in ScenarioConfig.Validate). The flight patterns for single drones and birds sit beside the 2D path editor: a type set to
    // CUSTOM PATH flies the paths drawn on the map.
    public static class ScenarioOptionsPanel
    {
        // A float field shown as a whole number: the value only changes when the typed / stepped number differs, so a stored 12.5 is not rounded by drawing it.
        static float Step(string id, string name, float value, int step, int min, GUIStyle label, GUIStyle field, GUIStyle button, float row, float labelWidth)
        {
            int shown = Mathf.RoundToInt(value);
            int now = NumberEntry.Stepper(id, name, shown, step, min, int.MaxValue, label, field, button, row, labelWidth);
            return now != shown ? now : value;
        }

        static int PathsFor(ScenarioConfig c, ContactClass cls)
        {
            int n = 0;
            if (c.paths == null) return 0;
            var use = cls == ContactClass.Bird ? PathUse.Bird : PathUse.Drone;
            foreach (var p in c.paths) if (p != null && p.Usable && p.use == use && (use != PathUse.Drone || p.cls < 0 || p.cls == (int)cls)) n++;
            return n;
        }

        static void Pattern(ScenarioConfig c, ContactClass cls, string name, GUIStyle button, float row)
        {
            var p = c.PatternFor(cls);
            string text = name + ": <b>" + FlightPatterns.Label(p) + "</b>" + (p == FlightPattern.CustomPath ? $" ({PathsFor(c, cls)})" : "");
            if (GUILayout.Button(text, button, GUILayout.Height(row))) c.SetPattern(cls, FlightPatterns.Next(p));
        }

        public static void Draw(ScenarioConfig c, string id, GUIStyle label, GUIStyle small, GUIStyle field, GUIStyle button, float row, float labelWidth)
        {
            GUILayout.Space(4f);
            GUILayout.Label("<b>Spawn and flight options</b>   (-1 or 0 = automatic; no upper limits)", label);
            GUILayout.BeginHorizontal();
            c.spawnAltMin = Step(id + ".altMin", "Spawn height min m", c.spawnAltMin, 10, -1, label, field, button, row, labelWidth);
            c.spawnAltMax = Step(id + ".altMax", "max m", c.spawnAltMax, 10, -1, label, field, button, row, labelWidth * 0.6f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            c.spawnDistMin = Step(id + ".distMin", "Spawn dist min m", c.spawnDistMin, 100, -1, label, field, button, row, labelWidth);
            GUILayout.Label("max = Ring m", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            c.sectorCentreDegrees = Step(id + ".sector", "Sector centre deg", c.sectorCentreDegrees, 10, int.MinValue, label, field, button, row, labelWidth);
            GUILayout.Label("0 = ahead, 90 = right; the arc (Arc deg) is centred on it", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Pattern(c, ContactClass.HostileQuad, "Hostile quad", button, row);
            Pattern(c, ContactClass.HostileFixedWing, "Hostile fixed-wing", button, row);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            Pattern(c, ContactClass.FriendlyISR, "Own ISR", button, row);
            Pattern(c, ContactClass.CivilianQuad, "Civilian", button, row);
            Pattern(c, ContactClass.Bird, "Birds", button, row);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            c.roostBearing = Step(id + ".roostB", "Roost bearing deg", c.roostBearing, 10, int.MinValue, label, field, button, row, labelWidth);
            c.roostDistance = Step(id + ".roostD", "dist m", c.roostDistance, 50, 0, label, field, button, row, labelWidth * 0.6f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            c.roostAltitude = Step(id + ".roostA", "Roost height m", c.roostAltitude, 10, -1, label, field, button, row, labelWidth);
            GUILayout.Label(c.roostBearing > 360f && c.roostDistance <= 0f && c.roostAltitude < 0f ? "roost: chosen from the seed (bearing above 360 = automatic)" : "bearing from the sector centre; flocks circle it", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            GUILayout.Label("Patterns: DEFAULT = the contact's own. STRAIGHT IN, ORBIT THE POST, LOITER, ZIG-ZAG, POP-UP (low run-in, climb near the end), TERRAIN-FOLLOWING (holds height above ground). CUSTOM PATH flies the paths drawn on the map; a pattern other than DEFAULT / CUSTOM PATH ignores drawn paths for that type. Swarms and flocks use their own path (drawn on the map) or the roost.", small);
        }
    }
}
