using System;
using NUnit.Framework;
using PocketArbor.Domain.Random;
using Unity.PerformanceTesting;

namespace PocketArbor.Performance.Tests
{
    public sealed class Pcg32PerformanceTests
    {
        const int Iterations = 10000;

        [Test, Performance]
        public void NextUInt_Throughput()
        {
            var rng = new Pcg32(1UL);
            Measure.Method(() =>
                {
                    for (int i = 0; i < Iterations; i++) rng.NextUInt();
                })
                .WarmupCount(3)
                .MeasurementCount(10)
                .GC()
                .Run();
        }

        [Test]
        public void NextUInt_AllocatesZeroBytes()
        {
            var rng = new Pcg32(1UL);
            uint sink = 0;
            for (int i = 0; i < Iterations; i++) sink ^= rng.NextUInt(); // warm-up / JIT

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < Iterations; i++)
            {
                sink ^= rng.NextUInt();
                sink ^= rng.NextUInt(13u);
                sink ^= (uint)(rng.NextDouble() * 1000.0);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.AreEqual(0L, allocated, "Pcg32 must be allocation-free (sink=" + sink + ")");
        }
    }
}
