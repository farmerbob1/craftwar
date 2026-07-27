using System.Collections.Generic;
using Craftwar.NetServer.Protocol;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    /// <summary>
    /// Correctness proof: Glickman's own worked example from the Glicko-2
    /// spec (glicko.net/glicko/glicko2.pdf, "Example of the Glicko-2
    /// system") — a player rated 1500/200/0.06 who plays three games in one
    /// period must land within a hair of his published results
    /// (1464.06 / 151.52 / 0.05999). Checked to 2 decimal places on rating/RD
    /// and 5 on volatility, matching the precision the paper itself reports.
    /// </summary>
    public class Glicko2Tests
    {
        [Test]
        public void GlickmansWorkedExample_MatchesThePublishedResult()
        {
            var player = new GlickoRating(1500, 200, 0.06);
            var results = new List<Glicko2.Result>
            {
                new(new GlickoRating(1400, 30, 0.06), 1.0),   // win
                new(new GlickoRating(1550, 100, 0.06), 0.0),  // loss
                new(new GlickoRating(1700, 300, 0.06), 0.0),  // loss
            };

            var updated = Glicko2.Update(player, results);

            Assert.AreEqual(1464.06, updated.Rating, 0.01);
            Assert.AreEqual(151.52, updated.RD, 0.01);
            Assert.AreEqual(0.05999, updated.Volatility, 0.00001);
        }

        [Test]
        public void WinningRaisesRating_LosingLowersIt()
        {
            var player = new GlickoRating(1500, 100, 0.06);
            var opponent = new GlickoRating(1500, 100, 0.06);

            var afterWin = Glicko2.Update(player, new List<Glicko2.Result> { new(opponent, 1.0) });
            var afterLoss = Glicko2.Update(player, new List<Glicko2.Result> { new(opponent, 0.0) });

            Assert.Greater(afterWin.Rating, 1500);
            Assert.Less(afterLoss.Rating, 1500);
        }

        [Test]
        public void ADraw_AgainstAnEqualOpponent_LeavesRatingUnchanged()
        {
            var player = new GlickoRating(1500, 100, 0.06);
            var opponent = new GlickoRating(1500, 100, 0.06);

            var updated = Glicko2.Update(player, new List<Glicko2.Result> { new(opponent, 0.5) });

            Assert.AreEqual(1500, updated.Rating, 0.001);
        }

        [Test]
        public void NoGamesThisPeriod_WidensDeviationButNotRating()
        {
            var player = new GlickoRating(1500, 100, 0.06);
            var updated = Glicko2.Update(player, new List<Glicko2.Result>());

            Assert.AreEqual(1500, updated.Rating, 0.0001);
            Assert.Greater(updated.RD, 100, "uncertainty must grow with inactivity");
        }

        [Test]
        public void BeatingAWeakerOpponent_GainsLessThanBeatingAStrongerOne()
        {
            var player = new GlickoRating(1500, 100, 0.06);
            var weaker = new GlickoRating(1300, 100, 0.06);
            var stronger = new GlickoRating(1700, 100, 0.06);

            var gainVsWeaker = Glicko2.Update(player, new List<Glicko2.Result> { new(weaker, 1.0) }).Rating - 1500;
            var gainVsStronger = Glicko2.Update(player, new List<Glicko2.Result> { new(stronger, 1.0) }).Rating - 1500;

            Assert.Greater(gainVsStronger, gainVsWeaker);
        }

        [Test]
        public void TeamAverage_IsThePlainMeanOfRatingAndRD()
        {
            var team = new List<GlickoRating>
            {
                new(1400, 80, 0.06),
                new(1600, 120, 0.06),
            };
            var avg = Glicko2.TeamAverage(team);
            Assert.AreEqual(1500, avg.Rating, 0.0001);
            Assert.AreEqual(100, avg.RD, 0.0001);
        }

        [Test]
        public void ALowRdPlayer_MovesLessThanAHighRdPlayer_ForTheSameResult()
        {
            // The whole point of carrying RD: an established (low-RD) player's
            // rating should be sturdier against a single upset than a
            // provisional (high-RD) player's.
            var established = new GlickoRating(1500, 50, 0.06);
            var provisional = new GlickoRating(1500, 300, 0.06);
            var opponent = new GlickoRating(1500, 100, 0.06);

            var establishedLoss = Glicko2.Update(established, new List<Glicko2.Result> { new(opponent, 0.0) });
            var provisionalLoss = Glicko2.Update(provisional, new List<Glicko2.Result> { new(opponent, 0.0) });

            Assert.Less(1500 - establishedLoss.Rating, 1500 - provisionalLoss.Rating);
        }
    }
}
