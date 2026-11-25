using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine.Localization.Settings;

namespace FellSellPlus.Skills;

// New skills are registered through the game's own PlayerSkillManager.AddRuntimeSkill,
// so skill books (TryUnlockSkillFromBook) roll them like vanilla ones.
internal sealed class ModSkill
{
    public string Id, IconFrom, NameEn, DescEn, NameRu, DescRu;
    public SkillCategory Category;
    public SkillRarity Rarity;
    public int MaxRank;
}

internal static class SkillRegistry
{
    public const string FighterWhirlwind = "fsp_fighter_whirlwind";
    public const string FighterExecution = "fsp_fighter_execution";
    public const string FighterLowGravity = "fsp_fighter_low_gravity";
    public const string FighterFreeStride = "fsp_fighter_free_stride";
    public const string KnightSteadfast = "fsp_knight_steadfast_aura";
    public const string KnightRetribution = "fsp_knight_retribution";
    public const string MageChainLightning = "fsp_mage_chain_lightning";
    public const string MageFrostArmor = "fsp_mage_frost_armor";
    public const string OccultBloodHarvest = "fsp_occult_blood_harvest";
    public const string OccultPainPact = "fsp_occult_pain_pact";

    // Second wave: three legendaries per class and more skills at every rarity.
    public const string FighterMomentum = "fsp_fighter_momentum";
    public const string FighterBloodthirst = "fsp_fighter_bloodthirst";
    public const string FighterUnstoppable = "fsp_fighter_unstoppable";
    public const string FighterSteelStorm = "fsp_fighter_steel_storm";
    public const string KnightSturdy = "fsp_knight_sturdy";
    public const string KnightShieldbearer = "fsp_knight_shieldbearer";
    public const string KnightCounterstrike = "fsp_knight_counterstrike";
    public const string KnightBulwark = "fsp_knight_bulwark";
    public const string MageFocus = "fsp_mage_focus";
    public const string MageManaFont = "fsp_mage_mana_font";
    public const string MageManaShield = "fsp_mage_mana_shield";
    public const string MageOverload = "fsp_mage_overload";
    public const string MageTimeLoop = "fsp_mage_time_loop";
    public const string OccultBloodletter = "fsp_occult_bloodletter";
    public const string OccultSoulSiphon = "fsp_occult_soul_siphon";
    public const string OccultRot = "fsp_occult_rot";
    public const string OccultLordOfDeath = "fsp_occult_lord_of_death";
    public const string OccultHarvestRite = "fsp_occult_harvest_rite";
    public const string OccultDevourer = "fsp_occult_devourer";
    public const string FighterWrecker = "fsp_fighter_wrecker";
    public const string FighterLightHand = "fsp_fighter_light_hand";

