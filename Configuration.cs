using Dalamud.Configuration;
using Dalamud.Plugin;

namespace HungerMeter;

/// <summary>
/// Persisted plugin settings, plus the persisted state of the hunger
/// accumulator itself (CurrentWaistScale / LastUpdateUnixSeconds).
///
/// Unlike Milk Meter's Food mode - a pure function of "how much Well
/// Fed time is left right now", recomputed fresh every tick - this
/// plugin's scale is a running total that only ever changes via
/// decay-over-time or a food-consumed event. That means the value
/// itself has to be saved and reloaded like any other setting (see
/// Plugin.cs's OnFrameworkUpdate), and closing the game for an hour
/// and reopening it should apply that hour's worth of decay
/// retroactively rather than silently resetting to Baseline - hence
/// LastUpdateUnixSeconds below.
/// </summary>
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Same intent as Milk Meter's ScalingPaused - freezes decay,
    /// food-consumed increases, and the Customize+ push in place,
    /// without disabling the plugin or hiding the HUD gauge.
    /// </summary>
    public bool ScalingPaused { get; set; } = false;

    // --- The five configurable sliders ---

    /// <summary>Floor the waist scale decays down to and never goes below.</summary>
    public float WaistMinScale { get; set; } = 0.8f;

    /// <summary>
    /// Starting value on first-ever run, and the target of the
    /// "Reset to Baseline" button in the settings window. This is NOT
    /// a value the accumulator eases toward on its own the way a
    /// thermostat setpoint would - it only ever matters at those two
    /// moments. See WaistScale.cs.
    /// </summary>
    public float WaistBaselineScale { get; set; } = 1.0f;

    /// <summary>Ceiling the waist scale grows up to and never exceeds.</summary>
    public float WaistMaxScale { get; set; } = 1.2f;

    /// <summary>
    /// Added to the current scale every time a food-consumed event is
    /// detected (see FoodBuffTracker.cs / Plugin.cs's edge detection),
    /// clamped at WaistMaxScale.
    /// </summary>
    public float WaistIncreasePerFood { get; set; } = 0.1f;

    /// <summary>
    /// Subtracted per hour of real elapsed time - applied continuously
    /// every frame proportional to elapsed seconds, not in discrete
    /// hourly steps (see WaistScale.ApplyDecay) - clamped at
    /// WaistMinScale.
    /// </summary>
    public float WaistReductionPerHour { get; set; } = 0.2f;

    // --- Persisted accumulator state ---

    /// <summary>
    /// The actual running scale value. float.NaN is the
    /// "never initialized" sentinel - Plugin.cs seeds it from
    /// WaistBaselineScale the first time it sees NaN, rather than this
    /// field just defaulting straight to 1.0f, so a config saved
    /// before this field existed (or a hand-edited/corrupted one)
    /// falls back to whatever WaistBaselineScale is currently set to,
    /// not a hardcoded value that could silently disagree with it.
    /// </summary>
    public float CurrentWaistScale { get; set; } = float.NaN;

    /// <summary>
    /// Unix seconds (UtcNow) as of the last time decay was applied.
    /// Used to retroactively apply decay for real time elapsed while
    /// the plugin/game wasn't running. Null (not 0) means "never run
    /// before" - so a config saved before this field existed doesn't
    /// get treated as "last updated at the Unix epoch" and apply
    /// decades of decay on its first load.
    /// </summary>
    public double? LastUpdateUnixSeconds { get; set; }

    // --- HUD gauge ---

    /// <summary>Whether the draggable chicken-leg HUD gauge is drawn at all.</summary>
    public bool ShowHudGauge { get; set; } = true;

    /// <summary>When true, the gauge window ignores drag input - toggle this once you've got it where you want it.</summary>
    public bool HudLocked { get; set; } = false;

    public float HudPositionX { get; set; } = 100f;
    public float HudPositionY { get; set; } = 100f;

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
