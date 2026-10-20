namespace Kurukshetra.Scenario
{
    // Explicit values so serialized assets stay valid when classes are added.
    public enum ContactClass
    {
        HostileQuad = 0,
        HostileFixedWing = 1,
        FriendlyISR = 2,
        CivilianQuad = 3,
        Bird = 4,
        // Reserved for later: ManFlyingAircraft = 5, BirdMimic = 6
    }

    public enum FlightBehaviour
    {
        Approach,   // straight in toward the defended point
        Transit,    // straight line across the sector
        Loiter,     // fly in, then orbit a point inside the sector
        Erratic,    // bird: wandering heading, bobbing altitude
        // Added with the scenario options (flight pattern per type); set by the spawner, not by a contact definition.
        ZigZag,         // heading swings left and right of the line to the goal
        PopUp,          // low run-in, then a climb a few hundred metres out
        TerrainFollow,  // holds a set height above the ground all the way
    }

    public enum Silhouette
    {
        Quad,
        FixedWing,
        Bird,
    }

    // Small placeholder identification cue under/on the body, visible mainly through binoculars.
    public enum Cue
    {
        None,
        PayloadBox,     // box slung under the body
        GimbalBall,     // camera ball under the body
        AntennaStick,   // thin mast on top
    }

    // What the player can declare a contact to be (keys 1-5).
    public enum PlayerCall
    {
        None,
        HostileQuad,
        HostileFixedWing,
        OwnIsrDrone,
        CivilianDrone,
        Bird,
    }

    public enum RoeCard
    {
        WeaponsHold,
        WeaponsTight,
        WeaponsFree,
        UnderAttack,
        // Module 1 cards (solution doc §5.4.4). Placeholder wording until an officer replaces it.
        CardA,      // attack in progress
        CardB,      // alert: engage only positively identified hostile
        CardC,      // peacetime / own drones up: report and track, engage only in self-defence or on order
    }

    // Names shown on the banner, the tablet and the dashboard.
    public static class RoeCards
    {
        public static bool IsLettered(RoeCard c) => c == RoeCard.CardA || c == RoeCard.CardB || c == RoeCard.CardC;

        // Cards under which holding a hostile costs time (the hold-delay rule).
        public static bool IsAttack(RoeCard c) => c == RoeCard.UnderAttack || c == RoeCard.CardA;

        public static string Label(RoeCard c)
        {
            switch (c)
            {
                case RoeCard.WeaponsHold: return "WEAPONS HOLD";
                case RoeCard.WeaponsTight: return "WEAPONS TIGHT";
                case RoeCard.WeaponsFree: return "WEAPONS FREE";
                case RoeCard.UnderAttack: return "UNDER ATTACK";
                case RoeCard.CardA: return "CARD A - ATTACK IN PROGRESS";
                case RoeCard.CardB: return "CARD B - ENGAGE ONLY POSITIVELY IDENTIFIED HOSTILE";
                default: return "CARD C - OWN DRONES UP: REPORT AND TRACK";
            }
        }

        public static string Short(RoeCard c)
        {
            switch (c)
            {
                case RoeCard.WeaponsHold: return "HOLD";
                case RoeCard.WeaponsTight: return "TIGHT";
                case RoeCard.WeaponsFree: return "FREE";
                case RoeCard.UnderAttack: return "UNDER ATTACK";
                case RoeCard.CardA: return "CARD A";
                case RoeCard.CardB: return "CARD B";
                default: return "CARD C";
            }
        }

        // The order the trainer's button steps through.
        public static readonly RoeCard[] Cycle = { RoeCard.CardA, RoeCard.CardB, RoeCard.CardC, RoeCard.WeaponsHold, RoeCard.UnderAttack };
        public static RoeCard Next(RoeCard c) => Cycle[(System.Array.IndexOf(Cycle, c) + 1) % Cycle.Length];
    }

    // A recorded change of card during a run.
    public struct RoeChange
    {
        public float time;
        public RoeCard from, to;
    }

    // One final action per contact.
    public enum PlayerAction
    {
        None,
        Hold,
        Report,
        Jam,
        Engage,
    }

    public static class ContactClassExtensions
    {
        public static string Label(this PlayerCall c)
        {
            switch (c)
            {
                case PlayerCall.HostileQuad: return "Hostile quad";
                case PlayerCall.HostileFixedWing: return "Hostile fixed-wing";
                case PlayerCall.OwnIsrDrone: return "Own ISR drone";
                case PlayerCall.CivilianDrone: return "Civilian drone";
                case PlayerCall.Bird: return "Bird";
                default: return "none";
            }
        }
        public static bool IsHostile(this ContactClass c)
        {
            return c == ContactClass.HostileQuad || c == ContactClass.HostileFixedWing;
        }
    }
}
