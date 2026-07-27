using System;

namespace Craftwar.NetServer
{
    /// <summary>
    /// Everything that changes between "running on my own machine for now"
    /// and "deployed on a real box" — host/port/cert/db path. Deliberately
    /// plain fields, not a config-file framework: this is a handful of
    /// values for a single-process server.
    ///
    /// Two sources, applied in order (each overrides the previous): the
    /// <c>CRAFTWAR_*</c> environment variables, then command-line args. Env
    /// vars exist because they are what both packaging targets from the M11
    /// plan's phase 6 actually use for secrets/config — a systemd unit's
    /// EnvironmentFile and a Docker `-e`/`--env-file` both set environment,
    /// neither naturally sets argv. Args stay supported and win on conflict
    /// for ad-hoc local runs (`dotnet run -- --port 27020`).
    /// </summary>
    public sealed class ServerConfig
    {
        public string Host { get; set; } = "0.0.0.0";
        public int Port { get; set; } = 27015;
        public string DbPath { get; set; } = "craftwar.db";

        /// <summary>Empty = generate/cache a self-signed cert next to the
        /// executable. Point this at a real PFX (e.g. from Let's Encrypt)
        /// once deployed — see M11 plan phase 6.</summary>
        public string CertPath { get; set; } = "";
        public string CertPassword { get; set; } = "craftwar";

        public static ServerConfig FromArgs(string[] args)
        {
            var config = new ServerConfig();
            ApplyEnvironment(config);

            for (int i = 0; i < args.Length - 1; i++)
            {
                switch (args[i])
                {
                    case "--host": config.Host = args[++i]; break;
                    case "--port": config.Port = int.Parse(args[++i]); break;
                    case "--db": config.DbPath = args[++i]; break;
                    case "--cert": config.CertPath = args[++i]; break;
                    case "--cert-password": config.CertPassword = args[++i]; break;
                }
            }
            return config;
        }

        static void ApplyEnvironment(ServerConfig config)
        {
            config.Host = Environment.GetEnvironmentVariable("CRAFTWAR_HOST") ?? config.Host;
            config.DbPath = Environment.GetEnvironmentVariable("CRAFTWAR_DB_PATH") ?? config.DbPath;
            config.CertPath = Environment.GetEnvironmentVariable("CRAFTWAR_CERT_PATH") ?? config.CertPath;
            config.CertPassword = Environment.GetEnvironmentVariable("CRAFTWAR_CERT_PASSWORD") ?? config.CertPassword;
            string port = Environment.GetEnvironmentVariable("CRAFTWAR_PORT");
            if (!string.IsNullOrEmpty(port) && int.TryParse(port, out int parsed))
                config.Port = parsed;
        }
    }
}
