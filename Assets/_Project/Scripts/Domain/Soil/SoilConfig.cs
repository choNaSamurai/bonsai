namespace PocketArbor.Domain.Soil
{
    /// <summary>
    /// Soil balance values (BALANCE §4). PA-102 needs only <see cref="DryThreshold"/> for M_soil;
    /// the rest of the table is added with SoilSimulation in PA-202. Plain C# (mapped from a ScriptableObject in PA-007).
    /// </summary>
    public sealed class SoilConfig
    {
        /// <summary>W_dry: below it growth stops and the soil cracks. BALANCE §4 <c>DryThreshold</c>.</summary>
        public double DryThreshold = 0.10;

        public static SoilConfig CreateDefault() => new SoilConfig();
    }
}
