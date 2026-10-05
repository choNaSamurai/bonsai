using System;
using PocketArbor.Domain.Random;
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
            /// <summary>⚑T BALANCE §1.6.2(б): slot 0 of the chain top — a dome lateral (#7).</summary>
            CrownShoulder,
        }

        /// <param name="applyApicalDelay">False only for planting: the sapling leader is the apical child of #0 by definition (§1.2.8).</param>
        public static NodeData Build(TreeGraph tree, int parentIndex, in ShootDraws u, GrowthConfig config, SpeciesConfig species,
            bool applyApicalDelay = true)
        {
            ShapeParams shape = ShapeParams.Resolve(config, species);
            return Build(tree, parentIndex, in u, config, species, in shape, applyApicalDelay);
        }

        /// <summary>Same as above with the shape parameters resolved once by the caller (DailyStep — once per day, ADR-0010).</summary>
        public static NodeData Build(TreeGraph tree, int parentIndex, in ShootDraws u, GrowthConfig config, SpeciesConfig species, in ShapeParams shape,
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
            // ⚑Q BALANCE §1.4.4 lever: crown-axis laterals have their own slot 0 (default = LateralSlotT0).
            double slotT0 = parentIndex == 0 ? config.TrunkBaseSlotT0 : parentCrown ? config.CrownLateralSlotT0 : config.LateralSlotT0;
            double slotT1 = parentIndex == 0 ? config.TrunkBaseSlotT1 : config.LateralSlotT1;
            double slotBoundary = 0.5 * (slotT0 + slotT1);
            // ⚑T BALANCE §1.6.1(а): a chain lateral's slot is its side (slot 1 on s_k, slot 0 on −s_k) — identical to the AttachT midpoint for
            // every PA-102R tree, and exact for the crown shoulder (slot 0 at AttachT 0.95).
            int chainTierSide = parentChain ? TierSide(parent.Depth, genome.Seed) : 0; // == TreeShapeAnalysis.ChainLateralSlot side
            bool hasApical = false;
            bool slot0Taken = false;
            bool slot1Taken = false;
            int laterals = 0;
            sbyte newestLateralSide = 0;
            for (int i = parentIndex + 1; i < nodes.Length; i++)
            {
                if (nodes[i].ParentIndex != parentIndex) continue;
                if (nodes[i].BranchSide == 0)
                {
                    hasApical = true;
                    continue;
                }
                bool slot0 = parentChain ? TreeShapeAnalysis.ChainLateralSlot(nodes[i].BranchSide, parent.Depth, genome.Seed) == 0
                    : nodes[i].AttachT < slotBoundary;
                if (slot0) slot0Taken = true;
                else slot1Taken = true;
                laterals++;
                newestLateralSide = nodes[i].BranchSide;
            }

            double parentWorld = tree.WorldAngle(parentIndex);
            double jitter = u.U2 + u.U3 - 1.0; // triangular in [−1, 1]
            // ⚑P BALANCE §1.3.1: an ord-0 node (chain or crown axis) takes an apical child only after its apical delay.
            // ⚑T §1.6.2(а): a crown-axis node — only once it has CrownApicalNeedsLaterals live laterals; §1.6.1(в): an ord ≥ 1 node with a fresh
            // cut (pinch) — never (laterals only).
            bool apical = !hasApical && !MustBeLateral(in parent, parentOrder, parentCrown, laterals, tree.Growth.GrowthDayIndex, config, applyApicalDelay);
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
                    worldDeg = TrunkSegmentWorldAngleDeg(depth, genome, in shape, config, jitter);
                }
                else if (parentChain || parentCrown)
                {
                    role = Role.CrownAxis;
                    // BALANCE §1.2: interpretation — crown axis has ordEff = 1 (TD-022); its length factor is §1.3.1 CrownAxisLengthFactor.
                    orderEff = 1;
                    // ⚑T §1.6.2(в): + −s_top·CrownAxisLeanDeg·σ (towards the shoulder, against the last bend of the chain).
                    worldDeg = genome.BendBias * config.TrunkBendScaleDeg + config.ApicalAngleJitterDeg * jitter
                        + CrownAxisLeanDeg(genome.Seed, in shape, config);
                }
                else
                {
                    role = Role.Continuation;
                    orderEff = parentOrderEff;
                    double parentDeg = parentWorld / GrowthFormulas.DegToRad;
                    worldDeg = parentDeg + (GrowthFormulas.SignNonZero(parentDeg) * HorizontalDeg - parentDeg) * GrowthFormulas.Clamp01(shape.BranchDroop)
                        + config.ApicalAngleJitterDeg * jitter;
                }
                relative = worldDeg * GrowthFormulas.DegToRad - parentWorld;
            }
            else
            {
                // ⚑P BALANCE §1.3.2: a trunk-chain parent fills slot 1 (the tier on the outer side of the bend) before slot 0;
                // other parents — slot 0 first (§1.2.1). A lateral with AttachT below the slot midpoint occupies slot 0.
                int slot = parentChain ? (slot1Taken ? 0 : 1) : (slot0Taken ? 1 : 0);
                // ⚑T §1.6.2(б): slot 0 of the chain top is the crown shoulder (AttachT CrownShoulderSlotT).
                bool shoulder = parentChain && slot == 0 && IsShoulderSlot(parent.Depth, config);
                double slotT = shoulder ? config.CrownShoulderSlotT : slot == 0 ? slotT0 : slotT1;
                attachT = GrowthFormulas.Clamp(slotT + config.AttachTJitter * (2.0 * u.U8 - 1.0), config.AttachTMin, config.AttachTMax);
                orderEff = parentOrder + 1;
                if (shoulder)
                {
                    // A dome lateral on −s_k (opposite the k-top tier #6): world angle CrownLateralAngle*, length × CrownLateralLengthFactor.
                    role = Role.CrownShoulder;
                    side = (sbyte)-chainTierSide;
                    double worldDeg = side * GrowthFormulas.Triangular(shape.CrownLateralAngleMeanDeg, shape.CrownLateralAngleSpreadDeg, u.U2, u.U3);
                    relative = worldDeg * GrowthFormulas.DegToRad - parentWorld;
                }
                else if (parentChain)
                {
                    role = Role.Primary;
                    // ⚑P BALANCE §1.3.2: slot 1 on s_k = TierPhase·(−1)^k (where segment k leans), slot 0 on −s_k.
                    int tierSide = TierSide(parent.Depth, genome.Seed);
                    side = (sbyte)(slot == 1 ? tierSide : -tierSide);
                    // §1.3.2 apex dome: laterals of the chain top (k = TrunkChainSegments − 1) before the crown axis grows.
                    bool dome = !hasApical && parent.Depth == config.TrunkChainSegments - 1;
                    double mean = dome ? shape.CrownLateralAngleMeanDeg : shape.PrimaryAngleMeanDeg;
                    double spread = dome ? shape.CrownLateralAngleSpreadDeg : shape.PrimaryAngleSpreadDeg;
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
                        double worldDeg = side * GrowthFormulas.Triangular(shape.CrownLateralAngleMeanDeg, shape.CrownLateralAngleSpreadDeg, u.U2, u.U3);
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
                    // ⚑Q BALANCE §1.4.7: chain Curvature × max(σ, TrunkSwayJitterMinScale) (× 1 for Moyogi).
                    // ⚑R §1.5.4(а): + the bow TierPhase·(−1)^k·TrunkBowCurvature·σ (no RNG; same u6, u7).
                    curvature = TrunkBowCurvature(depth, genome.Seed, in shape)
                        + GrowthFormulas.Triangular(0.0, config.TrunkCurvatureJitter, u.U6, u.U7) * shape.JitterScale;
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

            double axisFactor = AxisFactor(nodes, parentIndex, role, parentChain, parentCrown, parentOrder, config, in shape, genome.Seed, out int padTier);
            double targetLength = TargetLength(role, depth, orderEff, u.U5, config, in shape, axisFactor, padTier);
            // ⚑R BALANCE §1.5.4(б): the first node of a primary is shortened / lengthened by the S-offset of its attachment.
            if (role == Role.Primary) targetLength = ReachCompensatedLength(tree, parentIndex, attachT, side, world, targetLength, config, in shape);

            return new NodeData
            {
                ParentIndex = parentIndex,
                Depth = (byte)depth,
                Length = config.ShootInitialLength,
                TargetLength = targetLength,
                Thickness = NewShootThickness(config.ShootInitialThickness, depth, parent.Thickness, attachT, apical, parentOrder == 0, in shape, config),
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
        /// <remarks>⚑Q BALANCE §1.4.3 / §1.4.4: <paramref name="axisFactor"/> = PrimaryLengthFactor (ord-1 nodes of a primary axis),
        /// CrownLateralLengthFactor (crown-axis laterals and their continuations) or 1, multiplied last.
        /// ⚑R §1.5.2(а): <paramref name="padTier"/> = k_t ≥ 0 for a node of a primary's subtree other than the primary's first node — the
        /// decay exponent becomes (d − 1) − PadTierDecayCompensation·k_t and the length × PadLengthFactor; −1 = unchanged.</remarks>
        static double TargetLength(Role role, int depth, int orderEff, double u5, GrowthConfig config, in ShapeParams shape, double axisFactor,
            int padTier)
        {
            if (role == Role.TrunkSegment)
            {
                return shape.TrunkSegmentLength * Math.Pow(shape.TrunkSegmentDecay, depth - 1)
                    * (1.0 + config.TrunkSegmentJitter * (2.0 * u5 - 1.0));
            }
            bool pad = padTier >= 0 && role != Role.CrownAxis;
            double exponent = pad ? depth - 1 - shape.PadTierDecayCompensation * padTier : depth - 1;
            double length = config.ShootBaseLength * Math.Pow(config.ShootLengthDecay, exponent)
                * (1.0 + config.ShootLengthJitter * (2.0 * u5 - 1.0));
            if (pad) length *= shape.PadLengthFactor;
            switch (role)
            {
                case Role.CrownAxis: return length * config.ApicalLengthMultiplier * shape.CrownAxisLengthFactor;
                case Role.Continuation: return length * config.BranchApicalLengthMultiplier * OrderLengthMultiplier(orderEff, config) * axisFactor;
                default: return length * OrderLengthMultiplier(orderEff, config) * axisFactor;
            }
        }

        /// <summary>
        /// ⚑Q BALANCE §1.4.3 / §1.4.4 length factor of the new shoot's axis: a primary (lateral of a chain node) and the apical
        /// continuations of an ord-1 axis rooted on the chain — PrimaryLengthFactor; a crown-axis lateral and the continuations of an
        /// ord-1 axis rooted on the crown axis — CrownLateralLengthFactor; otherwise 1. No RNG, O(depth), allocation-free.
        /// </summary>
        static double AxisFactor(ReadOnlySpan<NodeData> nodes, int parentIndex, Role role, bool parentChain, bool parentCrown, int parentOrder,
            GrowthConfig config, in ShapeParams shape, uint seed, out int padTier)
        {
            padTier = -1;
            if (role == Role.Primary) return shape.PrimaryLengthFactor;
            if (role == Role.CrownShoulder) return shape.CrownLateralLengthFactor;
            if (role == Role.TrunkSegment || role == Role.CrownAxis || parentOrder < 1) return parentCrown && role == Role.Secondary
                ? shape.CrownLateralLengthFactor : 1.0;
            // ⚑R §1.5.2(а): the parent lies in an ord ≥ 1 subtree; its topmost lateral (the ord-1 axis root) sits on an ord-0 node —
            // a chain node k_t (a primary's subtree) or the crown axis (the dome: unchanged). O(depth), no allocation.
            int ord1Root = -1;
            for (int i = parentIndex; i > 0; i = nodes[i].ParentIndex)
            {
                if (nodes[i].BranchSide != 0) ord1Root = i;
            }
            int axisParent = ord1Root > 0 ? nodes[ord1Root].ParentIndex : -1;
            bool chainRooted = axisParent >= 0 && nodes[axisParent].Depth < config.TrunkChainSegments;
            // ⚑T §1.6.2(б): the crown shoulder's subtree is the dome — like a crown-axis lateral (no pad-tier compensation).
            if (chainRooted && IsShoulderSlot(nodes[axisParent].Depth, config)
                && nodes[ord1Root].BranchSide == -TierSide(nodes[axisParent].Depth, seed))
            {
                return role == Role.Secondary || parentOrder != 1 ? 1.0 : shape.CrownLateralLengthFactor;
            }
            if (chainRooted) padTier = nodes[axisParent].Depth;
            if (role == Role.Secondary || parentOrder != 1 || axisParent < 0) return 1.0;
            // The parent of an ord-1 axis root is an ord-0 node: chain (Depth < segments) or crown axis.
            return chainRooted ? shape.PrimaryLengthFactor : shape.CrownLateralLengthFactor;
        }

        /// <summary>
        /// ⚑R BALANCE §1.5.4(б) reach compensation of a primary's first node:
        /// L' = clamp(L − g·o / max(|sin θ_p|, PrimaryReachSinMin), PrimaryReachMinRatio·L, PrimaryReachMaxRatio·L),
        /// o = side_screen·(x_attach − x_lean(y_attach)), side_screen = −side (a lateral of side s points to x sign −s);
        /// (x, y)_attach from the TargetLength and world angles of the chain below (AttachT, no Curvature);
        /// x_lean(y) = −y·tan(lean + λ·TierPhase) — the straight leaning line without the S (the Shakan lean is not compensated).
        /// No RNG, O(depth), allocation-free.
        /// </summary>
        public static double ReachCompensatedLength(TreeGraph tree, int parentIndex, double attachT, int side, double worldAngle, double length,
            GrowthConfig config, in ShapeParams shape)
        {
            double g = shape.PrimaryReachCompensation;
            if (!(g > 0.0) || side == 0) return length;
            ReadOnlySpan<NodeData> nodes = tree.Nodes;
            // Walk down the chain from the parent: the attachment point = AttachT·TL(parent)·dir(parent) + Σ TL(a)·dir(a) over the ancestors.
            double world = tree.WorldAngle(parentIndex);
            double x = -attachT * nodes[parentIndex].TargetLength * Math.Sin(world);
            double y = attachT * nodes[parentIndex].TargetLength * Math.Cos(world);
            for (int i = parentIndex; nodes[i].ParentIndex >= 0;)
            {
                world -= nodes[i].CurrentAngle;
                i = nodes[i].ParentIndex;
                x -= nodes[i].TargetLength * Math.Sin(world);
                y += nodes[i].TargetLength * Math.Cos(world);
            }
            TreeGenome genome = tree.Genome;
            double leanDeg = genome.BendBias * config.TrunkBendScaleDeg + shape.LeanOffsetDeg * TreeShapeAnalysis.TierPhase(genome.Seed);
            double xLean = -y * Math.Tan(leanDeg * GrowthFormulas.DegToRad);
            double offset = -side * (x - xLean);
            double sin = Math.Abs(Math.Sin(worldAngle));
            if (!(sin > config.PrimaryReachSinMin)) sin = config.PrimaryReachSinMin;
            double compensated = length - g * offset / sin;
            return GrowthFormulas.Clamp(compensated, config.PrimaryReachMinRatio * length, config.PrimaryReachMaxRatio * length);
        }

        /// <summary>⚑R BALANCE §1.5.4(а): bow of chain segment k = TierPhase·(−1)^k·TrunkBowCurvature·σ (positive Curvature bulges to the +side,
        /// i.e. towards the side segment k leans to — the outer side of its top bend).</summary>
        public static double TrunkBowCurvature(int k, uint seed, in ShapeParams shape) =>
            TreeShapeAnalysis.TierPhase(seed) * ((k & 1) == 0 ? 1.0 : -1.0) * shape.TrunkBowCurvature * shape.SwayScale;

        /// <summary>
        /// TD-066: one predicate of the shoot type — a node without an apical child takes only a lateral when (ord 0) its apical delay lasts
        /// (⚑P §1.3.1; skipped for planting), (crown axis) it has fewer than CrownApicalNeedsLaterals laterals (⚑T §1.6.2 а), or (ord ≥ 1) it is
        /// pinched (⚑T §1.6.1 в). Used by GrowthSimulator (roles / weight group) and Build (apical or lateral).
        /// </summary>
        public static bool MustBeLateral(in NodeData node, int order, bool crownAxis, int laterals, int growthDayIndex, GrowthConfig config,
            bool applyApicalDelay = true)
        {
            if (order >= 1) return IsPinched(in node, growthDayIndex, config);
            if (applyApicalDelay && IsApicalDelayed(in node, config)) return true;
            return crownAxis && laterals < config.CrownApicalNeedsLaterals;
        }

        /// <summary>⚑T BALANCE §1.6.1(в): pinch — GrowthConfig.PinchLateralOnly ∧ a fresh cut at the node (GrowthDayIndex − LastCutDay &lt; BackBudDays).
        /// The caller checks ord ≥ 1 and the missing apical child.</summary>
        public static bool IsPinched(in NodeData node, int growthDayIndex, GrowthConfig config) =>
            config.PinchLateralOnly && node.LastCutDay >= 0 && growthDayIndex - node.LastCutDay < config.BackBudDays;

        /// <summary>⚑T §1.6.2(б): slot 0 of chain node <paramref name="k"/> is the crown shoulder (the chain top with CrownShoulderSlotT &gt; 0).</summary>
        public static bool IsShoulderSlot(int k, GrowthConfig config) => config.CrownShoulderSlotT > 0.0 && k == config.TrunkChainSegments - 1;

        /// <summary>⚑T §1.6.2(в): −s_top·CrownAxisLeanDeg·σ, s_top = TierSide(TrunkChainSegments − 1) (degrees, + = counter-clockwise).</summary>
        public static double CrownAxisLeanDeg(uint seed, in ShapeParams shape, GrowthConfig config) =>
            -TierSide(config.TrunkChainSegments - 1, seed) * shape.CrownAxisLeanDeg * shape.SwayScale;

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
                // ⚑Q §1.4.3: a lateral of #0 (ord 0) — PrimaryThicknessRatio ceiling.
                Thickness = Math.Min(config.SaplingTwigThickness,
                    Math.Min(GrowthFormulas.MaxThicknessAt(1, config), GrowthFormulas.AxisChildThicknessCap(trunk.Thickness, attachT, false,
                        config.TrunkChainTaperRatio, config.PrimaryThicknessRatio, config))),
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
        /// ⚑Q BALANCE §1.4.7: θ_k = lean + λ·TierPhase + TierPhase·(−1)^k·A_k·σ + TrunkSwayJitterDeg·max(σ, jitterMin)·jitter,
        /// degrees from vertical (§1.3.1 at σ = 1, λ = 0). <paramref name="jitter"/> = u2 + u3 − 1.
        /// </summary>
        public static double TrunkSegmentWorldAngleDeg(int k, in TreeGenome genome, GrowthConfig config, SpeciesConfig species, double jitter)
        {
            ShapeParams shape = ShapeParams.Resolve(config, species);
            return TrunkSegmentWorldAngleDeg(k, genome, in shape, config, jitter);
        }

        public static double TrunkSegmentWorldAngleDeg(int k, in TreeGenome genome, in ShapeParams shape, GrowthConfig config, double jitter)
        {
            int tierPhase = TreeShapeAnalysis.TierPhase(genome.Seed);
            double lean = genome.BendBias * config.TrunkBendScaleDeg + shape.LeanOffsetDeg * tierPhase;
            double alternation = (k & 1) == 0 ? 1.0 : -1.0;
            return lean + tierPhase * alternation * TrunkSwayAmplitudeDeg(k, genome, in shape, config) * shape.SwayScale
                + config.TrunkSwayJitterDeg * shape.JitterScale * jitter;
        }

        /// <summary>⚑R BALANCE §1.5.4(а): A_k = TrunkSwayDeg·(1 + TrunkSwayBendGain·|bendBias|)·TrunkSwayProfile[k]·(1 + TrunkSwayIrregularity·h_k)
        /// (before σ), h_k = 2·hash01(randomSeed, k, "SWAY") − 1 (<see cref="StableHash"/>, not the PCG stream).</summary>
        public static double TrunkSwayAmplitudeDeg(int k, in TreeGenome genome, GrowthConfig config, SpeciesConfig species)
        {
            ShapeParams shape = ShapeParams.Resolve(config, species);
            return TrunkSwayAmplitudeDeg(k, genome, in shape, config);
        }

        public static double TrunkSwayAmplitudeDeg(int k, in TreeGenome genome, in ShapeParams shape, GrowthConfig config) =>
            shape.TrunkSwayDeg * (1.0 + config.TrunkSwayBendGain * Math.Abs(genome.BendBias)) * shape.SwayProfileAt(k)
                * (1.0 + shape.TrunkSwayIrregularity * SwayHash(k, genome.Seed));

        /// <summary>⚑R §1.5.4(а): h_k ∈ [−1, 1) — hash of (randomSeed, k, "SWAY"); the same for the same seed.</summary>
        public static double SwayHash(int k, uint seed) => StableHash.Signed(seed, k, StableHash.SwaySalt);

        /// <summary>⚑Q BALANCE §1.4.7: world angle of #0 = lean + λ·TierPhase + TierPhase·TrunkBaseSwayRatio·A_1·σ (degrees, before Pot Lip).</summary>
        public static double TrunkBaseWorldAngleDeg(in TreeGenome genome, GrowthConfig config, SpeciesConfig species)
        {
            ShapeParams shape = ShapeParams.Resolve(config, species);
            int tierPhase = TreeShapeAnalysis.TierPhase(genome.Seed);
            return genome.BendBias * config.TrunkBendScaleDeg + shape.LeanOffsetDeg * tierPhase
                + tierPhase * shape.TrunkBaseSwayRatio * TrunkSwayAmplitudeDeg(1, genome, in shape, config) * shape.SwayScale;
        }

        /// <summary>OrderLengthFactor^min(ordEff, 2) (BALANCE §1.2.6).</summary>
        public static double OrderLengthMultiplier(int orderEff, GrowthConfig config)
        {
            if (orderEff <= 0) return 1.0;
            return orderEff == 1 ? config.OrderLengthFactor : config.OrderLengthFactor * config.OrderLengthFactor;
        }

        /// <summary>max(MinShootThickness, min(initial, th_max(d), th_cap)) (BALANCE §1.2.2; ⚑Q §1.4.1(б) / §1.4.3 for children of ord-0 parents).</summary>
        public static double NewShootThickness(double initial, int depth, double parentThickness, double attachT, bool apical, bool parentOrd0,
            in ShapeParams shape, GrowthConfig config)
        {
            double cap = GrowthFormulas.ThicknessCap(parentThickness, attachT, apical, parentOrd0, in shape, config);
            bool ord0 = parentOrd0 && apical;
            double thickness = Math.Min(initial, Math.Min(GrowthFormulas.MaxThicknessAt(depth, ord0, shape.TrunkChainDepthScaleFactor, config), cap));
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
