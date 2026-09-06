using Dalamud.Plugin.Services;

namespace HungerMeter;

/// <summary>
/// Looks at the local player's status effects and reports whether a
/// food ("Well Fed") buff is active, and how much time is left on it.
/// Carried over from Milk Meter's FoodBuffTracker.cs essentially
/// unchanged - this plugin's own "food consumed" event detection
/// (see Plugin.cs) is built entirely on top of the same state reader
/// here (specifically, watching for RemainingSeconds to rise instead
/// of merely tracking whether it taper-scales anything), rather than
/// this class needing to know about "consumption" as a concept itself.
///
/// FFXIV's food buff is applied as a status effect named "Well Fed"
/// (localized) with a per-food-item duration (30 min for normal
/// quality, 45 min for HQ). Matching is done by name rather than a
/// hardcoded ID list because new "Well Fed" status IDs are added with
/// new food items every patch; a fixed ID list would silently go
/// stale. If your client isn't English, swap the comparison for the
/// right localized string, or match on GameData.RowId against Lumina's
/// Status sheet where Name == "Well Fed" for your client language.
/// </summary>
public sealed class FoodBuffTracker(IObjectTable objectTable)
{
    private const string WellFedStatusName = "Well Fed";

    /// <returns>
    /// (true, remainingSeconds) if a food buff is active, otherwise (false, null).
    /// </returns>
    public (bool Active, float? RemainingSeconds) GetFoodBuffState()
    {
        var player = objectTable.LocalPlayer;
        if (player is null)
            return (false, null);

        foreach (var status in player.StatusList)
        {
            var data = status.GameData;
            var row = data.ValueNullable;
            if (row is null)
                continue;

            var name = row.Value.Name.ExtractText();
            if (string.IsNullOrEmpty(name))
                continue;

            if (!name.StartsWith(WellFedStatusName, System.StringComparison.OrdinalIgnoreCase))
                continue;

            // RemainingTime counts down to 0 while the status is active.
            return (true, status.RemainingTime);
        }

        return (false, null);
    }
}
