using System;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>Role-based angle / slot assertions of BALANCE §1.2.1, §1.2.3, §1.2.6 with ⚑P §1.3.1–§1.3.2 for unpruned, unwired trees.</summary>
    internal static class ShapeAsserts
    {
        const double Eps = 1e-9;

        static bool InRange(double value, double mean, double spread) => value >= mean - spread - Eps && value <= mean + spread + Eps;

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
                        double amplitude = ShootBuilder.TrunkSwayAmplitudeDeg(k, tree.Genome, config, species);
                        double center = lean + tier * ((k & 1) == 0 ? 1.0 : -1.0) * amplitude;
                        Assert.AreEqual(center, world, config.TrunkSwayJitterDeg + Eps, "trunk segment k=" + k + " at " + i);
                    }
                    else if (shape.IsCrownAxis(i))
                    {
                        // ⚑T §1.6.2(в) (conscious update): + −s_top·CrownAxisLeanDeg·σ towards the crown shoulder.
                        double crownLean = ShootBuilder.CrownAxisLeanDeg(tree.Genome.Seed, ShapeParams.Resolve(config, species), config);
                        if (!clamped) Assert.AreEqual(lean + crownLean, world, config.ApicalAngleJitterDeg + Eps, "crown axis at " + i);
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
                        // ⚑T TD-065 (conscious update): the slot of a chain lateral is its side (the shoulder sits at AttachT 0.95 in slot 0).
                        bool slot0 = TreeShapeAnalysis.ChainLateralSlot(n.BranchSide, nodes[p].Depth, tree.Genome.Seed) == 0;
                        // ⚑P BALANCE §1.3.2: slot 1 on s_k = TierPhase·(−1)^k, slot 0 on −s_k.
                        int sk = ShootBuilder.TierSide(nodes[p].Depth, tree.Genome.Seed);
                        Assert.AreEqual(slot0 ? -sk : sk, n.BranchSide, "primary side = slot1 ? s_k : −s_k at " + i);
                        if (!clamped)
                        {
                            bool inPrimary = InRange(Math.Abs(world), species.PrimaryAngleMeanDeg, species.PrimaryAngleSpreadDeg);
                            // §1.3.2 apex dome: laterals of the chain top born before the crown axis take the crown-lateral angle.
                            bool inDome = (nodes[p].Depth == config.TrunkChainSegments - 1 || shape.IsCrownShoulder(i))
                                && InRange(Math.Abs(world), species.CrownLateralAngleMeanDeg, species.CrownLateralAngleSpreadDeg);
                            Assert.IsTrue(inPrimary || inDome, "primary angle at " + i + ": " + world);
                        }
                        Assert.AreEqual(n.BranchSide, Math.Sign(world), "primary direction = side at " + i);
                    }
                    else if (shape.IsCrownAxis(p))
                    {
                        // ⚑P BALANCE §1.3.2: crown-axis laterals — world angle side·tri(CrownLateralAngleMean, Spread).
                        if (!clamped) Assert.IsTrue(InRange(Math.Abs(world), species.CrownLateralAngleMeanDeg, species.CrownLateralAngleSpreadDeg), "dome lateral at " + i);
                        Assert.AreEqual(n.BranchSide, Math.Sign(world), "dome lateral direction = side at " + i);
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
