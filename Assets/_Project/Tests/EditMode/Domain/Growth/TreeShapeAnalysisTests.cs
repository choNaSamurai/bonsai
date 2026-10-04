using System;
using System.Collections.Generic;
using NUnit.Framework;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Random;
using PocketArbor.Domain.Tree;

namespace PocketArbor.Domain.Tests.Growth
{
    /// <summary>PA-102S AC9: TreeShapeAnalysis vs a straightforward recursive reference; 0 B GC.</summary>
    public sealed class TreeShapeAnalysisTests
    {
        // ---- Reference (recursive, allocating) implementation of BALANCE §1.2.0 / §1.2.5 -----------------

        sealed class Reference
        {
            public int[] Order, Tip, PadId, PadSize;
            public bool[] Chain, Crown, Leafy;
            public int PadCount;
        }

        static Reference Compute(NodeData[] nodes, int chainSegments, int padDepth)
        {
            int n = nodes.Length;
            var children = new List<int>[n];
            for (int i = 0; i < n; i++) children[i] = new List<int>();
            for (int i = 1; i < n; i++) children[nodes[i].ParentIndex].Add(i);

            var r = new Reference
            {
                Order = new int[n], Tip = new int[n], PadId = new int[n], PadSize = new int[n],
                Chain = new bool[n], Crown = new bool[n], Leafy = new bool[n],
            };

            void Walk(int i, int order)
            {
                r.Order[i] = order;
                foreach (int c in children[i]) Walk(c, order + (nodes[c].BranchSide != 0 ? 1 : 0));
            }
            Walk(0, 0);

            int TipOf(int i)
            {
                if (children[i].Count == 0) return 0;
                int best = int.MaxValue;
                foreach (int c in children[i]) best = Math.Min(best, TipOf(c));
                return best + 1;
            }

            for (int i = 0; i < n; i++)
            {
                r.Tip[i] = TipOf(i);
                r.Chain[i] = r.Order[i] == 0 && nodes[i].Depth < chainSegments;
                r.Crown[i] = r.Order[i] == 0 && nodes[i].Depth >= chainSegments;
                int ordEff = r.Crown[i] ? 1 : r.Order[i];
                r.Leafy[i] = nodes[i].HasLeaves && ((r.Chain[i] && children[i].Count == 0) || (ordEff >= 1 && r.Tip[i] <= padDepth));
                r.PadId[i] = -1;
            }

            // Flood fill of leafy components over parent-child edges; id = smallest index.
            for (int i = 0; i < n; i++)
            {
                if (!r.Leafy[i] || r.PadId[i] >= 0) continue;
                var members = new List<int>();
                var stack = new Stack<int>();
                stack.Push(i);
                r.PadId[i] = i;
                while (stack.Count > 0)
                {
                    int x = stack.Pop();
                    members.Add(x);
                    var neighbours = new List<int>(children[x]);
                    if (nodes[x].ParentIndex >= 0) neighbours.Add(nodes[x].ParentIndex);
                    foreach (int y in neighbours)
                    {
                        if (!r.Leafy[y] || r.PadId[y] >= 0) continue;
                        r.PadId[y] = i;
                        stack.Push(y);
                    }
                }
                int min = int.MaxValue;
                foreach (int m in members) min = Math.Min(min, m);
                foreach (int m in members)
                {
                    r.PadId[m] = min;
                    r.PadSize[m] = members.Count;
                }
                r.PadCount++;
            }
            return r;
        }

        /// <summary>Random topology: random parent, side, status; at most one apical child per node.</summary>
        static NodeData[] RandomTree(Pcg32 rng)
        {
            int count = 1 + (int)rng.NextUInt(TreeGraph.Capacity);
            var nodes = new NodeData[count];
            var hasApical = new bool[count];
            nodes[0] = new NodeData { Id = 0, ParentIndex = -1, Depth = 0, Status = NodeStatus.Mature, AttachT = 1.0 };
            for (int i = 1; i < count; i++)
            {
                int parent = (int)rng.NextUInt((uint)i);
                if (nodes[parent].Depth >= 12) parent = 0;
                bool lateral = hasApical[parent] || rng.NextDouble() < 0.4;
                if (!lateral) hasApical[parent] = true;
                nodes[i] = new NodeData
                {
                    Id = i,
                    ParentIndex = parent,
                    Depth = (byte)(nodes[parent].Depth + 1),
                    Status = (NodeStatus)rng.NextUInt(3),
                    BranchSide = lateral ? (rng.NextDouble() < 0.5 ? (sbyte)-1 : (sbyte)1) : (sbyte)0,
                    AttachT = lateral ? rng.NextDouble() : 1.0,
                };
            }
            return nodes;
        }

