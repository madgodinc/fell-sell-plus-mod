namespace FellSellPlus.Info;

// [Info] section: read-only helper screens. All toggles default on except spoilers.
internal static class InfoSettings
{
    public static ConfigEntry<bool> StatsPanel;
    public static ConfigEntry<bool> SkillBookOdds;
    public static ConfigEntry<bool> PrayerHints;
    public static ConfigEntry<bool> Runs;
    public static ConfigEntry<bool> RunSummary;
    public static ConfigEntry<string> RunsKey;
    public static ConfigEntry<bool> ShowIdealValues;
    public static ConfigEntry<bool> SkillTileHints;
    public static ConfigEntry<bool> RevealSkillTiles;
    public static ConfigEntry<bool> LootOdds;
    public static ConfigEntry<bool> EnemyHealthBars;
    public static ConfigEntry<int> EnemyHealthBarRange;
    public static ConfigEntry<bool> EnemyHealthBarsOnlyDamaged;

    public static void Bind(ConfigFile cfg)
    {
        StatsPanel = cfg.Bind("Info", "StatsPanel", true,
            "Extra panel next to the inventory (I): gear bonus totals and effective stats.");
        SkillBookOdds = cfg.Bind("Info", "SkillBookOdds", true,
            "Looking at a skill book lists every skill it can still roll, with exact chances.");
        PrayerHints = cfg.Bind("Info", "PrayerHints", true,
            "Under each prayer card: what it changes for you now (before -> after), costs in red, a star if it is the only useful card.");
        Runs = cfg.Bind("Info", "RunHistory", true,
            "Record every dungeon and forest run (UserData/FellSellPlus/runs).");
        RunSummary = cfg.Bind("Info", "RunSummary", true,
            "Show a summary screen when you come back to town.");
        RunsKey = cfg.Bind("Info", "RunsWindowKey", "J",
            "Key that opens the runs window (Unity Input System key name, e.g. J, F7).");
        ShowIdealValues = cfg.Bind("Info", "LootValues", true,
            "Show loot value in run records, from the item's hidden ideal price (marked with ~).");
        SkillTileHints = cfg.Bind("Info", "SkillTileHints", true,
            "Skills page: hovering an undiscovered ? tile lists which skills it could be, with book chances.");
        RevealSkillTiles = cfg.Bind("Info", "RevealSkillTiles", false,
            "Spoiler: mark the exact skill behind each ? tile.");
        LootOdds = cfg.Bind("Info", "LootOdds", true,
            "Runs window tab 6: dungeon chest odds per biome and floor, measured with the game's own roll (UserData/FellSellPlus/loot_tables.json).");
        EnemyHealthBars = cfg.Bind("Info", "EnemyHealthBars", true,
            "Health bar over every enemy's head (hidden behind walls).");
        EnemyHealthBarRange = cfg.Bind("Info", "EnemyHealthBarRange", 15,
            new ConfigDescription("Farthest distance, in meters, at which enemy health bars show.", new AcceptableValueRange<int>(5, 80)));
        EnemyHealthBarsOnlyDamaged = cfg.Bind("Info", "EnemyHealthBarsOnlyDamaged", false,
            "Show an enemy's bar only after it has taken damage.");
    }
}
