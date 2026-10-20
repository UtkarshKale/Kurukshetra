using System.Collections.Generic;
using UnityEngine;

namespace Kurukshetra.Trainer
{
    // A number the trainer can click and type into. The text is kept as typed while the field has focus (so "1", "12",
    // "120" can be typed without being clamped half-way) and is applied on Enter or when the field loses focus.
    // Invalid text puts the old value back.
    public static class NumberEntry
    {
        static readonly Dictionary<string, string> buffers = new Dictionary<string, string>();
        static int focusedFrame = -1;

        // True while one of these fields is being typed in (this frame or the last), so player hotkeys can be ignored.
        public static bool AnyFocused => Time.frameCount - focusedFrame <= 1;

        // What typed text means: whole number, clamped to the range; anything else keeps the current value.
        public static int Parse(string text, int current, int min, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return current;
            text = text.Trim();
            if (!int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int v))
            {
                // Accept "120.0" or "120,5" by taking the whole part; reject everything else.
                if (!double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) || double.IsNaN(d) || double.IsInfinity(d)) return current;
                v = d > int.MaxValue ? int.MaxValue : d < int.MinValue ? int.MinValue : (int)d;
            }
            return Mathf.Clamp(v, min, max);
        }

        // Draws the field with GUILayout. `id` must be unique among the fields on screen.
        public static int Field(string id, int value, int min, int max, GUIStyle style, params GUILayoutOption[] options)
        {
            GUI.SetNextControlName(id);
            bool focused = GUI.GetNameOfFocusedControl() == id;
            string text = focused && buffers.TryGetValue(id, out string typed) ? typed : value.ToString();

            var e = Event.current;
            if (focused && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                value = Parse(text, value, min, max);
                buffers.Remove(id);
                GUI.FocusControl(null);
                e.Use();
                text = value.ToString();
            }

            string edited = GUILayout.TextField(text, 11, style, options);
            if (focused)
            {
                buffers[id] = edited;
                focusedFrame = Time.frameCount;
            }
            else if (buffers.TryGetValue(id, out string pending))
            {
                // Focus has moved on: apply what was typed.
                value = Parse(pending, value, min, max);
                buffers.Remove(id);
            }
            return value;
        }

        // A decimal number (latitude, longitude). Same typing rules as Field; shown with `decimals` places.
        public static double FieldDouble(string id, double value, int decimals, GUIStyle style, params GUILayoutOption[] options)
        {
            GUI.SetNextControlName(id);
            bool focused = GUI.GetNameOfFocusedControl() == id;
            string shown = value.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture);
            string text = focused && buffers.TryGetValue(id, out string typed) ? typed : shown;
            var e = Event.current;
            if (focused && e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                value = ParseDouble(text, value);
                buffers.Remove(id);
                GUI.FocusControl(null);
                e.Use();
                text = value.ToString("F" + decimals, System.Globalization.CultureInfo.InvariantCulture);
            }
            string edited = GUILayout.TextField(text, 16, style, options);
            if (focused) { buffers[id] = edited; focusedFrame = Time.frameCount; }
            else if (buffers.TryGetValue(id, out string pending)) { value = ParseDouble(pending, value); buffers.Remove(id); }
            return value;
        }

        public static double ParseDouble(string text, double current)
        {
            if (string.IsNullOrWhiteSpace(text)) return current;
            return double.TryParse(text.Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d) && !double.IsNaN(d) && !double.IsInfinity(d) ? d : current;
        }

        // Label, typed field and - / + buttons on one line.
        public static int Stepper(string id, string name, int value, int step, int min, int max, GUIStyle label, GUIStyle field, GUIStyle button, float row, float labelWidth = 104f)
        {
            GUILayout.Label(name, label, GUILayout.Width(labelWidth), GUILayout.Height(row));
            value = Field(id, value, min, max, field, GUILayout.Width(64f), GUILayout.Height(row));
            if (GUILayout.Button("-", button, GUILayout.Width(28f), GUILayout.Height(row))) { value = Mathf.Clamp(value - step, min, max); buffers.Remove(id); }
            if (GUILayout.Button("+", button, GUILayout.Width(28f), GUILayout.Height(row))) { value = Mathf.Clamp(value + step, min, max); buffers.Remove(id); }
            GUILayout.Space(12f);
            return value;
        }
    }
}
