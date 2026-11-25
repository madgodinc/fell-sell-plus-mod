using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Il2CppTMPro;
using UnityEngine;

namespace FellSellPlus.Info;

// Under each prayer card: what the prayer changes for this character right now (before -> after),
// wasted bonuses flagged, costs in red, and a star when it is the only card that helps.
// Hook: PrayerCard.Setup (0x180498F70), called by the PrayersUI card-spawn coroutines.
[HarmonyPatch(typeof(PrayerCard), nameof(PrayerCard.Setup))]
internal static class PrayerHints
{
    sealed class Hint { public PrayerCard Card; public TextMeshProUGUI Text; public string Body; public bool Helpful; }
    static readonly List<Hint> Live = new();

    static void Postfix(PrayerCard __instance, PrayerData __0)
    {
        if (!InfoSettings.PrayerHints.Value || __instance == null || __0 == null) return;
        try
        {
            Live.RemoveAll(h => h.Card == null || h.Text == null);
            var body = Describe(__0, out bool helpful);
            var t = Attach(__instance);
            Live.RemoveAll(h => h.Card.Pointer == __instance.Pointer);
            Live.Add(new Hint { Card = __instance, Text = t, Body = body, Helpful = helpful });
            int helpers = 0; foreach (var h in Live) if (h.Helpful) helpers++;
            foreach (var h in Live)
                h.Text.text = (helpers == 1 && h.Helpful && Live.Count > 1
                    ? InfoUi.C(InfoUi.Gold, "★ " + InfoUi.T("единственный полезный", "the only one that helps")) + "\n"
                    : "") + h.Body;
        }
        catch (Exception e) { Plugin.L.LogWarning($"Prayer hint failed: {e.Message}"); }
    }

    static TextMeshProUGUI Attach(PrayerCard card)
    {
        var parent = card.transform;
        var old = parent.Find("FSP_Hint");
        if (old != null) return old.GetComponent<TextMeshProUGUI>();
        var rt = InfoUi.Rect("FSP_Hint", parent, new Vector2(0.5f, 0f), new Vector2(0, -12), new Vector2(380, 220), new Vector2(0.5f, 1f));
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        var f = card.descText != null ? card.descText.font : InfoUi.Font();
        if (f != null) t.font = f;
        t.fontSize = 18; t.alignment = TextAlignmentOptions.Top; t.richText = true;
        t.enableWordWrapping = true; t.raycastTarget = false; t.color = InfoUi.Ink;
        return t;
    }

    static float Mul(float m) => Math.Abs(m) < 0.0001f ? 1f : m;

