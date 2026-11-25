using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FellSellPlus.Shop;
using Il2CppTMPro;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FellSellPlus.Crafting;

// Crafting bench improvements, driven every frame from Plugin.OnUpdate while CraftingUI is open.
// It works on the rows the game already built (CraftingUI._activeSlots), so no list-building code
// is replaced: rows are reordered (sibling index), hidden by filters, and get an info suffix.
internal static class CraftModule
{
    sealed class Filters
    {
        public bool CraftableOnly { get; set; }
        public bool NeededOnly { get; set; }
        public bool HideCapped { get; set; }
        public int Rarity { get; set; } = -1; // -1 = any
        public int Type { get; set; } = -1;   // ItemType, -1 = any
        public List<string> Stats { get; set; } = new();
        public bool MatchAll { get; set; }
        public Filters Copy() { var f = (Filters)MemberwiseClone(); f.Stats = new List<string>(Stats ?? new()); return f; }
    }

    sealed class SetsFile
    {
        public Filters Last { get; set; }
        public List<NamedSet> Sets { get; set; } = new();
    }

    sealed class NamedSet { public string Name { get; set; } public Filters F { get; set; } }

    static Filters _f = new();
    static SetsFile _sets;
    static int _setIndex = -1;
    static bool _wasOpen, _dirty;
    static int _lastCount = -1;
    static float _nextApply;
    static float _origDelay = -1f;

    // slot pointer -> (recipe pointer, category text the game wrote)
    static readonly Dictionary<IntPtr, (IntPtr recipe, string text)> _origCategory = new();

    static GameObject _bar;
    static TextMeshProUGUI _bCraftable, _bNeeded, _bCapped, _bRarity, _bSet, _bType, _bStat, _bStatToggle, _bMatch;
    static int _statCursor;
    static DateTime _setsStamp;

    // Stats a recipe's result can offer, for the stat filter. Key, Russian, English, getter (non-zero = has it).
    static readonly (string key, string ru, string en, Func<ItemData, float> get)[] StatDefs =
    {
        ("comfort", "Комфорт", "Comfort", i => i.comfortBonus),
        ("prestige", "Престиж", "Prestige", i => i.prestigeBonus),
        ("tip", "Чаевые", "Tips", i => i.tipBonus),
        ("price", "Цена +%", "Price +%", i => (i.subTypeModifiers?.Count ?? 0) + (i.rarityModifiers?.Count ?? 0)),
        ("damage", "Урон оружия", "Weapon damage", i => i.damage),
        ("armor", "Броня", "Armor", i => i.armor),
        ("heal", "Лечение", "Healing", i => i.healAmount + i.healOverTime),
        ("hp", "Здоровье (подз.)", "Health (dungeon)", i => i.dungeonBonusHealth),
        ("stamina", "Выносливость (подз.)", "Stamina (dungeon)", i => i.dungeonBonusStamina + i.dungeonStaminaRegen),
        ("mana", "Мана (подз.)", "Mana (dungeon)", i => i.dungeonBonusMana + i.dungeonManaRegen),
        ("carry", "Максимальный вес", "Max Weight", i => i.dungeonBonusMaxCarryWeight),
        ("dArmor", "Броня (подз.)", "Armor (dungeon)", i => i.dungeonBonusArmor),
        ("speed", "Скорость передвижения", "Move Speed", i => i.dungeonMoveSpeed),
        ("aspeed", "Скорость атаки", "Attack Speed", i => i.dungeonAttackSpeed),
        ("dmg", "Урон от атаки (подз.)", "Attack Damage (dungeon)", i => i.dungeonBonusAttackDamage),
        ("crit", "Крит. удар", "Crit", i => i.dungeonBonusCritChance + i.dungeonBonusCritDamage),
        ("rerolls", "Перебросы", "Rerolls", i => i.dungeonBonusRerolls),
        ("drop", "Шанс выпадения", "Drop Chance", i => i.dungeonDropChance),
        ("recipe", "Шанс на рецепт", "Recipe Chance", i => i.dungeonRecipeDropChance),
    };

