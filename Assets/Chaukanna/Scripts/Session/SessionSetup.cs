using System;
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

    // Turns a freshly loaded field scene into the session the trainer set up: builds the terrain, moves the post,
    // hands over spawn points and starts the run. Does nothing when the scene is opened on its own.
    public static class FieldBootstrap
    {
        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!SessionSetup.Ready || SessionSetup.terrain == null) return;
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
            // Hide whatever ground the scene shipped with: the flat "Ground" and any terrain baked in the Editor.
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == "Ground" || go.GetComponent<GeneratedTerrainRoot>() != null) go.SetActive(false);

            var cfg = SessionSetup.terrain;
            cfg.padCentres = new List<Vector2>();
            foreach (var p in SessionSetup.players) cfg.padCentres.Add(p.post);
            var built = TerrainBuilder.Build(cfg, SessionSetup.dem, null, null, SessionSetup.imagery);
            built.root.name = "GeneratedTerrain (session)";
            Debug.Log("[Session] Terrain built for the session.\n" + built.report);

            // The local player's post: spawner (the defended point and sector origin), camera and post marker move there.
            var local = SessionSetup.LocalPlayer;
            Vector2 post = local != null ? local.post : Vector2.zero;
            float ground = TerrainBuilder.GroundHeight(new Vector3(post.x, 0f, post.y));
            if (float.IsNaN(ground)) ground = 0f;
            spawner.transform.position = new Vector3(post.x, ground, post.y);
            var cam = Camera.main;
            if (cam != null)
            {
                cam.transform.position = new Vector3(post.x, ground + 1.7f, post.y);
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, cfg.sizeMetres * 1.5f);
                var walker = cam.GetComponent<Chaukanna.Player.ObserverMove>();
                if (walker == null) walker = cam.gameObject.AddComponent<Chaukanna.Player.ObserverMove>();
                walker.SetPost(cam.transform.position);
            }
            var marker = GameObject.Find("ObserverPost");
            if (marker != null) marker.transform.position = new Vector3(post.x, ground + 0.75f, post.y);

            spawner.customSpawnPoints = new List<Vector3>();
            foreach (var s in SessionSetup.spawnPoints) spawner.customSpawnPoints.Add(new Vector3(s.x, 0f, s.y));

            // Start once every object in the scene has had its Start.
            var runner = new GameObject("SessionStarter").AddComponent<SessionStarter>();
            runner.spawner = spawner;
        }
    }

    // Starts the trainer's run one frame after the field scene has loaded.
    public class SessionStarter : MonoBehaviour
    {
        public ScenarioSpawner spawner;

        System.Collections.IEnumerator Start()
        {
            yield return null;
            if (spawner != null && SessionSetup.scenario != null)
            {
                // Go through the in-field trainer panel when there is one, so it shows the same settings and applies the difficulty level.
                var panel = FindAnyObjectByType<Chaukanna.Trainer.TrainerScreen>();
                if (panel != null) panel.StartSessionRun(SessionSetup.scenario, SessionSetup.difficultyIndex);
                else spawner.StartRun(SessionSetup.scenario);
            }
            Destroy(gameObject);
        }
    }
}
