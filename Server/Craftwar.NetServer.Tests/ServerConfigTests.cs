using System;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    /// <summary>
    /// M11 phase 6: config must be settable via environment (systemd
    /// EnvironmentFile / Docker -e) as well as argv (ad-hoc local runs), with
    /// argv winning when both are set for the same value.
    /// </summary>
    public class ServerConfigTests
    {
        static readonly string[] Vars =
        {
            "CRAFTWAR_HOST", "CRAFTWAR_PORT", "CRAFTWAR_DB_PATH",
            "CRAFTWAR_CERT_PATH", "CRAFTWAR_CERT_PASSWORD",
        };

        [SetUp]
        [TearDown]
        public void ClearEnvironment()
        {
            foreach (string name in Vars)
                Environment.SetEnvironmentVariable(name, null);
        }

        [Test]
        public void WithNothingSet_UsesTheLocalDevDefaults()
        {
            var config = ServerConfig.FromArgs(Array.Empty<string>());
            Assert.AreEqual("0.0.0.0", config.Host);
            Assert.AreEqual(27015, config.Port);
            Assert.AreEqual("craftwar.db", config.DbPath);
            Assert.AreEqual("", config.CertPath);
        }

        [Test]
        public void EnvironmentVariables_OverrideTheDefaults()
        {
            Environment.SetEnvironmentVariable("CRAFTWAR_HOST", "10.0.0.5");
            Environment.SetEnvironmentVariable("CRAFTWAR_PORT", "27020");
            Environment.SetEnvironmentVariable("CRAFTWAR_DB_PATH", "/data/craftwar.db");
            Environment.SetEnvironmentVariable("CRAFTWAR_CERT_PATH", "/etc/craftwar/cert.pfx");
            Environment.SetEnvironmentVariable("CRAFTWAR_CERT_PASSWORD", "s3cret");

            var config = ServerConfig.FromArgs(Array.Empty<string>());
            Assert.AreEqual("10.0.0.5", config.Host);
            Assert.AreEqual(27020, config.Port);
            Assert.AreEqual("/data/craftwar.db", config.DbPath);
            Assert.AreEqual("/etc/craftwar/cert.pfx", config.CertPath);
            Assert.AreEqual("s3cret", config.CertPassword);
        }

        [Test]
        public void ArgsOverrideEnvironmentVariables_WhenBothAreSet()
        {
            Environment.SetEnvironmentVariable("CRAFTWAR_PORT", "27020");
            Environment.SetEnvironmentVariable("CRAFTWAR_HOST", "10.0.0.5");

            var config = ServerConfig.FromArgs(new[] { "--port", "27099" });
            Assert.AreEqual(27099, config.Port, "an explicit --port must win over the environment");
            Assert.AreEqual("10.0.0.5", config.Host, "an unset arg leaves the environment's value in place");
        }

        [Test]
        public void AnInvalidPortEnvironmentVariable_IsIgnoredRatherThanThrowing()
        {
            Environment.SetEnvironmentVariable("CRAFTWAR_PORT", "not-a-number");
            var config = ServerConfig.FromArgs(Array.Empty<string>());
            Assert.AreEqual(27015, config.Port, "a malformed env value falls back to the default, not a crash");
        }
    }
}
