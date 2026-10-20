using System;
using UnityEngine;

namespace Kurukshetra
{
    // Mouse-look options (F10 > Controls). Saved in PlayerPrefs (per PC) and read live by every mouse-look path each frame.
    // 1.0 and invert off = the original feel.
    public static class ControlsPrefs
    {
        const string KLook = "chk.ctl.look", KZoom = "chk.ctl.zoom", KInvert = "chk.ctl.invy";
        static float look = 1f, zoom = 1f;
        static bool invertY, loaded;

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            try
            {
                look = Sane(PlayerPrefs.GetFloat(KLook, 1f)); zoom = Sane(PlayerPrefs.GetFloat(KZoom, 1f));
                invertY = PlayerPrefs.GetInt(KInvert, 0) == 1;
            }
            catch (Exception) { }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { loaded = false; look = 1f; zoom = 1f; invertY = false; }

        static float Sane(float v) => float.IsNaN(v) || float.IsInfinity(v) || v <= 0f ? 1f : v;

        public static float LookSensitivity { get { Load(); return look; } set { Load(); look = value > 0f ? value : look; Save(); } }
        public static float ZoomSensitivity { get { Load(); return zoom; } set { Load(); zoom = value > 0f ? value : zoom; Save(); } }
        public static bool InvertY { get { Load(); return invertY; } set { Load(); invertY = value; Save(); } }

        static void Save()
        {
            try { PlayerPrefs.SetFloat(KLook, look); PlayerPrefs.SetFloat(KZoom, zoom); PlayerPrefs.SetInt(KInvert, invertY ? 1 : 0); PlayerPrefs.Save(); }
            catch (Exception) { }
        }

        public static void Reset() { Load(); look = 1f; zoom = 1f; invertY = false; Save(); }

        // The mouse delta as the look code should use it: sensitivity (and the zoom multiplier while the optic is up) and the Y flip.
        public static Vector2 Look(Vector2 delta, bool zoomed = false)
        {
            Load();
            float m = look * (zoomed ? zoom : 1f);
            return new Vector2(delta.x * m, delta.y * m * (invertY ? -1f : 1f));
        }
    }
}
