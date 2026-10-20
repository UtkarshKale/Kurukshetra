using System;
using System.Collections.Generic;
using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Net
{
    // How another seat is drawn in the shared field. LanSession's 10 Hz seat poses drive it; the avatar owns its own smoothing.
    // A capsule "bean" is the first implementation (CapsuleAvatar); an animated 3D model (walk, run, hold binoculars, binoculars up)
    // can replace it by setting PlayerAvatars.Factory, without touching anything else (see TODO AVA-1b).
    public interface IPlayerAvatar
    {
        void SetIdentity(string name, string unit, string role);
        // eye = the seat's camera position (what the pose packet carries), yaw / pitch in degrees; dt = frame time for smoothing.
        void SetPose(Vector3 eye, float yaw, float pitch, float dt);
        void SetBinoculars(bool up);
        void SetTalking(bool talking);
        void SetVisible(bool visible);
        Vector3 TagPosition { get; }                  // world point the name tag hangs above
        string Tag { get; }
        void Dispose();
    }

    public class CapsuleAvatar : IPlayerAvatar
    {
        // The capsule is 1.8 m tall; its centre sits 0.8 m under the eye (the pose is the seat's camera).
        static readonly Color[] UnitColours =
        {
            new Color(0.25f, 0.6f, 0.95f), new Color(0.35f, 0.8f, 0.4f), new Color(0.9f, 0.5f, 0.85f),
            new Color(0.3f, 0.85f, 0.85f), new Color(0.75f, 0.6f, 0.95f), new Color(0.95f, 0.55f, 0.35f),
        };
        static readonly Color BinocularColour = new Color(1f, 0.78f, 0.1f);

        readonly GameObject root, body, marker;
        readonly Material bodyMat, markerMat;
        Color unitColour = UnitColours[0];
        string name = "", unit = "", role = "";
        bool binoculars, talking, havePose;
        float markerLift;                               // 0 = held at the chest, 1 = raised to the eyes
        Vector3 pos;
        float yaw;

        public Vector3 TagPosition => pos + Vector3.up * 0.4f;
        public string Tag => name + (unit.Length > 0 ? " [" + unit + "]" : "") + (role.Length > 0 ? "  " + role : "") + (binoculars ? "  BINOCULARS" : "") + (talking ? "  (talking)" : "");

        public CapsuleAvatar(Transform parent)
        {
            root = new GameObject("Avatar");
            root.transform.SetParent(parent, false);
            var baseMat = Chaukanna.RuntimeMaterials.Lit != null ? Chaukanna.RuntimeMaterials.Lit : Chaukanna.RuntimeMaterials.Unlit;
            bodyMat = baseMat != null ? new Material(baseMat) : null;
            markerMat = baseMat != null ? new Material(baseMat) : null;
            Tint(markerMat, new Color(0.12f, 0.12f, 0.14f));

            body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Bean";
            Strip(body);
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);       // capsule primitive is 2 m tall at scale 1
            body.transform.localPosition = new Vector3(0f, -0.8f, 0f);
            if (bodyMat != null) body.GetComponent<Renderer>().sharedMaterial = bodyMat;

            // The binocular marker: two short dark tubes side by side, in front of the chest; lifted to the eyes when the seat raises its binoculars.
            marker = new GameObject("Binoculars");
            marker.transform.SetParent(root.transform, false);
            for (int i = -1; i <= 1; i += 2)
            {
                var tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Strip(tube);
                tube.transform.SetParent(marker.transform, false);
                tube.transform.localPosition = new Vector3(0.07f * i, 0f, 0f);
                tube.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                tube.transform.localScale = new Vector3(0.11f, 0.12f, 0.11f);
                if (markerMat != null) tube.GetComponent<Renderer>().sharedMaterial = markerMat;
            }
            Colour();
        }

        static void Strip(GameObject g)
        {
            var c = g.GetComponent<Collider>();
            if (c != null) UnityEngine.Object.Destroy(c);
        }

        static void Tint(Material m, Color c)
        {
            if (m == null) return;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        void Colour() { Tint(bodyMat, binoculars ? BinocularColour : unitColour); }

        public void SetIdentity(string n, string u, string r)
        {
            name = n ?? ""; unit = u ?? ""; role = r ?? "";
            int h = 0;
            foreach (char ch in unit) h = h * 31 + ch;
            unitColour = UnitColours[Mathf.Abs(h) % UnitColours.Length];
            Colour();
        }

        public void SetPose(Vector3 eye, float y, float pitch, float dt)
        {
            Vector3 target = eye;
            if (!havePose) { pos = target; yaw = y; havePose = true; }
            // Poses arrive at 10 Hz: an exponential follow (about 0.07 s) hides the steps without adding much lag.
            float k = 1f - Mathf.Exp(-dt / 0.07f);
            pos = Vector3.Lerp(pos, target, k);
            yaw = Mathf.LerpAngle(yaw, y, k);
            root.transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            float want = binoculars ? 1f : 0f;
            markerLift = Mathf.MoveTowards(markerLift, want, dt * 5f);
            marker.transform.localPosition = new Vector3(0f, Mathf.Lerp(-0.45f, -0.02f, markerLift), 0.32f);
            marker.transform.localRotation = Quaternion.Euler(Mathf.Lerp(35f, 0f, markerLift) , 0f, 0f);
        }

        public void SetBinoculars(bool up) { if (up != binoculars) { binoculars = up; Colour(); } }
        public void SetTalking(bool t) { talking = t; }
        public void SetVisible(bool v) { if (root != null && root.activeSelf != v) root.SetActive(v); }

        public void Dispose()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            if (bodyMat != null) UnityEngine.Object.Destroy(bodyMat);
            if (markerMat != null) UnityEngine.Object.Destroy(markerMat);
        }
    }

    // Draws every other seat of a LAN run as an avatar at its synced pose, with a name / unit tag. Lives on the LanSession object
    // for the length of a run (LanSession adds it in EnterField and removes it in LeaveField / Leave).
    public class PlayerAvatars : MonoBehaviour
    {
        // Swap this to draw something other than a bean (called once per remote seat).
        public static Func<Transform, IPlayerAvatar> Factory = parent => new CapsuleAvatar(parent);

        const float StaleAfter = 5f;                    // no pose for this long: the seat is hidden
        LanSession lan;
        readonly Dictionary<ulong, IPlayerAvatar> avatars = new Dictionary<ulong, IPlayerAvatar>();
        readonly List<ulong> gone = new List<ulong>();
        GUIStyle tagStyle;

        public static void Begin(LanSession s)
        {
            if (s == null) return;
            var a = s.GetComponent<PlayerAvatars>();
            if (a == null) a = s.gameObject.AddComponent<PlayerAvatars>();
            a.lan = s;
        }

        public static void End(LanSession s)
        {
            if (s == null) return;
            var a = s.GetComponent<PlayerAvatars>();
            if (a != null) { a.Clear(); Destroy(a); }
        }

        public int Count => avatars.Count;

        void Clear()
        {
            foreach (var a in avatars.Values) a.Dispose();
            avatars.Clear();
        }

        void OnDestroy() { Clear(); }

        void LateUpdate()
        {
            if (lan == null || !lan.InField) { Clear(); return; }
            float dt = Time.deltaTime;
            foreach (var kv in lan.SeatPoses)
            {
                if (kv.Key == lan.LocalClientId) continue;
                var pose = kv.Value;
                if (!avatars.TryGetValue(kv.Key, out var av)) { av = Factory(transform); avatars[kv.Key] = av; }
                var p = lan.roster.Find(kv.Key);
                if (p != null) av.SetIdentity(p.name, lan.roster.UnitOrDefault(p.unit), lan.roster.mode == NetMode.Team ? NetPlayer.RoleName(p.Role) : "");
                bool stale = Time.time - pose.receivedAt > StaleAfter;
                av.SetVisible(!stale);
                if (stale) continue;
                av.SetBinoculars((pose.flags & 4) != 0);
                av.SetTalking((pose.flags & 3) != 0);
                av.SetPose(pose.position, pose.yaw, pose.pitch, dt);
            }
            gone.Clear();
            foreach (var id in avatars.Keys) if (!lan.SeatPoses.ContainsKey(id)) gone.Add(id);
            foreach (var id in gone) { avatars[id].Dispose(); avatars.Remove(id); }
        }

        void OnGUI()
        {
            if (avatars.Count == 0 || Event.current.type != EventType.Repaint || PlayerView.HideOverlays) return;
            var cam = Camera.main;
            if (cam == null) return;
            if (tagStyle == null) tagStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, richText = false };
            tagStyle.fontSize = Mathf.RoundToInt(13f * Screen.height / 720f);
            var view = cam.pixelRect;
            foreach (var kv in avatars)
            {
                if (lan.SeatPoses.TryGetValue(kv.Key, out var pose) && Time.time - pose.receivedAt > StaleAfter) continue;
                Vector3 sp = cam.WorldToScreenPoint(kv.Value.TagPosition);
                if (sp.z <= 0f || sp.z > 600f || !view.Contains(sp)) continue;
                var rect = new Rect(sp.x - 150f, Screen.height - sp.y - 14f, 300f, 22f);
                tagStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), kv.Value.Tag, tagStyle);
                tagStyle.normal.textColor = Color.white;
                GUI.Label(rect, kv.Value.Tag, tagStyle);
            }
        }
    }
}
