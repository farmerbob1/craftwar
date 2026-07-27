using Microsoft.Data.Sqlite;

namespace Craftwar.NetServer.Db
{
    /// <summary>
    /// Schema + connection factory. One file, self-contained — matches the
    /// "standalone console app you host yourself" framing: no separate DB
    /// service to run, back up, or lose track of.
    /// </summary>
    public sealed class Database
    {
        public string ConnectionString { get; }

        public Database(string path)
        {
            ConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                ForeignKeys = true,
            }.ToString();
        }

        public SqliteConnection OpenConnection()
        {
            var conn = new SqliteConnection(ConnectionString);
            conn.Open();
            return conn;
        }

        /// <summary>Idempotent — safe to call on every startup.</summary>
        public void EnsureSchema()
        {
            using var conn = OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS accounts (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    username      TEXT NOT NULL UNIQUE,
                    password_hash TEXT NOT NULL,
                    created_at    TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS sessions (
                    token      TEXT PRIMARY KEY,
                    account_id INTEGER NOT NULL REFERENCES accounts(id),
                    created_at TEXT NOT NULL,
                    expires_at TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS idx_sessions_account ON sessions(account_id);

                CREATE TABLE IF NOT EXISTS ratings (
                    account_id   INTEGER PRIMARY KEY REFERENCES accounts(id),
                    rating       REAL NOT NULL DEFAULT 1500,
                    rd           REAL NOT NULL DEFAULT 350,
                    volatility   REAL NOT NULL DEFAULT 0.06,
                    games_played INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS match_history (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    started_at   TEXT NOT NULL,
                    map          TEXT NOT NULL,
                    mode         TEXT NOT NULL,
                    participants TEXT NOT NULL,
                    result       TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
        }
    }
}
