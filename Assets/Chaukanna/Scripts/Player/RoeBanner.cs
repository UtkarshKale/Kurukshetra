using Chaukanna.Scenario;
using UnityEngine;

namespace Chaukanna.Player
{

    // Top-centre ROE banner and top-right mission countdown. Display only: nothing is enforced or scored here.
    public class RoeBanner : MonoBehaviour
    {
        [Header("ROE")]
        public RoeCard card = RoeCard.WeaponsHold;
        [Tooltip("Switch to another card part-way through the run.")]
        public bool flipEnabled;
        [Tooltip("Scenario seconds at which the card flips.")]
        public float flipTime = 120f;
        public RoeCard flipTo = RoeCard.UnderAttack;
        [Tooltip("Seconds the banner flashes after a flip.")]
        public float flashSeconds = 3f;

        [Header("Text size at 720p; scales with screen height")]
        public int bannerFontSize = 24;
        public int timerFontSize = 24;

        public RoeCard Current { get; private set; }

        ScenarioSpawner spawner;
        int run = -1;
        float flippedAt = -1f;

        // In force for this run: from the trainer config when one is applied, else from the fields above.
        RoeCard startCard, flipCard;
        bool doFlip;
        float flipAt;
        GUIStyle style;

        public static string Label(RoeCard c)
        {
            switch (c)
            {
                case RoeCard.WeaponsHold: return "WEAPONS HOLD";
                case RoeCard.WeaponsTight: return "WEAPONS TIGHT";
                case RoeCard.WeaponsFree: return "WEAPONS FREE";
                default: return "UNDER ATTACK";
            }
        }

        void Update()
        {
            if (spawner == null) spawner = FindAnyObjectByType<ScenarioSpawner>();
            if (spawner == null) return;
            if (spawner.RunNumber != run)
            {
                run = spawner.RunNumber;
                var cfg = spawner.TrainerConfig;
                startCard = cfg != null ? cfg.roeAtStart : card;
                doFlip = cfg != null ? cfg.roeFlip : flipEnabled;
                flipAt = cfg != null ? cfg.roeFlipTime : flipTime;
                flipCard = cfg != null ? cfg.roeFlipTo : flipTo;
                Current = ContactRegistry.CurrentRoe = startCard;
                ContactRegistry.RoeSince = 0f;
                flippedAt = -1f;
            }
            if (doFlip && flippedAt < 0f && spawner.ScenarioTime >= flipAt)
            {
                flippedAt = spawner.ScenarioTime;
                Current = ContactRegistry.CurrentRoe = flipCard;
                ContactRegistry.RoeSince = flippedAt;
                ContactRegistry.Note("ROE changed to " + Label(Current));
            }
        }

        void OnGUI()
        {
            if (PlayerView.HideOverlays) return;
            GUI.BeginGroup(PlayerView.Rect);
            DrawGui();
            GUI.EndGroup();
        }

        void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || spawner == null) return;
            float ui = PlayerView.Height / 720f;
            if (style == null) style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, fontStyle = FontStyle.Bold };
            style.normal.textColor = Color.white;

            bool flashing = flippedAt >= 0f && spawner.ScenarioTime - flippedAt < flashSeconds && Mathf.Repeat(Time.unscaledTime * 4f, 1f) < 0.5f;
            var back = flashing ? new Color(0.8f, 0.1f, 0.1f, 0.85f) : new Color(0f, 0f, 0f, 0.55f);

            style.fontSize = Mathf.RoundToInt(bannerFontSize * ui);
            Panel("ROE: " + Label(Current), 0.5f, back, ui);

            float remaining = Mathf.Max(0f, spawner.Duration - spawner.ScenarioTime);
            style.fontSize = Mathf.RoundToInt(timerFontSize * ui);
            Panel($"{(int)remaining / 60:00}:{(int)remaining % 60:00}", 1f, new Color(0f, 0f, 0f, 0.55f), ui);
        }

        // anchor: 0.5 = centred on screen, 1 = against the right edge.
        void Panel(string text, float anchor, Color back, float ui)
        {
            var content = new GUIContent(text);
            Vector2 size = style.CalcSize(content);
            float pad = 8f * ui;
            float w = size.x + pad * 2f, h = size.y + pad;
            float x = anchor >= 1f ? PlayerView.Width - w - pad : (PlayerView.Width - w) * anchor;
            var box = new Rect(x, pad, w, h);
            var old = GUI.color;
            GUI.color = back;
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;
            GUI.Label(box, content, style);
        }
    }
}
