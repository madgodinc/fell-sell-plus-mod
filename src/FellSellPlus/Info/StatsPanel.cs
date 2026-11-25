using System;
using System.Text;
using Il2CppTMPro;
using UnityEngine;

namespace FellSellPlus.Info;

// Extra panel beside the inventory window: gear bonus totals (the game never sums them) and
// effective stats after prayers, gear and difficulty. Polled, no hooks: shown while the
// inventory window panel is active, refreshed twice a second.
internal static class StatsPanel
{
    static GameObject _root;
    static TextMeshProUGUI _text;
    static InventoryUI _inv;
    static float _next;
    static bool _broken;
    static float _nextFind;

    public static void Tick()
    {
        if (_broken || !InfoSettings.StatsPanel.Value) return;
        try
        {
            // A whole-scene search is expensive: at most twice a second while the window is missing.
            if (_inv == null && UnityEngine.Time.unscaledTime >= _nextFind)
            {
                _nextFind = UnityEngine.Time.unscaledTime + 0.5f;
                _inv = UnityEngine.Object.FindObjectOfType<InventoryUI>();
            }
            bool show = _inv != null && _inv.inventoryWindowPanel != null && _inv.inventoryWindowPanel.activeInHierarchy
                        && PlayerStats.Instance != null
                        && !ChestOpen(); // the chest grid sits where the panel would go
            if (!show) { if (_root != null && _root.activeSelf) _root.SetActive(false); return; }
            if (_root == null) Build();
            if (!_root.activeSelf) { _root.SetActive(true); _next = 0; }
            if (UnityEngine.Time.unscaledTime < _next) return;
            _next = UnityEngine.Time.unscaledTime + 0.5f;
            _text.text = Compose();
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Stats panel disabled: {e.Message}");
            _broken = true;
            if (_root != null) _root.SetActive(false);
        }
    }

    static bool ChestOpen()
    {
        var c = ChestStorageUI.Instance;
        return c != null && (c.IsChestModeActive || (c.chestWindowPanel != null && c.chestWindowPanel.activeInHierarchy));
    }

    static void Build()
    {
        _root = InfoUi.Canvas("FSP_StatsPanel", 90);
        var panel = InfoUi.Panel("Panel", _root.transform, new Vector2(1f, 1f), new Vector2(-24, -112), new Vector2(500, 600), new Vector2(1f, 1f), 0.95f);
        _text = InfoUi.Text(panel, "Text", 19, TextAlignmentOptions.TopLeft, 26);
    }

    struct Gear
    {
        public float Hp, Sta, Mana, Carry, Armor, StaRegen, ManaRegen, Move, AtkSpd, Dmg, Crit, CritDmg, Drop, Recipe;
        public int Rerolls;
    }

    static Gear SumGear(InventoryManager inv)
    {
        var g = new Gear();
        foreach (var s in new[] { inv.rightHandSlot, inv.leftHandSlot, inv.helmetSlot, inv.upperArmorSlot, inv.lowerArmorSlot })
        {
            var i = s?.item;
            if (i == null) continue;
            g.Hp += i.dungeonBonusHealth; g.Sta += i.dungeonBonusStamina; g.Mana += i.dungeonBonusMana;
            g.Carry += i.dungeonBonusMaxCarryWeight; g.Armor += i.dungeonBonusArmor;
            g.StaRegen += i.dungeonStaminaRegen; g.ManaRegen += i.dungeonManaRegen; g.Move += i.dungeonMoveSpeed;
            g.AtkSpd += i.dungeonAttackSpeed; g.Dmg += i.dungeonBonusAttackDamage; g.Crit += i.dungeonBonusCritChance;
            g.CritDmg += i.dungeonBonusCritDamage; g.Drop += i.dungeonDropChance; g.Recipe += i.dungeonRecipeDropChance;
            g.Rerolls += i.dungeonBonusRerolls;
        }
        return g;
    }

