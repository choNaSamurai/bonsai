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
