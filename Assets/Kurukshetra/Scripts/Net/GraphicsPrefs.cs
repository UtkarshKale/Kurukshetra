using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace Kurukshetra
{
    // One set of graphics options. Window mode: 0 Fullscreen, 1 Borderless, 2 Windowed. msaa: 1 (off), 2, 4 or 8.
    [Serializable]
    public struct GraphicsState
    {
        public int width, height, mode;
        public bool vsync;
        public int fps;                 // 0 = uncapped
        public int quality;             // index into QualitySettings.names
        public int renderScalePct;      // 100 = native
        public bool shadows;
        public int shadowDist;          // metres
        public int msaa;
        public int viewDistPct;         // 100 = unchanged
    }

    // Graphics options (F10 > Graphics, or the Settings buttons). Saved in PlayerPrefs (per PC) and applied at startup.
    // The URP values (render scale, MSAA, shadow distance) are changed on a runtime CLONE of the active pipeline asset, never on the
    // asset in Assets/Settings, so playing in the Editor does not dirty it; the original is put back when the game quits.
    public static class GraphicsPrefs
    {
        const string KSaved = "chk.gfx.saved", KW = "chk.gfx.w", KH = "chk.gfx.h", KMode = "chk.gfx.mode", KVsync = "chk.gfx.vsync", KFps = "chk.gfx.fps",
            KQuality = "chk.gfx.quality", KScale = "chk.gfx.scale", KShadows = "chk.gfx.shadows", KShadowDist = "chk.gfx.shadowdist", KMsaa = "chk.gfx.msaa", KView = "chk.gfx.view";

        static GraphicsState defaults, current;
        static bool captured;
        static readonly Dictionary<int, UniversalRenderPipelineAsset> clones = new Dictionary<int, UniversalRenderPipelineAsset>();
        static readonly Dictionary<int, RenderPipelineAsset> originals = new Dictionary<int, RenderPipelineAsset>();
        static int startQuality;

        // 1.0 = no change. Every distance cut-off (terrain trees, grass, bushes, building LODs, drones, birds, crowd) multiplies by this.
        public static float ViewDistanceScale { get; private set; } = 1f;

        // Raised at the end of every Apply, after ViewDistanceScale has its new value, so the distance systems can update live.
        public static event Action ViewDistanceChanged;

        static float lodBiasBase = -1f;     // QualitySettings.lodBias of the current level before the view-distance scale; < 0 = not read yet

        public static GraphicsState Defaults { get { Capture(); return defaults; } }
        public static GraphicsState Current { get { Capture(); return current; } }
        public static bool HasSaved => PlayerPrefs.GetInt(KSaved, 0) == 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            captured = false; clones.Clear(); originals.Clear(); ViewDistanceScale = 1f; lodBiasBase = -1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Startup()
        {
            Capture();
            Application.quitting -= Restore;
            Application.quitting += Restore;
            if (!HasSaved)
            {
                // Nothing saved: the level's own view distance (Low 60 %, Medium 80 %, ...) still applies.
                ViewDistanceScale = defaults.viewDistPct / 100f;
                lodBiasBase = QualitySettings.lodBias;
                QualitySettings.lodBias = lodBiasBase * ViewDistanceScale;
                return;
            }
            try { current = Load(); Apply(current); } catch (Exception) { }
        }

        static void Capture()
        {
            if (captured) return;
            captured = true;
            startQuality = QualitySettings.GetQualityLevel();
            var fsm = Screen.fullScreenMode;
            var asset = ActiveAsset() as UniversalRenderPipelineAsset;
            defaults = new GraphicsState
            {
                width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height),
                mode = fsm == FullScreenMode.ExclusiveFullScreen ? 0 : fsm == FullScreenMode.Windowed ? 2 : 1,
                vsync = QualitySettings.vSyncCount > 0,
                fps = Application.targetFrameRate > 0 ? Application.targetFrameRate : 0,
                quality = startQuality,
                renderScalePct = asset != null ? Mathf.RoundToInt(asset.renderScale * 100f) : 100,
                shadows = asset == null || asset.shadowDistance > 0f,
                shadowDist = asset != null ? Mathf.Max(1, Mathf.RoundToInt(asset.shadowDistance)) : 50,
                msaa = asset != null ? Mathf.Max(1, asset.msaaSampleCount) : 1,
                viewDistPct = PresetViewDistPct(startQuality),
            };
            current = defaults;
        }

        static RenderPipelineAsset ActiveAsset() => QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline;

        static GraphicsState Load()
        {
            var d = defaults;
            return new GraphicsState
            {
                width = Mathf.Max(1, PlayerPrefs.GetInt(KW, d.width)), height = Mathf.Max(1, PlayerPrefs.GetInt(KH, d.height)),
                mode = Mathf.Clamp(PlayerPrefs.GetInt(KMode, d.mode), 0, 2),
                vsync = PlayerPrefs.GetInt(KVsync, d.vsync ? 1 : 0) == 1,
                fps = Mathf.Max(0, PlayerPrefs.GetInt(KFps, d.fps)),
                quality = Mathf.Clamp(PlayerPrefs.GetInt(KQuality, d.quality), 0, Mathf.Max(0, QualitySettings.names.Length - 1)),
                renderScalePct = Mathf.Max(25, PlayerPrefs.GetInt(KScale, d.renderScalePct)),
                shadows = PlayerPrefs.GetInt(KShadows, d.shadows ? 1 : 0) == 1,
                shadowDist = Mathf.Max(1, PlayerPrefs.GetInt(KShadowDist, d.shadowDist)),
                msaa = SnapMsaa(PlayerPrefs.GetInt(KMsaa, d.msaa)),
                viewDistPct = Mathf.Max(1, PlayerPrefs.GetInt(KView, d.viewDistPct)),
            };
        }

        // Default view distance of a quality level (percent). Low 60, Medium 80, High 100, Ultra 130; any other level 100.
        public static int PresetViewDistPct(int level)
        {
            string[] names = QualitySettings.names;
            if (level < 0 || level >= names.Length) return 100;
            switch (names[level])
            {
                case "Low": return 60;
                case "Medium": return 80;
                case "Ultra": return 130;
                default: return 100;
            }
        }

        // Picking a preset in the window fills these from the level's own pipeline asset (and the view-distance table); each stays editable.
        public static GraphicsState WithPreset(GraphicsState s, int level)
        {
            Capture();
            s.quality = level;
            var urp = level >= 0 && level < QualitySettings.names.Length ? QualitySettings.GetRenderPipelineAssetAt(level) as UniversalRenderPipelineAsset : null;
            if (urp != null)
            {
                s.shadows = urp.shadowDistance > 0f;
                s.shadowDist = Mathf.Max(1, Mathf.RoundToInt(urp.shadowDistance));
                s.msaa = Mathf.Max(1, urp.msaaSampleCount);
                s.renderScalePct = Mathf.RoundToInt(urp.renderScale * 100f);
            }
            s.viewDistPct = PresetViewDistPct(level);
            return s;
        }

        public static int SnapMsaa(int v) => v >= 8 ? 8 : v >= 4 ? 4 : v >= 2 ? 2 : 1;

        // Applies the options now and keeps them as the saved choice.
        public static void ApplyAndSave(GraphicsState s)
        {
            Capture();
            s = Sanitise(s);
            Apply(s);
            current = s;
            try
            {
                PlayerPrefs.SetInt(KSaved, 1);
                PlayerPrefs.SetInt(KW, s.width); PlayerPrefs.SetInt(KH, s.height); PlayerPrefs.SetInt(KMode, s.mode);
                PlayerPrefs.SetInt(KVsync, s.vsync ? 1 : 0); PlayerPrefs.SetInt(KFps, s.fps); PlayerPrefs.SetInt(KQuality, s.quality);
                PlayerPrefs.SetInt(KScale, s.renderScalePct); PlayerPrefs.SetInt(KShadows, s.shadows ? 1 : 0); PlayerPrefs.SetInt(KShadowDist, s.shadowDist);
                PlayerPrefs.SetInt(KMsaa, s.msaa); PlayerPrefs.SetInt(KView, s.viewDistPct);
                PlayerPrefs.Save();
            }
            catch (Exception) { }
        }

        // Back to what the game started with, and forget the saved choice.
        public static void ResetToDefaults()
        {
            Capture();
            Apply(defaults);
            current = defaults;
            try
            {
                foreach (var k in new[] { KSaved, KW, KH, KMode, KVsync, KFps, KQuality, KScale, KShadows, KShadowDist, KMsaa, KView }) PlayerPrefs.DeleteKey(k);
                PlayerPrefs.Save();
            }
            catch (Exception) { }
        }

        static GraphicsState Sanitise(GraphicsState s)
        {
            s.width = Mathf.Max(1, s.width); s.height = Mathf.Max(1, s.height);
            s.mode = Mathf.Clamp(s.mode, 0, 2);
            s.fps = Mathf.Max(0, s.fps);
            s.quality = Mathf.Clamp(s.quality, 0, Mathf.Max(0, QualitySettings.names.Length - 1));
            s.renderScalePct = Mathf.Max(25, s.renderScalePct);
            s.shadowDist = Mathf.Max(1, s.shadowDist);
            s.msaa = SnapMsaa(s.msaa);
            s.viewDistPct = Mathf.Max(1, s.viewDistPct);
            return s;
        }

        static void Apply(GraphicsState s)
        {
            if (s.quality != QualitySettings.GetQualityLevel()) { QualitySettings.SetQualityLevel(s.quality, true); lodBiasBase = -1f; }
            if (lodBiasBase < 0f) lodBiasBase = QualitySettings.lodBias;
            var urp = EnsureClone();
            if (urp != null)
            {
                urp.renderScale = s.renderScalePct / 100f;            // URP itself caps this at its own maximum (2.0)
                urp.msaaSampleCount = s.msaa;
                urp.shadowDistance = s.shadows ? s.shadowDist : 0f;   // distance 0 = no shadows (the on/off switch itself is not settable at runtime)
            }
            QualitySettings.antiAliasing = s.msaa <= 1 ? 0 : s.msaa;
            QualitySettings.vSyncCount = s.vsync ? 1 : 0;
            Application.targetFrameRate = s.fps > 0 ? s.fps : -1;
            ViewDistanceScale = s.viewDistPct / 100f;
            QualitySettings.lodBias = lodBiasBase * ViewDistanceScale;        // mesh LODs (buildings, prefabs, terrain tree LODs) switch at scaled distances
            try { ViewDistanceChanged?.Invoke(); } catch (Exception) { }
            var fsm = s.mode == 0 ? FullScreenMode.ExclusiveFullScreen : s.mode == 1 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            if (s.width != Screen.width || s.height != Screen.height || fsm != Screen.fullScreenMode) Screen.SetResolution(s.width, s.height, fsm);
        }

        // The pipeline asset of the current quality level, cloned once; only the clone is ever modified.
        static UniversalRenderPipelineAsset EnsureClone()
        {
            int lvl = QualitySettings.GetQualityLevel();
            var active = ActiveAsset();
            if (clones.TryGetValue(lvl, out var have) && have != null && ReferenceEquals(active, have)) return have;
            var src = active as UniversalRenderPipelineAsset;
            if (src == null) return null;
            if (!originals.ContainsKey(lvl)) originals[lvl] = QualitySettings.renderPipeline;
            var clone = Object.Instantiate(src);
            clone.name = src.name + " (runtime clone)";
            clone.hideFlags = HideFlags.HideAndDontSave;
            QualitySettings.renderPipeline = clone;
            clones[lvl] = clone;
            return clone;
        }

        // On quit (and on leaving Play mode) the original pipeline assets go back and the clones are destroyed.
        static void Restore()
        {
            try
            {
                foreach (var kv in clones)
                {
                    QualitySettings.SetQualityLevel(kv.Key, false);
                    if (originals.TryGetValue(kv.Key, out var orig)) QualitySettings.renderPipeline = orig;
                }
                QualitySettings.SetQualityLevel(startQuality, false);
                if (lodBiasBase > 0f) QualitySettings.lodBias = lodBiasBase;
                foreach (var kv in clones) if (kv.Value != null) Object.Destroy(kv.Value);
            }
            catch (Exception) { }
            clones.Clear(); originals.Clear();
        }

        // Screen.resolutions with duplicates (same size, different refresh rate) removed, smallest first.
        public static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            try
            {
                foreach (var r in Screen.resolutions)
                {
                    var v = new Vector2Int(r.width, r.height);
                    if (!list.Contains(v)) list.Add(v);
                }
            }
            catch (Exception) { }
            if (list.Count == 0) list.Add(new Vector2Int(Screen.width, Screen.height));
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }
    }
}
