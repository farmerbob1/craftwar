using Craftwar.Sim.Pud;

namespace Craftwar.Sim
{
    /// <summary>Per-slot lobby configuration. Race comes from the PUD unless overridden.</summary>
    public struct SlotSetup
    {
        public Controller Controller;
        public Race Race;
        public byte Team;
    }

    /// <summary>
    /// The lobby's answer to "who is playing what". Separated from
    /// <see cref="PudFile"/> so a match-setup screen (M8) or a network lobby (M11)
    /// can override the map's defaults without the sim caring where it came from.
    ///
    /// <see cref="FromPud"/> reproduces exactly what GameSim.Setup used to derive
    /// inline, so the no-argument path is unchanged.
    /// </summary>
    public struct MatchSetup
    {
        public SlotSetup[] Slots; // length SimConstants.MaxPlayers

        /// <summary>
        /// Default setup from the map's OWNR/SIDE sections. Free-for-all: every
        /// slot is its own team, so a 1v1 map resolves without lobby input.
        /// </summary>
        public static MatchSetup FromPud(PudFile pud)
        {
            var slots = new SlotSetup[SimConstants.MaxPlayers];
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                slots[p] = new SlotSetup
                {
                    Controller = ControllerFor(pud.Owner[p]),
                    Race = pud.Side[p] <= 2 ? (Race)pud.Side[p] : Race.Neutral,
                    Team = (byte)p,
                };
            }
            return new MatchSetup { Slots = slots };
        }

        /// <summary>
        /// PUD owner byte to melee participant kind. Passive-computer and both
        /// rescue kinds are <see cref="Controller.None"/>: their units spawn (they
        /// are still <c>InGame</c>) but they are scenery, not opponents. Treating
        /// them as opponents is why victory must not key off InGame.
        /// </summary>
        public static Controller ControllerFor(byte owner) => owner switch
        {
            (byte)PudOwner.Human => Controller.Human,
            (byte)PudOwner.Computer => Controller.Computer,
            _ => Controller.None,
        };

        /// <summary>True for any owner byte whose units should spawn at all.</summary>
        public static bool IsInGame(byte owner) =>
            owner == (byte)PudOwner.Human || owner == (byte)PudOwner.Computer
            || owner == (byte)PudOwner.PassiveComputer || owner == (byte)PudOwner.RescuePassive
            || owner == (byte)PudOwner.RescueActive;
    }
}