    static string Describe(PrayerData p, out bool helpful)
    {
        var ps = PlayerStats.Instance;
        var pm = PrayersManager.Instance;
        var inv = InventoryManager.Instance;
        var sb = new StringBuilder();
        bool help = false;

        bool hasStaff = false, hasMelee = false;
        if (inv != null)
            foreach (var s in new[] { inv.rightHandSlot, inv.leftHandSlot })
            {
                var w = s?.item;
                if (w == null || w.damage <= 0) continue;
                if (w.manaCost > 0f) hasStaff = true; else hasMelee = true;
            }

        void Line(string label, float before, float after, string fmt, bool higherIsBetter = true, string wasted = null)
        {
            if (Math.Abs(after - before) < 0.0001f) return;
            bool better = higherIsBetter ? after > before : after < before;
            string arrow = $"{before.ToString(fmt)} → {after.ToString(fmt)}";
            if (wasted != null) { sb.Append(InfoUi.C(InfoUi.Muted, $"{label}: {arrow} ({wasted})")).Append('\n'); return; }
            sb.Append(InfoUi.C(better ? InfoUi.Good : InfoUi.Bad, $"{label}: {arrow}")).Append('\n');
            if (better) help = true;
        }
        void Cost(string ru, string en) => sb.Append(InfoUi.C(InfoUi.Bad, InfoUi.T(ru, en))).Append('\n');

        if (ps != null)
        {
            Line(InfoUi.T("Здоровье", "Health"), ps.MaxHealth, (ps.MaxHealth + p.maxHealthBonus) * Mul(p.maxHealthMultiplier), "0");
            Line(InfoUi.T("Выносливость", "Stamina"), ps.MaxStamina, (ps.MaxStamina + p.maxStaminaBonus) * Mul(p.maxStaminaMultiplier), "0");
            Line(InfoUi.T("Мана", "Mana"), ps.MaxMana, (ps.MaxMana + p.maxManaBonus) * Mul(p.maxManaMultiplier), "0");
        }
        if (pm != null)
        {
            float crit = pm.GetCritChanceBonus();
            Line(InfoUi.T("Шанс крит. удара", "Crit Chance"), crit, crit + p.critChanceBonus, "0.#",
                wasted: crit >= 100f && p.critChanceBonus > 0 ? InfoUi.T("уже 100%", "already 100%") : null);
            float atk = pm.GetAttackSpeedMultiplier();
            Line(InfoUi.T("Скорость атаки", "Attack Speed"), atk, atk + p.attackSpeedBonus, "x0.00");
            float ph = pm.GetPhysicalDamageMultiplier();
            Line(InfoUi.T("Физ. урон", "Physical Damage"), ph, ph + p.physicalDamageBonus, "x0.00",
                wasted: !hasMelee && p.physicalDamageBonus > 0 ? InfoUi.T("нет оружия ближнего боя", "no melee weapon") : null);
            float mg = pm.GetMagicalDamageMultiplier();
            Line(InfoUi.T("Маг. урон", "Magical Damage"), mg, mg + p.magicalDamageBonus, "x0.00",
                wasted: !hasStaff && p.magicalDamageBonus > 0 ? InfoUi.T("нет посоха", "no staff") : null);
            float mv = pm.GetMovementSpeedMultiplier();
            Line(InfoUi.T("Скорость передвижения", "Move Speed"), mv, mv + p.movementSpeedBonus, "x0.00");
            float red = pm.GetDamageReduction() * 100f;
            Line(InfoUi.T("Снижение урона", "Damage Reduction"), red, red + p.damageReduction * 100f, "0.#'%'");
            float drop = pm.GetDropChanceBonus();
            Line(InfoUi.T("Шанс выпадения", "Drop Chance"), drop, drop + p.dropChanceBonus, "+0.#;-0.#;0");
        }
        if (Math.Abs(p.armorBonus) > 0.001f) { sb.Append(InfoUi.C(p.armorBonus > 0 ? InfoUi.Good : InfoUi.Bad, $"{InfoUi.T("Броня", "Armor")} {p.armorBonus:+0;-0}")).Append('\n'); if (p.armorBonus > 0) help = true; }
        if (Math.Abs(p.staminaRegenBonus) > 0.001f) { sb.Append(InfoUi.C(InfoUi.Good, $"{InfoUi.T("Восстановление выносливости", "Stamina Regeneration")} +{Pct(p.staminaRegenBonus):0.#}%")).Append('\n'); help = true; }
        if (Math.Abs(p.manaRegenBonus) > 0.001f) { sb.Append(InfoUi.C(InfoUi.Good, $"{InfoUi.T("Регенерация маны", "Mana Regeneration")} +{Pct(p.manaRegenBonus):0.#}%")).Append('\n'); help = true; }
        if (p.deathSavesCount > 0) { sb.Append(InfoUi.C(InfoUi.Good, InfoUi.T($"+{p.deathSavesCount} спасение от смерти", $"+{p.deathSavesCount} death save"))).Append('\n'); help = true; }
        if (p.lifeDrainPercent > 0 || p.lifeDrainPerHit > 0) { sb.Append(InfoUi.C(InfoUi.Good, InfoUi.T("Похищение здоровья", "Lifedrain"))).Append('\n'); help = true; }

        float dtm = Mul(p.damageTakenMultiplier);
        if (Math.Abs(dtm - 1f) > 0.001f) sb.Append(InfoUi.C(dtm > 1f ? InfoUi.Bad : InfoUi.Good, $"{InfoUi.T("Получаемый урон", "Damage Received")} x{dtm:0.##}")).Append('\n');
        float esm = Mul(p.enemySpawnMultiplier);
        if (Math.Abs(esm - 1f) > 0.001f) sb.Append(InfoUi.C(esm > 1f ? InfoUi.Bad : InfoUi.Good, $"{InfoUi.T("Врагов", "Enemies")} x{esm:0.##}")).Append('\n');
        if (p.blocksSprint) Cost("Нельзя бегать", "No sprinting");
        if (p.blocksJump) Cost("Нельзя прыгать", "No jumping");
        if (p.blocksConsumables) Cost("Нельзя расходники", "No consumables");
        if (p.forceOneHP) Cost("Всегда 1 ОЗ", "Always 1 HP");
        if (p.hpDrainPerSecond > 0 || p.hpDrainPercentPerSecond > 0) Cost("Теряете здоровье каждую секунду", "Lose health every second");
        if (p.midasTouch) Cost("Всё золото теряется при смерти", "Lose all gold on death");

        if (sb.Length == 0) sb.Append(InfoUi.C(InfoUi.Muted, InfoUi.T("особый эффект, см. описание", "special effect, see description")));
        helpful = help;
        return sb.ToString().TrimEnd('\n');
    }

    // Regen bonuses are stored as fractions in PrayerData (0.2 = +20%).
    static float Pct(float v) => Math.Abs(v) <= 5f ? v * 100f : v;
}
