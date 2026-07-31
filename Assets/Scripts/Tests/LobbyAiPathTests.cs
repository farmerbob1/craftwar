using System.Collections.Generic;
using NUnit.Framework;
using Craftwar.App;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Pud;

namespace Craftwar.Sim.Tests
{
    /// <summary>
    /// AiMatchTests exercises the AI only via GameSim.Setup's 2-arg map-defaults
    /// path. This covers the path an actual lobby/LAN/online match takes: a
    /// MatchConfig built the way MainMenuController.Lan.ToMatchConfig() builds
    /// it, projected through MatchConfig.ToMatchSetup() into the 3-arg Setup,
    /// then an AiPlayer constructed with GameLoopRunner.CreateAis()'s exact
    /// formula (slot.aiType / AiProfileLibrary.Resolve(slot.aiStrategy) /
    /// slot.aiTier) instead of the AiBehavior.LandAttack shortcut the other AI
    /// tests use.
    /// </summary>
    public class LobbyAiPathTests
    {
        const int Budget = 80000; // ~26 sim-minutes at 50 Hz, matches AiMatchTests

        [Test]
        public void LobbyBuiltComputerSeat_ActsAndDefeatsAnIdleHuman()
        {
            var pud = AiTestHarness.TwoBaseMap(); // slot 0 Computer, slot 1 Computer by default
            pud.Owner[0] = (byte)PudOwner.Human;   // slot 0: an idle human who never acts

            // Mirrors ToMatchConfig(): controller/race/team set from the lobby,
            // aiType/aiStrategy left at SlotConfig's own field defaults (0 / "").
            var config = new MatchConfig { slots = new SlotConfig[SimConstants.MaxPlayers] };
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                config.slots[p] = new SlotConfig { controller = Controller.None, race = Race.Human, team = (byte)p };
            config.slots[0] = new SlotConfig { controller = Controller.Human, race = Race.Human, team = 0 };
            config.slots[1] = new SlotConfig { controller = Controller.Computer, race = Race.Orc, team = 1, aiTier = (byte)AiTier.Normal };

            var setup = config.ToMatchSetup();
            Assert.IsTrue(setup.HasValue);
            var sim = new GameSim(21);
            sim.Setup(pud, RuleSet.CreateDefault(), setup.Value);

            Assert.AreEqual(Controller.Computer, sim.State.Players[1].Controller);
            Assert.IsTrue(sim.State.Players[1].InGame);

            // GameLoopRunner.CreateAis()'s exact per-slot construction.
            var ais = new List<AiPlayer>();
            for (byte p = 0; p < SimConstants.MaxPlayers; p++)
            {
                ref PlayerState ps = ref sim.State.Players[p];
                if (ps.Controller != Controller.Computer || !ps.InGame) continue;
                var slot = config.slots[p];
                var profile = AiProfileLibrary.Resolve(slot.aiStrategy);
                var tier = (AiTier)slot.aiTier;
                ais.Add(new AiPlayer(p, AiBehaviorMap.FromAiplByte(slot.aiType), profile, tier));
            }
            Assert.AreEqual(1, ais.Count, "only slot 1 is a computer");

            static bool Resolved(GameSim s) =>
                s.State.Players[0].Outcome != PlayerOutcome.Playing
                || s.State.Players[1].Outcome != PlayerOutcome.Playing;

            int ticks = AiTestHarness.RunAiMatch(sim, ais, Budget, stop: Resolved);

            Assert.Less(ticks, Budget, "the AI must finish off an idle human via the lobby-built path");
            Assert.AreEqual(PlayerOutcome.Defeated, sim.State.Players[0].Outcome);
            Assert.AreEqual(PlayerOutcome.Victorious, sim.State.Players[1].Outcome);
        }

        [Test]
        public void ToMatchConfig_StyleAiType_ResolvesToLandAttack_NotPassive()
        {
            // AiBehaviorMap.FromAiplByte(0) must be LandAttack: the one value
            // that would silently disable the AI is 0x01, and the SlotConfig
            // field default is 0, not 1.
            Assert.AreEqual(AiBehavior.LandAttack, AiBehaviorMap.FromAiplByte(0));
        }
    }
}
