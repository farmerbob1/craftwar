using System;
using Craftwar.Sim;

namespace Craftwar.App
{
    /// <summary>One lobby seat. Mirrors <see cref="SlotSetup"/> plus presentation-only colour.</summary>
    [Serializable]
    public sealed class SlotConfig
    {
        public Controller controller = Controller.None;
        public Race race = Race.Human;
        public byte team;
        public byte colour;

        /// <summary>PUD AIPL byte for Computer slots (0 = land attack, 1 =
        /// passive). App-side data consumed by GameLoopRunner when it creates
        /// the AIs; the sim never sees it.</summary>
        public byte aiType;

        /// <summary>AI profile name for a Computer slot — a built-in
        /// (<see cref="Craftwar.Sim.Ai.BuiltinAiProfiles"/>) or a discovered modder
        /// file. Empty = the default land-attack. App-side; resolved by
        /// GameLoopRunner into the AiProfile handed to the AiPlayer.</summary>
        public string aiStrategy = "";

        /// <summary>Difficulty tier (<see cref="Craftwar.Sim.Ai.AiTier"/>) for a
        /// Computer slot. Its SKILL half drives the out-of-sim AiPlayer; its
        /// HANDICAP half is baked into the slot's hashed PlayerState by
        /// <see cref="MatchConfig.ToMatchSetup"/>.</summary>
        public byte aiTier = (byte)Craftwar.Sim.Ai.AiTier.Normal;
    }

    /// <summary>
    /// Everything needed to start a match, in one place. Before this existed the
    /// seed was a literal written twice in GameBootstrap (once for the sim, once
    /// for the replay header) and the local player was a compile-time const in
    /// two view classes, so there was nowhere for a lobby to put its answers.
    ///
    /// Plain [Serializable] so it round-trips through JsonUtility for
    /// "resume last settings", and so M10's lobby can put it on the wire.
    /// </summary>
    [Serializable]
    public sealed class MatchConfig
    {
        /// <summary>Resolved exactly like GameBootstrap.mapOverridePath: empty =
        /// LocalAssetPaths default, bare name = StreamingAssets/Maps, otherwise literal.</summary>
        public string mapPath = "";

        public ulong seed = 42;

        /// <summary>Which slot the local client drives. Must be a slot with a Human controller.</summary>
        public byte localSlot;

        /// <summary>Length SimConstants.MaxPlayers, or null to take the map's own OWNR/SIDE.</summary>
        public SlotConfig[] slots;

        /// <summary>The map's defaults, unmodified — what pressing Play has always done.</summary>
        public static MatchConfig FromMapDefaults(string mapPath, ulong seed = 42) =>
            new MatchConfig { mapPath = mapPath, seed = seed, localSlot = 0, slots = null };

        /// <summary>
        /// Project to the sim's view of the lobby. Null <see cref="slots"/> means
        /// "use the PUD", which keeps GameBootstrap's historical behaviour exact.
        /// </summary>
        public MatchSetup? ToMatchSetup()
        {
            if (slots == null)
                return null;

            var setup = new MatchSetup { Slots = new SlotSetup[SimConstants.MaxPlayers] };
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                var s = p < slots.Length ? slots[p] : null;
                if (s == null)
                {
                    setup.Slots[p] = new SlotSetup
                    {
                        Controller = Controller.None, Race = Race.Human, Team = (byte)p,
                    };
                    continue;
                }
                var slot = new SlotSetup { Controller = s.controller, Race = s.race, Team = s.team };
                // Bake the tier's handicap knobs into the hashed slot — only for
                // Computer slots (a human never gets the AI's cheats).
                if (s.controller == Controller.Computer)
                {
                    var tp = Craftwar.Sim.Ai.AiTierTable.For((Craftwar.Sim.Ai.AiTier)s.aiTier);
                    slot.StartGoldBonus = tp.StartGoldBonus;
                    slot.StartLumberBonus = tp.StartLumberBonus;
                    slot.HarvestBonusTenths = tp.HarvestBonusTenths;
                    slot.SightBonus = tp.SightBonus;
                }
                setup.Slots[p] = slot;
            }
            return setup;
        }
    }

    /// <summary>
    /// The handoff between the menu scene and the game scene. A static because
    /// scene loads destroy everything else, and because GameBootstrap must be
    /// able to run with nothing set — pressing Play directly on Game.unity is
    /// the dev loop and has to keep working.
    /// </summary>
    public static class MatchSession
    {
        /// <summary>Consumed (and cleared) by GameBootstrap on load. Null = use the inspector fields.</summary>
        public static MatchConfig Pending;

        /// <summary>The config the running match was actually started from —
        /// what Restart replays and what the victory screen reports.</summary>
        public static MatchConfig Current { get; private set; }

        public static MatchConfig Take()
        {
            var cfg = Pending;
            Pending = null;
            if (cfg != null)
                Current = cfg;
            return cfg;
        }

        public static void SetCurrent(MatchConfig cfg) => Current = cfg;
    }
}
