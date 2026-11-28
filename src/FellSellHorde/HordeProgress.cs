using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FellSellHorde;

// Run progression: gifts on a schedule (a choice of Good or Evil, then the game's own prayer cards
// from that statue's pool) and the bank chest by the pillar, which collects one piece of gear per
// boss and can be opened at any time; a fresh empty one takes its place.
internal static class HordeProgress
{
    internal static int Xp, Level, ChestPoints, Kills;
    static int _pendingChoices;
    static bool _choosing;
    static float _savedTimeScale = 1f;

    // Thresholds grow: 10, 30, 60, 100, 150... (each step 10 more than the last).
    internal static int Threshold(int level) => 10 * (level + 1) + 10 * level * (level + 1) / 2;

    internal static void Reset()
    {
        Xp = Level = ChestPoints = Kills = 0;
        BankItems.Clear();
        _giftedWave = -1;
        _pendingChoices = 0;
        _revealAt = -1f;
        if (_choosing) Unpause();
        _choosing = false;
        HordeChoice.Close();
    }

    // Mad: the chest holds only gear (weapons and armour), one piece per boss killed, better the
    // longer the run lasts; it keeps everything until it is opened.
    internal static void OnKill(int tier, int bonus = 0)
    {
        Kills++;
        if (tier >= 30) AddBankItem(HordeDirector.State.Wave, tier >= 60);
    }

    internal static readonly List<ItemData> BankItems = new();
    static readonly System.Random LootRng = new();

    internal static void AddBankItem(int wave, bool grandma = false)
    {
        var it = RollGear(wave, grandma);
        if (it == null) return;
        BankItems.Add(it);
        ChestPoints = BankItems.Count;
        Plugin.L.Msg($"Horde: chest got {it.name} ({it.rarity}, +{it.upgradeLevel}) at wave {wave}; holds {BankItems.Count}.");
        HordeDirector.ShowToast(HordeDirector.Ru ? $"В сундук: {it.name.Replace("_Data", "")}" : $"Into the chest: {it.name.Replace("_Data", "")}");
    }

    // Rarity by how far the run went; upgraded versions appear later on.
    static ItemData RollGear(int wave, bool grandma)
    {
        double r = LootRng.NextDouble();
        ItemRarity rar =
            wave < 20 ? (r < 0.5 ? ItemRarity.Uncommon : ItemRarity.Rare) :
            wave < 35 ? (r < 0.6 ? ItemRarity.Rare : ItemRarity.Epic) :
            wave < 50 ? (r < 0.2 ? ItemRarity.Rare : r < 0.9 ? ItemRarity.Epic : ItemRarity.Legendary) :
            wave < 70 ? (r < 0.6 ? ItemRarity.Epic : ItemRarity.Legendary) :
                        (r < 0.3 ? ItemRarity.Epic : ItemRarity.Legendary);
        if (grandma && rar < ItemRarity.Legendary) rar++;
        int up = Mathf.Clamp((wave - 10) / 15, 0, 3);
        var db = ItemDatabaseUtility._database;
        if (db?.allItems == null) return null;
        for (; rar >= ItemRarity.Common; rar--)
            for (int u = up; u >= 0; u--)
            {
                var pool = new List<ItemData>();
                for (int i = 0; i < db.allItems.Count; i++)
                {
                    var it = db.allItems[i];
                    if (it == null || it.rarity != rar || it.upgradeLevel != u || !(it.IsWeapon || it.IsArmor)) continue;
                    if (it.name != null && it.name.StartsWith("FSB_")) continue; // our bows only from shops/drops
                    pool.Add(it);
                }
                if (pool.Count > 0) return pool[LootRng.Next(pool.Count)];
            }
        return null;
    }

    // Mad: gifts after waves 1, 3, 6 and 10 (the first boss), then after every boss wave and once
    // between bosses (13, 15, 18, 20, 23, 25...).
    internal static bool IsGiftWave(int w) => w == 1 || w == 3 || w == 6 || w == 10 || (w > 10 && (w % 5 == 0 || w % 5 == 3));

