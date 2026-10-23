using System;
using System.Collections.Generic;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using URandom = UnityEngine.Random;

namespace MoreWizards;

/// <summary>
/// Extra bosses: when the final boss spawns, the host rolls each extra boss slot separately
/// ([Extra Bosses] Boss2ChancePercent, Boss3ChancePercent, ... up to MaxExtraBosses), if the run has at least MinPlayers.
/// Each extra is a copy of the boss, placed alternately right and left of the normal spawn.
///
/// How the game ends a run: EndBossObjective.SpawnBoss hooks two handlers on the boss it spawns:
///   - health depleted   -> GameplayEndManager.FlagRunAsSuccessful   (closure ...DisplayClass27_0 "Boolean_DamageInfo")
///   - despawn started   -> HandleObjectiveEndedRpc + victory sequence (closure ...DisplayClass27_0 "PDM_0")
/// While any extra boss lives, those handlers are held back when the original boss dies, the boss health bar moves
/// to the next living boss, and the held handlers run once the last boss is dead and gone. Host-only.
/// </summary>
[HarmonyPatch]
internal static class ExtraBosses
{
    private sealed class Extra
    {
        public NetworkObject Obj;
        public HealthContainer Health;
        public bool Dead => Obj == null || Obj.WasCollected || !Obj.IsSpawned ||
                            Health == null || Health.WasCollected || Health.IsHealthZero();
        public bool Gone => Obj == null || Obj.WasCollected || !Obj.IsSpawned;
    }

    private static readonly List<Extra> extras = new();
    private static EndBossObjective objective;
    private static EndBossObjective.__c__DisplayClass27_0 heldClosure;
    private static bool heldDepleted, heldDespawn, releasing, firstBossDown;
    private static Extra barOn;
    private static float allDeadSince = -1f, nextTick;

    private static bool Active => extras.Count > 0;

    private static bool AllExtrasDead
    {
        get { foreach (var e in extras) if (!e.Dead) return false; return true; }
    }

    private static bool AllExtrasGone
    {
        get { foreach (var e in extras) if (!e.Gone) return false; return true; }
    }

