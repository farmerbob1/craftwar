using Craftwar.Net;
using Craftwar.Sim;
using NUnit.Framework;

namespace Craftwar.NetServer.Tests
{
    public class ControlProtocolTests
    {
        [Test]
        public void Hello_RoundTrips()
        {
            var w = new ByteWriter(16);
            ControlProtocol.WriteHello(ref w, 42);
            var r = new ByteReader(w.ToArray());
            r.ReadByte();
            Assert.AreEqual(42, ControlProtocol.ReadHello(ref r));
        }

        [Test]
        public void HelloAck_RoundTrips_IncludingTheReasonString()
        {
            var w = new ByteWriter(64);
            ControlProtocol.WriteHelloAck(ref w, false, "version mismatch");
            var r = new ByteReader(w.ToArray());
            r.ReadByte();
            ControlProtocol.ReadHelloAck(ref r, out bool accepted, out string reason);
            Assert.IsFalse(accepted);
            Assert.AreEqual("version mismatch", reason);
        }

        [Test]
        public void RegisterAndResult_RoundTrip()
        {
            var w = new ByteWriter(64);
            ControlProtocol.WriteRegister(ref w, "grom", "hunter22345");
            var r = new ByteReader(w.ToArray());
            r.ReadByte();
            ControlProtocol.ReadRegister(ref r, out string username, out string password);
            Assert.AreEqual("grom", username);
            Assert.AreEqual("hunter22345", password);

            w = new ByteWriter(16);
            ControlProtocol.WriteRegisterResult(ref w, AccountResult.UsernameTaken);
            r = new ByteReader(w.ToArray());
            r.ReadByte();
            Assert.AreEqual(AccountResult.UsernameTaken, ControlProtocol.ReadRegisterResult(ref r));
        }

        [Test]
        public void LoginResult_RoundTrips_TheSessionToken()
        {
            var w = new ByteWriter(64);
            ControlProtocol.WriteLoginResult(ref w, AccountResult.Ok, "a-session-token");
            var r = new ByteReader(w.ToArray());
            r.ReadByte();
            ControlProtocol.ReadLoginResult(ref r, out var result, out string token);
            Assert.AreEqual(AccountResult.Ok, result);
            Assert.AreEqual("a-session-token", token);
        }

        [Test]
        public void ResumeSessionResult_RoundTrips()
        {
            var w = new ByteWriter(64);
            ControlProtocol.WriteResumeSessionResult(ref w, AccountResult.Ok, "grom");
            var r = new ByteReader(w.ToArray());
            r.ReadByte();
            ControlProtocol.ReadResumeSessionResult(ref r, out var result, out string username);
            Assert.AreEqual(AccountResult.Ok, result);
            Assert.AreEqual("grom", username);
        }

        [Test]
        public void EveryWriter_ConsumesExactlyWhatItProduced()
        {
            // The same invariant NetMessageTests holds the relay protocol to:
            // a reader must land exactly on the end of the buffer, or framing
            // has silently drifted.
            var w = new ByteWriter(64);
            ControlProtocol.WriteLogin(ref w, "grom", "hunter22345");
            var r = new ByteReader(w.ToArray());
            r.ReadByte();
            ControlProtocol.ReadLogin(ref r, out _, out _);
            Assert.AreEqual(w.Position, r.Position);
        }
    }
}
