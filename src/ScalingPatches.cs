using System;
using HarmonyLib;
using Unity.Netcode;

namespace MoreWizards;

/// <summary>
/// Player-count scaling that isn't about enemies: the shared revive pool and objective fixes.
///
/// What the base game already scales past 4 players without help:
///   - Revive pool: sum of every wizard's own revive stat (8 wizards ~ 2x the revives of 4).
///   - Party XP needed per level: base * partySize^0.95.
///   - Boss/elite health: x(1 + 0.5 * (players - 1)).
///   - Objectives: collectables, hydra healers, pushables, clean-zone size (all linear per player).
///   - Herding targets (extended by LobbyPatches).
/// What it doesn't, and is handled here or in NativePatcher:
///   - Capture-point speed for 5+ players (vanilla: 0x, capture stalls).
///   - Capture/sacrifice zone radius (vanilla: stops growing at 4).
/// </summary>
[HarmonyPatch]
internal static class ScalingPatches
{
    // ---------- Revive pool ----------

    [HarmonyPatch(typeof(ReviveManager), nameof(ReviveManager.InitializeOnServerRpc))]
    [HarmonyPrefix]
    private static void InitRevives_Prefix(ReviveManager __instance, out bool __state)
    {
        // RPC methods run twice on the host (send stage, then execute stage); only the execute stage sets the pool.
        __state = false;
        try { __state = __instance.__rpc_exec_stage == NetworkBehaviour.__RpcExecStage.Execute; }
        catch { }
    }

    [HarmonyPatch(typeof(ReviveManager), nameof(ReviveManager.InitializeOnServerRpc))]
    [HarmonyPostfix]
    private static void InitRevives_Postfix(ReviveManager __instance, bool __state)
    {
        if (!__state) return;
        try
        {
            if (!__instance.IsServer) return;

            float share = Plugin.ExtraPlayerReviveShare.Value;
            int bonus = Plugin.BonusRevivesPerExtraPlayer.Value;
            if (Math.Abs(share - 1f) < 0.001f && bonus == 0) return; // vanilla

            int players = DifficultyPatches.QueryPlayerCount();
            int threshold = Plugin.ScalingStartsAbove.Value;
            int extra = players - threshold;
            if (extra <= 0) return;

            var pool = __instance.CurrentRevives;
            int vanilla = pool.Value;
            // Every wizard contributes about vanilla/players revives. Wizards above the threshold contribute
            // `share` of that, plus a flat bonus each.
            float perWizard = (float)vanilla / players;
            int adjusted = (int)Math.Round(perWizard * (threshold + extra * share)) + bonus * extra;
            adjusted = Math.Max(0, adjusted);
            if (adjusted == vanilla) return;

            pool.Value = adjusted;                       // NetworkVariable: syncs to clients
            __instance.InitializeOnClientsRpc(adjusted); // refresh everyone's revive counter UI

            Plugin.Log.LogInfo($"Revive pool: {vanilla} -> {adjusted} ({players} wizards, {extra} above {threshold})");
        }
        catch (Exception ex) { Plugin.Log.LogError($"Revive pool: {ex}"); }
    }

    // ---------- Capture point speed (non-inlined copy; inlined copies are fixed in NativePatcher) ----------

    [HarmonyPatch(typeof(CapturePoint), nameof(CapturePoint.GetPlayersInRangeSpeedMultiplier))]
    [HarmonyPostfix]
    private static void CaptureSpeed_Postfix(int playersInRange, ref float __result)
    {
        if (!Plugin.FixCaptureSpeed.Value) return;
        if (playersInRange > 4 && __result <= 0f) __result = 2.5f; // vanilla 4-player value
    }
}
