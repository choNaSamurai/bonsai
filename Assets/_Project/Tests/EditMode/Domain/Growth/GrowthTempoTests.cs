using System;
using System.Text;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>Monte-Carlo tempo check of BALANCE §1.1 (30 seeds, median, from the post-onboarding state N = 2).</summary>
    public sealed class GrowthTempoTests
    {
        const int Seeds = 30;
        const int MaxDays = 400;
        static readonly int[] Thresholds = { 16, 30, 50, 100, 200 };

        /// <summary>First day on which N ≥ threshold, per threshold.</summary>
        static int[] DaysToThresholds(uint seed, double vigor, double temperatureC)
        {
            var sim = GrowthTestKit.Simulator();
            var tree = GrowthTestKit.PostOnboarding(seed, SpeciesId.Kuromatsu, vigor);
            var days = new int[Thresholds.Length];
            for (int t = 0; t < days.Length; t++) days[t] = -1;
            for (int day = 1; day <= MaxDays; day++)
            {
                GrowthTestKit.RunDay(sim, tree, GrowthTestKit.WetMoisture, temperatureC);
                for (int t = 0; t < Thresholds.Length; t++)
                {
                    if (days[t] < 0 && tree.Count >= Thresholds[t]) days[t] = day;
                }
                if (tree.Count >= 200) break;
            }
            return days;
        }

        static double[] Medians(double vigor, double temperatureC, out int earliest200)
        {
            var samples = new int[Thresholds.Length][];
            for (int t = 0; t < samples.Length; t++) samples[t] = new int[Seeds];
            earliest200 = int.MaxValue;
            for (int s = 0; s < Seeds; s++)
            {
                int[] days = DaysToThresholds((uint)(s + 1), vigor, temperatureC);
                for (int t = 0; t < Thresholds.Length; t++) samples[t][s] = days[t] < 0 ? MaxDays + 1 : days[t];
                earliest200 = Math.Min(earliest200, samples[Thresholds.Length - 1][s]);
            }
            var medians = new double[Thresholds.Length];
            for (int t = 0; t < Thresholds.Length; t++)
            {
                Array.Sort(samples[t]);
                medians[t] = 0.5 * (samples[t][Seeds / 2 - 1] + samples[t][Seeds / 2]);
            }
            return medians;
        }

        static string Format(string label, double[] medians, int earliest200)
        {
            var sb = new StringBuilder(label);
            for (int t = 0; t < Thresholds.Length; t++) sb.Append(" N").Append(Thresholds[t]).Append("=D").Append(medians[t]);
            return sb.Append(" (earliest 200: D").Append(earliest200).Append(')').ToString();
        }

        // BALANCE §1.1 «Перевірка темпу»: median day to reach N = 16 / 30 / 50 / 100 / 200.
        [TestCase(1.0, 25.0, 14, 23, 31, 47, 81, TestName = "Tempo_G1Vigor1_MediansWithin15PercentOfBalanceTable")]
        [TestCase(0.5, 25.0, 14, 28, 41, 60, 94, TestName = "Tempo_G1Vigor05_MediansWithin15PercentOfBalanceTable")]
        [TestCase(1.5, 25.0, 13, 20, 27, 43, 77, TestName = "Tempo_G1Vigor15_MediansWithin15PercentOfBalanceTable")]
        [TestCase(1.0, 18.5, 19, 29, 39, 61, 106, TestName = "Tempo_G075Vigor1_MediansWithin15PercentOfBalanceTable")]
        [TestCase(1.0, 14.0, 27, 41, 56, 89, 156, TestName = "Tempo_G05Vigor1_MediansWithin15PercentOfBalanceTable")]
        public void Tempo_Scenario_MediansWithin15PercentOfBalanceTable(double vigor, double temperatureC,
            int d16, int d30, int d50, int d100, int d200)
        {
            int[] expected = { d16, d30, d50, d100, d200 };
            double[] medians = Medians(vigor, temperatureC, out int earliest200);
            TestContext.Out.WriteLine(Format("vigor=" + vigor + " T=" + temperatureC + ":", medians, earliest200));

            for (int t = 0; t < Thresholds.Length; t++)
            {
                Assert.AreEqual(expected[t], medians[t], expected[t] * 0.15, "N=" + Thresholds[t]);
            }
            Assert.GreaterOrEqual(earliest200, 66, "hard lower bound (200 − 2) / ShootsPerDayMax");
        }

        [Test]
        public void Tempo_FixtureSeed_MeetsBalanceAcceptanceCriteria()
        {
            // BALANCE §1.1 AC: N(D31) ≥ 50, N(D47) ≥ 100, N ≤ 200 and reaches 200 not before D66.
            var sim = GrowthTestKit.Simulator();
            var tree = GrowthTestKit.PostOnboarding(GrowthTestKit.FixtureSeed);
            int reached200 = -1;
            for (int day = 1; day <= 150; day++)
            {
                GrowthTestKit.RunDay(sim, tree, GrowthTestKit.WetMoisture, GrowthTestKit.OptimalTemperature);
                Assert.LessOrEqual(tree.Count, 200);
                if (day == 31) TestContext.Out.WriteLine("fixture N(D31) = " + tree.Count);
                if (day == 47) TestContext.Out.WriteLine("fixture N(D47) = " + tree.Count);
                if (day == 31) Assert.GreaterOrEqual(tree.Count, 50, "N(D31)");
                if (day == 47) Assert.GreaterOrEqual(tree.Count, 100, "N(D47)");
                if (reached200 < 0 && tree.Count == 200) reached200 = day;
            }
            TestContext.Out.WriteLine("fixture reached 200 on D" + reached200);
            Assert.GreaterOrEqual(reached200, 66);
        }
    }
}