    internal static int NextGiftWave(int after)
    {
        for (int w = after + 1; w < after + 20; w++) if (IsGiftWave(w)) return w;
        return after + 1;
    }

    static int _giftedWave = -1;

    internal static void OnWaveCleared(int wave)
    {
        if (!IsGiftWave(wave) || _giftedWave == wave) return;
        _giftedWave = wave;
        Level++; _pendingChoices++;
    }

    // Called every frame by the director (authority and clients alike: the choice is per player).
    internal static void Tick()
    {
        bool pauseUp = PauseMenuUp();
        HordeChoice.Suppressed = _choosing && pauseUp;
        SuppressCards(_choosing && pauseUp);
        HordeChoice.Tick();
        if (_choosing)
        {
            if (pauseUp) return; // nothing reopens or closes under the pause menu
            bool cardsOpen = PrayersUI.Instance != null && PrayersUI.Instance.IsOpen;
            // Esc (the game's window stack) closes the top window as a cancel: in horde a gift cannot
            // be refused, so a choice closed without a pick comes back.
            if (!_boonTaken && !HordeChoice.IsOpen && !cardsOpen && Time.unscaledTime > _cardsOpenedAt + 0.5f && Time.unscaledTime > _sideOpenedAt + 0.5f)
            {
                if (_lastBoons == null) { _sideOpenedAt = Time.unscaledTime; HordeChoice.Open(_sideLevel, OnSide); }
                else
                {
                    try
                    {
                        if (_lastStatue != null) _lastStatue.hasPrayed = false;
                        _cardsOpenedAt = Time.unscaledTime;
                        PrayersUI.Instance.OpenUI(_lastStatue, _lastBoons, _lastTitle);
                    }
                    catch (Exception e) { Plugin.L.Error("Horde: reopening the gift cards failed: " + e.Message); _boonTaken = true; }
                }
                Plugin.L.Msg("Horde: the gift choice was closed without a pick; reopened.");
                return;
            }
            if (!HordeChoice.IsOpen && !cardsOpen && Time.unscaledTime > _cardsOpenedAt + 0.5f)
            {
                _choosing = false;
                Unpause();
                HordeDirector.RestoreStatues();
            }
            return;
        }
        if (_pendingChoices <= 0) { _revealAt = -1f; return; }
        if (PrayersUI.Instance != null && PrayersUI.Instance.IsOpen) return;
        // "New level" lands on screen like a wave banner; the choice follows a moment later.
        if (_revealAt < 0f)
        {
            _revealAt = Time.unscaledTime + RevealDelay;
            int shown = Level - _pendingChoices + 1;
            HordeHud.Announce(HordeDirector.Ru ? "НОВЫЙ УРОВЕНЬ" : "NEW LEVEL",
                HordeDirector.Ru ? $"уровень {shown}  ·  выбери сторону" : $"level {shown}  ·  choose a side", "level");
            return;
        }
        if (Time.unscaledTime < _revealAt) return;
        _revealAt = -1f;
        _pendingChoices--;
        _choosing = true;
        _boonTaken = false; _lastBoons = null; _lastStatue = null;
        _sideLevel = Level - _pendingChoices;
        _sideOpenedAt = Time.unscaledTime;
        Pause();
        HordeChoice.Open(_sideLevel, OnSide);
    }

    internal static bool PauseMenuUp()
    {
        var wm = WindowsManager.Instance;
        var p = wm != null ? wm.mainPauseMenuPanel : null;
        return p != null && p.activeInHierarchy;
    }

    // The game's prayer cards hide (not close) while the pause menu is open.
    static bool _cardsHidden;
    static float _cardsAlpha = 1f;
    static void SuppressCards(bool hide)
    {
        if (hide == _cardsHidden) return;
        var ui = PrayersUI.Instance;
        if (ui == null) { _cardsHidden = false; return; }
        var cg = ui.gameObject.GetComponent<CanvasGroup>() ?? ui.gameObject.AddComponent<CanvasGroup>();
        if (hide) { _cardsAlpha = cg.alpha; cg.alpha = 0f; cg.blocksRaycasts = false; cg.interactable = false; }
        else { cg.alpha = _cardsAlpha; cg.blocksRaycasts = true; cg.interactable = true; }
        _cardsHidden = hide;
    }

