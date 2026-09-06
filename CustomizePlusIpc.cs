using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace HungerMeter;

/// <summary>
/// Every call this plugin makes into Customize+ lives in this one
/// class - carried over method-for-method from Milk Meter's own
/// CustomizePlusIpc.cs (same IPC surface, same error-code shapes,
/// same baseline-multiplier design), with only the bone target
/// changed from chest to waist.
///
/// BASELINE SCALING: same reasoning as Milk Meter - writing an
/// absolute waist scale into the temporary profile would look wrong
/// if your own permanent Customize+ profile already scales the waist
/// away from the game's raw default. CaptureBaseline reads your
/// active profile's current waist scale once and treats every value
/// this plugin computes as a MULTIPLIER on that baseline, not an
/// absolute value.
///
/// CROSS-PLUGIN CONFLICT (found and fixed): SetTemporaryProfileOnCharacter
/// does NOT merge with whatever temp profile is already active - it
/// fully REPLACES the resolved bone set with only what's in the
/// payload passed to it. A payload naming only the waist bone
/// therefore blanks out every other bone another plugin (e.g. Milk
/// Meter's chest bones) - or your own permanent profile - had set, the
/// instant it's pushed. Milk Meter had this exact bug on its own
/// chest-bone push and was fixed with an identical read-merge-write;
/// SetWaistScale below does the same thing here: read whatever bones
/// are part of the CURRENTLY ACTIVE profile right before pushing,
/// overwrite/insert only the waist entry within that full set, and
/// push the merged result - so this plugin's own push no longer
/// erases another plugin's bones, and (as long as that other plugin
/// does the same) its pushes stop erasing this plugin's waist bone
/// too.
///
/// UNVERIFIED: the exact bone name for the waist. Milk Meter's own
/// chest implementation used "j_mune_l"/"j_mune_r" (confirmed against
/// a real exported template), but that template only showed leg and
/// chest bones, not the waist. "j_kosi" is the best-guess single-bone
/// name (koshi = waist/hip in the FFXIV/Anamnesis skeleton naming
/// convention this game's rig is documented to follow, as opposed to
/// chest's left/right pair) but is NOT independently confirmed the
/// way the chest bones were. If scale doesn't visibly affect the
/// waist after installing this plugin, wrong bone name is the first
/// thing to check - use /hungermeter dumpprofile (or open a template
/// directly in Customize+'s own bone editor UI and look at what it
/// calls the waist bone on your character's body type) to find the
/// real name and update WaistBoneName below.
/// </summary>
public sealed class CustomizePlusIpc : IDisposable
{
    private readonly IPluginLog log;

    private readonly ICallGateSubscriber<(int, int)> apiVersion;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> getActiveProfile;
    private readonly ICallGateSubscriber<Guid, (int, string?)> getProfileById;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> setTemporaryProfile;
    private readonly ICallGateSubscriber<ushort, int> revertProfile;

    // See the class doc comment above re: this name being an educated
    // guess, not an independently confirmed one the way Milk Meter's
    // chest bones were.
    private const string WaistBoneName = "j_kosi";

    private ushort? objectIndex;
    private (float X, float Y, float Z)? baselineWaistScale;

    public CustomizePlusIpc(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.log = log;

        apiVersion = pluginInterface.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        getActiveProfile = pluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>(
            "CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        getProfileById = pluginInterface.GetIpcSubscriber<Guid, (int, string?)>(
            "CustomizePlus.Profile.GetByUniqueId");
        setTemporaryProfile = pluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>(
            "CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        revertProfile = pluginInterface.GetIpcSubscriber<ushort, int>(
            "CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");

        try
        {
            var (major, minor) = apiVersion.InvokeFunc();
            log.Information($"[HungerMeter] Customize+ IPC version {major}.{minor}");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[HungerMeter] Could not reach Customize+ - is it installed and enabled?");
        }
    }

    /// <summary>
    /// Target character, identified by Dalamud object table index (not
    /// name). Captures the baseline waist scale the first time it's
    /// called, or again if the character changes (e.g. switching alts).
    /// </summary>
    public void SetCharacterObjectIndex(ushort index)
    {
        var changed = objectIndex != index;
        objectIndex = index;

        if (changed || baselineWaistScale is null)
            CaptureBaseline();
    }

    /// <summary>
    /// Re-reads the waist scale from whatever Customize+ profile is
    /// currently active on the character, and caches it as the
    /// baseline that this plugin's own scale multiplies against.
    /// Defaults to (1,1,1) if there's no active profile, no override
    /// on the waist bone, or the call fails.
    /// </summary>
    public void CaptureBaseline()
    {
        if (objectIndex is null)
            return;

        try
        {
            var (err, profileId) = getActiveProfile.InvokeFunc(objectIndex.Value);
            if (err != 0 || profileId is null)
            {
                log.Information("[HungerMeter] No active Customize+ profile found - using (1,1,1) baseline waist scale.");
                baselineWaistScale = (1f, 1f, 1f);
                return;
            }

            var (err2, json) = getProfileById.InvokeFunc(profileId.Value);
            if (err2 != 0 || string.IsNullOrEmpty(json))
            {
                baselineWaistScale = (1f, 1f, 1f);
                return;
            }

            baselineWaistScale = ParseWaistScale(json) ?? (1f, 1f, 1f);
            log.Information($"[HungerMeter] Captured baseline waist scale: " +
                $"X={baselineWaistScale.Value.X:F3} Y={baselineWaistScale.Value.Y:F3} Z={baselineWaistScale.Value.Z:F3}");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[HungerMeter] Failed to capture baseline waist scale - defaulting to (1,1,1)");
            baselineWaistScale = (1f, 1f, 1f);
        }
    }

