namespace PocketArbor.Domain.Tree
{
    /// <summary>Species id as encoded in the seed genome (BALANCE §8: 0 Pine, 1 Maple, 2 Sakura).</summary>
    public enum SpeciesId : byte
    {
        Kuromatsu = 0,
        Momiji = 1,
        Yamazakura = 2,
    }
}
