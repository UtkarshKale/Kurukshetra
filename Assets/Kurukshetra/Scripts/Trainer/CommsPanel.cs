using System.Collections.Generic;
using Kurukshetra.Net;
using UnityEngine;

namespace Kurukshetra.Trainer
{
    // The trainer's "Comms" section (Lobby tab and the F6 panel, host only): degrade proximity voice live. Values are typeable
    // (Enter applies); only impossible ones are corrected. The effect is applied on each listener's PC, so the recording stays clean.
    public static class CommsPanel
    {
        static ulong target = ulong.MaxValue;        // MaxValue = all seats
        static GUIStyle field;

        public static void Draw(GUIStyle label, GUIStyle small, GUIStyle button, float row, int fontSize)
        {
            var lan = LanSession.Instance;
            if (lan == null || !lan.IsHost) return;
            if (field == null) field = new GUIStyle(GUI.skin.textField);
            field.fontSize = fontSize;
            var comms = lan.Comms;

            // The target seat may have left.
            if (target != ulong.MaxValue && lan.roster.Find(target) == null) target = ulong.MaxValue;
            string targetName = target == ulong.MaxValue ? "ALL SEATS" : lan.roster.Find(target).name;

            GUILayout.Label("<b>Comms</b>  (nearby voice on V, radio on Y; degraded on each listener's PC, live)", label);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Applies to", label, GUILayout.Width(90f), GUILayout.Height(row));
            if (GUILayout.Button(targetName + "  (click: next)", button, GUILayout.Width(230f), GUILayout.Height(row))) target = NextTarget(lan);
            if (target != ulong.MaxValue && comms.HasOverride(target) && GUILayout.Button("Clear this seat's override", button, GUILayout.Width(210f), GUILayout.Height(row)))
            {
                comms.ClearOverride(target);
                Changed(lan, target);
            }
            GUILayout.EndHorizontal();

            // Show what is in force for the target; the first edit of a seat makes its own set from the all-seats set.
            var d = target == ulong.MaxValue ? comms.all : comms.For(target);
            var before = d.Clone();
            GUILayout.BeginHorizontal();
            float near = (float)Entry("comms.near", d.nearRange, 0, "near range m", label, row, 150f);
            float full = (float)Entry("comms.full", d.fullRange, 0, "full vol m", label, row, 110f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            float noise = (float)Entry("comms.noise", d.noise, 2, "radio noise 0-1", label, row, 150f);
            float drop = (float)Entry("comms.drop", d.dropoutPercent, 0, "dropout %", label, row, 110f);
            float muffle = (float)Entry("comms.muffle", d.muffleHz, 0, "muffle Hz", label, row, 110f);
            GUILayout.EndHorizontal();
            if (near != before.nearRange || full != before.fullRange || noise != before.noise || drop != before.dropoutPercent || muffle != before.muffleHz)
            {
                var edit = comms.Edit(target);
                edit.nearRange = near; edit.fullRange = full; edit.noise = noise; edit.dropoutPercent = drop; edit.muffleHz = muffle;
                edit.Clamp();
                Changed(lan, target);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset all comms", button, GUILayout.Width(150f), GUILayout.Height(row)))
            {
                comms.all = new VoiceDegrade();
                comms.seats.Clear();
                Changed(lan, ulong.MaxValue);
            }
            GUILayout.Label("full volume inside 'full vol', silent beyond 'near range'; the radio ignores distance; muffle 22000 = off", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            var v = VoiceChat.Instance;
            if (v != null) GUILayout.Label($"voice: {v.FramesSent} frames sent here, {v.FramesReceived} heard, {v.FramesDroppedByDegrade} dropped by the trainer's dropout" + (v.Recorder != null ? $", recording {v.Recorder.FramesStored} frames" : ""), small);
        }

        static double Entry(string id, float value, int decimals, string name, GUIStyle label, float row, float labelWidth)
        {
            GUILayout.Label(name, label, GUILayout.Width(labelWidth), GUILayout.Height(row));
            return NumberEntry.FieldDouble(id, value, decimals, field, GUILayout.Width(80f), GUILayout.Height(row));
        }

        static ulong NextTarget(LanSession lan)
        {
            var ids = new List<ulong>();
            foreach (var p in lan.roster.players) ids.Add(p.clientId);
            if (target == ulong.MaxValue) return ids.Count > 0 ? ids[0] : ulong.MaxValue;
            int i = ids.IndexOf(target);
            return i < 0 || i + 1 >= ids.Count ? ulong.MaxValue : ids[i + 1];
        }

        static void Changed(LanSession lan, ulong t)
        {
            var v = VoiceChat.Instance;
            if (v != null) v.CommsChanged(t);
        }
    }
}
