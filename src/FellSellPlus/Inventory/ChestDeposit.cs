using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FellSellPlus.Inventory;

// Deposit: move every bag stack whose item the open chest already holds into that chest, using the
// game's own TryMoveToChest (it handles fit, stacking and refresh). Auto runs it on every open of
// a chest the player flagged; flags are stored per save slot by chest position.
internal static class ChestDeposit
{
    static string AutoPath => Path.Combine(LootRules.Dir, "auto_deposit.json");
    static Dictionary<string, HashSet<string>> _auto; // slot -> chest keys

    static Dictionary<string, HashSet<string>> Auto
    {
        get
        {
            if (_auto != null) return _auto;
            _auto = new();
            try { if (File.Exists(AutoPath)) _auto = JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(File.ReadAllText(AutoPath)) ?? new(); }
            catch (Exception e) { Plugin.L.LogWarning($"auto_deposit.json unreadable: {e.Message}"); }
            return _auto;
        }
    }

    static string Slot() => Progression.GameQuery.CurrentSlot().ToString();

    static string Key(ChestStorage c)
    {
        var p = c.transform.position;
        return $"{Mathf.RoundToInt(p.x * 10)}_{Mathf.RoundToInt(p.y * 10)}_{Mathf.RoundToInt(p.z * 10)}";
    }

    public static bool IsAuto(ChestStorage c) => Auto.TryGetValue(Slot(), out var set) && set.Contains(Key(c));

    public static bool ToggleAuto(ChestStorage c)
    {
        if (!Auto.TryGetValue(Slot(), out var set)) Auto[Slot()] = set = new HashSet<string>();
        bool on = set.Add(Key(c)) || !set.Remove(Key(c));
        try { Directory.CreateDirectory(LootRules.Dir); File.WriteAllText(AutoPath, JsonSerializer.Serialize(Auto)); }
        catch (Exception e) { Plugin.L.LogWarning($"auto_deposit.json not saved: {e.Message}"); }
        return on;
    }

    public static void Deposit(ChestStorageUI ui, ChestStorage chest)
    {
        if (ui == null || chest?.inventory == null) return;
        var held = new HashSet<string>();
        foreach (var s in chest.inventory)
            if (s?.item != null && s.quantity > 0) held.Add(s.item.name + "#" + s.upgradeLevel);

        var inv = InventoryManager.Instance;
        var sources = new List<InventorySlot>();
        if (inv.inventorySlots != null) foreach (var s in inv.inventorySlots) sources.Add(s);
        if (InventorySettings.DepositQuickSlots.Value && inv.quickSlots != null) foreach (var s in inv.quickSlots) sources.Add(s);

        int moved = 0, left = 0;
        foreach (var s in sources)
        {
            if (s?.item == null || s.quantity <= 0 || s.isEquipped) continue;
            if (s.storedItems != null && s.storedItems.Count > 0) continue; // containers stay with the player
            if (!held.Contains(s.item.name + "#" + s.upgradeLevel)) continue;
            int before = s.quantity;
            ui.TryMoveToChest(s, -1);
            int after = s.item == null ? 0 : s.quantity;
            moved += before - after;
            left += after;
        }
        ui.RequestRefresh();
        inv.NotifyInventoryChanged();
        string msg = Progression.GameQuery.IsRussian()
            ? (left > 0 ? $"Сложено {moved}, не влезло {left}" : $"Сложено в сундук: {moved}")
            : (left > 0 ? $"Deposited {moved}, {left} didn't fit" : $"Deposited {moved}");
        if (moved > 0 || left > 0)
            PickupNotificationManager.Instance?.NotifyCustom(null, msg, new Color(0.8f, 0.9f, 1f), 1);
    }

    // Deposit key / Shift+key while a chest window is open.
    public static void Tick()
    {
        if (!InventorySettings.ChestDeposit.Value) return;
        var ui = ChestStorageUI.Instance;
        if (ui == null || !ui.IsChestModeActive || ui.CurrentChest == null) return;
        var kb = Keyboard.current;
        if (kb == null || !Enum.TryParse<Key>(InventorySettings.DepositKey.Value, true, out var key) || !kb[key].wasPressedThisFrame) return;
        try
        {
            if (kb.shiftKey.isPressed)
            {
                bool on = ToggleAuto(ui.CurrentChest);
                string msg = Progression.GameQuery.IsRussian() ? (on ? "Авто-склад: вкл" : "Авто-склад: выкл") : (on ? "Auto deposit: on" : "Auto deposit: off");
                PickupNotificationManager.Instance?.NotifyCustom(null, msg, new Color(0.8f, 0.9f, 1f), 1);
                if (on) Deposit(ui, ui.CurrentChest);
            }
            else Deposit(ui, ui.CurrentChest);
        }
        catch (Exception e) { Plugin.L.LogError($"Deposit failed: {e.Message}"); }
    }
}

[HarmonyPatch(typeof(ChestStorageUI), nameof(ChestStorageUI.OpenChestMode))]
internal static class ChestOpenPatch
{
    static void Postfix(ChestStorageUI __instance, ChestStorage chest)
    {
        if (chest == null) return;
        try
        {
            if (InventorySettings.StackChestOnOpen.Value) chest.ConsolidateChestStacks();
            if (InventorySettings.ChestDeposit.Value && ChestDeposit.IsAuto(chest)) ChestDeposit.Deposit(__instance, chest);
            else if (InventorySettings.StackChestOnOpen.Value) __instance.RequestRefresh();
        }
        catch (Exception e) { Plugin.L.LogError($"Chest open helpers failed: {e.Message}"); }
    }
}
