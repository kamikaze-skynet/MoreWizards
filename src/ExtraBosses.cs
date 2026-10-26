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
/// Each extra is a random boss (different from the main one and each other while there are enough kinds; RandomBosses
/// off = copies of the main boss), placed alternately right and left of the normal spawn.
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
        public BossType Type;
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

    /// <summary>The boss objective of this run (every game), for the overhead bars.</summary>
    internal static EndBossObjective CurrentObjective;

    // ---------- which bosses there are ----------

    /// <summary>Every boss the game has, one config per kind.</summary>
    internal static List<BossConfig> AllBosses()
    {
        var list = new List<BossConfig>();
        try
        {
            var all = BossConfigRepository.Get();
            if (all == null) return list;
            for (int i = 0; i < all.Count; i++)
            {
                var b = all[i];
                if (b != null && b.BossPrefab != null && !list.Exists(x => x.BossType == b.BossType)) list.Add(b);
            }
        }
        catch { }
        return list;
    }

    /// <summary>The boss config whose prefab this spawned object came from (by name), or null if it isn't a boss.</summary>
    internal static BossConfig ConfigFor(NetworkObject obj)
    {
        if (obj == null) return null;
        string name;
        try { name = obj.gameObject.name.Replace("(Clone)", "").Trim(); } catch { return null; }
        foreach (var b in AllBosses())
            try { if (b.BossPrefab.gameObject.name == name) return b; } catch { }
        return null;
    }

    internal static string DisplayName(BossType t) => t switch
    {
        BossType.Squid => "Sol'phish",
        BossType.Toad => "Krog'wa",
        BossType.Hydra => "Mul'gath",
        _ => t.ToString(),
    };

    /// <summary>The kinds for the extra bosses: others than the main boss first (shuffled), then repeats.</summary>
    private static List<BossConfig> PickExtras(BossConfig main, int count)
    {
        var picks = new List<BossConfig>();
        if (!Plugin.ExtraBossRandom.Value) { for (int i = 0; i < count; i++) picks.Add(main); return picks; }
        var all = AllBosses();
        if (all.Count == 0) { for (int i = 0; i < count; i++) picks.Add(main); return picks; }
        var fresh = all.FindAll(b => b.BossType != main.BossType);
        for (int i = fresh.Count - 1; i > 0; i--) { int j = URandom.Range(0, i + 1); (fresh[i], fresh[j]) = (fresh[j], fresh[i]); }
        foreach (var b in fresh) { if (picks.Count >= count) break; picks.Add(b); }
        while (picks.Count < count) picks.Add(all[URandom.Range(0, all.Count)]);
        return picks;
    }

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
    private static void Objective_Spawn(EndBossObjective __instance) { Reset(); CurrentObjective = __instance; }

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
            var kinds = PickExtras(config, toSpawn);
            Quaternion rot = Quaternion.Euler(0f, 180f, 0f);
            Vector3 center = __instance.transform.position;
            float spacing = Plugin.ExtraBossSpacing.Value;
            for (int k = 1; k <= toSpawn; k++)
            {
                float side = (k % 2 == 1 ? 1f : -1f) * spacing * ((k + 1) / 2);
                Vector3 pos = center + rot * Vector3.right * side;
                // destroyWithScene: true, like the game's own boss. With false, Netcode keeps the object when the
                // scene changes, so a living extra boss followed the players into the lobby and the next run.
                var kind = kinds[k - 1] ?? config;
                var kindPrefab = kind.BossPrefab ?? prefab;
                NetworkObject spawned = nm.SpawnManager.InstantiateAndSpawn(kindPrefab, 0UL, true, false, false, pos, rot);
                if (spawned == null) { Plugin.Log.LogWarning($"Extra bosses: spawn #{k} ({kind.BossType}) failed."); continue; }

                extras.Add(new Extra { Obj = spawned, Health = spawned.GetComponentInChildren<HealthContainer>(), Type = kind.BossType });
                // Let the game's "enemy spawned" listeners (pointers, trackers) know about it.
                __instance.InvokeEnemySpawnedRpc(new NetworkObjectReference(spawned));
            }

            if (extras.Count > 0)
            {
                objective = __instance;
                Plugin.Log.LogInfo($"Extra bosses: {config.BossType} with {string.Join(", ", extras.ConvertAll(e => e.Type.ToString()))} this fight.");
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
                objective.SetBossHealthContainerRpc(new NetworkObjectReference(e.Obj), e.Type);
                return;
            }
        }
        catch (Exception ex) { Plugin.Log.LogWarning($"Extra bosses (health bar): {ex.Message}"); }
    }

    // ---------- Boss health bar (every player's game) ----------
    // The boss health panel subscribes to "health depleted" on the boss it shows; on that boss's death it plays the
    // defeat sequence and hides. With extra bosses that hides the bar while others still live (and pointing the
    // objective at another boss doesn't re-attach the panel). So: if another copy of that boss is still alive,
    // skip the defeat sequence and re-attach the bar to the living one. Runs on every game with the mod.

    [HarmonyPatch(typeof(ObjectiveDetailPanel_BossHealth.__c__DisplayClass18_0), nameof(ObjectiveDetailPanel_BossHealth.__c__DisplayClass18_0.Method_Internal_Void_Boolean_DamageInfo_PDM_0))]
    [HarmonyPrefix]
    private static bool PanelBossDied_Prefix(ObjectiveDetailPanel_BossHealth.__c__DisplayClass18_0 __instance)
    {
        try
        {
            var dead = __instance.healthContainer;
            var panel = __instance.__4__this;
            if (dead == null || panel == null) return true;

            var next = OtherLivingBoss(dead);
            if (next == null) return true; // last boss standing: the normal defeat sequence

            var nextObj = OwnerObject(next);
            if (nextObj == null) return true;
            // A different kind of boss: its own name and bar on the panel.
            var cfg = ConfigFor(nextObj);
            if (cfg != null)
            {
                try { panel.bossConfig = cfg; panel.InitializeUIElements(); } catch { }
            }
            panel.InitializeUIEvents(nextObj);
            panel.InitializeHealthBar(nextObj);
            Plugin.Log.LogInfo($"Extra bosses: health bar moved to the next boss ({LivingBossCount(dead)} left).");
            return false;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"Extra bosses (panel): {ex.Message}");
            return true;
        }
    }

    private static NetworkObject OwnerObject(Component c)
    {
        try { return c.GetComponentInParent<NetworkObject>(); } catch { return null; }
    }

    private static string BossName(Component c)
    {
        var o = OwnerObject(c);
        try { return o == null ? "" : o.gameObject.name.Replace("(Clone)", "").Trim(); } catch { return ""; }
    }

    private static bool AliveBoss(EnemyHealthContainer c) =>
        c is not null && !c.WasCollected && c != null && c.gameObject.activeInHierarchy && !c.IsHealthZero();

    /// <summary>Another living boss (any kind) than <paramref name="dead"/>, if any.</summary>
    private static EnemyHealthContainer OtherLivingBoss(EnemyHealthContainer dead)
    {
        var deadObj = OwnerObject(dead);
        var names = BossPrefabNames();
        foreach (var c in UnityEngine.Object.FindObjectsOfType<EnemyHealthContainer>())
            if (AliveBoss(c) && OwnerObject(c) != deadObj && names.Contains(BossName(c))) return c;
        return null;
    }

    private static int LivingBossCount(EnemyHealthContainer dead)
    {
        var deadObj = OwnerObject(dead);
        var names = BossPrefabNames();
        int n = 0;
        foreach (var c in UnityEngine.Object.FindObjectsOfType<EnemyHealthContainer>())
            if (AliveBoss(c) && OwnerObject(c) != deadObj && names.Contains(BossName(c))) n++;
        return n;
    }

    internal static HashSet<string> BossPrefabNames()
    {
        var set = new HashSet<string>();
        foreach (var b in AllBosses()) try { set.Add(b.BossPrefab.gameObject.name); } catch { }
        return set;
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
    private static void Run_Despawn()
    {
        // Host: an extra boss still alive when the run ends goes with it.
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.IsServer)
                foreach (var e in extras)
                    try { if (!e.Gone) e.Obj.Despawn(true); } catch { }
        }
        catch { }
        Reset();
    }
}
