using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FellSellCharacter;

// The character editor: a panel on the left built from the game's own window art, the hero on the
// right under an orbit camera. Every change shows on the body at once; closing saves look.json.
internal static class Editor
{
    static GameObject _root;
    static bool _builtRu, _open;
    static int _openedFrame;
    static Look _before;
    static readonly List<Action> Refreshers = new();
    static readonly List<GameObject> Pages = new();
    static readonly List<(Image img, TextMeshProUGUI label, Color baseColor)> Tabs = new();
    static int _page;
    const float PanelW = 660f, PanelH = 1000f, PanelX = 40f;

    static readonly Color Ink = new(0.93f, 0.89f, 0.80f, 1f);
    static readonly Color Muted = new(0.70f, 0.65f, 0.56f, 1f);
    static readonly Color Gold = new(1f, 0.80f, 0.38f, 1f);
    static readonly Color PanelFallback = new(0.09f, 0.075f, 0.06f, 0.96f);

    internal static bool IsOpen => _open;

    internal static bool Ru
    {
        get
        {
            try { return UnityEngine.Localization.Settings.LocalizationSettings.SelectedLocale?.Identifier.Code?.StartsWith("ru") == true; }
            catch { return false; }
        }
    }

    static string T(string ru, string en) => Ru ? ru : en;
    static Look Look => ThirdPerson.Look;

    static readonly string[] SkinTones = { "FCD7BD", "F5C2A7", "EDB592", "DDA07A", "C98B63", "B0744E", "8F5A3A", "6E4329", "4F2E1C", "9CB08A", "8E9BB5", "C9C9D6" };
    static readonly string[] HairTones = { "1B1B1D", "3B2A1F", "5A3A22", "8B5A2B", "B8863B", "D9B26F", "F0E2B6", "A0522D", "B22222", "808488", "E8E8E8", "3A5FCD", "6A3FA0", "2E8B57" };
    static readonly string[] EyeTones = { "607DB7", "3F7A4A", "6B4A2B", "2A2A2A", "8A8F98", "C9A227", "9A1F1F", "7A4FBF" };

    internal static void Open()
    {
        if (_open) return;
        var fpc = FirstPersonController.Instance;
        if (fpc == null || !Plugin.InGame) return;
        if (!ThirdPerson.EnsureReady(fpc)) { Plugin.L.Warning("The character editor needs the hero's body, and it could not be built."); return; }
        if (_root != null && _builtRu != Ru) { UnityEngine.Object.Destroy(_root); _root = null; }
        if (_root == null) { Build(); _builtRu = Ru; }
        if (_root == null) return;
        _before = Look.Clone();
        _open = true;
        _openedFrame = Time.frameCount;
        ThirdPerson.Preview = true;
        ThirdPerson.OrbitYaw = 160f; ThirdPerson.OrbitPitch = 8f; ThirdPerson.OrbitDist = 2.6f;
        _root.SetActive(true);
        var wm = WindowsManager.Instance;
        try { if (wm != null && !wm.IsWindowInStack(_root)) { wm.RegisterWindow(_root); wm.OpenWindow(_root); } }
        catch (Exception e) { Plugin.L.Warning("Window open failed: " + e.Message); }
        ShowPage(_page);
        Refresh();
    }

    internal static void Close(bool keep = true)
    {
        if (!_open) return;
        _open = false;
        ThirdPerson.Preview = false;
        if (!keep && _before != null) ThirdPerson.ApplyLook(_before);
        Look?.Save();
        var wm = WindowsManager.Instance;
        try { if (wm != null && _root != null) { wm.CloseWindow(_root); wm.UnregisterWindow(_root); } }
        catch (Exception e) { Plugin.L.Warning("Window close failed: " + e.Message); }
        if (_root != null) _root.SetActive(false);
    }