    static string Compose()
    {
        var ps = PlayerStats.Instance;
        var pm = PrayersManager.Instance;
        var inv = InventoryManager.Instance;
        var dm = DifficultyManager.Instance;
        var sb = new StringBuilder();
        string H(string ru, string en) => $"<size=23><color={InfoUi.Hex(InfoUi.Gold)}>{InfoUi.T(ru, en)}</color></size>\n";
        void L(string ru, string en, string v) => sb.Append($"{InfoUi.T(ru, en)}<pos=68%>{v}\n");
        string Pct(float f) => (f >= 0 ? "+" : "") + f.ToString("0.#") + "%";

        var g = inv != null ? SumGear(inv) : default;

        sb.Append(H("Бонусы снаряжения", "Equipment Bonuses"));
        int lines = 0;
        void G(float v, string ru, string en, bool pct) { if (Math.Abs(v) < 0.001f) return; lines++; L(ru, en, pct ? Pct(v) : "+" + v.ToString("0.#")); }
        G(g.Hp, "Здоровье", "Health", false); G(g.Sta, "Выносливость", "Stamina", false); G(g.Mana, "Мана", "Mana", false);
        G(g.Armor, "Броня", "Armor", false); G(g.Carry, "Максимальный вес", "Max Weight", false);
        G(g.StaRegen, "Восстановление выносливости", "Stamina Regeneration", true); G(g.ManaRegen, "Регенерация маны", "Mana Regeneration", true);
        G(g.Move, "Скорость передвижения", "Move Speed", true); G(g.AtkSpd, "Скорость атаки", "Attack Speed", true);
        G(g.Dmg, "Урон от атаки", "Attack Damage", true); G(g.Crit, "Шанс крит. удара", "Crit Chance", true);
        G(g.CritDmg, "Критический урон", "Crit Damage", true); G(g.Drop, "Шанс выпадения", "Drop Chance", true);
        G(g.Recipe, "Шанс на рецепт", "Recipe Chance", true);
        if (g.Rerolls != 0) { lines++; L("Перебросы", "Rerolls", "+" + g.Rerolls); }
        if (lines == 0) sb.Append(InfoUi.C(InfoUi.Muted, InfoUi.T("нет", "none")) + "\n");

        sb.Append('\n').Append(H("Итоговые параметры", "Effective Stats"));
        L("Макс. здоровье", "Max Health", ps.MaxHealth.ToString("0"));
        L("Макс. выносливость", "Max Stamina", ps.MaxStamina.ToString("0"));
        L("Макс. мана", "Max Mana", ps.MaxMana.ToString("0"));
        L("Восст. здоровья", "Health regen", ps._healthRegenRate.ToString("0.##") + InfoUi.T("/с", "/s"));
        L("Восстановление выносливости", "Stamina Regeneration", ps._staminaRegenRate.ToString("0.##") + InfoUi.T("/с", "/s"));
        L("Регенерация маны", "Mana Regeneration", ps._manaRegenRate.ToString("0.##") + InfoUi.T("/с", "/s"));

        float armorRed = ps.GetCurrentArmorDamageReduction();
        float prayerRed = pm != null ? pm.GetDamageReduction() : 0f;
        float prayerTaken = pm != null ? pm.DamageTakenMultiplier : 1f;
        float diffIn = dm != null ? dm.IncomingDamageMultiplier : 1f;
        float taken = diffIn * (1f - prayerRed) * (prayerTaken <= 0 ? 1f : prayerTaken) * (1f - armorRed);
        L("Снижение бронёй", "Armor Reduction", (armorRed * 100f).ToString("0.#") + "%");
        L("Получаемый урон", "Damage Received", "x" + taken.ToString("0.00"));

        float atk = pm != null ? pm.GetAttackSpeedMultiplier() : 1f;
        L("Скорость атаки", "Attack Speed", "x" + (atk * ps.AttackSpeedModifier).ToString("0.00"));

        float crit = (pm != null ? pm.GetCritChanceBonus() : 0f) + g.Crit;
        L("Шанс крит. удара", "Crit Chance", crit > 100f
            ? InfoUi.C(InfoUi.Bad, $"100% ({crit:0.#})")
            : $"{crit:0.#}% {InfoUi.C(InfoUi.Muted, InfoUi.T("+мебель", "+furniture"))}");
        float critMul = 1.5f + g.CritDmg / 100f;
        L("Критический урон", "Crit Damage", "x" + critMul.ToString("0.00"));

        float move = pm != null ? pm.GetMovementSpeedMultiplier() : 1f;
        L("Скорость передвижения", "Move Speed", "x" + move.ToString("0.00"));
        float drop = (pm != null ? pm.GetDropChanceBonus() : 0f) + g.Drop;
        L("Шанс выпадения", "Drop Chance", Pct(drop));
        L("Шанс на рецепт", "Recipe Chance", Pct(g.Recipe));

        if (inv != null)
        {
            sb.Append('\n').Append(H("Урон за удар", "Damage per hit"));
            float phys = pm != null ? pm.GetPhysicalDamageMultiplier() : 1f;
            float mag = pm != null ? pm.GetMagicalDamageMultiplier() : 1f;
            float diffOut = dm != null ? dm.OutgoingDamageMultiplier : 1f;
            Hand(sb, InfoUi.T("Правая рука", "Right hand"), inv.rightHandSlot?.item, phys, mag, diffOut, g.Dmg, critMul);
            Hand(sb, InfoUi.T("Левая рука", "Left hand"), inv.leftHandSlot?.item, phys, mag, diffOut, g.Dmg, critMul);
            sb.Append(InfoUi.C(InfoUi.Muted, InfoUi.T("<size=16>без бонусов мебели и зелий</size>", "<size=16>furniture and potions not included</size>")));
        }
        return sb.ToString();
    }

    static void Hand(StringBuilder sb, string label, ItemData w, float phys, float mag, float diff, float gearDmgPct, float critMul)
    {
        if (w == null || w.damage <= 0) return;
        bool staff = w.manaCost > 0f;
        float hit = w.damage * (staff ? mag : phys) * diff * (1f + gearDmgPct / 100f);
        sb.Append($"{label}<pos=68%>{hit:0} / {InfoUi.C(InfoUi.Gold, (hit * critMul).ToString("0"))}\n");
    }
}
