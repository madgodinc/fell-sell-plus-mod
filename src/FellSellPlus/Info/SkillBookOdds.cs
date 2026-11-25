using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppTMPro;
using UnityEngine;

namespace FellSellPlus.Info;

// While the player looks at a skill book, list every skill it can still roll and its exact chance.
// Rarity odds per tier are the constants in PlayerSkillManager.RollRarityForTier (0x180486040):
// Random.Range(0,100) against 75 (T1), 70/95 (T2), 50/90 (T3). A skill can roll while its unlocked
// capacity is below maxRank. When a rarity has nothing left, its share moves to the next higher
// rarity that still has skills, else to the next lower one. Mod skills (fsp_*) are in the same
// database, so they show up automatically.
internal static class SkillBookOdds
{
    static GameObject _root;
    static TextMeshProUGUI _text;
    static IntPtr _shown;
    static float _next;
    static bool _broken;

    static readonly (int rarity, float pct)[][] TierOdds =
    {
        new[] { (0, 75f), (1, 25f) },
        new[] { (1, 70f), (2, 25f), (3, 5f) },
        new[] { (2, 50f), (3, 40f), (4, 10f) },
    };

    public static void Tick()
    {
        if (_broken || !InfoSettings.SkillBookOdds.Value) return;
        try
        {
            var book = LookedAtBook();
            if (book == null)
            {
                if (_root != null && _root.activeSelf) _root.SetActive(false);
                _shown = IntPtr.Zero;
                return;
            }
            if (_root == null) Build();
            // Recompute on target change and every second (a purchase changes the odds).
            if (book.Pointer != _shown || UnityEngine.Time.unscaledTime >= _next)
            {
                _shown = book.Pointer;
                _next = UnityEngine.Time.unscaledTime + 1f;
                _text.text = Compose(book.Category, book.Tier);
            }
            if (!_root.activeSelf) _root.SetActive(true);
        }
        catch (Exception e)
        {
            Plugin.L.LogWarning($"Skill book odds disabled: {e.Message}");
            _broken = true;
            if (_root != null) _root.SetActive(false);
        }
    }

    static SkillBookInteractable LookedAtBook()
    {
        var fpc = FirstPersonController.Instance;
        var cur = fpc != null ? fpc.CurrentInteractable : null;
        if (cur == null) return null;
        return ((Il2CppObjectBase)cur).TryCast<SkillBookInteractable>();
    }

    static void Build()
    {
        _root = InfoUi.Canvas("FSP_SkillBookOdds", 95);
        var panel = InfoUi.Panel("Panel", _root.transform, new Vector2(1f, 0.5f), new Vector2(-40, 40), new Vector2(520, 720), new Vector2(1f, 0.5f), 0.95f);
        _text = InfoUi.Text(panel, "Text", 19, TextAlignmentOptions.TopLeft, 26);
    }

    internal sealed class Option
    {
        public SkillData Skill;
        public float Chance; // 0..100
        public bool IsNew;
        public int Rank, MaxRank;
    }

    // Exposed for other screens (e.g. skills page hints).
    internal static List<Option> Odds(SkillCategory cat, int tier, out float newChance)
    {
        newChance = 0f;
        var result = new List<Option>();
        var psm = PlayerSkillManager.Instance;
        if (psm == null || tier < 1 || tier > 3) return result;

        var pool = new Dictionary<int, List<Option>>();
        foreach (var s in psm.GetSkillsByCategory(cat))
        {
            if (s == null) continue;
            var st = psm.GetSkillState(s.skillId);
            int cap = st != null ? st.unlockedCapacity : 0;
            if (cap >= s.maxRank) continue;
            int r = (int)s.rarity;
            if (!pool.TryGetValue(r, out var l)) pool[r] = l = new List<Option>();
            l.Add(new Option { Skill = s, IsNew = cap == 0, Rank = cap, MaxRank = s.maxRank });
        }

        var share = new float[6];
        foreach (var (rarity, pct) in TierOdds[tier - 1])
        {
            int target = rarity;
            if (!pool.ContainsKey(target))
            {
                target = -1;
                for (int up = rarity + 1; up <= 4 && target < 0; up++) if (pool.ContainsKey(up)) target = up;
                for (int down = rarity - 1; down >= 0 && target < 0; down--) if (pool.ContainsKey(down)) target = down;
            }
            if (target >= 0) share[target] += pct;
        }

        foreach (var (r, list) in pool)
        {
            if (share[r] <= 0f) continue;
            float each = share[r] / list.Count;
            foreach (var o in list) { o.Chance = each; result.Add(o); if (o.IsNew) newChance += each; }
        }
        return result.OrderByDescending(o => (int)o.Skill.rarity).ThenByDescending(o => o.Chance).ToList();
    }

    static string Compose(SkillCategory cat, int tier)
    {
        var sb = new StringBuilder();
        var opts = Odds(cat, tier, out float newChance);
        sb.Append($"<size=24><color={InfoUi.Hex(InfoUi.Gold)}>{InfoUi.T("Книга навыков", "Skill Book")}</color></size>  ");
        sb.Append(InfoUi.C(InfoUi.Muted, $"{CategoryName(cat)} · {InfoUi.T("ур.", "tier")} {tier}")).Append('\n');
        if (opts.Count == 0)
        {
            sb.Append(InfoUi.C(InfoUi.Muted, InfoUi.T("Все навыки этой книги уже на максимуме.", "Every skill this book can give is maxed.")));
            return sb.ToString();
        }
        sb.Append(InfoUi.T("Шанс нового навыка: ", "Chance of a new skill: "))
          .Append(InfoUi.C(newChance > 0 ? InfoUi.Good : InfoUi.Muted, $"{newChance:0.#}%")).Append("\n\n");

        int lastR = -1;
        foreach (var o in opts)
        {
            int r = (int)o.Skill.rarity;
            if (r != lastR)
            {
                float sum = opts.Where(x => (int)x.Skill.rarity == r).Sum(x => x.Chance);
                sb.Append($"<color={InfoUi.RarityHex(r)}>{InfoUi.RarityName(r)}</color>  {InfoUi.C(InfoUi.Muted, $"{sum:0.#}%")}\n");
                lastR = r;
            }
            string name = SafeName(o.Skill);
            string tag = o.IsNew
                ? InfoUi.C(InfoUi.Good, InfoUi.T("НОВЫЙ", "NEW"))
                : InfoUi.C(InfoUi.Muted, $"{o.Rank}→{o.Rank + 1}/{o.MaxRank}");
            sb.Append($"  {name}<pos=70%>{o.Chance:0.#}%<pos=84%>{tag}\n");
        }
        return sb.ToString();
    }

    internal static string SafeName(SkillData s)
    {
        try { var n = s.GetLocalizedName(); if (!string.IsNullOrEmpty(n)) return n; } catch { }
        return s.skillName ?? s.skillId;
    }

    static string CategoryName(SkillCategory c) => c switch
    {
        SkillCategory.Fighter => InfoUi.T("Боец", "Fighter"),
        SkillCategory.Knight => InfoUi.T("Рыцарь", "Knight"),
        SkillCategory.Mage => InfoUi.T("Маг", "Mage"),
        _ => InfoUi.T("Оккультист", "Occultist"),
    };
}
