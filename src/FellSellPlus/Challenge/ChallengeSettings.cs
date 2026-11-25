using System;
using System.Collections.Generic;

namespace FellSellPlus.Challenge;

// One tunable of the challenge. Every value is a float in the cfg:
//   -1  = take it from the preset (the default for every key),
//   any other value = use exactly this (may be easier or harder than vanilla).
// Switches use 0 = off, 1 = on. Precedence: explicit cfg value > preset > vanilla.
// With the per-save lock on, the result is then never easier than what the save recorded.
internal sealed class Param
{
    public readonly string Section, Key, Text;
    public readonly float Vanilla, Min, Max;
    public readonly float[] Presets;   // Veteran, Nightmare, Blasphemous, Insane
    public readonly int Harder;        // +1: higher is harder, -1: lower is harder
    public ConfigEntry<float> Entry;

    public Param(string section, string key, string text, float vanilla, float min, float max, int harder, params float[] presets)
    {
        Section = section; Key = key; Text = text; Vanilla = vanilla; Min = min; Max = max; Harder = harder; Presets = presets;
    }

    public float FromConfig()
    {
        float v = Entry.Value;
        if (v != -1f) return Math.Clamp(v, Min, Max);
        int p = ChallengeSettings.PresetIndex();
        return p < 0 ? Vanilla : Presets[p];
    }

    public float Value => ChallengeLock.Apply(this, FromConfig());
    public bool On => Value >= 0.5f;
    public bool IsVanilla => Math.Abs(Value - Vanilla) < 0.0001f;
    public float HarderOf(float a, float b) => Harder > 0 ? Math.Max(a, b) : Math.Min(a, b);
}

internal static class ChallengeSettings
{
    public static readonly string[] PresetNames = { "Off", "Veteran", "Nightmare", "Blasphemous", "Insane" };
    public static ConfigEntry<string> Preset;
    public static ConfigEntry<bool> OnlyHarderLock;
    public static ConfigEntry<bool> ShowBadge;

