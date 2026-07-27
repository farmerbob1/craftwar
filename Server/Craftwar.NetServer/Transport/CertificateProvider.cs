using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Craftwar.NetServer.Transport
{
    /// <summary>
    /// TLS certificate for the listener. Real accounts travel this
    /// connection, so it is never plaintext, even for "just running it
    /// locally for now" — a self-signed cert generated (and cached) on first
    /// run covers that case; pointing <see cref="ServerConfig.CertPath"/> at
    /// a real PFX (e.g. a Let's Encrypt cert) later is a config change, not a
    /// code change.
    /// </summary>
    public static class CertificateProvider
    {
        public static X509Certificate2 Load(ServerConfig config)
        {
            if (!string.IsNullOrEmpty(config.CertPath) && File.Exists(config.CertPath))
                return X509CertificateLoader.LoadPkcs12FromFile(
                    config.CertPath, config.CertPassword);

            string cachePath = config.CertPath is { Length: > 0 } p ? p : "craftwar-dev-cert.pfx";
            if (File.Exists(cachePath))
                return X509CertificateLoader.LoadPkcs12FromFile(cachePath, config.CertPassword);

            var cert = GenerateSelfSigned(config.Host);
            byte[] pfx = cert.Export(X509ContentType.Pfx, config.CertPassword);
            File.WriteAllBytes(cachePath, pfx);
            // Re-load from the export: the freshly-generated cert's private
            // key is not always marked exportable/persistable the way
            // SslStream needs on every platform, but a round-tripped PFX
            // always is.
            return X509CertificateLoader.LoadPkcs12FromFile(cachePath, config.CertPassword);
        }

        static X509Certificate2 GenerateSelfSigned(string hostName)
        {
            using var rsa = RSA.Create(2048);
            var req = new CertificateRequest(
                $"CN={hostName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            req.CertificateExtensions.Add(
                new X509KeyUsageExtension(
                    X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
            req.CertificateExtensions.Add(
                new X509EnhancedKeyUsageExtension(
                    [new Oid("1.3.6.1.5.5.7.3.1")], false)); // server authentication
            var sanBuilder = new SubjectAlternativeNameBuilder();
            sanBuilder.AddDnsName(hostName);
            sanBuilder.AddDnsName("localhost");
            req.CertificateExtensions.Add(sanBuilder.Build());

            return req.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        }
    }
}
