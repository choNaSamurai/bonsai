// PA-102X-S spike, G1: the C# port of the sketch generator against 90 golden configurations exported from sketch_v11.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PocketArbor.Spike.TreeGen;

namespace PocketArbor.Spike.Tests
{
    public sealed class SketchGeneratorGoldenTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static IEnumerable<string> GoldenKeys()
        {
            string[] keys = GoldenLoader.Keys();
            if (keys.Length == 0) yield return "missing-golden-data";
            foreach (string k in keys) yield return k;
        }

        private static string RunsDir
        {
            get
            {
                string d = Path.Combine(Path.GetDirectoryName(GoldenLoader.Dir.TrimEnd('/')) ?? ".", "runs");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        private static GoldenDiff Run(string key, out SketchPlan plan, out SketchScene scene)
        {
            GoldenData g = GoldenLoader.Load(key);
            SketchScene e = g.Scene;
            plan = SketchGenerator.Build(e.Style, e.Seed);
            scene = SketchGenerator.Scene(plan, e.SpeciesKey, e.T, e.LightDir);
            return GoldenCompare.Compare(g, plan, scene);
        }

        [TestCaseSource(nameof(GoldenKeys))]
        public void Scene_GoldenConfiguration_MatchesSketchWithinTolerance(string key)
        {
            if (key == "missing-golden-data") Assert.Ignore("Golden data not found in " + GoldenLoader.Dir);
            SketchPlan plan;
            SketchScene scene;
            GoldenDiff d = Run(key, out plan, out scene);
            TestContext.WriteLine(d.Summary());
            Assert.IsEmpty(d.CounterMismatches, "counters: " + string.Join(", ", d.CounterMismatches.ToArray()));
            foreach (ArrayDiff a in d.Arrays)
                Assert.IsTrue(a.Pass, key + " " + a.Name + ": " + a.Violations + " of " + a.Compared + " elements out of tolerance, first at "
                                      + a.FirstViolation + " expected " + a.FirstViolationExpected.ToString("R", Inv) + " actual "
                                      + a.FirstViolationActual.ToString("R", Inv));
        }

        /// <summary>Error table for the report: max |Δ| per array and configuration -> runs/g1_errors.tsv.</summary>
        [Test]
        public void Scene_AllGoldenConfigurations_ErrorTableWrittenAndAllPass()
        {
            string[] keys = GoldenLoader.Keys();
            if (keys.Length == 0) Assert.Ignore("Golden data not found in " + GoldenLoader.Dir);
            var sb = new StringBuilder();
            sb.Append("key\tpass\tcounters");
            foreach (string n in GoldenCompare.ArrayNames) sb.Append('\t').Append(n).Append("_maxAbs");
            foreach (string n in GoldenCompare.ArrayNames) sb.Append('\t').Append(n).Append("_n");
            sb.Append('\n');
            int passed = 0;
            var worst = new Dictionary<string, List<KeyValuePair<double, string>>>();
            foreach (string n in GoldenCompare.ArrayNames) worst[n] = new List<KeyValuePair<double, string>>();
            var failed = new List<string>();
            foreach (string key in keys)
            {
                SketchPlan plan;
                SketchScene scene;
                GoldenDiff d = Run(key, out plan, out scene);
                if (d.Pass) passed++;
                else failed.Add(d.Summary());
                sb.Append(key).Append('\t').Append(d.Pass ? "PASS" : "FAIL").Append('\t')
                    .Append(d.CounterMismatches.Count == 0 ? "ok" : string.Join(";", d.CounterMismatches.ToArray()));
                foreach (string n in GoldenCompare.ArrayNames)
                {
                    ArrayDiff a = d.Get(n);
                    sb.Append('\t').Append(a.MaxAbs.ToString("R", Inv));
                    worst[n].Add(new KeyValuePair<double, string>(a.MaxAbs, key + "@" + a.MaxAbsIndex));
                }

                foreach (string n in GoldenCompare.ArrayNames) sb.Append('\t').Append(d.Get(n).Compared);
                sb.Append('\n');
            }

            var summary = new StringBuilder();
            summary.Append("G1: ").Append(passed).Append('/').Append(keys.Length).Append(" configurations pass\n");
            foreach (string n in GoldenCompare.ArrayNames)
            {
                List<KeyValuePair<double, string>> w = worst[n];
                w.Sort((x, y) => y.Key.CompareTo(x.Key));
                summary.Append(n).Append(" worst:");
                for (int i = 0; i < Math.Min(5, w.Count); i++)
                    summary.Append(' ').Append(w[i].Value).Append('=').Append(w[i].Key.ToString("0.###E+0", Inv));
                summary.Append('\n');
            }

            foreach (string f in failed) summary.Append(f).Append('\n');
            File.WriteAllText(Path.Combine(RunsDir, "g1_errors.tsv"), sb.ToString());
            File.WriteAllText(Path.Combine(RunsDir, "g1_summary.txt"), summary.ToString());
            TestContext.WriteLine(summary.ToString());
            Assert.AreEqual(keys.Length, passed, summary.ToString());
        }

        /// <summary>Informative timing (no assertion): Build + Scene for Kuromatsu at t = 1 -> runs/g1_timing.txt.</summary>
        [Test]
        public void Scene_KuromatsuT1_GenerationTimeMeasured()
        {
            const int runs = 15;
            var report = new StringBuilder();
            report.Append("config\tleaves\textras\tbuild_ms_median\tscene_ms_median\ttotal_ms_median\ttotal_ms_min\n");
            var reuse = new SketchScene();
            // warm-up (JIT)
            SketchGenerator.Scene(SketchGenerator.Build("moyogi", 3), "kuromatsu", 1, SketchLayout.DefaultLight, reuse);
            foreach (string style in SketchSpecies.StyleKeys)
            {
                foreach (int seed in new[] { 3, 27 })
                {
                    var tb = new double[runs];
                    var ts = new double[runs];
                    var tt = new double[runs];
                    SketchScene s = null;
                    for (int i = 0; i < runs; i++)
                    {
                        long a = Stopwatch.GetTimestamp();
                        SketchPlan plan = SketchGenerator.Build(style, seed);
                        long b = Stopwatch.GetTimestamp();
                        s = SketchGenerator.Scene(plan, "kuromatsu", 1, SketchLayout.DefaultLight, reuse);
                        long c = Stopwatch.GetTimestamp();
                        tb[i] = (b - a) * 1000.0 / Stopwatch.Frequency;
                        ts[i] = (c - b) * 1000.0 / Stopwatch.Frequency;
                        tt[i] = (c - a) * 1000.0 / Stopwatch.Frequency;
                    }

                    Array.Sort(tb);
                    Array.Sort(ts);
                    Array.Sort(tt);
                    report.Append("kuromatsu_").Append(style).Append("_s").Append(seed).Append("_t100\t").Append(s.Nl).Append('\t')
                        .Append(s.Nx).Append('\t').Append(tb[runs / 2].ToString("0.00", Inv)).Append('\t')
                        .Append(ts[runs / 2].ToString("0.00", Inv)).Append('\t').Append(tt[runs / 2].ToString("0.00", Inv))
                        .Append('\t').Append(tt[0].ToString("0.00", Inv)).Append('\n');
                }
            }

            File.WriteAllText(Path.Combine(RunsDir, "g1_timing.txt"), report.ToString());
            TestContext.WriteLine(report.ToString());
            Assert.Pass(report.ToString());
        }
    }
}
