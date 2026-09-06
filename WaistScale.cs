namespace HungerMeter;

/// <summary>
/// Pure math for the hunger accumulator. Unlike Milk Meter's FoodScale
/// (scale = f(remaining Well Fed duration), recomputed fresh every
/// tick from a single live reading), this is a running total: the
/// caller (Plugin.cs) owns the actual float value in
/// Configuration.CurrentWaistScale and calls into these two functions
/// to mutate it -
///   - ApplyDecay every frame, proportional to elapsed real time
///   - ApplyFoodConsumed once per detected food-consumed edge
/// Kept as static, stateless functions (mirroring FoodScale.cs's own
/// shape) so the accumulation logic itself is trivially testable
/// independent of Dalamud, ObjectTable polling, or persistence.
/// </summary>
public static class WaistScale
{
    public const float DefaultMinScale = 0.8f;
    public const float DefaultBaselineScale = 1.0f;
    public const float DefaultMaxScale = 1.2f;
    public const float DefaultIncreasePerFood = 0.1f;
    public const float DefaultReductionPerHour = 0.2f;

    /// <summary>
    /// Reduces currentScale by (reductionPerHour * elapsedSeconds / 3600),
    /// clamped so it never drops below minScale. Called every frame with
    /// that frame's own small elapsedSeconds (continuous decay, not a
    /// once-an-hour step) AND on startup with however many real seconds
    /// passed since Configuration.LastUpdateUnixSeconds, so a decay rate
    /// tuned per-hour behaves the same whether it's applied in 1/60th-
    /// second slices while playing or in one large catch-up slice after
    /// the game was closed for a while.
    /// </summary>
    public static float ApplyDecay(float currentScale, float minScale, float reductionPerHour, double elapsedSeconds)
    {
        if (elapsedSeconds <= 0d)
            return currentScale;

        var reduction = (float)(reductionPerHour * (elapsedSeconds / 3600.0));
        var result = currentScale - reduction;
        return result < minScale ? minScale : result;
    }

    /// <summary>
    /// Adds increasePerFood to currentScale once per call - callers are
    /// responsible for calling this exactly once per detected
    /// food-consumed edge, not once per frame the food buff happens to
    /// be active (see Plugin.cs's edge detection). Clamped so it never
    /// exceeds maxScale.
    /// </summary>
    public static float ApplyFoodConsumed(float currentScale, float maxScale, float increasePerFood)
    {
        var result = currentScale + increasePerFood;
        return result > maxScale ? maxScale : result;
    }

    /// <summary>
    /// Clamps an arbitrary scale value into [minScale, maxScale] -
    /// used defensively whenever the configured min/max range itself
    /// might have changed since CurrentWaistScale was last written
    /// (e.g. the user drags Maximum below the current live value).
    /// </summary>
    public static float Clamp(float scale, float minScale, float maxScale)
    {
        if (scale < minScale)
            return minScale;
        if (scale > maxScale)
            return maxScale;
        return scale;
    }

    /// <summary>
    /// Maps the raw scale to a percentage for the server-info-bar
    /// display, per spec: Minimum -> 0%, Baseline -> 100%, Maximum ->
    /// 200%. Baseline is not necessarily the exact midpoint of
    /// Minimum/Maximum (all three are independently configurable
    /// sliders), so this is two separate linear segments rather than
    /// one single formula across the whole range - Minimum..Baseline
    /// maps onto 0%..100%, and Baseline..Maximum maps onto 100%..200%,
    /// each with its own span.
    ///
    /// baselineScale is defensively clamped into [minScale, maxScale]
    /// for purposes of this calculation only (not mutating the
    /// caller's actual config) - if someone sets Baseline outside the
    /// Min/Max range, the two segments would otherwise have a
    /// zero-or-negative span and produce a nonsensical percentage
    /// rather than just clamping to the nearest sensible end.
    /// </summary>
    public static float ComputePercent(float scale, float minScale, float baselineScale, float maxScale)
    {
        var clampedBaseline = Clamp(baselineScale, minScale, maxScale);

        if (scale <= clampedBaseline)
        {
            var span = clampedBaseline - minScale;
            if (span <= 0f)
                return 100f;

            var fraction = (scale - minScale) / span;
            return fraction * 100f;
        }
        else
        {
            var span = maxScale - clampedBaseline;
            if (span <= 0f)
                return 100f;

            var fraction = (scale - clampedBaseline) / span;
            return 100f + fraction * 100f;
        }
    }

    /// <summary>
    /// Moves currentApplied toward target by at most
    /// (maxDeltaPerSecond * deltaSeconds), in whichever direction
    /// target lies - never overshoots past target in either
    /// direction. Used so a food-consumed jump (an instant change to
    /// the underlying target/CurrentWaistScale) shows up as a gradual
    /// ramp on the value actually pushed to Customize+ and displayed
    /// (AppliedWaistScale), instead of snapping immediately. Also
    /// smooths decay's own already-gradual change, though decay's
    /// natural per-second rate is normally well under
    /// maxDeltaPerSecond already, so this only meaningfully affects
    /// the food-consumed case in practice.
    /// </summary>
    public static float Ease(float currentApplied, float target, float maxDeltaPerSecond, float deltaSeconds)
    {
        var maxStep = maxDeltaPerSecond * deltaSeconds;
        if (maxStep <= 0f)
            return currentApplied;

        var diff = target - currentApplied;
        if (diff > maxStep)
            return currentApplied + maxStep;
        if (diff < -maxStep)
            return currentApplied - maxStep;
        return target;
    }

    /// <summary>
    /// Reflects a scale value around baselineScale - used when
    /// Configuration.InvertWaistScalingDirection is on, to compensate
    /// for Customize+ visibly shrinking the waist for a LARGER
    /// "Scaling" multiplier on at least one tested character (the
    /// opposite of every other bone this project has scaled). Leaves
    /// baselineScale itself unchanged (100% still looks like your
    /// normal, un-adjusted size either way) while swapping which
    /// physical multiplier gets sent for the Minimum vs. Maximum ends.
    /// </summary>
    public static float MirrorAroundBaseline(float scale, float baselineScale) =>
        (2f * baselineScale) - scale;
}