    /// <summary>
    /// Push a temporary bone-scale override for the waist bone,
    /// computed as (your baseline waist scale) * multiplier - merged
    /// into whatever OTHER bones are part of the currently active
    /// profile right now (see the class doc comment's CROSS-PLUGIN
    /// CONFLICT note for why this can't be a bare waist-only payload).
    /// </summary>
    public void SetWaistScale(float multiplier)
    {
        if (objectIndex is null)
            return;

        var (bx, by, bz) = baselineWaistScale ?? (1f, 1f, 1f);
        var profileJson = BuildMergedTemplateJson(bx * multiplier, by * multiplier, bz * multiplier);

        try
        {
            var (errorCode, _) = setTemporaryProfile.InvokeFunc(objectIndex.Value, profileJson);
            if (errorCode != 0)
                log.Warning($"[HungerMeter] Customize+ SetTemporaryProfileOnCharacter returned error {errorCode}");
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[HungerMeter] Failed to push waist scale to Customize+");
        }
    }

    public void RevertWaistScale()
    {
        if (objectIndex is null)
            return;

        try
        {
            revertProfile.InvokeFunc(objectIndex.Value);
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[HungerMeter] Failed to revert Customize+ temporary profile");
        }
    }

    /// <summary>
    /// Fetches and returns the raw JSON of whatever profile is
    /// currently active on the character - for manual inspection via
    /// /xllog when diagnosing the profile JSON schema, or checking the
    /// real waist bone name if WaistBoneName above turns out wrong.
    /// </summary>
    public string DumpActiveProfileJson()
    {
        if (objectIndex is null)
            return "(no character set yet)";

        try
        {
            var (err, profileId) = getActiveProfile.InvokeFunc(objectIndex.Value);
            if (err != 0 || profileId is null)
                return $"(no active profile, GetActiveProfileIdOnCharacter error {err})";

            var (err2, json) = getProfileById.InvokeFunc(profileId.Value);
            if (err2 != 0 || json is null)
                return $"(GetByUniqueId error {err2})";

            return json;
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[HungerMeter] Failed to dump active profile");
            return "(exception - see /xllog warning above)";
        }
    }

    /// <summary>
    /// Parses a Customize+ template JSON string looking for the waist
    /// bone's Scaling - same "Bones -> boneName -> Translation/Rotation/
    /// Scaling" schema confirmed for Milk Meter's chest bones.
    /// </summary>
    private static (float, float, float)? ParseWaistScale(string profileJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(profileJson);
            if (!doc.RootElement.TryGetProperty("Bones", out var bones))
                return null;

            if (!bones.TryGetProperty(WaistBoneName, out var bone))
                return null;
            if (!bone.TryGetProperty("Scaling", out var scaling))
                return null;

            var x = scaling.GetProperty("X").GetSingle();
            var y = scaling.GetProperty("Y").GetSingle();
            var z = scaling.GetProperty("Z").GetSingle();
            return (x, y, z);
        }
        catch
        {
            // Schema didn't match - caller falls back to (1,1,1).
        }

        return null;
    }

    /// <summary>
    /// Reads whatever bones are part of the CURRENTLY ACTIVE profile -
    /// which reflects any other plugin's own temp-profile edits (e.g.
    /// Milk Meter's chest bones), not just this character's permanent
    /// Customize+ profile - clones that bone set, replaces/inserts only
    /// the waist bone entry with the given values, and returns the
    /// full merged JSON payload to push. Falls back to a waist-only
    /// payload if reading the active profile fails for any reason -
    /// better to still apply our own change than silently do nothing,
    /// even though that fallback reintroduces the blank-out risk for
    /// that one push.
    /// </summary>
    private string BuildMergedTemplateJson(float x, float y, float z)
    {
        var bones = new JsonObject();

        try
        {
            if (objectIndex is { } index)
            {
                var (err, profileId) = getActiveProfile.InvokeFunc(index);
                if (err == 0 && profileId is not null)
                {
                    var (err2, json) = getProfileById.InvokeFunc(profileId.Value);
                    if (err2 == 0 && !string.IsNullOrEmpty(json))
                    {
                        var parsed = JsonNode.Parse(json);
                        // DeepClone() so the extracted "Bones" object is
                        // fully detached from the parsed document - a
                        // JsonNode can only ever belong to one parent,
                        // and we're about to attach it to a brand new
                        // root object below.
                        if (parsed?["Bones"] is JsonObject existingBones)
                            bones = (JsonObject)existingBones.DeepClone();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Warning(ex, "[HungerMeter] Failed to read active profile for merge - pushing a waist-only payload instead.");
            bones = new JsonObject();
        }

        // Deliberately omits the top-level Version/UniqueId/
        // CreationDate/ModifiedDate/IsWriteProtected fields that
        // persisted template files on disk have - those are
        // file-storage metadata, not something the temporary-profile
        // IPC call needs.
        bones[WaistBoneName] = new JsonObject
        {
            ["Translation"] = new JsonObject { ["X"] = 0.0, ["Y"] = 0.0, ["Z"] = 0.0 },
            ["Rotation"] = new JsonObject { ["X"] = 0.0, ["Y"] = 0.0, ["Z"] = 0.0 },
            ["Scaling"] = new JsonObject { ["X"] = (double)x, ["Y"] = (double)y, ["Z"] = (double)z },
        };

        var root = new JsonObject { ["Bones"] = bones };
        return root.ToJsonString();
    }

    public void Dispose()
    {
        // Nothing to unsubscribe - ICallGateSubscriber instances are
        // cleaned up when the DalamudPluginInterface they came from
        // goes away.
    }
}
