using PocketArbor.Domain.Random;

namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// The fixed 7 uniform draws per shoot (BALANCE §1.1 step 6.4): u1 parent, u2/u3 angle, u4 sign,
    /// u5 length, u6/u7 curvature. Always drawn in full to keep the stream stable.
    /// </summary>
    internal struct ShootDraws
    {
        const double InvTwoPow32 = 1.0 / 4294967296.0;

        public double U1, U2, U3, U4, U5, U6, U7;

        /// <summary>u() = NextUInt() / 2^32 ∈ [0, 1).</summary>
        public static double Uniform(IRandomSource rng) => rng.NextUInt() * InvTwoPow32;

        public static ShootDraws Next(IRandomSource rng)
        {
            ShootDraws d;
            d.U1 = Uniform(rng);
            d.U2 = Uniform(rng);
            d.U3 = Uniform(rng);
            d.U4 = Uniform(rng);
            d.U5 = Uniform(rng);
            d.U6 = Uniform(rng);
            d.U7 = Uniform(rng);
            return d;
        }
    }
}
