using System;

namespace PocketArbor.Domain.Tree
{
    /// <summary>
    /// Gameplay-relevant part of the seed genome (BALANCE §8). Encoding/CRC is PA-205; this type only
    /// carries sanitized values. Values are clamped to the genome ranges; NaN/Infinity fall back to defaults.
    /// </summary>
    public readonly struct TreeGenome : IEquatable<TreeGenome>
    {
        // BALANCE §8 genome field ranges (format constants, not tunables).
        public const double VigorMin = 0.5;
        public const double VigorMax = 1.5;
        public const double VigorDefault = 1.0;
        public const double BendBiasMin = -0.6;
        public const double BendBiasMax = 0.6;
        public const double BendBiasDefault = 0.0;

        public readonly uint Seed;
        public readonly SpeciesId Species;
        public readonly double Vigor;
        public readonly double BendBias;

        public TreeGenome(uint seed, SpeciesId species, double vigor = VigorDefault, double bendBias = BendBiasDefault)
        {
            Seed = seed;
            Species = species <= SpeciesId.Yamazakura ? species : SpeciesId.Kuromatsu;
            Vigor = Sanitize(vigor, VigorMin, VigorMax, VigorDefault);
            BendBias = Sanitize(bendBias, BendBiasMin, BendBiasMax, BendBiasDefault);
        }

        static double Sanitize(double value, double min, double max, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return fallback;
            return value < min ? min : value > max ? max : value;
        }

        public bool Equals(TreeGenome other)
        {
            return Seed == other.Seed && Species == other.Species
                && BitConverter.DoubleToInt64Bits(Vigor) == BitConverter.DoubleToInt64Bits(other.Vigor)
                && BitConverter.DoubleToInt64Bits(BendBias) == BitConverter.DoubleToInt64Bits(other.BendBias);
        }

        public override bool Equals(object obj) => obj is TreeGenome other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Seed;
                hash = hash * 31 + (int)Species;
                hash = hash * 31 + Vigor.GetHashCode();
                return hash * 31 + BendBias.GetHashCode();
            }
        }
    }
}