    public static readonly List<ModSkill> All = new()
    {
        new() { Id = FighterWhirlwind, Category = SkillCategory.Fighter, Rarity = SkillRarity.Epic, MaxRank = 1, IconFrom = "fighter_blade_vortex",
            NameEn = "Whirlwind", DescEn = "After a dash, your next melee hit within 2s also strikes every enemy around you.",
            NameRu = "Вихрь", DescRu = "После рывка следующий удар ближнего боя (в течение 2 с) задевает всех врагов вокруг." },
        new() { Id = FighterExecution, Category = SkillCategory.Fighter, Rarity = SkillRarity.Rare, MaxRank = 3, IconFrom = "fighter_knockback",
            NameEn = "Execution", DescEn = "Melee hits deal +30%/+50%/+75% damage to enemies below 25% health.",
            NameRu = "Добивание", DescRu = "Удары ближнего боя наносят +30%/+50%/+75% урона врагам с здоровьем ниже 25%." },
        new() { Id = FighterLowGravity, Category = SkillCategory.Fighter, Rarity = SkillRarity.Legendary, MaxRank = 3, IconFrom = "fighter_acrobat",
            NameEn = "Low Gravity", DescEn = "Jumps are higher (about x1.7/x2.25/x3 height). Dash goes x1.5/x1.75/x2 as far, half again more in the air. Hold Jump while falling to glide: you fall 3 times slower. You take 30%/40%/50% less damage while in the air.",
            NameRu = "Слабая гравитация", DescRu = "Прыжки выше (примерно в 1,7/2,25/3 раза). Рывок длиннее в 1,5/1,75/2 раза, в воздухе ещё на половину больше. Удерживайте прыжок при падении, чтобы планировать: падение в 3 раза медленнее. В воздухе вы получаете на 30%/40%/50% меньше урона." },
        new() { Id = FighterFreeStride, Category = SkillCategory.Fighter, Rarity = SkillRarity.Epic, MaxRank = 3, IconFrom = "fighter_perfect_dodge",
            NameEn = "Free Stride", DescEn = "While you are out of combat (sprint costs no stamina then), in town too, you move +30%/60%/100% faster.",
            NameRu = "Вольный шаг", DescRu = "Пока вы не в бою (тогда бег не тратит выносливость), и в деревне тоже, вы двигаетесь на 30%/60%/100% быстрее." },
        new() { Id = KnightSteadfast, Category = SkillCategory.Knight, Rarity = SkillRarity.Rare, MaxRank = 3, IconFrom = "knight_perfect_block",
            NameEn = "Steadfast Aura", DescEn = "While your shield is raised, you take 10%/15%/20% less damage.",
            NameRu = "Аура стойкости", DescRu = "Пока щит поднят, получаемый урон снижен на 10%/15%/20%." },
        new() { Id = KnightRetribution, Category = SkillCategory.Knight, Rarity = SkillRarity.Legendary, MaxRank = 1, IconFrom = "knight_smite",
            NameEn = "Retribution", DescEn = "Every 10th blocked attack calls down Smite lightning on the attacker.",
            NameRu = "Возмездие", DescRu = "Каждый 10-й заблокированный удар обрушивает молнию Кары на атакующего." },
        new() { Id = MageChainLightning, Category = SkillCategory.Mage, Rarity = SkillRarity.Epic, MaxRank = 3, IconFrom = "knight_smite",
            NameEn = "Chain Lightning", DescEn = "Every 5th staff projectile also strikes the 2/3/4 nearest enemies with lightning.",
            NameRu = "Цепная молния", DescRu = "Каждый 5-й снаряд посоха также бьёт молнией 2/3/4 ближайших врагов." },
        new() { Id = MageFrostArmor, Category = SkillCategory.Mage, Rarity = SkillRarity.Rare, MaxRank = 3, IconFrom = "mage_critical_frost",
            NameEn = "Frost Armor", DescEn = "When hit, 20%/30%/40% chance to freeze the attacker for 1.5s.",
            NameRu = "Ледяная броня", DescRu = "При получении урона шанс 20%/30%/40% заморозить атакующего на 1,5 с." },
        new() { Id = OccultBloodHarvest, Category = SkillCategory.Occultist, Rarity = SkillRarity.Epic, MaxRank = 3, IconFrom = "occult_curse",
            NameEn = "Blood Harvest", DescEn = "Cursed enemies explode on death for 20%/35%/50% of their max health and curse enemies nearby (needs Curse).",
            NameRu = "Кровавая жатва", DescRu = "Проклятые враги при смерти взрываются на 20%/35%/50% своего здоровья и проклинают соседей (нужно Проклятие)." },
        new() { Id = OccultPainPact, Category = SkillCategory.Occultist, Rarity = SkillRarity.Rare, MaxRank = 3, IconFrom = "occult_martyrdom",
            NameEn = "Pain Pact", DescEn = "Every 10% of missing health grants +5%/+7%/+10% attack speed.",
            NameRu = "Пакт боли", DescRu = "Каждые 10% потерянного здоровья дают +5%/+7%/+10% к скорости атаки." },

        // ---- Fighter ----
        new() { Id = FighterMomentum, Category = SkillCategory.Fighter, Rarity = SkillRarity.Common, MaxRank = 3, IconFrom = "fighter_perfect_dodge",
            NameEn = "Momentum", DescEn = "After a dash, +10%/+15%/+20% Movement Speed for 3s.",
            NameRu = "Разгон", DescRu = "После рывка +10%/+15%/+20% к скорости передвижения на 3 с." },
        new() { Id = FighterBloodthirst, Category = SkillCategory.Fighter, Rarity = SkillRarity.Uncommon, MaxRank = 3, IconFrom = "fighter_killing_spree",
            NameEn = "Bloodthirst", DescEn = "Each kill restores 3%/5%/8% of your Max Health.",
            NameRu = "Жажда крови", DescRu = "Каждое убийство восстанавливает 3%/5%/8% максимального здоровья." },
        new() { Id = FighterUnstoppable, Category = SkillCategory.Fighter, Rarity = SkillRarity.Legendary, MaxRank = 1, IconFrom = "fighter_battle_rage",
            NameEn = "Unstoppable", DescEn = "For 1s after a dash you take no damage.",
            NameRu = "Неудержимый", DescRu = "В течение 1 с после рывка вы не получаете урона." },
        new() { Id = FighterSteelStorm, Category = SkillCategory.Fighter, Rarity = SkillRarity.Legendary, MaxRank = 1, IconFrom = "fighter_blade_vortex",
            NameEn = "Steel Storm", DescEn = "Each kill grants +30% Attack Speed for 5s.",
            NameRu = "Стальная буря", DescRu = "Каждое убийство даёт +30% к скорости атаки на 5 с." },
        // ---- Knight ----
        new() { Id = KnightSturdy, Category = SkillCategory.Knight, Rarity = SkillRarity.Common, MaxRank = 3, IconFrom = "knight_might",
            NameEn = "Sturdy", DescEn = "+10/+20/+30 Max Health.",
            NameRu = "Крепыш", DescRu = "+10/+20/+30 к максимальному здоровью." },
        new() { Id = KnightShieldbearer, Category = SkillCategory.Knight, Rarity = SkillRarity.Uncommon, MaxRank = 3, IconFrom = "knight_perfect_block",
            NameEn = "Shieldbearer", DescEn = "Blocking attacks costs 15%/25%/35% less Stamina.",
            NameRu = "Щитоносец", DescRu = "Блок ударов тратит на 15%/25%/35% меньше выносливости." },
        new() { Id = KnightCounterstrike, Category = SkillCategory.Knight, Rarity = SkillRarity.Epic, MaxRank = 3, IconFrom = "knight_defensive_style",
            NameEn = "Counterstrike", DescEn = "After a Block, your next Melee hit within 2s deals +40%/+60%/+80% damage.",
            NameRu = "Контрудар", DescRu = "После блока следующий удар ближнего боя (в течение 2 с) наносит +40%/+60%/+80% урона." },
        new() { Id = KnightBulwark, Category = SkillCategory.Knight, Rarity = SkillRarity.Legendary, MaxRank = 1, IconFrom = "knight_unyielding_champion",
            NameEn = "Bulwark", DescEn = "Once per floor, a killing blow leaves you at 1 Health and makes you invulnerable for 2s.",
            NameRu = "Бастион", DescRu = "Раз за этаж смертельный удар оставляет вас с 1 здоровья и даёт 2 с неуязвимости." },
        // ---- Mage ----
        new() { Id = MageFocus, Category = SkillCategory.Mage, Rarity = SkillRarity.Common, MaxRank = 3, IconFrom = "mage_a_little_something",
            NameEn = "Focus", DescEn = "+10/+20/+30 Max Mana.",
            NameRu = "Сосредоточение", DescRu = "+10/+20/+30 к максимальной мане." },
        new() { Id = MageManaFont, Category = SkillCategory.Mage, Rarity = SkillRarity.Uncommon, MaxRank = 3, IconFrom = "mage_essence_drain",
            NameEn = "Mana Font", DescEn = "Out of combat, +25%/+50%/+75% Mana regeneration.",
            NameRu = "Источник маны", DescRu = "Вне боя регенерация маны +25%/+50%/+75%." },
        new() { Id = MageManaShield, Category = SkillCategory.Mage, Rarity = SkillRarity.Epic, MaxRank = 3, IconFrom = "mage_pain_to_mana",
            NameEn = "Mana Shield", DescEn = "30%/40%/50% of incoming damage is taken from your Mana instead of Health while you have Mana.",
            NameRu = "Щит маны", DescRu = "30%/40%/50% получаемого урона снимается с маны вместо здоровья, пока есть мана." },
        new() { Id = MageOverload, Category = SkillCategory.Mage, Rarity = SkillRarity.Legendary, MaxRank = 1, IconFrom = "mage_arcane_overload",
            NameEn = "Overload", DescEn = "Every 4th Staff projectile also blows up the nearest enemy for 200% of your weapon damage in an area.",
            NameRu = "Перегрузка", DescRu = "Каждый 4-й снаряд посоха также взрывает ближайшего врага на 200% урона оружия по области." },
        new() { Id = MageTimeLoop, Category = SkillCategory.Mage, Rarity = SkillRarity.Legendary, MaxRank = 1, IconFrom = "mage_singularity",
            NameEn = "Time Loop", DescEn = "Once per dungeon run, death sends you back to the start of the floor: inventory, gold, Health, Mana and prayers as they were when you entered it. The floor starts over.",
            NameRu = "Петля времени", DescRu = "Раз за вылазку смерть возвращает вас в начало этажа: инвентарь, золото, здоровье, мана и молитвы такие, как при входе на этаж. Этаж начинается заново." },
        // ---- Occultist ----
        new() { Id = OccultBloodletter, Category = SkillCategory.Occultist, Rarity = SkillRarity.Common, MaxRank = 3, IconFrom = "occult_desecration",
            NameEn = "Bloodletter", DescEn = "Melee hits restore 1/2/3 Health.",
            NameRu = "Кровопускание", DescRu = "Удары ближнего боя восстанавливают 1/2/3 здоровья." },
        new() { Id = OccultSoulSiphon, Category = SkillCategory.Occultist, Rarity = SkillRarity.Uncommon, MaxRank = 3, IconFrom = "occult_soul_harvest",
            NameEn = "Soul Siphon", DescEn = "Each kill grants +2/+3/+4 Max Health until the end of the floor (up to 20 kills).",
            NameRu = "Сбор душ", DescRu = "Каждое убийство даёт +2/+3/+4 к максимальному здоровью до конца этажа (до 20 убийств)." },
        new() { Id = OccultRot, Category = SkillCategory.Occultist, Rarity = SkillRarity.Rare, MaxRank = 3, IconFrom = "occult_curse",
            NameEn = "Rot", DescEn = "Melee hits make the enemy lose 2%/3%/4% of its max health every second for 4s.",
            NameRu = "Гниль", DescRu = "Удары ближнего боя заставляют врага терять 2%/3%/4% его здоровья каждую секунду в течение 4 с." },
        new() { Id = OccultLordOfDeath, Category = SkillCategory.Occultist, Rarity = SkillRarity.Legendary, MaxRank = 1, IconFrom = "occult_dark_ritual",
            NameEn = "Lord of Death", DescEn = "Every enemy you kill explodes, dealing 30% of its max health to enemies around it.",
            NameRu = "Повелитель смерти", DescRu = "Каждый убитый враг взрывается и наносит 30% своего здоровья врагам вокруг." },
        new() { Id = OccultHarvestRite, Category = SkillCategory.Occultist, Rarity = SkillRarity.Legendary, MaxRank = 1, IconFrom = "occult_primal_vampirism",
            NameEn = "Harvest Rite", DescEn = "Every 10 kills on a floor give +10% damage until the end of the floor (up to +50%).",
            NameRu = "Обряд жатвы", DescRu = "Каждые 10 убийств на этаже дают +10% урона до конца этажа (до +50%)." },
        new() { Id = FighterWrecker, Category = SkillCategory.Fighter, Rarity = SkillRarity.Epic, MaxRank = 3, IconFrom = "occult_desecration",
            NameEn = "Wrecker", DescEn = "Every destructible object you break restores 2/3/4 Health.",
            NameRu = "Разрушитель", DescRu = "Каждый разрушенный объект восстанавливает 2/3/4 здоровья." },
        new() { Id = FighterLightHand, Category = SkillCategory.Fighter, Rarity = SkillRarity.Common, MaxRank = 3, IconFrom = "fighter_acrobat",
            NameEn = "Light Hand", DescEn = "When a hit breaks a destructible object, 50%/75%/100% of that swing's Stamina comes back at once.",
            NameRu = "Лёгкая рука", DescRu = "Если удар разрушил объект, сразу возвращается 50%/75%/100% выносливости, потраченной на этот удар." },
        new() { Id = OccultDevourer, Category = SkillCategory.Occultist, Rarity = SkillRarity.Legendary, MaxRank = 1, IconFrom = "occult_dark_ritual",
            NameEn = "Devourer", DescEn = "During a run, hold X over an item in your inventory to consume it: until you return to town you gain 10% of what the item does (a potion healing 30 gives +3 Max Health, a 20-damage sword +2% damage). Anvil upgrades add 25% per level.",
            NameRu = "Пожиратель", DescRu = "Во время вылазки зажмите X на предмете в инвентаре, чтобы поглотить его: до возвращения в деревню вы получаете 10% от того, что делает предмет (зелье на 30 здоровья даёт +3 к макс. здоровью, меч с уроном 20 даёт +2% урона). Улучшения с наковальни добавляют 25% за уровень." },
    };

