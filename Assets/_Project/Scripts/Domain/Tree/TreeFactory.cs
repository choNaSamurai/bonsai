using System;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Random;

namespace PocketArbor.Domain.Tree
{
    /// <summary>Planting (BALANCE §1.1 «Посадка», §1.2.8; PCG32 stream 0, exactly 18 draws).</summary>
    public static class TreeFactory
    {
        /// <summary>Uniform draws consumed by planting: 2 (trunk curvature) + 8 (leader) + 8 (wild twig).</summary>
        public const int PlantingDraws = 2 + 2 * ShootDraws.DrawsPerShoot;

        /// <summary>
        /// Starting sapling, N = 3: #0 trunk, #1 leader (trunk segment k = 1), #2 wild twig (lateral of the trunk in slot 0,
        /// AttachT 0.30; the onboarding cut target). Growth counters are 0; the tree starts frozen (onboarding Freeze Guard).
        /// </summary>
        public static TreeGraph PlantSapling(TreeGenome genome, GrowthConfig config, SpeciesConfig species) =>
            PlantSapling(genome, config, species, new Pcg32(genome.Seed, 0UL));

        /// <summary>Planting with an explicit stream-0 source (tests count the draws).</summary>
        internal static TreeGraph PlantSapling(TreeGenome genome, GrowthConfig config, SpeciesConfig species, IRandomSource rng)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (species == null) throw new ArgumentNullException(nameof(species));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            var tree = new TreeGraph(genome);
            tree.SetGrowthFrozen(true);

            // #0 trunk. BALANCE §1.2.8: Curvature = tri(0, TrunkCurvatureJitter), exactly 2 draws, before the shoots.
            double ua = ShootDraws.Uniform(rng);
            double ub = ShootDraws.Uniform(rng);
            // ⚑P BALANCE §1.3.1: the base of #0 leans opposite to k1 (lean + TierPhase·TrunkBaseSwayRatio·A_1), no RNG.
            // ⚑Q §1.4.7: + λ·TierPhase and σ on the base sway (ShapeStyleProfile).
            double trunkAngle = ShootBuilder.ClampToPotLip(0.0,
                ShootBuilder.TrunkBaseWorldAngleDeg(genome, config, species) * GrowthFormulas.DegToRad, config);
            tree.Append(new NodeData
            {
                ParentIndex = -1,
                Depth = 0,
                Length = config.TrunkInitialLength,
                TargetLength = config.TrunkTargetLength,
                Thickness = config.TrunkInitialThickness,
                BaseAngle = trunkAngle,
                CurrentAngle = trunkAngle,
                Curvature = GrowthFormulas.Triangular(0.0, config.TrunkCurvatureJitter, ua, ub) * ShapeParams.Resolve(config, species).JitterScale,
                Status = NodeStatus.Mature,
                LeafScale = 0f,
                AttachT = 1.0,
                LastCutDay = NodeData.NoCut,
            });

            // #1 leader: trunk segment k = 1 (8 draws), sapling overrides of length / thickness / age.
            ShootDraws leaderDraws = ShootDraws.Next(rng);
            NodeData leader = ShootBuilder.Build(tree, 0, in leaderDraws, config, species, applyApicalDelay: false);
            leader.Length = config.SaplingLeaderLength;
            // ⚑Q BALANCE §1.4.1(б): the leader is an ord-0 apical child of #0.
            ShapeParams shape = ShapeParams.Resolve(config, species);
            leader.Thickness = Math.Min(config.SaplingLeaderThickness,
                Math.Min(GrowthFormulas.MaxThicknessAt(leader.Depth, true, shape.TrunkChainDepthScaleFactor, config),
                    GrowthFormulas.AxisChildThicknessCap(config.TrunkInitialThickness, leader.AttachT, true, shape.TrunkChainTaperRatio,
                        shape.PrimaryThicknessRatio, config)));
            leader.Status = NodeStatus.Growing;
            leader.AgeDays = config.SaplingLeaderAgeDays;
            leader.LeafScale = 1f;
            tree.Append(leader);

            // #2 wild twig: 8 draws are consumed to keep the stream layout, none is used (BALANCE §1.2.8).
            ShootDraws.Next(rng);
            tree.Append(ShootBuilder.BuildSaplingTwig(tree, config));

            tree.MarkChanged();
            return tree;
        }
    }
}
