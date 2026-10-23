using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Unity.Collections;
using Unity.Netcode;

namespace MoreWizards;

/// <summary>
/// Effective settings. On the host and in single player these are the local config.
/// On a client, after the host's settings arrive at run start, they are the host's values for that run.
/// The client's config file is never modified.
/// </summary>
internal static class S
{
    private static Dictionary<string, string> synced;

    internal static bool IsSynced => synced != null;

    private static float F(string key, BepInEx.Configuration.ConfigEntry<float> local) =>
        synced != null && synced.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : local.Value;

    private static int I(string key, BepInEx.Configuration.ConfigEntry<int> local) =>
        synced != null && synced.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : local.Value;

    private static bool B(string key, BepInEx.Configuration.ConfigEntry<bool> local) =>
        synced != null && synced.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : local.Value;

    internal static bool ScalingEnabled => B("ScalingEnabled", Plugin.ScalingEnabled);
    internal static int ScalingStartsAbove => I("ScalingStartsAbove", Plugin.ScalingStartsAbove);
    internal static float HealthPerExtraPlayer => F("HealthPerExtraPlayer", Plugin.HealthPerExtraPlayer);
    internal static float DamagePerExtraPlayer => F("DamagePerExtraPlayer", Plugin.DamagePerExtraPlayer);
    internal static float SpawnsPerExtraPlayer => F("SpawnsPerExtraPlayer", Plugin.SpawnsPerExtraPlayer);
    internal static float EnemyCapPerExtraPlayer => F("EnemyCapPerExtraPlayer", Plugin.EnemyCapPerExtraPlayer);
    internal static float FlatHealthMultiplier => F("FlatHealthMultiplier", Plugin.FlatHealthMultiplier);
    internal static float FlatDamageMultiplier => F("FlatDamageMultiplier", Plugin.FlatDamageMultiplier);
    internal static float FlatSpawnMultiplier => F("FlatSpawnMultiplier", Plugin.FlatSpawnMultiplier);
    internal static float BossAttackSpeedPerExtraPlayer => F("BossAttackSpeedPerExtraPlayer", Plugin.BossAttackSpeedPerExtraPlayer);
    internal static float ShopTimeLimitSeconds => F("ShopTimeLimitSeconds", Plugin.ShopTimeLimitSeconds);

    /// <summary>Settings the host sends. Server-only settings (spawns, revives, elites, adds) only matter on the host but are included for logging.</summary>
    internal static string Serialize()
    {
        var c = CultureInfo.InvariantCulture;
        var kv = new List<string>
        {
            "Mod=" + Plugin.Version,
            "ScalingEnabled=" + Plugin.ScalingEnabled.Value,
            "ScalingStartsAbove=" + Plugin.ScalingStartsAbove.Value.ToString(c),
            "HealthPerExtraPlayer=" + Plugin.HealthPerExtraPlayer.Value.ToString(c),
            "DamagePerExtraPlayer=" + Plugin.DamagePerExtraPlayer.Value.ToString(c),
            "SpawnsPerExtraPlayer=" + Plugin.SpawnsPerExtraPlayer.Value.ToString(c),
            "EnemyCapPerExtraPlayer=" + Plugin.EnemyCapPerExtraPlayer.Value.ToString(c),
            "FlatHealthMultiplier=" + Plugin.FlatHealthMultiplier.Value.ToString(c),
            "FlatDamageMultiplier=" + Plugin.FlatDamageMultiplier.Value.ToString(c),
            "FlatSpawnMultiplier=" + Plugin.FlatSpawnMultiplier.Value.ToString(c),
            "BossAttackSpeedPerExtraPlayer=" + Plugin.BossAttackSpeedPerExtraPlayer.Value.ToString(c),
            "ShopTimeLimitSeconds=" + Plugin.ShopTimeLimitSeconds.Value.ToString(c),
        };
        return string.Join(";", kv);
    }

    internal static void Apply(string payload)
    {
        var d = new Dictionary<string, string>();
        foreach (var part in payload.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq > 0) d[part.Substring(0, eq)] = part.Substring(eq + 1);
        }
        synced = d;
        NativePatcher.ApplyShopTimer(ShopTimeLimitSeconds); // keep the client's shop countdown in step with the host
    }

    internal static void Clear()
    {
        if (synced == null) return;
        synced = null;
        NativePatcher.ApplyShopTimer(Plugin.ShopTimeLimitSeconds.Value);
        Plugin.Log.LogInfo("Host settings cleared; using local config.");
    }
}

/// <summary>
/// At run start the host sends its scaling settings to every client over a Netcode named message.
/// Clients use them for that run, so enemy damage (computed on the client that gets hit) and
/// the shop countdown match the host for everyone.
/// </summary>
[HarmonyPatch]
internal static class HostSync
{
    private const string MessageName = "girthy_mods.MoreWizards.Config";
    private static CustomMessagingManager.HandleNamedMessageDelegate handler; // kept alive for IL2CPP

    // Clients: listen as soon as the run's RunManager exists (before the host's StartRun message can arrive).
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnNetworkSpawn))]
    [HarmonyPostfix]
    private static void RunManager_Spawn()
    {
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer || nm.CustomMessagingManager == null) return;
            handler ??= DelegateSupport.ConvertDelegate<CustomMessagingManager.HandleNamedMessageDelegate>(
                new Action<ulong, FastBufferReader>(OnConfigReceived));
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MessageName, handler);
        }
        catch (Exception ex) { Plugin.Log.LogError($"Host sync (listen): {ex}"); }
    }

    private static void OnConfigReceived(ulong sender, FastBufferReader reader)
    {
        try
        {
            if (!Plugin.SyncHostSettings.Value) return;
            reader.ReadValueSafe(out string payload, false);
            S.Apply(payload);
            Plugin.Log.LogInfo($"Using host settings for this run: {payload}");
        }
        catch (Exception ex) { Plugin.Log.LogError($"Host sync (receive): {ex}"); }
    }

    // Host: broadcast when the run starts (execute stage of the server RPC).
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.StartRunOnServerRpc))]
    [HarmonyPrefix]
    private static void StartRun_Prefix(RunManager __instance, out bool __state)
    {
        __state = false;
        try { __state = __instance.__rpc_exec_stage == NetworkBehaviour.__RpcExecStage.Execute; } catch { }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.StartRunOnServerRpc))]
    [HarmonyPostfix]
    private static void StartRun_Postfix(bool __state)
    {
        if (!__state) return;
        try
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer || nm.CustomMessagingManager == null) return;
            string payload = S.Serialize();
            int size = Encoding.UTF8.GetByteCount(payload) * 2 + 64;
            var writer = new FastBufferWriter(size, Allocator.Temp, -1);
            try
            {
                writer.WriteValueSafe(payload, false);
                nm.CustomMessagingManager.SendNamedMessageToAll(MessageName, writer, NetworkDelivery.ReliableSequenced);
            }
            finally { writer.Dispose(); }
            Plugin.Log.LogInfo("Sent host settings to all clients.");
        }
        catch (Exception ex) { Plugin.Log.LogError($"Host sync (send): {ex}"); }
    }

    // Back to local settings when the run ends.
    [HarmonyPatch(typeof(RunManager), nameof(RunManager.OnDestroy))] // RunManager has no OnNetworkDespawn of its own
    [HarmonyPostfix]
    private static void RunManager_Despawn() => S.Clear();
}
