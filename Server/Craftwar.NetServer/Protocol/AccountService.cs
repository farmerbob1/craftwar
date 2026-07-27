using System;
using System.Security.Cryptography;
using Craftwar.Net;
using Craftwar.NetServer.Auth;
using Craftwar.NetServer.Db;

namespace Craftwar.NetServer.Protocol
{
    /// <summary>
    /// Business logic for accounts: username/password rules, session token
    /// issuance and expiry. No socket or wire-format knowledge — that's
    /// <see cref="ControlProtocol"/> and the transport layer's job, kept
    /// separate so this is testable without opening a single connection.
    /// </summary>
    public sealed class AccountService
    {
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);
        const int MinUsernameLength = 3;
        const int MaxUsernameLength = 24;
        const int MinPasswordLength = 8;

        readonly AccountRepository _accounts;

        public AccountService(AccountRepository accounts) => _accounts = accounts;

        public AccountResult Register(string username, string password, out long accountId)
        {
            accountId = 0;
            if (!IsValidUsername(username))
                return AccountResult.InvalidUsername;
            if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
                return AccountResult.WeakPassword;

            string hash = PasswordHasher.Hash(password);
            return _accounts.TryCreate(username, hash, out accountId)
                ? AccountResult.Ok
                : AccountResult.UsernameTaken;
        }

        public AccountResult Login(string username, string password, out string sessionToken,
            out long accountId)
        {
            sessionToken = null;
            accountId = 0;
            if (!_accounts.TryGetByUsername(username, out var account))
                return AccountResult.WrongCredentials;
            if (!PasswordHasher.Verify(password, account.PasswordHash))
                return AccountResult.WrongCredentials;

            sessionToken = NewToken();
            _accounts.CreateSession(sessionToken, account.Id, DateTime.UtcNow + SessionLifetime);
            accountId = account.Id;
            return AccountResult.Ok;
        }

        public AccountResult ResumeSession(string token, out long accountId, out string username)
        {
            if (_accounts.TryResumeSession(token, out accountId, out username))
                return AccountResult.Ok;
            return AccountResult.SessionExpired;
        }

        static bool IsValidUsername(string username)
        {
            if (string.IsNullOrEmpty(username))
                return false;
            if (username.Length < MinUsernameLength || username.Length > MaxUsernameLength)
                return false;
            foreach (char c in username)
                if (!char.IsLetterOrDigit(c) && c != '_' && c != '-')
                    return false;
            return true;
        }

        /// <summary>256 bits of randomness, URL-safe — this is the bearer
        /// credential a reconnect will present instead of a password.</summary>
        static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
