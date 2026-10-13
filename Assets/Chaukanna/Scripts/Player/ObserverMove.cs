using Chaukanna.Scenario;
using Chaukanna.TerrainGen;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Chaukanna.Player
{
    // Simple first-person walking for the soldier, when the trainer allows it: W A S D, Left Shift to run.
    // No physics: the camera follows the ground height under it, so it works on the flat ground and on generated terrain.
    [RequireComponent(typeof(Camera))]
    public class ObserverMove : MonoBehaviour
    {
        [Tooltip("Used when no trainer config is applied. The trainer's 'Soldier movement' setting overrides it.")]
        public bool allowMovement;
        public float walkSpeed = 4f;
        public float runSpeed = 7f;
        [Tooltip("Camera height above the ground, metres.")]
        public float eyeHeight = 1.7f;
        [Tooltip("How far from the post the soldier may go, metres. 0 = no limit.")]
        public float maxDistanceFromPost = 50f;

        public bool Allowed { get; private set; }

        ScenarioSpawner spawner;
        int run = -1;
        Vector3 post;
        bool havePost;

        // Keeps a position within `radius` metres of the post on the ground plane. Radius 0 or less = no limit.
        public static Vector3 ClampToRadius(Vector3 post, Vector3 p, float radius)
        {
            if (radius <= 0f) return p;
            float dx = p.x - post.x, dz = p.z - post.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            if (d <= radius) return p;
            float k = radius / d;
            return new Vector3(post.x + dx * k, p.y, post.z + dz * k);
        }

        // Called when the trainer's session moves the post, so "back at the post" means the new place.
        public void SetPost(Vector3 eyePosition)
        {
            post = eyePosition;
            havePost = true;
            transform.position = eyePosition;
            // Keep whatever eye height the camera was placed at, so the view does not jump on the first step.
            float ground = TerrainBuilder.GroundHeight(eyePosition);
            float height = eyePosition.y - (float.IsNaN(ground) ? 0f : ground);
            if (height > 0.3f && height < 5f) eyeHeight = height;
        }

        void Update()
        {
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner != null && spawner.RunNumber != run)
            {
                run = spawner.RunNumber;
                var cfg = spawner.TrainerConfig;
                Allowed = cfg != null ? cfg.allowMovement : allowMovement;
                if (cfg != null) { walkSpeed = cfg.walkSpeed; runSpeed = cfg.runSpeed; maxDistanceFromPost = cfg.moveRadius; }
                // A new run starts back at the post.
                if (havePost) transform.position = post; else SetPost(transform.position);
            }
            else if (spawner == null) Allowed = allowMovement;

            var kb = Keyboard.current;
            if (!Allowed || kb == null || ContactRegistry.InputBlocked || Cursor.lockState != CursorLockMode.Locked) return;

            float forward = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
            float right = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
            if (forward == 0f && right == 0f) return;

            // Move along where the soldier is facing, on the ground plane.
            float yaw = transform.eulerAngles.y * Mathf.Deg2Rad;
            var ahead = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var side = new Vector3(ahead.z, 0f, -ahead.x);
            Vector3 step = (ahead * forward + side * right).normalized * (kb.leftShiftKey.isPressed ? runSpeed : walkSpeed) * Time.deltaTime;
            Vector3 p = transform.position + step;

            if (havePost) p = ClampToRadius(post, p, maxDistanceFromPost);

            float ground = TerrainBuilder.GroundHeight(p);
            if (float.IsNaN(ground))
            {
                // Off the edge of a generated terrain: stay put. With no terrain at all, the ground is flat at height 0.
                if (Terrain.activeTerrains.Length > 0) return;
                ground = 0f;
            }
            p.y = ground + eyeHeight;
            transform.position = p;
        }
    }
}
