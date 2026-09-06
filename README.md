# Hunger Meter

A barebones Dalamud plugin, sibling to [Milk Meter](https://github.com/mr-reeh/milk-meter),
that drives your character's waist size (via [Customize+](https://github.com/Aether-Tools/CustomizePlus))
from your **Well Fed** food buff.

## How it works

- Scale starts at **Baseline** (default `1.0`).
- Every real-world hour that passes, it decays toward **Minimum** (default `0.8`) by the
  configured **Scaling Reduction Over Time** rate (default `0.2`/hour) - applied
  continuously, not in once-an-hour jumps.
- Every time you eat a food item (whether that's your first bite or refreshing an
  already-active buff), scale jumps up toward **Maximum** (default `1.2`) by the
  configured **Scaling Increase Per Food Eaten** amount (default `0.1`).
- All five values - Minimum, Baseline, Maximum, Increase Per Food, Reduction Per Hour -
  are sliders in the settings window.

Your progress persists across game restarts: closing the game for a while and reopening
it applies that elapsed time's decay retroactively, rather than resetting to Baseline.

## The server info bar display

Your current scale is shown as a percentage on the server info bar (the row next to the
clock, shared with FPS counters/gil trackers/etc.) - `Minimum` maps to `0%`, `Baseline`
maps to `100%`, and `Maximum` maps to `200%`. Toggle it off in the settings window if you'd
rather not see it there.

## Getting started

**Requires [Customize+](https://github.com/Aether-Tools/CustomizePlus)** to already be
installed - this plugin applies a temporary scale on top of your existing Customize+
profile (nothing else you've scaled is lost, and nothing is changed permanently).

**Everything is local** - nobody else sees your waist size change.

### Installing

1. In-game, open the plugin installer, go to **Settings → Experimental → Custom Plugin
   Repositories**.
2. Add this URL: `https://raw.githubusercontent.com/mr-reeh/Hunger-Meter/main/repo.json`
3. Save, then search for **Hunger Meter** in the plugin installer and install it.

### Commands

- `/hungermeter` (or the short form, `/hunger`) — toggles the settings window.
- `/hungermeter status` — prints the current scale to `/xllog`.
- `/hungermeter reset` — resets scale to Baseline.
- `/hungermeter dumpprofile` — prints your active Customize+ profile's raw JSON, useful
  for confirming the real waist bone name if scaling doesn't seem to affect anything (see
  the comment at the top of `CustomizePlusIpc.cs` - the bone name used, `j_kosi`, is an
  educated guess carried over from Milk Meter's own chest-bone verification process, not
  independently confirmed the same way).

## ⚠️ Not yet verified

Milk Meter's chest bones (`j_mune_l`/`j_mune_r`) were confirmed against a real exported
Customize+ template. This project's waist bone (`j_kosi`) has **not** been independently
confirmed the same way - if scale changes don't visibly affect your waist after
installing, this is the first thing to check. See `/hungermeter dumpprofile` above.

## 🤖 AI Assistance & Attribution
This project is AI-assisted, adapted from Milk Meter.
* **Core Coding & Architecture:** Assisted by [Anthropic's Claude](https://claude.ai)
