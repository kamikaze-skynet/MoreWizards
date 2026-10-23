using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Il2CppList = Il2CppSystem.Collections.Generic;

namespace MoreWizards;

/// <summary>
/// Adds a "3/8 wizards - 5 invites left" label under the lobby's Invite Friends button.
/// The label is a child of the button, so it appears and hides with it.
/// It refreshes whenever the game updates the lobby seats or the session roster changes.
/// </summary>
[HarmonyPatch]
internal static class InviteCounter
{
    private const string LabelName = "MoreWizards_InviteCounter";

    private static TextMeshProUGUI label;

    private static readonly Color OpenColor = new(0.85f, 0.95f, 1f, 1f);
    private static readonly Color FullColor = new(1f, 0.7f, 0.25f, 1f);

    private static bool LabelAlive => label != null && !label.WasCollected;

    // ---------- Hooks ----------

    [HarmonyPatch(typeof(InviteFriendsButton), nameof(InviteFriendsButton.OnEnable))]
    [HarmonyPostfix]
    private static void InviteButton_OnEnable(InviteFriendsButton __instance)
    {
        if (!Plugin.ShowInviteCounter.Value) return;
        try
        {
            EnsureLabel(__instance);
            Refresh();
        }
        catch (Exception ex) { Plugin.Log.LogError($"Invite counter (create): {ex}"); }
    }

    [HarmonyPatch(typeof(PlayerSeatUpdater), nameof(PlayerSeatUpdater.UpdateSeats))]
    [HarmonyPostfix]
    private static void Seats_UpdateSeats(Il2CppList.List<MainMenuPlayerData> playerData)
    {
        int hint = 0;
        try { if (playerData != null) hint = playerData.Count; } catch { }
        Refresh(hint);
    }

    [HarmonyPatch(typeof(PlayerSeatUpdater), nameof(PlayerSeatUpdater.RefreshSeats))]
    [HarmonyPostfix]
    private static void Seats_Refresh() => Refresh();

    [HarmonyPatch(typeof(SessionManager), nameof(SessionManager.AddPlayerData))]
    [HarmonyPostfix]
    private static void Session_Add() => Refresh();

    [HarmonyPatch(typeof(SessionManager), nameof(SessionManager.RemovePlayerData))]
    [HarmonyPostfix]
    private static void Session_Remove() => Refresh();

    [HarmonyPatch(typeof(SessionManager), nameof(SessionManager.StopSession))]
    [HarmonyPostfix]
    private static void Session_Stop() => Refresh();

    // ---------- Label ----------

    private static void EnsureLabel(InviteFriendsButton button)
    {
        Transform parent = button.transform;

        // Reuse the label if it already exists under this button (e.g. after the menu re-enables).
        Transform existing = parent.Find(LabelName);
        if (existing != null)
        {
            label = existing.GetComponent<TextMeshProUGUI>();
            if (LabelAlive) return;
        }

        // Borrow font + material from the button's own text so the label matches the game's style.
        TextMeshProUGUI template = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (template == null) template = UnityEngine.Object.FindObjectOfType<TextMeshProUGUI>();

        var go = new GameObject(LabelName);
        go.layer = button.gameObject.layer;
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);

        // Pin to the bottom edge of the button, stretched to its width, hanging below it.
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, Plugin.InviteCounterOffsetY.Value);
        rect.sizeDelta = new Vector2(160f, 40f);

        // Don't let a layout group on the button reposition us.
        var layout = go.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        if (template != null)
        {
            tmp.font = template.font;
            tmp.fontSharedMaterial = template.fontSharedMaterial;
        }
        else if (TMP_Settings.defaultFontAsset != null)
        {
            tmp.font = TMP_Settings.defaultFontAsset;
        }

        float size = Plugin.InviteCounterFontSize.Value;
        if (size <= 0f) size = template != null ? Mathf.Clamp(template.fontSize * 0.8f, 18f, 40f) : 26f;
        tmp.fontSize = size;
        tmp.alignment = TextAlignmentOptions.Top;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.richText = true;
        tmp.raycastTarget = false;
        tmp.color = OpenColor;

        label = tmp;
        Plugin.Log.LogInfo($"Invite counter attached to '{button.gameObject.name}'.");
    }

    private static void Refresh(int hint = 0)
    {
        if (!LabelAlive) return;
        try
        {
            if (!Plugin.ShowInviteCounter.Value)
            {
                label.gameObject.SetActive(false);
                return;
            }

            int max = Plugin.MaxPlayers.Value;
            int count = DifficultyPatches.QueryPlayerCount();
            if (count <= 0) count = hint;
            count = Mathf.Clamp(count, 1, max);
            int free = max - count;

            if (free > 0)
            {
                label.text = $"<b>{count}/{max}</b> wizards  •  {free} invite{(free == 1 ? "" : "s")} left";
                label.color = OpenColor;
            }
            else
            {
                label.text = $"<b>{count}/{max}</b> wizards  •  lobby full";
                label.color = FullColor;
            }

            if (!label.gameObject.activeSelf) label.gameObject.SetActive(true);
        }
        catch (Exception ex) { Plugin.Log.LogError($"Invite counter (refresh): {ex}"); }
    }
}
