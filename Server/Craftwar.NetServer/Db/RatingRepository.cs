using System;
using Craftwar.NetServer.Protocol;
using Microsoft.Data.Sqlite;

namespace Craftwar.NetServer.Db
{
    public sealed class RatingRepository
    {
        readonly Database _db;

        public RatingRepository(Database db) => _db = db;

        /// <summary>Glickman's own recommended defaults (1500/350/0.06) for
        /// an account with no rated games yet — the `ratings` row seeded at
        /// registration (see AccountRepository.TryCreate) makes this the
        /// common path, not a fallback for a missing row.</summary>
        public (GlickoRating rating, int gamesPlayed) GetOrDefault(long accountId)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT rating, rd, volatility, games_played FROM ratings WHERE account_id = $id";
            cmd.Parameters.AddWithValue("$id", accountId);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
                return (GlickoRating.Unrated, 0);
            return (new GlickoRating(r.GetDouble(0), r.GetDouble(1), r.GetDouble(2)), r.GetInt32(3));
        }

        public void Save(long accountId, GlickoRating rating, int gamesPlayed)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ratings (account_id, rating, rd, volatility, games_played)
                VALUES ($id, $r, $rd, $v, $g)
                ON CONFLICT(account_id) DO UPDATE SET
                    rating = $r, rd = $rd, volatility = $v, games_played = $g;
                """;
            cmd.Parameters.AddWithValue("$id", accountId);
            cmd.Parameters.AddWithValue("$r", rating.Rating);
            cmd.Parameters.AddWithValue("$rd", rating.RD);
            cmd.Parameters.AddWithValue("$v", rating.Volatility);
            cmd.Parameters.AddWithValue("$g", gamesPlayed);
            cmd.ExecuteNonQuery();
        }

        public void RecordMatch(string map, string mode, string participantsJson, string resultJson)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO match_history (started_at, map, mode, participants, result)
                VALUES ($t, $map, $mode, $participants, $result);
                """;
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$map", map);
            cmd.Parameters.AddWithValue("$mode", mode);
            cmd.Parameters.AddWithValue("$participants", participantsJson);
            cmd.Parameters.AddWithValue("$result", resultJson);
            cmd.ExecuteNonQuery();
        }
    }
}
