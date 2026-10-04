using System;
using NUnit.Framework;
using UnityEngine;

namespace PocketArbor.Infrastructure.Tests.Persistence
{
    /// <summary>
    /// Characterization tests for UnityEngine.JsonUtility, which ADR-0008 rev.3 builds the save format on (PA-101b).
    /// They pin down engine behaviour the save DTO rules depend on. If a Unity upgrade makes one of them fail,
    /// revisit ADR-0008 (e.g. a correctly rounded double reader would allow plain double fields again).
    /// </summary>
    public sealed class JsonUtilityContractTests
    {
        const int SampleCount = 20000;
        const int Seed = 12345;

        [Serializable]
        sealed class NumbersDto
        {
            public int schemaVersion;
            public double d;
            public float f;
            public long l;
        }

        [Serializable]
        sealed class ULongDto
        {
            public ulong u;
        }

        static double RandomDouble(System.Random rng) => (rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-30, 30));

        static long UlpDistance(double a, double b) =>
            Math.Abs(BitConverter.DoubleToInt64Bits(a) - BitConverter.DoubleToInt64Bits(b));

        [Test]
        public void Double_IsWrittenShortest_ButReadBackIsNotCorrectlyRounded()
        {
            const double value = 123456789.12345679;
            var json = JsonUtility.ToJson(new NumbersDto { d = value });
            StringAssert.Contains("\"d\":123456789.12345679", json, "writer emits the shortest round-trip form");
            double back = JsonUtility.FromJson<NumbersDto>(json).d;
            Assert.AreEqual(1, UlpDistance(value, back), "reader is off by 1 ULP for this value: " + json);
        }

        [Test]
        public void Double_RandomSample_DriftsByAtMostTwoUlp_SoDoubleFieldsAreNotLossless()
        {
            var rng = new System.Random(Seed);
            var dto = new NumbersDto();
            int mismatches = 0;
            long maxUlp = 0;
            for (int i = 0; i < SampleCount; i++)
            {
                dto.d = RandomDouble(rng);
                double back = JsonUtility.FromJson<NumbersDto>(JsonUtility.ToJson(dto)).d;
                long ulp = UlpDistance(dto.d, back);
                if (ulp != 0) mismatches++;
                maxUlp = Math.Max(maxUlp, ulp);
            }
            // Measured on 6000.6.4f1: 3858 / 20000 (19%) values drift, max 2 ULP.
            Assert.Greater(mismatches, 0);
            Assert.LessOrEqual(maxUlp, 2);
        }

        [Test]
        public void DoubleAsInt64Bits_RoundTrips_Losslessly_IncludingNonFinite()
        {
            var rng = new System.Random(Seed);
            var dto = new NumbersDto();
            for (int i = 0; i < SampleCount; i++)
            {
                double value = i switch
                {
                    0 => double.NaN,
                    1 => double.PositiveInfinity,
                    2 => double.NegativeInfinity,
                    3 => -0.0,
                    4 => double.Epsilon,
                    _ => RandomDouble(rng),
                };
                dto.l = BitConverter.DoubleToInt64Bits(value);
                long back = JsonUtility.FromJson<NumbersDto>(JsonUtility.ToJson(dto)).l;
                Assert.AreEqual(dto.l, back, "value " + value.ToString("R"));
            }
        }

        [Test]
        public void Float_RandomSample_RoundTripsBitwise()
        {
            var rng = new System.Random(Seed);
            var dto = new NumbersDto();
            for (int i = 0; i < SampleCount; i++)
            {
                dto.f = (float)((rng.NextDouble() - 0.5) * Math.Pow(10, rng.Next(-20, 20)));
                float back = JsonUtility.FromJson<NumbersDto>(JsonUtility.ToJson(dto)).f;
                Assert.AreEqual(BitConverter.SingleToInt32Bits(dto.f), BitConverter.SingleToInt32Bits(back), dto.f.ToString("R"));
            }
        }

        [Test]
        public void Long_And_ULong_RoundTrip_AtExtremes()
        {
            long[] longs = { long.MaxValue, long.MinValue, 1L << 53, (1L << 53) + 1, -1L };
            for (int i = 0; i < longs.Length; i++)
            {
                var json = JsonUtility.ToJson(new NumbersDto { l = longs[i] });
                Assert.AreEqual(longs[i], JsonUtility.FromJson<NumbersDto>(json).l, json);
            }

            ulong[] ulongs = { ulong.MaxValue, 0x853C49E6748FEA9BUL, 1UL << 63 }; // RNG state (Pcg32) is ulong
            for (int i = 0; i < ulongs.Length; i++)
            {
                var json = JsonUtility.ToJson(new ULongDto { u = ulongs[i] });
                Assert.AreEqual(ulongs[i], JsonUtility.FromJson<ULongDto>(json).u, json);
            }
        }

        [Test]
        public void NegativeZero_And_Subnormals_AreNotPreserved()
        {
            var json = JsonUtility.ToJson(new NumbersDto { d = -0.0, f = -0f });
            var back = JsonUtility.FromJson<NumbersDto>(json);
            Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(back.d), "-0.0 is written as 0.0: " + json);
            Assert.AreEqual(0, BitConverter.SingleToInt32Bits(back.f), "-0f is written as 0.0: " + json);

            json = JsonUtility.ToJson(new NumbersDto { d = double.Epsilon });
            StringAssert.Contains("5e-324", json);
            Assert.AreEqual(0.0, JsonUtility.FromJson<NumbersDto>(json).d, "subnormal is flushed to zero on read");
        }

        [Test]
        public void NonFiniteDouble_IsWrittenAsBareToken_WhichIsNotStandardJson()
        {
            double[] values = { double.NaN, double.PositiveInfinity, double.NegativeInfinity };
            string[] tokens = { "\"d\":NaN", "\"d\":Infinity", "\"d\":-Infinity" };
            for (int i = 0; i < values.Length; i++)
            {
                var json = JsonUtility.ToJson(new NumbersDto { d = values[i] });
                StringAssert.Contains(tokens[i], json);
                double back = JsonUtility.FromJson<NumbersDto>(json).d;
                Assert.IsTrue(back.Equals(values[i]), json); // NaN.Equals(NaN) is true; the NaN payload is not kept
            }
        }

        [Test]
        public void MissingField_KeepsDefault_SoRequiredFieldsNeedExplicitValidation()
        {
            var back = JsonUtility.FromJson<NumbersDto>("{\"f\":1.5}");
            Assert.AreEqual(0, back.schemaVersion, "JsonUtility has no 'required' concept: absent -> default(T)");
            Assert.AreEqual(1.5f, back.f);
        }
    }
}
