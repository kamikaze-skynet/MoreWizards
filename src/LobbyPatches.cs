using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Il2CppList = Il2CppSystem.Collections.Generic;
using Object = UnityEngine.Object;

namespace MoreWizards;

/// <summary>
/// The game's scenes were authored for 4 players: 4 lobby seats, 4 spawn points, 4 player colours, etc.
/// These patches grow those lists so wizards 5+ have somewhere to sit, spawn and a colour of their own.
/// </summary>
[HarmonyPatch]
internal static class LobbyPatches
{
    private static int Max => Plugin.MaxPlayers.Value;

    // ---------- Player colours ----------
    // SessionManager hands every joining player a colour from a pool built from a 4-colour list.
    // Player 5 would draw from an empty pool and the join would fail.

    private static readonly Color[] ExtraColors =
    {
        new Color(1.00f, 0.55f, 0.10f), // orange
        new Color(0.70f, 0.30f, 1.00f), // violet
        new Color(0.10f, 0.90f, 0.85f), // teal
        new Color(1.00f, 0.40f, 0.70f), // pink
        new Color(0.60f, 1.00f, 0.20f), // lime
        new Color(0.95f, 0.95f, 0.95f), // white
        new Color(0.55f, 0.35f, 0.20f), // brown
        new Color(1.00f, 0.90f, 0.20f), // gold
        new Color(0.30f, 0.45f, 1.00f), // royal blue
        new Color(0.85f, 0.15f, 0.25f), // crimson
        new Color(0.40f, 0.75f, 0.45f), // sage
        new Color(0.60f, 0.60f, 0.65f), // grey
    };

    private static void EnsureColors(SessionManager sm)
    {
        var colors = sm.playerColors;
        if (colors == null) return;
        int i = 0;
        while (colors.Count < Max)
        {
            colors.Add(ExtraColors[i % ExtraColors.Length]);
            i++;
        }
    }

    [HarmonyPatch(typeof(SessionManager), nameof(SessionManager.StartSession))]
    [HarmonyPrefix]
    private static void StartSession_Prefix(SessionManager __instance)
    {
        try { EnsureColors(__instance); }
        catch (Exception e) { Plugin.Log.LogError($"StartSession colours: {e}"); }
    }

    [HarmonyPatch(typeof(SessionManager), nameof(SessionManager.AddPlayerData))]
    [HarmonyPrefix]
    private static void AddPlayerData_Prefix(SessionManager __instance)
    {
        try
        {
            EnsureColors(__instance);
            var pool = __instance.playerColorsPool;
            if (pool != null && pool.Count == 0)
            {
                // Pool ran dry: hand out a colour nobody is using right now.
                int n = __instance.GetNumberOfPlayers();
                pool.Add(ExtraColors[n % ExtraColors.Length]);
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"AddPlayerData colours: {e}"); }
    }

    // ---------- In-run spawn points ----------
    // LevelStarter places player i at spawnPoints[i]; only 4 exist.

    [HarmonyPatch(typeof(LevelStarter), nameof(LevelStarter.Awake))]
    [HarmonyPostfix]
    private static void LevelStarter_Awake(LevelStarter __instance)
    {
        try
        {
            var points = __instance.spawnPoints;
            if (points == null || points.Count == 0 || points.Count >= Max) return;

            int baseCount = points.Count;
            Vector3 center = Vector3.zero;
            for (int i = 0; i < baseCount; i++) center += points[i].position;
            center /= baseCount;

            for (int i = baseCount; i < Max; i++)
            {
                var src = points[i % baseCount];
                Vector3 dir = src.position - center;
                dir.y = 0f;
                if (dir.sqrMagnitude < 0.01f) dir = Quaternion.Euler(0, 360f * i / Max, 0) * Vector3.forward;
                int ring = i / baseCount; // 1 for players 5-8, 2 for 9-12 ...
                var go = new GameObject($"MoreWizards_Spawn_{i + 1}");
                go.transform.SetParent(src.parent, false);
                go.transform.position = src.position + dir.normalized * (1.75f * ring);
                go.transform.rotation = src.rotation;
                points.Add(go.transform);
            }
            Plugin.Log.LogInfo($"Spawn points: {baseCount} -> {points.Count}");
        }
        catch (Exception e) { Plugin.Log.LogError($"LevelStarter spawn points: {e}"); }
    }

