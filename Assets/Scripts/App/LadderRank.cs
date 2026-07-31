namespace Craftwar.App
{
    /// <summary>
    /// Rank-tier labels for the online Glicko-2 ladder — race-agnostic
    /// (rating is per-account, not per-race) and named distinctly from
    /// VictoryScreen's HumanRanks/OrcRanks: that is an unrelated, cosmetic,
    /// per-race end-of-match title derived from score, not this ladder.
    /// Breakpoints and wording are invented (no real WC2 Battle.net ladder
    /// document to source them from) and are cheap to retune later.
    /// </summary>
    public static class LadderRank
    {
        /// <summary>Below this many rated games, RD is still wide enough
        /// (Glicko-2 seeds every account at 1500/RD 350 — see Glicko2.cs)
        /// that the rating isn't a confident placement yet, so it is shown
        /// as Unranked instead of a tier it may not deserve.</summary>
        public const int MinGamesForRank = 5;

        static readonly (int minRating, string title)[] Tiers =
        {
            (0, "Peasant"),
            (1200, "Grunt"),
            (1400, "Knight"),
            (1600, "Champion"),
            (1800, "Warlord"),
            (2000, "Grand Marshal"),
        };

        public static string TitleFor(int rating, int gamesPlayed)
        {
            if (gamesPlayed < MinGamesForRank)
                return "Unranked";
            string title = Tiers[0].title;
            for (int i = 0; i < Tiers.Length; i++)
                if (rating >= Tiers[i].minRating)
                    title = Tiers[i].title;
            return title;
        }

        /// <summary>"1500 Knight" for a known, ranked account; "Unranked" for
        /// an unregistered host, a LAN game (no server to ask), or a
        /// registered account under the games-played floor.</summary>
        public static string Label(bool known, int rating, int gamesPlayed) =>
            known ? $"{rating} {TitleFor(rating, gamesPlayed)}" : "Unranked";
    }
}
