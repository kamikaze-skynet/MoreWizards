using System;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Unity.Netcode;

namespace MoreWizards;

/// <summary>
/// Join handshake. Every player's connection payload already carries the game version, which the host
/// checks with BuildVersionProvider.IsIncompatibleWith. We append a MoreWizards marker to our own payload,
/// and during the host's join checks we require that marker with the same mod version.
/// A vanilla or mismatched player is turned away by the game's own "incompatible version" rejection.
/// Outside the host's join checks (e.g. matchmaking lobby filtering) the marker is just stripped,
/// so modded players can still join normal lobbies.
/// </summary>
[HarmonyPatch]
internal static class ModHandshake
{
    internal static string Marker => "|MoreWizards/" + Plugin.Version;
    private const string MarkerPrefix = "|MoreWizards/";

    [ThreadStatic] private static int hostCheckDepth;

    // ---------- Client side: tag our payload ----------

    [HarmonyPatch(typeof(OnlineState), nameof(OnlineState.SetConnectionPayload))]
    [HarmonyPostfix]
    private static void SetConnectionPayload_Postfix()
    {
        try
        {
            var config = NetworkManager.Singleton?.NetworkConfig;
            if (config == null || config.ConnectionData == null) return;

            string json = Encoding.UTF8.GetString((byte[])config.ConnectionData);
            const string key = "\"Version\":\"";
            int start = json.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) { Plugin.Log.LogWarning("Handshake: no Version in connection payload."); return; }
            start += key.Length;
            int end = json.IndexOf('"', start);
            if (end < 0) return;

            string version = StripMarker(json.Substring(start, end - start));
            json = json.Substring(0, start) + version + Marker + json.Substring(end);
            config.ConnectionData = new Il2CppStructArray<byte>(Encoding.UTF8.GetBytes(json));
        }
        catch (Exception ex) { Plugin.Log.LogError($"Handshake (payload): {ex}"); }
    }

    // ---------- Host side: require the marker during join checks ----------

    [HarmonyPatch(typeof(HostState), nameof(HostState.OnApprovalCheck))]
    [HarmonyPrefix]
    private static void Approval_Prefix() => hostCheckDepth++;

    [HarmonyPatch(typeof(HostState), nameof(HostState.OnApprovalCheck))]
    [HarmonyPostfix]
    private static void Approval_Postfix() => hostCheckDepth = Math.Max(0, hostCheckDepth - 1);

    [HarmonyPatch(typeof(HostState), nameof(HostState.GetConnectionStatus), typeof(ConnectionPayload))]
    [HarmonyPrefix]
    private static void Status_Prefix() => hostCheckDepth++;

    [HarmonyPatch(typeof(HostState), nameof(HostState.GetConnectionStatus), typeof(ConnectionPayload))]
    [HarmonyPostfix]
    private static void Status_Postfix() => hostCheckDepth = Math.Max(0, hostCheckDepth - 1);

    [ThreadStatic] private static bool inOriginalCheck;

    [HarmonyPatch(typeof(BuildVersionProvider), nameof(BuildVersionProvider.IsIncompatibleWith))]
    [HarmonyPrefix]
    private static bool IsIncompatibleWith_Prefix(string fullVersionToMatch, ref bool __result)
    {
        if (inOriginalCheck) return true; // our own call below: run the game's check untouched
        try
        {
            string version = fullVersionToMatch ?? "";
            int idx = version.IndexOf(MarkerPrefix, StringComparison.Ordinal);
            string theirMod = idx >= 0 ? version.Substring(idx + MarkerPrefix.Length) : null;

            if (hostCheckDepth > 0 && Plugin.RequireModForJoiners.Value && theirMod != Plugin.Version)
            {
                __result = true; // incompatible -> game rejects the join with its version-mismatch message
                Plugin.Log.LogWarning(theirMod == null
                    ? "Rejected a player without MoreWizards."
                    : $"Rejected a player on MoreWizards {theirMod} (host has {Plugin.Version}).");
                return false;
            }

            if (idx < 0) return true; // no marker: normal check

            // Compare the real game version (marker removed) using the game's own check.
            inOriginalCheck = true;
            try { __result = BuildVersionProvider.IsIncompatibleWith(StripMarker(version)); }
            finally { inOriginalCheck = false; }
            return false;
        }
        catch (Exception ex) { Plugin.Log.LogError($"Handshake (check): {ex}"); }
        return true;
    }

    private static string StripMarker(string v)
    {
        int idx = v.IndexOf(MarkerPrefix, StringComparison.Ordinal);
        return idx >= 0 ? v.Substring(0, idx) : v;
    }
}
