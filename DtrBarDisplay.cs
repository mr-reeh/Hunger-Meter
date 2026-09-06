using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;

namespace HungerMeter;

/// <summary>
/// Wraps a single Dalamud "server info bar" (DTR bar) entry - the row
/// of text next to the server clock, shared with other plugins like
/// FPS counters and gil trackers - showing the current waist scale as
/// a percentage. Replaces the earlier HudGaugeWindow (a draggable
/// ImGui window) entirely per request; this plugin no longer draws
/// anything of its own in the game viewport.
///
/// IDtrBar/IDtrBarEntry is new API surface for this project - Milk
/// Meter's own HUD used a plain ImGui window instead of the server
/// info bar. Confirmed directly against dalamud.dev's API reference
/// rather than assumed: IDtrBar itself lives in Dalamud.Plugin.Services
/// (same namespace as IObjectTable/IFramework/etc. - an earlier pass
/// at this file wrongly guessed Dalamud.Game.Gui.Dtr for it, which is
/// only where the returned IDtrBarEntry lives), Get(title) returns
/// IDtrBarEntry, and Text is typed SeString? - not a plain string, and
/// SeString has no implicit conversion from string, so Update() below
/// builds one explicitly via SeStringBuilder (the documented way to
/// construct one) rather than a direct string assignment.
///
/// No click-to-pause interaction here (Milk Meter's bottle gauge had
/// one) - pausing is still available via the settings window checkbox
/// and `/hungermeter` commands.
/// </summary>
public sealed class DtrBarDisplay : System.IDisposable
{
    private readonly IDtrBarEntry entry;
    private string? lastText;

    public DtrBarDisplay(IDtrBar dtrBar)
    {
        entry = dtrBar.Get("Hunger Meter");
    }

    /// <summary>
    /// Updates the displayed text/visibility. Only actually touches
    /// the entry when something changed, avoiding needless SeString
    /// churn every single frame - same "don't do unnecessary work
    /// every tick" instinct as the Customize+ push throttle in
    /// Plugin.cs.
    /// </summary>
    public void Update(bool shown, string text)
    {
        if (entry.Shown != shown)
            entry.Shown = shown;

        if (shown && text != lastText)
        {
            entry.Text = new SeStringBuilder().AddText(text).Build();
            lastText = text;
        }
    }

    public void Dispose() => entry.Remove();
}
