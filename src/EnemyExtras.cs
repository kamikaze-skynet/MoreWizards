using System;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using URandom = UnityEngine.Random;

namespace MoreWizards;

/// <summary>
/// More things to fight with a big party:
///  - Elites: each elite spawn brings extra copies.
///  - Stronger Enemies stacks: extra wizards count as stacks of the game's "Stronger Enemies" curse, which unlocks
///    the extra wave events and elite waves the designers made for that curse.
///  - Bosses: attack more often, and summon groups of the current wave's enemies around them.
/// Spawning is host-only; clients don't run any of the spawn logic.
/// </summary>
[HarmonyPatch]
internal static class EnemyExtras
{
    private static bool IsServer
    {
        get { try { return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer; } catch { return false; } }
    }

    /// <summary>Whole part always applies; the fractional part is a chance. 4 extra wizards x 0.3 = 1 guaranteed + 20% for a 2nd.</summary>
    private static int RollCount(float amount)
    {
        if (amount <= 0f) return 0;
        int whole = (int)Math.Floor(amount);
        return whole + (URandom.value < amount - whole ? 1 : 0);
    }

    // ---------- Elites ----------

    private static bool spawningExtraElites;

    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.SpawnEliteEnemy))]
    [HarmonyPostfix]
    private static void SpawnElite_Postfix(EnemySpawner __instance, EliteEnemyConfiguration eliteEnemyConfig)
    {
        if (spawningExtraElites || !IsServer) return;
        int extra = DifficultyPatches.ExtraPlayers();
        int copies = RollCount(extra * Plugin.ElitesPerExtraPlayer.Value);
        if (copies <= 0) return;

        spawningExtraElites = true;
        try
        {
            for (int i = 0; i < copies; i++) __instance.SpawnEliteEnemy(eliteEnemyConfig);
            Plugin.Log.LogInfo($"Elite spawn: +{copies} extra for {extra} extra wizard(s).");
        }
        catch (Exception ex) { Plugin.Log.LogError($"Extra elites: {ex}"); }
        finally { spawningExtraElites = false; }
    }

    // ---------- Stronger Enemies curse stacks (unlocks extra wave events + elite waves) ----------

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetHighestStrongerEnemiesCurseStacks))]
    [HarmonyPostfix]
    private static void CurseStacks_Postfix(RunManager __instance)
    {
        try
        {
            int extra = DifficultyPatches.ExtraPlayers();
            int bonus = (int)Math.Floor(extra * Plugin.StrongerEnemiesStacksPerExtraPlayer.Value);
            if (bonus <= 0) return;
            int before = __instance.highestStrongerEnemiesCurseStacks;
            __instance.highestStrongerEnemiesCurseStacks = before + bonus;
            Plugin.Log.LogInfo($"Stronger Enemies stacks: {before} -> {before + bonus} ({extra} extra wizard(s)).");
        }
        catch (Exception ex) { Plugin.Log.LogError($"Curse stacks: {ex}"); }
    }

    // ---------- Bosses: faster attacks ----------

    [HarmonyPatch(typeof(BossPhase), nameof(BossPhase.Awake))]
    [HarmonyPostfix]
    private static void BossPhase_Awake(BossPhase __instance)
    {
        try
        {
            float speed = 1f + DifficultyPatches.ExtraPlayers() * S.BossAttackSpeedPerExtraPlayer;
            if (speed <= 1.001f) return;
            __instance.timeBetweenAttacks /= speed;
            __instance.delayBeforeFirstAttack /= speed;
        }
        catch (Exception ex) { Plugin.Log.LogError($"Boss attack speed: {ex}"); }
    }

    // ---------- Bosses: adds ----------

    private static EndBossObjective bossObjective;
    private static float addsTimer;
    private static float nextCheck;

    [HarmonyPatch(typeof(EndBossObjective), nameof(EndBossObjective.OnNetworkSpawn))]
    [HarmonyPostfix]
    private static void BossObjective_Spawn(EndBossObjective __instance)
    {
        bossObjective = __instance;
        addsTimer = 0f;
    }

    // Ticked from RunManager (always running during a run), not EnemySpawner, in case normal spawning pauses for the boss.
    [HarmonyPatch(typeof(RunManager), "Update")]
    [HarmonyPostfix]
    private static void Run_Update()
    {
        if (!Plugin.BossAddsEnabled.Value || bossObjective == null) return;
        float now = Time.time;
        if (now < nextCheck) return;
        float dt = 0.5f;
        nextCheck = now + dt;

        try
        {
            if (bossObjective.WasCollected) { bossObjective = null; return; }
            if (!IsServer) return;

            HealthContainer boss = bossObjective.bossHealthContainer;
            if (boss == null || boss.WasCollected || boss.IsHealthZero()) { addsTimer = 0f; return; }

            int extra = DifficultyPatches.ExtraPlayers();
            if (extra <= 0) return;

            addsTimer += dt;
            if (addsTimer < Mathf.Max(5f, Plugin.BossAddsIntervalSeconds.Value)) return;
            addsTimer = 0f;

            int count = RollCount(extra * Plugin.BossAddsPerExtraPlayer.Value);
            if (count <= 0) return;

            EnemySpawner spawner = null;
            try { spawner = Singleton<EnemySpawner>.Instance; } catch { }
            if (spawner == null) spawner = UnityEngine.Object.FindObjectOfType<EnemySpawner>();
            if (spawner == null) return;
            if (!TryPickWaveEnemy(spawner, out EnemyType type)) return;

            var config = new EnemyGroupSpawnConfig(type, count, boss.transform,
                6f, 12f, 0f, 1.5f, EnemySpawnRotation.TowardsClosestPlayer);
            spawner.SpawnGroupAsync(config);
            Plugin.Log.LogInfo($"Boss adds: {count} x {type}.");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Boss adds: {ex}");
            bossObjective = null; // don't spam the log every half second
        }
    }

    /// <summary>Weighted pick from the current wave's enemy table, so adds fit the world and wave.</summary>
    private static bool TryPickWaveEnemy(EnemySpawner spawner, out EnemyType type)
    {
        type = default;
        var wave = spawner.currentWave;
        if (wave == null) return false;

        var list = wave.EnemyProbabilitiesForSelectedWorld;
        if (list == null || list.Count == 0) list = wave.EnemyProbabilities;
        if (list == null || list.Count == 0) return false;

        float total = 0f;
        for (int i = 0; i < list.Count; i++) total += Mathf.Max(0f, list[i].Probability);
        float roll = URandom.value * (total > 0f ? total : 1f);
        for (int i = 0; i < list.Count; i++)
        {
            roll -= Mathf.Max(0f, list[i].Probability);
            if (roll <= 0f || i == list.Count - 1) { type = list[i].Type; return true; }
        }
        return false;
    }
}
