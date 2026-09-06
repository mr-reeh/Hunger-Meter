using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HungerMeter;

/// <summary>
/// Configuration + live monitoring window. Toggled via /hungermeter
/// config, or the gear icon in the plugin installer. Same ImGui
/// binding and Begin/End/SliderFloat pattern as Milk Meter's own
/// SettingsWindow.cs.
/// </summary>
public sealed class SettingsWindow(
    Configuration configuration,
    Func<float> getCurrentScale,
    Func<float> getAppliedScale,
    Func<(bool Active, float? RemainingSeconds)> getFoodState,
    Action resetToBaseline)
{
    public bool IsOpen;

    public void Draw()
    {
        if (!IsOpen)
            return;

        ImGui.SetNextWindowSize(new Vector2(400, 420), ImGuiCond.FirstUseEver);

        if (!ImGui.Begin("Hunger Meter Settings", ref IsOpen))
        {
            ImGui.End();
            return;
        }

        var enabled = configuration.Enabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            configuration.Enabled = enabled;
            configuration.Save();
        }

        var scalingPaused = configuration.ScalingPaused;
        if (ImGui.Checkbox("Scaling Paused", ref scalingPaused))
        {
            configuration.ScalingPaused = scalingPaused;
            configuration.Save();
        }
        ImGui.TextDisabled("Freezes decay and food-consumed increases in place.");

        var showDtrBarEntry = configuration.ShowDtrBarEntry;
        if (ImGui.Checkbox("Show % on Server Info Bar", ref showDtrBarEntry))
        {
            configuration.ShowDtrBarEntry = showDtrBarEntry;
            configuration.Save();
        }

        ImGui.Separator();
        ImGui.Text("Waist Scaling Range");

        var minScale = configuration.WaistMinScale;
        if (ImGui.SliderFloat("Minimum Waist Scaling", ref minScale, 0.10f, 2.00f, "%.2f"))
        {
            if (minScale > configuration.WaistMaxScale)
                minScale = configuration.WaistMaxScale;
            configuration.WaistMinScale = minScale;
            configuration.CurrentWaistScale = WaistScale.Clamp(configuration.CurrentWaistScale, configuration.WaistMinScale, configuration.WaistMaxScale);
            configuration.AppliedWaistScale = WaistScale.Clamp(configuration.AppliedWaistScale, configuration.WaistMinScale, configuration.WaistMaxScale);
            configuration.Save();
        }

        var baselineScale = configuration.WaistBaselineScale;
        if (ImGui.SliderFloat("Baseline Waist Scale", ref baselineScale, 0.10f, 2.00f, "%.2f"))
        {
            configuration.WaistBaselineScale = baselineScale;
            configuration.Save();
        }
        ImGui.TextDisabled("Only used as the starting value on first run, and by the Reset button below - not a value scaling is pulled toward.");

        var maxScale = configuration.WaistMaxScale;
        if (ImGui.SliderFloat("Maximum Waist Scaling", ref maxScale, 0.10f, 3.00f, "%.2f"))
        {
            if (maxScale < configuration.WaistMinScale)
                maxScale = configuration.WaistMinScale;
            configuration.WaistMaxScale = maxScale;
            configuration.CurrentWaistScale = WaistScale.Clamp(configuration.CurrentWaistScale, configuration.WaistMinScale, configuration.WaistMaxScale);
            configuration.AppliedWaistScale = WaistScale.Clamp(configuration.AppliedWaistScale, configuration.WaistMinScale, configuration.WaistMaxScale);
            configuration.Save();
        }

        var invertDirection = configuration.InvertWaistScalingDirection;
        if (ImGui.Checkbox("Invert Waist Scaling Direction", ref invertDirection))
        {
            configuration.InvertWaistScalingDirection = invertDirection;
            configuration.Save();
        }
        ImGui.TextDisabled("On if a larger scale value visibly SHRINKS your waist instead of growing it - flip this if scaling ever looks backwards.");

        ImGui.Separator();
        ImGui.Text("Rates");

        var increasePerFood = configuration.WaistIncreasePerFood;
        if (ImGui.SliderFloat("Scaling Increase Per Food Eaten", ref increasePerFood, 0.00f, 1.00f, "%.2f"))
        {
            configuration.WaistIncreasePerFood = increasePerFood;
            configuration.Save();
        }

        var reductionPerHour = configuration.WaistReductionPerHour;
        if (ImGui.SliderFloat("Scaling Reduction Per Hour", ref reductionPerHour, 0.00f, 1.00f, "%.2f"))
        {
            configuration.WaistReductionPerHour = reductionPerHour;
            configuration.Save();
        }

        var changeRate = configuration.WaistChangeRatePerSecond;
        if (ImGui.SliderFloat("Increase Ramp Rate (Scale/Second)", ref changeRate, 0.001f, 0.50f, "%.3f"))
        {
            configuration.WaistChangeRatePerSecond = changeRate;
            configuration.Save();
        }
        ImGui.TextDisabled("How fast a food-consumed jump ramps in, instead of applying instantly - lower is slower.");

        ImGui.Separator();
        ImGui.Text("Status");

        var displayed = getCurrentScale();
        var sentToCustomizePlus = getAppliedScale();
        var percent = WaistScale.ComputePercent(displayed, configuration.WaistMinScale, configuration.WaistBaselineScale, configuration.WaistMaxScale);
        ImGui.Text($"Displayed scale: {displayed:F3} ({percent:F0}%)");
        ImGui.Text($"Sent to Customize+: {sentToCustomizePlus:F3}");

        var (foodActive, remaining) = getFoodState();
        ImGui.Text(foodActive
            ? $"Well Fed: active ({remaining:F0}s remaining)"
            : "Well Fed: not active");

        if (ImGui.Button("Reset to Baseline"))
            resetToBaseline();

        ImGui.End();
    }
}
