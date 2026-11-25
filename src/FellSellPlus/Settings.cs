
namespace FellSellPlus;

// Every value is read once at game start. The config file is edited outside the game
// (UserData/MelonPreferences.cfg); defaults reproduce vanilla behaviour.
// Enemy, dungeon, loot, XP and shop tuning lives in Challenge/ChallengeSettings.cs.
internal static class Settings
{
    // Prayers
    public static ConfigEntry<int> CardsPerStatue;
    public static ConfigEntry<int> MaxActivePrayers;

    public static void Bind(ConfigFile cfg)
    {
        CardsPerStatue = cfg.Bind("Prayers", "CardsPerStatue", 3,
            new ConfigDescription("How many prayers a statue offers (vanilla 3). More than 5 may not fit on screen.",
                new AcceptableValueRange<int>(1, 8)));
        MaxActivePrayers = cfg.Bind("Prayers", "MaxActivePrayers", 5,
            new ConfigDescription("Dungeon+ only: how many prayers you can hold before the game asks you to replace one (vanilla 5). Normal runs have no cap in the game.",
                new AcceptableValueRange<int>(1, 20)));
    }
}
