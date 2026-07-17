using Craftwar.Sim;
using NUnit.Framework;

namespace Craftwar.Sim.Tests
{
    public class Pcg32Tests
    {
        [Test]
        public void MatchesReferenceImplementation()
        {
            // Known-answer values from the canonical pcg32-demo
            // (pcg32_srandom(42, 54), first outputs). If these ever fail,
            // cross-platform lockstep is broken.
            var rng = new Pcg32(42, 54);
            uint[] expected =
            {
                0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e,
            };
            foreach (uint e in expected)
                Assert.AreEqual(e, rng.NextUInt());
        }

        [Test]
        public void BoundedNext_StaysInRangeAndCoversRange()
        {
            var rng = new Pcg32(123, 456);
            bool sawZero = false, sawMax = false;
            for (int i = 0; i < 10000; i++)
            {
                int v = rng.Next(8);
                Assert.GreaterOrEqual(v, 0);
                Assert.Less(v, 8);
                if (v == 0) sawZero = true;
                if (v == 7) sawMax = true;
            }
            Assert.IsTrue(sawZero && sawMax);
        }

        [Test]
        public void DamageRollShape_HalfToFull()
        {
            // The WC2 damage roll: half + rng.Next(half + 1) yields [half, 2*half].
            var rng = new Pcg32(9, 9);
            const int nominal = 9; // e.g. footman 6+3 vs 0 armor
            const int half = (nominal + 1) / 2;
            for (int i = 0; i < 10000; i++)
            {
                int roll = half + rng.Next(half + 1);
                Assert.GreaterOrEqual(roll, half);
                Assert.LessOrEqual(roll, nominal + 1); // half*2 can exceed nominal by 1 when odd
            }
        }
    }
}
