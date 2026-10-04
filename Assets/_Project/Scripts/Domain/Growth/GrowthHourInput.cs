namespace PocketArbor.Domain.Growth
{
    /// <summary>Hourly inputs of growth: soil moisture W after SoilSimulation (§4) and air temperature of that hour.</summary>
    public readonly struct GrowthHourInput
    {
        public readonly double Moisture;
        public readonly double TemperatureC;

        public GrowthHourInput(double moisture, double temperatureC)
        {
            Moisture = moisture;
            TemperatureC = temperatureC;
        }
    }

    public enum GrowthHourResult : byte
    {
        /// <summary>Freeze guard active: nothing accumulated.</summary>
        Frozen = 0,

        /// <summary>Hour accumulated, day not complete.</summary>
        Accumulated = 1,

        /// <summary>Hour accumulated and a DailyStep ran.</summary>
        DayCompleted = 2,
    }
}