    static float _cardsOpenedAt, _sideOpenedAt;
    internal static bool _boonTaken;
    static int _sideLevel;
    static PrayersStatue _lastStatue;
    static Il2CppSystem.Collections.Generic.List<PrayerData> _lastBoons;
    static string _lastTitle;
    static float _revealAt = -1f;
    internal static bool Busy => _pendingChoices > 0 || _choosing;
    internal static bool Choosing => _choosing;
    const float RevealDelay = 1.5f;

    static void OnSide(bool evil)
    {
        var statue = HordeDirector.StatueFor(evil);
        if (statue == null || PrayersUI.Instance == null) { _boonTaken = true; Plugin.L.Warning("Horde: no statue / prayer UI for the level-up choice."); return; }
        try
        {
            statue.hasPrayed = false;
            var boons = statue.RollBoons(3);
            if (boons == null || boons.Count == 0)
            {
                // Every gift of this side is already taken: the chest gets an extra item instead.
                AddBankItem(HordeDirector.State.Wave + 10);
                _boonTaken = true;
                HordeDirector.ShowToast(HordeDirector.Ru ? "Все дары этой стороны уже твои: сундук пополнен" : "You hold every gift of this side: the chest is filled instead");
                return;
            }
            string title = HordeDirector.Ru ? $"Уровень {Level}" : $"Level {Level}";
            _cardsOpenedAt = Time.unscaledTime;
            _lastStatue = statue; _lastBoons = boons; _lastTitle = title;
            PrayersUI.Instance.OpenUI(statue, boons, title);
        }
        catch (Exception e) { _boonTaken = true; Plugin.L.Error("Horde: prayer cards failed: " + e); }
    }

    static void Pause()
    {
        if (!HordeSettings.PauseOnChoice.Value || !HordeNet.Current.IsAuthority || HordeNet.Current.PlayerCount > 1) return;
        _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
    }

    static void Unpause()
    {
        if (Time.timeScale == 0f) Time.timeScale = _savedTimeScale;
    }

    // ---------- chest bank ----------

    // The chest's level (its model and glow) follows the wave.
    internal static int ChestLevel => Mathf.Clamp(HordeDirector.State.Wave, 1, 40);
}

// The bank chest pays out what it holds: filled right before it opens, then replaced.
[HarmonyPatch(typeof(DungeonChest), nameof(DungeonChest.Interact))]
internal static class BankChestPatch
{
    static bool Prefix(DungeonChest __instance, out bool __state)
    {
        __state = false;
        if (!HordeGen.Active || !HordeDirector.IsBank(__instance)) return true;
        if (HordeProgress.BankItems.Count == 0)
        {
            HordeDirector.ShowToast(HordeDirector.Ru ? "Сундук пуст: за каждого убитого босса в нём появляется предмет" : "The chest is empty: every boss you kill puts an item in it");
            return false;
        }
        HordeDirector.FillBank(__instance);
        __state = true;
        return true;
    }

    // Harmony runs postfixes even when the prefix skipped the original: replace only real openings.
    static void Postfix(bool __state)
    {
        if (!__state) return;
        HordeProgress.BankItems.Clear();
        HordeProgress.ChestPoints = 0;
        HordeDirector.ReplaceBankLater();
    }
}

// Enemies hit harder and attack faster as the run goes on (on top of the game's difficulty).
[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.IncomingDamageMultiplier), MethodType.Getter)]
internal static class HordeDamagePatch
{
    static void Postfix(ref float __result)
    {
        if (!HordeGen.Active) return;
        __result *= HordeBalance.DamageMul(Mathf.Max(1, HordeDirector.State.Wave));
    }
}

[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.EnemyAttackCooldownMultiplier), MethodType.Getter)]
internal static class HordeCooldownPatch
{
    static void Postfix(ref float __result)
    {
        if (!HordeGen.Active) return;
        __result *= Mathf.Max(0.6f, 1f - 0.005f * Mathf.Max(0, HordeDirector.State.Wave - 1));
    }
}

