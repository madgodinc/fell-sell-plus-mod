using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FellSellPlus.Inventory;

// Item value and "what to get rid of first" (Purge order: lowest gold per kg first, then unknown
// value, then Needed, then Keep). Value = the best price a customer accepted (the game's own price
// memory, ItemPricingDetailsUI.GetMaxAcceptedPrice), so nothing is guessed.
internal static class Value
{
    public static int Best(ItemData item)
    {
        if (item == null) return 0;
        try { return Math.Max(0, ItemPricingDetailsUI.GetMaxAcceptedPrice(item)); } catch { return 0; }
    }

    // Gold per kg, or null when the value is unknown or the item weighs nothing.
    public static float? PerKg(ItemData item)
    {
        if (item == null || item.weight <= 0.0001f) return null;
        int v = Best(item);
        return v > 0 ? v / item.weight : null;
    }
}

// Keep marks: K while hovering an item. UserData/FellSellPlus/keep.json = {"items":["<item id>",...]}.
internal static class KeepList
{
    sealed class FileData { public List<string> items { get; set; } = new(); }
    static HashSet<string> _set;
    static string PathFile => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus", "keep.json");

    static HashSet<string> Set
    {
        get
        {
            if (_set != null) return _set;
            _set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(PathFile))
                    foreach (var s in JsonSerializer.Deserialize<FileData>(File.ReadAllText(PathFile))?.items ?? new()) _set.Add(s);
            }
            catch (Exception e) { Plugin.L.LogWarning($"keep.json not read: {e.Message}"); }
            return _set;
        }
    }

    public static bool Has(string id) => id != null && Set.Contains(id);

    public static bool Toggle(string id)
    {
        if (!Set.Remove(id)) Set.Add(id);
        bool on = Set.Contains(id);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathFile));
            File.WriteAllText(PathFile, JsonSerializer.Serialize(new FileData { items = Set.OrderBy(x => x).ToList() },
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Plugin.L.LogWarning($"keep.json not written: {e.Message}"); }
        return on;
    }
}

internal static class PurgeOrder
{
    // Sort key: group (0 normal by gold/kg, 1 unknown value, 2 Needed, 3 Keep, 4 weightless), then gold/kg.
    public static (int group, float perKg) Key(ItemData item)
    {
        if (item == null) return (5, 0);
        string id = item.name;
        if (KeepList.Has(id)) return (3, 0);
        if (NeedList.Missing(id) > 0) return (2, 0);
        if (item.weight <= 0.0001f) return (4, 0);
        var v = Value.PerKg(item);
        return v.HasValue ? (0, v.Value) : (1, 0);
    }

    // Rank of an item among the distinct ranked items of a slot list (1 = get rid of first).
    public static (int rank, int total) Rank(ItemData item, IEnumerable<InventorySlot> slots)
    {
        var items = slots.Where(s => s != null && s.item != null && s.quantity > 0 && s.item.weight > 0.0001f)
                         .Select(s => s.item).GroupBy(i => i.name).Select(g => g.First())
                         .OrderBy(i => Key(i).group).ThenBy(i => Key(i).perKg).ThenBy(i => i.name).ToList();
        int idx = items.FindIndex(i => i.name == item.name);
        return (idx + 1, items.Count);
    }
}

// Shift + the sort button sorts by Purge order instead of the selected criterion.
// InventorySortUtility.SortSlots (0x1807A0…) is called by InventoryManager.SortInventory,
// ChestStorageUI.SortChest and the toolbar callback (verified xrefs).
[HarmonyPatch(typeof(InventorySortUtility), nameof(InventorySortUtility.SortSlots))]
internal static class PurgeSortPatch
{
    static bool Prefix(Il2CppSystem.Collections.Generic.List<InventorySlot> slots, bool ascending)
    {
        if (!InventorySettings.PurgeSort.Value || slots == null) return true;
        var kb = Keyboard.current;
        if (kb == null || !kb.shiftKey.isPressed) return true;
        try
        {
            var all = new List<InventorySlot>();
            for (int i = 0; i < slots.Count; i++) all.Add(slots[i]);
            var filled = all.Where(s => s != null && !s.IsEmpty).ToList();
            var empty = all.Where(s => s == null || s.IsEmpty).ToList();
            var ordered = filled.OrderBy(s => PurgeOrder.Key(s.item).group).ThenBy(s => PurgeOrder.Key(s.item).perKg)
                                .ThenBy(s => s.item?.name).ToList();
            // "ascending" in the game's toolbar = arrow up; descending puts what to drop first at the top.
            if (ascending) ordered.Reverse();
            slots.Clear();
            foreach (var s in ordered) slots.Add(s);
            foreach (var s in empty) slots.Add(s);
            ShopToast(Progression.GameQuery.IsRussian() ? "Сортировка: на выброс" : "Sorted: Purge");
            return false;
        }
        catch (Exception e) { Plugin.L.LogWarning($"Purge sort skipped: {e.Message}"); return true; }
    }

