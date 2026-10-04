namespace PocketArbor.Domain.Random
{
    /// <summary>
    /// Deterministic random source (TD-004). Never back this with System.Random or UnityEngine.Random:
    /// seed genomes must produce identical trees on every platform and runtime version.
    /// </summary>
    public interface IRandomSource
    {
        /// <summary>Uniformly distributed 32-bit value.</summary>
        uint NextUInt();

        /// <summary>Uniform integer in [0, bound). Unbiased. <paramref name="bound"/> must be &gt; 0.</summary>
        uint NextUInt(uint bound);

        /// <summary>Uniform double in [0, 1) with 53 bits of precision.</summary>
        double NextDouble();
    }
}
