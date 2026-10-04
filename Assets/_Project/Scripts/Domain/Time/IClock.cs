using System;

namespace PocketArbor.Domain.Time
{
    /// <summary>
    /// Wall-clock abstraction. All simulation code reads time through this port so tests can
    /// drive offline catch-up, clock rollback and DST scenarios deterministically.
    /// </summary>
    public interface IClock
    {
        /// <summary>Current time in UTC. Implementations must return <see cref="DateTimeKind.Utc"/>.</summary>
        DateTime UtcNow { get; }
    }
}
