using PocketArbor.Domain.Growth;

namespace PocketArbor.DevTools.GrowthPreview
{
    /// <summary>Classical styles of BALANCE §1.4.7 / GDD §10.9 (DevTools presets; in the game a style comes only from the player's actions).</summary>
    public enum ShapeStyle
    {
        Moyogi = 0,
        Chokkan = 1,
        Shakan = 2,
    }

    /// <summary>
    /// ⚑Q BALANCE §1.4.7 [STYLE-PROFILE] presets (PA-102Q, AC20): the <see cref="ShapeStyleProfile"/> of each style plus its
    /// <see cref="StyleConfig"/> difference. Plain C# so the measurement harness and tests use the same values.
    /// </summary>
    public static class ShapeStylePresets
    {
        /// <summary>Shakan lean λ, degrees (owner 2026-10-05: range 15–30°, default 25°; clamped by GrowthConfig.TrunkLeanOffsetMaxDeg = 30).</summary>
        public const double ShakanLeanDeg = 25.0;

        /// <summary>Chokkan apex: narrower dome laterals and one more crown-axis node (§1.4.7).</summary>
        public const double ChokkanCrownLateralAngleMeanDeg = 45.0;
        public const int ChokkanStyleCrownAxisMax = 3;

        /// <summary>⚑T BALANCE §1.6.7: the narrower Chokkan apex has a smaller dome node target.</summary>
        public const double ChokkanDomeNodeTarget = 12.0;

        /// <summary>Profile of <paramref name="style"/>; Moyogi = null (plain config fields — bitwise the same tree as <see cref="ShapeStyleProfile.Moyogi"/>).</summary>
        public static ShapeStyleProfile Profile(ShapeStyle style)
        {
            switch (style)
            {
                case ShapeStyle.Chokkan:
                    return new ShapeStyleProfile
                    {
                        Name = "Chokkan", TrunkSwayScale = 0.0, TrunkLeanOffsetDeg = 0.0, TrunkSwayJitterMinScale = 0.1,
                        TrunkBaseSwayRatio = 0.0, CrownLateralAngleMeanDeg = ChokkanCrownLateralAngleMeanDeg,
                        DomeNodeTarget = ChokkanDomeNodeTarget,
                    };
                case ShapeStyle.Shakan:
                    return new ShapeStyleProfile
                    {
                        Name = "Shakan", TrunkSwayScale = 0.5, TrunkLeanOffsetDeg = ShakanLeanDeg, TrunkSwayJitterMinScale = 0.1,
                        TrunkBaseSwayRatio = 0.0,
                    };
                default:
                    return null;
            }
        }

        /// <summary>StyleConfig difference of <paramref name="style"/> on top of <paramref name="baseline"/> (the Moyogi values).</summary>
        public static void ApplyStyle(StyleConfig target, StyleConfig baseline, ShapeStyle style)
        {
            target.StyleCrownAxisMax = style == ShapeStyle.Chokkan ? ChokkanStyleCrownAxisMax : baseline.StyleCrownAxisMax;
        }
    }
}
