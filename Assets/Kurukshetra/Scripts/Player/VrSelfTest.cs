using System.Collections.Generic;
using Kurukshetra.Scenario;
using Kurukshetra.Scoring;
using UnityEngine;

namespace Kurukshetra.Player
{
    // Checks for VR mode that need no headset and no scene: the hand gesture state machine, the radial menu,
    // the display tier and the switch itself. Run from the CLI: Kurukshetra.Player.VrSelfTest.Run()
    public static class VrSelfTest
    {
        static int failures;
        static readonly List<string> lines = new List<string>();

        public static bool Run()
        {
            failures = 0;
            lines.Clear();

            // Gesture: both hands up = binoculars, faded in over about 150 ms; either hand down puts them away.
            var g = new HandGesture();
            g.Tick(false, false, false, 0.02f);
            Check("hands start at rest, binoculars down", g.Left == 0f && g.Right == 0f && !g.BinocularsUp && g.OpticBlend == 0f);
            g.Tick(true, true, false, 0.05f);
            Check("both hands up: binoculars up, optic fading in", g.BinocularsUp && g.OpticBlend > 0.2f && g.OpticBlend < 0.5f);
            for (int i = 0; i < 6; i++) g.Tick(true, true, false, 0.02f);
            Check("after about 150 ms the optic is fully up", Mathf.Approximately(g.OpticBlend, 1f));
            g.Tick(true, false, false, 0.02f);
            Check("lowering the right hand puts the binoculars away at once", !g.BinocularsUp && g.OpticBlend == 0f && g.Right < 1f && g.Left == 1f);
            g.Reset();
            g.Tick(false, true, false, 0.2f);
            Check("right hand alone does not raise the binoculars", !g.BinocularsUp && g.Right == 1f && g.Left == 0f);
            g.Reset();
            g.Tick(false, false, true, 0.2f);
            Check("Q brings the tablet closer without raising the binoculars", g.TabletCloser == 1f && !g.BinocularsUp);
            g.Tick(true, false, true, 0.2f);
            Check("raising the left hand to the face takes the tablet away from reading", g.TabletCloser == 0f);

            // Radial menu: up is the first item, clockwise from there; a small push picks nothing.
            Check("radial: up picks item 0 of 5", RadialMenu.Pick(new Vector2(0f, 1f), 5) == 0);
            Check("radial: right picks item 1 of 4", RadialMenu.Pick(new Vector2(1f, 0f), 4) == 1);
            Check("radial: down picks item 2 of 4", RadialMenu.Pick(new Vector2(0f, -1f), 4) == 2);
            Check("radial: left picks item 3 of 4", RadialMenu.Pick(new Vector2(-1f, 0f), 4) == 3);
            Check("radial: inside the dead zone picks nothing", RadialMenu.Pick(new Vector2(0.1f, 0.1f), 5) == -1);
            bool round = true;
            for (int n = 3; n <= 9; n++)
                for (int i = 0; i < n; i++)
                    if (RadialMenu.Pick(RadialMenu.Direction(i, n), n) != i) round = false;
            Check("radial: every item's own direction picks that item", round);

            // Display tier: what the run started on, or mixed if switched during it.
            Check("tier desktop", ViewRigSwitch.TierFor(false, false) == ViewRigSwitch.TierDesktop);
            Check("tier vr-sim", ViewRigSwitch.TierFor(true, false) == ViewRigSwitch.TierVrSim);
            Check("tier mixed when switched during the run", ViewRigSwitch.TierFor(true, true) == ViewRigSwitch.TierMixed && ViewRigSwitch.TierFor(false, true) == ViewRigSwitch.TierMixed);
            var cfg = new ScenarioConfig { vrMode = true };
            Check("VR mode is part of the scenario config and survives a clone", cfg.Clone().vrMode && !new ScenarioConfig().vrMode);
            var session = new SessionResult();
            Check("sessions default to the desktop tier with no comfort answer", session.displayTier == "desktop" && session.comfortRating == -1);
            Check("the tier is written to the session file", JsonUtility.ToJson(new SessionResult { displayTier = "vr-sim", comfortRating = 4 }).Contains("\"displayTier\":\"vr-sim\""));

            // The switch: builds the rig on the camera and removes every trace of it again. Needs Play mode (objects are destroyed at end of frame).
            if (Application.isPlaying && !ViewRigSwitch.VrActive)
            {
                var go = new GameObject("VrSelfTestHead", typeof(Camera), typeof(Binoculars), typeof(ContactSensor));
                go.GetComponent<Camera>().enabled = false;
                var sw = go.AddComponent<ViewRigSwitch>();
                float near = go.GetComponent<Camera>().nearClipPlane;
                sw.Set(true);
                Check("switch on: VR rig with two hands and a tablet, near plane pulled in",
                    ViewRigSwitch.VrActive && sw.Vr != null && go.transform.Find("VrSimRig/LeftHand/SensorTablet") != null && go.transform.Find("VrSimRig/RightHand") != null && go.GetComponent<Camera>().nearClipPlane < near);
                sw.Set(false);
                Check("switch off: desktop again, near plane restored", !ViewRigSwitch.VrActive && sw.Vr == null && Mathf.Approximately(go.GetComponent<Camera>().nearClipPlane, near));
                Object.Destroy(go);
            }

            string summary = $"[VrSelfTest] {lines.Count - failures}/{lines.Count} passed\n" + string.Join("\n", lines);
            if (failures == 0) Debug.Log(summary); else Debug.LogError(summary);
            return failures == 0;
        }

        static void Check(string name, bool ok)
        {
            if (!ok) failures++;
            lines.Add((ok ? "PASS  " : "FAIL  ") + name);
        }
    }
}
