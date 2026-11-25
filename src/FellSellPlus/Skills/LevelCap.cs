using HarmonyLib;

namespace FellSellPlus.Skills;

// Character level and skill points. The game caps the character at level 30
// (PlayerProgressionManager._maxLevel = 30 in its constructor) with 1 skill point per level; all
// skills together need 232 points (activation costs, logged on the first load). Default rule: cap 120, levels 31-120 give 2 points and every
// 5th of them two more, so everything can be learned at 120 (29 + 180 + 36 = 245 points).
// XP per level keeps following the game's own curve, so the late levels are very slow.
internal static class LevelCap
{
    public static ConfigEntry<int> MaxPlayerLevel;
    public static ConfigEntry<int> PointsAbove30;
    public static ConfigEntry<int> BonusEveryFifth;

    public static void Bind(ConfigFile cfg)
    {
        MaxPlayerLevel = cfg.Bind("Skills", "MaxPlayerLevel", 120,
            new ConfigDescription("Highest character level (the game's own cap is 30).", new AcceptableValueRange<int>(30, 300)));
        PointsAbove30 = cfg.Bind("Skills", "SkillPointsPerLevelAbove30", 2,
            new ConfigDescription("Skill points for each level above 30 (the game gives 1 per level).", new AcceptableValueRange<int>(1, 10)));
        BonusEveryFifth = cfg.Bind("Skills", "BonusPointEveryFifthLevel", 2,
            new ConfigDescription("Extra skill points on every 5th level above 30 (35, 40, ...).", new AcceptableValueRange<int>(0, 10)));
    }

    // Total skill points earned by reaching `level` (level 1 = 0 points).
    public static int PointsFor(int level)
    {
        if (level <= 30) return level - 1;
        int p = 29;
        for (int l = 31; l <= level; l++) p += PointsAbove30.Value + (l % 5 == 0 ? BonusEveryFifth.Value : 0);
        return p;
    }

    // Lowest level whose earned points cover `points`.
    static int LevelFor(int points, int max)
    {
        for (int l = 1; l <= max; l++) if (PointsFor(l) >= points) return l;
        return max;
    }

    public static void Apply(PlayerProgressionManager m)
    {
        if (m == null || MaxPlayerLevel == null) return;
        if (m._maxLevel != MaxPlayerLevel.Value) m._maxLevel = MaxPlayerLevel.Value;
    }

    // Replacement for the game's ValidateLevelAgainstSkillPoints, which assumes 1 point per level and
    // would raise the level to (points + 1) as soon as the extra points exist.
    public static void Validate(PlayerProgressionManager m)
    {
        Apply(m);
        int spent = PlayerSkillManager.Instance != null ? PlayerSkillManager.Instance.GetTotalSpentSkillPoints() : 0;
        int have = System.Math.Max(m._totalEarnedSkillPoints, spent + m._availableSkillPoints);
        // Same intent as the game's check: a save with more points than its level explains gets the level.
        int needLevel = LevelFor(have, m._maxLevel);
        if (m._currentLevel < needLevel) m._currentLevel = needLevel;
        // Points the current level should have earned (e.g. extra points of levels gained before this version).
        int earned = PointsFor(m._currentLevel);
        if (have < earned) m._availableSkillPoints += earned - have;
        m._totalEarnedSkillPoints = System.Math.Max(earned, have);
    }
}

[HarmonyPatch(typeof(PlayerProgressionManager), "Awake")]
internal static class LevelCapAwakePatch { static void Postfix(PlayerProgressionManager __instance) => LevelCap.Apply(__instance); }

[HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.RestoreData))]
internal static class LevelCapRestorePatch
{
    static bool _logged;
    static void Postfix(PlayerProgressionManager __instance)
    {
        LevelCap.Validate(__instance);
        if (_logged) return;
        // Check that the level cap still pays for every skill. Measured in game: activating a skill costs
        // its rarity (1..5) once; ranks above 1 cost nothing extra.
        try
        {
            var sm = PlayerSkillManager.Instance;
            if (sm == null) return;
            int need = 0, n = 0;
            foreach (var sk in sm.GetAllSkills()) { need += (int)sk.rarity + 1; n++; }
            int have = LevelCap.PointsFor(__instance._maxLevel);
            _logged = true;
            Plugin.L.LogInfo($"Skill points: {n} skills need {need}, level {__instance._maxLevel} gives {have}{(have < need ? " (NOT ENOUGH)" : "")}.");
        }
        catch { }
    }
}

// Called from Start and from the skill manager's restore (0x18046E2D0).
[HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.ValidateLevelAgainstSkillPoints))]
internal static class LevelValidatePatch
{
    static bool Prefix(PlayerProgressionManager __instance)
    {
        try { LevelCap.Validate(__instance); __instance.NotifyAllState(); }
        catch (System.Exception e) { Plugin.L.LogWarning($"Level validation: {e.Message}"); return true; }
        return false;
    }
}

// The game adds 1 point per level-up inside AddXP; levels above 30 get the rest here.
[HarmonyPatch(typeof(PlayerProgressionManager), nameof(PlayerProgressionManager.AddXP))]
internal static class ExtraSkillPointsPatch
{
    static void Prefix(PlayerProgressionManager __instance, out int __state) => __state = __instance._currentLevel;

    static void Postfix(PlayerProgressionManager __instance, int __state)
    {
        for (int l = __state + 1; l <= __instance._currentLevel; l++)
        {
            if (l <= 30) continue;
            int extra = LevelCap.PointsFor(l) - LevelCap.PointsFor(l - 1) - 1; // the game already gave 1
            if (extra <= 0) continue;
            __instance._availableSkillPoints += extra;
            __instance._totalEarnedSkillPoints += extra;
        }
        if (__instance._currentLevel > __state) __instance.NotifyAllState();
    }
}