    static void ShopToast(string m) { try { Shop.ShopModule.Toast(m); } catch { } }
}

// The sort criterion and direction the player last picked survive closing the window and the game.
// Toolbar state lives on InventorySortToolbarUI (_currentSortOption / _isAscending); OnEnable and
// OnDisable are Unity messages.
internal static class SortMemory
{
    sealed class FileData { public Dictionary<string, int[]> sorts { get; set; } = new(); }
    static FileData _data;
    static string PathFile => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus", "sort.json");

    static FileData Data
    {
        get
        {
            if (_data != null) return _data;
            try { _data = File.Exists(PathFile) ? JsonSerializer.Deserialize<FileData>(File.ReadAllText(PathFile)) : null; } catch { }
            return _data ??= new FileData();
        }
    }

    static string Key(InventorySortToolbarUI t) => t._isPlayerInventory ? "inventory" : "chest";

    [HarmonyPatch(typeof(InventorySortToolbarUI), "OnEnable")]
    internal static class RestorePatch
    {
        static void Postfix(InventorySortToolbarUI __instance)
        {
            if (!InventorySettings.RememberSort.Value || __instance == null) return;
            try
            {
                if (!Data.sorts.TryGetValue(Key(__instance), out var v) || v.Length < 2) return;
                __instance._currentSortOption = (ItemSortOption)v[0];
                __instance._isAscending = v[1] != 0;
                __instance.UpdateAllVisuals();
            }
            catch (Exception e) { Plugin.L.LogWarning($"Sort restore skipped: {e.Message}"); }
        }
    }

    [HarmonyPatch(typeof(InventorySortToolbarUI), "OnDisable")]
    internal static class SavePatch
    {
        static void Postfix(InventorySortToolbarUI __instance)
        {
            if (!InventorySettings.RememberSort.Value || __instance == null) return;
            try
            {
                Data.sorts[Key(__instance)] = new[] { (int)__instance._currentSortOption, __instance._isAscending ? 1 : 0 };
                Directory.CreateDirectory(Path.GetDirectoryName(PathFile));
                File.WriteAllText(PathFile, JsonSerializer.Serialize(Data));
            }
            catch (Exception e) { Plugin.L.LogWarning($"Sort save skipped: {e.Message}"); }
        }
    }
}

// Looking at a loose item on the ground adds a line to its name box: value per kg and where it
// would rank among what you carry, or what to drop to make room when it does not fit.
// ItemPickup.GetObjectText (0x1807A9640) is called through IInteractable; patching the body catches it.
[HarmonyPatch(typeof(ItemPickup), nameof(ItemPickup.GetObjectText))]
internal static class GroundWorthPatch
{
    static void Postfix(ItemPickup __instance, ref string __result)
    {
        if (!InventorySettings.GroundItemInfo.Value || __instance == null) return;
        try
        {
            var item = __instance.itemData;
            if (item == null || !__instance.IsLooseInScene()) return;
            string line = Line(item);
            if (!string.IsNullOrEmpty(line)) __result += $"\n<size=70%><color=#c8c8c8>{line}</color></size>";
        }
        catch { }
    }

