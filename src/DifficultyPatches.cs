using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace MoreWizards;

/// <summary>
/// Makes runs harder as the party grows past the vanilla 4.
/// The base game already scales some things with player count (boss/elite health +50% per player,
/// some objective sizes, spawn influence). These patches add configurable extra scaling on top.
/// </summary>
[HarmonyPatch]
internal static class DifficultyPatches
{
    // ---------- Player count (cached once per frame) ----------
    private static int cachedFrame = -1;
    private static int cachedCount = 1;

    internal static int PlayerCount()
    {
        int frame = Time.frameCount;
        if (frame == cachedFrame) return cachedCount;
        cachedFrame = frame;
        cachedCount = Math.Max(1, QueryPlayerCount());
        return cachedCount;
    }

    private static GameplayPlayerManager cachedGpm;
    private static SessionManager cachedSession;
    private static float nextGpmFind, nextSessionFind;
    private static int lastLoggedCount = -1;

    /// <summary>
    /// The game's own singleton accessor first; if that comes back empty through IL2CPP interop,
    /// find the object once and cache it.
    /// </summary>
    private static T Resolve<T>(Func<T> singleton, ref T cache, ref float nextFind) where T : UnityEngine.Object
    {
        try { var s = singleton(); if (s != null) return s; } catch { }
        if (cache != null && !cache.WasCollected) return cache;
        if (Time.unscaledTime < nextFind) return null;
        nextFind = Time.unscaledTime + 1f; // FindObjectOfType is slow: at most once a second
        try { cache = UnityEngine.Object.FindObjectOfType<T>(); } catch { cache = null; }
        return cache;
    }

    internal static int QueryPlayerCount()
    {
        int count = 0;
        string source = "";
        try
        {
            var gpm = Resolve(() => Singleton<GameplayPlayerManager>.Instance, ref cachedGpm, ref nextGpmFind);
            var ids = gpm?.Identities;
            if (ids != null && ids.Count > 0) { count = ids.Count; source = "in-run players"; }
        }
        catch { /* not in a run */ }

        if (count == 0)
        {
            try
            {
                var sm = Resolve(() => SingletonPersistent<SessionManager>.Instance, ref cachedSession, ref nextSessionFind);
                if (sm != null) { count = sm.GetNumberOfPlayers(); source = "session"; }
            }
            catch { }
        }

        if (count <= 0) { count = 1; source = "fallback"; }
        if (count != lastLoggedCount)
        {
            lastLoggedCount = count;
            Plugin.Log.LogInfo($"Player count: {count} ({source}).");
        }
        return count;
    }

    /// <summary>Wizards above ScalingStartsAbove (0 if scaling is off). Uses the host's settings when synced.</summary>
    internal static int ExtraPlayers()
    {
        if (!S.ScalingEnabled) return 0;
        return Math.Max(0, PlayerCount() - S.ScalingStartsAbove);
    }

    private static float HealthMult => S.FlatHealthMultiplier * (1f + ExtraPlayers() * S.HealthPerExtraPlayer);
    private static float DamageMult => S.FlatDamageMultiplier * (1f + ExtraPlayers() * S.DamagePerExtraPlayer);
    private static float SpawnMult => S.FlatSpawnMultiplier * (1f + ExtraPlayers() * S.SpawnsPerExtraPlayer);
    private static float CapMult => S.FlatSpawnMultiplier * (1f + ExtraPlayers() * S.EnemyCapPerExtraPlayer);

    // ---------- Enemy health ----------
    // Called once when each enemy initialises; result becomes its max HP.
    [HarmonyPatch(typeof(EnemyHealthContainer), nameof(EnemyHealthContainer.CalculateMaxHealth))]
    [HarmonyPostfix]
    private static void MaxHealth_Postfix(ref float __result)
    {
        float m = HealthMult;
        if (m != 1f && m > 0f) __result *= m;
    }

    // ---------- Enemy damage ----------
    // Used by enemy projectiles, swipes, area attacks, boss attacks etc.
    [HarmonyPatch(typeof(EnemyDifficultyParameters), nameof(EnemyDifficultyParameters.GetAttackMultiplier))]
    [HarmonyPostfix]
    private static void Attack_Postfix(ref float __result)
    {
        float m = DamageMult;
        if (m != 1f && m > 0f) __result *= m;
    }

    // ---------- Spawns ----------
    [HarmonyPatch(typeof(WaveConfiguration), nameof(WaveConfiguration.MinNumberOfConcurrentEnemiesForDifficulty), MethodType.Getter)]
    [HarmonyPostfix]
    private static void MinConcurrent_Postfix(ref int __result)
    {
        float m = SpawnMult;
        if (m != 1f && m > 0f) __result = Mathf.Max(1, Mathf.RoundToInt(__result * m));
    }

    [HarmonyPatch(typeof(WaveConfiguration), nameof(WaveConfiguration.NumberOfEnemiesPerSpawnForCycle), MethodType.Getter)]
    [HarmonyPostfix]
    private static void PerSpawn_Postfix(ref int __result)
    {
        float m = SpawnMult;
        if (m != 1f && m > 0f) __result = Mathf.Max(1, Mathf.RoundToInt(__result * m));
    }

    [HarmonyPatch(typeof(WaveConfiguration), nameof(WaveConfiguration.SpawnIntervalInSecondsForDifficultyAndCycle), MethodType.Getter)]
    [HarmonyPostfix]
    private static void Interval_Postfix(ref float __result)
    {
        float m = SpawnMult;
        if (m != 1f && m > 0f) __result /= m;
    }

    // Raise the "max enemies alive at once" cap. Remember each spawner's original cap so we
    // can recompute it as players join/leave instead of compounding.
    private static readonly Dictionary<IntPtr, int> baseCaps = new();

    [HarmonyPatch(typeof(EnemySpawner), nameof(EnemySpawner.Update))]
    [HarmonyPrefix]
    private static void Spawner_Update(EnemySpawner __instance)
    {
        try
        {
            IntPtr key = __instance.Pointer;
            if (!baseCaps.TryGetValue(key, out int baseCap))
            {
                baseCap = __instance.maxNumberOfConcurrentEnemies;
                if (baseCaps.Count > 64) baseCaps.Clear();
                baseCaps[key] = baseCap;
            }
            float m = CapMult;
            int want = m > 0f ? Mathf.Max(1, Mathf.RoundToInt(baseCap * m)) : baseCap;
            if (__instance.maxNumberOfConcurrentEnemies != want)
                __instance.maxNumberOfConcurrentEnemies = want;
        }
        catch { }
    }
}