        static void AssertMatches(TreeShapeAnalysis shape, NodeData[] nodes, int chainSegments, int padDepth, string label)
        {
            Reference r = Compute(nodes, chainSegments, padDepth);
            Assert.AreEqual(nodes.Length, shape.Count, label);
            Assert.AreEqual(r.PadCount, shape.PadCount, label + " pad count");
            int chain = 0;
            for (int i = 0; i < nodes.Length; i++)
            {
                string at = label + " node " + i;
                Assert.AreEqual(r.Order[i], shape.Order(i), at + " ord");
                Assert.AreEqual(r.Chain[i], shape.IsTrunkChain(i), at + " chain");
                Assert.AreEqual(r.Crown[i], shape.IsCrownAxis(i), at + " crown");
                Assert.AreEqual(r.Crown[i] ? 1 : r.Order[i], shape.EffectiveOrder(i), at + " ordEff");
                Assert.AreEqual(r.Tip[i], shape.TipDistance(i), at + " tip");
                Assert.AreEqual(r.Leafy[i], shape.IsLeafy(i), at + " leafy");
                Assert.AreEqual(r.PadId[i], shape.PadId(i), at + " pad id");
                Assert.AreEqual(r.PadSize[i], shape.PadSize(i), at + " pad size");
                bool primary = nodes[i].BranchSide != 0 && r.Chain[nodes[i].ParentIndex];
                Assert.AreEqual(primary, shape.IsPrimary(i), at + " primary");
                if (r.Chain[i]) chain++;
                if (r.Chain[i] && shape.ChildCount(i) > 0) Assert.IsFalse(shape.IsLeafy(i), at + ": trunk chain with children is never leafy");
            }
            Assert.AreEqual(chain, shape.TrunkChainLength, label + " chain length");
        }

        [Test]
        public void Analyze_HundredRandomTrees_MatchesRecursiveReference()
        {
            var shape = new TreeShapeAnalysis();
            var rng = new Pcg32(2026UL, 102UL);
            for (int t = 0; t < 100; t++)
            {
                NodeData[] nodes = RandomTree(rng);
                int chainSegments = 1 + (int)rng.NextUInt(6);
                int padDepth = (int)rng.NextUInt(4);
                shape.Analyze(nodes, chainSegments, padDepth);
                AssertMatches(shape, nodes, chainSegments, padDepth, "random tree " + t);
            }
        }

        [Test]
        public void Analyze_GrownAndPrunedTrees_MatchesRecursiveReference()
        {
            var config = GrowthConfig.CreateDefault();
            var sim = GrowthTestKit.Simulator(config);
            var shape = new TreeShapeAnalysis();
            var pick = new Pcg32(5UL, 9UL);
            foreach (SpeciesId species in new[] { SpeciesId.Kuromatsu, SpeciesId.Momiji, SpeciesId.Yamazakura })
            {
                for (uint seed = 1; seed <= 6; seed++)
                {
                    var tree = GrowthTestKit.PostOnboarding(seed, species);
                    for (int step = 0; step < 8; step++)
                    {
                        GrowthTestKit.RunDays(sim, tree, 12);
                        if (tree.Count > 2) TreePruning.Prune(tree, tree.Nodes[1 + (int)pick.NextUInt((uint)tree.Count - 1)].Id, config);
                        var sp = SpeciesConfig.CreateDefault(species);
                        shape.Analyze(tree, config, sp);
                        AssertMatches(shape, tree.Nodes.ToArray(), config.TrunkChainSegments, sp.PadDepth, species + " seed " + seed + " step " + step);
                    }
                }
            }
        }

        [Test]
        public void Analyze_SaplingLeaderWithoutChildren_IsLeafyTrunkChainApex()
        {
            var config = GrowthConfig.CreateDefault();
            var tree = GrowthTestKit.PostOnboarding(3u);
            var shape = new TreeShapeAnalysis();
            shape.Analyze(tree, config, SpeciesConfig.Kuromatsu());
            Assert.IsTrue(shape.IsTrunkChain(1));
            Assert.IsTrue(shape.IsLeafy(1), "young leader reads as a plant");
            Assert.IsFalse(shape.IsLeafy(0), "trunk has no leaves");
            Assert.AreEqual(1, shape.PadCount);
            Assert.AreEqual(1, shape.PadSize(1));
        }

        [TestCase(0u, 1)]
        [TestCase(20261004u, 1)]
        [TestCase(1u, -1)]
        [TestCase(uint.MaxValue, -1)]
        public void TierPhase_SeedLowBit_GivesSide(uint seed, int expected)
        {
            Assert.AreEqual(expected, TreeShapeAnalysis.TierPhase(seed));
        }

        [Test]
        public void Analyze_200Nodes_AllocatesZeroBytes()
        {
            var config = GrowthConfig.CreateDefault();
            var tree = GrowthTestKit.PostOnboarding(GrowthTestKit.FixtureSeed);
            GrowthTestKit.RunDays(GrowthTestKit.Simulator(config), tree, 150);
            Assert.AreEqual(200, tree.Count);
            var species = SpeciesConfig.Kuromatsu();
            var shape = new TreeShapeAnalysis();
            shape.Analyze(tree, config, species); // warm-up

            long before = GC.GetAllocatedBytesForCurrentThread();
            int sink = 0;
            for (int r = 0; r < 100; r++)
            {
                shape.Analyze(tree, config, species);
                for (int i = 0; i < shape.Count; i++) sink += shape.PadId(i) + shape.TipDistance(i) + shape.EffectiveOrder(i);
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
            Assert.AreNotEqual(int.MinValue, sink);
        }
    }
}
