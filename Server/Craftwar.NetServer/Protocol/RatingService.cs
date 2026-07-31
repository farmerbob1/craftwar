using System.Collections.Generic;
using System.Text;
using Craftwar.NetServer.Db;

namespace Craftwar.NetServer.Protocol
{
    /// <summary>
    /// Applies Glicko-2 updates when a match ends and records it. The host
    /// reports the result once, trusted for v1 — there is no verification
    /// that the reporting host is telling the truth; that is a known,
    /// explicitly accepted gap (see the M11 plan), not an oversight.
    ///
    /// One shared model regardless of player count or team count: each
    /// player's single Glicko-2 result for the period is (average of every
    /// OTHER player in the match, 1.0 if their own team won else 0.0) — team
    /// victory in this game is already binary per player (PlayerOutcome.
    /// Victorious/Defeated), so this needs no separate FFA-vs-teams case.
    /// </summary>
    public sealed class RatingService
    {
        readonly AccountRepository _accounts;
        readonly RatingRepository _ratings;

        public RatingService(AccountRepository accounts, RatingRepository ratings)
        {
            _accounts = accounts;
            _ratings = ratings;
        }

        public readonly struct PlayerResult
        {
            public readonly string Username;
            public readonly bool Won;

            public PlayerResult(string username, bool won)
            {
                Username = username;
                Won = won;
            }
        }

        /// <summary>Read-only counterpart to ReportResult, for the lobby
        /// roster / room browser / player-inspect popup. Same trust level as
        /// ListRooms — callable on an anonymous connection, no auth check.
        /// An unregistered username simply has no rating, same "not rated"
        /// treatment ReportResult already gives a guest.</summary>
        public bool TryGetRating(string username, out GlickoRating rating, out int gamesPlayed)
        {
            if (!_accounts.TryGetByUsername(username, out var account))
            {
                rating = GlickoRating.Unrated;
                gamesPlayed = 0;
                return false;
            }
            (rating, gamesPlayed) = _ratings.GetOrDefault(account.Id);
            return true;
        }

        /// <summary>Unregistered/guest usernames are simply not rated — the
        /// match is still recorded in history with everyone who played, but
        /// only accounts that exist get a Glicko-2 update.</summary>
        public void ReportResult(string map, string mode, IReadOnlyList<PlayerResult> players)
        {
            var resolved = new List<(long accountId, string username, GlickoRating rating, int games, bool won)>();
            foreach (var p in players)
            {
                if (!_accounts.TryGetByUsername(p.Username, out var account))
                    continue;
                var (rating, games) = _ratings.GetOrDefault(account.Id);
                resolved.Add((account.Id, p.Username, rating, games, p.Won));
            }

            if (resolved.Count >= 2)
            {
                var updates = new List<(long accountId, GlickoRating rating, int games)>(resolved.Count);
                for (int i = 0; i < resolved.Count; i++)
                {
                    var opponents = new List<GlickoRating>(resolved.Count - 1);
                    for (int j = 0; j < resolved.Count; j++)
                        if (j != i)
                            opponents.Add(resolved[j].rating);
                    var opponentAverage = Glicko2.TeamAverage(opponents);
                    var result = new Glicko2.Result(opponentAverage, resolved[i].won ? 1.0 : 0.0);
                    var updated = Glicko2.Update(resolved[i].rating, new[] { result });
                    updates.Add((resolved[i].accountId, updated, resolved[i].games + 1));
                }
                foreach (var (accountId, rating, games) in updates)
                    _ratings.Save(accountId, rating, games);
            }

            _ratings.RecordMatch(map, mode, ToJson(players), ToResultJson(resolved));
        }

        static string ToJson(IReadOnlyList<PlayerResult> players)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < players.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(Escape(players[i].Username)).Append('"');
            }
            return sb.Append(']').ToString();
        }

        static string ToResultJson(List<(long accountId, string username, GlickoRating rating, int games, bool won)> resolved)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < resolved.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var r = resolved[i];
                sb.Append('{')
                  .Append("\"username\":\"").Append(Escape(r.username)).Append("\",")
                  .Append("\"won\":").Append(r.won ? "true" : "false")
                  .Append('}');
            }
            return sb.Append(']').ToString();
        }

        static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
