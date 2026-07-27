using System;
using System.Security.Cryptography;

namespace Craftwar.NetServer.Auth
{
    /// <summary>
    /// PBKDF2-HMACSHA256 password hashing — built into .NET (no extra
    /// dependency, unlike Argon2/BCrypt), NIST-approved, and plenty for a
    /// self-hosted server at this scale. The encoded form carries its own
    /// iteration count and salt, so the work factor can be raised later
    /// without invalidating passwords hashed under the old one.
    /// </summary>
    public static class PasswordHasher
    {
        const int SaltBytes = 16;
        const int HashBytes = 32;
        const int Iterations = 100_000;

        /// <summary>"{iterations}.{saltBase64}.{hashBase64}" — self-describing,
        /// so <see cref="Verify"/> never has to guess the parameters it was
        /// hashed with.</summary>
        public static string Hash(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
            byte[] hash = Derive(password, salt, Iterations);
            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        public static bool Verify(string password, string encoded)
        {
            string[] parts = encoded.Split('.');
            if (parts.Length != 3)
                return false;
            if (!int.TryParse(parts[0], out int iterations))
                return false;
            byte[] salt, expected;
            try
            {
                salt = Convert.FromBase64String(parts[1]);
                expected = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }
            byte[] actual = Derive(password, salt, iterations);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        static byte[] Derive(string password, byte[] salt, int iterations) =>
            Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
    }
}
