using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Injection;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FellSellHorde;

// The Reaper statue by the shop gate: the way into horde mode. It is a real IInteractable
// registered into IL2CPP, so the game's own look-at prompt and E key drive it.
public class HordeStatue : MonoBehaviour
{
    public HordeStatue(IntPtr ptr) : base(ptr) { }
    public void Interact() => HordeWindow.Open();
    public string GetActionText() => HordeDirector.Ru ? "Бросить вызов" : "Challenge";
    public string GetObjectText() => HordeDirector.Ru ? "Статуя Жнеца: бесконечная орда" : "Reaper Statue: Endless Horde";
}

// The Start/Continue pillar in the arena: a real IInteractable, so the game's look raycast picks
// either it or the chest, never both on one E press.
public class HordePillar : MonoBehaviour
{
    public HordePillar(IntPtr ptr) : base(ptr) { }
    public void Interact() => HordeDirector.PillarPressed();
    public string GetActionText() => HordeDirector.PillarActionText();
    public string GetObjectText() => HordeDirector.Ru ? "Алтарь орды" : "Horde altar";
}

internal static class HordeStatuePlacer
{
    internal static bool Registered;
    static GameObject _statue;
    static float _nextTry;
    static bool _failLogged;

    internal static void Register()
    {
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<HordeStatue>(new RegisterTypeOptions
            {
                Interfaces = new Il2CppInterfaceCollection(new[] { typeof(IInteractable) }),
            });
            ClassInjector.RegisterTypeInIl2Cpp<HordePillar>(new RegisterTypeOptions
            {
                Interfaces = new Il2CppInterfaceCollection(new[] { typeof(IInteractable) }),
            });
            Registered = true;
        }
        catch (Exception e) { Plugin.L.Error("Statue type injection failed, no statue in the village: " + e); }
    }

    // Called every frame in the village; places the statue once the scene and the player exist.
    internal static void Tick()
    {
        if (!Registered || _statue != null || Time.unscaledTime < _nextTry) return;
        _nextTry = Time.unscaledTime + 1f;
        if (FirstPersonController.Instance == null) return;
        try { _statue = Place(); _failLogged = false; }
        catch (Exception e) { if (!_failLogged) Plugin.L.Error("Statue placement failed: " + e); _failLogged = true; }
    }

    static GameObject Place()
    {
        if (!TryAnchor(out var pos, out var yaw)) return null;
        var root = new GameObject("Horde_ReaperStatue");
        root.transform.position = pos;
        root.transform.rotation = Quaternion.Euler(0, yaw, 0);

        var item = ItemDatabaseUtility.GetItemByName("Reaper Statue");
        if (item?.prefab != null)
        {
            var model = UnityEngine.Object.Instantiate(item.prefab, root.transform);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            StripToVisual(model);
        }
        else Plugin.L.Warning("Reaper Statue item not found: the statue is invisible, only its prompt works.");

        // Interaction box on the layer the player's look raycast sees.
        var hit = new GameObject("interact");
        hit.transform.SetParent(root.transform, false);
        hit.layer = InteractionLayer();
        var b = Bounds(root);
        hit.transform.position = b.size.sqrMagnitude > 0.01f ? b.center : pos + Vector3.up * 1f;
        var box = hit.AddComponent<BoxCollider>();
        box.size = b.size.sqrMagnitude > 0.01f ? Vector3.Max(b.size, new Vector3(0.8f, 2f, 0.8f)) : new Vector3(1f, 2.2f, 1f);
        hit.AddComponent<HordeStatue>();
        Plugin.L.Msg($"Reaper statue placed at {pos} yaw {yaw:0} (layer {hit.layer}). To move it, set FellSellHorde.StatuePosition = \"x, y, z, yaw\".");
        return root;
    }

    // A furniture prefab carries pickup and placement scripts: keep only what draws.
    static void StripToVisual(GameObject go)
    {
        var kill = new List<Component>();
        foreach (var c in go.GetComponentsInChildren<Component>(true))
        {
            if (c == null) continue;
            if (c.TryCast<Transform>() != null || c.TryCast<MeshFilter>() != null || c.TryCast<Renderer>() != null || c.TryCast<LODGroup>() != null) continue;
            if (c.TryCast<Collider>() != null) { var col = c.TryCast<Collider>(); col.isTrigger = false; continue; } // solid statue
            kill.Add(c);
        }
        foreach (var c in kill) UnityEngine.Object.DestroyImmediate(c);
    }

    static Bounds Bounds(GameObject root)
    {
        var rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return default;
        var b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    static bool TryAnchor(out Vector3 pos, out float yaw)
    {
        pos = default; yaw = 0;
        var cfg = HordeSettings.StatuePosition.Value;
        if (!string.IsNullOrWhiteSpace(cfg))
        {
            var p = cfg.Split(',');
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            if (p.Length >= 3 && float.TryParse(p[0], System.Globalization.NumberStyles.Float, inv, out var x)
                && float.TryParse(p[1], System.Globalization.NumberStyles.Float, inv, out var y)
                && float.TryParse(p[2], System.Globalization.NumberStyles.Float, inv, out var z))
            {
                pos = new Vector3(x, y, z);
                if (p.Length >= 4) float.TryParse(p[3], System.Globalization.NumberStyles.Float, inv, out yaw);
                return true;
            }
        }
        // Default: beside the fence gate nearest to the shop's main door, 2.5 m to its side.
        var gate = ShopGate();
        if (gate == null) return false;
        var t = gate.transform;
        pos = Ground(t.position + t.right * 2.5f);
        yaw = t.eulerAngles.y;
        return true;
    }

    static Transform ShopGate()
    {
        Vector3? shop = null;
        var doors = Door.AllDoors;
        for (int i = 0; doors != null && i < doors.Count; i++)
            if (doors[i] != null && doors[i]._isMainShopDoor) { shop = doors[i].transform.position; break; }
        Transform best = null; float bd = float.MaxValue;
        foreach (var tr in UnityEngine.Object.FindObjectsOfType<Transform>())
        {
            var n = tr.name;
            if (!System.Text.RegularExpressions.Regex.IsMatch(n, @"^SM_Prop_Meadow_Fence_Gate_Set_\d+$")) continue; // the gate set, not its leaf or latch
            float d = shop.HasValue ? (tr.position - shop.Value).sqrMagnitude : 0f;
            if (d < bd) { bd = d; best = tr; }
        }
        return best;
    }

    // The ground under a point, ignoring the fence itself and triggers.
    static Vector3 Ground(Vector3 p)
    {
        var hits = Physics.RaycastAll(p + Vector3.up * 6f, Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue; Vector3 at = p;
        foreach (var h in hits)
        {
            var n = h.collider.gameObject.name;
            if (n.Contains("Fence") || n.Contains("Gate")) continue;
            if (h.distance < best) { best = h.distance; at = h.point; }
        }
        return at;
    }

    static int InteractionLayer()
    {
        var fpc = FirstPersonController.Instance;
        int mask = fpc != null ? fpc.interactionLayer.value : 0;
        for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
        return 0;
    }

    internal static void OnSceneChanged() => _statue = null;
}

// The horde settings window, styled from the game's own assets like Fell & Sell Plus windows.
internal static class HordeWindow
{
    static GameObject _root;
    static bool _builtRu, _open;
    static int _openedFrame;
    static readonly List<Action> Refreshers = new();
    static TextMeshProUGUI _status;
    static TextMeshProUGUI _startLabel, _inviteLabel, _inviteNote;
    static Button _invite;
    static bool _reopen, _inviteFailed;

    static readonly Color Ink = new(0.93f, 0.89f, 0.80f, 1f);
    static readonly Color Muted = new(0.70f, 0.65f, 0.56f, 1f);
    static readonly Color Blood = new(0.85f, 0.35f, 0.30f, 1f);
    static readonly Color PanelFallback = new(0.09f, 0.075f, 0.06f, 0.96f);
    static bool Ru => HordeDirector.Ru;

    // Difficulty: health growth per wave, extra enemies per wave, damage growth per wave.
    internal static readonly (string ru, string en, float hp, int perWave, float dmg)[] Difficulties =
    {
        ("Лёгкая", "Easy", 4f, 1, 2f), ("Обычная", "Normal", 6f, 2, 3f), ("Тяжёлая", "Hard", 10f, 3, 4f), ("Безумная", "Insane", 15f, 4, 6f),
    };
    static readonly int[] BlockOptions = { 5, 10, 15, 20 };
    static readonly int[] MaxAliveOptions = { 30, 50 };

    static readonly Color Gold = new(1f, 0.80f, 0.38f, 1f);

    internal static void Open()
    {
        if (_open) return;
        if (_root != null && _builtRu != Ru) { UnityEngine.Object.Destroy(_root); _root = null; }
        if (_root == null) { Build(); _builtRu = Ru; }
        if (_root == null) return;
        _open = true;
        _openedFrame = Time.frameCount;
        _root.SetActive(true);
        var wm = WindowsManager.Instance;
        try { if (wm != null && !wm.IsWindowInStack(_root)) { wm.RegisterWindow(_root); wm.OpenWindow(_root); } }
        catch (Exception e) { Plugin.L.Warning("Window open failed: " + e.Message); }
        Refresh();
    }

    internal static void Close()
    {
        if (!_open) return;
        _open = false;
        var wm = WindowsManager.Instance;
        try { if (wm != null && _root != null) { wm.CloseWindow(_root); wm.UnregisterWindow(_root); } }
        catch (Exception e) { Plugin.L.Warning("Window close failed: " + e.Message); }
        if (_root != null) _root.SetActive(false);
    }

    internal static void Tick()
    {
        // Back from the co-op window, a frame later so the Esc that closed it is spent.
        if (_reopen) { _reopen = false; Open(); return; }
        if (!_open) return;
        if (_root == null || !_root.activeSelf) { _open = false; return; }
        if (Time.frameCount == _openedFrame) return;
        var kb = Keyboard.current;
        if (kb != null && kb.eKey.wasPressedThisFrame) Close();
    }

    static int DifficultyIndex()
    {
        for (int i = 0; i < Difficulties.Length; i++)
            if (Mathf.Approximately(Difficulties[i].hp, HordeSettings.HealthPercentPerWave.Value) && Difficulties[i].perWave == HordeSettings.EnemiesPerWave.Value) return i;
        return 1;
    }

    static void SetDifficulty(int i)
    {
        HordeSettings.HealthPercentPerWave.Value = Difficulties[i].hp;
        HordeSettings.EnemiesPerWave.Value = Difficulties[i].perWave;
        HordeSettings.DamagePercentPerWave.Value = Difficulties[i].dmg;
        MelonPreferences.Save();
        Refresh();
    }

    static string DifficultyText(int i)
    {
        var d = Difficulties[i];
        return Ru
            ? $"после 10-й волны +{d.perWave} {Plural(d.perWave, "враг", "врага", "врагов")} за волну  ·  здоровье врагов +{d.hp:0}% и урон +{d.dmg:0}% за волну"
            : $"after wave 10: +{d.perWave} enem{(d.perWave == 1 ? "y" : "ies")} per wave  ·  enemy health +{d.hp:0}% and damage +{d.dmg:0}% per wave";
    }

    static string Plural(int n, string one, string few, string many)
    {
        int m10 = n % 10, m100 = n % 100;
        if (m10 == 1 && m100 != 11) return one;
        if (m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14)) return few;
        return many;
    }

    static void Cycle<T>(T[] opts, T current, int dir, Action<T> set)
    {
        int i = Array.IndexOf(opts, current);
        if (i < 0) i = 0; else i = (i + dir + opts.Length) % opts.Length;
        set(opts[i]);
        MelonPreferences.Save();
        Refresh();
    }

    static void Refresh()
    {
        foreach (var r in Refreshers) r();
        _status.text = HordeGen.Armed
            ? (Ru ? "Вызов принят. Войди в подземелье: тебя встретит зал первого босса." : "Challenge accepted. Enter the dungeon: the first boss's hall awaits.")
            : "";
        _status.color = Blood;
        _startLabel.text = HordeGen.Armed ? (Ru ? "Отказаться" : "Withdraw") : (Ru ? "Принять вызов" : "Accept the challenge");
        bool coop = HordeCoop.Invite != null;
        _invite.interactable = coop;
        _inviteLabel.color = coop ? Ink : Muted;
        _inviteNote.text = _inviteFailed ? (Ru ? "окно кооператива выключено в настройках Co-op" : "the co-op window is switched off in the Co-op settings")
            : coop ? (Ru ? "друг войдёт в орду вместе с тобой" : "your friend enters the horde with you")
            : (Ru ? "нужно дополнение Fell & Sell Coop" : "needs the Fell & Sell Coop add-on");
    }

    // The co-op mod's own window takes over (host a lobby, Steam friends with Invite); this one
    // comes back when it closes.
    static void InvitePressed()
    {
        var open = HordeCoop.Invite;
        if (open == null) return;
        Close();
        bool shown = false;
        try { shown = open(() => _reopen = true); }
        catch (Exception e) { Plugin.L.Warning("Co-op invite window failed: " + e.Message); }
        if (shown) { Plugin.L.Msg("Co-op invite window opened from the statue."); return; }
        _inviteFailed = true;
        Open();
    }

    static void Build()
    {
        try
        {
            Refreshers.Clear();
            var font = FirstPersonController.Instance?.interactionText?.font;
            var shrine = GrandmaShrineUI._instance;
            var panelImg = shrine?._dialogWindow?.GetComponent<Image>();
            var buttonImg = shrine?._actionButton?.image;

            _root = new GameObject("Horde_Window");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            var dim = Rect("Dim", _root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            dim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

            const float W = 940, H = 760;
            var panel = Rect("Panel", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(W, H));
            var bg = panel.gameObject.AddComponent<Image>();
            if (panelImg != null && panelImg.sprite != null) { bg.sprite = panelImg.sprite; bg.type = panelImg.type; bg.color = panelImg.color; }
            else bg.color = PanelFallback;

            var title = Text(panel, "Title", font, Ru ? "Бесконечная орда" : "Endless Horde", 44, Ink, TextAlignmentOptions.Center);
            Place(title.rectTransform, 0, 46, W, 62, top: true);

            var intro = Text(panel, "Intro", font, Ru
                ? "Зал первого босса, только без босса. Встань у алтаря в центре и начни: первая волна это один враг, каждая следующая больше и злее прежней.\n" +
                  "Дары приходят после 1-й, 3-й и 6-й волны, после каждого босса и между боссами: выбери Добро или Зло, затем сам дар. Дары копятся без предела.\n" +
                  $"Сундук у алтаря копит награду с каждого убийства, открывай его когда захочешь. Раз в несколько волн передышка: решётка поднимается, и можно уйти через портал с добычей. Первый босс приходит на 10-й волне, дальше каждые {HordeSettings.BossEvery.Value}: сначала боссы первого биома, потом второго и третьего, а с 70-й волны все вперемешку вместе с бабушкой."
                : "The first boss's hall, without the boss. Stand at the altar in the centre and begin: the first wave is a single enemy, each next one bigger and meaner.\n" +
                  "Gifts come after waves 1, 3 and 6, after every boss and between bosses: choose Good or Evil, then the gift. Gifts stack without limit.\n" +
                  $"The chest by the altar fills with every kill; open it whenever you like. Every few waves comes a respite: the gate rises and you may leave through the portal with your loot. The first boss comes on wave 10, then every {HordeSettings.BossEvery.Value}: the first biome's bosses, then the second's and third's, and from wave 70 all of them mixed, Grandma included.",
                19, Muted, TextAlignmentOptions.TopLeft);
            Place(intro.rectTransform, 0, 170, W - 110, 170, top: true);
            intro.enableWordWrapping = true;
            intro.enableAutoSizing = true; intro.fontSizeMin = 15; intro.fontSizeMax = 19;

            // Difficulty: four segments, the chosen one lit, and what it changes spelled out.
            var dl = Text(panel, "DiffLabel", font, Ru ? "Сложность" : "Difficulty", 25, Ink, TextAlignmentOptions.Left);
            Place(dl.rectTransform, -W / 2 + 55 + 150, 290, 300, 34, top: true);
            for (int i = 0; i < Difficulties.Length; i++)
            {
                int idx = i;
                var b = MakeButton(panel, font, buttonImg, Ru ? Difficulties[i].ru : Difficulties[i].en, out var lbl);
                var rt = b.GetComponent<RectTransform>();
                Place(rt, -306 + 204 * i, 340, 190, 50, top: true);
                b.onClick.AddListener((UnityAction)new Action(() => SetDifficulty(idx)));
                var img = b.GetComponent<Image>();
                var baseColor = img.color;
                Refreshers.Add(() =>
                {
                    bool on = DifficultyIndex() == idx;
                    img.color = on ? baseColor : new Color(baseColor.r * 0.45f, baseColor.g * 0.45f, baseColor.b * 0.45f, 0.9f);
                    lbl.color = on ? Gold : Muted;
                });
            }
            var desc = Text(panel, "DiffDesc", font, "", 18, Gold, TextAlignmentOptions.Center);
            Place(desc.rectTransform, 0, 390, W - 90, 30, top: true);
            Refreshers.Add(() => desc.text = DifficultyText(DifficultyIndex()));

            float y = 450;
            Option(panel, font, buttonImg, ref y, Ru ? "Передышка каждые" : "A respite every",
                Ru ? "волн: решётка поднимается, портал открыт, можно уйти с добычей" : "waves: the gate rises, the portal opens, you may leave with your loot",
                () => HordeSettings.WavesPerBlock.Value.ToString(),
                dir => Cycle(BlockOptions, HordeSettings.WavesPerBlock.Value, dir, v => HordeSettings.WavesPerBlock.Value = v));
            Option(panel, font, buttonImg, ref y, Ru ? "Максимум врагов на арене" : "Most enemies in the arena",
                Ru ? "одновременно; остальные из волны выходят, когда освобождается место" : "at once; the rest of the wave comes in as room frees up",
                () => HordeSettings.MaxAlive.Value.ToString(),
                dir => Cycle(MaxAliveOptions, HordeSettings.MaxAlive.Value, dir, v => HordeSettings.MaxAlive.Value = v));

            _status = Text(panel, "Status", font, "", 20, Blood, TextAlignmentOptions.Center);
            Place(_status.rectTransform, 0, 150, W - 100, 30, top: false);

            var start = MakeButton(panel, font, buttonImg, "", out _startLabel);
            Place(start.GetComponent<RectTransform>(), -160, 92, 290, 58, top: false);
            start.onClick.AddListener((UnityAction)new Action(() =>
            {
                HordeGen.Armed = !HordeGen.Armed;
                Plugin.L.Msg(HordeGen.Armed ? "Horde armed at the statue." : "Horde disarmed at the statue.");
                Refresh();
            }));

            // Works when the co-op mod is loaded (it fills HordeCoop.Invite); greyed with a note otherwise.
            _invite = MakeButton(panel, font, buttonImg, Ru ? "Пригласить друга" : "Invite a friend", out _inviteLabel);
            Place(_invite.GetComponent<RectTransform>(), 160, 92, 290, 58, top: false);
            _invite.onClick.AddListener((UnityAction)new Action(InvitePressed));
            _inviteNote = Text(panel, "InviteNote", font, "", 16, Muted, TextAlignmentOptions.Center);
            Place(_inviteNote.rectTransform, 160, 52, 330, 24, top: false);

            var hint = Text(panel, "Hint", font, Ru ? "[Esc] Закрыть" : "[Esc] Close", 18, Muted, TextAlignmentOptions.Center);
            Place(hint.rectTransform, 0, 22, W, 26, top: false);
            _root.SetActive(false);
        }
        catch (Exception e)
        {
            Plugin.L.Error("Horde window build failed: " + e);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }

    // One row: label and a small explanation on the left, "<  value  >" on the right.
    static void Option(Transform panel, TMP_FontAsset font, Image style, ref float y, string label, string note, Func<string> value, Action<int> change)
    {
        var row = Rect("Row_" + label, panel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -y), new Vector2(830, 70));
        var name = Text(row, "Name", font, label, 24, Ink, TextAlignmentOptions.Left);
        Place(name.rectTransform, -160, 13, 500, 34);
        var sub = Text(row, "Note", font, note, 16, Muted, TextAlignmentOptions.Left);
        Place(sub.rectTransform, -160, -17, 500, 26);
        var val = Text(row, "Value", font, "", 28, Gold, TextAlignmentOptions.Center);
        Place(val.rectTransform, 290, 0, 110, 50);
        var left = MakeButton(row, font, style, "<", out _);
        Place(left.GetComponent<RectTransform>(), 200, 0, 46, 42);
        left.onClick.AddListener((UnityAction)new Action(() => change(-1)));
        var right = MakeButton(row, font, style, ">", out _);
        Place(right.GetComponent<RectTransform>(), 380, 0, 46, 42);
        right.onClick.AddListener((UnityAction)new Action(() => change(1)));
        Refreshers.Add(() => val.text = value());
        y += 82;
    }

    internal static RectTransform Rect(string name, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        return rt;
    }

    internal static void Place(RectTransform rt, float x, float y, float w, float h, bool? top = null)
    {
        if (top == true) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.anchoredPosition = new Vector2(x, -y); }
        else if (top == false) { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.anchoredPosition = new Vector2(x, y); }
        else { rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.anchoredPosition = new Vector2(x, y); }
        rt.sizeDelta = new Vector2(w, h);
    }

    internal static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.color = color; t.alignment = align;
        t.raycastTarget = false;
        t.enableAutoSizing = true; t.fontSizeMax = size; t.fontSizeMin = size * 0.7f;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    internal static Button MakeButton(Transform parent, TMP_FontAsset font, Image style, string label, out TextMeshProUGUI text)
    {
        var go = new GameObject("Button");
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var img = go.AddComponent<Image>();
        if (style != null && style.sprite != null) { img.sprite = style.sprite; img.type = style.type; img.color = style.color; }
        else img.color = new Color(0.30f, 0.22f, 0.14f, 1f);
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        text = Text(go.transform, "Label", font, label, 22, Ink, TextAlignmentOptions.Center);
        var rt = text.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return b;
    }
}
