using System;
using PocketArbor.Domain.Time;

namespace PocketArbor.Infrastructure.Time
{
    /// <summary>Production <see cref="IClock"/> backed by the device wall clock.</summary>
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
