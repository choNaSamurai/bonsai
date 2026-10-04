using NUnit.Framework;
using PocketArbor.Domain.Random;

namespace PocketArbor.Domain.Tests
{
    public sealed class Pcg32Tests
    {
        // First six outputs of the reference pcg32-demo: pcg32_srandom_r(&rng, 42u, 54u).
        static readonly uint[] ReferenceSeed42Seq54 =
        {
            0xa15c02b7u, 0x7b47f409u, 0xba1d3330u, 0x83d2f293u, 0xbfa4784bu, 0xcbed606eu
        };

        [Test]
        public void MatchesReferenceImplementation()
        {
            var rng = new Pcg32(42UL, 54UL);
            for (int i = 0; i < ReferenceSeed42Seq54.Length; i++)
            {
                Assert.AreEqual(ReferenceSeed42Seq54[i], rng.NextUInt(), "output #" + i);
            }
        }

        [Test]
        public void SameSeedProducesSameSequence()
        {
            var a = new Pcg32(123456789UL);
            var b = new Pcg32(123456789UL);
            for (int i = 0; i < 1000; i++)
            {
                Assert.AreEqual(a.NextUInt(), b.NextUInt());
            }
        }

        [Test]
        public void DifferentStreamsDiverge()
        {
            var a = new Pcg32(7UL, 1UL);
            var b = new Pcg32(7UL, 2UL);
            int equal = 0;
            for (int i = 0; i < 64; i++)
            {
                if (a.NextUInt() == b.NextUInt()) equal++;
            }
            Assert.Less(equal, 4);
        }

        [Test]
        public void BoundedAndDoubleStayInRange()
        {
            var rng = new Pcg32(2026UL);
            for (int i = 0; i < 10000; i++)
            {
                Assert.Less(rng.NextUInt(7u), 7u);
                double d = rng.NextDouble();
                Assert.GreaterOrEqual(d, 0.0);
                Assert.Less(d, 1.0);
            }
        }
    }
}