    internal static void Tick()
    {
        var kb = Keyboard.current;
        if (!_open)
        {
            var fpc = FirstPersonController.Instance;
            if (kb != null && !string.IsNullOrWhiteSpace(Settings.EditorKey.Value) && Enum.TryParse<Key>(Settings.EditorKey.Value, true, out var key)
                && key != Key.None && kb[key].wasPressedThisFrame && ThirdPerson.InControl(fpc)) Open();
            return;
        }
        // The game closes the top window on Esc by switching it off: that is a normal close here.
        if (_root == null || !_root.activeSelf) { _open = false; ThirdPerson.Preview = false; Look?.Save(); return; }
        if (Time.frameCount == _openedFrame) return;
        if (kb != null && Enum.TryParse<Key>(Settings.EditorKey.Value, true, out var k2) && k2 != Key.None && kb[k2].wasPressedThisFrame) { Close(); return; }

        // Orbit: drag anywhere right of the panel, wheel to zoom.
        var mouse = Mouse.current;
        if (mouse == null) return;
        float panelEdge = Screen.width * ((PanelX + PanelW) / 1920f) * Mathf.Lerp(1f, (Screen.height / 1080f) / (Screen.width / 1920f), 0.5f);
        if (mouse.position.ReadValue().x < panelEdge) return;
        if (mouse.leftButton.isPressed || mouse.rightButton.isPressed)
        {
            var d = mouse.delta.ReadValue();
            ThirdPerson.OrbitYaw += d.x * 0.35f;
            ThirdPerson.OrbitPitch = Mathf.Clamp(ThirdPerson.OrbitPitch - d.y * 0.2f, -15f, 50f);
        }
        float wheel = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(wheel) > 0.01f) ThirdPerson.OrbitDist = Mathf.Clamp(ThirdPerson.OrbitDist - Mathf.Sign(wheel) * 0.25f, 1.0f, 5f);
    }

    static void Changed()
    {
        ThirdPerson.ApplyLook(Look);
        Refresh();
    }

    static void Refresh() { foreach (var r in Refreshers) r(); }

    static void ShowPage(int page)
    {
        _page = page;
        for (int i = 0; i < Pages.Count; i++) Pages[i].SetActive(i == page);
        for (int i = 0; i < Tabs.Count; i++)
        {
            bool on = i == page;
            var b = Tabs[i].baseColor;
            Tabs[i].img.color = on ? b : new Color(b.r * 0.45f, b.g * 0.45f, b.b * 0.45f, 0.9f);
            Tabs[i].label.color = on ? Gold : Muted;
        }
    }

    // ---- look edits ----

    // Cycle one slot (and its mirrored twin) through the parts the game has; optional slots start with "none".
    static void Cycle(int slot, int twin, bool optional, int dir)
    {
        var parts = Catalog.Parts(slot);
        if (parts.Count == 0) return;
        var cur = Catalog.Find(Look.Part(slot));
        int n = parts.Count + (optional ? 1 : 0);
        int i = cur == null ? (optional ? 0 : -1) : parts.IndexOf(cur) + (optional ? 1 : 0);
        i = ((i < 0 ? 0 : i + dir) % n + n) % n;
        var pick = optional ? (i == 0 ? null : parts[i - 1]) : parts[i];
        SetPart(slot, pick);
        if (twin > 0) SetPart(twin, Catalog.Twin(pick, twin));
        Changed();
    }

    static void SetPart(int slot, Part p)
    {
        if (p == null) Look.Parts.Remove(slot); else Look.Parts[slot] = p.Name;
    }

    static string PartText(int slot, bool optional)
    {
        var parts = Catalog.Parts(slot);
        var cur = Catalog.Find(Look.Part(slot));
        int i = cur == null ? 0 : parts.IndexOf(cur) + 1;
        return cur == null ? T("Нет", "None") : $"{i}   <size=70%><color=#B3A68F>/ {parts.Count}</color></size>";
    }

    static readonly int[] OutfitSlots = { Slot.Torso, Slot.ArmUpperL, Slot.ArmUpperR, Slot.ArmLowerL, Slot.ArmLowerR, Slot.HandL, Slot.HandR, Slot.Hips, Slot.LegL, Slot.LegR, Slot.FootL, Slot.FootR };

    static List<string> OutfitKeys()
    {
        var keys = new List<string>();
        foreach (var p in Catalog.Parts(Slot.Torso)) if (!keys.Contains(p.Key)) keys.Add(p.Key);
        return keys;
    }

    static string CurrentOutfit()
    {
        var torso = Catalog.Find(Look.Part(Slot.Torso));
        if (torso == null) return null;
        foreach (int s in OutfitSlots)
        {
            var p = Catalog.Find(Look.Part(s));
            bool exists = Catalog.Parts(s).Any(q => q.Key == torso.Key);
            if (exists && (p == null || p.Key != torso.Key)) return null;
        }
        return torso.Key;
    }

    // A whole costume at once: every body slot takes the piece of that set, or the bare body where the set has none.
    static void CycleOutfit(int dir)
    {
        var keys = OutfitKeys();
        if (keys.Count == 0) return;
        var torso = Catalog.Find(Look.Part(Slot.Torso));
        int i = torso == null ? 0 : keys.IndexOf(torso.Key);
        i = ((i + dir) % keys.Count + keys.Count) % keys.Count;
        if (CurrentOutfit() == null && torso != null && dir > 0) i = keys.IndexOf(torso.Key); // first click completes the mixed set
        foreach (int s in OutfitSlots)
        {
            var parts = Catalog.Parts(s);
            var pick = parts.FirstOrDefault(q => q.Key == keys[i]) ?? parts.FirstOrDefault(q => q.Key == "HUMN_BASE_01") ?? parts.FirstOrDefault();
            SetPart(s, pick);
        }
        Changed();
    }

    // Steps through a few fixed values, starting from the one nearest the current value.
    static void Choose(Func<float> get, Action<float> set, float[] values, int dir)
    {
        int cur = 0;
        for (int k = 1; k < values.Length; k++) if (Mathf.Abs(values[k] - get()) < Mathf.Abs(values[cur] - get())) cur = k;
        set(values[((cur + dir) % values.Length + values.Length) % values.Length]);
        Changed();
    }

    static void CyclePreset(int group, Func<string> get, Action<string> set, int dir)
    {
        var list = Catalog.PresetsOf(group);
        int n = list.Count + 1;
        int i = list.FindIndex(p => p.Name == get()) + 1;
        i = ((i + dir) % n + n) % n;
        set(i == 0 ? "" : list[i - 1].Name);
        Changed();
    }

    static string PresetText(int group, string name)
    {
        if (string.IsNullOrEmpty(name)) return T("Как есть", "As is");
        var list = Catalog.PresetsOf(group);
        int i = list.FindIndex(p => p.Name == name) + 1;
        return $"{i}   <size=70%><color=#B3A68F>/ {list.Count}</color></size>";
    }

    static void Randomize()
    {
        var rng = new System.Random();
        Part Pick(int slot, double chance = 1.0)
        {
            var parts = Catalog.Parts(slot);
            return parts.Count == 0 || rng.NextDouble() > chance ? null : parts[rng.Next(parts.Count)];
        }
        void Pair(int a, int b, double chance = 1.0) { var p = Pick(a, chance); SetPart(a, p); SetPart(b, Catalog.Twin(p, b)); }
        SetPart(Slot.Hair, Pick(Slot.Hair, 0.9));
        SetPart(Slot.FacialHair, Look.Feminine >= 50f ? null : Pick(Slot.FacialHair, 0.4));
        Pair(Slot.EyebrowL, Slot.EyebrowR);
        SetPart(Slot.Nose, Pick(Slot.Nose));
        SetPart(Slot.Torso, Pick(Slot.Torso)); SetPart(Slot.Hips, Pick(Slot.Hips));
        Pair(Slot.ArmUpperL, Slot.ArmUpperR); Pair(Slot.ArmLowerL, Slot.ArmLowerR); Pair(Slot.HandL, Slot.HandR);
        Pair(Slot.LegL, Slot.LegR); Pair(Slot.FootL, Slot.FootR);
        SetPart(Slot.AttachHead, Pick(Slot.AttachHead, 0.3)); SetPart(Slot.AttachFace, Pick(Slot.AttachFace, 0.1));
        SetPart(Slot.AttachBack, Pick(Slot.AttachBack, 0.4));
        Pair(Slot.AttachShoulderL, Slot.AttachShoulderR, 0.35); Pair(Slot.AttachElbowL, Slot.AttachElbowR, 0.2); Pair(Slot.AttachKneeL, Slot.AttachKneeR, 0.2);
        SetPart(Slot.AttachHipsFront, Pick(Slot.AttachHipsFront, 0.25)); SetPart(Slot.AttachHipsBack, Pick(Slot.AttachHipsBack, 0.15));
        SetPart(Slot.AttachHipsL, Pick(Slot.AttachHipsL, 0.25)); SetPart(Slot.AttachHipsR, Pick(Slot.AttachHipsR, 0.25));
        Look.Skin = SkinTones[rng.Next(9)];
        Look.Hair = HairTones[rng.Next(HairTones.Length)];
        Look.Eyes = EyeTones[rng.Next(EyeTones.Length)];
        string Preset(int g) { var l = Catalog.PresetsOf(g); return l.Count == 0 ? "" : l[rng.Next(l.Count)].Name; }
        Look.Outfit = Preset(2); Look.Attachments = Preset(3); Look.Materials = Preset(4);
        Changed();
    }

    // ---- window ----

    static void Build()
    {
        try
        {
            Catalog.Load();
            Refreshers.Clear(); Pages.Clear(); Tabs.Clear();
            var font = FirstPersonController.Instance?.interactionText?.font;
            var shrine = GrandmaShrineUI._instance;
            var panelImg = shrine?._dialogWindow?.GetComponent<Image>();
            var buttonImg = shrine?._actionButton?.image;

            _root = new GameObject("FSC_Editor");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _root.AddComponent<GraphicRaycaster>();

            var panelRt = Rect("Panel", _root.transform);
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0f, 0.5f);
            panelRt.pivot = new Vector2(0f, 0.5f);
            panelRt.anchoredPosition = new Vector2(PanelX, 0f);
            panelRt.sizeDelta = new Vector2(PanelW, PanelH);
            var bg = panelRt.gameObject.AddComponent<Image>();
            if (panelImg != null && panelImg.sprite != null) { bg.sprite = panelImg.sprite; bg.type = panelImg.type; bg.color = panelImg.color; }
            else bg.color = PanelFallback;
            Transform panel = panelRt;

            var title = Text(panel, "Title", font, T("Облик героя", "Hero's look"), 42, Ink, TextAlignmentOptions.Center);
            Place(title.rectTransform, 0, 48, PanelW - 80, 70);

            string[] tabs = { T("Тело", "Body"), T("Одежда", "Outfit"), T("Снаряжение", "Gear"), T("Цвета", "Colours") };
            float tabW = (PanelW - 90f) / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                var b = MakeButton(panel, font, buttonImg, tabs[i], 22, out var lbl);
                Place(b.GetComponent<RectTransform>(), -(PanelW - 90f) / 2f + tabW * (i + 0.5f), 122, tabW - 8, 50);
                b.onClick.AddListener((UnityAction)new Action(() => ShowPage(idx)));
                var img = b.GetComponent<Image>();
                Tabs.Add((img, lbl, img.color));
            }

            // Body.
            var page = Page(panel);
            float y = 0;
            PartRow(page, font, buttonImg, ref y, T("Голова", "Head"), Slot.Head, 0, false);
            PartRow(page, font, buttonImg, ref y, T("Причёска", "Hair"), Slot.Hair, 0, true);
            PartRow(page, font, buttonImg, ref y, T("Брови", "Eyebrows"), Slot.EyebrowL, Slot.EyebrowR, true);
            PartRow(page, font, buttonImg, ref y, T("Борода", "Facial hair"), Slot.FacialHair, 0, true);
            PartRow(page, font, buttonImg, ref y, T("Уши", "Ears"), Slot.EarL, Slot.EarR, false);
            PartRow(page, font, buttonImg, ref y, T("Нос", "Nose"), Slot.Nose, 0, false);
            // Mad: plain choices, not percentages ("25% feminine" read as code).
            Row(page, font, buttonImg, ref y, T("Тело", "Body"),
                () => Look.Feminine >= 50f ? T("Женское", "Female") : T("Мужское", "Male"),
                dir => Choose(() => Look.Feminine, v => Look.Feminine = v, new[] { 0f, 100f }, dir));
            Row(page, font, buttonImg, ref y, T("Сложение", "Build"),
                () => Look.Size <= -30f ? T("Худое", "Slim") : Look.Size >= 30f ? T("Плотное", "Heavy") : T("Обычное", "Average"),
                dir => Choose(() => Look.Size, v => Look.Size = v, new[] { -60f, 0f, 60f }, dir));
            Row(page, font, buttonImg, ref y, T("Мышцы", "Muscle"),
                () => Look.Muscle <= 25f ? T("Слабые", "Lean") : Look.Muscle >= 75f ? T("Сильные", "Strong") : T("Обычные", "Average"),
                dir => Choose(() => Look.Muscle, v => Look.Muscle = v, new[] { 0f, 50f, 100f }, dir));

            // Outfit.
            page = Page(panel); y = 0;
            Row(page, font, buttonImg, ref y, T("Весь костюм", "Whole costume"), () =>
            {
                string key = CurrentOutfit();
                var torso = Catalog.Find(Look.Part(Slot.Torso));
                if (key == null || torso == null) return T("Сборный", "Mixed");
                var keys = OutfitKeys();
                return $"{keys.IndexOf(key) + 1}   <size=70%><color=#B3A68F>/ {keys.Count}</color></size>";
            }, CycleOutfit);
            PartRow(page, font, buttonImg, ref y, T("Торс", "Torso"), Slot.Torso, 0, false);
            PartRow(page, font, buttonImg, ref y, T("Плечи", "Upper arms"), Slot.ArmUpperL, Slot.ArmUpperR, false);
            PartRow(page, font, buttonImg, ref y, T("Предплечья", "Forearms"), Slot.ArmLowerL, Slot.ArmLowerR, false);
            PartRow(page, font, buttonImg, ref y, T("Кисти", "Hands"), Slot.HandL, Slot.HandR, false);
            PartRow(page, font, buttonImg, ref y, T("Бёдра", "Hips"), Slot.Hips, 0, false);
            PartRow(page, font, buttonImg, ref y, T("Голени", "Legs"), Slot.LegL, Slot.LegR, false);
            PartRow(page, font, buttonImg, ref y, T("Ступни", "Feet"), Slot.FootL, Slot.FootR, false);

            // Gear: Sidekick's attachments.
            page = Page(panel); y = 0;
            PartRow(page, font, buttonImg, ref y, T("Головной убор", "Headwear"), Slot.AttachHead, 0, true);
            PartRow(page, font, buttonImg, ref y, T("Маска", "Mask"), Slot.AttachFace, 0, true);
            PartRow(page, font, buttonImg, ref y, T("За спиной", "Back"), Slot.AttachBack, 0, true);
            PartRow(page, font, buttonImg, ref y, T("Наплечники", "Shoulders"), Slot.AttachShoulderL, Slot.AttachShoulderR, true);
            PartRow(page, font, buttonImg, ref y, T("Налокотники", "Elbows"), Slot.AttachElbowL, Slot.AttachElbowR, true);
            PartRow(page, font, buttonImg, ref y, T("Наколенники", "Knees"), Slot.AttachKneeL, Slot.AttachKneeR, true);
            PartRow(page, font, buttonImg, ref y, T("Пояс спереди", "Belt, front"), Slot.AttachHipsFront, 0, true);
            PartRow(page, font, buttonImg, ref y, T("Пояс сзади", "Belt, back"), Slot.AttachHipsBack, 0, true);
            PartRow(page, font, buttonImg, ref y, T("Пояс слева", "Belt, left"), Slot.AttachHipsL, 0, true);
            PartRow(page, font, buttonImg, ref y, T("Пояс справа", "Belt, right"), Slot.AttachHipsR, 0, true);

            // Colours.
            page = Page(panel); y = 0;
            Palette(page, font, ref y, T("Кожа", "Skin"), SkinTones, () => Look.Skin, v => Look.Skin = v);
            Palette(page, font, ref y, T("Волосы", "Hair"), HairTones, () => Look.Hair, v => Look.Hair = v);
            Palette(page, font, ref y, T("Глаза", "Eyes"), EyeTones, () => Look.Eyes, v => Look.Eyes = v);
            y += 8;
            Row(page, font, buttonImg, ref y, T("Краски одежды", "Outfit dyes"), () => PresetText(2, Look.Outfit),
                dir => CyclePreset(2, () => Look.Outfit, v => Look.Outfit = v, dir));
            Row(page, font, buttonImg, ref y, T("Краски снаряжения", "Gear dyes"), () => PresetText(3, Look.Attachments),
                dir => CyclePreset(3, () => Look.Attachments, v => Look.Attachments = v, dir));
            Row(page, font, buttonImg, ref y, T("Металл и кожа", "Metal and leather"), () => PresetText(4, Look.Materials),
                dir => CyclePreset(4, () => Look.Materials, v => Look.Materials = v, dir));

            // Bottom.
            float bw = (PanelW - 100f) / 3f;
            var rnd = MakeButton(panel, font, buttonImg, T("Наугад", "Random"), 22, out _);
            Place(rnd.GetComponent<RectTransform>(), -bw - 5, 96, bw - 6, 56, top: false);
            rnd.onClick.AddListener((UnityAction)new Action(Randomize));
            var def = MakeButton(panel, font, buttonImg, T("Как был", "Original"), 22, out _);
            Place(def.GetComponent<RectTransform>(), 0, 96, bw - 6, 56, top: false);
            def.onClick.AddListener((UnityAction)new Action(() => { ThirdPerson.ApplyLook(Look.Default()); Refresh(); }));
            var done = MakeButton(panel, font, buttonImg, T("Готово", "Done"), 22, out var doneLbl);
            Place(done.GetComponent<RectTransform>(), bw + 5, 96, bw - 6, 56, top: false);
            doneLbl.color = Gold;
            done.onClick.AddListener((UnityAction)new Action(() => Close()));

            var hint = Text(panel, "Hint", font, T("Мышь справа от окна: повернуть  ·  колесо: ближе, дальше  ·  [Esc] закрыть",
                "Mouse right of the window: turn  ·  wheel: closer, further  ·  [Esc] close"), 15, Muted, TextAlignmentOptions.Center);
            Place(hint.rectTransform, 0, 42, PanelW - 90, 30, top: false);
            hint.enableAutoSizing = true; hint.fontSizeMax = 15; hint.fontSizeMin = 11;
            _root.SetActive(false);
        }
        catch (Exception e)
        {
            Plugin.L.Error("Editor window build failed: " + e);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }

    static Transform Page(Transform panel)
    {
        var rt = Rect("Page" + Pages.Count, panel);
        rt.anchorMin = new Vector2(0.5f, 1f); rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0, -170); rt.sizeDelta = new Vector2(PanelW - 80, 680);
        Pages.Add(rt.gameObject);
        return rt;
    }

    static void PartRow(Transform page, TMP_FontAsset font, Image style, ref float y, string label, int slot, int twin, bool optional) =>
        Row(page, font, style, ref y, label, () => PartText(slot, optional), dir => Cycle(slot, twin, optional, dir));

    // One row: the name on the left, "<  value  >" on the right.
    static void Row(Transform page, TMP_FontAsset font, Image style, ref float y, string label, Func<string> value, Action<int> change)
    {
        const float W = PanelW - 80, H = 60;
        var row = Rect("Row_" + label, page);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f); row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0, -y); row.sizeDelta = new Vector2(W, H);
        var name = Text(row, "Name", font, label, 23, Ink, TextAlignmentOptions.Left);
        Center(name.rectTransform, -W / 2 + 105, 0, 210, 44);
        var left = MakeButton(row, font, style, "<", 24, out _);
        Center(left.GetComponent<RectTransform>(), -W / 2 + 240, 0, 44, 44);
        left.onClick.AddListener((UnityAction)new Action(() => change(-1)));
        var right = MakeButton(row, font, style, ">", 24, out _);
        Center(right.GetComponent<RectTransform>(), W / 2 - 22, 0, 44, 44);
        right.onClick.AddListener((UnityAction)new Action(() => change(1)));
        var val = Text(row, "Value", font, "", 22, Gold, TextAlignmentOptions.Center);
        Center(val.rectTransform, (-W / 2 + 262 + W / 2 - 44) / 2f, 0, W - 262 - 44 - 16, 44);
        val.enableAutoSizing = true; val.fontSizeMax = 22; val.fontSizeMin = 15;
        val.richText = true;
        Refreshers.Add(() => val.text = value());
        y += H + 6;
    }

    // A row of colour chips; the first chip is "the hero's own colour".
    static void Palette(Transform page, TMP_FontAsset font, ref float y, string label, string[] tones, Func<string> get, Action<string> set)
    {
        const float W = PanelW - 80, Chip = 34, Gap = 5;
        var row = Rect("Palette_" + label, page);
        row.anchorMin = row.anchorMax = new Vector2(0.5f, 1f); row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0, -y); row.sizeDelta = new Vector2(W, 96);
        var name = Text(row, "Name", font, label, 23, Ink, TextAlignmentOptions.Left);
        Center(name.rectTransform, -W / 2 + 150, 26, 300, 40);
        var own = Text(row, "Own", font, "", 17, Muted, TextAlignmentOptions.Right);
        Center(own.rectTransform, W / 2 - 150, 26, 300, 32);
        for (int i = -1; i < tones.Length; i++)
        {
            string tone = i < 0 ? "" : tones[i];
            float x = -W / 2 + Chip / 2 + 2 + (i + 1) * (Chip + Gap);
            var frame = Rect("Frame", row);
            Center(frame, x, -22, Chip + 6, Chip + 6);
            var frameImg = frame.gameObject.AddComponent<Image>();
            frameImg.raycastTarget = false;
            var chip = Rect("Chip", row);
            Center(chip, x, -22, Chip, Chip);
            var img = chip.gameObject.AddComponent<Image>();
            if (Catalog.TryHex(tone, out var c)) img.color = c;
            else
            {
                // "As the hero is": an empty chip with a stroke through it.
                img.color = new Color(0.16f, 0.13f, 0.11f, 1f);
                var slash = Rect("Slash", chip);
                Center(slash, 0, 0, Chip * 1.2f, 3);
                slash.localRotation = Quaternion.Euler(0, 0, 45);
                var si = slash.gameObject.AddComponent<Image>(); si.color = Muted; si.raycastTarget = false;
            }
            var b = chip.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener((UnityAction)new Action(() => { set(tone); Changed(); }));
            Refreshers.Add(() => frameImg.color = (get() ?? "") == tone ? Gold : new Color(0f, 0f, 0f, 0.55f));
        }
        Refreshers.Add(() => own.text = string.IsNullOrEmpty(get()) ? T("как у героя", "the hero's own") : "");
        y += 100;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    // Place from the panel's top (or bottom) edge, centred horizontally.
    static void Place(RectTransform rt, float x, float y, float w, float h, bool top = true)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, top ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, top ? -y : y);
        rt.sizeDelta = new Vector2(w, h);
    }

    static void Center(RectTransform rt, float x, float y, float w, float h)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(x, y);
        rt.sizeDelta = new Vector2(w, h);
    }

    // TMP draws nothing in a box much shorter than the font with Ellipsis overflow: plain overflow, roomy boxes.
    static TextMeshProUGUI Text(Transform parent, string name, TMP_FontAsset font, string text, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name);
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.color = color; t.alignment = align;
        t.fontSize = size;
        t.raycastTarget = false;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Overflow;
        return t;
    }

    static Button MakeButton(Transform parent, TMP_FontAsset font, Image style, string label, float size, out TextMeshProUGUI text)
    {
        var go = new GameObject("Button");
        go.AddComponent<RectTransform>().SetParent(parent, false);
        var img = go.AddComponent<Image>();
        if (style != null && style.sprite != null) { img.sprite = style.sprite; img.type = style.type; img.color = style.color; }
        else img.color = new Color(0.30f, 0.22f, 0.14f, 1f);
        var b = go.AddComponent<Button>();
        b.targetGraphic = img;
        text = Text(go.transform, "Label", font, label, size, Ink, TextAlignmentOptions.Center);
        var rt = text.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        return b;
    }
}
