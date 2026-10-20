using System.Text;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace Kurukshetra.Player.EditorTools
{
    // One-time OpenXR setup for the Windows standalone build (menu: Kurukshetra > VR > Set up OpenXR; also callable from the CLI).
    // It only touches the XR settings assets under Assets/XR and the XR loader-settings entry in EditorBuildSettings:
    //   - the OpenXR loader is assigned for Standalone;
    //   - "Initialize XR on Startup" is OFF (the game starts on the desktop and brings the headset up only when asked: XrRuntime);
    //   - render mode Single Pass Instanced, depth submission on (16 bit);
    //   - the controller profiles are enabled: Oculus Touch, Valve Index, HTC Vive, HP Reverb G2, Microsoft Motion Controller, Khronos simple.
    // It does not touch Player settings, quality, the URP assets, layers or tags.
    public static class XrSetup
    {
        [MenuItem("Kurukshetra/VR/Set up OpenXR")]
        public static void Run() => Debug.Log(Setup());

        public static string Setup()
        {
            var log = new StringBuilder();
            var group = BuildTargetGroup.Standalone;

            // 1. The per-build-target XR settings asset (created under Assets/XR and registered in EditorBuildSettings).
            var per = FindOrCreatePerBuildTarget(log);
            if (per == null) return "XR settings asset could not be created.";
            if (!per.HasSettingsForBuildTarget(group)) { per.CreateDefaultSettingsForBuildTarget(group); log.AppendLine("created Standalone XR settings"); }
            if (!per.HasManagerSettingsForBuildTarget(group)) { per.CreateDefaultManagerSettingsForBuildTarget(group); log.AppendLine("created Standalone XR manager"); }
            var general = per.SettingsForBuildTarget(group);
            general.InitManagerOnStart = false;
            EditorUtility.SetDirty(general);
            var manager = per.ManagerSettingsForBuildTarget(group);
            manager.automaticLoading = false;
            manager.automaticRunning = false;
            EditorUtility.SetDirty(manager);

            // 2. The OpenXR loader.
            if (!HasLoader(manager))
            {
                log.AppendLine(XRPackageMetadataStore.AssignLoader(manager, typeof(OpenXRLoader).FullName, group) ? "assigned the OpenXR loader" : "COULD NOT assign the OpenXR loader");
            }
            else log.AppendLine("OpenXR loader already assigned");

            // 3. OpenXR settings: render mode, depth, controller profiles.
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (settings == null) log.AppendLine("OpenXR settings for Standalone not found");
            else
            {
                settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
                settings.depthSubmissionMode = OpenXRSettings.DepthSubmissionMode.Depth16Bit;
                EnableProfile<OculusTouchControllerProfile>(settings, log);
                EnableProfile<ValveIndexControllerProfile>(settings, log);
                EnableProfile<HTCViveControllerProfile>(settings, log);
                EnableProfile<HPReverbG2ControllerProfile>(settings, log);
                EnableProfile<MicrosoftMotionControllerProfile>(settings, log);
                EnableProfile<KHRSimpleControllerProfile>(settings, log);
                EditorUtility.SetDirty(settings);
            }
            AssetDatabase.SaveAssets();
            log.AppendLine(Describe());
            return log.ToString();
        }

        static bool HasLoader(XRManagerSettings manager)
        {
            foreach (var l in manager.activeLoaders) if (l is OpenXRLoader) return true;
            return false;
        }

        // What is set now, for the log and the report.
        public static string Describe()
        {
            var group = BuildTargetGroup.Standalone;
            var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            var sb = new StringBuilder("XR state: ");
            if (general == null || general.Manager == null) return sb.Append("no Standalone XR settings").ToString();
            sb.Append("initOnStart=").Append(general.InitManagerOnStart);
            sb.Append(", loaders=");
            foreach (var l in general.Manager.activeLoaders) sb.Append(l != null ? l.GetType().Name : "null").Append(' ');
            var s = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            if (s != null)
            {
                sb.Append(", render=").Append(s.renderMode).Append(", depth=").Append(s.depthSubmissionMode).Append(", profiles=");
                foreach (var f in s.GetFeatures<OpenXRInteractionFeature>()) if (f.enabled) sb.Append(f.GetType().Name).Append(' ');
            }
            return sb.ToString();
        }

        static XRGeneralSettingsPerBuildTarget FindOrCreatePerBuildTarget(StringBuilder log)
        {
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out XRGeneralSettingsPerBuildTarget per);
            if (per != null) return per;
            foreach (var guid in AssetDatabase.FindAssets("t:XRGeneralSettingsPerBuildTarget"))
            {
                per = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(AssetDatabase.GUIDToAssetPath(guid));
                if (per != null) break;
            }
            if (per == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/XR")) AssetDatabase.CreateFolder("Assets", "XR");
                per = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(per, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                AssetDatabase.SaveAssets();
                log.AppendLine("created Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            }
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.settingsKey, per, true);
            return per;
        }

        static void EnableProfile<T>(OpenXRSettings settings, StringBuilder log) where T : OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null) { log.AppendLine("profile not found: " + typeof(T).Name); return; }
            if (!feature.enabled) { feature.enabled = true; EditorUtility.SetDirty(feature); log.AppendLine("enabled " + typeof(T).Name); }
        }
    }
}
