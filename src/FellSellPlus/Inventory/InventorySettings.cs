namespace FellSellPlus.Inventory;

// Config for the loot filter and inventory helpers (UserData/MelonPreferences.cfg).
// Item rules themselves live in UserData/FellSellPlus/loot_rules.json.
internal static class InventorySettings
{
    // Loot filter
    public static ConfigEntry<bool> FilterEnabled;
    public static ConfigEntry<bool> Alerts;
    public static ConfigEntry<bool> WriteCatalogue;

    // Inventory
    public static ConfigEntry<bool> QuickSlotRouting;
    public static ConfigEntry<bool> PlaceablesToEmptyQuickSlots;
    public static ConfigEntry<bool> ConsumablesToEmptyQuickSlots;
    public static ConfigEntry<float> PickupRadiusMultiplier;
    public static ConfigEntry<int> PickupScanLimit;
    public static ConfigEntry<bool> WeightBar;
    public static ConfigEntry<float> WeightBarWidth;
    public static ConfigEntry<float> WeightBarX;
    public static ConfigEntry<float> WeightBarY;
    public static ConfigEntry<bool> ChestDeposit;
    public static ConfigEntry<string> DepositKey;
    public static ConfigEntry<bool> StackChestOnOpen;
    public static ConfigEntry<bool> DepositQuickSlots;
    public static ConfigEntry<bool> TooltipInfo;
    public static ConfigEntry<bool> ShowIdealPrice;
    public static ConfigEntry<bool> KeepMarks;
    public static ConfigEntry<string> KeepKey;
    public static ConfigEntry<bool> PurgeInfo;
    public static ConfigEntry<bool> PurgeSort;
    public static ConfigEntry<bool> RememberSort;
    public static ConfigEntry<bool> GroundItemInfo;
    public static ConfigEntry<bool> FixDungeonPrices;

    public static void Bind(ConfigFile cfg)
    {
        FilterEnabled = cfg.Bind("LootFilter", "Enabled", true,
            "Per-item pickup rules from UserData/FellSellPlus/loot_rules.json (hot-reloaded while the game runs).");
        Alerts = cfg.Bind("LootFilter", "Alerts", true,
            "Show a line in the pickup feed when a Total / ThisRun limit is reached or a Need is covered.");
        WriteCatalogue = cfg.Bind("LootFilter", "WriteCatalogue", true,
            "Write UserData/FellSellPlus/items_catalogue.txt (every item id with English/Russian names) when a save loads.");

        QuickSlotRouting = cfg.Bind("Inventory", "QuickSlotRouting", true,
            "Picked-up and crafted items go to the backpack instead of empty quick slots. Existing quick slot stacks still top up.");
        PlaceablesToEmptyQuickSlots = cfg.Bind("Inventory", "PlaceablesToEmptyQuickSlots", true,
            "With routing on, placeable furniture may still take an empty quick slot.");
        ConsumablesToEmptyQuickSlots = cfg.Bind("Inventory", "ConsumablesToEmptyQuickSlots", false,
            "With routing on, potions and food may still take an empty quick slot.");
        PickupRadiusMultiplier = cfg.Bind("Inventory", "PickupRadiusMultiplier", 2f,
            new ConfigDescription("Auto-pickup radius multiplier (1 = game default).", new AcceptableValueRange<float>(0.5f, 5f)));
        PickupScanLimit = cfg.Bind("Inventory", "PickupScanLimit", 512,
            new ConfigDescription("Colliders the auto-pickup scan looks at (game: 32, which skips items in cluttered rooms).",
                new AcceptableValueRange<int>(32, 2048)));
        WeightBar = cfg.Bind("Inventory", "WeightBar", true, "Carry weight bar at the lower left (orange at 75%, red at 95%).");
        WeightBarWidth = cfg.Bind("Inventory", "WeightBarWidth", 420f, "Weight bar length in pixels (1920x1080 reference).");
        WeightBarX = cfg.Bind("Inventory", "WeightBarX", 40f, "Weight bar distance from the left edge.");
        WeightBarY = cfg.Bind("Inventory", "WeightBarY", 40f, "Weight bar distance from the bottom edge.");
        ChestDeposit = cfg.Bind("Inventory", "ChestDeposit", true,
            "Deposit button / key in chests: moves everything the chest already holds from your bag into it. Shift+key toggles Auto for that chest.");
        DepositKey = cfg.Bind("Inventory", "DepositKey", "G", "Key for Deposit while a chest is open (UnityEngine.KeyCode name).");
        StackChestOnOpen = cfg.Bind("Inventory", "StackChestOnOpen", true, "Run the game's Stack on every chest when it opens.");
        DepositQuickSlots = cfg.Bind("Inventory", "DepositQuickSlots", false, "Deposit also takes matching items from quick slots.");
        TooltipInfo = cfg.Bind("Inventory", "TooltipInfo", true,
            "Extra item tooltip lines: how many you have and where, carried weight, Needed recipes, loot rule.");
        ShowIdealPrice = cfg.Bind("Inventory", "ShowIdealPrice", false, "Tooltip shows the item's hidden ideal price (spoiler).");
        KeepMarks = cfg.Bind("Inventory", "KeepMarks", true, "Mark items as Keep by pressing KeepKey while hovering them (UserData/FellSellPlus/keep.json). Kept items rank last for Purge.");
        KeepKey = cfg.Bind("Inventory", "KeepKey", "K", "Key for Keep (UnityEngine.InputSystem Key name).");
        PurgeInfo = cfg.Bind("Inventory", "PurgeInfo", true, "Tooltip shows value (best price a customer accepted), gold per kg and the Purge rank (1 = get rid of first).");
        PurgeSort = cfg.Bind("Inventory", "PurgeSort", true, "Hold Shift while clicking a sort button to sort by Purge order (lowest gold/kg first, then unknown, Needed, Keep).");
        RememberSort = cfg.Bind("Inventory", "RememberSort", true, "The inventory and chest sort criterion/direction you last picked are kept (UserData/FellSellPlus/sort.json).");
        GroundItemInfo = cfg.Bind("Inventory", "GroundItemInfo", true, "Looking at a loose item shows its gold/kg and Purge rank, or what to drop to make room for it.");
        FixDungeonPrices = cfg.Bind("Inventory", "FixDungeonPrices", true, "Load known prices when a save is loaded inside the dungeon/forest (the game skips this until town).");
    }
}