    static bool HasStat(ItemData i, string key)
    {
        if (i == null) return false;
        foreach (var d in StatDefs) if (d.key == key) { try { return Math.Abs(d.get(i)) > 0.0001f; } catch { return false; } }
        return false;
    }

    static string SetsPath => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus", "craft_filter_sets.json");

    public static void Tick()
    {
        try
        {
            if (!CraftSettings.Enabled.Value) return;
            // Game singletons are only safe in gameplay scenes; the bench is in town.
            if (Inventory.ItemCounts.Scene() != Inventory.ItemCounts.Town) return;
            var ui = CraftingUI.Instance;
            bool open = ui != null && ui.IsOpen;
            if (open && !_wasOpen) OnOpen(ui);
            if (!open && _wasOpen) OnClose();
            _wasOpen = open;
            if (!open) return;

            NeedKey(ui);
            var slots = ui._activeSlots;
            int count = slots != null ? slots.Count : 0;
            if (count != _lastCount || _dirty || Time.unscaledTime >= _nextApply)
            {
                _lastCount = count; _dirty = false;
                _nextApply = Time.unscaledTime + 1f;
                Apply(ui);
            }
        }
        catch (Exception e) { Plugin.L.LogWarning($"Crafting tick: {e.Message}"); }
    }

    static void OnOpen(CraftingUI ui)
    {
        LoadSets();
        if (!CraftSettings.RememberFilters.Value) _f = new Filters();
        if (CraftSettings.InstantList.Value)
        {
            if (_origDelay < 0) _origDelay = ui.recipeEnterDelay;
            ui.recipeEnterDelay = 0f;
        }
        if (CraftSettings.FilterBar.Value && _bar == null) BuildBar(ui);
        if (_bar != null) _bar.SetActive(CraftSettings.FilterBar.Value);
        UpdateBar();
        _lastCount = -1;
    }

    static void OnClose()
    {
        if (CraftSettings.RememberFilters.Value) SaveSets();
        _origCategory.Clear();
    }

    static void NeedKey(CraftingUI ui)
    {
        var kb = Keyboard.current;
        if (kb == null) return;
        var key = ShopSettings.ParseKey(CraftSettings.NeedKey.Value, Key.N);
        if (!kb[key].wasPressedThisFrame) return;
        var r = ui.SelectedRecipe;
        if (r == null) return;
        bool on = NeedList.Toggle(r);
        ShopModule.Toast(on ? Ui.T("Рецепт отмечен как нужный", "Recipe marked as Need") : Ui.T("Отметка снята", "Need mark removed"));
        _dirty = true;
    }

    sealed class Row
    {
        public CraftingRecipeSlotUI Slot;
        public RecipeData Recipe;
        public bool Craftable, Furniture, Capped, Need;
        public int Placed, Stored;
        public string Name;
    }

    static void Apply(CraftingUI ui)
    {
        var slots = ui._activeSlots;
        if (slots == null || slots.Count == 0) return;
        var cm = CraftingManager.Instance;
        var sm = ShopManager.Instance;
        int cap = CraftSettings.PlacedCap.Value;

        var rows = new List<Row>();
        int minSibling = int.MaxValue;
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (s == null || s.Recipe == null) continue;
            var r = s.Recipe;
            var item = r.resultItem;
            var row = new Row { Slot = s, Recipe = r, Name = item != null ? item.GetDisplayName() : r.name };
            row.Craftable = cm != null && cm.CanCraft(r);
            row.Furniture = item != null && item.IsFurniture;
            row.Placed = row.Furniture && sm != null ? sm.GetPlacedFurnitureCount(item) : 0;
            row.Capped = row.Furniture && row.Placed >= cap;
            row.Stored = item != null && cm != null ? cm.GetItemCount(item) : 0;
            row.Need = NeedList.Has(r);
            rows.Add(row);
            minSibling = Math.Min(minSibling, s.transform.GetSiblingIndex());
        }

