using System;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// Builds a new shoot row by the rules of BALANCE §1.1 step 6.4 as amended by the bonsai-shape rules §1.2
    /// (slots / AttachT §1.2.1, taper ceiling §1.2.2, trunk S-curve §1.2.3, tier angles / droop / lengths §1.2.6) and the
    /// reference-shape rules §1.3 (trunk lengths, apical delay, tier sides and slot order, apex dome, branch continuations).
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

        /// <param name="applyApicalDelay">False only for planting: the sapling leader is the apical child of #0 by definition (§1.2.8).</param>
        public static NodeData Build(TreeGraph tree, int parentIndex, in ShootDraws u, GrowthConfig config, SpeciesConfig species,
            bool applyApicalDelay = true)
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
            bool slot1Taken = false;
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
                else slot1Taken = true;
                newestLateralSide = nodes[i].BranchSide;
            }

            double parentWorld = tree.WorldAngle(parentIndex);
            double jitter = u.U2 + u.U3 - 1.0; // triangular in [−1, 1]
            // ⚑P BALANCE §1.3.1: an ord-0 node (chain or crown axis) takes an apical child only after its apical delay.
            bool apical = !hasApical && !(applyApicalDelay && parentOrder == 0 && IsApicalDelayed(parent, config));
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
                    // BALANCE §1.2: interpretation — crown axis has ordEff = 1 (TD-022); its length factor is §1.3.1 CrownAxisLengthFactor.
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
                // ⚑P BALANCE §1.3.2: a trunk-chain parent fills slot 1 (the tier on the outer side of the bend) before slot 0;
                // other parents — slot 0 first (§1.2.1). A lateral with AttachT below the slot midpoint occupies slot 0.
                int slot = parentChain ? (slot1Taken ? 0 : 1) : (slot0Taken ? 1 : 0);
                attachT = GrowthFormulas.Clamp((slot == 0 ? slotT0 : slotT1) + config.AttachTJitter * (2.0 * u.U8 - 1.0),
                    config.AttachTMin, config.AttachTMax);
                orderEff = parentOrder + 1;
                if (parentChain)
                {
                    role = Role.Primary;
                    // ⚑P BALANCE §1.3.2: slot 1 on s_k = TierPhase·(−1)^k (where segment k leans), slot 0 on −s_k.
                    int tierSide = TierSide(parent.Depth, genome.Seed);
                    side = (sbyte)(slot == 1 ? tierSide : -tierSide);
                    // §1.3.2 apex dome: laterals of the chain top (k = TrunkChainSegments − 1) before the crown axis grows.
                    bool dome = !hasApical && parent.Depth == config.TrunkChainSegments - 1;
                    double mean = dome ? species.CrownLateralAngleMeanDeg : species.PrimaryAngleMeanDeg;
                    double spread = dome ? species.CrownLateralAngleSpreadDeg : species.PrimaryAngleSpreadDeg;
                    double worldDeg = side * GrowthFormulas.Triangular(mean, spread, u.U2, u.U3);
                    relative = worldDeg * GrowthFormulas.DegToRad - parentWorld;
                }
                else
                {
                    role = Role.Secondary;
                    side = newestLateralSide != 0 ? (sbyte)-newestLateralSide : u.U4 < 0.5 ? (sbyte)-1 : (sbyte)1;
                    if (parentCrown)
                    {
                        // ⚑P BALANCE §1.3.2: laterals of the crown axis take a world angle (apex dome), not a relative one.
                        double worldDeg = side * GrowthFormulas.Triangular(species.CrownLateralAngleMeanDeg, species.CrownLateralAngleSpreadDeg, u.U2, u.U3);
                        relative = worldDeg * GrowthFormulas.DegToRad - parentWorld;
                    }
                    else
                    {
                        relative = side * GrowthFormulas.Triangular(species.BranchAngleMeanDeg, species.BranchAngleSpreadDeg, u.U2, u.U3)
                            * GrowthFormulas.DegToRad;
                    }
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

            double targetLength = TargetLength(role, depth, orderEff, u.U5, config);

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
                LastCutDay = NodeData.NoCut,
            };
        }

        /// <summary>
        /// TargetLength by role (BALANCE §1.1 step 6.4, §1.2.6, ⚑P §1.3.1 / §1.3.4), <paramref name="u5"/> = the length draw:
        /// trunk segment k ≥ 1 — TrunkSegmentLength·TrunkSegmentDecay^(k−1)·(1 + TrunkSegmentJitter·(2u5 − 1));
        /// crown axis — §1.1 base × ApicalLengthMultiplier × CrownAxisLengthFactor;
        /// branch continuation — §1.1 base × BranchApicalLengthMultiplier × OrderLengthFactor^min(ordEff, 2);
        /// laterals — §1.1 base × OrderLengthFactor^min(ordEff, 2).
        /// </summary>
        static double TargetLength(Role role, int depth, int orderEff, double u5, GrowthConfig config)
        {
            if (role == Role.TrunkSegment)
            {
                return config.TrunkSegmentLength * Math.Pow(config.TrunkSegmentDecay, depth - 1)
                    * (1.0 + config.TrunkSegmentJitter * (2.0 * u5 - 1.0));
            }
            double length = config.ShootBaseLength * Math.Pow(config.ShootLengthDecay, depth - 1)
                * (1.0 + config.ShootLengthJitter * (2.0 * u5 - 1.0));
            switch (role)
            {
                case Role.CrownAxis: return length * config.ApicalLengthMultiplier * config.CrownAxisLengthFactor;
                case Role.Continuation: return length * config.BranchApicalLengthMultiplier * OrderLengthMultiplier(orderEff, config);
                default: return length * OrderLengthMultiplier(orderEff, config);
            }
        }

        /// <summary>⚑P BALANCE §1.3.1: AgeDays of an ord-0 node of index k = Depth is below TrunkApicalDelayBaseDays + StepDays·k.</summary>
        public static bool IsApicalDelayed(in NodeData node, GrowthConfig config) =>
            node.AgeDays < ApicalDelayDays(node.Depth, config);

        public static int ApicalDelayDays(int k, GrowthConfig config) =>
            config.TrunkApicalDelayBaseDays + config.TrunkApicalDelayStepDays * k;

        /// <summary>⚑P BALANCE §1.3.2: s_k = TierPhase·(−1)^k — side of the slot-1 tier of chain node k (the outer side of the bend).</summary>
        public static int TierSide(int k, uint seed) => TreeShapeAnalysis.TierPhase(seed) * ((k & 1) == 0 ? 1 : -1);

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
                LastCutDay = NodeData.NoCut,
            };
        }

        /// <summary>
        /// θ_k = lean + TierPhase·(−1)^k·A_k + TrunkSwayJitterDeg·jitter (BALANCE §1.2.3 with ⚑P §1.3.1 numbers),
        /// degrees from vertical. <paramref name="jitter"/> = u2 + u3 − 1.
        /// </summary>
        public static double TrunkSegmentWorldAngleDeg(int k, in TreeGenome genome, GrowthConfig config, SpeciesConfig species, double jitter)
        {
            double lean = genome.BendBias * config.TrunkBendScaleDeg;
            double alternation = (k & 1) == 0 ? 1.0 : -1.0;
            return lean + TreeShapeAnalysis.TierPhase(genome.Seed) * alternation * TrunkSwayAmplitudeDeg(k, genome, config, species)
                + config.TrunkSwayJitterDeg * jitter;
        }

        /// <summary>⚑P BALANCE §1.3.1: A_k = TrunkSwayDeg·(1 + TrunkSwayBendGain·|bendBias|)·TrunkSwayDecay^(k−1).</summary>
        public static double TrunkSwayAmplitudeDeg(int k, in TreeGenome genome, GrowthConfig config, SpeciesConfig species) =>
            species.TrunkSwayDeg * (1.0 + config.TrunkSwayBendGain * Math.Abs(genome.BendBias)) * Math.Pow(config.TrunkSwayDecay, k - 1);

        /// <summary>⚑P BALANCE §1.3.1: world angle of #0 = lean + TierPhase·TrunkBaseSwayRatio·A_1 (degrees, before the Pot Lip clamp).</summary>
        public static double TrunkBaseWorldAngleDeg(in TreeGenome genome, GrowthConfig config, SpeciesConfig species) =>
            genome.BendBias * config.TrunkBendScaleDeg
            + TreeShapeAnalysis.TierPhase(genome.Seed) * config.TrunkBaseSwayRatio * TrunkSwayAmplitudeDeg(1, genome, config, species);

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