// "Good or Evil?" before the cards: two cards on the game's own prayer card faces (white for good,
// red for evil), placed above the screen centre where the cursor sits, and inert for a moment after
// they appear so a click meant for the fight never picks one.
internal static class HordeChoice
{
    static GameObject _root;
    static bool _builtRu;
    static Action<bool> _onPick;
    static TextMeshProUGUI _title;
    static Button _good, _evil;
    static CanvasGroup _group;
    internal static bool Suppressed;
    static RectTransform _goodRt, _evilRt;
    static float _openedAt;
    const float InputLock = 0.95f; // the cards are in place by then
    internal static bool IsOpen => _root != null && _root.activeSelf;

    static readonly Color Ink = new(0.93f, 0.89f, 0.80f, 1f);
    static readonly Color Muted = new(0.75f, 0.70f, 0.60f, 1f);
    static readonly Color DarkInk = new(0.18f, 0.12f, 0.06f, 1f);

    internal static void Open(int level, Action<bool> onPick)
    {
        _onPick = onPick;
        bool ru = HordeDirector.Ru;
        if (_root != null && _builtRu != ru) { UnityEngine.Object.Destroy(_root); _root = null; }
        if (_root == null) { Build(ru); _builtRu = ru; }
        if (_root == null) { onPick(false); return; }
        _title.text = ru ? $"Дар {level}" : $"Gift {level}";
        _openedAt = Time.unscaledTime;
        if (_good != null) _good.interactable = _evil.interactable = false;
        _root.SetActive(true);
        var wm = WindowsManager.Instance;
        try { if (wm != null && !wm.IsWindowInStack(_root)) { wm.RegisterWindow(_root); wm.OpenWindow(_root); } } catch { }
    }

    // Unscaled: the game is paused while the choice is open.
    internal static void Tick()
    {
        if (!IsOpen) return;
        float t = Time.unscaledTime - _openedAt;
        // Dim and title fade in; the cards rise and grow in one after the other.
        _group.alpha = Suppressed ? 0f : Mathf.Clamp01(t / 0.45f);
        _group.blocksRaycasts = !Suppressed;
        if (Suppressed) { _openedAt += Time.unscaledDeltaTime; return; } // the entrance waits for the menu to close
        float gap = Mathf.Max(190f, _goodRt.rect.width * 0.5f + 30f);
        Enter(_goodRt, -gap, t - 0.10f);
        Enter(_evilRt, gap, t - 0.22f);
        if (_good != null && t >= InputLock && !_good.interactable) _good.interactable = _evil.interactable = true;
    }

    static void Enter(RectTransform rt, float x, float t)
    {
        float k = Mathf.Clamp01(t / 0.6f);
        float e = 1f - Mathf.Pow(1f - k, 3f);
        float over = k < 1f ? Mathf.Sin(k * Mathf.PI) * 0.04f : 0f; // a slight swell at the end
        rt.anchoredPosition = new Vector2(x, -90f * (1f - e));
        rt.localScale = Vector3.one * (0.82f + 0.18f * e + over);
        var cg = rt.GetComponent<CanvasGroup>() ?? rt.gameObject.AddComponent<CanvasGroup>();
        cg.alpha = Mathf.Clamp01(t / 0.3f);
    }

    internal static void Close()
    {
        if (_root == null || !_root.activeSelf) return;
        var wm = WindowsManager.Instance;
        try { if (wm != null) { wm.CloseWindow(_root); wm.UnregisterWindow(_root); } } catch { }
        _root.SetActive(false);
    }

    static void Pick(bool evil)
    {
        if (Time.unscaledTime - _openedAt < InputLock) return;
        Close();
        _onPick?.Invoke(evil);
    }

    static Sprite CardFace(bool evil)
    {
        var ui = PrayersUI.Instance;
        var prefab = ui == null ? null : (evil ? ui.evilPrayerCardPrefab : ui.prayerCardPrefab);
        if (prefab == null) return null;
        var img = prefab.GetComponent<Image>() ?? prefab.GetComponentInChildren<Image>(true);
        return img != null ? img.sprite : null;
    }

