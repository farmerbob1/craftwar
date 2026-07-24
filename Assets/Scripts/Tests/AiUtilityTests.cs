using System.Collections.Generic;
using NUnit.Framework;
using Craftwar.Sim.Ai;
using Craftwar.Sim.Ai.Utility;

namespace Craftwar.Sim.Tests
{
    /// <summary>Response curves + IAUS scoring + deterministic ranking.</summary>
    public class AiUtilityTests
    {
        [Test]
        public void Constant_IgnoresInput()
        {
            var c = ResponseCurve.Constant(AiMath.Half);
            Assert.AreEqual(AiMath.Half, c.Eval(0));
            Assert.AreEqual(AiMath.Half, c.Eval(AiMath.One));
        }

        [Test]
        public void Linear_Identity_And_Downslope()
        {
            var id = ResponseCurve.Identity;
            Assert.AreEqual(0, id.Eval(0));
            Assert.AreEqual(AiMath.One, id.Eval(AiMath.One));
            Assert.AreEqual(AiMath.Half, id.Eval(AiMath.Half));

            // Downslope: y = 1 - x
            var down = ResponseCurve.Linear(-AiMath.One, AiMath.One);
            Assert.AreEqual(AiMath.One, down.Eval(0));
            Assert.AreEqual(0, down.Eval(AiMath.One));
        }

        [Test]
        public void Linear_ClampsOutOfRange()
        {
            var steep = ResponseCurve.Linear(AiMath.FromInt(4), 0);
            Assert.AreEqual(AiMath.One, steep.Eval(AiMath.Half)); // 4*0.5 = 2 -> clamp 1
            Assert.AreEqual(0, steep.Eval(0));
        }

        [Test]
        public void Step_IsAHardGate()
        {
            var s = ResponseCurve.Step(AiMath.Half);
            Assert.AreEqual(0, s.Eval(AiMath.Half - 1));
            Assert.AreEqual(AiMath.One, s.Eval(AiMath.Half));
            Assert.AreEqual(AiMath.One, s.Eval(AiMath.One));
        }

        [Test]
        public void Logistic_IsMonotonicSCurveInBounds()
        {
            var l = ResponseCurve.Logistic(AiMath.FromInt(2), AiMath.Half);
            int prev = -1;
            for (int i = 0; i <= 16; i++)
            {
                int x = i * AiMath.One / 16;
                int y = l.Eval(x);
                Assert.GreaterOrEqual(y, 0);
                Assert.LessOrEqual(y, AiMath.One);
                Assert.GreaterOrEqual(y, prev, "logistic must be non-decreasing");
                prev = y;
            }
            // Symmetric-ish: midpoint maps near 0.5.
            Assert.AreEqual(AiMath.Half, l.Eval(AiMath.Half));
        }

        [Test]
        public void Compensate_LiftsMultiConsiderationProducts()
        {
            // Two considerations both 0.5: raw product 0.25, compensated should lift.
            int raw = AiMath.Mul(AiMath.Half, AiMath.Half); // 0.25
            int comp = Util.Compensate(raw, 2);
            Assert.Greater(comp, raw);
            Assert.LessOrEqual(comp, AiMath.One);
            // Single consideration is unchanged.
            Assert.AreEqual(AiMath.Half, Util.Compensate(AiMath.Half, 1));
        }

        [Test]
        public void Score_ScalesByWeight()
        {
            int s1 = Util.Score(AiMath.One, AiMath.Half);
            int s2 = Util.Score(AiMath.FromInt(2), AiMath.Half);
            Assert.AreEqual(AiMath.Half, s1);
            Assert.AreEqual(2 * s1, s2);
        }

        [Test]
        public void SortByScore_IsDeterministicTotalOrder()
        {
            var list = new List<UtilityAction>
            {
                new UtilityAction { Kind = AiActionKind.Build, Score = AiMath.Half, Seq = 3 },
                new UtilityAction { Kind = AiActionKind.TrainUnit, Score = AiMath.One, Seq = 1 },
                new UtilityAction { Kind = AiActionKind.Research, Score = AiMath.Half, Seq = 2 },
            };
            Util.SortByScore(list);
            Assert.AreEqual(AiActionKind.TrainUnit, list[0].Kind);          // highest score
            // Tie at 0.5 broken by Kind: Build(2) < Research(6) so Build first.
            Assert.AreEqual(AiActionKind.Build, list[1].Kind);
            Assert.AreEqual(AiActionKind.Research, list[2].Kind);
        }
    }
}
