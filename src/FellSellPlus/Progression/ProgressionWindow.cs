using System;
using System.Collections.Generic;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FellSellPlus.Progression;

// The purchase window. Built from code once, styled from the game's own assets
// (TMP font of the interaction prompt, panel and button sprites of Grandma's shrine window).
internal static class ProgressionWindow
{
    static GameObject _root;
    static bool _builtRu;
    static TextMeshProUGUI _gold, _locked;
    static readonly List<Row> _rows = new();
    static bool _open;
    static int _openedFrame;

    static readonly Color Ink = new(0.93f, 0.89f, 0.80f, 1f);
    static readonly Color Muted = new(0.70f, 0.65f, 0.56f, 1f);
    static readonly Color GoldInk = new(1f, 0.82f, 0.40f, 1f);
    static readonly Color PanelFallback = new(0.09f, 0.075f, 0.06f, 0.96f);

    sealed class Row
    {
        public Stat Stat;
        public TextMeshProUGUI Level, Effect, Cost;
        public Button Buy;
    }

    static bool Ru => GameQuery.IsRussian();

    public static void Open()
    {
        if (_open) return;
        // Labels are written when the window is built: rebuild it after a language switch.
        if (_root != null && _builtRu != Ru) { UnityEngine.Object.Destroy(_root); _root = null; }
        if (_root == null) { Build(); _builtRu = Ru; }
        if (_root == null) return;
        _open = true;
        _openedFrame = Time.frameCount;
        _root.SetActive(true);
        GameWindows.Open(_root);
        Refresh();
    }

    public static void Close()
    {
        if (!_open) return;
        _open = false;
        GameWindows.Close(_root);
        if (_root != null) _root.SetActive(false);
    }

    public static void Tick()
    {
        if (!_open) return;
        ModalGuard.Touch();
        if (_root == null) { _open = false; return; } // scene unloaded under us
        if (!_root.activeSelf) { _open = false; return; } // closed by the game's window manager
        if (Time.frameCount == _openedFrame) return; // the E press that opened us
        var kb = Keyboard.current;
        var pad = Gamepad.current;
        if ((kb != null && kb.eKey.wasPressedThisFrame) // Esc is the game's: its window stack closes us
            || (pad != null && pad.buttonEast.wasPressedThisFrame))
            Close();
    }

    static PlayerInputHandler InputHandler() =>
        FirstPersonController.Instance != null ? FirstPersonController.Instance.playerInputHandler : null;

    static void Refresh()
    {
        bool unlocked = ProgressionState.Unlocked;
        var next = ProgressionState.NextTier();
        string nextText = next == null ? "" : Ru
            ? $"  ·  престиж {next.Value.prestige:0.#} → лимит {next.Value.cap}"
            : $"  ·  prestige {next.Value.prestige:0.#} → cap {next.Value.cap}";
        _gold.text = Ru
            ? $"Золото: {GameQuery.Gold():N0}  ·  Престиж лавки: {GameQuery.Prestige():0.##}{nextText}"
            : $"Gold: {GameQuery.Gold():N0}  ·  Shop prestige: {GameQuery.Prestige():0.##}{nextText}";
        _locked.gameObject.SetActive(!unlocked);
        if (!unlocked && next != null)
            _locked.text = Ru
                ? $"Бабушка поможет, когда престиж лавки достигнет {next.Value.prestige:0.#}."
                : $"Grandma will help once your shop prestige reaches {next.Value.prestige:0.#}.";
        foreach (var r in _rows)
        {
            r.Level.gameObject.transform.parent.gameObject.SetActive(unlocked);
            int lvl = ProgressionState.Level(r.Stat), cap = ProgressionState.Cap(r.Stat);
            r.Level.text = $"{lvl} / {cap}";
            r.Effect.text = EffectText(r.Stat);
            bool maxed = lvl >= cap;
            int cost = ProgressionState.NextCost(r.Stat);
            r.Cost.text = maxed ? (Ru ? "максимум" : "max") : $"{cost:N0}";
            r.Cost.color = maxed ? Muted : GoldInk;
            r.Buy.interactable = unlocked && !maxed && GameQuery.Gold() >= cost;
            // A maxed stat has nothing to buy: hide its button (the cost column already says "max").
            r.Buy.gameObject.SetActive(!maxed);
        }
    }

    static void OnBuy(Stat s)
    {
        ProgressionState.TryBuy(s);
        Refresh();
    }

    static string Name(Stat s) => s switch
    {
        Stat.MaxHealth => Ru ? "Здоровье" : "Health",
        Stat.MaxStamina => Ru ? "Выносливость" : "Stamina",
        Stat.MaxMana => Ru ? "Мана" : "Mana",
        Stat.HealthRegen => Ru ? "Восстановление здоровья" : "Health Regeneration",
        Stat.StaminaRegen => Ru ? "Восстановление выносливости" : "Stamina Regeneration",
        Stat.ManaRegen => Ru ? "Регенерация маны" : "Mana Regeneration",
        Stat.Damage => Ru ? "Урон" : "Damage",
        Stat.MoveSpeed => Ru ? "Скорость передвижения" : "Move Speed",
        _ => s.ToString(),
    };