    // The game's own prayer card (frame, glow, hover motion) with our words on it; clicks on it are
    // routed to Pick by the PrayerCard patches below. Falls back to a plain card if the prefab is missing.

    static GameObject GameCard(Transform parent, bool evil, bool ru)
    {
        var ui = PrayersUI.Instance;
        var prefab = ui == null ? null : (evil ? ui.evilPrayerCardPrefab : ui.prayerCardPrefab);
        if (prefab == null) return null;
        var go = UnityEngine.Object.Instantiate(prefab, parent, false);
        go.name = evil ? "EvilCard" : "GoodCard";
        var card = go.GetComponent<PrayerCard>();
        if (card != null)
        {
            if (card.nameText != null) card.nameText.text = evil ? (ru ? "ЗЛО" : "EVIL") : (ru ? "ДОБРО" : "GOOD");
            if (card.descText != null) card.descText.text = evil
                ? (ru ? "Сила с платой.\nДары зла сильнее, но каждый что-то забирает." : "Power with a price.\nEvil gifts are stronger, and each takes something.")
                : (ru ? "Дары без цены.\nЧистая помощь, без платы." : "Gifts without a price.\nPure help, nothing taken.");
            var icon = SideIcon(evil);
            if (card.iconImage != null && icon != null) card.iconImage.sprite = icon;
            try { card.OnSpawnAnimationComplete(); } catch { }
        }
        foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true)) { cg.alpha = 1f; }
        go.transform.localScale = Vector3.one;
        // A transparent button over the whole card takes the click, so the card's own prayer code
        // never sees it (patching PrayerCard's pointer handlers crashed the game).
        var hit = HordeWindow.Rect("Hit", go.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var img = hit.gameObject.AddComponent<Image>();
        img.color = new Color(0, 0, 0, 0);
        var btn = hit.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener((UnityAction)new Action(() => Pick(evil)));
        return go;
    }

    // An icon of that side: the first good or evil prayer in the game's prayer list.
    static Sprite SideIcon(bool evil)
    {
        var db = PrayersManager.Instance?.allPrayersDatabase;
        for (int i = 0; db != null && i < db.Count; i++)
            if (db[i] != null && db[i].isEvil == evil && db[i].icon != null) return db[i].icon;
        return null;
    }


    static Button Card(Transform parent, TMP_FontAsset font, bool evil, bool ru, out RectTransform rt)
    {
        rt = HordeWindow.Rect(evil ? "EvilCard" : "GoodCard", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300, 400));
        var img = rt.gameObject.AddComponent<Image>();
        var face = CardFace(evil);
        if (face != null) { img.sprite = face; img.type = Image.Type.Sliced; img.color = Color.white; }
        else img.color = evil ? new Color(0.50f, 0.07f, 0.06f, 1f) : new Color(0.93f, 0.89f, 0.78f, 1f);
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var colors = b.colors;
        colors.disabledColor = new Color(0.75f, 0.75f, 0.75f, 0.85f);
        b.colors = colors;
        Color ink = evil ? Ink : DarkInk;
        var word = HordeWindow.Text(rt, "Word", font, evil ? (ru ? "ЗЛО" : "EVIL") : (ru ? "ДОБРО" : "GOOD"), 54, ink, TextAlignmentOptions.Center);
        HordeWindow.Place(word.rectTransform, 0, 40, 260, 80);
        word.characterSpacing = 10f;
        var line = HordeWindow.Text(rt, "Line", font,
            evil ? (ru ? "сила с платой" : "power with a price") : (ru ? "дары без цены" : "gifts without a price"),
            24, ink, TextAlignmentOptions.Center);
        HordeWindow.Place(line.rectTransform, 0, -30, 250, 60);
        line.enableWordWrapping = true;
        return b;
    }

    static void Build(bool ru)
    {
        try
        {
            var font = FirstPersonController.Instance?.interactionText?.font;
            _root = new GameObject("Horde_LevelChoice");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 520;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();
            _group = _root.AddComponent<CanvasGroup>();
            var dim = HordeWindow.Rect("Dim", _root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

            // The group sits high: the screen centre (the cursor's spot) stays empty.
            var group = HordeWindow.Rect("Group", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 230), new Vector2(900, 560));
            _title = HordeWindow.Text(group, "Title", font, "", 46, Ink, TextAlignmentOptions.Center);
            HordeWindow.Place(_title.rectTransform, 0, 20, 900, 64, top: true);
            var sub = HordeWindow.Text(group, "Sub", font, ru ? "кому помолиться?" : "whom do you pray to?", 24, Muted, TextAlignmentOptions.Center);
            HordeWindow.Place(sub.rectTransform, 0, 76, 900, 34, top: true);
            sub.characterSpacing = 6f;

            var cards = HordeWindow.Rect("Cards", group, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -150), new Vector2(900, 420));
            var gc = GameCard(cards, false, ru);
            var ec = GameCard(cards, true, ru);
            if (gc != null && ec != null)
            {
                _goodRt = gc.GetComponent<RectTransform>();
                _evilRt = ec.GetComponent<RectTransform>();
                _goodRt.anchorMin = _goodRt.anchorMax = _evilRt.anchorMin = _evilRt.anchorMax = new Vector2(0.5f, 0.5f);
            }
            else
            {
                _good = Card(cards, font, false, ru, out _goodRt);
                _good.onClick.AddListener((UnityAction)new Action(() => Pick(false)));
                _evil = Card(cards, font, true, ru, out _evilRt);
                _evil.onClick.AddListener((UnityAction)new Action(() => Pick(true)));
            }
            _root.SetActive(false);
        }
        catch (Exception e)
        {
            Plugin.L.Error("Horde level choice window failed: " + e);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }
}

