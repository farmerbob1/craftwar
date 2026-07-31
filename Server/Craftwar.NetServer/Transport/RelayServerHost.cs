using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Craftwar.NetServer.Db;
using Craftwar.NetServer.Protocol;

namespace Craftwar.NetServer.Transport
{
    /// <summary>
    /// Server bring-up, extracted from Program.cs so integration tests can
    /// start a real instance in-process (real TCP+TLS on loopback, no
    /// subprocess) instead of re-implementing the accept loop — the same
    /// reasoning as testing HostTurnExchange/TurnRelay through the actual
    /// class rather than a stand-in.
    /// </summary>
    public sealed class RelayServerHost : IDisposable
    {
        readonly TcpListener _listener;
        readonly X509Certificate2 _cert;
        readonly AccountService _accounts;
        readonly RatingService _ratings;
        readonly RoomManager _rooms;
        readonly ConnectionRegistry _registry;
        readonly PresenceDirectory _presence;
        readonly ChannelManager _channels;
        readonly Action<string> _log;
        readonly CancellationTokenSource _cts = new();
        Task _acceptLoop;

        public int Port { get; }
        public RoomManager Rooms => _rooms;

        public RelayServerHost(ServerConfig config, Action<string> log = null)
        {
            _log = log ?? (_ => { });
            var db = new Database(config.DbPath);
            db.EnsureSchema();
            var accountRepo = new AccountRepository(db);
            _accounts = new AccountService(accountRepo);
            _ratings = new RatingService(accountRepo, new RatingRepository(db));
            _rooms = new RoomManager();
            _registry = new ConnectionRegistry();
            _presence = new PresenceDirectory();
            _channels = new ChannelManager();
            _cert = CertificateProvider.Load(config);

            _listener = new TcpListener(IPAddress.Parse(config.Host), config.Port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        }

        public X509Certificate2 Certificate => _cert;

        public void Start() => _acceptLoop = AcceptLoopAsync(_cts.Token);

        async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient tcp;
                try
                {
                    tcp = await _listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                tcp.NoDelay = true;
                var conn = new ClientConnection(tcp, _cert, _accounts, _ratings, _rooms, _registry, _presence,
                    _channels, _log);
                _ = conn.RunAsync(); // fire-and-forget: one task per connection, errors logged inside
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _listener.Stop();
            _cts.Dispose();
        }
    }
}
