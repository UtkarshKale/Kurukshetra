using System;
using System.IO;
using UnityEngine;

namespace Chaukanna.Player
{
    // The display contract (doc §1.7a, module 6): true-size display. Entered once per PC: screen diagonal, aspect, and how
    // far the soldier sits from it. The camera's vertical field of view is then set so that a drone covers the same angle on
    // the screen as it would in the real sky (Unity's fieldOfView is vertical):
    //   screen height H = diagonal x a_h / sqrt(a_w^2 + a_h^2);  vFOV = 2 atan(H / 2D);  arcmin per pixel = (H / rows) / D x 3437.7
    // A run is comparable only when arcmin per pixel is between 1.0 and 2.0 (placeholder band). Panning is capped at
    // 60 deg/s while calibrated, because the calibrated view is narrow and the sector is searched by turning.
    [Serializable]
    public class DisplaySettings
    {
        public bool trueSize;
        public float diagonalInches = 24f;
        public float aspectW = 16f, aspectH = 9f;
        public float distanceCm = 60f;
        public float binocularMagnification = 7f;
        public float maxPanDegreesPerSecond = 60f;
    }

    public static class DisplayCalibration
    {
        public const float ComparableMin = 1.0f, ComparableMax = 2.0f;
        static DisplaySettings current;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "display.json");

        public static DisplaySettings Settings
        {
            get
            {
                if (current != null) return current;
                try { if (File.Exists(FilePath)) current = JsonUtility.FromJson<DisplaySettings>(File.ReadAllText(FilePath)); } catch { }
                return current ?? (current = new DisplaySettings());
            }
        }

        public static void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(Settings, true)); }
            catch (Exception e) { Debug.LogWarning("[Display] Could not save: " + e.Message); }
        }

        public static float ScreenHeightCm(DisplaySettings s) => s.diagonalInches * 2.54f * s.aspectH / Mathf.Sqrt(s.aspectW * s.aspectW + s.aspectH * s.aspectH);

        public static float VerticalFov(DisplaySettings s) => 2f * Mathf.Atan(ScreenHeightCm(s) / (2f * Mathf.Max(1f, s.distanceCm))) * Mathf.Rad2Deg;

        public static float ArcminPerPixel(DisplaySettings s, int rows) => ScreenHeightCm(s) / Mathf.Max(1, rows) / Mathf.Max(1f, s.distanceCm) * 3437.7f;

        public static bool Comparable(float arcminPerPx) => arcminPerPx >= ComparableMin && arcminPerPx <= ComparableMax;

        // The arcmin per pixel this PC is actually running at: calibrated, or the plain camera field of view if not.
        public static float CurrentArcminPerPixel(Camera cam)
        {
            int rows = cam != null ? Mathf.Max(1, cam.pixelHeight) : Mathf.Max(1, Screen.height);
            if (Settings.trueSize) return ArcminPerPixel(Settings, rows);
            float fov = cam != null ? cam.fieldOfView : 60f;
            return fov * 60f / rows;
        }

        // Applies the settings to the observer camera and its binoculars (called when the field starts and after changes).
        public static void ApplyTo(Camera cam)
        {
            if (cam == null) return;
            var bino = cam.GetComponent<Binoculars>();
            var look = cam.GetComponent<Chaukanna.Scenario.ObserverLook>();
            var s = Settings;
            if (s.trueSize)
            {
                float v = VerticalFov(s);
                if (bino != null) { bino.normalFov = v; bino.zoomedFov = v / Mathf.Max(1f, s.binocularMagnification); }
                cam.fieldOfView = v;
                if (look != null) look.maxPanDegreesPerSecond = s.maxPanDegreesPerSecond;
            }
            else if (look != null) look.maxPanDegreesPerSecond = 0f;   // off: the scene's own field of view is left as it is
        }

        public static string Describe(Camera cam)
        {
            var s = Settings;
            int rows = cam != null ? cam.pixelHeight : Screen.height;
            float a = CurrentArcminPerPixel(cam);
            return s.trueSize
                ? $"True size ON: {s.diagonalInches:F1}\" {s.aspectW:F0}:{s.aspectH:F0} at {s.distanceCm:F0} cm, {rows} rows: vFOV {VerticalFov(s):F1} deg, {a:F2} arcmin/px" + (Comparable(a) ? " (comparable)" : " (NOT comparable: outside 1.0-2.0)")
                : $"True size OFF: 60 deg view, {a:F2} arcmin/px (drones look about {a / 1.59f:F1}x smaller than on a calibrated 24\" screen)";
        }
    }
}
