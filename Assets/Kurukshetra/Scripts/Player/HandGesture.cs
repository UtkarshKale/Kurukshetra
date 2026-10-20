using UnityEngine;

namespace Kurukshetra.Player
{
    // The hands of the seated VR soldier as a small state machine, so it can be tested without a headset or a scene.
    // Inputs each frame: is each hand being held up, is the left hand (sensor tablet) brought closer.
    // Outputs: how far each hand is raised (0 = resting low in view, 1 = at the face), and whether the binoculars are up.
    // Raising takes about RaiseSeconds, so the optic fades in rather than snapping (comfort rule: ~150 ms).
    public class HandGesture
    {
        public const float RaiseSeconds = 0.15f;

        public float Left { get; private set; }         // 0 rest, 1 at the face
        public float Right { get; private set; }
        public float TabletCloser { get; private set; } // 0 low-left glance position, 1 brought up to read
        public bool BinocularsUp { get; private set; }

        // Both hands up to the face = binoculars. Lowering either hand puts them away straight away.
        public void Tick(bool leftUp, bool rightUp, bool tabletUp, float dt)
        {
            float step = RaiseSeconds > 0f ? dt / RaiseSeconds : 1f;
            Left = Mathf.MoveTowards(Left, leftUp ? 1f : 0f, step);
            Right = Mathf.MoveTowards(Right, rightUp ? 1f : 0f, step);
            // The tablet only comes closer while the left hand is not up at the face.
            TabletCloser = Mathf.MoveTowards(TabletCloser, tabletUp && !leftUp ? 1f : 0f, step);
            BinocularsUp = leftUp && rightUp;
        }

        // How visible the optic should be: it fades in as the slower of the two hands arrives.
        public float OpticBlend => BinocularsUp ? Mathf.Min(Left, Right) : 0f;

        public void Reset()
        {
            Left = Right = TabletCloser = 0f;
            BinocularsUp = false;
        }
    }

    // The right-hand radial menu: items round a circle, picked by the direction the mouse (later the thumbstick) is pushed.
    public static class RadialMenu
    {
        // Items start at the top and go clockwise. direction: x right, y up. Returns -1 inside the dead zone.
        public static int Pick(Vector2 direction, int count, float deadZone = 0.35f)
        {
            if (count <= 0 || direction.magnitude < deadZone) return -1;
            float angle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;      // 0 = up, clockwise positive
            if (angle < 0f) angle += 360f;
            float sector = 360f / count;
            return Mathf.FloorToInt((angle + sector * 0.5f) / sector) % count;
        }

        // Where item i sits, as a unit direction (x right, y up).
        public static Vector2 Direction(int index, int count)
        {
            float a = index * 360f / count * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(a), Mathf.Cos(a));
        }
    }
}
