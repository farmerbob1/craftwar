using System;

namespace Craftwar.NetServer.Db
{
    /// <summary>
    /// Persisted per-channel MOTD, keyed by the channel's lowercase name.
    /// Channels themselves stay ephemeral (no table for membership/history —
    /// see ChannelManager's own doc comment), but a "message of the day"
    /// that vanishes the moment the channel happens to empty out defeats the
    /// entire point of a MOTD, so this one property of a channel survives
    /// independently of the in-memory ChatChannel's lifecycle.
    /// </summary>
    public sealed class ChannelMotdRepository
    {
        readonly Database _db;

        public ChannelMotdRepository(Database db) => _db = db;

        public string GetOrDefault(string channelKey)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT motd FROM channel_motd WHERE channel_key = $k";
            cmd.Parameters.AddWithValue("$k", channelKey);
            return cmd.ExecuteScalar() as string ?? "";
        }

        public void Save(string channelKey, string motd)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO channel_motd (channel_key, motd, updated_at)
                VALUES ($k, $m, $t)
                ON CONFLICT(channel_key) DO UPDATE SET motd = $m, updated_at = $t;
                """;
            cmd.Parameters.AddWithValue("$k", channelKey);
            cmd.Parameters.AddWithValue("$m", motd ?? "");
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            cmd.ExecuteNonQuery();
        }
    }
}
