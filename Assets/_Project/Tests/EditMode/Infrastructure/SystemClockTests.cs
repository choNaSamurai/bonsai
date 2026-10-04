using System;
using NUnit.Framework;
using PocketArbor.Infrastructure.Time;

namespace PocketArbor.Infrastructure.Tests
{
    public sealed class SystemClockTests
    {
        [Test]
        public void ReturnsUtcCloseToNow()
        {
            var clock = new SystemClock();
            DateTime now = clock.UtcNow;
            Assert.AreEqual(DateTimeKind.Utc, now.Kind);
            Assert.Less(Math.Abs((DateTime.UtcNow - now).TotalSeconds), 5.0);
        }
    }
}
