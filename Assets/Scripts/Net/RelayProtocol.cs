using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// The relay server's own control-plane wire format — login/register/
    /// session resume, room create/join/list, and the opaque room-relay
    /// envelope. Deliberately separate from <see cref="NetMessageKind"/>:
    /// that enum is scoped to the peer-to-peer lobby/turn handshake (its
    /// ProtocolVersion is only ever compared host-vs-joiner), and conflating
    /// the two would force unrelated wire-format bumps together.
    ///
    /// Lives here, in Craftwar.Net, rather than in the server-only project,
    /// because BOTH sides need to speak it: <see cref="RelayPeerSocket"/>
    /// (this assembly) is a client of it, and Craftwar.NetServer (which
    /// compiles this whole folder by source, the same way the standalone Sim
    /// test harness does) is the other. Reuses <see cref="NetMessages"/>'
    /// WriteString/ReadString for the same one-little-endian-convention
    /// reason the rest of this file does.
    /// </summary>
    public enum ControlMessageKind : byte
    {
        None = 0,
        Hello,
        HelloAck,
        Register,
        RegisterResult,
        Login,
        LoginResult,
        ResumeSession,
        ResumeSessionResult,
        CreateRoom,
        CreateRoomResult,
        JoinRoom,
        JoinRoomResult,
        ListRooms,
        ListRoomsResult,
        /// <summary>Opaque passthrough — the server never parses the payload.
        /// Everything today's LAN transport carries (JoinRequest/TurnInput/
        /// TurnCommit/…) travels inside this envelope unchanged.</summary>
        RoomRelay,
        /// <summary>Server-pushed: a room-scoped peer connected or dropped.
        /// Mirrors IPacketPeer.TryDequeueConnectionEvent exactly — sent to
        /// existing members about a newcomer, and to the newcomer about
        /// everyone already there, the same reciprocal announcement
        /// LoopbackNetwork.CreatePeer does today.</summary>
        RoomPeerEvent,
        /// <summary>Client -> server: room-wide chat. The sender's display
        /// name travels with the text (self-reported, same trust level as
        /// LobbyClient's playerName already is) because room membership
        /// tracks connections, not display names.</summary>
        ChatMessage,
        /// <summary>Server -> every room member, INCLUDING the sender (so
        /// everyone's chat log is built from the same broadcast rather than
        /// the sender echoing its own line locally).</summary>
        ChatBroadcast,
        /// <summary>Host -> server, once, at match end: who played and who
        /// won. Trusted for v1 (see RatingService) — the server updates
        /// Glicko-2 ratings for every registered username in the report and
        /// records match history.</summary>
        ReportMatchResult,
        ReportMatchResultAck,
        /// <summary>Client -> server: join a named chat channel (created on
        /// first join if it doesn't exist yet — channels are ephemeral, see
        /// ChannelManager). Leaves whatever channel the connection was
        /// already in, if any — one channel at a time, matching the
        /// original Battle.net model. Requires the connection to already be
        /// account-bound (see ClientConnection's Login/ResumeSession
        /// handling) — sent nowhere near this on an anonymous connection.</summary>
        ChannelJoin,
        ChannelJoinResult,
        /// <summary>Server -> every member of a channel (including the
        /// mover, for a join): someone joined or left, plus who the
        /// channel's current operator is after the change — clients just
        /// overwrite their local "who can kick" state from this field every
        /// time rather than tracking migration themselves.</summary>
        ChannelMemberEvent,
        /// <summary>Client -> server: chat in whatever channel this
        /// connection is currently in. No sender field — unlike room chat,
        /// this connection is account-bound, so the server fills in the
        /// sender from that, not a self-reported string.</summary>
        ChannelChat,
        ChannelChatBroadcast,
        /// <summary>Client -> server: kick a member from the caller's
        /// current channel, by username. Refused unless the caller is that
        /// channel's operator.</summary>
        ChannelKick,
        ChannelKickResult,
        /// <summary>Server -> the kicked account only, distinct from the
        /// ChannelMemberEvent everyone else gets, so that client can show
        /// "you were kicked" rather than just an ordinary departure.</summary>
        ChannelKicked,
    }

    public enum AccountResult : byte
    {
        Ok = 0,
        UsernameTaken,
        InvalidUsername,
        WeakPassword,
        WrongCredentials,
        SessionExpired,
    }

    /// <summary>Why a JoinRoom request was refused, or None. Its own enum
    /// (rather than JoinRejectReason from the LAN lobby handshake) because
    /// room membership and lobby-seat negotiation are different layers —
    /// a room join only fails for room-level reasons (full, not found,
    /// already in one); LobbyHost's seat logic runs on top, unchanged,
    /// once the room-relay transport is up.</summary>
    public enum RoomJoinFailure : byte
    {
        None = 0,
        RoomNotFound,
        RoomFull,
        AlreadyInARoom,
    }

    /// <summary>One room as the browser/list sees it.</summary>
    public struct RoomSummary
    {
        public string RoomId;
        public string MapName;
        public string HostName;
        public int PlayerCount;
        public int MaxPlayers;
    }

    public static class ControlProtocol
    {
        /// <summary>Bump on any control-plane wire-format change.</summary>
        public const ushort CurrentVersion = 1;

        public static void WriteHello(ref ByteWriter w, ushort clientVersion)
        {
            w.WriteByte((byte)ControlMessageKind.Hello);
            w.WriteUShort(clientVersion);
        }

        public static ushort ReadHello(ref ByteReader r) => r.ReadUShort();

        public static void WriteHelloAck(ref ByteWriter w, bool accepted, string reason)
        {
            w.WriteByte((byte)ControlMessageKind.HelloAck);
            w.WriteByte((byte)(accepted ? 1 : 0));
            NetMessages.WriteString(ref w, reason ?? "");
        }

        public static void ReadHelloAck(ref ByteReader r, out bool accepted, out string reason)
        {
            accepted = r.ReadByte() != 0;
            reason = NetMessages.ReadString(ref r);
        }

        public static void WriteRegister(ref ByteWriter w, string username, string password)
        {
            w.WriteByte((byte)ControlMessageKind.Register);
            NetMessages.WriteString(ref w, username);
            NetMessages.WriteString(ref w, password);
        }

        public static void ReadRegister(ref ByteReader r, out string username, out string password)
        {
            username = NetMessages.ReadString(ref r);
            password = NetMessages.ReadString(ref r);
        }

        public static void WriteRegisterResult(ref ByteWriter w, AccountResult result)
        {
            w.WriteByte((byte)ControlMessageKind.RegisterResult);
            w.WriteByte((byte)result);
        }

        public static AccountResult ReadRegisterResult(ref ByteReader r) => (AccountResult)r.ReadByte();

        public static void WriteLogin(ref ByteWriter w, string username, string password)
        {
            w.WriteByte((byte)ControlMessageKind.Login);
            NetMessages.WriteString(ref w, username);
            NetMessages.WriteString(ref w, password);
        }

        public static void ReadLogin(ref ByteReader r, out string username, out string password)
        {
            username = NetMessages.ReadString(ref r);
            password = NetMessages.ReadString(ref r);
        }

        public static void WriteLoginResult(ref ByteWriter w, AccountResult result, string sessionToken)
        {
            w.WriteByte((byte)ControlMessageKind.LoginResult);
            w.WriteByte((byte)result);
            NetMessages.WriteString(ref w, sessionToken ?? "");
        }

        public static void ReadLoginResult(ref ByteReader r, out AccountResult result, out string sessionToken)
        {
            result = (AccountResult)r.ReadByte();
            sessionToken = NetMessages.ReadString(ref r);
        }

        public static void WriteResumeSession(ref ByteWriter w, string sessionToken)
        {
            w.WriteByte((byte)ControlMessageKind.ResumeSession);
            NetMessages.WriteString(ref w, sessionToken);
        }

        public static string ReadResumeSession(ref ByteReader r) => NetMessages.ReadString(ref r);

        public static void WriteResumeSessionResult(ref ByteWriter w, AccountResult result, string username)
        {
            w.WriteByte((byte)ControlMessageKind.ResumeSessionResult);
            w.WriteByte((byte)result);
            NetMessages.WriteString(ref w, username ?? "");
        }

        public static void ReadResumeSessionResult(ref ByteReader r, out AccountResult result, out string username)
        {
            result = (AccountResult)r.ReadByte();
            username = NetMessages.ReadString(ref r);
        }

        public static void WriteCreateRoom(ref ByteWriter w, string mapName, string hostName, int maxPlayers)
        {
            w.WriteByte((byte)ControlMessageKind.CreateRoom);
            NetMessages.WriteString(ref w, mapName);
            NetMessages.WriteString(ref w, hostName);
            w.WriteByte((byte)maxPlayers);
        }

        public static void ReadCreateRoom(ref ByteReader r, out string mapName, out string hostName,
            out int maxPlayers)
        {
            mapName = NetMessages.ReadString(ref r);
            hostName = NetMessages.ReadString(ref r);
            maxPlayers = r.ReadByte();
        }

        /// <summary>The creator is always room-peer-id 0 (the server's
        /// RoomManager enforces it), so there is nothing else for this to
        /// carry beyond the room id.</summary>
        public static void WriteCreateRoomResult(ref ByteWriter w, string roomId)
        {
            w.WriteByte((byte)ControlMessageKind.CreateRoomResult);
            NetMessages.WriteString(ref w, roomId);
        }

        public static string ReadCreateRoomResult(ref ByteReader r) => NetMessages.ReadString(ref r);

        public static void WriteJoinRoom(ref ByteWriter w, string roomId)
        {
            w.WriteByte((byte)ControlMessageKind.JoinRoom);
            NetMessages.WriteString(ref w, roomId);
        }

        public static string ReadJoinRoom(ref ByteReader r) => NetMessages.ReadString(ref r);

        public static void WriteJoinRoomResult(ref ByteWriter w, RoomJoinFailure failure, int yourPeerId)
        {
            w.WriteByte((byte)ControlMessageKind.JoinRoomResult);
            w.WriteByte((byte)failure);
            w.WriteInt(yourPeerId);
        }

        public static void ReadJoinRoomResult(ref ByteReader r, out RoomJoinFailure failure, out int yourPeerId)
        {
            failure = (RoomJoinFailure)r.ReadByte();
            yourPeerId = r.ReadInt();
        }

        public static void WriteListRooms(ref ByteWriter w) => w.WriteByte((byte)ControlMessageKind.ListRooms);

        public static void WriteListRoomsResult(ref ByteWriter w,
            System.Collections.Generic.IReadOnlyCollection<RoomSummary> rooms)
        {
            w.WriteByte((byte)ControlMessageKind.ListRoomsResult);
            w.WriteUShort((ushort)rooms.Count);
            foreach (var room in rooms)
            {
                NetMessages.WriteString(ref w, room.RoomId);
                NetMessages.WriteString(ref w, room.MapName);
                NetMessages.WriteString(ref w, room.HostName);
                w.WriteByte((byte)room.PlayerCount);
                w.WriteByte((byte)room.MaxPlayers);
            }
        }

        public static RoomSummary[] ReadListRoomsResult(ref ByteReader r)
        {
            int count = r.ReadUShort();
            var rooms = new RoomSummary[count];
            for (int i = 0; i < count; i++)
            {
                rooms[i] = new RoomSummary
                {
                    RoomId = NetMessages.ReadString(ref r),
                    MapName = NetMessages.ReadString(ref r),
                    HostName = NetMessages.ReadString(ref r),
                    PlayerCount = r.ReadByte(),
                    MaxPlayers = r.ReadByte(),
                };
            }
            return rooms;
        }

        /// <summary>Opaque passthrough. <paramref name="targetPeerId"/> of -1
        /// means broadcast to every other room member — the server still
        /// never looks past this header into <paramref name="payload"/>.</summary>
        public static void WriteRoomRelay(ref ByteWriter w, int targetPeerId, byte[] payload, int length)
        {
            w.WriteByte((byte)ControlMessageKind.RoomRelay);
            w.WriteInt(targetPeerId);
            w.WriteBytes(payload, 0, length);
        }

        /// <summary>Server-side re-stamp: same payload, but the header now
        /// says who it came FROM rather than who it's going to — the
        /// receiving RelayPeerSocket needs the sender's peer id, not the
        /// target it was originally addressed to.</summary>
        public static void WriteRoomRelayFrom(ref ByteWriter w, int fromPeerId, byte[] payload, int offset,
            int length)
        {
            w.WriteByte((byte)ControlMessageKind.RoomRelay);
            w.WriteInt(fromPeerId);
            w.WriteBytes(payload, offset, length);
        }

        public static void ReadRoomRelay(ref ByteReader r, out int peerId, out byte[] payload)
        {
            peerId = r.ReadInt();
            payload = r.ReadBytes();
        }

        public static void WriteRoomPeerEvent(ref ByteWriter w, int peerId, bool connected)
        {
            w.WriteByte((byte)ControlMessageKind.RoomPeerEvent);
            w.WriteInt(peerId);
            w.WriteByte((byte)(connected ? 1 : 0));
        }

        public static void ReadRoomPeerEvent(ref ByteReader r, out int peerId, out bool connected)
        {
            peerId = r.ReadInt();
            connected = r.ReadByte() != 0;
        }

        public static void WriteChatMessage(ref ByteWriter w, string senderName, string text)
        {
            w.WriteByte((byte)ControlMessageKind.ChatMessage);
            NetMessages.WriteString(ref w, senderName);
            NetMessages.WriteString(ref w, text);
        }

        public static void ReadChatMessage(ref ByteReader r, out string senderName, out string text)
        {
            senderName = NetMessages.ReadString(ref r);
            text = NetMessages.ReadString(ref r);
        }

        public static void WriteChatBroadcast(ref ByteWriter w, string senderName, string text)
        {
            w.WriteByte((byte)ControlMessageKind.ChatBroadcast);
            NetMessages.WriteString(ref w, senderName);
            NetMessages.WriteString(ref w, text);
        }

        public static void ReadChatBroadcast(ref ByteReader r, out string senderName, out string text)
        {
            senderName = NetMessages.ReadString(ref r);
            text = NetMessages.ReadString(ref r);
        }

        /// <summary>One player's outcome — username plus whether their side
        /// won. Parallel arrays on the wire rather than a struct array to
        /// keep this file's only dependency the same WriteString/ReadString
        /// every other message here already uses.</summary>
        public static void WriteReportMatchResult(ref ByteWriter w, string map, string mode,
            string[] usernames, bool[] won)
        {
            w.WriteByte((byte)ControlMessageKind.ReportMatchResult);
            NetMessages.WriteString(ref w, map);
            NetMessages.WriteString(ref w, mode);
            w.WriteByte((byte)usernames.Length);
            for (int i = 0; i < usernames.Length; i++)
            {
                NetMessages.WriteString(ref w, usernames[i]);
                w.WriteByte((byte)(won[i] ? 1 : 0));
            }
        }

        public static void ReadReportMatchResult(ref ByteReader r, out string map, out string mode,
            out string[] usernames, out bool[] won)
        {
            map = NetMessages.ReadString(ref r);
            mode = NetMessages.ReadString(ref r);
            int count = r.ReadByte();
            usernames = new string[count];
            won = new bool[count];
            for (int i = 0; i < count; i++)
            {
                usernames[i] = NetMessages.ReadString(ref r);
                won[i] = r.ReadByte() != 0;
            }
        }

        public static void WriteReportMatchResultAck(ref ByteWriter w, bool ok)
        {
            w.WriteByte((byte)ControlMessageKind.ReportMatchResultAck);
            w.WriteByte((byte)(ok ? 1 : 0));
        }

        public static bool ReadReportMatchResultAck(ref ByteReader r) => r.ReadByte() != 0;

        public static void WriteChannelJoin(ref ByteWriter w, string channelName)
        {
            w.WriteByte((byte)ControlMessageKind.ChannelJoin);
            NetMessages.WriteString(ref w, channelName);
        }

        public static string ReadChannelJoin(ref ByteReader r) => NetMessages.ReadString(ref r);

        public static void WriteChannelJoinResult(ref ByteWriter w, bool ok, string reason, string channelName,
            string[] memberUsernames, string opUsername)
        {
            w.WriteByte((byte)ControlMessageKind.ChannelJoinResult);
            w.WriteByte((byte)(ok ? 1 : 0));
            NetMessages.WriteString(ref w, reason ?? "");
            NetMessages.WriteString(ref w, channelName ?? "");
            w.WriteUShort((ushort)(memberUsernames?.Length ?? 0));
            if (memberUsernames != null)
                foreach (string name in memberUsernames)
                    NetMessages.WriteString(ref w, name);
            NetMessages.WriteString(ref w, opUsername ?? "");
        }

        public static void ReadChannelJoinResult(ref ByteReader r, out bool ok, out string reason,
            out string channelName, out string[] memberUsernames, out string opUsername)
        {
            ok = r.ReadByte() != 0;
            reason = NetMessages.ReadString(ref r);
            channelName = NetMessages.ReadString(ref r);
            int count = r.ReadUShort();
            memberUsernames = new string[count];
            for (int i = 0; i < count; i++)
                memberUsernames[i] = NetMessages.ReadString(ref r);
            opUsername = NetMessages.ReadString(ref r);
        }

        public static void WriteChannelMemberEvent(ref ByteWriter w, string channelName, string username,
            bool joined, string opUsername)
        {
            w.WriteByte((byte)ControlMessageKind.ChannelMemberEvent);
            NetMessages.WriteString(ref w, channelName);
            NetMessages.WriteString(ref w, username);
            w.WriteByte((byte)(joined ? 1 : 0));
            NetMessages.WriteString(ref w, opUsername ?? "");
        }

        public static void ReadChannelMemberEvent(ref ByteReader r, out string channelName, out string username,
            out bool joined, out string opUsername)
        {
            channelName = NetMessages.ReadString(ref r);
            username = NetMessages.ReadString(ref r);
            joined = r.ReadByte() != 0;
            opUsername = NetMessages.ReadString(ref r);
        }

        public static void WriteChannelChat(ref ByteWriter w, string text)
        {
            w.WriteByte((byte)ControlMessageKind.ChannelChat);
            NetMessages.WriteString(ref w, text);
        }

        public static string ReadChannelChat(ref ByteReader r) => NetMessages.ReadString(ref r);

        public static void WriteChannelChatBroadcast(ref ByteWriter w, string channelName, string senderUsername,
            string text)
        {
            w.WriteByte((byte)ControlMessageKind.ChannelChatBroadcast);
            NetMessages.WriteString(ref w, channelName);
            NetMessages.WriteString(ref w, senderUsername);
            NetMessages.WriteString(ref w, text);
        }

        public static void ReadChannelChatBroadcast(ref ByteReader r, out string channelName,
            out string senderUsername, out string text)
        {
            channelName = NetMessages.ReadString(ref r);
            senderUsername = NetMessages.ReadString(ref r);
            text = NetMessages.ReadString(ref r);
        }

        public static void WriteChannelKick(ref ByteWriter w, string targetUsername)
        {
            w.WriteByte((byte)ControlMessageKind.ChannelKick);
            NetMessages.WriteString(ref w, targetUsername);
        }

        public static string ReadChannelKick(ref ByteReader r) => NetMessages.ReadString(ref r);

        public static void WriteChannelKickResult(ref ByteWriter w, bool ok, string reason)
        {
            w.WriteByte((byte)ControlMessageKind.ChannelKickResult);
            w.WriteByte((byte)(ok ? 1 : 0));
            NetMessages.WriteString(ref w, reason ?? "");
        }

        public static void ReadChannelKickResult(ref ByteReader r, out bool ok, out string reason)
        {
            ok = r.ReadByte() != 0;
            reason = NetMessages.ReadString(ref r);
        }

        public static void WriteChannelKicked(ref ByteWriter w, string channelName, string byUsername)
        {
            w.WriteByte((byte)ControlMessageKind.ChannelKicked);
            NetMessages.WriteString(ref w, channelName);
            NetMessages.WriteString(ref w, byUsername ?? "");
        }

        public static void ReadChannelKicked(ref ByteReader r, out string channelName, out string byUsername)
        {
            channelName = NetMessages.ReadString(ref r);
            byUsername = NetMessages.ReadString(ref r);
        }
    }
}
