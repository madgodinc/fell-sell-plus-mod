using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Inventory;

// Where the player is and how many of an item they own. Chest contents only exist in the town
// scene, so they are snapshotted there and reused in the dungeon/forest.
internal static class ItemCounts
{
    public const string Town = "MAIN", Dungeon = "DUNGEON", Forest = "FOREST";

    public static string Scene()
    {
        try { return SceneManager.GetActiveScene().name; } catch { return ""; }
    }

    public static bool InRun() { var s = Scene(); return s == Dungeon || s == Forest; }

    static readonly Dictionary<string, int> _storage = new();
    static readonly Dictionary<string, int> _placed = new();
    static float _nextSnapshot;

    // Called every frame from the module tick; refreshes the town snapshot every few seconds.
    public static void Tick()
    {
        if (Scene() != Town || Time.unscaledTime < _nextSnapshot) return;
        _nextSnapshot = Time.unscaledTime + 3f;
        try
        {
            _storage.Clear();
            foreach (var chest in UnityEngine.Object.FindObjectsOfType<ChestStorage>())
            {
                if (chest == null || chest.inventory == null) continue;
                foreach (var s in chest.inventory) Add(_storage, s);
            }
            _placed.Clear();
            var placed = ShopManager.Instance?._placedFurniture;
            if (placed != null)
                foreach (var item in placed)
                    if (item != null) _placed[item.name] = _placed.GetValueOrDefault(item.name) + 1;
        }
        catch (Exception e) { Plugin.L.LogWarning($"Storage snapshot failed: {e.Message}"); _nextSnapshot = Time.unscaledTime + 30f; }
    }

    static void Add(Dictionary<string, int> d, InventorySlot s)
    {
        if (s == null || s.item == null || s.quantity <= 0) return;
        d[s.item.name] = d.GetValueOrDefault(s.item.name) + s.quantity;
    }

    public static IEnumerable<InventorySlot> CarriedSlots()
    {
        var inv = InventoryManager.Instance;
        if (inv == null) yield break;
        if (inv.inventorySlots != null) foreach (var s in inv.inventorySlots) yield return s;
        if (inv.quickSlots != null) foreach (var s in inv.quickSlots) yield return s;
        yield return inv.rightHandSlot; yield return inv.leftHandSlot;
        yield return inv.helmetSlot; yield return inv.upperArmorSlot; yield return inv.lowerArmorSlot;
    }

    public static int Carried(string id)
    {
        int n = 0;
        foreach (var s in CarriedSlots())
            if (s != null && s.item != null && s.item.name == id) n += s.quantity;
        return n;
    }

    public static int Stored(string id) => _storage.GetValueOrDefault(id);
    public static IEnumerable<string> StoredNames() => _storage.Keys;

    // ItemData by asset name (the chest snapshot keeps names only); filled once, then cached.
    static readonly Dictionary<string, ItemData> _items = new();
    public static ItemData ResolveItem(string id)
    {
        if (_items.TryGetValue(id, out var it) && it != null) return it;
        foreach (var d in Resources.FindObjectsOfTypeAll<ItemData>())
            if (d != null) _items[d.name] = d;
        return _items.GetValueOrDefault(id);
    }
    public static int Placed(string id) => _placed.GetValueOrDefault(id);

    // "Have" for Total rules: carried + chests + placed furniture.
    public static int Have(string id) => Carried(id) + Stored(id) + Placed(id);
}
