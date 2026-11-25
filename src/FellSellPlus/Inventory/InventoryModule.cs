using System;

namespace FellSellPlus.Inventory;

// Entry points called from Plugin: Bind at init, Tick every frame.
internal static class InventoryModule
{
    static string _lastScene = "";
    static bool _tickFailedLogged;

    public static void Bind(ConfigFile cfg) => InventorySettings.Bind(cfg);

    public static void Tick()
    {
        try
        {
            // Safety net: these context flags are set/cleared around single game calls.
            LootFilter.AutoContext = false;
            QuickSlotRouting.Crafting = false;
            var scene = ItemCounts.Scene();
            if (scene != _lastScene)
            {
                // A run starts when leaving town for the dungeon or forest.
                if (_lastScene == ItemCounts.Town && (scene == ItemCounts.Dungeon || scene == ItemCounts.Forest))
                    LootFilter.ResetRun();
                QuickSlotRouting.PendingPickups.Clear();
                LootFilter.PendingLoose.Clear();
                _lastScene = scene;
            }
            if (InventorySettings.FilterEnabled.Value && !Plugin.Skip("LootRules")) LootRules.Tick();
            // Touching game singletons (e.g. ChestStorageUI.Instance) on the splash screen crashes the
            // game natively, so everything below runs only in gameplay scenes.
            bool gameplay = scene == ItemCounts.Town || scene == ItemCounts.Dungeon || scene == ItemCounts.Forest;
            if (!gameplay) return;
            if (!Plugin.Skip("ItemCounts")) ItemCounts.Tick();
            if (scene == ItemCounts.Town || scene == ItemCounts.Dungeon || scene == ItemCounts.Forest)
                LootRules.WriteCatalogue();
            if (!Plugin.Skip("WeightBar")) WeightBar.Tick();
            if (!Plugin.Skip("ChestDeposit")) ChestDeposit.Tick();
            PurgeTick.Tick(scene);
        }
        catch (Exception e)
        {
            if (!_tickFailedLogged) Plugin.L.LogError($"Inventory tick failed: {e}");
            _tickFailedLogged = true;
        }
    }
}
