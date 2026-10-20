using System;
using System.Collections.Generic;
using Chaukanna.Scenario;
using Chaukanna.Trainer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Net
{
    public enum MicMode { Open, Muted, PushToTalk }

    // Voice settings: mic mode (open / muted / push-to-talk), the push-to-talk key, the microphone device, the input level meter and
    // the output volume. Saved in PlayerPrefs (per PC). Opened with F10 in the menu and in the field; there is deliberately no
    // on-screen hint for F10. VoiceChat reads Mode / PttKey / DeviceName / OutputVolume and reports the input level it measures.
    public class VoiceSettings : MonoBehaviour
    {
        const string KMode = "chk.voice.mode", KKey = "chk.voice.ptt", KDevice = "chk.voice.device", KOut = "chk.voice.out";

        static bool loaded;
        static MicMode mode = MicMode.PushToTalk;
        static Key pttKey = Key.V;
        static string device = "";
        static float outVolume = 1f;

        static VoiceSettings inst;
        public static bool Open { get; private set; }
        // Opens the window (the Settings buttons in the trainer menu and the F6 panel).
        public static void OpenWindow() { if (inst != null) inst.SetOpen(true); }
        public static float InputLevel { get; private set; }          // 0..1, smoothed
        static float levelAt = -10f;

        public static MicMode Mode { get { Load(); return mode; } set { Load(); mode = value; Save(); } }
        public static Key PttKey { get { Load(); return pttKey; } set { Load(); pttKey = value; Save(); } }
        // null = the system default microphone (what Unity's Microphone calls null).
        public static string DeviceName { get { Load(); return string.IsNullOrEmpty(device) ? null : device; } set { Load(); device = value ?? ""; Save(); } }
        public static float OutputVolume { get { Load(); return outVolume; } set { Load(); outVolume = Mathf.Clamp01(value); Save(); } }

        // Keys the game already uses (or that are modifiers / function keys); push-to-talk may not take them.
        static readonly HashSet<Key> Reserved = new HashSet<Key>
        {
            Key.Escape, Key.Space, Key.Tab, Key.Enter, Key.Backspace, Key.LeftShift, Key.RightShift, Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt,
            Key.W, Key.A, Key.S, Key.D, Key.Q, Key.E, Key.R, Key.T, Key.Y, Key.H, Key.J, Key.K, Key.F, Key.G, Key.X, Key.Z, Key.C, Key.B, Key.N, Key.I,
            Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Numpad1, Key.Numpad2, Key.Numpad3, Key.Numpad4, Key.Numpad5,
            Key.Comma, Key.Period, Key.UpArrow, Key.DownArrow, Key.LeftArrow, Key.RightArrow,
            Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12,
        };

        public static bool IsReserved(Key k) => Reserved.Contains(k);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { loaded = false; Open = false; InputLevel = 0f; levelAt = -10f; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (FindAnyObjectByType<VoiceSettings>() != null) return;
            var go = new GameObject("Chaukanna_VoiceSettings");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            go.AddComponent<VoiceSettings>();
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                int m = PlayerPrefs.GetInt(KMode, (int)MicMode.PushToTalk);
                mode = Enum.IsDefined(typeof(MicMode), m) ? (MicMode)m : MicMode.PushToTalk;
                if (!Enum.TryParse(PlayerPrefs.GetString(KKey, "V"), out pttKey) || IsReserved(pttKey)) pttKey = Key.V;
                device = PlayerPrefs.GetString(KDevice, "");
                outVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KOut, 1f));
            }
            catch (Exception) { }
        }

        static void Save()
        {
            try
            {
                PlayerPrefs.SetInt(KMode, (int)mode);
                PlayerPrefs.SetString(KKey, pttKey.ToString());
                PlayerPrefs.SetString(KDevice, device ?? "");
                PlayerPrefs.SetFloat(KOut, outVolume);
                PlayerPrefs.Save();
            }
            catch (Exception) { }
        }

        // VoiceChat (or this screen's own microphone) reports the level of the newest samples.
        public static void ReportLevel(float rms)
        {
            float v = Mathf.Clamp01(rms * 4f);
            InputLevel = Mathf.Max(v, InputLevel * 0.85f);
            levelAt = Time.unscaledTime;
        }

        // ---- The screen.

        bool prevBlocked;
        CursorLockMode prevLock;
        bool prevCursorVisible, capturing;
        string capturingNote = "";
        AudioClip ownClip;
        string ownDevice;
        float[] window = new float[512];
        Vector2 scroll;
        string[] devices = new string[0];
        float nextDeviceScan;
        GUIStyle label, small, button, box, field;
        int tab;
        GraphicsState pend;
        string gfxNote = "";

        void Awake() { inst = this; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (capturing)
            {
                if (kb.escapeKey.wasPressedThisFrame) { capturing = false; capturingNote = ""; return; }
                foreach (var k in kb.allKeys)
                {
                    if (!k.wasPressedThisFrame) continue;
                    if (IsReserved(k.keyCode)) capturingNote = $"{k.keyCode} is used by the game. Pick another key.";
                    else { PttKey = k.keyCode; capturing = false; capturingNote = ""; }
                    break;
                }
                return;
            }
            if (kb.f10Key.wasPressedThisFrame) SetOpen(!Open);
            if (Open && kb.escapeKey.wasPressedThisFrame) SetOpen(false);
            if (Open) { MeterOwnMic(); if (Time.unscaledTime >= nextDeviceScan) { nextDeviceScan = Time.unscaledTime + 2f; try { devices = Microphone.devices; } catch (Exception) { devices = new string[0]; } } }
            if (Time.unscaledTime - levelAt > 0.25f) InputLevel *= 0.9f;
        }

        void SetOpen(bool on)
        {
            if (on == Open) return;
            Open = on;
            if (on)
            {
                prevBlocked = ContactRegistry.InputBlocked;
                prevLock = Cursor.lockState; prevCursorVisible = Cursor.visible;
                ContactRegistry.InputBlocked = true;
                Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
                nextDeviceScan = 0f;
                pend = GraphicsPrefs.Current; gfxNote = "";
            }
            else
            {
                capturing = false; capturingNote = "";
                ContactRegistry.InputBlocked = prevBlocked;
                Cursor.lockState = prevLock; Cursor.visible = prevCursorVisible;
                StopOwnMic();
            }
        }

        void OnDisable() { if (Open) SetOpen(false); StopOwnMic(); }

        // Outside a team run VoiceChat does not exist, so this screen listens to the chosen microphone itself while it is open.
        void MeterOwnMic()
        {
            if (VoiceChat.Instance != null && VoiceChat.Instance.MicOpen) { StopOwnMic(); return; }
            string dev = DeviceName;
            if (ownClip != null && ownDevice != dev) StopOwnMic();
            try
            {
                if (ownClip == null)
                {
                    if (Microphone.devices.Length == 0) return;
                    ownClip = Microphone.Start(dev, true, 1, 16000);
                    ownDevice = dev;
                    return;
                }
                int pos = Microphone.GetPosition(dev);
                int n = window.Length;
                if (pos < n) return;
                ownClip.GetData(window, pos - n);
                float sum = 0f;
                for (int i = 0; i < n; i++) sum += window[i] * window[i];
                ReportLevel(Mathf.Sqrt(sum / n));
            }
            catch (Exception) { StopOwnMic(); }
        }

        void StopOwnMic()
        {
            if (ownClip == null) return;
            try { Microphone.End(ownDevice); } catch (Exception) { }
            ownClip = null;
        }

        void OnGUI()
        {
            if (!Open) return;
            GUI.depth = -1000;
            float ui = Mathf.Max(0.8f, Screen.height / 720f);
            if (label == null)
            {
                label = new GUIStyle(GUI.skin.label) { richText = true, alignment = TextAnchor.MiddleLeft };
                small = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
                button = new GUIStyle(GUI.skin.button);
                box = new GUIStyle(GUI.skin.box);
                field = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleCenter };
            }
            int fs = Mathf.RoundToInt(15f * ui);
            label.fontSize = fs; small.fontSize = Mathf.RoundToInt(13f * ui); button.fontSize = fs; field.fontSize = fs;
            float w = Mathf.Min(Screen.width - 20f, 600f * ui), h = Mathf.Min(Screen.height - 20f, 560f * ui);
            var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(r, "", box);
            GUI.Box(r, "", box);
            GUILayout.BeginArea(new Rect(r.x + 14f * ui, r.y + 10f * ui, r.width - 28f * ui, r.height - 20f * ui));
            scroll = GUILayout.BeginScrollView(scroll);
            float row = 28f * ui;
            tab = GUILayout.Toolbar(tab, new[] { "Voice", "Graphics" }, button, GUILayout.Height(row));
            GUILayout.Space(6f);
            if (tab == 1) DrawGraphics(ui, row);
            else DrawVoice(ui, row);
            GUILayout.Space(8f);
            if (GUILayout.Button("Close", button, GUILayout.Width(120f * ui), GUILayout.Height(row))) SetOpen(false);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        void DrawVoice(float ui, float row)
        {
            GUILayout.Label("<b>Voice settings</b>", label);
            GUILayout.Space(4f);

            GUILayout.Label("Microphone mode", label);
            GUILayout.BeginHorizontal();
            Choice("Open mic", MicMode.Open, row);
            Choice("Muted", MicMode.Muted, row);
            Choice("Push-to-talk", MicMode.PushToTalk, row);
            GUILayout.EndHorizontal();
            GUILayout.Label(Mode == MicMode.Open ? "Open mic: your voice goes out whenever you speak (a level gate stops silence being sent). Y still works as the radio key."
                : Mode == MicMode.Muted ? "Muted: nothing you say is sent." : "Push-to-talk: hold the key below to talk to people nearby. Y is the radio (whole team).", small);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Push-to-talk key", label, GUILayout.Width(150f * ui), GUILayout.Height(row));
            if (GUILayout.Button(capturing ? "press a key ... (Esc cancels)" : PttKey.ToString(), button, GUILayout.Width(230f * ui), GUILayout.Height(row))) { capturing = true; capturingNote = ""; }
            if (!capturing && PttKey != Key.V && GUILayout.Button("Reset (V)", button, GUILayout.Width(90f * ui), GUILayout.Height(row))) PttKey = Key.V;
            GUILayout.EndHorizontal();
            if (capturingNote.Length > 0) GUILayout.Label("<color=#FFB040>" + capturingNote + "</color>", small);

            GUILayout.Space(6f);
            GUILayout.Label("Microphone device", label);
            string cur = DeviceName;
            if (devices.Length == 0) GUILayout.Label("<color=#FFB040>No microphone found on this PC.</color>", small);
            if (GUILayout.Toggle(cur == null, " System default", GUILayout.Height(row)) && cur != null) DeviceName = null;
            foreach (var d in devices)
                if (GUILayout.Toggle(cur == d, " " + d, GUILayout.Height(row)) && cur != d) DeviceName = d;

            GUILayout.Space(6f);
            GUILayout.Label("Input level (speak to test)", label);
            var bar = GUILayoutUtility.GetRect(10f, 16f * ui, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                var old = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.6f); GUI.DrawTexture(bar, Texture2D.whiteTexture);
                float lv = InputLevel;
                GUI.color = lv > 0.85f ? new Color(0.95f, 0.3f, 0.2f) : lv > 0.6f ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.3f, 0.85f, 0.4f);
                GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * lv, bar.height), Texture2D.whiteTexture);
                GUI.color = old;
            }

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Output volume  {Mathf.RoundToInt(OutputVolume * 100f)}%", label, GUILayout.Width(200f * ui), GUILayout.Height(row));
            float ov = GUILayout.HorizontalSlider(OutputVolume, 0f, 1f, GUILayout.Height(row));
            if (!Mathf.Approximately(ov, OutputVolume)) OutputVolume = ov;
            GUILayout.EndHorizontal();
        }

        bool Pick(string text, bool on, float row) => GUILayout.Toggle(on, text, button, GUILayout.Height(row)) && !on;

        void DrawGraphics(float ui, float row)
        {
            GUILayout.Label("<b>Graphics settings</b>", label);
            if (Application.isEditor) GUILayout.Label("In the Unity Editor the resolution and window mode do not change; everything else does.", small);
            var g = pend;

            GUILayout.Label("Resolution", label);
            var res = GraphicsPrefs.Resolutions();
            int ri = res.FindIndex(v => v.x == g.width && v.y == g.height);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", button, GUILayout.Width(40f * ui), GUILayout.Height(row)))
            { ri = ri < 0 ? res.Count - 1 : Mathf.Max(0, ri - 1); g.width = res[ri].x; g.height = res[ri].y; }
            GUILayout.Label($"{g.width} x {g.height}" + (ri < 0 ? "  (current)" : ""), label, GUILayout.Width(220f * ui), GUILayout.Height(row));
            if (GUILayout.Button(">", button, GUILayout.Width(40f * ui), GUILayout.Height(row)))
            { ri = ri < 0 ? res.Count - 1 : Mathf.Min(res.Count - 1, ri + 1); g.width = res[ri].x; g.height = res[ri].y; }
            GUILayout.EndHorizontal();

            GUILayout.Label("Window mode", label);
            GUILayout.BeginHorizontal();
            if (Pick("Fullscreen", g.mode == 0, row)) g.mode = 0;
            if (Pick("Borderless", g.mode == 1, row)) g.mode = 1;
            if (Pick("Windowed", g.mode == 2, row)) g.mode = 2;
            GUILayout.EndHorizontal();

            var names = QualitySettings.names;
            GUILayout.Label("Quality preset", label);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < names.Length; i++) if (Pick(names[i], g.quality == i, row)) g.quality = i;
            GUILayout.EndHorizontal();

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("V-sync", label, GUILayout.Width(150f * ui), GUILayout.Height(row));
            if (Pick("On", g.vsync, row)) g.vsync = true;
            if (Pick("Off", !g.vsync, row)) g.vsync = false;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("FPS cap (0 = none)", label, GUILayout.Width(150f * ui), GUILayout.Height(row));
            g.fps = NumberEntry.Field("gfx.fps", g.fps, 0, int.MaxValue, field, GUILayout.Width(90f * ui), GUILayout.Height(row));
            GUILayout.EndHorizontal();
            if (g.vsync && g.fps > 0) GUILayout.Label("V-sync is on, so the screen refresh rate limits the frame rate first.", small);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Render scale %", label, GUILayout.Width(150f * ui), GUILayout.Height(row));
            g.renderScalePct = NumberEntry.Field("gfx.scale", g.renderScalePct, 25, 1000, field, GUILayout.Width(90f * ui), GUILayout.Height(row));
            GUILayout.Label("100 = native, lowest 25, the pipeline stops at 200", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Shadows", label, GUILayout.Width(150f * ui), GUILayout.Height(row));
            if (Pick("On", g.shadows, row)) g.shadows = true;
            if (Pick("Off", !g.shadows, row)) g.shadows = false;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shadow distance (m)", label, GUILayout.Width(150f * ui), GUILayout.Height(row));
            g.shadowDist = NumberEntry.Field("gfx.shadowdist", g.shadowDist, 1, int.MaxValue, field, GUILayout.Width(90f * ui), GUILayout.Height(row));
            GUILayout.EndHorizontal();

            GUILayout.Label("Anti-aliasing (MSAA)", label);
            GUILayout.BeginHorizontal();
            if (Pick("Off", g.msaa == 1, row)) g.msaa = 1;
            if (Pick("2x", g.msaa == 2, row)) g.msaa = 2;
            if (Pick("4x", g.msaa == 4, row)) g.msaa = 4;
            if (Pick("8x", g.msaa == 8, row)) g.msaa = 8;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("View distance %", label, GUILayout.Width(150f * ui), GUILayout.Height(row));
            g.viewDistPct = NumberEntry.Field("gfx.view", g.viewDistPct, 1, int.MaxValue, field, GUILayout.Width(90f * ui), GUILayout.Height(row));
            GUILayout.Label("100 = unchanged; stored for later optimisation", small, GUILayout.Height(row));
            GUILayout.EndHorizontal();
            pend = g;

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply", button, GUILayout.Width(120f * ui), GUILayout.Height(row))) { GraphicsPrefs.ApplyAndSave(pend); pend = GraphicsPrefs.Current; gfxNote = "Applied and saved on this PC."; }
            if (GUILayout.Button("Reset to defaults", button, GUILayout.Width(160f * ui), GUILayout.Height(row))) { GraphicsPrefs.ResetToDefaults(); pend = GraphicsPrefs.Current; gfxNote = "Back to the defaults this game started with."; }
            GUILayout.EndHorizontal();
            if (gfxNote.Length > 0) GUILayout.Label(gfxNote, small);
        }

        void Choice(string text, MicMode m, float row)
        {
            bool on = Mode == m;
            if (GUILayout.Toggle(on, text, button, GUILayout.Height(row)) && !on) Mode = m;
        }
    }
}
