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

    /// <summary>
    /// Caps how fast the visibly-applied scale (AppliedWaistScale) can
    /// move toward the underlying target value (CurrentWaistScale),
    /// in scale units per real second - e.g. the default 0.02 means a
    /// full +0.10 food-consumed jump takes about 5 real seconds to
    /// visibly finish rather than snapping instantly. Applies equally
    /// to decay's own (already gradual) change, though decay's natural
    /// per-second rate is normally far below this cap already, so in
    /// practice this setting is really only felt on food-consumed
    /// jumps. See WaistScale.Ease and Plugin.cs's OnFrameworkUpdate.
    /// </summary>
    public float WaistChangeRatePerSecond { get; set; } = 0.02f;

    /// <summary>
    /// Whether the multiplier actually sent to Customize+ is mirrored
    /// around Baseline (2*Baseline - target) before being applied.
    ///
    /// HISTORY: this was originally added (and defaulted on) after an
    /// apparent inversion was observed on a real character - a larger
    /// Customize+ "Scaling" value seemed to visibly SHRINK the waist.
    /// That observation turned out to be a red herring: it was made
    /// while a separate cross-plugin bug was still active (see
    /// CustomizePlusIpc.cs's own CROSS-PLUGIN CONFLICT note) that was
    /// causing this plugin's own pushes to be intermittently stomped
    /// by another plugin's, corrupting what was actually being tested.
    /// Once that was fixed, a clean test showed normal, un-inverted
    /// behavior (a sub-Baseline value correctly shrinks the waist) -
    /// so this now defaults OFF. Left as a toggle rather than removed
    /// entirely, in case a genuine inversion ever does show up on a
    /// different body type/mod/skeleton setup.
    /// </summary>
    public bool InvertWaistScalingDirection { get; set; } = false;

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
    /// The scale value actually pushed to Customize+ (after mirroring,
    /// if InvertWaistScalingDirection is on) and used for the DTR/
    /// settings-window percent display - eases toward CurrentWaistScale
    /// over time (see WaistScale.Ease) rather than jumping instantly
    /// the way CurrentWaistScale itself does on a food-consumed event.
    /// Same NaN-sentinel/seeding reasoning as CurrentWaistScale above.
    /// </summary>
    public float AppliedWaistScale { get; set; } = float.NaN;

    /// <summary>
    /// Unix seconds (UtcNow) as of the last time decay was applied.
    /// Used to retroactively apply decay for real time elapsed while
    /// the plugin/game wasn't running. Null (not 0) means "never run
    /// before" - so a config saved before this field existed doesn't
    /// get treated as "last updated at the Unix epoch" and apply
    /// decades of decay on its first load.
    /// </summary>
    public double? LastUpdateUnixSeconds { get; set; }

    /// <summary>Whether the percentage entry is shown on the server info bar (DTR bar), next to the clock.</summary>
    public bool ShowDtrBarEntry { get; set; } = true;

    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    public void Save() => pluginInterface?.SavePluginConfig(this);
}
