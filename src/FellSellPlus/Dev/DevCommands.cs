using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using FellSellPlus.Progression;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FellSellPlus.Dev;

// Developer test harness, off by default (cfg Dev/Commands). Lines written to
// UserData/FellSellPlus/dev_cmd.txt are executed once and the file is deleted; results are
// appended to dev_out.txt. Lets the mod be exercised in a running game without manual input.
internal static class DevCommands
{
    public static ConfigEntry<bool> Enabled;
    static string Dir => Path.Combine(MelonEnvironment.UserDataDirectory, "FellSellPlus");
    static float _next;

    public static void Bind(ConfigFile cfg) =>
        Enabled = cfg.Bind("Dev", "Commands", false, "Developer test commands from UserData/FellSellPlus/dev_cmd.txt. Leave off.");

    public static void Tick()
    {
        if (!Enabled.Value || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.5f;
        // Two-instance testing passes --fsp-role=<name> so each instance gets its own command file.
        var roleArg = Environment.GetCommandLineArgs().FirstOrDefault(x => x.StartsWith("--fsp-role="));
        string suffix = roleArg != null ? "_" + roleArg.Substring(11) : "";
        var file = Path.Combine(Dir, $"dev_cmd{suffix}.txt");
        if (!File.Exists(file)) return;
        string[] lines;
        try { lines = File.ReadAllLines(file); File.Delete(file); } catch { return; }
        var sb = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            string result;
            try { result = Run(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)); }
            catch (Exception e) { result = "ERROR " + e; }
            sb.AppendLine($"[{DateTime.Now:HH:mm:ss}] > {line}\n{result}");
        }
        Directory.CreateDirectory(Dir);
        File.AppendAllText(Path.Combine(Dir, $"dev_out{suffix}.txt"), sb.ToString());
    }

    static string Run(string[] a)
    {
        var fpc = FirstPersonController.Instance;
        switch (a[0])
        {
            case "scene": return SceneManager.GetActiveScene().name;
            case "continue":
            {
                var menu = UnityEngine.Object.FindObjectOfType<MainPauseMenu>();
                if (menu == null || !menu.CanContinue()) return "cannot continue";
                menu.Continue();
                return "continuing";
            }
            case "load":
            {
                var ui = Resources.FindObjectsOfTypeAll<SaveSlotUI>().FirstOrDefault();
                if (ui == null) return "no SaveSlotUI";
                ui.gameObject.SetActive(true);
                ui.SetMode(SaveSlotUI.SlotPanelMode.Load);
                ui.OnSlotSelected(int.Parse(a[1]));
                return "load requested";
            }
            case "pos":
                return fpc == null ? "no player" : $"{fpc.transform.position} fwd {fpc.transform.forward} cam {Camera.main?.transform.position} camfwd {Camera.main?.transform.forward} dist {fpc.interactionDistance} radius {fpc.interactionRadius} mask {fpc.interactionLayer.value}";
            case "tp":
                Teleport(new Vector3(float.Parse(a[1]), float.Parse(a[2]), float.Parse(a[3])));
                return "ok";
            case "tpobj":
            {
                var go = GameObject.Find(a[1]);
                if (go == null) return "not found";
                var off = a.Length >= 5 ? new Vector3(float.Parse(a[2]), float.Parse(a[3]), float.Parse(a[4])) : Vector3.zero;
                Teleport(go.transform.position + off);
                return $"at {go.transform.position + off}";
            }
            case "face":
            {
                var go = GameObject.Find(a[1]);
                if (go == null || fpc == null) return "not found";
                var d = go.transform.position - fpc.transform.position; d.y = 0;
                fpc.transform.rotation = Quaternion.LookRotation(d);
                return "ok";
            }
            case "objs":
            {
                var rx = new Regex(a[1], RegexOptions.IgnoreCase);
                var sb = new StringBuilder();
                foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
                    if (rx.IsMatch(t.name)) sb.AppendLine($"{t.name} {t.position} active={t.gameObject.activeInHierarchy}");
                return sb.ToString();
            }
            case "look":
            {
                var it = fpc?.CurrentInteractable;
                if (it == null) return "nothing";
                return $"{it.GetActionText()} / {it.GetObjectText()}";
            }
            case "use": // interact with what the player looks at
                fpc?.CurrentInteractable?.Interact();
                return "ok";
            case "interact":
            {
                var go = GameObject.Find(a[1]);
                if (go == null) return "not found";
                var st = go.GetComponentInChildren<PrayersStatue>(); if (st != null) { st.Interact(); return "statue"; }
                var bk = go.GetComponentInChildren<SkillBookInteractable>(); if (bk != null) { bk.Interact(); return "book"; }
                var al = go.GetComponentInChildren<GraveAltar>(); if (al != null) { al.Interact(); return "altar"; }
                return "no known interactable";
            }
            case "gold":
                ShopManager.Instance.PlayerGold += int.Parse(a[1]);
                return ShopManager.Instance.PlayerGold.ToString();
            case "prestige":
                ShopManager.Instance.shopPrestige = float.Parse(a[1]);
                return "ok";
            case "buy":
                return ProgressionState.TryBuy(Enum.Parse<Stat>(a[1])).ToString();
            case "progwin":
                if (a.Length > 1 && a[1] == "close") ProgressionWindow.Close(); else ProgressionWindow.Open();
                return "ok";
            case "pick":
            {
                var ui = UnityEngine.Object.FindObjectOfType<PrayersUI>();
                var boons = ui?._cachedOfferedBoons;
                if (boons == null) return "no prayer UI";
                ui.SelectBoon(boons[int.Parse(a[1])]);
                return "picked";
            }
            case "book":
            {
                var ok = PlayerSkillManager.Instance.TryUnlockSkillFromBook((SkillCategory)int.Parse(a[1]), int.Parse(a[2]), out var sk, out _);
                return ok ? $"{sk.skillId} {sk.GetLocalizedName()}" : "failed";
            }
            case "recalc":
                PlayerStats.Instance.RecalculateAllStats();
                return $"HP max {PlayerStats.Instance.MaxHealth}";
            case "roll":
            {
                var st = GameObject.Find(a[1])?.GetComponentInChildren<PrayersStatue>();
                if (st == null) return "no statue";
                var got = st.RollBoons(3);
                var names = new StringBuilder();
                for (int i = 0; i < got.Count; i++) names.Append(got[i].name).Append(", ");
                return $"RollBoons(3) -> {got.Count} [{names}] pool {st.boonPool?.Count}";
            }
            case "addprayer":
            {
                var pm = PrayersManager.Instance;
                var pr = PrayersManager.GetPrayerByName(a[1]);
                if (pr == null) return "unknown prayer";
                pm.AddPrayer(pr);
                return $"added {pr.name}";
            }
            case "showprayers": // open the prayer choice with the named prayers
            {
                var ui = UnityEngine.Object.FindObjectOfType<PrayersUI>();
                var list = new Il2CppSystem.Collections.Generic.List<PrayerData>();
                foreach (var n in a.Skip(1)) { var pr = PrayersManager.GetPrayerByName(n); if (pr != null) list.Add(pr); }
                ui.OpenUI(null, list, "Test");
                return $"shown {list.Count}";
            }
            case "shopwin":
                FellSellPlus.Shop.ShopWindow.Toggle();
                return $"open={FellSellPlus.Shop.ShopWindow.IsOpen} blocked={FirstPersonController.IsPlayerBlocked} modal={ModalGuard.Active}";
            case "save":
                SaveManager.Instance.TryAutoSave();
                return "autosave requested";
            case "skillup": // unlock (if needed) and rank up a skill, spending skill points
            {
                var sm = PlayerSkillManager.Instance;
                bool ok = sm.TryUpgradeSkill(a[1]);
                return $"upgrade {a[1]}: {ok}, rank now {sm.GetSkillRank(a[1])}";
            }
            case "skillset": // dev: force a skill's discovered capacity and active rank
            {
                var sm = PlayerSkillManager.Instance;
                int r = int.Parse(a[2]);
                var states = sm._skillStates;
                if (!states.ContainsKey(a[1]))
                {
                    var ns = new PlayerSkillManager.SkillProgressState();
                    ns.skillId = a[1];
                    states[a[1]] = ns;
                }
                var st = states[a[1]];
                st.unlockedCapacity = Math.Max(st.unlockedCapacity, r);
                st.currentActiveRank = r;
                PlayerStats.Instance?.RecalculateAllStats();
                return $"{a[1]} rank {sm.GetSkillRank(a[1])}";
            }
            case "lang": // switch the game's language (en / ru) to check both
            {
                var loc = UnityEngine.Localization.Settings.LocalizationSettings.AvailableLocales.GetLocale(a[1]);
                if (loc == null) return "no such locale";
                UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale = loc;
                return "language " + a[1];
            }
            case "hurt": // damage the player like an enemy hit would
                PlayerStats.Instance.TakeDamage(float.Parse(a[1]), null);
                return $"HP {PlayerStats.Instance.CurrentHealth}";
            case "give": // give an item by asset name (substring match), e.g. give Heal Potion
            {
                var items = ItemDatabaseUtility._database?.allItems;
                string q = string.Join(" ", a.Skip(1));
                for (int i = 0; items != null && i < items.Count; i++)
                    if (items[i] != null && items[i].name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        InventoryManager.Instance.AddItem(items[i], 1, true, null, true);
                        return "gave " + items[i].name;
                    }
                return "not found";
            }
            case "devour": // consume the first carried item matching the name, report the step that fails
            {
                string step = "start";
                try
                {
                    string q = string.Join(" ", a.Skip(1));
                    step = "slots";
                    var slots = FellSellPlus.Inventory.ItemCounts.CarriedSlots().ToList();
                    step = "find";
                    InventorySlot found = null;
                    foreach (var sl in slots)
                    {
                        if (sl == null || sl.item == null) continue;
                        var n = sl.item.name;
                        if (n != null && n.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) { found = sl; break; }
                    }
                    if (found == null) return $"not carried ({slots.Count} slots)";
                    var item = found.item;
                    step = "hint";
                    string hint = FellSellPlus.Skills.Devourer.Hint(item);
                    step = "consume";
                    FellSellPlus.Skills.Devourer.Consume(item);
                    step = "report";
                    var t = string.Join(", ", FellSellPlus.Skills.Devourer.Total.Select(kv => $"{kv.Key}={kv.Value:0.##}"));
                    return $"{hint} || totals: {t} | HP max {PlayerStats.Instance.MaxHealth}";
                }
                catch (Exception e) { return $"failed at {step}: {e}"; }
            }
            case "xp": // add XP through the game's own path and report level and points
            {
                var pm = UnityEngine.Object.FindObjectOfType<PlayerProgressionManager>();
                if (pm == null) return "no progression manager";
                long left = a.Length > 1 ? long.Parse(a[1]) : 1000;
                while (left > 0) { int n = (int)Math.Min(left, 1_000_000_000L); pm.AddXP(n, "dev"); left -= n; }
                return $"level {pm._currentLevel}/{pm._maxLevel} free {pm._availableSkillPoints} earned {pm._totalEarnedSkillPoints}";
            }
            case "skillcost": // measure what the game counts as spent points per rank, per rarity
            {
                var sm = PlayerSkillManager.Instance;
                var sb = new System.Text.StringBuilder();
                var seen = new HashSet<string>();
                int total = 0, sumMax = 0;
                foreach (var sk in sm.GetAllSkills())
                {
                    sumMax += sk.maxRank;
                    string key = sk.rarity.ToString();
                    if (seen.Contains(key + sk.maxRank)) continue;
                    var st = sm.GetSkillState(sk.skillId);
                    if (st == null || st.currentActiveRank != 0) continue;
                    seen.Add(key + sk.maxRank);
                    int before = sm.GetTotalSpentSkillPoints();
                    var costs = new List<int>();
                    int oldCap = st.unlockedCapacity;
                    st.unlockedCapacity = sk.maxRank;
                    for (int r = 1; r <= sk.maxRank; r++) { st.currentActiveRank = r; costs.Add(sm.GetTotalSpentSkillPoints() - before); }
                    st.currentActiveRank = 0; st.unlockedCapacity = oldCap;
                    sb.Append($"{key} max{sk.maxRank}: {string.Join("/", costs)}; ");
                }
                foreach (var sk in sm.GetAllSkills()) total++;
                return $"{total} skills, sum of max ranks {sumMax} | {sb}";
            }
            case "village":
            {
                var menu = UnityEngine.Object.FindObjectOfType<MainPauseMenu>();
                if (menu == null) return "no menu";
                menu.BackToVillage();
                return "going to village";
            }
            case "points":
                return $"skill points {PlayerProgressionManager._instance?.AvailableSkillPoints}";
            case "enemies":
            {
                var sb = new StringBuilder();
                foreach (var h in UnityEngine.Object.FindObjectsOfType<Il2CppEmeraldAI.EmeraldHealth>())
                    sb.AppendLine($"{h.gameObject.name} hp {h.CurrentHealth}/{h.StartingHealth} at {h.transform.position}");
                return sb.ToString();
            }
            case "key": // press a key through the game's input (for keys the harness can't send)
                return "unused";
            case "cards":
            {
                var ui = UnityEngine.Object.FindObjectOfType<PrayersUI>();
                if (ui == null) return "no ui";
                return $"cached {ui._cachedOfferedBoons?.Count} active {ui.activeCards?.Count} evil {ui._isEvilRoll}";
            }
            case "dump": return Dump();
            default: return "unknown command";
        }
    }

    static void Teleport(Vector3 p)
    {
        var fpc = FirstPersonController.Instance;
        var cc = fpc.CharacterController;
        cc.enabled = false;
        fpc.transform.position = p;
        cc.enabled = true;
    }

    static string Dump()
    {
        var sb = new StringBuilder();
        var ps = PlayerStats.Instance;
        if (ps != null)
            sb.AppendLine($"HP {ps.CurrentHealth}/{ps.MaxHealth} ST {ps.CurrentStamina}/{ps.MaxStamina} MP {ps.CurrentMana}/{ps.MaxMana}");
        if (ShopManager.Instance != null)
            sb.AppendLine($"gold {ShopManager.Instance.PlayerGold} prestige {ShopManager.Instance.shopPrestige}");
        var pm = PrayersManager.Instance;
        if (pm != null)
        {
            var list = pm.GetActivePrayers();
            sb.Append("prayers:");
            for (int i = 0; i < list.Count; i++) sb.Append(' ').Append(list[i].name);
            sb.AppendLine();
        }
        var sm = PlayerSkillManager.Instance;
        if (sm != null)
        {
            var all = sm.GetAllSkills();
            sb.Append($"skills in db {all.Count}; ranks:");
            for (int i = 0; i < all.Count; i++)
            {
                int r = sm.GetSkillRank(all[i].skillId);
                if (r > 0 || all[i].skillId.StartsWith("fsp_")) sb.Append($" {all[i].skillId}={r}");
            }
            sb.AppendLine();
        }
        foreach (var s in ProgressionState.All) sb.Append($"{s}:{ProgressionState.Level(s)} ");
        sb.AppendLine($"unlocked={ProgressionState.Unlocked}");
        return sb.ToString();
    }
}
