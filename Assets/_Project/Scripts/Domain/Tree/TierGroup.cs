namespace PocketArbor.Domain.Tree
{
    /// <summary>
    /// ⚑T BALANCE §1.6.1(а) [MODEL-derived] tier group of a node (<see cref="TreeShapeAnalysis.TierGroup"/>; not persisted).
    /// T0…T5 = the six side pads #1…#6 (t0 = slot 0 of #0, t1 = slot 1 of #0, t(k+1) = slot 1 of chain node k ≥ 1), <see cref="Dome"/> = the crown
    /// axis, its laterals and the crown shoulder (slot 0 of the chain top) with all they bear, <see cref="Chain"/> = the trunk chain,
    /// <see cref="Other"/> = intermediate tiers (slot 0 of k1…k3), the sapling twig and everything else (no balance).
    /// </summary>
    public enum TierGroup : byte
    {
        T0 = 0,
        T1 = 1,
        T2 = 2,
        T3 = 3,
        T4 = 4,
        T5 = 5,
        Dome = 6,
        Chain = 7,
        Other = 8,
    }

    public static class TierGroups
    {
        /// <summary>Number of <see cref="TierGroup"/> values (array size of per-group buffers).</summary>
        public const int Count = 9;

        /// <summary>Side tiers t0…t5.</summary>
        public const int SideTiers = 6;

        /// <summary>True for t0…t5 and the dome — the groups with a node target (§1.6.1 б).</summary>
        public static bool IsBalanced(TierGroup group) => group <= TierGroup.Dome;

        /// <summary>k_t of a side tier: t0, t1 → 0; t(k+1) → k (chain node index the tier grows from); −1 for other groups.</summary>
        public static int ChainIndex(TierGroup group) => group == TierGroup.T0 ? 0 : group <= TierGroup.T5 ? (int)group - 1 : -1;
    }
}
