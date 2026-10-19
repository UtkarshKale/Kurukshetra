using System;
using System.Collections;
using System.Collections.Generic;
using Chaukanna.Scenario;
using Chaukanna.TerrainGen;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Chaukanna.Session
{
    // One trainee seat. Plain data on purpose: with LAN multiplayer the host (trainer) will send this list to every
    // client, each client will set its own Ready flag, and each player is spawned at their post.
    [Serializable]
    public class PlayerSlot
    {
        public string name = "Player";
        public Vector2 post;        // metres east / north of the terrain centre
        public bool ready;
        public bool isLocal;        // played on this machine; exactly one today
    }

    // Everything the trainer decided in the menu, carried into the field scene. Static so it survives the scene change;
    // nothing here is written to disk.
    public static class SessionSetup
    {
        public static bool Ready;                           // START was pressed in the trainer menu
        public static TerrainGenConfig terrain;             // runtime copy, never an asset
        public static ElevationGrid dem;                    // loaded source elevation, if the mode uses one
        public static Imagery imagery;                      // optional ground picture
        public static ElevationGrid surroundDem;            // optional coarse ground around the play area, for the horizon
        public static Imagery surroundImagery;
        public static ScenarioConfig scenario;              // before difficulty; the level is applied when the run starts
        public static int difficultyIndex;
        public static readonly List<PlayerSlot> players = new List<PlayerSlot>();
        public static readonly List<Vector2> spawnPoints = new List<Vector2>();   // drone spawn points; empty = the 1 km ring
        public static string menuScenePath = "";            // so the field can go back

        public static PlayerSlot LocalPlayer
        {
            get
            {
                foreach (var p in players) if (p.isLocal) return p;
                return players.Count > 0 ? players[0] : null;
            }
        }

        public static void Clear()
        {
            Ready = false;
            terrain = null;
            dem = null;
            imagery = null;
            surroundDem = null;
            surroundImagery = null;
            scenario = null;
            difficultyIndex = 0;
            players.Clear();
            spawnPoints.Clear();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Init()
        {
            Clear();
            menuScenePath = "";
            SceneManager.sceneLoaded -= FieldBootstrap.OnSceneLoaded;
            SceneManager.sceneLoaded += FieldBootstrap.OnSceneLoaded;
        }

        // START in the trainer menu: the same load, behind a loading screen, spread over frames so the window does
        // not freeze on a blank picture.
        public static void LoadWithScreen(string scenePath)
        {
            var screen = LoadingScreen.Show();
            screen.StartCoroutine(screen.LoadField(scenePath));
        }

        // Loads a scene by path. In the Editor this works without the scene being in Build Settings; a built player
        // needs both scenes added there.
        public static void Load(string scenePath)
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            SceneManager.LoadScene(System.IO.Path.GetFileNameWithoutExtension(scenePath), LoadSceneMode.Single);
#endif
        }
    }

    // Full-window loading screen shown from START in the trainer menu until the run is ready. It survives the scene
    // change, and drives the load itself: scene first, then terrain, posts and contacts, one step per frame so each
    // step's name is on screen before the work for it begins.
    public class LoadingScreen : MonoBehaviour
    {
        public static LoadingScreen Current { get; private set; }

        public string Step { get; private set; } = "";
        public float Progress { get; private set; }

        static readonly string[] Tips =
        {
            "Right mouse button raises the binoculars. Small contacts can only be picked out through them.",
            "Classify first (1 to 5), then act: H hold, R report, J jam, F engage.",
            "A call can be changed at any time. Changing it with nothing new to go on costs a little.",
            "Ctrl + click adds a contact to a group. G selects everything in view, X clears the group.",
            "Check the ROE card at the top of the screen before you engage. It can change during the run.",
            "An own drone that leaves its patrol pattern is worth a second look.",
            "Jamming only works on quadcopters. Fixed-wing drones and birds carry on.",
        };

        float shown, fade = 1f;
        bool closing;
        string tip = "";
        GUIStyle title, text, small;

        public static LoadingScreen Show()
        {
            if (Current != null) return Current;
            var go = new GameObject("LoadingScreen");
            DontDestroyOnLoad(go);
            Current = go.AddComponent<LoadingScreen>();
            Current.tip = Tips[(System.Environment.TickCount & 0x7FFFFFFF) % Tips.Length];
            return Current;
        }

        public void Set(string step, float progress)
        {
            Step = step;
            Progress = Mathf.Clamp01(progress);
        }

        public void Done()
        {
            Progress = 1f;
            closing = true;
        }

        // Loads the field scene and sets the session up in it, a step at a time.
        public IEnumerator LoadField(string scenePath)
        {
            ContactRegistry.InputBlocked = true;
            Set("Loading the field", 0.05f);
            yield return null;
            yield return null;      // two frames, so the screen is painted before the load starts

#if UNITY_EDITOR
            var op = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            var op = SceneManager.LoadSceneAsync(System.IO.Path.GetFileNameWithoutExtension(scenePath), LoadSceneMode.Single);
#endif
            while (op != null && !op.isDone)
            {
                Set("Loading the field", 0.05f + 0.35f * op.progress);
                yield return null;
            }

            var spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner != null && SessionSetup.Ready && SessionSetup.terrain != null)
            {
                Exception problem = null;
                // Terrain one step per frame (the ground shape, then each tile), so the bar keeps moving.
                Set("Generating terrain", 0.42f);
                yield return null;
                IEnumerator build = null;
                try { build = FieldBootstrap.BuildTerrainInSteps(spawner.gameObject.scene, (f, what) => Set("Generating terrain: " + what, 0.42f + 0.4f * f)); }
                catch (Exception e) { problem = e; }
                while (problem == null && build != null)
                {
                    bool more;
                    try { more = build.MoveNext(); } catch (Exception e) { problem = e; break; }
                    if (!more) break;
                    yield return null;
                }

                if (problem == null)
                {
                    Set("Placing posts", 0.85f);
                    yield return null;
                    try { FieldBootstrap.PlacePosts(spawner); } catch (Exception e) { problem = e; }
                }
                if (problem == null)
                {
                    Set("Spawning contacts", 0.95f);
                    yield return null;
                    try { FieldBootstrap.StartRun(spawner); } catch (Exception e) { problem = e; }
                    yield return null;
                }
                if (problem != null) Debug.LogError("[Session] Could not set up the field from the trainer's session: " + problem);
            }
            ContactRegistry.InputBlocked = false;
            Done();
        }

        void Update()
        {
            shown = Mathf.MoveTowards(shown, Progress, Time.unscaledDeltaTime * 1.5f);
            if (!closing) return;
            fade -= Time.unscaledDeltaTime * 3f;
            if (fade <= 0f)
            {
                if (Current == this) Current = null;
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void OnGUI()
        {
            GUI.depth = -1000;      // above everything
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold };
                text = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft };
                small = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true };
            }
            float ui = Screen.height / 720f;
            title.fontSize = Mathf.RoundToInt(34f * ui);
            text.fontSize = Mathf.RoundToInt(19f * ui);
            small.fontSize = Mathf.RoundToInt(15f * ui);
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            // Swallows clicks so nothing underneath (the menu's START, the trainer panel) can be pressed twice.
            if (!closing) GUI.Button(full, GUIContent.none, GUIStyle.none);
            if (Event.current.type != EventType.Repaint) return;

            var old = GUI.color;
            GUI.color = new Color(0.04f, 0.05f, 0.07f, fade);
            GUI.DrawTexture(full, Texture2D.whiteTexture);

            float w = Mathf.Min(Screen.width * 0.6f, 760f * ui), x = (Screen.width - w) * 0.5f, y = Screen.height * 0.40f;
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.Label(new Rect(x, y - 70f * ui, w, 50f * ui), "KURUKSHETRA", title);
            GUI.Label(new Rect(x, y - 18f * ui, w, 30f * ui), Step + (closing ? "" : new string('.', 1 + (int)(Time.unscaledTime * 2f) % 3)), text);

            var bar = new Rect(x, y + 20f * ui, w, 10f * ui);
            GUI.color = new Color(1f, 1f, 1f, 0.15f * fade);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 0.75f, 1f, fade);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(shown), bar.height), Texture2D.whiteTexture);
            GUI.color = new Color(0.75f, 0.8f, 0.85f, fade);
            GUI.Label(new Rect(x, y + 34f * ui, w, 24f * ui), $"{Mathf.RoundToInt(shown * 100f)} %", small);
            GUI.Label(new Rect(x, y + 80f * ui, w, 80f * ui), "TIP   " + tip, small);
            GUI.color = old;
        }
    }

    // Turns a freshly loaded field scene into the session the trainer set up: builds the terrain, moves the post,
    // hands over spawn points and starts the run. Does nothing when the scene is opened on its own.
    public static class FieldBootstrap
    {
        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!SessionSetup.Ready || SessionSetup.terrain == null) return;
            if (LoadingScreen.Current != null) return;      // the loading screen runs the same steps itself, one per frame
            var spawner = UnityEngine.Object.FindAnyObjectByType<ScenarioSpawner>();
            if (spawner == null) return;       // not a field scene

            try
            {
                Apply(scene, spawner);
            }
            catch (Exception e)
            {
                Debug.LogError("[Session] Could not set up the field from the trainer's session: " + e);
            }
        }

        static void Apply(Scene scene, ScenarioSpawner spawner)
        {
            BuildTerrain(scene);
            PlacePosts(spawner);
            // Start once every object in the scene has had its Start.
            var runner = new GameObject("SessionStarter").AddComponent<SessionStarter>();
            runner.spawner = spawner;
        }

        public static void BuildTerrain(Scene scene)
        {
            var steps = BuildTerrainInSteps(scene, null);
            while (steps.MoveNext()) { }
        }

        public static IEnumerator BuildTerrainInSteps(Scene scene, Action<float, string> progress)
        {
            surroundSize = 0f;
            // Hide whatever ground the scene shipped with: the flat "Ground" and any terrain baked in the Editor.
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "Ground" || go.GetComponent<GeneratedTerrainRoot>() != null) go.SetActive(false);

            var cfg = SessionSetup.terrain;
            cfg.padCentres = new List<Vector2>();
            foreach (var p in SessionSetup.players) cfg.padCentres.Add(p.post);
            var built = new TerrainBuildResult();
            var steps = TerrainBuilder.BuildInSteps(built, cfg, SessionSetup.dem, null, null, SessionSetup.imagery, null, progress);
            while (steps.MoveNext()) yield return null;
            built.root.name = "GeneratedTerrain (session)";
            Debug.Log("[Session] Terrain built for the session.\n" + built.report);

            TerrainBuildResult around = null;
            if (SessionSetup.surroundDem != null)
            {
                progress?.Invoke(1f, "Building the surround (horizon)");
                yield return null;
                around = TerrainBuilder.BuildSurround(cfg, SessionSetup.surroundDem, SessionSetup.surroundImagery, built.field.centreElevation);
                surroundSize = around.field.sizeMetres;
                Debug.Log($"[Session] Surround built: {surroundSize / 1000f:F0} km square around the play area.");
            }

            // Trees, grass and buildings on top of the ground (one frame-sized slice at a time). A failure here costs the
            // layers, not the run.
            progress?.Invoke(1f, "Trees, grass and buildings");
            yield return null;
            IEnumerator layers = null;
            Exception layerProblem = null;
            try { layers = BuildLayers(cfg, built, around, progress); }
            catch (Exception e) { layerProblem = e; }
            while (layerProblem == null && layers != null)
            {
                bool more;
                try { more = layers.MoveNext(); } catch (Exception e) { layerProblem = e; break; }
                if (!more) break;
                yield return null;
            }
            if (layerProblem != null) Debug.LogError("[Session] The terrain layers (trees, grass, buildings) could not be built: " + layerProblem);
        }

        // Makes the layer manager for this session and builds all three layers.
        static IEnumerator BuildLayers(TerrainGenConfig cfg, TerrainBuildResult built, TerrainBuildResult around, Action<float, string> progress)
        {
            var spawner = UnityEngine.Object.FindAnyObjectByType<ScenarioSpawner>();
            var ctx = new LayerContext
            {
                cfg = cfg, kit = TerrainLayerKit.Default, demPath = cfg.demPath,
                playImagery = SessionSetup.imagery, surroundImagery = SessionSetup.surroundImagery,
                biome = SurfaceBiomes.Resolve(cfg.biome, built.field),
                centreLatitude = SessionSetup.dem != null ? SessionSetup.dem.CentreLatitude : cfg.centreLatitude,
                centreLongitude = SessionSetup.dem != null ? SessionSetup.dem.CentreLongitude : cfg.centreLongitude,
            };
            ctx.play = new LayerGround(built.field, cfg.groundPostAtZero ? -built.field.centreElevation : 0f);
            ctx.playTiles.AddRange(built.tiles);
            if (around != null)
            {
                ctx.surround = new LayerGround(around.field, -built.field.centreElevation - TerrainBuilder.SurroundSink);
                ctx.surroundTiles.AddRange(around.tiles);
            }
            var local = SessionSetup.LocalPlayer;
            if (local != null) ctx.posts.Add(local.post);
            foreach (var p in SessionSetup.players) if (p != local) ctx.posts.Add(p.post);

            // The site stands around the post: the layers keep clear of it from the start.
            if (spawner != null && local != null && SessionSetup.scenario != null)
                SiteClearance.Set(SiteBuilder.Clearance(new Vector3(local.post.x, 0f, local.post.y), spawner.transform.forward, SessionSetup.scenario.site, spawner.preset != null ? spawner.preset.siteProfile : null));
            else SiteClearance.Clear();

            var manager = TerrainLayerManager.Create(ctx);
            var run = manager.BuildAll((f, text) => progress?.Invoke(1f, "Trees, grass and buildings: " + text));
            while (run.MoveNext()) yield return run.Current;
            Debug.Log("[Session] Terrain layers: " + manager.Summary());
        }

        static float surroundSize;

        public static void PlacePosts(ScenarioSpawner spawner)
        {
            // The local player's post: spawner (the defended point and sector origin), camera and post marker move there.
            var cfg = SessionSetup.terrain;
            var local = SessionSetup.LocalPlayer;
            Vector2 post = local != null ? local.post : Vector2.zero;
            float ground = TerrainBuilder.GroundHeight(new Vector3(post.x, 0f, post.y));
            if (float.IsNaN(ground)) ground = 0f;
            spawner.transform.position = new Vector3(post.x, ground, post.y);
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(post.x, ground + 1.7f, post.y);
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, cfg.sizeMetres * 1.5f, surroundSize * 0.75f);
                var walker = cam.GetComponent<Chaukanna.Player.ObserverMove>();
                if (walker == null) walker = cam.gameObject.AddComponent<Chaukanna.Player.ObserverMove>();
                walker.SetPost(cam.transform.position);
            }
            var marker = GameObject.Find("ObserverPost");
            if (marker != null) marker.transform.position = new Vector3(post.x, ground + 0.75f, post.y);

            spawner.customSpawnPoints = new List<Vector3>();
            foreach (var s in SessionSetup.spawnPoints) spawner.customSpawnPoints.Add(new Vector3(s.x, 0f, s.y));
        }

        // Goes through the in-field trainer panel when there is one, so it shows the same settings and applies the difficulty level.
        public static void StartRun(ScenarioSpawner spawner)
        {
            if (spawner == null || SessionSetup.scenario == null) return;
            var panel = UnityEngine.Object.FindAnyObjectByType<Chaukanna.Trainer.TrainerScreen>();
            if (panel != null) panel.StartSessionRun(SessionSetup.scenario, SessionSetup.difficultyIndex);
            else spawner.StartRun(SessionSetup.scenario);
        }
    }
    // Starts the trainer's run one frame after the field scene has loaded.
    public class SessionStarter : MonoBehaviour
    {
        public ScenarioSpawner spawner;

        System.Collections.IEnumerator Start()
        {
            yield return null;
            FieldBootstrap.StartRun(spawner);
            Destroy(gameObject);
        }
    }
}
