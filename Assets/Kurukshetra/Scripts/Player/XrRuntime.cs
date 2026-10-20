using System;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace Kurukshetra.Player
{
    // Starting and stopping the headset at run time. The game starts on the desktop ("Initialize XR on Startup" is OFF in the XR
    // settings); this is the only place that brings XR up, and only when the trainee asks for the Headset view (F7 or the trainer's
    // VIEW setting). TryStart never throws: it returns false with a reason a person can act on, and the caller stays on the rig it had.
    public static class XrRuntime
    {
        public const string ReasonNoSettings = "XR is not set up in this build (no XR loader settings)";
        public const string ReasonNoRuntime = "no OpenXR runtime: install SteamVR or the headset's PC app and set it as the OpenXR runtime";
        public const string ReasonNoHeadset = "headset not connected (an OpenXR runtime is installed but it did not start a headset session)";

        public static bool Running { get; private set; }
        public static string RuntimeName { get; private set; } = "";
        public static string RuntimeVersion { get; private set; } = "";
        public static string LastReason { get; private set; } = "";

        // Self-tests only: stands in for the real start so the switch can be checked with and without a runtime.
        public delegate bool StartHook(out string reason);
        public static StartHook TestHook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Running = false;
            TestHook = null;
            RuntimeName = RuntimeVersion = LastReason = "";
        }

        // Brings the OpenXR loader up and starts its subsystems. True = a headset session is running.
        public static bool TryStart(out string reason)
        {
            reason = "";
            if (Running && Alive) return true;
            Running = false;
            if (TestHook != null)
            {
                if (TestHook(out reason)) { Running = true; RuntimeName = "test runtime"; return true; }
                return Fail(reason, out reason);
            }
            try
            {
                var general = XRGeneralSettings.Instance;
                var manager = general != null ? general.Manager : null;
                if (manager == null) return Fail(ReasonNoSettings, out reason);

                if (manager.activeLoader == null) manager.InitializeLoaderSync();
                if (manager.activeLoader == null) return Fail(DiagnoseNoLoader(), out reason);

                manager.StartSubsystems();
                Running = true;
                RuntimeName = SafeString(() => OpenXRRuntime.name);
                RuntimeVersion = SafeString(() => OpenXRRuntime.version);
                Application.quitting -= Stop;
                Application.quitting += Stop;
                Debug.Log($"[XR] OpenXR runtime started: {RuntimeName} {RuntimeVersion} (API {SafeString(() => OpenXRRuntime.apiVersion)}).");
                LastReason = "";
                return true;
            }
            catch (Exception e)
            {
                Stop();
                return Fail("OpenXR failed to start: " + e.Message, out reason);
            }
        }

        // StopSubsystems + DeinitializeLoader. Safe to call when XR never started.
        public static void Stop()
        {
            Application.quitting -= Stop;
            if (TestHook != null) { Running = false; return; }
            try
            {
                var general = XRGeneralSettings.Instance;
                var manager = general != null ? general.Manager : null;
                if (manager != null && manager.activeLoader != null)
                {
                    manager.StopSubsystems();
                    manager.DeinitializeLoader();
                    Debug.Log("[XR] OpenXR stopped.");
                }
            }
            catch (Exception e) { Debug.LogWarning("[XR] Stop: " + e.Message); }
            Running = false;
        }

        // The loader is still up (a runtime that quits under us leaves Running true until this is checked).
        public static bool Alive
        {
            get
            {
                if (TestHook != null) return Running;
                var general = XRGeneralSettings.Instance;
                var manager = general != null ? general.Manager : null;
                return manager != null && manager.activeLoader != null;
            }
        }

        static bool Fail(string why, out string reason)
        {
            reason = LastReason = why;
            Debug.Log("[XR] Headset not available: " + why);
            return false;
        }

        static string SafeString(Func<string> f)
        {
            try { return f() ?? ""; } catch { return ""; }
        }

        // Why the loader would not start. A runtime registered in Windows (or XR_RUNTIME_JSON) but no session = no headset ready.
        static string DiagnoseNoLoader()
        {
            string runtime = RegisteredRuntime();
            return string.IsNullOrEmpty(runtime) ? ReasonNoRuntime : ReasonNoHeadset + " [runtime: " + runtime + "]";
        }

        public static string RegisteredRuntime()
        {
            string path = null;
            try { path = System.Environment.GetEnvironmentVariable("XR_RUNTIME_JSON"); } catch { }
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (string.IsNullOrEmpty(path)) path = ReadActiveRuntimeFromRegistry();
#endif
            return path ?? "";
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        // The Khronos registry key names the active OpenXR runtime. The .NET profile here has no Registry class, so ask reg.exe.
        static string ReadActiveRuntimeFromRegistry()
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo("reg", @"query HKLM\SOFTWARE\Khronos\OpenXR\1 /v ActiveRuntime")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true,
                };
                using (var process = System.Diagnostics.Process.Start(info))
                {
                    string text = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(2000)) return "";
                    int at = text.IndexOf("REG_SZ", StringComparison.Ordinal);
                    return at < 0 ? "" : text.Substring(at + 6).Trim();
                }
            }
            catch { return ""; }
        }
