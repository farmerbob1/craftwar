using System.Collections.Generic;
using Craftwar.NetServer.Db;

namespace Craftwar.NetServer.Protocol
{
    /// <summary>One ephemeral chat channel: exists from its first member's
    /// join until its last member's leave (see ChannelManager.LeaveInternal),
    /// no DB table, no persisted history. The operator (kick rights) is
    /// always whoever has been a member the longest — tracked via the
    /// explicit <see cref="JoinOrder"/> list rather than read off Dictionary
    /// iteration order, so op migration on the current op's departure is
    /// deterministic.</summary>
    public sealed class ChatChannel
    {
        public string Name;
        /// <summary>Set by the op only (ChannelManager.TrySetMotd). UNLIKE
        /// the rest of this class, this one field is backed by
        /// ChannelMotdRepository when ChannelManager is given one — a MOTD
        /// that vanished every time a channel happened to empty out would
        /// defeat the entire point of a "message of the day".</summary>
        public string Motd = "";
        public readonly Dictionary<long, string> Members = new();
        public readonly List<long> JoinOrder = new();

        public long OpAccountId => JoinOrder.Count > 0 ? JoinOrder[0] : 0;
        public string OpUsername => Members.TryGetValue(OpAccountId, out string name) ? name : "";

        /// <summary>Join-order snapshot, safe to enumerate after the manager's
        /// lock is released — same reasoning as Room.MembersSnapshot() in
        /// RoomManager.cs.</summary>
        public List<(long accountId, string username)> MembersSnapshot()
        {
            var list = new List<(long, string)>(JoinOrder.Count);
            foreach (long accountId in JoinOrder)
                list.Add((accountId, Members[accountId]));
            return list;
        }

        public string[] MemberUsernamesSnapshot()
        {
            var names = new string[JoinOrder.Count];
            for (int i = 0; i < JoinOrder.Count; i++)
                names[i] = Members[JoinOrder[i]];
            return names;
        }
    }

    /// <summary>
    /// Ephemeral chat channels, one per account at a time (joining a new
    /// channel leaves whatever one you were already in) — matches the
    /// original Battle.net model. No socket knowledge — connections are
    /// named by accountId, the transport layer (ClientConnection) owns
    /// turning that into an actual frame push, same split RoomManager
    /// already uses for rooms.
    ///
    /// Internally locked for the same reason as RoomManager: connections run
    /// on independent async tasks and all call into the same instance.
    /// </summary>
    public sealed class ChannelManager
    {
        const int MinNameLength = 1;
        const int MaxNameLength = 32;

        readonly object _lock = new();
        readonly Dictionary<string, ChatChannel> _channels = new(); // keyed lowercase
        readonly Dictionary<long, string> _channelKeyOfAccount = new();
        /// <summary>Null in most existing unit tests (`new ChannelManager()`),
        /// which keeps MOTD in-memory-only for them — production wiring
        /// (RelayServerHost) always supplies a real one.</summary>
        readonly ChannelMotdRepository _motdRepo;

        public ChannelManager(ChannelMotdRepository motdRepo = null) => _motdRepo = motdRepo;

        public static bool IsValidName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.Length < MinNameLength || name.Length > MaxNameLength)
                return false;
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-' && c != ' ' && c != '/')
                    return false;
            return true;
        }

        /// <summary>Leaves whatever channel accountId was already in (if any —
        /// returned via <paramref name="previousChannel"/>, already updated
        /// to reflect the departure), then joins (creating if needed) the
        /// named channel. Caller must validate the name first via
        /// <see cref="IsValidName"/>.</summary>
        public ChatChannel Join(long accountId, string username, string channelName, out ChatChannel previousChannel)
        {
            lock (_lock)
            {
                previousChannel = LeaveInternal(accountId);

                string key = channelName.ToLowerInvariant();
                if (!_channels.TryGetValue(key, out var channel))
                {
                    channel = new ChatChannel { Name = channelName, Motd = _motdRepo?.GetOrDefault(key) ?? "" };
                    _channels[key] = channel;
                }
                channel.Members[accountId] = username;
                channel.JoinOrder.Add(accountId);
                _channelKeyOfAccount[accountId] = key;
                return channel;
            }
        }

        /// <summary>Disconnect/explicit-leave with no new channel to join.
        /// Returns the vacated channel (already updated), or null if the
        /// account was not in one.</summary>
        public ChatChannel Leave(long accountId)
        {
            lock (_lock)
                return LeaveInternal(accountId);
        }

        ChatChannel LeaveInternal(long accountId)
        {
            if (!_channelKeyOfAccount.TryGetValue(accountId, out string key))
                return null;
            _channelKeyOfAccount.Remove(accountId);
            if (!_channels.TryGetValue(key, out var channel))
                return null;
            channel.Members.Remove(accountId);
            channel.JoinOrder.Remove(accountId);
            if (channel.Members.Count == 0)
                _channels.Remove(key);
            return channel;
        }

        public bool TryGetChannelOf(long accountId, out ChatChannel channel)
        {
            lock (_lock)
            {
                channel = null;
                return _channelKeyOfAccount.TryGetValue(accountId, out string key)
                    && _channels.TryGetValue(key, out channel);
            }
        }

        /// <summary>Null on success. On failure, a plain-English reason
        /// (this project's HelloAck/AccountResult convention for
        /// non-enum-worthy failures) and out params are left default.</summary>
        public string TryKick(long kickerAccountId, string targetUsername, out ChatChannel channel,
            out long targetAccountId)
        {
            lock (_lock)
            {
                channel = null;
                targetAccountId = 0;
                if (!_channelKeyOfAccount.TryGetValue(kickerAccountId, out string key)
                    || !_channels.TryGetValue(key, out channel))
                    return "you are not in a channel";
                if (channel.OpAccountId != kickerAccountId)
                    return "only the channel operator can kick";

                targetAccountId = 0;
                foreach (var (accountId, username) in channel.Members)
                {
                    if (username == targetUsername)
                    {
                        targetAccountId = accountId;
                        break;
                    }
                }
                if (targetAccountId == 0)
                    return "that user is not in this channel";
                if (targetAccountId == kickerAccountId)
                    return "cannot kick yourself";

                channel.Members.Remove(targetAccountId);
                channel.JoinOrder.Remove(targetAccountId);
                _channelKeyOfAccount.Remove(targetAccountId);
                if (channel.Members.Count == 0)
                    _channels.Remove(key);
                return null;
            }
        }

        /// <summary>Null on success. Only the channel's own operator may set
        /// its message of the day.</summary>
        public string TrySetMotd(long accountId, string motd, out ChatChannel channel)
        {
            lock (_lock)
            {
                channel = null;
                if (!_channelKeyOfAccount.TryGetValue(accountId, out string key)
                    || !_channels.TryGetValue(key, out channel))
                    return "you are not in a channel";
                if (channel.OpAccountId != accountId)
                    return "only the channel operator can set the message of the day";
                channel.Motd = motd ?? "";
                _motdRepo?.Save(key, channel.Motd);
                return null;
            }
        }
    }
}
