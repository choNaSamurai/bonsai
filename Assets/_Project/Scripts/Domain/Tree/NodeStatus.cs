namespace PocketArbor.Domain.Tree
{
    /// <summary>Life-cycle stage of a node (ADR-0003, BALANCE §1.1).</summary>
    public enum NodeStatus : byte
    {
        Bud = 0,
        Growing = 1,
        Mature = 2,

        /// <summary>Reserved for presentation (falling branch). The domain removes pruned nodes outright.</summary>
        Pruned = 3,

        /// <summary>Reserved for Dormancy wake-up shoots (BALANCE §9.5, PA-204).</summary>
        Dormant = 4,
    }
}
