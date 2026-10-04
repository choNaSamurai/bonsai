namespace PocketArbor.Domain.Tree
{
    /// <summary>
    /// Per-tree growth state outside the node table (BALANCE §1.1). Persisted with the tree.
    /// </summary>
    public struct GrowthState
    {
        /// <summary>Hours accumulated towards the next DailyStep, 0..HoursPerGrowthDay-1.</summary>
        public int GrowthHourCounter;

        /// <summary>0 at planting; incremented at the start of every DailyStep. Also the PCG32 stream of that day.</summary>
        public int GrowthDayIndex;

        /// <summary>Fractional shoots carried over between days, ≥ 0.</summary>
        public double ShootCredit;

        /// <summary>Σ g = M_soil·T_factor over the hours of the current growth day.</summary>
        public double SumGrowthFactor;

        /// <summary>Σ M_soil over the hours of the current growth day.</summary>
        public double SumMoistureFactor;

        /// <summary>
        /// Freeze guard: while true no hour accumulates (onboarding not completed, Dormancy).
        /// Driven by Application (PA-106 / PA-204).
        /// </summary>
        public bool Frozen;
    }
}