#endif
    }

    // What the headset can show, and what that means for the size rule. Pure maths so it is checked without a headset.
    public static class XrDisplay
    {
        // Vertical / horizontal field of view (degrees) from one eye's projection matrix (asymmetric frusta are fine).
        public static void FieldOfView(Matrix4x4 p, out float horizontal, out float vertical)
        {
            horizontal = vertical = 0f;
            if (Mathf.Abs(p.m00) < 1e-6f || Mathf.Abs(p.m11) < 1e-6f) return;
            horizontal = (Mathf.Atan((p.m02 + 1f) / p.m00) - Mathf.Atan((p.m02 - 1f) / p.m00)) * Mathf.Rad2Deg;
            vertical = (Mathf.Atan((p.m12 + 1f) / p.m11) - Mathf.Atan((p.m12 - 1f) / p.m11)) * Mathf.Rad2Deg;
        }

        public static float ArcminPerPixel(float verticalFov, int pixelRows) => verticalFov * 60f / Mathf.Max(1, pixelRows);

        // The vertical FOV to plug into ContactSensor's size rule (threshold = minApparentDegrees x fov / referenceFov) so that a speck
        // has the same ANGULAR size threshold as on the calibrated desktop (desktopFov = the true-size display's vertical FOV, or 60),
        // unless the headset cannot resolve that: below minPixels of the headset's own pixels it would be invisible, so the threshold
        // rises to minPixels. Returns the FOV that produces the threshold.
        public static float ThresholdFov(float desktopFov, float eyeVerticalFov, int eyePixelRows, float minApparentDegrees, float referenceFov, float minPixels = 1.5f)
        {
            if (minApparentDegrees <= 0f) return desktopFov;
            float wanted = minApparentDegrees * desktopFov / referenceFov;
            float resolvable = minPixels * ArcminPerPixel(eyeVerticalFov, eyePixelRows) / 60f;
            return Mathf.Max(wanted, resolvable) / minApparentDegrees * referenceFov;
        }

        // The vertical FOV the desktop rule is calibrated at: the true-size display's FOV when set, else the default 60.
        public static float DesktopRuleFov()
        {
            var s = DisplayCalibration.Settings;
            return s.trueSize ? DisplayCalibration.VerticalFov(s) : 60f;
        }
    }

    // Test-only command-line switch: -chkVrProbe tries XrRuntime.TryStart once shortly after load, logs the result and stops XR.
    // It proves that a built game starts on the desktop and that asking for a headset with no runtime reports a clear reason.
    public static class XrProbe
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            foreach (var a in System.Environment.GetCommandLineArgs())
            {
                if (a != "-chkVrProbe") continue;
                var go = new GameObject("XrProbe");
                UnityEngine.Object.DontDestroyOnLoad(go);
                go.AddComponent<Runner>();
                return;
            }
        }

        class Runner : MonoBehaviour
        {
            System.Collections.IEnumerator Start()
            {
                for (int i = 0; i < 30; i++) yield return null;
                Debug.Log($"[VrProbe] before: scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} VrActive={ViewRigSwitch.VrActive} xrRunning={XrRuntime.Running} tier={Kurukshetra.Scenario.RunConditions.DisplayTier} registeredRuntime='{XrRuntime.RegisteredRuntime()}'");
                bool ok = XrRuntime.TryStart(out string reason);
                Debug.Log($"[VrProbe] TryStart -> {ok}, reason='{reason}', runtime='{XrRuntime.RuntimeName}' {XrRuntime.RuntimeVersion}");
                XrRuntime.Stop();
                Debug.Log($"[VrProbe] after Stop: xrRunning={XrRuntime.Running} alive={XrRuntime.Alive} VrActive={ViewRigSwitch.VrActive}");
                Debug.Log("[VrProbe] done");
                Destroy(gameObject);
            }
        }
    }
}