// In horde runs prayers stack without a slot limit: the chosen card is added directly, never
// sent to the replacement screen. Balance comes from enemy growth (waves and levels taken).
[HarmonyPatch(typeof(PrayersUI), nameof(PrayersUI.SelectBoon))]
internal static class UnlimitedPrayersPatch
{
    static bool Prefix(PrayersUI __instance, PrayerData chosenBoon)
    {
        if (!HordeGen.Active || chosenBoon == null) return true;
        HordeProgress._boonTaken = true; // a pick, whichever path adds it
        try
        {
            PrayersManager.Instance.AddPrayer(chosenBoon);
            HordeProgress._boonTaken = true;
            Plugin.L.Msg($"Horde: prayer taken {chosenBoon.name} (level {HordeProgress.Level}, active {PrayersManager.Instance.GetActivePrayers()?.Count}).");
            __instance.StartCoroutine(__instance.HideUIRoutine());
        }
        catch (Exception e) { Plugin.L.Error("Horde: adding the prayer failed, falling back to the game: " + e.Message); return true; }
        return false;
    }
}

// Horde statues are decoration: the level-up choice uses their pools, the player cannot pray there.
[HarmonyPatch(typeof(PrayersStatue), nameof(PrayersStatue.Interact))]
internal static class HordeStatueOffPatch
{
    static bool Prefix() => !HordeGen.Active;
}



// Esc closes the top of the game's window stack. With a gift choice open the pause menu sits
// above it on screen but below it in the stack, so Esc used to close the gift. Mad: Esc closes the
// pause menu first; the gift choice comes back after it.
[HarmonyPatch(typeof(WindowsManager), nameof(WindowsManager.HandlePauseInput))]
internal static class GiftPauseEscPatch
{
    static bool Prefix(WindowsManager __instance)
    {
        if (!HordeGen.Active || !HordeProgress.Choosing) return true;
        var p = __instance.mainPauseMenuPanel;
        if (p == null) return true;
        try
        {
            // Esc never closes the gift: it opens the pause menu over it, and the next Esc closes the menu.
            if (HordeProgress.PauseMenuUp())
            {
                if (__instance.IsWindowInStack(p)) __instance.CloseWindow(p); else p.SetActive(false);
            }
            else __instance.OpenWindow(p);
        }
        catch (Exception e) { Plugin.L.Warning("Horde: pause menu over a gift failed: " + e.Message); return true; }
        return false;
    }
}
