using System;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using URandom = UnityEngine.Random;

namespace MoreWizards;

/// <summary>
/// Twin bosses: with more wizards than ScalingStartsAbove, the final boss has a chance to arrive with a copy of itself.
///
/// How the game ends a run: EndBossObjective.SpawnBoss hooks two handlers on the boss it spawns:
///   - health depleted   -> GameplayEndManager.FlagRunAsSuccessful   (closure ...DisplayClass27_0 "Boolean_DamageInfo")
///   - despawn started   -> HandleObjectiveEndedRpc + victory sequence (closure ...DisplayClass27_0 "PDM_0")
/// With a twin alive, those handlers are held back when the first boss dies, the boss health bar switches to the twin,
/// and the held handlers run once the twin is dead and gone. Whichever boss dies last ends the run. Host-only.
/// </summary>
[HarmonyPatch]
internal static class TwinBoss
{
    private static NetworkObject twin;
    private static HealthContainer twinHealth;
    private static EndBossObjective objective;
    private static EndBossObjective.__c__DisplayClass27_0 heldClosure;
    private static bool heldDepleted, heldDespawn, releasing;
    private static float twinDeadSince = -1f, nextTick;

    private static bool TwinActive => twin != null;

    private static bool TwinDead =>
        twin == null || twin.WasCollected || !twin.IsSpawned ||
        twinHealth == null || twinHealth.WasCollected || twinHealth.IsHealthZero();

    private static void Reset()
    {
        twin = null; twinHealth = null; objective = null; heldClosure = null;
        heldDepleted = heldDespawn = releasing = false;
        twinDeadSince = -1f;
    }

    // ---------- Spawn ----------

    [HarmonyPatch(typeof(EndBossObjective), nameof(EndBossObjective.OnNetworkSpawn))]
    [HarmonyPostfix]
    private static void Objective_Spawn() => Reset();

    [HarmonyPatch(typeof(EndBossObjective), nameof(EndBossObjective.SpawnBoss))]
    [HarmonyPostfix]
    private static void SpawnBoss_Postfix(EndBossObjective __instance)
    {
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || TwinActive) return;

            int extra = DifficultyPatches.ExtraPlayers();
            if (extra <= 0) return;
            float chance = Mathf.Clamp01(Plugin.TwinBossChancePerExtraPlayer.Value * extra);
            if (URandom.value >= chance)
            {
                Plugin.Log.LogInfo($"Twin boss: not this time ({chance:P0} chance).");
                return;
            }

            BossConfig config = __instance.bossConfigToSpawn;
            NetworkObject prefab = config?.BossPrefab;
            if (prefab == null) { Plugin.Log.LogWarning("Twin boss: no boss prefab."); return; }

            // Same spawn as the game (objective position, facing 180 deg), shifted to the side.
            Quaternion rot = Quaternion.Euler(0f, 180f, 0f);
            Vector3 pos = __instance.transform.position + rot * Vector3.right * Plugin.TwinBossSpacing.Value;

            NetworkObject spawned = nm.SpawnManager.InstantiateAndSpawn(prefab, 0UL, false, false, false, pos, rot);
            if (spawned == null) { Plugin.Log.LogWarning("Twin boss: spawn failed."); return; }

            twin = spawned;
            twinHealth = spawned.GetComponentInChildren<HealthContainer>();
            objective = __instance;

            // Let the game's "enemy spawned" listeners (pointers, trackers) know about the twin.
            __instance.InvokeEnemySpawnedRpc(new NetworkObjectReference(spawned));

            Plugin.Log.LogInfo($"Twin boss spawned: {config.BossType} x2 ({chance:P0} chance, {extra} extra wizard(s)).");
        }
        catch (Exception ex) { Plugin.Log.LogError($"Twin boss (spawn): {ex}"); }
    }

    // ---------- Hold the first boss's victory handlers while the twin lives ----------

    [HarmonyPatch(typeof(EndBossObjective.__c__DisplayClass27_0), nameof(EndBossObjective.__c__DisplayClass27_0.Method_Internal_Void_Boolean_DamageInfo_PDM_0))]
    [HarmonyPrefix]
    private static bool BossHealthDepleted_Prefix(EndBossObjective.__c__DisplayClass27_0 __instance)
    {
        if (releasing || !TwinActive || TwinDead) return true;
        heldClosure = __instance;
        heldDepleted = true;
        SwitchHealthBarToTwin();
        Plugin.Log.LogInfo("Twin boss: first boss down, the twin is still fighting.");
        return false;
    }

    [HarmonyPatch(typeof(EndBossObjective.__c__DisplayClass27_0), nameof(EndBossObjective.__c__DisplayClass27_0.Method_Internal_Void_PDM_0))]
    [HarmonyPrefix]
    private static bool BossDespawnStarted_Prefix(EndBossObjective.__c__DisplayClass27_0 __instance)
    {
        if (releasing || !TwinActive || TwinDead) return true;
        heldClosure = __instance;
        heldDespawn = true;
        return false;
    }

    private static void SwitchHealthBarToTwin()
    {
        try
        {
            if (objective == null || objective.WasCollected || twin == null) return;
            objective.SetBossHealthContainerRpc(new NetworkObjectReference(twin), objective.bossConfigToSpawn.BossType);
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"Twin boss (health bar): {ex.Message}"); }
    }

    // ---------- Release when the twin is dead ----------

    [HarmonyPatch(typeof(RunManager), "Update")]
    [HarmonyPostfix]
    private static void Run_Update()
    {
        if (!TwinActive) return;
        float now = Time.time;
        if (now < nextTick) return;
        nextTick = now + 0.25f;

        try
        {
            if (!TwinDead) { twinDeadSince = -1f; return; }
            if (twinDeadSince < 0f) twinDeadSince = now;

            // Health gone: the run counts as won (same as the first boss's handler).
            if (heldDepleted && heldClosure != null)
            {
                heldDepleted = false;
                releasing = true;
                try { heldClosure.Method_Internal_Void_Boolean_DamageInfo_PDM_0(false, null); }
                finally { releasing = false; }
            }

            // Wait for the twin's death animation to finish (it despawns), or 10s at most, then play the victory sequence.
            bool gone = twin.WasCollected || !twin.IsSpawned;
            if (heldDespawn && heldClosure != null && (gone || now - twinDeadSince > 10f))
            {
                heldDespawn = false;
                releasing = true;
                try { heldClosure.Method_Internal_Void_PDM_0(); }
                finally { releasing = false; }
            }

            if (!heldDepleted && !heldDespawn && (gone || now - twinDeadSince > 10f))
            {
                Plugin.Log.LogInfo("Twin boss: both bosses defeated.");
                Reset();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Twin boss (release): {ex}");
            Reset();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnNetworkDespawn))]
    [HarmonyPostfix]
    private static void Run_Despawn() => Reset();
}