    static readonly Dictionary<string, ModSkill> ById = new();

    static SkillRegistry() { foreach (var s in All) ById[s.Id] = s; }

    public static bool TryGet(string id, out ModSkill s) { s = null; return id != null && ById.TryGetValue(id, out s); }

    public static int Rank(string id)
    {
        var m = PlayerSkillManager.Instance;
        return m == null ? 0 : m.GetSkillRank(id);
    }

    // Rank that also counts in the village, where the game switches skills off (GetSkillRank = 0).
    // Used only by skills meant to work in town too (Low Gravity, Free Stride). A skill switched off
    // by the player stays off.
    public static int RankAnywhere(string id)
    {
        var m = PlayerSkillManager.Instance;
        if (m == null || SkillToggle.IsOff(id)) return 0;
        var st = m.GetSkillState(id);
        return st == null ? 0 : st.currentActiveRank;
    }

    public static bool Russian()
    {
        try
        {
            var loc = LocalizationSettings.SelectedLocale;
            return loc != null && loc.Identifier.Code.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

[HarmonyPatch(typeof(PlayerSkillManager), nameof(PlayerSkillManager.InitializeDatabase))]
internal static class RegisterSkillsPatch
{
    static void Postfix(PlayerSkillManager __instance)
    {
        foreach (var s in SkillRegistry.All)
        {
            try
            {
                var src = __instance.GetSkill(s.IconFrom);
                __instance.AddRuntimeSkill(s.Id, s.NameEn, s.DescEn, s.Category, s.Rarity, s.MaxRank, src != null ? src.iconName : "");
                var added = __instance.GetSkill(s.Id);
                // Copy the sprite object itself: icon names are not guaranteed to load by name.
                if (added != null && src != null && src.icon != null) added.icon = src.icon;
            }
            catch (Exception e) { Plugin.L.LogError($"Skill {s.Id} not registered: {e.Message}"); }
        }
    }
}

[HarmonyPatch(typeof(SkillData), nameof(SkillData.GetLocalizedName))]
internal static class SkillNamePatch
{
    static void Postfix(SkillData __instance, ref string __result)
    {
        if (SkillRegistry.TryGet(__instance.skillId, out var s)) __result = SkillRegistry.Russian() ? s.NameRu : s.NameEn;
    }
}

[HarmonyPatch(typeof(SkillData), nameof(SkillData.GetLocalizedDescription))]
internal static class SkillDescPatch
{
    static void Postfix(SkillData __instance, ref string __result)
    {
        if (SkillRegistry.TryGet(__instance.skillId, out var s)) __result = SkillRegistry.Russian() ? s.DescRu : s.DescEn;
    }
}