    public static int PresetIndex()
    {
        int i = Array.FindIndex(PresetNames, n => n.Equals(Preset.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
        return i <= 0 ? -1 : i - 1; // Off / unknown -> vanilla baseline
    }

    const int H = 1, L = -1; // which direction is harder

    // Combat
    public static readonly Param EnemyHealth = new("Combat", "EnemyHealth", "Enemy max health multiplier.", 1, 0.1f, 20, H, 1.2f, 1.4f, 1.7f, 2f);
    public static readonly Param EnemyDamage = new("Combat", "EnemyDamage", "Damage you take from enemies and traps.", 1, 0.1f, 20, H, 1.15f, 1.35f, 1.6f, 2f);
    public static readonly Param PlayerDamage = new("Combat", "PlayerDamage", "Damage you deal.", 1, 0.1f, 20, L, 1f, 0.95f, 0.9f, 0.8f);
    public static readonly Param EnemySpeed = new("Combat", "EnemySpeed", "Enemy movement speed.", 1, 0.25f, 4, H, 1f, 1.05f, 1.1f, 1.2f);
    public static readonly Param EnemyAttackRate = new("Combat", "EnemyAttackRate", "Enemy attack frequency (2 = attacks twice as often).", 1, 0.2f, 5, H, 1.1f, 1.2f, 1.3f, 1.4f);
    public static readonly Param EnemiesLungeAndCombo = new("Combat", "EnemiesLungeAndCombo", "Every enemy may lunge and combo regardless of difficulty (0/1).", 0, 0, 1, H, 1, 1, 1, 1);
    public static readonly Param LoseGearOnDeath = new("Combat", "LoseGearOnDeath", "Lose equipped gear on death on any difficulty (0/1).", 0, 0, 1, H, 0, 1, 1, 1);
    public static readonly Param BossHealth = new("Combat", "BossHealth", "Extra boss health multiplier (on top of EnemyHealth).", 1, 0.1f, 20, H, 1f, 1.15f, 1.3f, 1.5f);
    public static readonly Param BossDamage = new("Combat", "BossDamage", "Extra boss damage multiplier (on top of EnemyDamage).", 1, 0.1f, 20, H, 1f, 1.15f, 1.3f, 1.5f);
    public static readonly Param ExtraEliteChance = new("Combat", "ExtraEliteChance", "Additional chance (0-1) for a normal enemy to become elite.", 0, 0, 1, H, 0.05f, 0.1f, 0.2f, 0.3f);
    public static readonly Param EliteSecondAffix = new("Combat", "EliteSecondAffixChance", "Chance (0-1) that a mod-made elite gets a second affix.", 0, 0, 1, H, 0f, 0.2f, 0.4f, 0.6f);
    public static readonly Param ElitePlusChance = new("Combat", "ElitePlusChance", "Chance (0-1) that a mod-made elite is Elite+.", 0, 0, 1, H, 0f, 0.05f, 0.1f, 0.15f);
    public static readonly Param ElitesOutsideDungeonPlus = new("Combat", "ElitesOutsideDungeonPlus", "Extra elites also in normal runs and events (0/1).", 0, 0, 1, H, 0, 0, 1, 1);
    public static readonly Param ArmorEffect = new("Combat", "ArmorEffect", "How much your armor counts (0.7 = armor works 30% worse).", 1, 0.1f, 3, L, 1f, 0.9f, 0.8f, 0.7f);
    public static readonly Param StaminaDrain = new("Combat", "StaminaDrain", "Stamina cost of sprint, jump, dodge, attack and block.", 1, 0.1f, 5, H, 1f, 1.1f, 1.2f, 1.3f);
    public static readonly Param FurnitureCombatBonus = new("Combat", "FurnitureCombatBonus", "Multiplier on furniture dungeon bonuses.", 1, 0, 5, L, 0.9f, 0.8f, 0.65f, 0.5f);

    // Dungeon
    public static readonly Param EnemyCount = new("Dungeon", "EnemyCount", "Enemies per dungeon floor.", 1, 0.1f, 5, H, 1.15f, 1.3f, 1.5f, 2f);
    public static readonly Param DeeperEnemies = new("Dungeon", "DeeperEnemyFloors", "Tougher enemy types appear this many floors earlier.", 0, 0, 20, H, 0, 1, 2, 4);
    public static readonly Param TrapChance = new("Dungeon", "TrapChance", "Corridor trap chance multiplier.", 1, 0, 10, H, 1.5f, 2f, 2.5f, 3f);
    public static readonly Param EmptyChest = new("Dungeon", "EmptyChestChance", "Weight of empty rolls in dungeon chests.", 1, 0, 10, H, 1f, 1.25f, 1.5f, 2f);
    public static readonly Param RareLoot = new("Dungeon", "RareLoot", "Weight of uncommon-and-better items and recipe chance in dungeon chests.", 1, 0, 20, L, 1f, 0.9f, 0.8f, 0.7f);
    public static readonly Param PrayerLimit = new("Dungeon", "PrayerLimit", "Most prayers taken at statues per run, any run (99 = no limit, 0 = statues unusable).", 99, 0, 99, L, 99, 99, 4, 2);
    public static readonly Param EvilPrayersOnly = new("Dungeon", "EvilPrayersOnly", "Good statues cannot be used (0/1).", 0, 0, 1, H, 0, 0, 0, 1);
    public static readonly Param ForcedEvilPrayers = new("Dungeon", "ForcedEvilPrayers", "Per run, this many floors force one random evil prayer on arrival.", 0, 0, 30, H, 0, 0, 2, 5);

    // Forest
    public static readonly Param ForestEnemyCount = new("Forest", "EnemyCount", "Enemies per forest spawn point.", 1, 0.1f, 5, H, 1.15f, 1.3f, 1.5f, 2f);
    public static readonly Param ForestToughEnemies = new("Forest", "ToughEnemyChance", "Chance multiplier of the tougher forest enemy tiers.", 1, 0, 10, H, 1.25f, 1.5f, 1.75f, 2f);
    public static readonly Param ForestChests = new("Forest", "RandomChests", "Random forest chest chance multiplier.", 1, 0, 10, L, 1f, 0.85f, 0.7f, 0.5f);
    public static readonly Param TreasureGoblins = new("Forest", "TreasureGoblins", "Treasure goblin spawn chance multiplier.", 1, 0, 10, L, 1f, 0.85f, 0.7f, 0.5f);
    public static readonly Param TripDamage = new("Forest", "TripDamage", "Damage from hallucination (trip) events.", 1, 0, 10, H, 1f, 1.25f, 1.5f, 2f);

    // XP
    public static readonly Param XpAll = new("XP", "All", "Multiplier on every XP gain.", 1, 0, 10, L, 0.85f, 0.75f, 0.6f, 0.5f);
    public static readonly Param XpSales = new("XP", "Sales", "XP from collecting sales at the counter.", 1, 0, 10, L, 1, 1, 1, 1);
    public static readonly Param XpPerfectPrice = new("XP", "PerfectPrice", "XP from perfect-price bonuses (reason text contains 'perfect').", 1, 0, 10, L, 1, 1, 1, 1);
    public static readonly Param XpCombat = new("XP", "Combat", "XP from kills.", 1, 0, 10, L, 1, 1, 1, 1);
    public static readonly Param XpCrafting = new("XP", "Crafting", "XP from crafting.", 1, 0, 10, L, 1, 1, 1, 1);
    public static readonly Param XpLoot = new("XP", "Loot", "XP from chests and breakables.", 1, 0, 10, L, 1, 1, 1, 1);
    public static readonly Param XpOther = new("XP", "Other", "XP from everything else (letters, invites...).", 1, 0, 10, L, 1, 1, 1, 1);
    public static readonly Param XpToLevel = new("XP", "XpToLevel", "XP needed per level.", 1, 0.1f, 10, H, 1f, 1.15f, 1.3f, 1.5f);

    // Shop
    public static readonly Param SalePrices = new("Shop", "SalePrices", "What customers are willing to pay (their hidden ideal price).", 1, 0.1f, 5, L, 0.9f, 0.85f, 0.75f, 0.65f);
    public static readonly Param ShoppingChance = new("Shop", "ShoppingChance", "Chance a customer shops.", 1, 0, 5, L, 1f, 0.9f, 0.8f, 0.7f);
    public static readonly Param FurniturePriceBonus = new("Shop", "FurniturePriceBonus", "Multiplier on the sale-price bonus your furniture gives.", 1, 0, 10, L, 1f, 0.85f, 0.7f, 0.5f);
    public static readonly Param Tips = new("Shop", "Tips", "Tips from furniture.", 1, 0, 10, L, 1f, 0.75f, 0.5f, 0f);
    public static readonly Param MaxPerFurniture = new("Shop", "MaxBonusCopiesPerFurniture", "Only the first N copies of a furniture item give bonuses (99 = all).", 99, 1, 99, L, 99, 3, 2, 1);
    public static readonly Param VendorPrices = new("Shop", "VendorPrices", "Prices at town vendors.", 1, 0.1f, 10, H, 1.25f, 1.5f, 1.75f, 2f);
    public static readonly Param NoVendorBuying = new("Shop", "NoVendorBuying", "Town vendors refuse to sell (0/1).", 0, 0, 1, H, 0, 0, 0, 1);

    // Loot extras (not part of presets; vanilla unless set).
    public static readonly Param UncommonWeight = new("Loot", "UncommonWeight", "Extra weight of uncommon items in dungeon chests.", 1, 0, 20, L, 1, 1, 1, 1);
    public static readonly Param RareWeight = new("Loot", "RareWeight", "Extra weight of rare items in dungeon chests.", 1, 0, 20, L, 1, 1, 1, 1);
    public static readonly Param EpicWeight = new("Loot", "EpicWeight", "Extra weight of epic items in dungeon chests.", 1, 0, 20, L, 1, 1, 1, 1);
    public static readonly Param LegendaryWeight = new("Loot", "LegendaryWeight", "Extra weight of legendary items in dungeon chests.", 1, 0, 20, L, 1, 1, 1, 1);
    public static readonly Param ItemAmount = new("Loot", "ItemAmount", "Stack size of items rolled in dungeon chests.", 1, 0.1f, 10, L, 1, 1, 1, 1);
    public static readonly Param RecipeChance = new("Loot", "RecipeChance", "Extra recipe chance multiplier in dungeon chests.", 1, 0, 10, L, 1, 1, 1, 1);
    public static readonly Param ExtraDropChance = new("Loot", "ExtraDropChance", "Added to the drop-chance bonus (game units).", 0, 0, 500, L, 0, 0, 0, 0);

    public static readonly List<Param> All = new();

    public static void Bind(ConfigFile cfg)
    {
        Preset = cfg.Bind("Challenge", "Preset", "Off",
            "Baseline for every Challenge value: Off, Veteran, Nightmare, Blasphemous, Insane. " +
            "Each key below is -1 by default (= take the preset); set a number to override it, easier or harder.");
        OnlyHarderLock = cfg.Bind("Challenge", "OnlyHarderLock", false,
            "Per-save lock: the first load records the values; later the save never gets easier than that. " +
            "Reset by deleting UserData/FellSellPlus/challenge_slot<N>.json.");
        ShowBadge = cfg.Bind("Challenge", "ShowBadge", true, "Show the active challenge in the bottom-left corner.");

        foreach (var f in typeof(ChallengeSettings).GetFields())
            if (f.GetValue(null) is Param p) All.Add(p);
        foreach (var p in All)
            p.Entry = cfg.Bind("Challenge_" + p.Section, p.Key, -1f,
                new ConfigDescription($"{p.Text} Vanilla {p.Vanilla}. -1 = preset.", new AcceptableValueRange<float>(-1f, p.Max)));
        LegacyMigration.Run();
    }

    // Short label for the badge: preset name plus how many values differ from it.
    public static string Describe()
    {
        int p = PresetIndex();
        int custom = 0;
        foreach (var x in All) if (x.Entry.Value != -1f) custom++;
        string name = p < 0 ? (custom > 0 ? "Custom" : "Off") : PresetNames[p + 1];
        if (name == "Off") return null;
        return custom > 0 && p >= 0 ? $"{name} +{custom}" : name;
    }
}
