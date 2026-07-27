using System;
using System.IO;
using Craftwar.Net;
using Craftwar.NetServer.Db;
using Craftwar.NetServer.Protocol;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    /// <summary>Exercises AccountService against a real (temp-file) SQLite
    /// database — the account/session logic is exactly what a production
    /// server runs, just pointed at a throwaway file per test.</summary>
    public class AccountServiceTests
    {
        string _dbPath;
        AccountService _accounts;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"craftwar-test-{Guid.NewGuid():N}.db");
            var db = new Database(_dbPath);
            db.EnsureSchema();
            _accounts = new AccountService(new AccountRepository(db));
        }

        [TearDown]
        public void TearDown()
        {
            // SQLite keeps the file handle briefly after disposal on some
            // platforms; a missing file here is not a test failure.
            try { File.Delete(_dbPath); } catch (IOException) { }
        }

        [Test]
        public void Register_ThenLogin_Succeeds()
        {
            Assert.AreEqual(AccountResult.Ok, _accounts.Register("grom", "hunter22345", out long id));
            Assert.Greater(id, 0);

            var result = _accounts.Login("grom", "hunter22345", out string token, out long loginId);
            Assert.AreEqual(AccountResult.Ok, result);
            Assert.AreEqual(id, loginId);
            Assert.IsNotEmpty(token);
        }

        [Test]
        public void Register_DuplicateUsername_IsRefused()
        {
            Assert.AreEqual(AccountResult.Ok, _accounts.Register("grom", "hunter22345", out _));
            Assert.AreEqual(AccountResult.UsernameTaken,
                _accounts.Register("grom", "differentpassword", out _));
        }

        [TestCase("ab")] // too short
        [TestCase("this-username-is-far-too-long-to-be-real")] // too long
        [TestCase("has spaces")]
        [TestCase("")]
        public void Register_InvalidUsername_IsRefused(string username)
        {
            Assert.AreEqual(AccountResult.InvalidUsername, _accounts.Register(username, "hunter22345", out _));
        }

        [Test]
        public void Register_WeakPassword_IsRefused()
        {
            Assert.AreEqual(AccountResult.WeakPassword, _accounts.Register("grom", "short", out _));
        }

        [Test]
        public void Login_WrongPassword_IsRefused()
        {
            _accounts.Register("grom", "hunter22345", out _);
            var result = _accounts.Login("grom", "wrongpassword", out string token, out _);
            Assert.AreEqual(AccountResult.WrongCredentials, result);
            Assert.IsNull(token);
        }

        [Test]
        public void Login_UnknownUsername_IsRefused()
        {
            var result = _accounts.Login("nobody", "hunter22345", out _, out _);
            Assert.AreEqual(AccountResult.WrongCredentials, result,
                "an unknown username must look the same as a wrong password — no username enumeration");
        }

        [Test]
        public void ResumeSession_WithAFreshToken_Succeeds()
        {
            _accounts.Register("grom", "hunter22345", out long id);
            _accounts.Login("grom", "hunter22345", out string token, out _);

            var result = _accounts.ResumeSession(token, out long resumedId, out string username);
            Assert.AreEqual(AccountResult.Ok, result);
            Assert.AreEqual(id, resumedId);
            Assert.AreEqual("grom", username);
        }

        [Test]
        public void ResumeSession_WithAnUnknownToken_Fails()
        {
            var result = _accounts.ResumeSession("not-a-real-token", out _, out _);
            Assert.AreEqual(AccountResult.SessionExpired, result);
        }

        [Test]
        public void EachLogin_IssuesADistinctToken()
        {
            _accounts.Register("grom", "hunter22345", out _);
            _accounts.Login("grom", "hunter22345", out string tokenA, out _);
            _accounts.Login("grom", "hunter22345", out string tokenB, out _);
            Assert.AreNotEqual(tokenA, tokenB, "multiple sessions must not collide");
            // Both remain valid — logging in on a second device must not
            // silently sign the first one out.
            Assert.AreEqual(AccountResult.Ok, _accounts.ResumeSession(tokenA, out _, out _));
            Assert.AreEqual(AccountResult.Ok, _accounts.ResumeSession(tokenB, out _, out _));
        }
    }
}
