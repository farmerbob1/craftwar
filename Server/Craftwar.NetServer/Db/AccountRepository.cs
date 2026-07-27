using System;
using Microsoft.Data.Sqlite;

namespace Craftwar.NetServer.Db
{
    public readonly struct Account
    {
        public readonly long Id;
        public readonly string Username;
        public readonly string PasswordHash;

        public Account(long id, string username, string passwordHash)
        {
            Id = id;
            Username = username;
            PasswordHash = passwordHash;
        }
    }

    /// <summary>Plain data access — no password verification or session
    /// policy here, that's <see cref="Protocol.AccountService"/>'s job. Kept
    /// separate so the business logic is testable without a real SQLite file
    /// standing in for every case (it still needs one for the persistence
    /// tests, but not for e.g. "wrong password" cases).</summary>
    public sealed class AccountRepository
    {
        readonly Database _db;

        public AccountRepository(Database db) => _db = db;

        public bool TryGetByUsername(string username, out Account account)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, username, password_hash FROM accounts WHERE username = $u";
            cmd.Parameters.AddWithValue("$u", username);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                account = default;
                return false;
            }
            account = new Account(r.GetInt64(0), r.GetString(1), r.GetString(2));
            return true;
        }

        /// <summary>False if the username is already taken.</summary>
        public bool TryCreate(string username, string passwordHash, out long accountId)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO accounts (username, password_hash, created_at)
                VALUES ($u, $h, $c)
                ON CONFLICT(username) DO NOTHING
                RETURNING id;
                """;
            cmd.Parameters.AddWithValue("$u", username);
            cmd.Parameters.AddWithValue("$h", passwordHash);
            cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("O"));
            var result = cmd.ExecuteScalar();
            if (result == null)
            {
                accountId = 0;
                return false;
            }
            accountId = (long)result;

            using var seedRating = conn.CreateCommand();
            seedRating.CommandText =
                "INSERT INTO ratings (account_id) VALUES ($id) ON CONFLICT(account_id) DO NOTHING";
            seedRating.Parameters.AddWithValue("$id", accountId);
            seedRating.ExecuteNonQuery();
            return true;
        }

        public void CreateSession(string token, long accountId, DateTime expiresAtUtc)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sessions (token, account_id, created_at, expires_at)
                VALUES ($t, $id, $c, $e);
                """;
            cmd.Parameters.AddWithValue("$t", token);
            cmd.Parameters.AddWithValue("$id", accountId);
            cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$e", expiresAtUtc.ToString("O"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>False if the token does not exist or has expired —
        /// callers do not need to distinguish which.</summary>
        public bool TryResumeSession(string token, out long accountId, out string username)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT a.id, a.username, s.expires_at
                FROM sessions s JOIN accounts a ON a.id = s.account_id
                WHERE s.token = $t;
                """;
            cmd.Parameters.AddWithValue("$t", token);
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                accountId = 0;
                username = null;
                return false;
            }
            long id = r.GetInt64(0);
            string uname = r.GetString(1);
            var expiresAt = DateTime.Parse(r.GetString(2)).ToUniversalTime();
            if (expiresAt <= DateTime.UtcNow)
            {
                accountId = 0;
                username = null;
                return false;
            }
            accountId = id;
            username = uname;
            return true;
        }
    }
}
