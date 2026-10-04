using System;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Random;

namespace PocketArbor.Domain.Tree
{
    /// <summary>Planting (BALANCE §1.1 «Посадка», PCG32 stream 0).</summary>
    public static class TreeFactory
    {
        /// <summary>
        /// Starting sapling, N = 3: #0 trunk, #1 leader (apical child of the trunk), #2 wild twig (lateral child of the
        /// trunk; the onboarding cut target). Growth counters are 0; the tree starts frozen (onboarding Freeze Guard).
        /// </summary>
        public static TreeGraph PlantSapling(TreeGenome genome, GrowthConfig config, SpeciesConfig species)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (species == null) throw new ArgumentNullException(nameof(species));

            var tree = new TreeGraph(genome);
            tree.SetGrowthFrozen(true);
            var rng = new Pcg32(genome.Seed, 0UL);

            // #0 trunk. BALANCE §1.1: interpretation — trunk curvature consumes exactly 2 draws, before the shoots.
            double ua = ShootDraws.Uniform(rng);
            double ub = ShootDraws.Uniform(rng);
            double trunkAngle = ShootBuilder.ClampToPotLip(0.0, genome.BendBias * config.TrunkBendScaleDeg * GrowthFormulas.DegToRad, config);
            tree.Append(new NodeData
            {
                ParentIndex = -1,
                Depth = 0,
                Length = config.TrunkInitialLength,
                TargetLength = config.TrunkTargetLength,
                Thickness = config.TrunkInitialThickness,
                BaseAngle = trunkAngle,
                CurrentAngle = trunkAngle,
                Curvature = GrowthFormulas.Triangular(0.0, species.CurvatureSpread, ua, ub),
                Status = NodeStatus.Mature,
                LeafScale = 0f,
            });

            // #1 leader: apical child (7 draws), sapling overrides.
            ShootDraws leaderDraws = ShootDraws.Next(rng);
            NodeData leader = ShootBuilder.Build(tree, 0, in leaderDraws, config, species, genome.BendBias);
            leader.Length = config.SaplingLeaderLength;
            leader.Thickness = Math.Min(config.SaplingLeaderThickness, GrowthFormulas.MaxThicknessAt(leader.Depth, config));
            leader.Status = NodeStatus.Growing;
            leader.AgeDays = config.SaplingLeaderAgeDays;
            leader.LeafScale = 1f;
            tree.Append(leader);

            // #2 wild twig: lateral child (7 draws) at the extreme angle Mean + Spread.
            // BALANCE §1.1: interpretation — u2/u3 are drawn but unused (fixed angle); u4 picks the side, u5 length, u6/u7 curvature.
            ShootDraws twigDraws = ShootDraws.Next(rng);
            NodeData twig = ShootBuilder.Build(tree, 0, in twigDraws, config, species, genome.BendBias,
                species.BranchAngleMeanDeg + species.BranchAngleSpreadDeg);
            twig.Length = config.SaplingTwigLength;
            twig.Thickness = Math.Min(config.SaplingTwigThickness, GrowthFormulas.MaxThicknessAt(twig.Depth, config));
            twig.Status = NodeStatus.Growing;
            twig.AgeDays = config.SaplingTwigAgeDays;
            twig.LeafScale = 1f;
            tree.Append(twig);

            tree.MarkChanged();
            return tree;
        }
    }
}
