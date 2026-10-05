using System;

namespace PocketArbor.Domain.Growth
{
    /// <summary>
    /// Pure growth formulas of BALANCE §1. All inputs are clamped; NaN/Infinity never propagate into the tree.
    /// </summary>
    public static class GrowthFormulas
    {
        /// <summary>NaN-safe clamp to [0, 1]: NaN → 0.</summary>
        public static double Clamp01(double value)
        {
            if (!(value > 0.0)) return 0.0; // also catches NaN
            return value > 1.0 ? 1.0 : value;
        }

        /// <summary>NaN-safe clamp: NaN → <paramref name="min"/>.</summary>
        public static double Clamp(double value, double min, double max)
        {
            if (!(value > min)) return min;
            return value > max ? max : value;
        }

        /// <summary>depthScale(d) = max(min, clamp01(1 − d/D_max)^exponent). Never NaN, also for d &gt; D_max.</summary>
        public static double DepthScale(int depth, int maxDepth, double exponent, double minScale)
        {
            double baseValue = maxDepth > 0 ? Clamp01(1.0 - (double)depth / maxDepth) : 0.0;
            double scaled = Math.Pow(baseValue, exponent);
            if (double.IsNaN(scaled)) scaled = 0.0;
            return scaled > minScale ? scaled : minScale;
        }

        public static double DepthScale(int depth, GrowthConfig config) =>
            DepthScale(depth, config.MaxDepth, config.DepthScaleExponent, config.DepthScaleMin);

        /// <summary>depthScale of a fractional depth (⚑Q BALANCE §1.4.1(б): ord-0 nodes use Depth × TrunkChainDepthScaleFactor).</summary>
        public static double DepthScale(double depth, int maxDepth, double exponent, double minScale)
        {
            double baseValue = maxDepth > 0 ? Clamp01(1.0 - depth / maxDepth) : 0.0;
            double scaled = Math.Pow(baseValue, exponent);
            if (double.IsNaN(scaled)) scaled = 0.0;
            return scaled > minScale ? scaled : minScale;
        }

        /// <summary>
        /// ⚑Q BALANCE §1.4.1(б): depthScale of a node by its axis — ord 0 (trunk chain, crown axis): depthScale(Depth·chainDepthFactor);
        /// ord ≥ 1: depthScale(Depth) (TZ). With chainDepthFactor = 1 both coincide.
        /// </summary>
        public static double AxisDepthScale(int depth, bool ord0, double chainDepthFactor, GrowthConfig config) =>
            ord0 ? DepthScale(depth * Clamp(chainDepthFactor, 0.0, 1.0), config.MaxDepth, config.DepthScaleExponent, config.DepthScaleMin)
                : DepthScale(depth, config);

        /// <summary>th_max of a node by its axis (§1.4.1(б)): MaxThickness · AxisDepthScale.</summary>
        public static double MaxThicknessAt(int depth, bool ord0, double chainDepthFactor, GrowthConfig config) =>
            config.MaxThickness * AxisDepthScale(depth, ord0, chainDepthFactor, config);

        /// <summary>th_max(d) = MaxThickness · depthScale(d).</summary>
        public static double MaxThicknessAt(int depth, GrowthConfig config) =>
            config.MaxThickness * DepthScale(depth, config);

        /// <summary>M_soil(W) = clamp01((W − W_dry) / (W_opt − W_dry)). NaN → 0.</summary>
        public static double MoistureFactor(double moisture, double dryThreshold, double optimalMoisture)
        {
            double range = optimalMoisture - dryThreshold;
            if (!(range > 0.0)) return moisture >= optimalMoisture ? 1.0 : 0.0; // degenerate config: step function
            return Clamp01((moisture - dryThreshold) / range);
        }

        /// <summary>
        /// T_factor = max(TempFactorMin, clamp01((T − TempLow)/TempLowRange) · (1 − clamp((T − TempHigh)/TempHighRange, 0, maxSuppression))).
        /// NaN temperature → TempFactorMin.
        /// </summary>
        public static double TemperatureFactor(double temperatureC, GrowthConfig config)
        {
            double floor = Clamp01(config.TempFactorMin);
            if (double.IsNaN(temperatureC)) return floor;

            double cold = config.TempLowRange > 0.0
                ? Clamp01((temperatureC - config.TempLow) / config.TempLowRange)
                : (temperatureC >= config.TempLow ? 1.0 : 0.0);
            double maxSuppression = Clamp01(config.TempHighMaxSuppression);
            double heat = config.TempHighRange > 0.0
                ? Clamp((temperatureC - config.TempHigh) / config.TempHighRange, 0.0, maxSuppression)
                : (temperatureC > config.TempHigh ? maxSuppression : 0.0);
            double factor = cold * (1.0 - heat);
            return factor > floor ? factor : floor;
        }

        /// <summary>
        /// Exponential saturation (BALANCE §1): Δth = (th_max − th)(1 − e^{−rΔt}), r = K·M·depthScale / th_max;
        /// result clamped to [th, max(th, th_max)] — thickness never decreases and never overshoots.
        /// </summary>
        public static double Thicken(double thickness, double maxThickness, double thickeningRate, double moistureFactor, double depthScale, double deltaDays)
        {
            if (double.IsNaN(thickness) || double.IsInfinity(thickness)) return thickness;
            if (!(maxThickness > 0.0) || double.IsInfinity(maxThickness)) return thickness;
            // BALANCE §1: interpretation — clamp [th, th_max] with th > th_max is empty; keep th (never shrink).
            if (thickness >= maxThickness) return thickness;

            double rate = Clamp(thickeningRate, 0.0, double.MaxValue) * Clamp01(moistureFactor) * Clamp01(depthScale) / maxThickness;
            double dt = Clamp(deltaDays, 0.0, double.MaxValue);
            double delta = (maxThickness - thickness) * (1.0 - Math.Exp(-rate * dt));
            double result = thickness + delta;
            if (!(result > thickness)) return thickness; // NaN or no growth
            return result < maxThickness ? result : maxThickness;
        }

