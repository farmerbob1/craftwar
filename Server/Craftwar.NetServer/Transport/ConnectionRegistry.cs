using System.Collections.Concurrent;

namespace Craftwar.NetServer.Transport
{
    /// <summary>
    /// Live connections by id, so the room-relay path can reach into a
    /// DIFFERENT connection's outgoing stream than the one currently handling
    /// a message — a relay send's whole point. Concurrent because connections
    /// run on independent async tasks.
    /// </summary>
    public sealed class ConnectionRegistry
    {
        readonly ConcurrentDictionary<string, ClientConnection> _byId = new();

        public void Add(ClientConnection conn) => _byId[conn.ConnectionId] = conn;
        public void Remove(ClientConnection conn) => _byId.TryRemove(conn.ConnectionId, out _);

        public bool TryGet(string connectionId, out ClientConnection conn) =>
            _byId.TryGetValue(connectionId, out conn);
    }
}
