
namespace FellSellPlus.Progression;

// Tuning for the gold-bought base progression at Grandma's grave. Read at game start.
internal static class ProgressionSettings
{
    public static ConfigEntry<bool> Enabled;
    public static ConfigEntry<string> PrestigeTiers;
    public static ConfigEntry<int> LevelCapPerBoss;
    public static ConfigEntry<string> CostSchedule;
    public static ConfigEntry<int> CostStepAfter;

    public static ConfigEntry<float> HealthPerLevel;
    public static ConfigEntry<float> StaminaPerLevel;
    public static ConfigEntry<float> ManaPerLevel;
    public static ConfigEntry<float> HealthRegenPerLevel;
    public static ConfigEntry<float> StaminaRegenPerLevel;
    public static ConfigEntry<float> ManaRegenPerLevel;
    public static ConfigEntry<float> DamagePercentPerLevel;
    public static ConfigEntry<float> SpeedPercentPerLevel;
    public static ConfigEntry<float> SpeedPercentCap;
    public static ConfigEntry<float> CarryWeightPerLevel;
    public static ConfigEntry<float> AttackSpeedPercentPerLevel;
    public static ConfigEntry<float> AttackSpeedPercentCap;


    public static void Bind(ConfigFile cfg)
    {
        const string s = "Progression";
        Enabled = cfg.Bind(s, "Enabled", true, "Gold-bought base stat upgrades at Grandma's grave (look at the gravestone, press E).");
        PrestigeTiers = cfg.Bind(s, "PrestigeTiers", "0:5, 0.5:10, 1:15, 2:25, 3:35, 4:45, 5:50",
            "Shop prestige -> max level of each stat, as prestige:cap pairs. Below the first pair nothing can be bought.");
        LevelCapPerBoss = cfg.Bind(s, "LevelCapPerBoss", 5,
            new ConfigDescription("Extra max level per defeated boss (3 bosses + Grandma).", new AcceptableValueRange<int>(0, 1000)));
        CostSchedule = cfg.Bind(s, "CostSchedule", "50, 75, 100, 150, 300, 500, 750, 1000",
            "Gold cost of levels 1, 2, 3... in order. Levels past the list keep adding CostStepAfter.");
        CostStepAfter = cfg.Bind(s, "CostStepAfter", 500,
            new ConfigDescription("Added to the cost for every level past the CostSchedule list.", new AcceptableValueRange<int>(0, 1000000)));

        HealthPerLevel = cfg.Bind(s, "HealthPerLevel", 1f, "Max health per level.");
        StaminaPerLevel = cfg.Bind(s, "StaminaPerLevel", 1f, "Max stamina per level.");
        ManaPerLevel = cfg.Bind(s, "ManaPerLevel", 1f, "Max mana per level.");
        HealthRegenPerLevel = cfg.Bind(s, "HealthRegenPerLevel", 0.1f, "Health regeneration per second per level.");
        StaminaRegenPerLevel = cfg.Bind(s, "StaminaRegenPerLevel", 0.1f, "Stamina regeneration per second per level.");
        ManaRegenPerLevel = cfg.Bind(s, "ManaRegenPerLevel", 0.1f, "Mana regeneration per second per level.");
        DamagePercentPerLevel = cfg.Bind(s, "DamagePercentPerLevel", 1f, "Damage % of base per level, additive (10 levels = x1.10).");
        SpeedPercentPerLevel = cfg.Bind(s, "SpeedPercentPerLevel", 0.5f, "Movement speed % per level, additive.");
        SpeedPercentCap = cfg.Bind(s, "SpeedPercentCap", 25f,
            new ConfigDescription("Hard cap on the movement speed bonus, in %.", new AcceptableValueRange<float>(0f, 100f)));
        CarryWeightPerLevel = cfg.Bind(s, "CarryWeightPerLevel", 5f, "Max carry weight per level.");
        AttackSpeedPercentPerLevel = cfg.Bind(s, "AttackSpeedPercentPerLevel", 0.5f, "Attack speed % per level, additive.");
        AttackSpeedPercentCap = cfg.Bind(s, "AttackSpeedPercentCap", 25f,
            new ConfigDescription("Hard cap on the attack speed bonus, in %.", new AcceptableValueRange<float>(0f, 100f)));

    }
}
