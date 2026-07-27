using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Craftwar.Net;
using Craftwar.NetServer.Transport;

namespace Craftwar.NetServer
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            var config = ServerConfig.FromArgs(args);
            Log($"Craftwar relay server — control v{ControlProtocol.CurrentVersion}");

            using var server = new RelayServerHost(config, Log);
            Log($"TLS cert: {server.Certificate.Subject} (thumbprint {server.Certificate.Thumbprint[..8]}…)");
            Log($"Listening on {config.Host}:{server.Port}");
            server.Start();

            // Both Docker (`docker stop`) and systemd (`systemctl stop`) ask
            // a service to shut down by sending SIGTERM to it, then SIGKILL
            // it unconditionally after a grace period (Docker: 10s default;
            // systemd: TimeoutStopSec) if it hasn't exited by itself. Without
            // this, Main would just get killed mid-`Task.Delay` — server.
            // Dispose() (stop listening, cancel the accept loop) would never
            // run, so in-flight connections get a hard reset instead of the
            // process choosing to stop accepting new ones first.
            using var stopRequested = new SemaphoreSlim(0, 1);
            void RequestStop(string reason)
            {
                Log($"{reason} — shutting down");
                try { stopRequested.Release(); } catch (SemaphoreFullException) { }
            }
            using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM,
                ctx => { ctx.Cancel = true; RequestStop("SIGTERM"); });
            using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT,
                ctx => { ctx.Cancel = true; RequestStop("SIGINT"); });

            await stopRequested.WaitAsync().ConfigureAwait(false);
            return 0;
        }

        static void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
    }
}
