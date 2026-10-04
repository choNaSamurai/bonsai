using System;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// Builds a new shoot row by the rules of BALANCE §1.1 step 6.4 as amended by the bonsai-shape rules §1.2
    /// (slots / AttachT §1.2.1, taper ceiling §1.2.2, trunk S-curve §1.2.3, tier angles / droop / lengths §1.2.6).
    /// Shared by DailyStep and PlantSeed. Allocation-free.
    /// </summary>
    internal static class ShootBuilder
    {
        /// <summary>World angle of a horizontal shoot (geometry, not a tunable).</summary>
        const double HorizontalDeg = 90.0;

        enum Role : byte
        {
            TrunkSegment,
            CrownAxis,
            Continuation,
            Primary,
            Secondary,
        }

        public static NodeData Build(TreeGraph tree, int parentIndex, in ShootDraws u, GrowthConfig config, SpeciesConfig species)
        {
            ReadOnlySpan<NodeData> nodes = tree.Nodes;
            NodeData parent = nodes[parentIndex];
            TreeGenome genome = tree.Genome;
            int depth = parent.Depth + 1;

            int parentOrder = TreeShapeAnalysis.OrderOf(nodes, parentIndex);
            bool parentChain = parentOrder == 0 && parent.Depth < config.TrunkChainSegments;
            bool parentCrown = parentOrder == 0 && !parentChain;
            int parentOrderEff = parentCrown ? 1 : parentOrder;

            // Children of the parent: apical presence, occupied lateral slots, side of the newest lateral.
            double slotT0 = parentIndex == 0 ? config.TrunkBaseSlotT0 : config.LateralSlotT0;
            double slotT1 = parentIndex == 0 ? config.TrunkBaseSlotT1 : config.LateralSlotT1;
            double slotBoundary = 0.5 * (slotT0 + slotT1);
            bool hasApical = false;
            bool slot0Taken = false;
            sbyte newestLateralSide = 0;
            for (int i = parentIndex + 1; i < nodes.Length; i++)
            {
                if (nodes[i].ParentIndex != parentIndex) continue;
                if (nodes[i].BranchSide == 0)
                {
                    hasApical = true;
                    continue;
                }
                if (nodes[i].AttachT < slotBoundary) slot0Taken = true;
                newestLateralSide = nodes[i].BranchSide;
            }

            double parentWorld = tree.WorldAngle(parentIndex);
            double jitter = u.U2 + u.U3 - 1.0; // triangular in [−1, 1]
            bool apical = !hasApical;
            Role role;
            sbyte side = 0;
            double attachT = 1.0;
            double relative;
            int orderEff;

            if (apical)
            {
                double worldDeg;
                if (parentChain && depth < config.TrunkChainSegments)
                {
                    role = Role.TrunkSegment;
                    orderEff = 0;
                    worldDeg = TrunkSegmentWorldAngleDeg(depth, genome, config, species, jitter);
                }
                else if (parentChain || parentCrown)
                {
                    role = Role.CrownAxis;
                    // BALANCE §1.2: interpretation — crown axis has ordEff = 1, so its length gets OrderLengthFactor¹ (AC8).
                    orderEff = 1;
                    worldDeg = genome.BendBias * config.TrunkBendScaleDeg + config.ApicalAngleJitterDeg * jitter;
                }
                else
                {
                    role = Role.Continuation;
                    orderEff = parentOrderEff;
                    double parentDeg = parentWorld / GrowthFormulas.DegToRad;
                    worldDeg = parentDeg + (GrowthFormulas.SignNonZero(parentDeg) * HorizontalDeg - parentDeg) * GrowthFormulas.Clamp01(species.BranchDroop)
                        + config.ApicalAngleJitterDeg * jitter;
                }
                relative = worldDeg * GrowthFormulas.DegToRad - parentWorld;
            }
            else
            {
                // BALANCE §1.2: interpretation — first free slot; a lateral with AttachT below the slot midpoint occupies slot 0.
                int slot = slot0Taken ? 1 : 0;
                attachT = GrowthFormulas.Clamp((slot == 0 ? slotT0 : slotT1) + config.AttachTJitter * (2.0 * u.U8 - 1.0),
                    config.AttachTMin, config.AttachTMax);
                orderEff = parentOrder + 1;
                if (parentChain)
                {
                    role = Role.Primary;
                    side = (sbyte)(TreeShapeAnalysis.TierPhase(genome.Seed) * (slot == 0 ? -1 : 1));
                    double worldDeg = side * GrowthFormulas.Triangular(species.PrimaryAngleMeanDeg, species.PrimaryAngleSpreadDeg, u.U2, u.U3);
                    relative = worldDeg * GrowthFormulas.DegToRad - parentWorld;
                }
                else
                {
                    role = Role.Secondary;
                    side = newestLateralSide != 0 ? (sbyte)-newestLateralSide : u.U4 < 0.5 ? (sbyte)-1 : (sbyte)1;
                    relative = side * GrowthFormulas.Triangular(species.BranchAngleMeanDeg, species.BranchAngleSpreadDeg, u.U2, u.U3)
                        * GrowthFormulas.DegToRad;
                }
            }

            double angle = ClampToPotLip(parentWorld, relative, config);
            double world = parentWorld + angle;

            double curvature;
            switch (role)
            {
                case Role.TrunkSegment:
                    curvature = GrowthFormulas.Triangular(0.0, config.TrunkCurvatureJitter, u.U6, u.U7);
                    break;
                case Role.CrownAxis:
                    curvature = GrowthFormulas.Triangular(0.0, species.CurvatureSpread, u.U6, u.U7);
                    break;
                default:
                    // BALANCE §1.2: interpretation — the |θ| ≥ 45° test uses the world angle after the Pot Lip clamp.
                    bool arcUp = role == Role.Primary || Math.Abs(world) >= config.ArcUpMinWorldAngleDeg * GrowthFormulas.DegToRad;
                    double raw = GrowthFormulas.Triangular(0.0, species.CurvatureSpread, u.U6, u.U7);
                    // BALANCE §1.2.6: arc upwards = control points shifted along the CCW chord normal by −sgn(θ).
                    curvature = arcUp ? -GrowthFormulas.SignNonZero(world) * Math.Abs(raw) : raw;
                    break;
            }

            double targetLength = config.ShootBaseLength * Math.Pow(config.ShootLengthDecay, depth - 1)
                * (1.0 + config.ShootLengthJitter * (2.0 * u.U5 - 1.0));
            if (apical) targetLength *= config.ApicalLengthMultiplier;
            targetLength *= OrderLengthMultiplier(orderEff, config);

            return new NodeData
            {
                ParentIndex = parentIndex,
                Depth = (byte)depth,
                Length = config.ShootInitialLength,
                TargetLength = targetLength,
                Thickness = NewShootThickness(config.ShootInitialThickness, depth, parent.Thickness, attachT, apical, config),
                BaseAngle = angle,
                CurrentAngle = angle,
                Curvature = curvature,
                AgeDays = 0,
                Status = NodeStatus.Bud,
                LeafScale = 0f,
                BranchSide = side,
                AttachT = attachT,
            };
        }

        /// <summary>
        /// Sapling wild twig (BALANCE §1.2.8): lateral of #0 in slot 0, side −TierPhase, fixed AttachT and world angle,
        /// Curvature 0. Its 8 draws are consumed by the caller and unused.
        /// </summary>
        public static NodeData BuildSaplingTwig(TreeGraph tree, GrowthConfig config)
        {
            NodeData trunk = tree.Nodes[0];
            sbyte side = (sbyte)-TreeShapeAnalysis.TierPhase(tree.Genome.Seed);
            double parentWorld = tree.WorldAngle(0);
            double angle = ClampToPotLip(parentWorld, side * config.SaplingTwigWorldAngleDeg * GrowthFormulas.DegToRad - parentWorld, config);
            double attachT = GrowthFormulas.Clamp(config.SaplingTwigAttachT, 0.0, 1.0);
            return new NodeData
            {
                ParentIndex = 0,
                Depth = 1,
                Length = config.SaplingTwigLength,
                // BALANCE §1.2.8: interpretation — u5 is unused, so the twig's target length is the primary-branch
                // length of depth 1 without jitter: ShootBaseLength · OrderLengthFactor^1.
                TargetLength = config.ShootBaseLength * OrderLengthMultiplier(1, config),
                Thickness = Math.Min(config.SaplingTwigThickness,
                    Math.Min(GrowthFormulas.MaxThicknessAt(1, config), GrowthFormulas.ThicknessCap(trunk.Thickness, attachT, false, config))),
                BaseAngle = angle,
                CurrentAngle = angle,
                Curvature = 0.0,
                AgeDays = config.SaplingTwigAgeDays,
                Status = NodeStatus.Growing,
                LeafScale = 1f,
                BranchSide = side,
                AttachT = attachT,
            };
        }

        /// <summary>
        /// θ_k = lean + TierPhase·(−1)^k·A_k + TrunkSwayJitterDeg·jitter, A_k = TrunkSwayDeg·(1 + |bendBias|)·TrunkSwayDecay^(k−1)
        /// (BALANCE §1.2.3), degrees from vertical. <paramref name="jitter"/> = u2 + u3 − 1.
        /// </summary>
        public static double TrunkSegmentWorldAngleDeg(int k, in TreeGenome genome, GrowthConfig config, SpeciesConfig species, double jitter)
        {
            double lean = genome.BendBias * config.TrunkBendScaleDeg;
            double amplitude = species.TrunkSwayDeg * (1.0 + Math.Abs(genome.BendBias)) * Math.Pow(config.TrunkSwayDecay, k - 1);
            double alternation = (k & 1) == 0 ? 1.0 : -1.0;
            return lean + TreeShapeAnalysis.TierPhase(genome.Seed) * alternation * amplitude + config.TrunkSwayJitterDeg * jitter;
        }

        /// <summary>OrderLengthFactor^min(ordEff, 2) (BALANCE §1.2.6).</summary>
        public static double OrderLengthMultiplier(int orderEff, GrowthConfig config)
        {
            if (orderEff <= 0) return 1.0;
            return orderEff == 1 ? config.OrderLengthFactor : config.OrderLengthFactor * config.OrderLengthFactor;
        }

        /// <summary>max(MinShootThickness, min(initial, th_max(d), th_cap)) (BALANCE §1.2.2).</summary>
        public static double NewShootThickness(double initial, int depth, double parentThickness, double attachT, bool apical, GrowthConfig config)
        {
            double cap = GrowthFormulas.ThicknessCap(parentThickness, attachT, apical, config);
            double thickness = Math.Min(initial, Math.Min(GrowthFormulas.MaxThicknessAt(depth, config), cap));
            return thickness > config.MinShootThickness ? thickness : config.MinShootThickness;
        }

        /// <summary>
        /// Pot Lip by construction: the world angle (from vertical) of a new shoot is clamped to ±MaxShootWorldAngleDeg
        /// and the relative angle is recomputed.
        /// </summary>
        public static double ClampToPotLip(double parentWorldAngle, double relativeAngle, GrowthConfig config)
        {
            double maxWorld = config.MaxShootWorldAngleDeg * GrowthFormulas.DegToRad;
            double world = parentWorldAngle + relativeAngle;
            if (world > maxWorld) return maxWorld - parentWorldAngle;
            if (world < -maxWorld) return -maxWorld - parentWorldAngle;
            return relativeAngle;
        }
    }
}
