// PA-102X-S spike: JS-compatibility of the generator primitives and edge cases of the port.
using System;
using NUnit.Framework;
using PocketArbor.Spike.TreeGen;

namespace PocketArbor.Spike.Tests
{
    public sealed class SketchPrimitivesTests
    {
        // Reference values printed by node (V8) from the sketch's own rng()/hsh().
        [Test]
        public void Rng_MoyogiSeed3_FirstValuesMatchJs()
        {
            var r = new SketchRng(3 * 9973 + "moyogi".Length * 131);
            double[] js = { 0.35976641601882875, 0.6680463349912316, 0.994431741302833, 0.6551781345624477, 0.3847194828558713 };
            for (int i = 0; i < js.Length; i++) Assert.AreEqual(js[i], r.Next(), 0.0, "value " + i);
        }

        [Test]
        public void Rng_NegativeSeed_MatchesJs()
        {
            var r = new SketchRng(-123456789);
            double[] js = { 0.9335711891762912, 0.2907314267940819, 0.865351774264127 };
            for (int i = 0; i < js.Length; i++) Assert.AreEqual(js[i], r.Next(), 0.0, "value " + i);
        }

        [Test]
        public void Hsh_KnownInputsIncludingInt32Overflow_MatchJs()
        {
            Assert.AreEqual(0.7812785573769361, SketchMath.Hsh(3 * 131 + 99, 5 * 17 + 9, 3 * 31 + 2, 12), 0.0);
            Assert.AreEqual(0.682109439978376, SketchMath.Hsh(27 * 977 + 99, 1399 * 31 + 13, 8, 11), 0.0);
            Assert.AreEqual(0.05783271952532232, SketchMath.Hsh(27 * 53 + 23999, 2, 7, 3), 0.0);
            Assert.AreEqual(0.0, SketchMath.Hsh(0, 0, 0, 0), 0.0);
        }

        [Test]
        public void JsRound_HalfAndNearHalf_RoundsLikeJs()
        {
            Assert.AreEqual(3.0, SketchMath.JsRound(2.5));
            Assert.AreEqual(-2.0, SketchMath.JsRound(-2.5));
            Assert.AreEqual(0.0, SketchMath.JsRound(0.49999999999999994));
            Assert.AreEqual(1078.0, SketchMath.JsRound(1077.5));
        }

        [Test]
        public void Scene_ReusedBuffers_SameLiveDataAsFreshScene()
        {
            SketchPlan a = SketchGenerator.Build("moyogi", 3);
            SketchPlan b = SketchGenerator.Build("bunjin", 27);
            var reuse = new SketchScene();
            SketchGenerator.Scene(a, "yamazakura", 1, SketchLayout.DefaultLight, reuse); // fill with a bigger tree first
            SketchGenerator.Scene(b, "kuromatsu", 0.6, SketchLayout.DefaultLight, reuse);
            SketchScene fresh = SketchGenerator.Scene(SketchGenerator.Build("bunjin", 27), "kuromatsu", 0.6, SketchLayout.DefaultLight);
            Assert.AreEqual(fresh.Nl, reuse.Nl);
            Assert.AreEqual(fresh.Nx, reuse.Nx);
            Assert.AreEqual(fresh.Nc, reuse.Nc);
            for (int i = 0; i < fresh.Nl * 16; i++) Assert.AreEqual(fresh.Leaf[i], reuse.Leaf[i], "LEAF " + i);
            for (int i = 0; i < fresh.Nx * 16; i++) Assert.AreEqual(fresh.XLeaf[i], reuse.XLeaf[i], "XLEAF " + i);
            for (int i = 0; i < fresh.Nc * 4; i++) Assert.AreEqual(fresh.Cb[i], reuse.Cb[i], "CB " + i);
            for (int i = 0; i < (SketchLayout.NT + 1) * 4; i++) Assert.AreEqual(fresh.Dat[i], reuse.Dat[i], "DAT " + i);
        }

        [Test]
        public void Scene_EdgeAgesAllSpeciesAndStyles_FiniteAndWithinCapacity()
        {
            double[] ages = { -1, 0, 0.001, 0.3, 0.62, 0.92, 0.999, 1, 2 };
            foreach (string style in SketchSpecies.StyleKeys)
            {
                SketchPlan plan = SketchGenerator.Build(style, 11);
                foreach (string sp in SketchSpecies.SpeciesKeys)
                {
                    foreach (double t in ages)
                    {
                        SketchScene s = SketchGenerator.Scene(plan, sp, t, SketchLayout.DefaultLight);
                        string ctx = sp + "/" + style + "/t=" + t;
                        Assert.That(s.T, Is.InRange(0.0, 1.0), ctx);
                        Assert.That(s.Nr, Is.InRange(0, SketchLayout.MAXR), ctx);
                        Assert.That(s.Nc, Is.InRange(0, SketchLayout.MAXCH), ctx);
                        Assert.That(s.Ng, Is.InRange(0, SketchLayout.MAXG), ctx);
                        Assert.That(s.Np, Is.InRange(1, SketchLayout.MAXPAD), ctx);
                        Assert.That(s.Nl, Is.InRange(0, SketchLayout.MAXLEAF), ctx);
                        Assert.That(s.Nx, Is.InRange(0, SketchLayout.MAXX), ctx);
                        AssertFinite(s.Dat, 0, (SketchLayout.NT + 1) * 4, ctx + " trunk");
                        AssertFinite(s.Dat, SketchLayout.OB * 4, SketchLayout.NB * 4, ctx + " bounds");
                        AssertFinite(s.Dat, SketchLayout.OR * 4, s.Nr * SketchLayout.RP * 4, ctx + " roots");
                        AssertFinite(s.Dat, SketchLayout.OBR * 4, s.Nc * 20, ctx + " chunks");
                        AssertFinite(s.Dat, SketchLayout.OPF * 4, s.Np * 28, ctx + " pads");
                        AssertFinite(s.Cb, 0, s.Nc * 4, ctx + " CB");
                        AssertFinite(s.Gm, 0, s.Ng * 8, ctx + " GM");
                        AssertFinite(s.Pe, 0, s.Np * 8, ctx + " PE");
                        AssertFinite(s.Leaf, 0, s.Nl * 16, ctx + " LEAF");
                        AssertFinite(s.XLeaf, 0, s.Nx * 16, ctx + " XLEAF");
                    }
                }
            }
        }

        [Test]
        public void Scene_NaNAge_Throws()
        {
            SketchPlan plan = SketchGenerator.Build("chokkan", 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => SketchGenerator.Scene(plan, "momiji", double.NaN, SketchLayout.DefaultLight));
        }

        private static void AssertFinite(float[] a, int start, int len, string ctx)
        {
            for (int i = start; i < start + len; i++)
                if (float.IsNaN(a[i]) || float.IsInfinity(a[i]))
                    Assert.Fail(ctx + ": non-finite value at " + i);
        }
    }
}
