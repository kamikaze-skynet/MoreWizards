using System;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using UObject = UnityEngine.Object;

namespace MoreWizards;

/// <summary>
/// A health bar over each boss's head when there's more than one boss (extra bosses), so you can see which is which.
/// The game's big boss bar at the top stays as it is.
///
/// Runs on every player's game with the mod: the bosses are the living enemies spawned from any boss prefab (extra
/// bosses can be other kinds), each labelled with its own name. Only looks while a boss objective exists.
/// </summary>
public class BossBars : MonoBehaviour
{
    public BossBars(IntPtr ptr) : base(ptr) { }

    private sealed class Bar
    {
        public HealthContainer Health;
        public NetworkObject Obj;
        public RectTransform Root, Fill;
        public TextMeshProUGUI Label;
        public string Name = "";
        public float Top;
    }

    private const float Width = 160f, Height = 12f;

    private readonly Dictionary<IntPtr, Bar> bars = new();
    private GameObject canvasGo;
    private HashSet<string> bossNames;
    private bool multi;                 // this fight has (had) more than one boss
    private float nextScan, nextErrorLog;

    private static bool Alive(UObject o) => o is not null && !o.WasCollected && o != null;

    private void Update()
    {
        try
        {
            if (!Plugin.OverheadHealthBars.Value) { ClearAll(); return; }
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 0.5f;
                Scan();
            }
            if (bars.Count > 0) Place();
        }
        catch (Exception ex)
        {
            if (Time.unscaledTime < nextErrorLog) return;
            nextErrorLog = Time.unscaledTime + 10f;
            Plugin.Log.LogError($"Boss bars: {ex}");
        }
    }

    // ---------- which bosses ----------

    private static NetworkObject OwnerObject(Component c)
    {
        try { return c.GetComponentInParent<NetworkObject>(); } catch { return null; }
    }

    private static string NameOf(Component c)
    {
        var o = OwnerObject(c);
        try { return o == null ? "" : o.gameObject.name.Replace("(Clone)", "").Trim(); } catch { return ""; }
    }

    private static bool AliveBoss(HealthContainer c) =>
        Alive(c) && c.gameObject.activeInHierarchy && !c.IsHealthZero();

    private void Scan()
    {
        // No scene searches outside the boss fight (the boss objective tells us when it appears).
        var objective = ExtraBosses.CurrentObjective;
        if (!Alive(objective) || !objective.IsSpawned) { ClearAll(); multi = false; return; }
        if (!Alive(objective.bossHealthContainer)) return; // boss not spawned yet

        bossNames ??= ExtraBosses.BossPrefabNames();
        if (bossNames.Count == 0) { bossNames = null; return; }
        var living = new List<EnemyHealthContainer>();
        foreach (var c in UObject.FindObjectsOfType<EnemyHealthContainer>())
            if (AliveBoss(c) && bossNames.Contains(NameOf(c))) living.Add(c);

        if (living.Count >= 2) multi = true;
        if (living.Count == 0) multi = false;
        bool show = multi || (Plugin.OverheadBarsForSingleBoss.Value && living.Count > 0);

        // Drop bars for dead or gone bosses.
        var drop = new List<IntPtr>();
        foreach (var kv in bars)
            if (!show || !AliveBoss(kv.Value.Health) || !living.Exists(l => l.Pointer == kv.Key)) drop.Add(kv.Key);
        foreach (var k in drop) { if (Alive(bars[k].Root)) Destroy(bars[k].Root.gameObject); bars.Remove(k); }
        if (!show) return;

        foreach (var c in living)
            if (!bars.ContainsKey(c.Pointer)) Create(c);
    }

    // ---------- drawing ----------

    private void EnsureCanvas()
    {
        if (Alive(canvasGo)) return;
        canvasGo = new GameObject("MoreWizards_BossBars");
        DontDestroyOnLoad(canvasGo);
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -1; // under the game's HUD and menus
    }

    private void Create(EnemyHealthContainer c)
    {
        EnsureCanvas();
        var obj = OwnerObject(c);
        if (obj == null) return;

        var root = new GameObject("BossBar").AddComponent<RectTransform>();
        root.SetParent(canvasGo.transform, false);
        root.anchorMin = root.anchorMax = Vector2.zero;   // positioned in screen pixels
        root.pivot = new Vector2(0.5f, 0f);
        var bg = root.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.65f);
        bg.raycastTarget = false;

        var fill = new GameObject("Fill").AddComponent<RectTransform>();
        fill.SetParent(root, false);
        fill.anchorMin = new Vector2(0f, 0f);
        fill.anchorMax = new Vector2(0f, 1f);
        fill.pivot = new Vector2(0f, 0.5f);
        fill.offsetMin = new Vector2(2f, 2f);
        fill.offsetMax = new Vector2(2f, -2f);
        var fi = fill.gameObject.AddComponent<Image>();
        fi.color = new Color(0.86f, 0.16f, 0.16f, 0.95f);
        fi.raycastTarget = false;

        TextMeshProUGUI label = null;
        var anyText = UObject.FindObjectOfType<TextMeshProUGUI>();
        if (anyText != null)
        {
            label = new GameObject("Name").AddComponent<TextMeshProUGUI>();
            label.rectTransform.SetParent(root, false);
            label.rectTransform.anchorMin = label.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            label.rectTransform.pivot = new Vector2(0.5f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(0f, 2f);
            label.rectTransform.sizeDelta = new Vector2(Width * 2f, 24f);
            label.font = anyText.font;
            label.alignment = TextAlignmentOptions.Bottom;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
            label.color = Color.white;
            label.outlineWidth = 0.2f;
            label.outlineColor = new Color32(0, 0, 0, 220);
        }

        var cfg = ExtraBosses.ConfigFor(obj);
        string name = cfg != null ? ExtraBosses.DisplayName(cfg.BossType) : NameOf(c);
        bars[c.Pointer] = new Bar { Health = c, Obj = obj, Root = root, Fill = fill, Label = label, Name = name, Top = HeadHeight(obj) };
    }

    // Height of the boss's head above its position, from its renderers (meshes).
    private static float HeadHeight(NetworkObject obj)
    {
        float top = 4f;
        try
        {
            float baseY = obj.transform.position.y, max = float.MinValue;
            foreach (var r in obj.GetComponentsInChildren<Renderer>())
                if (r != null && r.enabled && !r.GetIl2CppType().Name.Contains("Particle")) max = Mathf.Max(max, r.bounds.max.y);
            if (max > baseY + 0.5f && max < baseY + 40f) top = max - baseY;
        }
        catch { }
        return top + Plugin.OverheadBarExtraHeight.Value;
    }

    private void Place()
    {
        var cam = Camera.main;
        if (cam == null) return;
        float scale = Mathf.Max(0.5f, Screen.height / 1080f);

        foreach (var b in bars.Values)
        {
            if (!Alive(b.Root) || !Alive(b.Obj)) continue;
            Vector3 sp = cam.WorldToScreenPoint(b.Obj.transform.position + Vector3.up * b.Top);
            bool visible = sp.z > 0f && sp.x > -100f && sp.x < Screen.width + 100f && sp.y > -100f && sp.y < Screen.height + 100f;
            if (b.Root.gameObject.activeSelf != visible) b.Root.gameObject.SetActive(visible);
            if (!visible) continue;

            b.Root.anchoredPosition = new Vector2(sp.x, sp.y);
            b.Root.sizeDelta = new Vector2(Width * scale, Height * scale);

            float max = 0f, cur = 0f;
            try { max = b.Health.MaxHealth; cur = b.Health.CurrentHealth; } catch { }
            float pct = max > 0f ? Mathf.Clamp01(cur / max) : 0f;
            b.Fill.sizeDelta = new Vector2((Width * scale - 4f) * pct, -4f);

            if (b.Label != null)
            {
                b.Label.fontSize = 15f * scale;
                b.Label.rectTransform.sizeDelta = new Vector2(Width * 2f * scale, 24f * scale);
                string text = $"{b.Name}  {Mathf.CeilToInt(pct * 100f)}%";
                if (b.Label.text != text) b.Label.text = text;
            }
        }
    }

    private void ClearAll()
    {
        foreach (var b in bars.Values) if (Alive(b.Root)) Destroy(b.Root.gameObject);
        bars.Clear();
    }
}