    // ---------- Main menu lobby seats ----------
    // The lobby has 4 chairs around the table. Extra chairs are placed by SeatLayout (see below);
    // each extra seat i is a copy of an original seat moved by a transform (yaw + position), and the
    // matching seat camera gets the same transform so the per-seat close-up still frames the right chair.

    [HarmonyPatch(typeof(PlayerSeatUpdater), nameof(PlayerSeatUpdater.Awake))]
    [HarmonyPostfix]
    private static void PlayerSeatUpdater_Awake(PlayerSeatUpdater __instance)
    {
        try
        {
            var seats = __instance.seats;
            if (seats == null || seats.Length == 0 || seats.Length >= Max) return;
            int n = seats.Length;

            var originals = new Transform[n];
            for (int i = 0; i < n; i++) originals[i] = seats[i].transform;
            SeatLayout.Plan(originals, Max - n);

            var grown = new Il2CppReferenceArray<PlayerSeat>(Max);
            for (int i = 0; i < n; i++) grown[i] = seats[i];
            for (int i = n; i < Max; i++)
            {
                var p = SeatLayout.Placements[i - n];
                var src = seats[p.Source];
                var go = Object.Instantiate(src.gameObject, src.transform.parent);
                go.name = $"{src.gameObject.name}_MoreWizards_{i + 1}";
                SeatLayout.Place(go.transform, src.transform, p);
                grown[i] = go.GetComponent<PlayerSeat>();
            }
            __instance.seats = grown;
            Plugin.Log.LogInfo($"Lobby seats: {n} -> {Max} ({SeatLayout.UsedLayout} layout)");

            // If the camera controller woke up first, move its extra cameras now that we know the layout.
            var cams = Object.FindObjectOfType<MainMenuCameraController>();
            if (cams != null) SeatLayout.PlaceCameras(cams);
        }
        catch (Exception e) { Plugin.Log.LogError($"Lobby seats: {e}"); }
    }

    // Seat cameras (indexed by your seat) and run-overview cameras (indexed by player count - 1).
    [HarmonyPatch(typeof(MainMenuCameraController), nameof(MainMenuCameraController.Awake))]
    [HarmonyPostfix]
    private static void MainMenuCameraController_Awake(MainMenuCameraController __instance)
    {
        try
        {
            var overview = __instance.runOverviewCameras;
            if (overview != null && overview.Length > 0 && overview.Length < Max)
            {
                var grown = new Il2CppReferenceArray<Cinemachine.CinemachineVirtualCamera>(Max);
                for (int i = 0; i < Max; i++) grown[i] = overview[Math.Min(i, overview.Length - 1)];
                __instance.runOverviewCameras = grown;
            }

            var seatCams = __instance.seatCameras;
            if (seatCams != null && seatCams.Length > 0 && seatCams.Length < Max)
            {
                int n = seatCams.Length;
                Vector3 step = n >= 2
                    ? seatCams[n - 1].transform.position - seatCams[n - 2].transform.position
                    : Vector3.zero;
                var grown = new Il2CppReferenceArray<Cinemachine.CinemachineVirtualCamera>(Max);
                for (int i = 0; i < n; i++) grown[i] = seatCams[i];
                var template = seatCams[n - 1];
                for (int i = n; i < Max; i++)
                {
                    var go = Object.Instantiate(template.gameObject, template.transform.parent);
                    go.name = $"{template.gameObject.name}_MoreWizards_{i + 1}";
                    go.transform.position = template.transform.position + step * (i - n + 1);
                    go.transform.rotation = template.transform.rotation;
                    go.SetActive(false);
                    grown[i] = go.GetComponent<Cinemachine.CinemachineVirtualCamera>();
                }
                __instance.seatCameras = grown;
                SeatLayout.PlaceCameras(__instance); // no-op until the seat layout is known
            }
        }
        catch (Exception e) { Plugin.Log.LogError($"Menu cameras: {e}"); }
    }