    static string Line(ItemData item)
    {
        bool ru = Progression.GameQuery.IsRussian();
        var inv = InventoryManager.Instance;
        if (inv == null) return null;
        var perKg = Value.PerKg(item);
        // An unknown value says nothing useful under the name: show no value part then.
        string val = perKg.HasValue ? $"{perKg.Value:0} G/{(ru ? "кг" : "kg")}" : null;
        string Join(string head) => val == null ? head : (head == null ? val : $"{head} | {val}");
        if (inv.CanCarryItem(item, 1, null))
        {
            var slots = ItemCounts.CarriedSlots().ToList();
            slots.Add(new InventorySlot(item, 1, 0, 0));
            var (rank, total) = PurgeOrder.Rank(item, slots);
            return rank > 0 ? Join(ru ? $"На выброс {rank}/{total}" : $"Purge {rank}/{total} if picked up") : val;
        }
        // Too heavy: what (cheaper per kg, not Keep/Needed) would have to go.
        if (!perKg.HasValue) return ru ? "Не влезает" : "Too heavy";
        float need = inv.CurrentWeight + item.weight - inv.EffectiveMaxWeight;
        float freed = 0; int lost = 0; var drop = new List<string>();
        foreach (var s in ItemCounts.CarriedSlots().Where(s => s != null && s.item != null && s.quantity > 0 && !s.isEquipped)
                     .OrderBy(s => PurgeOrder.Key(s.item).group).ThenBy(s => PurgeOrder.Key(s.item).perKg))
        {
            var k = PurgeOrder.Key(s.item);
            if (k.group != 0 || k.perKg >= perKg.Value || freed >= need) break;
            int n = Math.Min(s.quantity, (int)Math.Ceiling((need - freed) / s.item.weight));
            freed += n * s.item.weight; lost += n * Value.Best(s.item);
            drop.Add($"{n} {InventorySortUtility.GetItemDisplayName(s.item)}");
        }
        int gain = Value.Best(item);
        if (freed >= need && drop.Count > 0)
            return ru ? $"Выгодно: выбросить {string.Join(", ", drop)} ({lost} G) ради {gain} G"
                      : $"Worth it: drop {string.Join(", ", drop)} ({lost} G) for {gain} G";
        return ru ? $"Не выгодно: {val} ниже всего, что можно выбросить" : $"Not worth it: {val} is below everything you'd drop";
    }
}

// Per-frame helpers, gameplay scenes only (called from InventoryModule.Tick after its scene gate).
internal static class PurgeTick
{
    static ItemData _hovered;
    static int _hoveredFrame;
    static string _pricesCheckedScene = "";

    public static void NoteHover(ItemData item) { _hovered = item; _hoveredFrame = Time.frameCount; }

    public static void Tick(string scene)
    {
        KeepKey();
        FixPricesAfterDungeonLoad(scene);
    }

    static void KeepKey()
    {
        if (!InventorySettings.KeepMarks.Value || _hovered == null) return;
        var kb = Keyboard.current;
        if (kb == null || !Enum.TryParse<Key>(InventorySettings.KeepKey.Value, true, out var key) || !kb[key].wasPressedThisFrame) return;
        var tip = InventoryTooltipUI.Instance;
        if (tip == null || tip.tooltipPanel == null || !tip.tooltipPanel.activeInHierarchy) return; // not hovering anymore
        bool on = KeepList.Toggle(_hovered.name);
        string name = InventorySortUtility.GetItemDisplayName(_hovered);
        bool ru = Progression.GameQuery.IsRussian();
        PickupNotificationManager.Instance?.NotifyCustom(null, ru ? $"{name}: {(on ? "оставить" : "снята отметка")}" : $"{name}: {(on ? "Keep" : "Keep removed")}",
            new Color(0.8f, 0.9f, 1f), 1);
    }

    // Loading a save inside the dungeon/forest skips the game's LoadItemLastPrices, so known prices
    // (and every value above) stay empty until town. Load them once from the same save file.
    static void FixPricesAfterDungeonLoad(string scene)
    {
        if (!InventorySettings.FixDungeonPrices.Value || scene == _pricesCheckedScene) return;
        _pricesCheckedScene = scene;
        if (scene == ItemCounts.Town) return;
        try
        {
            var sm = SaveManager.Instance;
            if (sm == null || sm._itemLastPrices == null || sm._itemLastPrices.Count > 0) return;
            int slot = sm.CurrentSlot;
            if (slot < 0) return;
            sm.LoadItemLastPrices(sm.GetSlotFileName(slot));
            Plugin.L.LogInfo($"Known prices loaded for the {scene} load ({sm._itemLastPrices.Count}).");
        }
        catch (Exception e) { Plugin.L.LogWarning($"Known prices fix skipped: {e.Message}"); }
    }
}
