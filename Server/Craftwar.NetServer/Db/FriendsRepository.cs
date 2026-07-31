using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace Craftwar.NetServer.Db
{
    /// <summary>Plain data access for friend requests and accepted
    /// friendships. A friendship is stored as two rows (one per direction)
    /// so "list my friends" is a single indexed lookup, not a UNION query.</summary>
    public sealed class FriendsRepository
    {
        readonly Database _db;

        public FriendsRepository(Database db) => _db = db;

        public void AddRequest(long fromAccountId, long toAccountId)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO friend_requests (from_account_id, to_account_id, created_at)
                VALUES ($f, $t, $c)
                ON CONFLICT(from_account_id, to_account_id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$f", fromAccountId);
            cmd.Parameters.AddWithValue("$t", toAccountId);
            cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        public void RemoveRequest(long fromAccountId, long toAccountId)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM friend_requests WHERE from_account_id = $f AND to_account_id = $t";
            cmd.Parameters.AddWithValue("$f", fromAccountId);
            cmd.Parameters.AddWithValue("$t", toAccountId);
            cmd.ExecuteNonQuery();
        }

        public bool HasRequest(long fromAccountId, long toAccountId)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM friend_requests WHERE from_account_id = $f AND to_account_id = $t";
            cmd.Parameters.AddWithValue("$f", fromAccountId);
            cmd.Parameters.AddWithValue("$t", toAccountId);
            using var r = cmd.ExecuteReader();
            return r.Read();
        }

        /// <summary>Both directions in one call — a friendship is symmetric
        /// by construction, never just one row.</summary>
        public void AddFriendship(long accountIdA, long accountIdB)
        {
            using var conn = _db.OpenConnection();
            InsertFriendshipRow(conn, accountIdA, accountIdB);
            InsertFriendshipRow(conn, accountIdB, accountIdA);
        }

        static void InsertFriendshipRow(SqliteConnection conn, long accountId, long friendAccountId)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO friendships (account_id, friend_account_id, created_at)
                VALUES ($a, $b, $c)
                ON CONFLICT(account_id, friend_account_id) DO NOTHING;
                """;
            cmd.Parameters.AddWithValue("$a", accountId);
            cmd.Parameters.AddWithValue("$b", friendAccountId);
            cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        public void RemoveFriendship(long accountIdA, long accountIdB)
        {
            using var conn = _db.OpenConnection();
            DeleteFriendshipRow(conn, accountIdA, accountIdB);
            DeleteFriendshipRow(conn, accountIdB, accountIdA);
        }

        static void DeleteFriendshipRow(SqliteConnection conn, long accountId, long friendAccountId)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM friendships WHERE account_id = $a AND friend_account_id = $b";
            cmd.Parameters.AddWithValue("$a", accountId);
            cmd.Parameters.AddWithValue("$b", friendAccountId);
            cmd.ExecuteNonQuery();
        }

        public bool AreFriends(long accountIdA, long accountIdB)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM friendships WHERE account_id = $a AND friend_account_id = $b";
            cmd.Parameters.AddWithValue("$a", accountIdA);
            cmd.Parameters.AddWithValue("$b", accountIdB);
            using var r = cmd.ExecuteReader();
            return r.Read();
        }

        public List<(long accountId, string username)> ListFriends(long accountId)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT f.friend_account_id, a.username
                FROM friendships f JOIN accounts a ON a.id = f.friend_account_id
                WHERE f.account_id = $a
                ORDER BY a.username;
                """;
            cmd.Parameters.AddWithValue("$a", accountId);
            using var r = cmd.ExecuteReader();
            var list = new List<(long, string)>();
            while (r.Read())
                list.Add((r.GetInt64(0), r.GetString(1)));
            return list;
        }

        /// <summary>Requests sent TO accountId, still pending.</summary>
        public List<string> ListIncomingUsernames(long accountId)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT a.username FROM friend_requests r JOIN accounts a ON a.id = r.from_account_id
                WHERE r.to_account_id = $a ORDER BY a.username;
                """;
            cmd.Parameters.AddWithValue("$a", accountId);
            using var r = cmd.ExecuteReader();
            var list = new List<string>();
            while (r.Read())
                list.Add(r.GetString(0));
            return list;
        }

        /// <summary>Requests accountId sent, still awaiting a response.</summary>
        public List<string> ListOutgoingUsernames(long accountId)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT a.username FROM friend_requests r JOIN accounts a ON a.id = r.to_account_id
                WHERE r.from_account_id = $a ORDER BY a.username;
                """;
            cmd.Parameters.AddWithValue("$a", accountId);
            using var r = cmd.ExecuteReader();
            var list = new List<string>();
            while (r.Read())
                list.Add(r.GetString(0));
            return list;
        }
    }
}
