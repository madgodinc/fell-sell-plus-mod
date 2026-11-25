using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FellSellPlus.Shop;

// Shelf price changes and stocking, done the way the game itself does them.
//  - Price: the game's price dialog callback (ShelfSlot._SecondaryInteract_b__45_0, 0x1807D4380)
//    sets basePrice (+0x58) = your price, currentPrice (+0x54) = ShopManager.GetModifiedPrice(item,
//    basePrice) = what the customer pays, then SaveManager.SetLastItemPrice(item, basePrice).
//  - Stocking: ShelfSlot.PlaceItem(item, amount, price, instant) (0x1807D31F0) checks CanAcceptItem,
//    sets the prices the same way, records the last price, updates the slot UI and returns how many
//    it placed; the mod then removes exactly that many from the bag or a town chest.
internal static class ShelfActions
{
    public static IEnumerable<ShelfSlot> Slots()
    {
        var all = ShelfSlot.AllSlots;
        if (all == null) yield break;
        for (int i = 0; i < all.Count; i++) if (all[i] != null) yield return all[i];
    }

    public static bool IsSellable(ShelfSlot s) => s != null && !ShopStore.IsDisplay(s.transform.position);

    public static void SetPrice(ShelfSlot s, int price)
    {
        var sm = ShopManager.Instance;
        if (s == null || s.IsEmpty || s.currentItem == null || sm == null) return;
        price = Math.Max(1, price);
        s.basePrice = price;
        s.currentPrice = sm.GetModifiedPrice(s.currentItem, price);
        SaveManager.Instance?.SetLastItemPrice(s.currentItem, price);
        s.UpdateSlotUI();
    }

    // A candidate for stocking: one item kind with where it lies.
    public sealed class Source
    {
        public ItemData Item;
        public int InBag, InChests;
        public readonly List<InventorySlot> BagSlots = new();
        public readonly List<InventorySlot> ChestSlots = new();
    }

    // Items you own that some free, sellable shelf place accepts. Upgraded gear and containers are
    // left out (a shelf place keeps no upgrade level or contents). Blasphemous items can't be sold.
    public static List<Source> Sources(List<ShelfSlot> freePlaces)
    {
        var map = new Dictionary<IntPtr, Source>();
        void Add(InventorySlot sl, bool chest)
        {
            if (sl == null || sl.item == null || sl.quantity <= 0 || sl.isEquipped) return;
            if (sl.upgradeLevel > 0 || (sl.storedItems != null && sl.storedItems.Count > 0)) return;
            if (sl.item.rarity == ItemRarity.Blasphemous) return;
            if (!map.TryGetValue(sl.item.Pointer, out var src)) map[sl.item.Pointer] = src = new Source { Item = sl.item };
            if (chest) { src.InChests += sl.quantity; src.ChestSlots.Add(sl); }
            else { src.InBag += sl.quantity; src.BagSlots.Add(sl); }
        }
        var inv = InventoryManager.Instance;
        if (inv != null)
        {
            if (inv.inventorySlots != null) for (int i = 0; i < inv.inventorySlots.Count; i++) Add(inv.inventorySlots[i], false);
            if (inv.quickSlots != null) for (int i = 0; i < inv.quickSlots.Length; i++) Add(inv.quickSlots[i], false);
        }
        foreach (var chest in UnityEngine.Object.FindObjectsOfType<ChestStorage>())
        {
            if (chest == null || chest.inventory == null) continue;
            for (int i = 0; i < chest.inventory.Count; i++) Add(chest.inventory[i], true);
        }
        return map.Values.Where(s => freePlaces.Any(p => p.CanAcceptItem(s.Item))).ToList();
    }

    // Default price for a newly stocked item: the price you last set for it, else the best price a
    // customer accepted, else the game's own starting price.
    public static int DefaultPrice(ItemData item, int bagCustomPrice)
    {
        if (bagCustomPrice > 0) return bagCustomPrice;
        var save = SaveManager.Instance;
        int last = save != null ? save.GetLastItemPrice(item) : 0;
        if (last > 1) return last;
        int best = Inventory.Value.Best(item);
        if (best > 0) return best;
        return save != null ? Math.Max(1, save.GetInitialStartPrice(item)) : 1;
    }

    // Puts as many as one free place holds (bag first, then chests). Returns the amount placed.
    public static int Stock(Source src, List<ShelfSlot> freePlaces, out string where)
    {
        where = null;
        var place = freePlaces.FirstOrDefault(p => p.IsEmpty && p.CanAcceptItem(src.Item));
        if (place == null) return 0;
        var from = src.BagSlots.Where(s => s.item != null && s.quantity > 0).Select(s => (s, chest: false))
            .Concat(src.ChestSlots.Where(s => s.item != null && s.quantity > 0).Select(s => (s, chest: true)))
            .FirstOrDefault();
        if (from.s == null) return 0;

        int want = Math.Max(1, Math.Min(from.s.quantity, Math.Max(1, place.MaxCapacity)));
        int price = DefaultPrice(src.Item, from.chest ? 0 : from.s.customPrice);
        int placed = place.PlaceItem(src.Item, want, price, true);
        if (placed <= 0) return 0;

        if (from.chest)
        {
            from.s.quantity -= placed;
            if (from.s.quantity <= 0) from.s.Clear();
        }
        else InventoryManager.Instance.RemoveItemAmount(from.s, placed);
        InventoryManager.Instance?.NotifyInventoryChanged();
        where = from.chest ? "chest" : "bag";
        return placed;
    }
}