    static string EffectText(Stat s)
    {
        float b = ProgressionState.Bonus(s);
        return s switch
        {
            Stat.Damage or Stat.MoveSpeed => $"+{b:0.#}%",
            Stat.HealthRegen or Stat.StaminaRegen or Stat.ManaRegen => $"+{b:0.0}/{(Ru ? "с" : "s")}",
            _ => $"+{b:0}",
        };
    }

    // ---------- construction ----------

    static void Build()
    {
        try
        {
            var font = FirstPersonController.Instance?.interactionText?.font;
            var shrineUi = GrandmaShrineUI._instance;
            var panelImg = shrineUi?._dialogWindow?.GetComponent<Image>();
            var buttonImg = shrineUi?._actionButton?.image;

            _root = new GameObject("FSP_ProgressionCanvas");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            var dim = Rect("Dim", _root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            var panel = Rect("Panel", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(820, 640));
            var bg = panel.gameObject.AddComponent<Image>();
            if (panelImg != null && panelImg.sprite != null) { bg.sprite = panelImg.sprite; bg.type = panelImg.type; bg.color = panelImg.color; }
            else bg.color = PanelFallback;

            var title = Text(panel, "Title", font, Ru ? "Наследие бабушки" : "Grandma's Legacy", 40, Ink, TextAlignmentOptions.Center);
            Place(title.rectTransform, 0, 40, 820, 60, top: true);
            _gold = Text(panel, "Gold", font, "", 24, GoldInk, TextAlignmentOptions.Center);
            Place(_gold.rectTransform, 0, 100, 820, 34, top: true);

            _locked = Text(panel, "Locked", font, "", 26, Muted, TextAlignmentOptions.Center);
            Place(_locked.rectTransform, 0, 300, 700, 120, top: true);
            _locked.enableWordWrapping = true;

            float y = 160;
            _rows.Clear();
            foreach (var s in ProgressionState.All)
            {
                var row = Rect("Row_" + s, panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -y), new Vector2(740, 48));
                var name = Text(row, "Name", font, Name(s), 24, Ink, TextAlignmentOptions.Left);
                Place(name.rectTransform, -235, 0, 300, 48);
                var r = new Row { Stat = s };
                r.Level = Text(row, "Level", font, "", 22, Muted, TextAlignmentOptions.Center);
                Place(r.Level.rectTransform, -15, 0, 90, 48);
                r.Effect = Text(row, "Effect", font, "", 22, Ink, TextAlignmentOptions.Center);
                Place(r.Effect.rectTransform, 85, 0, 110, 48);
                r.Cost = Text(row, "Cost", font, "", 22, GoldInk, TextAlignmentOptions.Right);
                Place(r.Cost.rectTransform, 195, 0, 100, 48);
                r.Buy = MakeButton(row, font, buttonImg, Ru ? "Купить" : "Buy");
                Place(r.Buy.GetComponent<RectTransform>(), 310, 0, 110, 40);
                var stat = s;
                r.Buy.onClick.AddListener((UnityAction)new Action(() => OnBuy(stat)));
                _rows.Add(r);
                y += 52;
            }

            var hint = Text(panel, "Hint", font, Ru ? "[Esc] Закрыть" : "[Esc] Close", 20, Muted, TextAlignmentOptions.Center);
            Place(hint.rectTransform, 0, -30, 820, 30, top: false);

            _root.SetActive(false);
        }
        catch (Exception e)
        {
            Plugin.L.LogError($"Progression window build failed: {e}");
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }

    static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    // Centre-anchored inside a row, or top/bottom-anchored inside the panel.
    static void Place(RectTransform rt, float x, float y, float w, float h, bool? top = null)
    {
        if (top == true) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.anchoredPosition = new Vector2(x, -y); }
        else if (top == false) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.anchoredPosition = new Vector2(x, -y); }
        else { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(x, y); }
        rt.sizeDelta = new Vector2(w, h);
    }

    static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = color; t.alignment = align;
        t.raycastTarget = false;
        TextFit.Line(t, size);
        return t;
    }

    static Button MakeButton(Transform parent, TMP_FontAsset font, Image style, string label)
    {
        var go = new GameObject("Buy");
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var img = go.AddComponent<Image>();
        if (style != null && style.sprite != null) { img.sprite = style.sprite; img.type = style.type; img.color = style.color; }
        else img.color = new Color(0.30f, 0.22f, 0.14f, 1f);
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        var t = Text(go.transform, "Label", font, label, 20, Ink, TextAlignmentOptions.Center);
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return b;
    }
}
