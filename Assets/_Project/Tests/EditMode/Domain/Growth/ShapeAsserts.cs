using System;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>Role-based angle / slot assertions of BALANCE §1.2.1, §1.2.3, §1.2.6 for unpruned, unwired trees.</summary>
    internal static class ShapeAsserts
    {
        const double Eps = 1e-9;

        public static void AssertShootAngles(TreeGraph tree, GrowthConfig config, SpeciesConfig species)
        {
            var nodes = tree.Nodes;
            var shape = new TreeShapeAnalysis();
            shape.Analyze(tree, config, species);
            double d2r = GrowthFormulas.DegToRad;
            double lean = tree.Genome.BendBias * config.TrunkBendScaleDeg;
            int tier = TreeShapeAnalysis.TierPhase(tree.Genome.Seed);
            var apicalCount = new int[nodes.Length];
            var lateralSide = new sbyte[nodes.Length];

            for (int i = 1; i < nodes.Length; i++)
            {
                NodeData n = nodes[i];
                int p = n.ParentIndex;
                double world = tree.WorldAngle(i) / d2r;
                double parentWorld = tree.WorldAngle(p) / d2r;
                bool clamped = Math.Abs(Math.Abs(world) - config.MaxShootWorldAngleDeg) < 1e-7;
                Assert.AreEqual(n.BaseAngle, n.CurrentAngle, "no wire in growth");
                Assert.LessOrEqual(Math.Abs(world), config.MaxShootWorldAngleDeg + Eps, "Pot Lip at " + i);
                Assert.LessOrEqual(shape.ChildCount(i), config.MaxChildrenPerNode);

                if (n.BranchSide == 0)
                {
                    Assert.LessOrEqual(++apicalCount[p], 1, "single apical child of " + p);
                    Assert.AreEqual(1.0, n.AttachT, "apical AttachT at " + i);
                    if (shape.IsTrunkChain(i))
                    {
                        int k = n.Depth;
                        double amplitude = species.TrunkSwayDeg * (1.0 + Math.Abs(tree.Genome.BendBias)) * Math.Pow(config.TrunkSwayDecay, k - 1);
                        double center = lean + tier * ((k & 1) == 0 ? 1.0 : -1.0) * amplitude;
                        Assert.AreEqual(center, world, config.TrunkSwayJitterDeg + Eps, "trunk segment k=" + k + " at " + i);
                    }
                    else if (shape.IsCrownAxis(i))
                    {
                        if (!clamped) Assert.AreEqual(lean, world, config.ApicalAngleJitterDeg + Eps, "crown axis at " + i);
                    }
                    else
                    {
                        double sign = GrowthFormulas.SignNonZero(parentWorld);
                        double center = parentWorld + (sign * 90.0 - parentWorld) * species.BranchDroop;
                        if (!clamped) Assert.AreEqual(center, world, config.ApicalAngleJitterDeg + Eps, "continuation at " + i);
                    }
                }
                else
                {
                    Assert.That(n.AttachT, Is.InRange(config.AttachTMin, config.AttachTMax), "lateral AttachT at " + i);
                    if (lateralSide[p] != 0) Assert.AreEqual(-lateralSide[p], n.BranchSide, "laterals of one parent on opposite sides at " + i);
                    lateralSide[p] = n.BranchSide;
                    if (shape.IsTrunkChain(p))
                    {
                        double boundary = p == 0 ? 0.5 * (config.TrunkBaseSlotT0 + config.TrunkBaseSlotT1) : 0.5 * (config.LateralSlotT0 + config.LateralSlotT1);
                        bool slot0 = n.AttachT < boundary;
                        Assert.AreEqual(tier * (slot0 ? -1 : 1), n.BranchSide, "primary side = TierPhase·(slot0 ? −1 : +1) at " + i);
                        if (!clamped)
                        {
                            Assert.That(Math.Abs(world), Is.InRange(species.PrimaryAngleMeanDeg - species.PrimaryAngleSpreadDeg - Eps,
                                species.PrimaryAngleMeanDeg + species.PrimaryAngleSpreadDeg + Eps), "primary angle at " + i);
                        }
                        Assert.AreEqual(n.BranchSide, Math.Sign(world), "primary direction = side at " + i);
                    }
                    else if (!clamped)
                    {
                        Assert.That(Math.Abs(n.BaseAngle) / d2r, Is.InRange(species.BranchAngleMeanDeg - species.BranchAngleSpreadDeg - Eps,
                            species.BranchAngleMeanDeg + species.BranchAngleSpreadDeg + Eps), "secondary angle at " + i);
                        Assert.AreEqual(n.BranchSide, Math.Sign(n.BaseAngle), "secondary side at " + i);
                    }
                }
            }
        }
    }
}
