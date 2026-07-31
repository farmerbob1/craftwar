using System.Collections.Generic;
using Craftwar.Net;

namespace Craftwar.NetServer.Protocol
{
    public sealed class Room
    {
        public string Id;
        public string MapName = "";
        public string HostName = "";
        public string RoomName = "";
        public int MaxPlayers;

        /// <summary>Room-scoped peer id -> connection. The creator is always
        /// peer 0 — the elected host, by construction, exactly like
        /// LoopbackPacketPeer's "peer 0 hosts" convention. This is what lets
        /// RelayPeerSocket need no id-remap table: the server enforces the
        /// invariant instead of the client having to paper over it.</summary>
        public readonly Dictionary<int, string> Members = new();
        public int NextPeerId = 1;

        public int PlayerCount => Members.Count;

        /// <summary>Snapshot for a caller about to enumerate outside the
        /// manager's lock (e.g. to push frames to each member).</summary>
        public List<KeyValuePair<int, string>> MembersSnapshot() => new(Members);
    }

    /// <summary>
    /// Room lifecycle: create (creator becomes peer 0), join (next sequential
    /// peer id), list, leave. No socket knowledge — connections are named by
    /// an opaque connectionId the transport layer owns, so this is testable
    /// without a single TCP connection. Forwarding the actual relay bytes is
    /// the transport layer's job (it needs to reach into other connections'
    /// write streams, which this class has no business knowing about).
    ///
    /// Internally locked: connections run on independent async tasks and all
    /// call into the same manager instance, so every public method is safe
    /// to call concurrently. Room objects themselves are only ever mutated
    /// under that same lock — callers that need to enumerate a room's
    /// members outside it (to push network frames, which must not happen
    /// while holding a lock) take <see cref="Room.MembersSnapshot"/> first.
    /// </summary>
    public sealed class RoomManager
    {
        readonly object _lock = new();
        readonly Dictionary<string, Room> _rooms = new();
        readonly Dictionary<string, (string roomId, int peerId)> _memberOf = new();
        int _nextRoomId = 1;

        public Room CreateRoom(string creatorConnectionId, string mapName, string hostName, string roomName,
            int maxPlayers)
        {
            lock (_lock)
            {
                var room = new Room
                {
                    Id = (_nextRoomId++).ToString(),
                    MapName = mapName,
                    HostName = hostName,
                    RoomName = string.IsNullOrEmpty(roomName) ? $"{hostName}'s Game" : roomName,
                    MaxPlayers = maxPlayers < 1 ? 1 : maxPlayers,
                };
                room.Members[0] = creatorConnectionId;
                _rooms[room.Id] = room;
                _memberOf[creatorConnectionId] = (room.Id, 0);
                return room;
            }
        }

        public RoomJoinFailure TryJoinRoom(string roomId, string connectionId, out Room room, out int yourPeerId)
        {
            lock (_lock)
            {
                yourPeerId = -1;
                room = null;
                if (_memberOf.ContainsKey(connectionId))
                    return RoomJoinFailure.AlreadyInARoom;
                if (!_rooms.TryGetValue(roomId, out room))
                    return RoomJoinFailure.RoomNotFound;
                if (room.PlayerCount >= room.MaxPlayers)
                    return RoomJoinFailure.RoomFull;

                yourPeerId = room.NextPeerId++;
                room.Members[yourPeerId] = connectionId;
                _memberOf[connectionId] = (room.Id, yourPeerId);
                return RoomJoinFailure.None;
            }
        }

        /// <summary>A snapshot, safe to enumerate after this call returns —
        /// never a live view into the locked dictionary.</summary>
        public List<Room> ListRooms()
        {
            lock (_lock)
                return new List<Room>(_rooms.Values);
        }

        public bool TryGetMembership(string connectionId, out Room room, out int peerId)
        {
            lock (_lock)
            {
                room = null;
                peerId = -1;
                if (!_memberOf.TryGetValue(connectionId, out var m))
                    return false;
                if (!_rooms.TryGetValue(m.roomId, out room))
                    return false;
                peerId = m.peerId;
                return true;
            }
        }

        /// <summary>A connection dropped or explicitly left. Empty rooms are
        /// removed; a room the HOST (peer 0) left is removed too — there is
        /// no meaning to a room with no elected host, and the app-layer
        /// rejoin/substitution story lives inside the match's own turn
        /// protocol, not at the room-membership level. Returns the room and
        /// the departing peer's id (for notifying remaining members), or
        /// false if this connection was not in a room.</summary>
        public bool RemoveMember(string connectionId, out Room room, out int peerId)
        {
            lock (_lock)
            {
                room = null;
                peerId = -1;
                if (!_memberOf.TryGetValue(connectionId, out var m))
                    return false;
                _memberOf.Remove(connectionId);
                if (!_rooms.TryGetValue(m.roomId, out room))
                    return false;
                peerId = m.peerId;
                room.Members.Remove(m.peerId);
                if (m.peerId == 0 || room.Members.Count == 0)
                    _rooms.Remove(m.roomId);
                return true;
            }
        }
    }
}
