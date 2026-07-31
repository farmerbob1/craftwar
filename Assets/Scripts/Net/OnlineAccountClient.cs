using System;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// Register/log in over a short-lived connection, separate from the
    /// long-lived <see cref="RelayPeerSocket"/> a room uses: an account is
    /// logged into once and can then create or join many rooms (or come back
    /// later with the session token), so tying auth to a single room
    /// connection's lifetime would be the wrong shape.
    /// </summary>
    public static class OnlineAccountClient
    {
        public static AccountResult Register(string serverHost, int serverPort, string username, string password)
        {
            using var conn = OpenAndHello(serverHost, serverPort);
            var w = new ByteWriter(128);
            ControlProtocol.WriteRegister(ref w, username, password);
            var r = SendAndReceive(conn, w);
            return ControlProtocol.ReadRegisterResult(ref r);
        }

        public static AccountResult Login(string serverHost, int serverPort, string username, string password,
            out string sessionToken)
        {
            using var conn = OpenAndHello(serverHost, serverPort);
            var w = new ByteWriter(128);
            ControlProtocol.WriteLogin(ref w, username, password);
            var r = SendAndReceive(conn, w);
            ControlProtocol.ReadLoginResult(ref r, out var result, out sessionToken);
            return result;
        }

        public static AccountResult ResumeSession(string serverHost, int serverPort, string sessionToken,
            out string username)
        {
            using var conn = OpenAndHello(serverHost, serverPort);
            var w = new ByteWriter(128);
            ControlProtocol.WriteResumeSession(ref w, sessionToken);
            var r = SendAndReceive(conn, w);
            ControlProtocol.ReadResumeSessionResult(ref r, out var result, out username);
            return result;
        }

        /// <summary>A short-lived query, same reasoning as Register/Login:
        /// the room browser needs this before any room-relay connection
        /// exists yet.</summary>
        public static RoomSummary[] ListRooms(string serverHost, int serverPort)
        {
            using var conn = OpenAndHello(serverHost, serverPort);
            var w = new ByteWriter(16);
            ControlProtocol.WriteListRooms(ref w);
            var r = SendAndReceive(conn, w);
            return ControlProtocol.ReadListRoomsResult(ref r);
        }

        /// <summary>One-shot rating lookup, for symmetry/testability with the
        /// rest of this class. The room browser itself doesn't call this per
        /// row — RoomSummary already carries each room's host rating (see
        /// ListRooms) — this is the entry point for a click-to-inspect popup
        /// on a browser row before joining it.</summary>
        public static bool GetRating(string serverHost, int serverPort, string username,
            out int rating, out int gamesPlayed)
        {
            using var conn = OpenAndHello(serverHost, serverPort);
            var w = new ByteWriter(username.Length + 16);
            ControlProtocol.WriteGetRating(ref w, username);
            var r = SendAndReceive(conn, w);
            ControlProtocol.ReadGetRatingResult(ref r, out _, out bool found, out rating, out gamesPlayed);
            return found;
        }

        static SslStream OpenAndHello(string serverHost, int serverPort)
        {
            var tcp = new TcpClient();
            tcp.Connect(serverHost, serverPort);
            tcp.NoDelay = true;
            var ssl = new SslStream(tcp.GetStream(), false, ValidateServerCertificate);
            ssl.AuthenticateAsClient(serverHost);

            var hello = new ByteWriter(16);
            ControlProtocol.WriteHello(ref hello, ControlProtocol.CurrentVersion);
            var ack = SendAndReceive(ssl, hello);
            ControlProtocol.ReadHelloAck(ref ack, out bool accepted, out string reason);
            if (!accepted)
            {
                ssl.Dispose();
                tcp.Dispose();
                throw new InvalidOperationException($"relay server refused the connection: {reason}");
            }
            return ssl;
        }

        static ByteReader SendAndReceive(SslStream ssl, ByteWriter w)
        {
            StreamFraming.WriteFrameAsync(ssl, w.Buffer, w.Position).GetAwaiter().GetResult();
            byte[] frame = StreamFraming.ReadFrameAsync(ssl).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("relay server closed the connection");
            var r = new ByteReader(frame);
            r.ReadByte(); // message kind — the specific ReadXxx call after this knows which
            return r;
        }

        static bool ValidateServerCertificate(object sender, X509Certificate certificate,
            X509Chain chain, SslPolicyErrors errors) => true; // self-signed dev cert — see CertificateProvider
    }
}
