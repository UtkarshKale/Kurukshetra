using System;
using System.Collections.Generic;
using Kurukshetra.Scenario;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Kurukshetra.Player
{
    // A trigger or button read from an analogue value with a gap between pressing and releasing, so it does not flicker.
    public struct Press
    {
        public bool Held, Down, Up;

        public void Feed(float value, float on = 0.6f, float off = 0.4f)
        {
            bool was = Held;
            if (!Held && value >= on) Held = true;
            else if (Held && value <= off) Held = false;
            Down = Held && !was;
            Up = !Held && was;
        }
    }

    // Snap turn on a thumbstick axis: one step per push past `engage`, re-armed when the stick returns inside `release`.
    // No smooth turning: it is the comfort setting.
    public struct SnapTurn
    {
        public const float StepDegrees = 30f;
        bool armed;

        // Returns -1 (turn left), 0, or +1 (turn right).
        public int Step(float axis, float engage = 0.7f, float release = 0.3f)
        {
            if (Mathf.Abs(axis) <= release) { armed = true; return 0; }
            if (armed && Mathf.Abs(axis) >= engage) { armed = false; return axis > 0f ? 1 : -1; }
            return 0;
        }
    }

    // The headset radial menu: one ring of everything the keyboard does for the decision, picked by the right thumbstick
    // direction and confirmed with the trigger. Order is clockwise from the top.
    public enum XrMenuKind { Call, Action, Iff, SelectAll }

    public struct XrMenuItem
    {
        public string label;
        public XrMenuKind kind;
        public PlayerCall call;
        public PlayerAction action;
    }

    public static class XrMenu
    {
        public static readonly XrMenuItem[] Items =
        {
            new XrMenuItem { label = "1 Hostile quad", kind = XrMenuKind.Call, call = PlayerCall.HostileQuad },
            new XrMenuItem { label = "2 Hostile fixed-wing", kind = XrMenuKind.Call, call = PlayerCall.HostileFixedWing },
            new XrMenuItem { label = "3 Own ISR drone", kind = XrMenuKind.Call, call = PlayerCall.OwnIsrDrone },
            new XrMenuItem { label = "4 Civilian drone", kind = XrMenuKind.Call, call = PlayerCall.CivilianDrone },
            new XrMenuItem { label = "5 Bird", kind = XrMenuKind.Call, call = PlayerCall.Bird },
            new XrMenuItem { label = "H Hold", kind = XrMenuKind.Action, action = PlayerAction.Hold },
            new XrMenuItem { label = "R Report", kind = XrMenuKind.Action, action = PlayerAction.Report },
            new XrMenuItem { label = "J Jam", kind = XrMenuKind.Action, action = PlayerAction.Jam },
            new XrMenuItem { label = "F Engage", kind = XrMenuKind.Action, action = PlayerAction.Engage },
            new XrMenuItem { label = "I IFF", kind = XrMenuKind.Iff },
            new XrMenuItem { label = "G All in view", kind = XrMenuKind.SelectAll },
        };

        public static readonly string[] PauseItems = { "Resume", "Recenter view", "Back to desktop" };
        public const int PauseResume = 0, PauseRecenter = 1, PauseDesktop = 2;

        // Does what the keyboard key does. Null decisions / sensor (a scene without them) just report false.
        public static bool Apply(int index, DecisionLoop decisions, ContactSensor sensor)
        {
            if (index < 0 || index >= Items.Length) return false;
            var item = Items[index];
            switch (item.kind)
            {
                case XrMenuKind.Call: return decisions != null && decisions.Classify(item.call);
                case XrMenuKind.Action: return decisions != null && decisions.Act(item.action);
                case XrMenuKind.Iff: return decisions != null && decisions.Challenge();
                case XrMenuKind.SelectAll: return sensor != null && sensor.SelectAllInView() > 0;
            }
            return false;
        }

        // Both hands up with their grips held near the face, or the left trigger held: binoculars.
        public static bool BinocularsRequested(bool leftGrip, float leftToHead, bool rightGrip, float rightToHead, bool leftTrigger, float faceDistance = 0.30f)
        {
            return leftTrigger || (leftGrip && leftToHead <= faceDistance && rightGrip && rightToHead <= faceDistance);
        }
    }

    // The controller and head bindings, built in code (no input-actions asset). Paths are the generic OpenXR ones (usages, not
    // profile names), so Oculus Touch, Valve Index, HTC Vive, HP Reverb G2, Windows MR and the Khronos simple controller all work.
    public class XrInput : IDisposable
    {
        public const string Left = "<XRController>{LeftHand}", Right = "<XRController>{RightHand}", Head = "<XRHMD>";

        public readonly List<InputAction> All = new List<InputAction>();
        public readonly InputAction HeadPosition, HeadRotation;
        public readonly InputAction LeftPosition, LeftRotation, RightPosition, RightRotation, RightAimPosition, RightAimRotation;
        public readonly InputAction LeftTracked, RightTracked;
        public readonly InputAction LeftTrigger, RightTrigger, LeftGrip, RightGrip, LeftStick, RightStick;
        public readonly InputAction RightStickClick, LeftPrimary, RightPrimary, LeftSecondary, RightSecondary, MenuButton;

        public Press LeftTriggerP, RightTriggerP, LeftGripP, RightGripP, RightStickClickP, LeftPrimaryP, RightPrimaryP, LeftSecondaryP, RightSecondaryP, MenuP;
        public Vector2 LeftStickValue, RightStickValue;
        public SnapTurn Snap;

        public XrInput()
        {
            HeadPosition = Make("HeadPosition", "Vector3", Head + "/centerEyePosition");
            HeadRotation = Make("HeadRotation", "Quaternion", Head + "/centerEyeRotation");
            // Grip pose = where the hand is (hand model, tablet); pointer pose = the aim ray.
            LeftPosition = Make("LeftHandPosition", "Vector3", Left + "/devicePosition");
            LeftRotation = Make("LeftHandRotation", "Quaternion", Left + "/deviceRotation");
            RightPosition = Make("RightHandPosition", "Vector3", Right + "/devicePosition");
            RightRotation = Make("RightHandRotation", "Quaternion", Right + "/deviceRotation");
            RightAimPosition = Make("RightAimPosition", "Vector3", Right + "/pointerPosition");
            RightAimRotation = Make("RightAimRotation", "Quaternion", Right + "/pointerRotation");
            LeftTracked = Make("LeftTracked", "Button", Left + "/isTracked");
            RightTracked = Make("RightTracked", "Button", Right + "/isTracked");

            LeftTrigger = Make("LeftTrigger", "Axis", Left + "/{Trigger}", Left + "/select");
            RightTrigger = Make("RightTrigger", "Axis", Right + "/{Trigger}", Right + "/select");
            LeftGrip = Make("LeftGrip", "Axis", Left + "/{Grip}");
            RightGrip = Make("RightGrip", "Axis", Right + "/{Grip}");
            LeftStick = Make("LeftStick", "Vector2", Left + "/{Primary2DAxis}");
            RightStick = Make("RightStick", "Vector2", Right + "/{Primary2DAxis}");
            RightStickClick = Make("RightStickClick", "Button", Right + "/{Primary2DAxisClick}");
            LeftPrimary = Make("LeftPrimary", "Button", Left + "/{PrimaryButton}");
            RightPrimary = Make("RightPrimary", "Button", Right + "/{PrimaryButton}");
            LeftSecondary = Make("LeftSecondary", "Button", Left + "/{SecondaryButton}");
            RightSecondary = Make("RightSecondary", "Button", Right + "/{SecondaryButton}");
            MenuButton = Make("Menu", "Button", Left + "/{MenuButton}", Right + "/{MenuButton}", Left + "/menu", Right + "/menu");
        }

        InputAction Make(string name, string expectedControlType, params string[] paths)
        {
            var action = new InputAction(name, InputActionType.Value, expectedControlType: expectedControlType);
            foreach (var p in paths) action.AddBinding(p);
            All.Add(action);
            return action;
        }

        public void Enable() { foreach (var a in All) a.Enable(); }

        public void Dispose()
        {
            foreach (var a in All) { a.Disable(); a.Dispose(); }
            All.Clear();
        }

        static float Value(InputAction a)
        {
            try { return a.ReadValue<float>(); } catch { return 0f; }
        }

        static Vector2 Vec(InputAction a)
        {
            try { return a.ReadValue<Vector2>(); } catch { return Vector2.zero; }
        }

        public bool Tracked(bool left)
        {
            var a = left ? LeftTracked : RightTracked;
            return a.controls.Count > 0 && Value(a) > 0.5f;
        }

        // Once per frame, before anything reads the Press fields.
        public void Poll()
        {
            LeftTriggerP.Feed(Value(LeftTrigger));
            RightTriggerP.Feed(Value(RightTrigger));
            LeftGripP.Feed(Value(LeftGrip));
            RightGripP.Feed(Value(RightGrip));
            RightStickClickP.Feed(Value(RightStickClick));
            LeftPrimaryP.Feed(Value(LeftPrimary));
            RightPrimaryP.Feed(Value(RightPrimary));
            LeftSecondaryP.Feed(Value(LeftSecondary));
            RightSecondaryP.Feed(Value(RightSecondary));
            MenuP.Feed(Value(MenuButton));
            LeftStickValue = Vec(LeftStick);
            RightStickValue = Vec(RightStick);
        }

        // Every action has at least one binding, and the controller actions are bound to the right hand or the left one as named.
        public bool BothHandsBound()
        {
            foreach (var a in All) if (a.bindings.Count == 0) return false;
            return HasHand(LeftTrigger, "{LeftHand}") && HasHand(RightTrigger, "{RightHand}")
                && HasHand(LeftGrip, "{LeftHand}") && HasHand(RightGrip, "{RightHand}")
                && HasHand(LeftStick, "{LeftHand}") && HasHand(RightStick, "{RightHand}")
                && HasHand(LeftPosition, "{LeftHand}") && HasHand(RightPosition, "{RightHand}")
                && HasHand(LeftRotation, "{LeftHand}") && HasHand(RightRotation, "{RightHand}");
        }

        static bool HasHand(InputAction a, string hand)
        {
            foreach (var b in a.bindings) if (b.path.Contains(hand)) return true;
            return false;
        }
    }
}