        IEnumerable<Row> ordered = rows;
        if (CraftSettings.Sort.Value)
            ordered = rows.OrderBy(Group).ThenBy(r => r.Furniture ? r.Placed : 0).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase);

        int idx = minSibling;
        foreach (var row in ordered)
        {
            var go = row.Slot.gameObject;
            bool visible = Passes(row);
            if (go.activeSelf != visible) go.SetActive(visible);
            if (CraftSettings.Sort.Value) row.Slot.transform.SetSiblingIndex(idx++);
            Decorate(row, cap);
        }
    }

    static int Group(Row r)
    {
        if (CraftSettings.SinkCapped.Value && r.Capped) return 2;
        if (CraftSettings.CraftableFirst.Value && !r.Craftable) return 1;
        return 0;
    }

    static bool Passes(Row r)
    {
        if (_f.CraftableOnly && !r.Craftable) return false;
        if (_f.NeededOnly && !r.Need) return false;
        if (_f.HideCapped && r.Capped) return false;
        if (_f.Rarity >= 0 && (r.Recipe.resultItem == null || (int)r.Recipe.resultItem.rarity != _f.Rarity)) return false;
        var item = r.Recipe.resultItem;
        if (_f.Type >= 0 && (item == null || (int)item.itemType != _f.Type)) return false;
        if (_f.Stats != null && _f.Stats.Count > 0)
        {
            bool ok = _f.MatchAll ? _f.Stats.All(k => HasStat(item, k)) : _f.Stats.Any(k => HasStat(item, k));
            if (!ok) return false;
        }
        return true;
    }

    static void Decorate(Row r, int cap)
    {
        var t = r.Slot.categoryText;
        if (t != null && CraftSettings.RowInfo.Value)
        {
            var key = r.Slot.Pointer;
            if (!_origCategory.TryGetValue(key, out var o) || o.recipe != r.Recipe.Pointer)
                _origCategory[key] = o = (r.Recipe.Pointer, t.text);
            var parts = new List<string>();
            if (r.Need) parts.Add(Ui.Col(Ui.T("НУЖНО", "NEED"), Ui.Warn));
            if (r.Furniture)
                parts.Add(Ui.Col($"{Ui.T("Размещено", "Placed")} {r.Placed}/{cap}", r.Capped ? Ui.Good : Ui.Muted));
            parts.Add(Ui.Col($"{Ui.T("Есть", "Stored")} {r.Stored}", Ui.Muted));
            t.text = o.text + "  <size=85%>" + string.Join("  ·  ", parts) + "</size>";
        }
        if (CraftSettings.FadeCapped.Value)
        {
            var cg = r.Slot.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha > 0.99f && r.Capped) cg.alpha = 0.6f;
            else if (cg != null && !r.Capped && Math.Abs(cg.alpha - 0.6f) < 0.01f) cg.alpha = 1f;
        }
    }

    // ---------- filter bar ----------

    // Filters stand in a column to the right of the bench window, one per row, drawn like the
    // bench's own category tabs (their sprite and active/inactive colours) instead of red buttons.
    static readonly Dictionary<IntPtr, Image> _btnImages = new();
    static CraftingUI _ui;
    const float RowH = 46f, ColW = 300f, Gap = 6f;

    static void BuildBar(CraftingUI ui)
    {
        try
        {
            _ui = ui;
            var host = ui.craftButton != null ? ui.craftButton.transform.parent : (ui.recipeScrollRect != null ? ui.recipeScrollRect.transform.parent : null);
            if (host == null) return;
            var bar = Ui.Rect("FSP_CraftFilterBar", host);
            bar.anchorMin = bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0f, 1f);
            bar.anchoredPosition = new Vector2(18f, 0f);
            bar.sizeDelta = new Vector2(ColW, 11 * (RowH + Gap));
            _bar = bar.gameObject;

            int row = 0;
            _bCraftable = Btn(bar, row++, Ui.T("Можно создать", "Craftable"), () => { _f.CraftableOnly ^= true; Changed(); });
            _bNeeded = Btn(bar, row++, Ui.T("Нужные", "Needed"), () => { _f.NeededOnly ^= true; Changed(); });
            _bCapped = Btn(bar, row++, Ui.T("Скрыть 3+", "Hide 3+"), () => { _f.HideCapped ^= true; Changed(); });
            _bRarity = Btn(bar, row++, "", () => { _f.Rarity = _f.Rarity >= 5 ? -1 : _f.Rarity + 1; Changed(); });
            _bType = Btn(bar, row++, "", () => { _f.Type = NextType(_f.Type); Changed(); });
            _bStat = Btn(bar, row++, "", () => { _statCursor = (_statCursor + 1) % StatDefs.Length; UpdateBar(); });
            _bStatToggle = Btn(bar, row++, "", () =>
            {
                var k = StatDefs[_statCursor].key;
                if (!_f.Stats.Remove(k)) _f.Stats.Add(k);
                Changed();
            });
            _bMatch = Btn(bar, row++, "", () => { _f.MatchAll ^= true; Changed(); });
            _bSet = Btn(bar, row++, "", CycleSet);
            Btn(bar, row++, Ui.T("Сохранить / сбросить", "Save / Clear"), SaveOrClear);
        }
        catch (Exception e) { Plugin.L.LogWarning($"Craft filter bar failed: {e.Message}"); _bar = null; }
    }

    static TextMeshProUGUI Btn(RectTransform bar, int row, string label, Action click)
    {
        var rt = Ui.Rect("Filter_" + row, bar);
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(0f, -row * (RowH + Gap));
        rt.sizeDelta = new Vector2(ColW, RowH);

        var img = rt.gameObject.AddComponent<Image>();
        var tab = _ui?.tabAll != null ? _ui.tabAll.GetComponent<Image>() : null;
        if (tab != null && tab.sprite != null) { img.sprite = tab.sprite; img.type = tab.type; }
        img.color = Inactive;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        b.onClick.AddListener((UnityEngine.Events.UnityAction)(() => click()));

        var tabText = _ui?.tabAll != null ? _ui.tabAll.GetComponentInChildren<TextMeshProUGUI>() : null;
        var t = Ui.Text(rt, "Label", label, 20, Ui.Ink, TextAlignmentOptions.Center);
        if (tabText != null) t.font = tabText.font;
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = new Vector2(12f, 2f); t.rectTransform.offsetMax = new Vector2(-12f, -2f);
        t.enableAutoSizing = true; t.fontSizeMin = 13f; t.fontSizeMax = 20f;
        t.enableWordWrapping = false; t.overflowMode = TextOverflowModes.Ellipsis;
        _btnImages[t.Pointer] = img;
        return t;
    }

    static Color Active => _ui != null && _ui.activeTabColor.a > 0.01f ? _ui.activeTabColor : new Color(0.85f, 0.7f, 0.35f, 1f);
    static Color Inactive => _ui != null && _ui.inactiveTabColor.a > 0.01f ? _ui.inactiveTabColor : new Color(0.25f, 0.24f, 0.28f, 0.95f);

    static void Changed() { _setIndex = -1; _dirty = true; UpdateBar(); }

    static void UpdateBar()
    {
        if (_bar == null) return;
        Mark(_bCraftable, _f.CraftableOnly);
        Mark(_bNeeded, _f.NeededOnly);
        Mark(_bCapped, _f.HideCapped);
        _bCapped.text = $"{Ui.T("Скрыть", "Hide")} {CraftSettings.PlacedCap.Value}+";
        _bRarity.text = _f.Rarity < 0 ? Ui.T("Редкость: все", "Rarity: any") : $"{Ui.T("Редкость", "Rarity")}: {(ItemRarity)_f.Rarity}";
        Mark(_bRarity, _f.Rarity >= 0);
        if (_bType != null)
        {
            _bType.text = _f.Type < 0 ? Ui.T("Тип: все", "Type: any") : $"{Ui.T("Тип", "Type")}: {(ItemType)_f.Type}";
            Mark(_bType, _f.Type >= 0);
            var d = StatDefs[_statCursor];
            _bStat.text = $"{Ui.T("Стат", "Stat")}: {Ui.T(d.ru, d.en)} ▸";
            bool on = _f.Stats.Contains(d.key);
            _bStatToggle.text = on ? Ui.T("− убрать стат", "− remove stat") : Ui.T("+ добавить стат", "+ add stat");
            Mark(_bStatToggle, on);
            _bMatch.text = _f.Stats.Count == 0 ? Ui.T("Статы: нет", "Stats: none")
                : $"{(_f.MatchAll ? Ui.T("Все из", "All of") : Ui.T("Любой из", "Any of"))} {_f.Stats.Count}";
            Mark(_bMatch, _f.Stats.Count > 0);
        }
        _bSet.text = _setIndex >= 0 && _sets != null && _setIndex < _sets.Sets.Count
            ? _sets.Sets[_setIndex].Name : $"{Ui.T("Наборы", "Sets")} ({_sets?.Sets.Count ?? 0})";
    }

    static void Mark(TextMeshProUGUI t, bool on)
    {
        if (t == null) return;
        t.color = on ? Ui.GoldInk : Ui.Ink;
        if (_btnImages.TryGetValue(t.Pointer, out var img) && img != null) img.color = on ? Active : Inactive;
    }

    static bool AnyFilter => _f.CraftableOnly || _f.NeededOnly || _f.HideCapped || _f.Rarity >= 0 || _f.Type >= 0 || (_f.Stats?.Count ?? 0) > 0;

    // Item types that the bench's current rows actually produce, cycled in order; -1 = any.
    static int NextType(int current)
    {
        var types = new SortedSet<int>();
        var slots = CraftingUI.Instance?._activeSlots;
        if (slots != null)
            for (int i = 0; i < slots.Count; i++)
                if (slots[i]?.Recipe?.resultItem != null) types.Add((int)slots[i].Recipe.resultItem.itemType);
        foreach (var t in types) if (t > current) return t;
        return -1;
    }

    // With filters set: save them as a new named set. With none: nothing to save, so it clears the set selection.
    static void SaveOrClear()
    {
        LoadSets();
        if (AnyFilter && _setIndex < 0)
        {
            var name = $"{Ui.T("Набор", "Set")} {_sets.Sets.Count + 1}";
            _sets.Sets.Add(new NamedSet { Name = name, F = _f.Copy() });
            SaveSets();
            _setIndex = _sets.Sets.Count - 1;
            ShopModule.Toast($"{Ui.T("Сохранено", "Saved")}: {name}");
        }
        else { _f = new Filters(); _setIndex = -1; }
        _dirty = true; UpdateBar();
    }

    static void CycleSet()
    {
        LoadSets();
        if (_sets.Sets.Count == 0) return;
        _setIndex = (_setIndex + 1) % _sets.Sets.Count;
        _f = _sets.Sets[_setIndex].F.Copy();
        _dirty = true; UpdateBar();
    }

    static void LoadSets()
    {
        try
        {
            var stamp = File.Exists(SetsPath) ? File.GetLastWriteTimeUtc(SetsPath) : default;
            if (_sets != null && stamp != _setsStamp) _sets = null; // edited outside the game (e.g. renamed sets)
            _setsStamp = stamp;
        }
        catch { }
        if (_sets != null) return;
        try
        {
            _sets = File.Exists(SetsPath) ? JsonSerializer.Deserialize<SetsFile>(File.ReadAllText(SetsPath)) : null;
        }
        catch (Exception e) { Plugin.L.LogWarning($"craft_filter_sets.json unreadable: {e.Message}"); }
        _sets ??= new SetsFile();
        if (CraftSettings.RememberFilters.Value && _sets.Last != null) _f = _sets.Last.Copy();
    }

    static void SaveSets()
    {
        try
        {
            _sets ??= new SetsFile();
            _sets.Last = _f.Copy();
            Directory.CreateDirectory(Path.GetDirectoryName(SetsPath));
            File.WriteAllText(SetsPath, JsonSerializer.Serialize(_sets, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Plugin.L.LogWarning($"craft_filter_sets.json save failed: {e.Message}"); }
    }
}
