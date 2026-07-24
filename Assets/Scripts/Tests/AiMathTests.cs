using NUnit.Framework;
using Craftwar.Sim.Ai;

namespace Craftwar.Sim.Tests
{
    /// <summary>Fixed-point (Q16.16) math used by the utility AI.</summary>
    public class AiMathTests
    {
        [Test]
        public void One_IsUnitInQ16_16()
        {
            Assert.AreEqual(65536, AiMath.One);
            Assert.AreEqual(AiMath.One, AiMath.FromInt(1));
            Assert.AreEqual(5, AiMath.ToInt(AiMath.FromInt(5)));
        }

        [Test]
        public void Mul_Div_RoundTrip()
        {
            int a = AiMath.FromInt(3);
            int b = AiMath.FromInt(7);
            Assert.AreEqual(AiMath.FromInt(21), AiMath.Mul(a, b));
            // half * half == quarter
            Assert.AreEqual(AiMath.One / 4, AiMath.Mul(AiMath.Half, AiMath.Half));
            // Div is the inverse of Mul within rounding.
            Assert.AreEqual(a, AiMath.Div(AiMath.Mul(a, b), b));
        }

        [Test]
        public void Div_ByZero_IsZero()
        {
            Assert.AreEqual(0, AiMath.Div(AiMath.One, 0));
        }

        [Test]
        public void Clamp01_Bounds()
        {
            Assert.AreEqual(0, AiMath.Clamp01(-5));
            Assert.AreEqual(AiMath.One, AiMath.Clamp01(AiMath.One + 5));
            Assert.AreEqual(AiMath.Half, AiMath.Clamp01(AiMath.Half));
        }

        [Test]
        public void Lerp_Endpoints_And_Midpoint()
        {
            int a = AiMath.FromInt(10), b = AiMath.FromInt(20);
            Assert.AreEqual(a, AiMath.Lerp(a, b, 0));
            Assert.AreEqual(b, AiMath.Lerp(a, b, AiMath.One));
            Assert.AreEqual(AiMath.FromInt(15), AiMath.Lerp(a, b, AiMath.Half));
        }

        [Test]
        public void Normalize_ClampsAndScales()
        {
            Assert.AreEqual(0, AiMath.Normalize(0, 0, 10));
            Assert.AreEqual(AiMath.One, AiMath.Normalize(10, 0, 10));
            Assert.AreEqual(AiMath.Half, AiMath.Normalize(5, 0, 10));
            Assert.AreEqual(0, AiMath.Normalize(-3, 0, 10));      // below min
            Assert.AreEqual(AiMath.One, AiMath.Normalize(50, 0, 10)); // above max
            // Degenerate range.
            Assert.AreEqual(AiMath.One, AiMath.Normalize(5, 5, 5));
            Assert.AreEqual(0, AiMath.Normalize(4, 5, 5));
        }

        [Test]
        public void Isqrt_FloorsExactAndInexact()
        {
            Assert.AreEqual(0, AiMath.Isqrt(0));
            Assert.AreEqual(0, AiMath.Isqrt(-9));
            Assert.AreEqual(1, AiMath.Isqrt(1));
            Assert.AreEqual(2, AiMath.Isqrt(4));
            Assert.AreEqual(3, AiMath.Isqrt(15));   // floor(3.87)
            Assert.AreEqual(4, AiMath.Isqrt(16));
            Assert.AreEqual(46340, AiMath.Isqrt(int.MaxValue)); // 46340^2 < 2^31 < 46341^2
        }

        [Test]
        public void Isqrt_MatchesBruteForce_SmallRange()
        {
            for (int n = 0; n < 5000; n++)
            {
                int r = AiMath.Isqrt(n);
                Assert.LessOrEqual(r * r, n, $"Isqrt({n}) too big");
                Assert.Greater((r + 1) * (r + 1), n, $"Isqrt({n}) too small");
            }
        }

        [Test]
        public void SqrtFixed_ApproximatesRealRoot()
        {
            // sqrt(4.0) == 2.0
            Assert.AreEqual(AiMath.FromInt(2), AiMath.SqrtFixed(AiMath.FromInt(4)));
            // sqrt(0.25) == 0.5
            Assert.AreEqual(AiMath.Half, AiMath.SqrtFixed(AiMath.One / 4));
            Assert.AreEqual(0, AiMath.SqrtFixed(0));
        }

        [Test]
        public void TileDistance_IsEuclideanFloor()
        {
            Assert.AreEqual(5, AiMath.TileDistance(0, 0, 3, 4)); // 3-4-5
            Assert.AreEqual(0, AiMath.TileDistance(7, 7, 7, 7));
            Assert.AreEqual(1, AiMath.TileDistance(0, 0, 1, 1)); // floor(1.41)
        }
    }
}
