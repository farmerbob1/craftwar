using System;
using System.Collections.Generic;

namespace Craftwar.NetServer.Protocol
{
    /// <summary>A player's rating state. Ratings start here for anyone with
    /// no prior games (Glickman's own recommended defaults).</summary>
    public readonly struct GlickoRating
    {
        public readonly double Rating;
        public readonly double RD;
        public readonly double Volatility;

        public GlickoRating(double rating, double rd, double volatility)
        {
            Rating = rating;
            RD = rd;
            Volatility = volatility;
        }

        public static readonly GlickoRating Unrated = new(1500, 350, 0.06);
    }

    /// <summary>
    /// Mark Glickman's Glicko-2 rating system
    /// (glicko.net/glicko/glicko2.pdf). Chosen over plain Elo because the
    /// rating-deviation term handles new/returning players far better than a
    /// single number can — a provisional player's rating swings hard until
    /// enough games narrow the deviation, without a separate "placement
    /// matches" phase bolted on.
    ///
    /// Team formats (2v2+) rate off the pre-match team-average rating/RD as
    /// the opponent side, with one shared result (win/loss/draw) applied per
    /// player — each player's OWN rating/RD still drives their own update,
    /// only the opponent side is aggregated.
    /// </summary>
    public static class Glicko2
    {
        const double Scale = 173.7178;
        /// <summary>System constant controlling how much volatility can
        /// change per rating period. 0.5 is Glickman's own suggested default
        /// for a system without extensive prior tuning data.</summary>
        const double Tau = 0.5;
        const double ConvergenceEpsilon = 0.000001;

        public readonly struct Result
        {
            public readonly GlickoRating Opponent;
            /// <summary>1 = win, 0.5 = draw, 0 = loss.</summary>
            public readonly double Score;

            public Result(GlickoRating opponent, double score)
            {
                Opponent = opponent;
                Score = score;
            }
        }

        /// <summary>Rate one player against one or more results in a single
        /// rating period. An empty result list still widens RD (Glickman's
        /// "no games this period" case) without moving the rating.</summary>
        public static GlickoRating Update(GlickoRating player, IReadOnlyList<Result> results)
        {
            double mu = (player.Rating - 1500) / Scale;
            double phi = player.RD / Scale;
            double sigma = player.Volatility;

            if (results.Count == 0)
            {
                double phiUnplayed = Math.Sqrt(phi * phi + sigma * sigma);
                return ToRating(mu, phiUnplayed, sigma);
            }

            double vInvSum = 0;
            double deltaSum = 0;
            foreach (var result in results)
            {
                double muJ = (result.Opponent.Rating - 1500) / Scale;
                double phiJ = result.Opponent.RD / Scale;
                double g = G(phiJ);
                double e = E(mu, muJ, g);
                vInvSum += g * g * e * (1 - e);
                deltaSum += g * (result.Score - e);
            }
            double v = 1.0 / vInvSum;
            double delta = v * deltaSum;

            double sigmaPrime = NewVolatility(phi, v, delta, sigma);

            double phiStar = Math.Sqrt(phi * phi + sigmaPrime * sigmaPrime);
            double phiPrime = 1.0 / Math.Sqrt(1.0 / (phiStar * phiStar) + 1.0 / v);
            double muPrime = mu + phiPrime * phiPrime * deltaSum;

            return ToRating(muPrime, phiPrime, sigmaPrime);
        }

        /// <summary>Average rating/RD across a team, used as the single
        /// "opponent" every member of the OTHER team rates against. Plain
        /// arithmetic mean — no weighting — matching the plan's "pre-match
        /// team-average" decision.</summary>
        public static GlickoRating TeamAverage(IReadOnlyList<GlickoRating> team)
        {
            double rating = 0, rd = 0;
            foreach (var member in team)
            {
                rating += member.Rating;
                rd += member.RD;
            }
            int n = team.Count;
            return new GlickoRating(rating / n, rd / n, 0.06);
        }

        static double G(double phi) => 1.0 / Math.Sqrt(1.0 + 3.0 * phi * phi / (Math.PI * Math.PI));

        static double E(double mu, double muJ, double g) => 1.0 / (1.0 + Math.Exp(-g * (mu - muJ)));

        /// <summary>Illinois algorithm (regula falsi variant) solving for the
        /// new volatility — the one step of Glicko-2 with no closed form.
        /// Mirrors Glickman's reference pseudocode exactly so this can be
        /// checked step-for-step against his worked example.</summary>
        static double NewVolatility(double phi, double v, double delta, double sigma)
        {
            double a = Math.Log(sigma * sigma);
            double phi2 = phi * phi;

            double F(double x)
            {
                double ex = Math.Exp(x);
                double num = ex * (delta * delta - phi2 - v - ex);
                double den = 2.0 * (phi2 + v + ex) * (phi2 + v + ex);
                return num / den - (x - a) / (Tau * Tau);
            }

            double A = a;
            double B;
            if (delta * delta > phi2 + v)
            {
                B = Math.Log(delta * delta - phi2 - v);
            }
            else
            {
                int k = 1;
                while (F(a - k * Tau) < 0)
                    k++;
                B = a - k * Tau;
            }

            double fA = F(A);
            double fB = F(B);
            while (Math.Abs(B - A) > ConvergenceEpsilon)
            {
                double C = A + (A - B) * fA / (fB - fA);
                double fC = F(C);
                if (fC * fB < 0)
                {
                    A = B;
                    fA = fB;
                }
                else
                {
                    fA /= 2.0;
                }
                B = C;
                fB = fC;
            }
            return Math.Exp(A / 2.0);
        }

        static GlickoRating ToRating(double mu, double phi, double sigma) =>
            new(Scale * mu + 1500, Scale * phi, sigma);
    }
}
