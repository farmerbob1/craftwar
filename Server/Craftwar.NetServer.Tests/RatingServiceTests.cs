using System;
using System.Collections.Generic;
using System.IO;
using Craftwar.NetServer.Db;
using Craftwar.NetServer.Protocol;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    public class RatingServiceTests
    {
        string _dbPath;
        AccountRepository _accounts;
        RatingRepository _ratings;
        RatingService _service;

        [SetUp]
        public void SetUp()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"craftwar-ratingtest-{Guid.NewGuid():N}.db");
            var db = new Database(_dbPath);
            db.EnsureSchema();
            _accounts = new AccountRepository(db);
            _ratings = new RatingRepository(db);
            _service = new RatingService(_accounts, _ratings);
        }

        [TearDown]
        public void TearDown()
        {
            try { File.Delete(_dbPath); } catch (IOException) { }
        }

        [Test]
        public void WinnerGainsAndLoserLoses_ForTwoRegisteredPlayers()
        {
            _accounts.TryCreate("grom", "hash", out _);
            _accounts.TryCreate("thrall", "hash", out _);

            _service.ReportResult("Skirmish.pud", "1v1", new List<RatingService.PlayerResult>
            {
                new("grom", true),
                new("thrall", false),
            });

            var (gromRating, gromGames) = _ratings.GetOrDefault(GetId("grom"));
            var (thrallRating, thrallGames) = _ratings.GetOrDefault(GetId("thrall"));

            Assert.Greater(gromRating.Rating, 1500);
            Assert.Less(thrallRating.Rating, 1500);
            Assert.AreEqual(1, gromGames);
            Assert.AreEqual(1, thrallGames);
        }

        [Test]
        public void AMatchWithFewerThanTwoRegisteredPlayers_RatesNobody()
        {
            // Only registered-vs-registered games count toward the ladder —
            // rating a real account against an unrated guest would mean
            // grinding rating against an unknown-skill opponent every time.
            _accounts.TryCreate("grom", "hash", out _);
            // "aguest" never registered.

            Assert.DoesNotThrow(() => _service.ReportResult("Skirmish.pud", "1v1", new List<RatingService.PlayerResult>
            {
                new("grom", true),
                new("aguest", false),
            }));

            var (gromRating, gromGames) = _ratings.GetOrDefault(GetId("grom"));
            Assert.AreEqual(1500, gromRating.Rating, 0.0001,
                "with no registered opponent, nobody's rating moves");
            Assert.AreEqual(0, gromGames);
        }

        [Test]
        public void AMatchWithFewerThanTwoRegisteredPlayers_StillRecordsHistory()
        {
            _accounts.TryCreate("grom", "hash", out _);
            _service.ReportResult("Skirmish.pud", "1v1", new List<RatingService.PlayerResult>
            {
                new("grom", true),
                new("aguest", false),
            });

            using var conn = new Database(_dbPath).OpenConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM match_history";
            long count = (long)cmd.ExecuteScalar();
            Assert.AreEqual(1, count, "the match itself is still recorded even if nobody was rated");
        }

        [Test]
        public void ATeamGame_RatesEachPlayerAgainstTheOpposingAverage()
        {
            foreach (string name in new[] { "a1", "a2", "b1", "b2" })
                _accounts.TryCreate(name, "hash", out _);

            _service.ReportResult("4v4.pud", "2v2", new List<RatingService.PlayerResult>
            {
                new("a1", true), new("a2", true),
                new("b1", false), new("b2", false),
            });

            var (a1, _) = _ratings.GetOrDefault(GetId("a1"));
            var (a2, _) = _ratings.GetOrDefault(GetId("a2"));
            var (b1, _) = _ratings.GetOrDefault(GetId("b1"));
            var (b2, _) = _ratings.GetOrDefault(GetId("b2"));

            Assert.Greater(a1.Rating, 1500);
            Assert.Greater(a2.Rating, 1500);
            Assert.Less(b1.Rating, 1500);
            Assert.Less(b2.Rating, 1500);
        }

        [Test]
        public void RepeatedWins_KeepRaisingRatingAndGamesPlayed()
        {
            _accounts.TryCreate("grom", "hash", out _);
            _accounts.TryCreate("thrall", "hash", out _);

            for (int i = 0; i < 3; i++)
                _service.ReportResult("Skirmish.pud", "1v1", new List<RatingService.PlayerResult>
                {
                    new("grom", true),
                    new("thrall", false),
                });

            var (rating, games) = _ratings.GetOrDefault(GetId("grom"));
            Assert.AreEqual(3, games);
            Assert.Greater(rating.Rating, 1500);
        }

        [Test]
        public void TryGetRating_ReturnsTrueAndTheSeededDefaultForAFreshAccount()
        {
            _accounts.TryCreate("grom", "hash", out _);

            bool found = _service.TryGetRating("grom", out var rating, out int games);

            Assert.IsTrue(found);
            Assert.AreEqual(1500, rating.Rating, 0.0001, "Glickman's own default, seeded at registration");
            Assert.AreEqual(0, games);
        }

        [Test]
        public void TryGetRating_ReflectsAPlayedMatch()
        {
            _accounts.TryCreate("grom", "hash", out _);
            _accounts.TryCreate("thrall", "hash", out _);
            _service.ReportResult("Skirmish.pud", "1v1", new List<RatingService.PlayerResult>
            {
                new("grom", true),
                new("thrall", false),
            });

            _service.TryGetRating("grom", out var rating, out int games);

            Assert.Greater(rating.Rating, 1500);
            Assert.AreEqual(1, games);
        }

        [Test]
        public void TryGetRating_ReturnsFalseForAnUnregisteredUsername()
        {
            bool found = _service.TryGetRating("nobody-signed-up", out _, out int games);

            Assert.IsFalse(found);
            Assert.AreEqual(0, games);
        }

        long GetId(string username)
        {
            Assert.IsTrue(_accounts.TryGetByUsername(username, out var account));
            return account.Id;
        }
    }
}