        /// <summary>Elongation (BALANCE §1.1 step 3): L += (L_target − L)(1 − e^{−rate·G}).</summary>
        public static double Elongate(double length, double targetLength, double elongationRate, double growthFactor)
        {
            double fraction = 1.0 - Math.Exp(-Clamp(elongationRate, 0.0, double.MaxValue) * Clamp01(growthFactor));
            double result = length + (targetLength - length) * fraction;
            return double.IsNaN(result) || double.IsInfinity(result) ? length : result;
        }

        /// <summary>Spawn weight of a candidate (BALANCE §1.1 step 6.1): w = 1 − depth / D_max.</summary>
        public static double CandidateWeight(int depth, int maxDepth) =>
            maxDepth > 0 ? Clamp01(1.0 - (double)depth / maxDepth) : 0.0;

        /// <summary>Triangular sample tri(m, s) = m + s·(u_a + u_b − 1) ∈ [m − s, m + s].</summary>
        public static double Triangular(double mean, double spread, double ua, double ub) => mean + spread * (ua + ub - 1.0);

        /// <summary>BALANCE §1.2.2: th_at(p, t) = th_p·(1 − (1 − SegmentTipTaper)·t); t and taper clamped to [0, 1].</summary>
        public static double ThicknessAt(double parentThickness, double t, double segmentTipTaper) =>
            parentThickness * (1.0 - (1.0 - Clamp01(segmentTipTaper)) * Clamp01(t));

        /// <summary>
        /// BALANCE §1.2.2 taper ceiling of a depth ≥ 1 node: th_cap = R·th_at(parent, AttachT),
        /// R = ApicalThicknessRatio (apical) / LateralThicknessRatio (lateral).
        /// </summary>
        public static double ThicknessCap(double parentThickness, double attachT, bool apical, GrowthConfig config)
        {
            double ratio = apical ? config.ApicalThicknessRatio : config.LateralThicknessRatio;
            return ratio * ThicknessAt(parentThickness, attachT, config.SegmentTipTaper);
        }

        /// <summary>
        /// ⚑Q BALANCE §1.4.1(б) / §1.4.3: ceiling of a child of an ord-0 parent (trunk chain, crown axis):
        /// apical — chainTaperRatio · th_parent (no th_at); lateral — primaryRatio · th_at(parent, AttachT).
        /// </summary>
        public static double AxisChildThicknessCap(double parentThickness, double attachT, bool apical, double chainTaperRatio, double primaryRatio,
            GrowthConfig config) =>
            apical ? chainTaperRatio * parentThickness : primaryRatio * ThicknessAt(parentThickness, attachT, config.SegmentTipTaper);

        /// <summary>Ceiling of a child by the order of its parent: ord 0 → <see cref="AxisChildThicknessCap"/>, else §1.2.2.</summary>
        public static double ThicknessCap(double parentThickness, double attachT, bool apical, bool parentOrd0, in ShapeParams shape, GrowthConfig config) =>
            parentOrd0
                ? AxisChildThicknessCap(parentThickness, attachT, apical, shape.TrunkChainTaperRatio, shape.PrimaryThicknessRatio, config)
                : ThicknessCap(parentThickness, attachT, apical, config);

        /// <summary>sgn with sgn(0) = +1 (BALANCE §1.2.0).</summary>
        public static double SignNonZero(double value) => value < 0.0 ? -1.0 : 1.0;

        public const double DegToRad = Math.PI / 180.0;

        /// <summary>
        /// ⚑T BALANCE §1.6.1(б) group multiplier B_g of a balanced tier group with raw selection mass <paramref name="groupMass"/> = R_g,
        /// total mass <paramref name="totalMass"/> = R, <paramref name="nodes"/> = N_g and <paramref name="target"/> = T_g:
        /// N &lt; T — max(R_g, TierHungryShare·d·R) / R_g, d = min(1, TierDeficitGain·(T − N)/T); T ≤ N &lt; TierCapRatio·T — TierFedFactor;
        /// N ≥ TierCapRatio·T — TierCapFactor. No target (T ≤ 0) or R_g ≤ 0 → 1. Finite for every input.
        /// </summary>
        public static double TierBalanceFactor(double groupMass, double totalMass, int nodes, int target, GrowthConfig config)
        {
            if (target <= 0 || !(groupMass > 0.0) || double.IsInfinity(groupMass)) return 1.0;
            if (nodes < target)
            {
                double deficit = Math.Min(1.0, Clamp(config.TierDeficitGain, 0.0, double.MaxValue) * (target - nodes) / target);
                double floor = Clamp(config.TierHungryShare, 0.0, 1.0) * deficit * (totalMass > 0.0 && !double.IsInfinity(totalMass) ? totalMass : 0.0);
                return (floor > groupMass ? floor : groupMass) / groupMass;
            }
            return nodes < config.TierCapRatio * target ? Clamp(config.TierFedFactor, 0.0, double.MaxValue) : Clamp(config.TierCapFactor, 0.0, double.MaxValue);
        }
    }
}