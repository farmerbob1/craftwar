using System.Collections.Concurrent;

namespace Craftwar.NetServer.Transport
{
    /// <summary>
    /// Live connections by ACCOUNT id, independent of room membership —
    /// what channels/whispers/friends/clans all need and
    /// <see cref="ConnectionRegistry"/> (keyed by connectionId) does not
    /// provide on its own. An account is "online" iff it has an entry here.
    /// (Phase 3 adds a PresenceStatus — Online vs InGame — alongside this;
    /// not needed until the friends list is the first consumer of it.)
    ///
    /// A connectionId is stored (not the <see cref="ClientConnection"/>
    /// itself) so callers still go through <see cref="ConnectionRegistry"/>
    /// to reach it — one source of truth for "is this connection still
    /// live", matching how the room-relay path already works.
    ///
    /// <see cref="Remove"/> is conditioned on the connectionId matching the
    /// currently-registered one: a short-lived control connection (e.g. a
    /// one-shot Login/ResumeSession call) and a long-lived social connection
    /// can both be registered for the same account in a brief overlapping
    /// window, and an unconditional remove-by-account in the short-lived
    /// connection's teardown would otherwise evict the persistent
    /// connection's own entry out from under it.
    /// </summary>
    public sealed class PresenceDirectory
    {
        readonly ConcurrentDictionary<long, string> _connectionByAccount = new();

        public void Add(long accountId, string connectionId) => _connectionByAccount[accountId] = connectionId;

        public void Remove(long accountId, string connectionId)
        {
            if (_connectionByAccount.TryGetValue(accountId, out string current) && current == connectionId)
                _connectionByAccount.TryRemove(accountId, out _);
        }

        public bool TryGetConnectionId(long accountId, out string connectionId) =>
            _connectionByAccount.TryGetValue(accountId, out connectionId);

        public bool IsOnline(long accountId) => _connectionByAccount.ContainsKey(accountId);
    }
}
