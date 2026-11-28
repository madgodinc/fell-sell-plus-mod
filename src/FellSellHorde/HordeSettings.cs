using MelonLoader;

namespace FellSellHorde;

internal static class HordeSettings
{
    public static MelonPreferences_Entry<bool> Enabled;
    public static MelonPreferences_Entry<string> ArmKey;
    public static MelonPreferences_Entry<int> Seed;
    public static MelonPreferences_Entry<int> MaxAlive;
    public static MelonPreferences_Entry<int> WavesPerBlock;
    public static MelonPreferences_Entry<int> BaseEnemies;
    public static MelonPreferences_Entry<int> EnemiesPerWave;
    public static MelonPreferences_Entry<float> SpawnInterval;
    public static MelonPreferences_Entry<float> HealthPercentPerWave;
    public static MelonPreferences_Entry<float> IntermissionSeconds;
    public static MelonPreferences_Entry<float> MinSpawnDistance;
    public static MelonPreferences_Entry<string> StatuePosition;
    public static MelonPreferences_Entry<int> WavesPerNewEnemy;
    public static MelonPreferences_Entry<float> DamagePercentPerWave;
    public static MelonPreferences_Entry<float> HealthPercentPerLevel;
    public static MelonPreferences_Entry<float> DamagePercentPerLevel;
    public static MelonPreferences_Entry<float> EliteChanceBase;
    public static MelonPreferences_Entry<int> EliteFromWave;
    public static MelonPreferences_Entry<float> EliteChancePerWave;
    public static MelonPreferences_Entry<float> EliteChanceMax;
    public static MelonPreferences_Entry<int> BossEvery;
    public static MelonPreferences_Entry<int> GrandmaWave;
    public static MelonPreferences_Entry<bool> PauseOnChoice;

    public static void Bind()
    {
        var c = MelonPreferences.CreateCategory("FellSellHorde", "Fell & Sell Horde");
        Enabled = c.CreateEntry("Enabled", true, description: "Endless horde mode.");
        ArmKey = c.CreateEntry("ArmKey", "", description: "Developer shortcut in the village: arm/disarm a horde run without the statue. Empty = off.");
        Seed = c.CreateEntry("Seed", 1337, description: "Fixed dungeon seed for the horde arena.");
        MaxAlive = c.CreateEntry("MaxAlive", 50, description: "Most enemies alive at once; the rest of the wave waits until some die.");
        WavesPerBlock = c.CreateEntry("WavesPerBlock", 10, description: "Waves between breaks. The side room with statues and a chest opens after each block.");
        BaseEnemies = c.CreateEntry("BaseEnemies", 6, description: "Enemies in wave 1.");
        EnemiesPerWave = c.CreateEntry("EnemiesPerWave", 2, description: "Extra enemies per wave number.");
        SpawnInterval = c.CreateEntry("SpawnInterval", 0.15f, description: "Seconds between single enemy spawns: waves roll in, they do not appear at once.");
        HealthPercentPerWave = c.CreateEntry("HealthPercentPerWave", 6f, description: "Enemy health +% per wave, additive.");
        IntermissionSeconds = c.CreateEntry("IntermissionSeconds", 2f, description: "Pause between waves inside a block.");
        MinSpawnDistance = c.CreateEntry("MinSpawnDistance", 3f, description: "Enemies never appear closer than this to a player (metres).");
        DamagePercentPerWave = c.CreateEntry("DamagePercentPerWave", 3f, description: "Enemy damage to the player +% per wave, additive.");
        HealthPercentPerLevel = c.CreateEntry("HealthPercentPerLevel", 5f, description: "Enemy health +% per level the player has taken (gifts stack without limit, enemies keep up).");
        DamagePercentPerLevel = c.CreateEntry("DamagePercentPerLevel", 2f, description: "Enemy damage +% per level the player has taken.");
        EliteChanceBase = c.CreateEntry("EliteChanceBase", 0f, description: "Elite chance (%) at EliteFromWave.");
        EliteFromWave = c.CreateEntry("EliteFromWave", 11, description: "First wave that can have elites.");
        EliteChancePerWave = c.CreateEntry("EliteChancePerWave", 1f, description: "Elite chance +% per wave after EliteFromWave.");
        EliteChanceMax = c.CreateEntry("EliteChanceMax", 30f, description: "Elite chance cap (%). At most 1 + wave/5 elites alive at once, one frost aura at a time; from wave 20 some elites get a second affix.");
        BossEvery = c.CreateEntry("BossEvery", 5, description: "After the first boss on wave 10, bosses come every this many waves: biome by biome in dungeon order, then singles from everyone, then pairs and triples.");
        GrandmaWave = c.CreateEntry("GrandmaWave", 60, description: "Grandma comes alone on this wave; afterwards she is part of the boss pool.");
        WavesPerNewEnemy = c.CreateEntry("WavesPerNewEnemy", 1, description: "Every this many waves another enemy kind joins the horde (weakest first).");
        PauseOnChoice = c.CreateEntry("PauseOnChoice", true, description: "Freeze the game while a level-up choice (good/evil, then cards) is open.");
        StatuePosition = c.CreateEntry("StatuePosition", "", description: "Reaper statue spot in the village as \"x, y, z, yaw\". Empty = next to the shop gate.");
    }
}
