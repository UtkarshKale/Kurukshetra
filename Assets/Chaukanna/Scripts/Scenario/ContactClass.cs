namespace Chaukanna.Scenario
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
