using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HungerMeter;

/// <summary>
/// Draggable HUD gauge showing the current waist scale, filling from
/// WaistMinScale to WaistMaxScale. Milk Meter's own HudGaugeWindow.cs
/// draws a genuinely detailed baby-bottle shape (nipple/cap/body, each
/// with its own rounded corners and highlight circles) built up over
/// many iterations of visual tuning - reproducing an equally detailed
/// chicken-leg illustration via raw ImGui draw-list primitives would
/// be a lot of fiddly shape work for a plugin whose whole point is to
/// be a barebones rebuild, so this deliberately draws a much simpler
/// two-shape icon instead (a rounded "bone" plus a filled "meat" oval)
/// - readably a drumstick at a glance, without chasing the bottle's
/// level of polish. Left-clicking the gauge toggles ScalingPaused, the
/// same interaction Milk Meter's own gauge uses.
/// </summary>
public sealed class HudGaugeWindow(Configuration configuration, System.Func<float> getCurrentScale)
{
    private const float BoneWidth = 22f;
    private const float BoneHeight = 70f;
    private const float MeatRadius = 34f;
    private const float BarWidth = 140f;
    private const float BarHeight = 20f;
    private const float Padding = 10f;

    public void Draw()
    {
        if (!configuration.ShowHudGauge)
            return;

        var flags = ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoBackground;

        if (configuration.HudLocked)
            flags |= ImGuiWindowFlags.NoMove;

        var iconWidth = MeatRadius * 2f;
        var totalWidth = iconWidth + Padding + BarWidth;
        var totalHeight = System.MathF.Max(BoneHeight, BarHeight) + Padding * 2f;

        var windowPos = new Vector2(configuration.HudPositionX, configuration.HudPositionY);
        ImGui.SetNextWindowPos(windowPos, configuration.HudLocked ? ImGuiCond.Always : ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(totalWidth, totalHeight), ImGuiCond.Always);

        if (!ImGui.Begin("##HungerMeterGauge", flags))
        {
            ImGui.End();
            return;
        }

        if (!configuration.HudLocked)
        {
            var pos = ImGui.GetWindowPos();
            configuration.HudPositionX = pos.X;
            configuration.HudPositionY = pos.Y;
        }

        var origin = ImGui.GetWindowPos();
        var drawList = ImGui.GetWindowDrawList();

        // --- Chicken leg icon: a rounded "bone" strip with a filled
        // "meat" oval at its top end - two primitives, not an attempt
        // to match the bottle's fidelity (see class doc comment).
        var boneMin = origin + new Vector2(Padding, Padding + (BoneHeight - BoneHeight) * 0.5f);
        var boneCenterX = boneMin.X + BoneWidth * 0.5f;
        var boneTop = new Vector2(boneCenterX, boneMin.Y);
        var boneBottom = new Vector2(boneCenterX, boneMin.Y + BoneHeight);

        var boneColor = ImGui.GetColorU32(new Vector4(0.92f, 0.85f, 0.72f, 1f));
        drawList.AddLine(boneTop, boneBottom, boneColor, BoneWidth * 0.5f);
        drawList.AddCircleFilled(boneBottom, BoneWidth * 0.35f, boneColor);

        var meatCenter = boneTop;
        var meatColor = ImGui.GetColorU32(new Vector4(0.80f, 0.45f, 0.22f, 1f));
        var meatOutline = ImGui.GetColorU32(new Vector4(0.45f, 0.22f, 0.08f, 1f));
        drawList.AddCircleFilled(meatCenter, MeatRadius, meatColor);
        drawList.AddCircle(meatCenter, MeatRadius, meatOutline, 0, 2.5f);

        // --- Fill bar: fraction of the way from WaistMinScale to
        // WaistMaxScale, same clamp-and-lerp shape as Milk Meter's own
        // bottle fill, just drawn as a plain rectangle instead of a
        // rounded bottle body.
        var current = getCurrentScale();
        var range = configuration.WaistMaxScale - configuration.WaistMinScale;
        var fraction = range > 0f
            ? System.Math.Clamp((current - configuration.WaistMinScale) / range, 0f, 1f)
            : 0f;

        var barMin = origin + new Vector2(iconWidth + Padding, Padding + (BoneHeight - BarHeight) * 0.5f);
        var barMax = barMin + new Vector2(BarWidth, BarHeight);

        var trackColor = ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.2f, 0.6f));
        drawList.AddRectFilled(barMin, barMax, trackColor, 4f);

        var fillColor = ImGui.GetColorU32(new Vector4(0.80f, 0.45f, 0.22f, 0.9f));
        var fillMax = new Vector2(barMin.X + BarWidth * fraction, barMax.Y);
        drawList.AddRectFilled(barMin, fillMax, fillColor, 4f);

        var outlineColor = ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.8f));
        drawList.AddRect(barMin, barMax, outlineColor, 4f, ImDrawFlags.None, 1.5f);

        var label = $"{current:F2}";
        var textSize = ImGui.CalcTextSize(label);
        var textPos = barMin + new Vector2((BarWidth - textSize.X) * 0.5f, (BarHeight - textSize.Y) * 0.5f);
        drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)), label);

        // Whole window area is clickable to toggle pause, same
        // interaction Milk Meter's own gauge uses. InvisibleButton +
        // IsItemClicked is standard, well-established Dear ImGui usage
        // for exactly this "make an arbitrary drawn region clickable"
        // pattern, but it's new API surface for this specific
        // codebase (Milk Meter's own gauge instead reads
        // ImGui.IsWindowHovered()/mouse state directly) - worth an
        // eye during testing.
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##hungerMeterGaugeClick", new Vector2(totalWidth, totalHeight));
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            configuration.ScalingPaused = !configuration.ScalingPaused;
            configuration.Save();
        }

        ImGui.End();
    }
}
