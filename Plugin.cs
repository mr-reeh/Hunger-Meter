using System;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace HungerMeter;

/// <summary>
/// Entry point. A deliberately barebones sibling of Milk Meter: one
/// signal (the Well Fed food buff), one Customize+ target (the waist
/// bone), and one mechanic - decay over time, bumped up on every food
/// consumed. No mode switching, no HUD sound/particle effects, no Job
/// mode mini-game, no ImGui HUD window; just the accumulator and a
/// percentage on the server info bar (see DtrBarDisplay.cs).
/// </summary>
public sealed class Plugin : IDalamudPlugin
{
    public string Name => "Hunger Meter";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;

    private const string CommandName = "/hungermeter";
    private const string ShortCommandName = "/hunger";

    public Configuration Configuration { get; }
    private readonly CustomizePlusIpc customizePlus;
    private readonly FoodBuffTracker foodTracker;
    private readonly SettingsWindow settingsWindow;
    private readonly DtrBarDisplay dtrBarDisplay;

    // Only push an update to Customize+ when the applied scale actually
    // changes by a meaningful amount, and at most several times a
    // second - same throttle Milk Meter uses, for the same reason (IPC
    // calls aren't free, and Customize+ has to reapply the whole
    // skeleton edit every time we call it).
    private const float MinScaleDelta = 0.0005f;
    private const double MinSecondsBetweenPushes = 0.1;

    private float lastPushedScale = -1f;
    private double lastPushTime = -1d;

    // Edge-detection state for "a food item was just consumed", built
    // on top of FoodBuffTracker's plain state reader. Two distinct
    // edges both count as a consumption event:
    //   - the buff transitions from not-active to active (first bite)
    //   - the buff is already active and RemainingSeconds jumps UP
    //     compared to last frame (eating a second item while still
    //     buffed, refreshing/extending the timer) - remaining time
    //     only ever counts down on its own, so any rise can only mean
    //     a new food item was just consumed.
    // Mirrors the rising-edge-plus-"value moved the wrong way for
    // passive decay" pattern Milk Meter's own Plugin.cs uses for GCD/
    // ability-use detection (see its lastOnCooldownByAbility /
    // lastElapsedByAbility comments) - same idea, applied to a status
    // effect's remaining time instead of an action's cooldown.
    private bool lastFoodBuffActive;
    private float? lastRemainingSeconds;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        // Seed both the target (CurrentWaistScale) and the eased,
        // actually-displayed/pushed value (AppliedWaistScale) on
        // first-ever run - see each field's own doc comment for why
        // NaN (not a hardcoded 1.0f) is the sentinel checked here.
        if (float.IsNaN(Configuration.CurrentWaistScale))
        {
            Configuration.CurrentWaistScale = Configuration.WaistBaselineScale;
            Configuration.Save();
        }
        if (float.IsNaN(Configuration.AppliedWaistScale))
        {
            Configuration.AppliedWaistScale = Configuration.WaistBaselineScale;
            Configuration.Save();
        }

