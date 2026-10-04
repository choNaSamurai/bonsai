namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>
    /// [STYLE] parameters of the moyogi styling script (BALANCE §1.3.7, PA-102P): the "ideal" an experienced player reaches with
    /// the pruner. DevTools only (balance reference, AC and the future style classifier §9.2); never imposed in the game.
    /// Plain C# so the measurement harness can run it outside Unity.
    /// </summary>
    public sealed class StyleConfig
    {
        /// <summary>Styling sessions: StyleStartDay + n·StyleIntervalDays up to StyleEndDay (D20, 30, …, 120).</summary>
        public int StyleStartDay = 20;
        public int StyleIntervalDays = 10;
        public int StyleEndDay = 120;

        /// <summary>S2: crown-axis nodes above the chain top k4 numbered beyond this are cut (height).</summary>
        public int StyleCrownAxisMax = 2;

        /// <summary>S3: false = slot-0 (intermediate) tiers of chain nodes k ≥ 1 are cut once the bend tier (slot 1) of the same node is not a bud.</summary>
        public bool StyleKeepIntermediateTiers;

        /// <summary>S4: a primary axis keeps PadDepth + StyleBranchAxisExtra nodes (1 bare base + the pad).</summary>
        public int StyleBranchAxisExtra = 1;

        /// <summary>S5: secondary axes (ord 2) keep this many nodes; ord ≥ 3 axes — StyleTertiaryAxisMax.</summary>
        public int StyleSecondaryAxisMax = 2;
        public int StyleTertiaryAxisMax = 1;

        /// <summary>S6: laterals of bare interior nodes (¬InPad) are cut.</summary>
        public bool StyleCutInteriorShoots = true;

        /// <summary>S7: lateral axes of the crown axis (apex dome) keep this many nodes.</summary>
        public int StyleDomeLateralMax = 2;

        public bool IsStyleDay(int day) =>
            day >= StyleStartDay && day <= StyleEndDay && StyleIntervalDays > 0 && (day - StyleStartDay) % StyleIntervalDays == 0;

        public static StyleConfig CreateDefault() => new StyleConfig();
    }
}
