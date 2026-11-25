namespace FellSellPlus.Crafting;

internal static class CraftSettings
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<bool> Sort;
    public static ConfigEntry<bool> CraftableFirst;
    public static ConfigEntry<bool> SinkCapped;
    public static ConfigEntry<int> PlacedCap;
    public static ConfigEntry<bool> RowInfo;
    public static ConfigEntry<bool> FadeCapped;
    public static ConfigEntry<bool> FilterBar;
    public static ConfigEntry<bool> RememberFilters;
    public static ConfigEntry<bool> InstantList;
    public static ConfigEntry<string> NeedKey;

    public static void Bind(ConfigFile cfg)
    {
        Enabled = cfg.Bind("Crafting", "Enabled", true, "Master switch for the crafting bench improvements.");
        Sort = cfg.Bind("Crafting", "Sort", true, "Sort the recipe list instead of plain alphabetical.");
        CraftableFirst = cfg.Bind("Crafting", "CraftableFirst", true, "Recipes you can craft right now come first.");
        SinkCapped = cfg.Bind("Crafting", "SinkCappedFurniture", true, "Furniture placed PlacedCap or more times goes to the bottom.");
        PlacedCap = cfg.Bind("Crafting", "PlacedCap", 3,
            new ConfigDescription("Placed copies that count as done (each extra copy gives half the bonus of the previous).",
                new AcceptableValueRange<int>(1, 10)));
        RowInfo = cfg.Bind("Crafting", "RowInfo", true, "Show 'Placed X/3' and 'Stored Z' on recipe rows.");
        FadeCapped = cfg.Bind("Crafting", "FadeCappedRows", true, "Fade rows whose furniture reached the cap.");
        FilterBar = cfg.Bind("Crafting", "FilterBar", true, "Show the filter bar under the recipe list.");
        RememberFilters = cfg.Bind("Crafting", "RememberFilters", false, "Keep filters between visits to the bench.");
        InstantList = cfg.Bind("Crafting", "InstantList", true, "Show the whole recipe list at once instead of row by row.");
        NeedKey = cfg.Bind("Crafting", "NeedKey", "N", "Key at the bench that marks the selected recipe as Need.");
    }
}
