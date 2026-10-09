// PA-102X-S spike, C5: SketchGenerator.Scene must not allocate managed memory after warm-up (Chronicle slider calls it every frame).
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PocketArbor.Spike.TreeGen;
using UnityEngine.Profiling;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace PocketArbor.Spike.Tests
{
    public sealed class SketchGeneratorAllocTests
    {
        private const int Calls = 50;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private static string RunsDir
        {
            get
            {
                string d = Path.Combine(Path.GetDirectoryName(GoldenLoader.Dir.TrimEnd('/')) ?? ".", "runs");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        /// <summary>True when the runtime really counts per-thread allocations (checked with a known allocation).</summary>
        private static bool ThreadCounterWorks()
        {
            long a = GC.GetAllocatedBytesForCurrentThread();
            var probe = new byte[4096];
            long b = GC.GetAllocatedBytesForCurrentThread();
            GC.KeepAlive(probe);
            return b - a >= 4096;
        }

        private static void Warm(SketchPlan plan, string species, SketchScene reuse)
        {
            for (int i = 0; i <= 100; i++) SketchGenerator.Scene(plan, species, i / 100.0, SketchLayout.DefaultLight, reuse);
        }

        /// <summary><see cref="Calls"/> calls at t = 1 plus a 0..1 sweep of <see cref="Calls"/>+1 steps (2*Calls+1 calls).</summary>
        private static void Workload(SketchPlan plan, string species, SketchScene reuse)
        {
            for (int i = 0; i < Calls; i++) SketchGenerator.Scene(plan, species, 1, SketchLayout.DefaultLight, reuse);
            for (int i = 0; i <= Calls; i++) SketchGenerator.Scene(plan, species, i / (double)Calls, SketchLayout.DefaultLight, reuse);
        }

        /// <summary>
        /// Bytes allocated by <see cref="Workload"/>: per-thread counter when the runtime supports it (CoreCLR / SGen),
        /// otherwise (Unity editor Mono = Boehm, counter stays 0) the managed heap growth GC.GetTotalMemory — exact for 0 B,
        /// approximate otherwise (a collection inside the window can hide growth).
        /// </summary>
        private static long MeasureBytes(SketchPlan plan, string species, SketchScene reuse, bool threadCounter)
        {
            long before = threadCounter ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalMemory(false);
            Workload(plan, species, reuse);
            return (threadCounter ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalMemory(false)) - before;
        }

        /// <summary>Number of GC.Alloc profiler samples on this thread during the workload (Unity Recorder).</summary>
        private static int CountGcAllocSamples(SketchPlan plan, string species, SketchScene reuse)
        {
            Recorder rec = Recorder.Get("GC.Alloc");
            rec.FilterToCurrentThread();
            rec.enabled = false;
            rec.enabled = true;
            Workload(plan, species, reuse);
            rec.enabled = false;
            int n = rec.sampleBlockCount;
            rec.CollectFromAllThreads();
            return n;
        }

        [Test]
        public void Scene_KuromatsuT1AndAgeSweep_AfterWarmup_AllocatesZeroBytes()
        {
            SketchPlan plan = SketchGenerator.Build("moyogi", 3);
            var reuse = new SketchScene();
            Warm(plan, "kuromatsu", reuse);
            bool counter = ThreadCounterWorks();
            int samples = CountGcAllocSamples(plan, "kuromatsu", reuse);
            long bytes = MeasureBytes(plan, "kuromatsu", reuse, counter);
            const int calls = 2 * Calls + 1;
            string msg = "kuromatsu_moyogi_s3, " + calls + " Scene calls (t=1 x" + Calls + " + sweep 0..1): GC.Alloc samples = " + samples
                         + " (" + (samples / (double)calls).ToString("0.0", Inv) + " per call); bytes ("
                         + (counter ? "GetAllocatedBytesForCurrentThread" : "GC.GetTotalMemory delta") + ") = " + bytes + " ("
                         + (bytes / calls).ToString(Inv) + " B/call)";
            TestContext.WriteLine(msg);
            File.WriteAllText(Path.Combine(RunsDir, "c5_alloc.txt"), msg + "\n");

            // Unity's own constraint (GC.Alloc profiler samples). The delegate is invoked once first: the first call JIT-compiles
            // the lambda, and Mono's lazy ldstr/JIT bookkeeping would otherwise be counted as a GC.Alloc of the generator.
            string species = "kuromatsu";
            TestDelegate work = () => Workload(plan, species, reuse);
            work();
            Assert.That(work, Is.Not.AllocatingGCMemory(), msg);
            Assert.AreEqual(0, samples, msg);
            if (counter) Assert.That(bytes, NUnit.Framework.Is.LessThanOrEqualTo(0L), msg); // GetTotalMemory is informative only
        }

        [Test]
        public void Scene_AllSpeciesAndStylesAgeSweep_AfterWarmup_AllocatesZeroBytes()
        {
            var reuse = new SketchScene();
            var sb = new StringBuilder();
            long total = 0;
            bool counter = ThreadCounterWorks();
            foreach (string style in SketchSpecies.StyleKeys)
            {
                SketchPlan plan = SketchGenerator.Build(style, 27);
                foreach (string sp in SketchSpecies.SpeciesKeys)
                {
                    Warm(plan, sp, reuse);
                    int samples = CountGcAllocSamples(plan, sp, reuse);
                    long bytes = MeasureBytes(plan, sp, reuse, counter);
                    total += samples + (counter ? Math.Max(0, bytes) : 0);
                    sb.Append(sp).Append('_').Append(style).Append("_s27: GC.Alloc samples ").Append(samples).Append(", bytes ")
                        .Append(bytes).Append('\n');
                }
            }

            TestContext.WriteLine(sb.ToString());
            File.WriteAllText(Path.Combine(RunsDir, "c5_alloc_all.txt"), sb.ToString());
            Assert.That(total, NUnit.Framework.Is.LessThanOrEqualTo(0L), sb.ToString());
        }

        /// <summary>Informative: Scene() time (plan cached, buffers reused) — Kuromatsu t = 1 and a 0..1 sweep -> runs/c5_timing.txt.</summary>
        [Test]
        public void Scene_KuromatsuReusedBuffers_TimeMeasured()
        {
            var report = new StringBuilder();
            report.Append("config\tscene_ms_median_t1\tscene_ms_min_t1\tscene_ms_mean_sweep0to1\n");
            var reuse = new SketchScene();
            foreach (string style in new[] { "moyogi", "chokkan", "fukinagashi" })
            {
                SketchPlan plan = SketchGenerator.Build(style, 3);
                Warm(plan, "kuromatsu", reuse);
                var t1 = new double[31];
                for (int i = 0; i < t1.Length; i++)
                {
                    long a = Stopwatch.GetTimestamp();
                    SketchGenerator.Scene(plan, "kuromatsu", 1, SketchLayout.DefaultLight, reuse);
                    t1[i] = (Stopwatch.GetTimestamp() - a) * 1000.0 / Stopwatch.Frequency;
                }

                Array.Sort(t1);
                long s0 = Stopwatch.GetTimestamp();
                for (int i = 0; i <= 100; i++) SketchGenerator.Scene(plan, "kuromatsu", i / 100.0, SketchLayout.DefaultLight, reuse);
                double sweep = (Stopwatch.GetTimestamp() - s0) * 1000.0 / Stopwatch.Frequency / 101;
                report.Append("kuromatsu_").Append(style).Append("_s3\t").Append(t1[t1.Length / 2].ToString("0.000", Inv)).Append('\t')
                    .Append(t1[0].ToString("0.000", Inv)).Append('\t').Append(sweep.ToString("0.000", Inv)).Append('\n');
            }

            File.WriteAllText(Path.Combine(RunsDir, "c5_timing.txt"), report.ToString());
            TestContext.WriteLine(report.ToString());
            Assert.Pass(report.ToString());
        }
    }
}
