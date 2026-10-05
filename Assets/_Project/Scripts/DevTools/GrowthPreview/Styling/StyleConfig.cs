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

        /// <summary>⚑R BALANCE §1.5.3(в) S8: minimum gap (units) between the tip Y of two same-side adjacent pads; ≤ cuts per pair per pass.
        /// Ranges 10–18 / 1–3.</summary>
        public double StylePadGapUnits = 18.0; // PA-102R 14 → PA-102T tuning 18 (range 10–18; #5/#6 vs the dome): TD-075
        public int StylePadGapCutsPerPass = 2;

        /// <summary>⚑T BALANCE §1.6.2(б): S3 keeps slot 0 of the chain top — the crown shoulder (#7), a dome lateral (S7 limits it like the others).</summary>
        public bool StyleKeepCrownShoulder = true;

        /// <summary>⚑T §1.6.3(а) S10 «pad ceiling»: a tier group (t0…t5, dome) with N_g &gt; ceil(StyleTierCapRatio·T_g) loses end nodes (the highest
        /// tip among ord ≥ 2 first, then the newest) until N_g ≤ T_g; the first node of a primary / the shoulder and the crown axis — never.
        /// 0 = off. Range 1.00–1.25.</summary>
        public double StyleTierCapRatio = 1.10;

        /// <summary>S8 geometry: the preview's Bezier Curvature offset (TreeRenderConfig.CurvatureOffsetRatio).</summary>
        public double CurvatureOffsetRatio = 0.25;

        public bool IsStyleDay(int day) =>
            day >= StyleStartDay && day <= StyleEndDay && StyleIntervalDays > 0 && (day - StyleStartDay) % StyleIntervalDays == 0;

        public static StyleConfig CreateDefault() => new StyleConfig();
    }
}