        customizePlus = new CustomizePlusIpc(PluginInterface, Log);
        foodTracker = new FoodBuffTracker(ObjectTable);
        settingsWindow = new SettingsWindow(
            Configuration,
            () => Configuration.AppliedWaistScale,
            () => lastPushedScale,
            () => foodTracker.GetFoodBuffState(),
            ResetToBaseline);
        dtrBarDisplay = new DtrBarDisplay(DtrBar);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "'/hungermeter' toggles the settings window. 'status' prints the current scale. "
                + "'config' opens the settings window. 'reset' resets scale to Baseline. "
                + "'dumpprofile' prints your active Customize+ profile's raw JSON to /xllog "
                + "(useful for confirming the real waist bone name - see CustomizePlusIpc.cs).",
        });
        CommandManager.AddHandler(ShortCommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Alias for /hungermeter.",
        });

        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += settingsWindow.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += OnOpenConfigUi;
    }

    private void OnOpenConfigUi() => settingsWindow.IsOpen = true;

    private void ResetToBaseline()
    {
        // Instant, not eased - a reset is meant to snap back
        // immediately, not ramp there like a normal food/decay change.
        Configuration.CurrentWaistScale = Configuration.WaistBaselineScale;
        Configuration.AppliedWaistScale = Configuration.WaistBaselineScale;
        Configuration.LastUpdateUnixSeconds = NowUnixSeconds();
        Configuration.Save();
    }

    private void OnCommand(string command, string args)
    {
        args = args.Trim();

        if (args.Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            Log.Information($"[HungerMeter] Target: {Configuration.CurrentWaistScale:F3}  " +
                $"Applied: {Configuration.AppliedWaistScale:F3}  " +
                $"Sent to Customize+: {lastPushedScale:F3}");
            return;
        }

        if (args.Equals("config", StringComparison.OrdinalIgnoreCase))
        {
            settingsWindow.IsOpen = true;
            return;
        }

        if (args.Equals("reset", StringComparison.OrdinalIgnoreCase))
        {
            ResetToBaseline();
            return;
        }

        if (args.Equals("dumpprofile", StringComparison.OrdinalIgnoreCase))
        {
            Log.Information($"[HungerMeter] Active profile JSON:\n{customizePlus.DumpActiveProfileJson()}");
            return;
        }

        settingsWindow.IsOpen = !settingsWindow.IsOpen;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!Configuration.Enabled)
            return;

        if (ObjectTable.LocalPlayer is null)
            return;

        customizePlus.SetCharacterObjectIndex(ObjectTable.LocalPlayer.ObjectIndex);

        var now = NowUnixSeconds();

        // First tick after a plugin load: catch up on decay for
        // whatever real time passed since the game/plugin was last
        // running, rather than silently skipping it. See
        // Configuration.LastUpdateUnixSeconds's own doc comment for
        // why null (not 0) is the "never run before" sentinel.
        var elapsedSeconds = Configuration.LastUpdateUnixSeconds is { } last ? now - last : 0d;
        Configuration.LastUpdateUnixSeconds = now;

        var (foodActive, remainingSeconds) = foodTracker.GetFoodBuffState();

        var consumedEdge = (foodActive && !lastFoodBuffActive)
            || (foodActive && lastFoodBuffActive
                && remainingSeconds is { } r && lastRemainingSeconds is { } lastR && r > lastR + 1f);

        lastFoodBuffActive = foodActive;
        lastRemainingSeconds = remainingSeconds;

        if (!Configuration.ScalingPaused)
        {
            if (elapsedSeconds > 0d)
            {
                Configuration.CurrentWaistScale = WaistScale.ApplyDecay(
                    Configuration.CurrentWaistScale,
                    Configuration.WaistMinScale,
                    Configuration.WaistReductionPerHour,
                    elapsedSeconds);
            }

            if (consumedEdge)
            {
                Configuration.CurrentWaistScale = WaistScale.ApplyFoodConsumed(
                    Configuration.CurrentWaistScale,
                    Configuration.WaistMaxScale,
                    Configuration.WaistIncreasePerFood);
            }

            // Defensive re-clamp in case Min/Max were edited at
            // runtime to a range that no longer contains the current
            // value.
            Configuration.CurrentWaistScale = WaistScale.Clamp(
                Configuration.CurrentWaistScale, Configuration.WaistMinScale, Configuration.WaistMaxScale);

            // Ease the visible/applied value toward that target
            // rather than snapping to it - this is what actually
            // makes a food-consumed jump feel gradual instead of
            // instantaneous. Uses this frame's own small delta (NOT
            // elapsedSeconds above, which can be a large catch-up
            // value after reopening the game) so the ramp rate feels
            // consistent regardless of how long the game was closed.
            var frameDeltaSeconds = (float)framework.UpdateDelta.TotalSeconds;
            Configuration.AppliedWaistScale = WaistScale.Ease(
                Configuration.AppliedWaistScale,
                Configuration.CurrentWaistScale,
                Configuration.WaistChangeRatePerSecond,
                frameDeltaSeconds);

            Configuration.AppliedWaistScale = WaistScale.Clamp(
                Configuration.AppliedWaistScale, Configuration.WaistMinScale, Configuration.WaistMaxScale);
        }

        // The physical multiplier sent to Customize+ - mirrored around
        // Baseline first if InvertWaistScalingDirection is on (see its
        // own doc comment in Configuration.cs for why this exists).
        var physicalScale = Configuration.InvertWaistScalingDirection
            ? WaistScale.MirrorAroundBaseline(Configuration.AppliedWaistScale, Configuration.WaistBaselineScale)
            : Configuration.AppliedWaistScale;

        var delta = MathF.Abs(physicalScale - lastPushedScale);
        var dueForPush = now - lastPushTime >= MinSecondsBetweenPushes;

        if ((delta >= MinScaleDelta || lastPushedScale < 0f) && (dueForPush || lastPushedScale < 0f))
        {
            customizePlus.SetWaistScale(physicalScale);
            lastPushedScale = physicalScale;
            lastPushTime = now;
        }

        var percent = WaistScale.ComputePercent(
            Configuration.AppliedWaistScale,
            Configuration.WaistMinScale,
            Configuration.WaistBaselineScale,
            Configuration.WaistMaxScale);
        dtrBarDisplay.Update(Configuration.ShowDtrBarEntry, $"Food: {percent:F0}%");
    }

    private static double NowUnixSeconds() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= settingsWindow.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= OnOpenConfigUi;
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(ShortCommandName);
        customizePlus.RevertWaistScale();
        customizePlus.Dispose();
        dtrBarDisplay.Dispose();
    }
}
