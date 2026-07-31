using NUnit.Framework;
using Craftwar.App;

namespace Craftwar.Sim.Tests
{
    /// <summary>LadderRank.TitleFor is pure C# (no UnityEngine dependency) —
    /// just the breakpoint table and the games-played floor.</summary>
    public class LadderRankTests
    {
        [Test]
        public void BelowTheGamesPlayedFloor_IsUnranked()
        {
            Assert.AreEqual("Unranked", LadderRank.TitleFor(2000, LadderRank.MinGamesForRank - 1));
        }

        [Test]
        public void AtOrAboveTheGamesPlayedFloor_ReturnsATierByRating()
        {
            Assert.AreEqual("Peasant", LadderRank.TitleFor(0, LadderRank.MinGamesForRank));
            Assert.AreEqual("Grunt", LadderRank.TitleFor(1200, LadderRank.MinGamesForRank));
            Assert.AreEqual("Knight", LadderRank.TitleFor(1400, LadderRank.MinGamesForRank));
            Assert.AreEqual("Champion", LadderRank.TitleFor(1600, LadderRank.MinGamesForRank));
            Assert.AreEqual("Warlord", LadderRank.TitleFor(1800, LadderRank.MinGamesForRank));
            Assert.AreEqual("Grand Marshal", LadderRank.TitleFor(2000, LadderRank.MinGamesForRank));
        }

        [Test]
        public void JustBelowABreakpoint_StaysInTheLowerTier()
        {
            Assert.AreEqual("Peasant", LadderRank.TitleFor(1199, LadderRank.MinGamesForRank));
            Assert.AreEqual("Grunt", LadderRank.TitleFor(1399, LadderRank.MinGamesForRank));
        }

        [Test]
        public void Label_ReportsUnrankedWhenTheRatingIsNotKnownAtAll()
        {
            Assert.AreEqual("Unranked", LadderRank.Label(known: false, rating: 1500, gamesPlayed: 50));
        }

        [Test]
        public void Label_IncludesTheNumberAndTierWhenKnown()
        {
            // 1500 falls in the 1400-1599 band -> Knight, per the breakpoint
            // table asserted directly in AtOrAboveTheGamesPlayedFloor_*.
            Assert.AreEqual("1500 Knight",
                LadderRank.Label(known: true, rating: 1500, gamesPlayed: LadderRank.MinGamesForRank));
        }
    }
}
