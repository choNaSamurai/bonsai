using System;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Growth
{
    /// <summary>Builds a new shoot row by the rules of BALANCE §1.1 step 6.4 (shared by DailyStep and PlantSeed).</summary>
    internal static class ShootBuilder
    {
        /// <param name="lateralMagnitudeDeg">
        /// If finite, overrides the triangular lateral angle magnitude (sapling twig uses Mean + Spread).
        /// </param>
        public static NodeData Build(TreeGraph tree, int parentIndex, in ShootDraws u, GrowthConfig config,
            SpeciesConfig species, double bendBias, double lateralMagnitudeDeg = double.NaN)
        {
            ReadOnlySpan<NodeData> nodes = tree.Nodes;
            NodeData parent = nodes[parentIndex];
            bool apical = tree.ChildCount(parentIndex) == 0;
            int depth = parent.Depth + 1;

            sbyte side = 0;
            double angleDeg;
            if (apical)
            {
                angleDeg = bendBias * config.ApicalBendScaleDeg + config.ApicalAngleJitterDeg * (u.U2 + u.U3 - 1.0);
            }
            else
            {
                side = LateralSign(nodes, parentIndex, u.U4);
                double magnitude = double.IsNaN(lateralMagnitudeDeg) || double.IsInfinity(lateralMagnitudeDeg)
                    ? GrowthFormulas.Triangular(species.BranchAngleMeanDeg, species.BranchAngleSpreadDeg, u.U2, u.U3)
                    : lateralMagnitudeDeg;
                angleDeg = side * magnitude;
            }

            double angle = ClampToPotLip(tree.WorldAngle(parentIndex), angleDeg * GrowthFormulas.DegToRad, config);

            double targetLength = config.ShootBaseLength * Math.Pow(config.ShootLengthDecay, depth - 1)
                * (1.0 + config.ShootLengthJitter * (2.0 * u.U5 - 1.0));
            if (apical) targetLength *= config.ApicalLengthMultiplier;

            return new NodeData
            {
                ParentIndex = parentIndex,
                Depth = (byte)depth,
                Length = config.ShootInitialLength,
                TargetLength = targetLength,
                Thickness = Math.Min(config.ShootInitialThickness, GrowthFormulas.MaxThicknessAt(depth, config)),
                BaseAngle = angle,
                CurrentAngle = angle,
                Curvature = GrowthFormulas.Triangular(0.0, species.CurvatureSpread, u.U6, u.U7),
                AgeDays = 0,
                Status = NodeStatus.Bud,
                LeafScale = 0f,
                BranchSide = side,
            };
        }

        // BALANCE §1.1: interpretation — "last" lateral child = highest index (= newest id, shoots are appended);
        // its side is read from the persisted BranchSide, not from the (possibly clamped/wired) angle.
        /// <summary>
        /// Opposite of the sign of the parent's last (highest index = newest) lateral child; if none — u4 &lt; 0.5 ? −1 : +1.
        /// </summary>
        static sbyte LateralSign(ReadOnlySpan<NodeData> nodes, int parentIndex, double u4)
        {
            for (int i = nodes.Length - 1; i > parentIndex; i--)
            {
                if (nodes[i].ParentIndex == parentIndex && nodes[i].BranchSide != 0)
                {
                    return (sbyte)-nodes[i].BranchSide;
                }
            }
            return u4 < 0.5 ? (sbyte)-1 : (sbyte)1;
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