    // ---------- UI lists with one prebuilt entry per player ----------

    private static void GrowUiList<T>(Il2CppList.List<T> list, string what) where T : Component
    {
        if (list == null || list.Count == 0 || list.Count >= Max) return;
        int before = list.Count;
        var template = list[before - 1];
        while (list.Count < Max)
        {
            var go = Object.Instantiate(template.gameObject, template.transform.parent);
            go.name = $"{template.gameObject.name}_MoreWizards_{list.Count + 1}";
            list.Add(go.GetComponent<T>());
        }
        Plugin.Log.LogInfo($"{what}: {before} -> {list.Count}");
    }

    // End-of-run / run-overview character cards.
    [HarmonyPatch(typeof(CharacterEntryGroup), nameof(CharacterEntryGroup.Show))]
    [HarmonyPrefix]
    private static void CharacterEntryGroup_Show(CharacterEntryGroup __instance)
    {
        try { GrowUiList(__instance.entries, "Character entries"); }
        catch (Exception e) { Plugin.Log.LogError($"Character entries: {e}"); }
    }

    // Pause menu player list.
    [HarmonyPatch(typeof(PlayerListPanel), nameof(PlayerListPanel.OnEnable))]
    [HarmonyPrefix]
    private static void PlayerListPanel_OnEnable(PlayerListPanel __instance)
    {
        try { GrowUiList(__instance.playerInfos, "Player list"); }
        catch (Exception e) { Plugin.Log.LogError($"Player list: {e}"); }
    }

    // ---------- Herding objective ----------
    // Has a lookup table "targets for N players" for N = 1..4 and throws if N isn't in it.
    // Extend the table by continuing its trend, so 5+ players get more targets (harder).

    [HarmonyPatch(typeof(HerdingObjectiveManager), nameof(HerdingObjectiveManager.Awake))]
    [HarmonyPrefix]
    private static void Herding_Awake(HerdingObjectiveManager __instance)
    {
        try
        {
            var table = __instance.herdTargetsForPlayerCounts;
            if (table == null || table.Length == 0) return;

            int maxCount = 0, atMax = 0, atPrev = 0, prevCount = 0;
            for (int i = 0; i < table.Length; i++)
            {
                var e = table[i];
                if (e._PlayerCount_k__BackingField > maxCount)
                {
                    maxCount = e._PlayerCount_k__BackingField;
                    atMax = e._HerdTargetCount_k__BackingField;
                }
            }
            for (int i = 0; i < table.Length; i++)
            {
                var e = table[i];
                if (e._PlayerCount_k__BackingField < maxCount && e._PlayerCount_k__BackingField > prevCount)
                {
                    prevCount = e._PlayerCount_k__BackingField;
                    atPrev = e._HerdTargetCount_k__BackingField;
                }
            }
            if (maxCount >= Max) return;

            int stepPerPlayer = Math.Max(1, prevCount > 0 ? atMax - atPrev : 1);
            int add = Max - maxCount;
            var grown = new Il2CppStructArray<HerdingObjectiveManager.HerdTargetsForPlayerCount>(table.Length + add);
            for (int i = 0; i < table.Length; i++) grown[i] = table[i];
            for (int k = 1; k <= add; k++)
            {
                var e = new HerdingObjectiveManager.HerdTargetsForPlayerCount();
                e._PlayerCount_k__BackingField = maxCount + k;
                e._HerdTargetCount_k__BackingField = atMax + stepPerPlayer * k;
                grown[table.Length + k - 1] = e;
            }
            __instance.herdTargetsForPlayerCounts = grown;
        }
        catch (Exception e) { Plugin.Log.LogError($"Herding table: {e}"); }
    }
}