    private static void Reset()
    {
        extras.Clear();
        objective = null; heldClosure = null; barOn = null;
        heldDepleted = heldDespawn = releasing = firstBossDown = false;
        allDeadSince = -1f;
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
            if (nm == null || !nm.IsServer || Active || !Plugin.ExtraBossesEnabled.Value) return;

            int players = DifficultyPatches.QueryPlayerCount();
            int minPlayers = Plugin.ExtraBossMinPlayers.Value;
            if (players < minPlayers)
            {
                Plugin.Log.LogInfo($"Extra bosses: {players} wizard(s), need {minPlayers}.");
                return;
            }

            BossConfig config = __instance.bossConfigToSpawn;
            NetworkObject prefab = config?.BossPrefab;
            if (prefab == null) { Plugin.Log.LogWarning("Extra bosses: no boss prefab."); return; }

            // Roll each slot separately.
            int slots = Math.Clamp(Plugin.ExtraBossCount.Value, 1, Plugin.MaxExtraBossSlots);
            var rolls = new List<string>();
            int toSpawn = 0;
            for (int i = 0; i < slots; i++)
            {
                float chance = Mathf.Clamp(Plugin.ExtraBossChances[i].Value, 0f, 100f);
                bool hit = URandom.value * 100f < chance;
                if (hit) toSpawn++;
                rolls.Add($"boss {i + 2}: {chance:0.#}% {(hit ? "yes" : "no")}");
            }
            Plugin.Log.LogInfo($"Extra bosses ({players} wizards): {string.Join(", ", rolls)}.");
            if (toSpawn == 0) return;

            // Same spawn as the game (objective position, facing 180 deg), alternating right/left.
            Quaternion rot = Quaternion.Euler(0f, 180f, 0f);
            Vector3 center = __instance.transform.position;
            float spacing = Plugin.ExtraBossSpacing.Value;
            for (int k = 1; k <= toSpawn; k++)
            {
                float side = (k % 2 == 1 ? 1f : -1f) * spacing * ((k + 1) / 2);
                Vector3 pos = center + rot * Vector3.right * side;
                NetworkObject spawned = nm.SpawnManager.InstantiateAndSpawn(prefab, 0UL, false, false, false, pos, rot);
                if (spawned == null) { Plugin.Log.LogWarning($"Extra bosses: spawn #{k} failed."); continue; }

                extras.Add(new Extra { Obj = spawned, Health = spawned.GetComponentInChildren<HealthContainer>() });
                // Let the game's "enemy spawned" listeners (pointers, trackers) know about it.
                __instance.InvokeEnemySpawnedRpc(new NetworkObjectReference(spawned));
            }

            if (extras.Count > 0)
            {
                objective = __instance;
                Plugin.Log.LogInfo($"Extra bosses: {config.BossType} x{extras.Count + 1} this fight.");
            }
        }
        catch (Exception ex) { Plugin.Log.LogError($"Extra bosses (spawn): {ex}"); }
    }

    // ---------- Hold the original boss's victory handlers while extras live ----------

    [HarmonyPatch(typeof(EndBossObjective.__c__DisplayClass27_0), nameof(EndBossObjective.__c__DisplayClass27_0.Method_Internal_Void_Boolean_DamageInfo_PDM_0))]
    [HarmonyPrefix]
    private static bool BossHealthDepleted_Prefix(EndBossObjective.__c__DisplayClass27_0 __instance)
    {
        if (releasing || !Active || AllExtrasDead) return true;
        heldClosure = __instance;
        heldDepleted = true;
        firstBossDown = true;
        MoveHealthBarToNextLivingBoss();
        Plugin.Log.LogInfo("Extra bosses: original boss down, more to go.");
        return false;
    }

    [HarmonyPatch(typeof(EndBossObjective.__c__DisplayClass27_0), nameof(EndBossObjective.__c__DisplayClass27_0.Method_Internal_Void_PDM_0))]
    [HarmonyPrefix]
    private static bool BossDespawnStarted_Prefix(EndBossObjective.__c__DisplayClass27_0 __instance)
    {
        if (releasing || !Active || AllExtrasDead) return true;
        heldClosure = __instance;
        heldDespawn = true;
        return false;
    }

    private static void MoveHealthBarToNextLivingBoss()
    {
        try
        {
            if (objective == null || objective.WasCollected) return;
            foreach (var e in extras)
            {
                if (e.Dead) continue;
                if (barOn == e) return;
                barOn = e;
                objective.SetBossHealthContainerRpc(new NetworkObjectReference(e.Obj), objective.bossConfigToSpawn.BossType);
                return;
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"Extra bosses (health bar): {ex.Message}"); }
    }

    // ---------- Track kills; release victory when the last boss is dead ----------

    [HarmonyPatch(typeof(RunManager), "Update")]
    [HarmonyPostfix]
    private static void Run_Update()
    {
        if (!Active) return;
        float now = Time.time;
        if (now < nextTick) return;
        nextTick = now + 0.25f;

        try
        {
            // The boss on the health bar died but others live: move the bar along.
            if (firstBossDown && barOn != null && barOn.Dead && !AllExtrasDead) MoveHealthBarToNextLivingBoss();

            if (!AllExtrasDead) { allDeadSince = -1f; return; }
            if (allDeadSince < 0f) allDeadSince = now;
            bool settled = AllExtrasGone || now - allDeadSince > 10f; // death animations done (or 10s at most)

            // Original boss already died: run its held handlers now that every extra is dead.
            if (heldDepleted && heldClosure != null)
            {
                heldDepleted = false;
                releasing = true;
                try { heldClosure.Method_Internal_Void_Boolean_DamageInfo_PDM_0(false, null); }
                finally { releasing = false; }
            }
            if (heldDespawn && heldClosure != null && settled)
            {
                heldDespawn = false;
                releasing = true;
                try { heldClosure.Method_Internal_Void_PDM_0(); }
                finally { releasing = false; }
            }

            // Either all handlers are released, or the original boss is still alive and will end the run itself.
            if (!heldDepleted && !heldDespawn && settled)
            {
                Plugin.Log.LogInfo(firstBossDown ? "Extra bosses: all bosses defeated." : "Extra bosses: extras defeated, original boss remains.");
                Reset();
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Extra bosses (release): {ex}");
            Reset();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnDestroy))] // RunManager has no OnNetworkDespawn of its own
    [HarmonyPostfix]
    private static void Run_Despawn() => Reset();
}
