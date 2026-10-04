using System;
using System.Collections.Generic;
using PocketArbor.Domain.Growth;
using PocketArbor.Domain.Soil;
using PocketArbor.Domain.Tree;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// Deterministic day-by-day replay for the growth preview (PA-102V). Day 0 = planted sapling (Freeze Guard lifted);
    /// day d = day d−1 + HoursPerGrowthDay hours of constant weather + cuts recorded for day d. Snapshots are cached per day.
    /// Dev-only: allocations are fine here.
    /// </summary>
    public sealed class GrowthPreviewModel
    {
        public const int MaxPreviewDay = 120;
        public const int Reach200SearchDays = 400;

        /// <summary>PA-102S AC19: styling sessions of the Styled mode.</summary>
        public static readonly int[] StyleDays = { 20, 30, 40, 50, 60, 70 };

        /// <summary>PruneToStyle: branch nodes farther than this many segments from the trunk are cut.</summary>
        public const int StyleMaxBranchSegments = 3;

        /// <summary>PruneToStyle: of two tiers attached closer than this (units of height), the lower one is cut.</summary>
        public const double StyleMinTierGap = 12.0;

        readonly GrowthConfig _config = GrowthConfig.CreateDefault();
        readonly SoilConfig _soil = SoilConfig.CreateDefault();
        readonly SpeciesCatalog _species = SpeciesCatalog.CreateDefault();
        readonly GrowthSimulator _simulator;
        readonly List<TreeSnapshot> _days = new List<TreeSnapshot>();
        readonly List<KeyValuePair<int, int>> _cuts = new List<KeyValuePair<int, int>>(); // (day, nodeId)
        readonly System.Random _cutPicker = new System.Random(1);

        SpeciesId _speciesId = SpeciesId.Kuromatsu;
        uint _seed = 20261004u;
        double _vigor = 1.0;
        double _bendBias;
        double _temperatureC = 25.0;
        double _moisture = 0.5;
        bool _onboardingCut;
        bool _styled;
        int _dayReaching200 = -2; // -2 = not computed, -1 = not within search window

        public GrowthPreviewModel()
        {
            _simulator = new GrowthSimulator(_config, _soil, _species);
        }

        public GrowthConfig Config => _config;
        public SpeciesId Species => _speciesId;
        public uint Seed => _seed;
        public double Vigor => _vigor;
        public double BendBias => _bendBias;
        public double TemperatureC => _temperatureC;
        public double Moisture => _moisture;
        public bool OnboardingCut => _onboardingCut;
        public int CutCount => _cuts.Count;
        public bool Styled => _styled;

        /// <summary>Wild (no pruning) or Styled (<see cref="PruneToStyle"/> on <see cref="StyleDays"/>).</summary>
        public void SetStyled(bool styled)
        {
            if (styled == _styled) return;
            _styled = styled;
            _cuts.Clear();
            Invalidate(0);
        }

        /// <summary>Changes invalidate the cache (and recorded cuts) only when a value actually changes.</summary>
        public void SetParameters(SpeciesId species, uint seed, double vigor, double bendBias, double temperatureC, double moisture, bool onboardingCut)
        {
            if (species == _speciesId && seed == _seed && vigor.Equals(_vigor) && bendBias.Equals(_bendBias)
                && temperatureC.Equals(_temperatureC) && moisture.Equals(_moisture) && onboardingCut == _onboardingCut) return;

            _speciesId = species;
            _seed = seed;
            _vigor = vigor;
            _bendBias = bendBias;
            _temperatureC = temperatureC;
            _moisture = moisture;
            _onboardingCut = onboardingCut;
            _cuts.Clear();
            Invalidate(0);
        }

        public SpeciesConfig SpeciesConfig => _species.Get(_speciesId);

        /// <summary>Snapshot of the tree at the end of <paramref name="day"/> (0..MaxPreviewDay).</summary>
        public TreeSnapshot GetDay(int day)
        {
            day = Math.Max(0, Math.Min(MaxPreviewDay, day));
            while (_days.Count <= day) _days.Add(ComputeDay(_days.Count));
            return _days[day];
        }

        public TreeGraph GetTree(int day) => TreeGraph.FromSnapshot(GetDay(day));

        /// <summary>First day with N = MaxNodes (−1 if not within <see cref="Reach200SearchDays"/>).</summary>
        public int DayReachingMaxNodes()
        {
            if (_dayReaching200 != -2) return _dayReaching200;
            _dayReaching200 = -1;
            for (int d = 0; d <= MaxPreviewDay; d++)
            {
                if (GetDay(d).NodeCount >= _config.MaxNodes)
                {
                    _dayReaching200 = d;
                    return d;
                }
            }

            TreeGraph tree = GetTree(MaxPreviewDay);
            for (int d = MaxPreviewDay + 1; d <= Reach200SearchDays; d++)
            {
                RunOneDay(tree);
                if (tree.Count >= _config.MaxNodes)
                {
                    _dayReaching200 = d;
                    break;
                }
            }
            return _dayReaching200;
        }

        /// <summary>Cuts a random non-trunk node at <paramref name="day"/>; recorded so later days replay it.</summary>
        public PruneResult CutRandomBranch(int day)
        {
            day = Math.Max(0, Math.Min(MaxPreviewDay, day));
            TreeGraph tree = GetTree(day);
            if (tree.Count < 2) return new PruneResult(PruneStatus.NotFound, 0, -1);
            int index = 1 + _cutPicker.Next(tree.Count - 1);
            int id = tree.Nodes[index].Id;
            PruneResult result = TreePruning.Prune(tree, id, _config); // same result the replay will produce
            _cuts.Add(new KeyValuePair<int, int>(day, id));
            Invalidate(day);
            return result;
        }

        /// <summary>
        /// Simple moyogi styling script (PA-102S AC19), in order: (1) the sapling's wild twig (trunk lateral below the tier
        /// slots); (2) every branch node farther than <see cref="StyleMaxBranchSegments"/> segments from the trunk chain
        /// (crown axis included); (3) of two primaries attached closer than <see cref="StyleMinTierGap"/> in height, the
        /// lower one; (4) interior shoots — laterals of interior branch nodes. Returns the number of cuts.
        /// </summary>
        public static int PruneToStyle(TreeGraph tree, GrowthConfig config, SpeciesConfig species)
        {
            var shape = new TreeShapeAnalysis();
            var targets = new List<int>();
            int cuts = 0;

            // (1) Wild twig.
            for (int i = 1; i < tree.Count; i++)
            {
                NodeData n = tree.Nodes[i];
                if (n.ParentIndex == 0 && n.BranchSide != 0 && n.AttachT < config.TrunkBaseSlotT0 - config.AttachTJitter - 1e-9) targets.Add(n.Id);
            }
            cuts += PruneAll(tree, targets, config);

            // (2) Branch "legs": nodes more than N segments from their trunk-chain ancestor.
            shape.Analyze(tree, config, species);
            for (int i = 1; i < tree.Count; i++)
            {
                if (shape.IsTrunkChain(i)) continue;
                int anchor = tree.Nodes[i].ParentIndex;
                while (anchor > 0 && !shape.IsTrunkChain(anchor)) anchor = tree.Nodes[anchor].ParentIndex;
                if (tree.Nodes[i].Depth - tree.Nodes[anchor].Depth == StyleMaxBranchSegments + 1) targets.Add(tree.Nodes[i].Id);
            }
            cuts += PruneAll(tree, targets, config);

            // (3) Tiers too close in height: cut the lower of the pair.
            shape.Analyze(tree, config, species);
            var baseY = AttachHeights(tree);
            var primaries = new List<int>();
            for (int i = 1; i < tree.Count; i++)
            {
                if (shape.IsPrimary(i)) primaries.Add(i);
            }
            primaries.Sort((a, b) => baseY[a].CompareTo(baseY[b]));
            for (int j = 1; j < primaries.Count; j++)
            {
                if (baseY[primaries[j]] - baseY[primaries[j - 1]] < StyleMinTierGap) targets.Add(tree.Nodes[primaries[j - 1]].Id);
            }
            cuts += PruneAll(tree, targets, config);

            // (4) Interior shoots.
            shape.Analyze(tree, config, species);
            for (int i = 1; i < tree.Count; i++)
            {
                int parent = tree.Nodes[i].ParentIndex;
                if (tree.Nodes[i].BranchSide != 0 && !shape.IsTrunkChain(parent) && shape.IsInterior(parent)) targets.Add(tree.Nodes[i].Id);
            }
            cuts += PruneAll(tree, targets, config);
            return cuts;
        }

        static int PruneAll(TreeGraph tree, List<int> ids, GrowthConfig config)
        {
            int cuts = 0;
            for (int k = 0; k < ids.Count; k++)
            {
                if (tree.IndexOf(ids[k]) < 0) continue; // already removed with an ancestor
                if (TreePruning.Prune(tree, ids[k], config).Succeeded) cuts++;
            }
            ids.Clear();
            return cuts;
        }

        /// <summary>Height of every node's base (chord geometry, AttachT along the parent chord).</summary>
        public static double[] AttachHeights(TreeGraph tree)
        {
            var nodes = tree.Nodes;
            var baseY = new double[nodes.Length];
            var tipY = new double[nodes.Length];
            for (int i = 0; i < nodes.Length; i++)
            {
                int p = nodes[i].ParentIndex;
                baseY[i] = p < 0 ? 0.0 : baseY[p] + nodes[i].AttachT * (tipY[p] - baseY[p]);
                tipY[i] = baseY[i] + nodes[i].Length * Math.Cos(tree.WorldAngle(i));
            }
            return baseY;
        }

        void Invalidate(int fromDay)
        {
            if (_days.Count > fromDay) _days.RemoveRange(fromDay, _days.Count - fromDay);
            _dayReaching200 = -2;
        }

        TreeSnapshot ComputeDay(int day)
        {
            TreeGraph tree;
            if (day == 0)
            {
                tree = CreateSapling();
            }
            else
            {
                tree = TreeGraph.FromSnapshot(_days[day - 1]);
                RunOneDay(tree);
            }
            if (_styled && Array.IndexOf(StyleDays, day) >= 0) PruneToStyle(tree, _config, SpeciesConfig);
            ApplyCuts(tree, day, _cuts.Count);
            return tree.ToSnapshot();
        }

        TreeGraph CreateSapling()
        {
            var tree = TreeFactory.PlantSapling(new TreeGenome(_seed, _speciesId, _vigor, _bendBias), _config, SpeciesConfig);
            tree.SetGrowthFrozen(false);
            if (_onboardingCut && tree.Count > 2) TreePruning.Prune(tree, tree.Nodes[2].Id, _config);
            return tree;
        }

        void RunOneDay(TreeGraph tree)
        {
            var input = new GrowthHourInput(_moisture, _temperatureC);
            for (int h = 0; h < _config.HoursPerGrowthDay; h++) _simulator.SimulateHour(tree, in input);
        }

        void ApplyCuts(TreeGraph tree, int day, int cutLimit)
        {
            for (int i = 0; i < cutLimit; i++)
            {
                if (_cuts[i].Key == day) TreePruning.Prune(tree, _cuts[i].Value, _config);
            }
        }
    }
}
