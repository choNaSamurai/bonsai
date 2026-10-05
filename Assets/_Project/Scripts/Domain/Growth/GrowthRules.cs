namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// Version of the growth rules (ADR-0003 §8): incremented in the same change that alters the growth result for the same
    /// (genome, history). 1 — PA-102S (BALANCE §1.2); 2 — PA-102P (BALANCE §1.3); 3 — PA-102Q (BALANCE §1.4); 4 — PA-102R (BALANCE §1.5). Saved as <c>rulesVersion</c> (ADR-0008).
    /// </summary>
    public static class GrowthRules
    {
        public const int Version = 4;
    }
}
