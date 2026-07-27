using Craftwar.NetServer.Auth;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    public class PasswordHasherTests
    {
        [Test]
        public void CorrectPassword_Verifies()
        {
            string encoded = PasswordHasher.Hash("hunter22345");
            Assert.IsTrue(PasswordHasher.Verify("hunter22345", encoded));
        }

        [Test]
        public void WrongPassword_FailsVerification()
        {
            string encoded = PasswordHasher.Hash("hunter22345");
            Assert.IsFalse(PasswordHasher.Verify("wrongpassword", encoded));
        }

        [Test]
        public void TwoHashesOfTheSamePassword_AreDifferent()
        {
            // A fresh random salt every time — equal encodings would mean the
            // salt was not actually random, which defeats its purpose.
            string a = PasswordHasher.Hash("hunter22345");
            string b = PasswordHasher.Hash("hunter22345");
            Assert.AreNotEqual(a, b);
            Assert.IsTrue(PasswordHasher.Verify("hunter22345", a));
            Assert.IsTrue(PasswordHasher.Verify("hunter22345", b));
        }

        [Test]
        public void MalformedEncoding_FailsClosed()
        {
            Assert.IsFalse(PasswordHasher.Verify("anything", "not-a-real-hash"));
            Assert.IsFalse(PasswordHasher.Verify("anything", ""));
        }
    }
}
