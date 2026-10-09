// PA-102X-S spike: golden loader smoke test (M1).
using NUnit.Framework;
using PocketArbor.Spike.TreeGen;

namespace PocketArbor.Spike.Tests
{
    public sealed class GoldenLoaderTests
    {
        [Test]
        public void Keys_GoldenDirectory_Has90Configurations()
        {
            string[] keys = GoldenLoader.Keys();
            if (keys.Length == 0) Assert.Ignore("Golden data not found in " + GoldenLoader.Dir);
            Assert.AreEqual(90, keys.Length);
        }

        [Test]
        public void Load_MomijiMoyogiSeed3T100_CountersAndArraysMatchJson()
        {
            if (GoldenLoader.Keys().Length == 0) Assert.Ignore("Golden data not found in " + GoldenLoader.Dir);
            GoldenData g = GoldenLoader.Load("momiji_moyogi_s3_t100");
            SketchScene s = g.Scene;

            Assert.AreEqual("momiji", s.SpeciesKey);
            Assert.AreEqual("moyogi", s.Style);
            Assert.AreEqual(3, s.Seed);
            Assert.AreEqual(1.0, s.T);
            Assert.AreEqual(0, s.Species.Id);
            Assert.AreEqual(290.0, s.L, 1e-9);
            Assert.AreEqual(24.576271186440678, s.R, 1e-9);
            Assert.AreEqual(6, s.Nr);
            Assert.AreEqual(8, s.Ng);
            Assert.AreEqual(7, s.Nb);
            Assert.AreEqual(4905, s.Nl);
            Assert.AreEqual(0, s.Nx);
            Assert.AreEqual(98, s.Nc);
            Assert.AreEqual(8, s.Np);
            Assert.AreEqual((SketchLayout.N + 1) * 2, g.P.Length);
            Assert.AreEqual(SketchLayout.N + 1, g.T.Length);
            Assert.AreEqual(0.0, g.P[0]);
            Assert.AreEqual(-g.P[SketchLayout.N * 2] / 2, g.Build["cx"].D, 1e-12);
            Assert.AreEqual(s.Nl * 16, g.ArrayLengths["LEAF"]);
            Assert.AreEqual(s.Nc * 4, g.ArrayLengths["CB"]);
            Assert.AreEqual(-0.4482191045323742, s.LightDir.X, 1e-15);
            Assert.AreEqual(99, s.Pads[s.Np - 1].Key);
            Assert.IsTrue(s.Pads[s.Np - 1].Dome);
            // PE row 0 is derived from pad 0: (c.x, c.y + 0.2b, c.z, 0.95a, ...)
            Assert.AreEqual(s.Pads[0].C.X, s.Pe[0], 1e-3);
            Assert.AreEqual(s.Pads[0].C.Y + 0.2 * s.Pads[0].B, s.Pe[1], 1e-3);
            Assert.AreEqual(0.95 * s.Pads[0].A, s.Pe[3], 1e-3);
        }

        [Test]
        public void Load_BunjinStyle_UsesSmallOvalPot()
        {
            if (GoldenLoader.Keys().Length == 0) Assert.Ignore("Golden data not found in " + GoldenLoader.Dir);
            SketchScene s = GoldenLoader.LoadScene("kuromatsu_bunjin_s27_t100");
            Assert.AreEqual(1, s.Pot.Shape);
            Assert.AreEqual(98.0, s.Pot.Hw);
            Assert.AreEqual(52.0, s.Pot.Hd);
            Assert.AreEqual(30.0, s.Pot.Dep);
            Assert.AreEqual(1, s.Pot.Type);
        }
    }
}
