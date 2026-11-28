using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FellSellHorde;

// Horde HUD on a canvas, in the game's own TMP font.
// - A top strip: the wave in big letters and one line of state under it (gifts, the next gift
//   wave, what the chest holds).
// - Centre announcements that hit: a punch-in with overshoot, an outline and a glow under the
//   letters, a blade line that cuts outward from the word, then a fade. Boss arrivals add a blood
//   pulse on the screen edges. Everything runs on unscaled time (it also plays over the pause).
internal static class HordeHud
{
    static GameObject _root;
    static TextMeshProUGUI _waveNum, _enemies, _bTitle, _bSub;
    static RectTransform _banner, _bladeL, _bladeR;
    static Image _bladeLImg, _bladeRImg;
    static Image[] _edges;
    static CanvasGroup _bannerGroup, _topGroup;
    static float _bannerStart = -100f, _bannerLen = 2.4f, _pulseStart = -100f;
    static Color _bannerColor;


    static readonly Color Bone = new(0.96f, 0.91f, 0.80f, 1f);
    static readonly Color Ash = new(0.70f, 0.64f, 0.55f, 1f);
    static readonly Color Gold = new(1f, 0.78f, 0.30f, 1f);
    static readonly Color Blood = new(0.92f, 0.16f, 0.12f, 1f);
    static readonly Color Ember = new(1f, 0.52f, 0.20f, 1f);

    internal static void Show(bool on)
    {
        if (on && _root == null) Build();
        if (_root != null) _root.SetActive(on);
    }

    internal static void Destroy()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        _root = null;
    }

    // kind: "wave", "boss", "level", "clear", "info"
    internal static void Announce(string title, string sub, string kind)
    {
        if (_root == null) Build();
        if (_root == null) return;
        _bannerColor = kind switch { "boss" => Blood, "level" => Gold, "clear" => Bone, "info" => Ash, _ => Ember };
        _bTitle.text = title;
        _bSub.text = sub ?? "";
        _bTitle.fontSize = kind == "info" ? 44 : kind == "clear" ? 64 : 92;
        Glow(_bTitle, _bannerColor);
        _bTitle.color = kind == "boss" ? new Color(1f, 0.86f, 0.80f) : Bone;
        _bSub.color = _bannerColor;
        _bladeLImg.color = _bladeRImg.color = _bannerColor;
        _bannerLen = kind == "info" ? 2.2f : kind == "boss" ? 3.4f : 2.6f;
        _bannerStart = Time.unscaledTime;
        if (kind == "boss") _pulseStart = Time.unscaledTime;
    }

    internal static void Tick(HordeSnapshot st, int level, int xp, int need, int chest)
    {
        if (_root == null || !_root.activeSelf) return;
        bool ru = HordeDirector.Ru;
        // Out of the way while a gift is being chosen.
        bool choosing = HordeChoice.IsOpen || (PrayersUI.Instance != null && PrayersUI.Instance.IsOpen);
        _topGroup.alpha = choosing ? 0f : 1f;

        Set(_waveNum, st.Wave > 0 ? (ru ? $"ВОЛНА {st.Wave}" : $"WAVE {st.Wave}") : (ru ? "ОРДА" : "HORDE"));
        string state = st.Phase switch
        {
            HordePhase.Ready => ru ? "подойди к алтарю" : "go to the altar",
            HordePhase.Break => ru ? "передышка, портал открыт" : "respite, the portal is open",
            _ => ru ? $"врагов: {st.Alive + st.LeftToSpawn}" : $"enemies: {st.Alive + st.LeftToSpawn}",
        };
        int next = HordeProgress.NextGiftWave(Mathf.Max(0, st.Wave - (st.Phase == HordePhase.Wave ? 1 : 0)));
        string gifts = ru ? $"даров: {level}  ·  следующий после волны {next}" : $"gifts: {level}  ·  next after wave {next}";
        string box = ru ? $"сундук: {chest}" : $"chest: {chest}";
        Set(_enemies, $"{state}   ·   {gifts}   ·   {box}");
        Animate();
    }

    static void Set(TextMeshProUGUI t, string v) { if (t != null && t.text != v) t.text = v; }
    static void Paint(TextMeshProUGUI t, Color c) { if (t != null && t.color != c) t.color = c; }

    static void Animate()
    {
        float t = Time.unscaledTime - _bannerStart;
        if (t > _bannerLen) { _bannerGroup.alpha = 0f; }
        else
        {
            // Punch: 1.55 -> overshoot 0.94 -> 1.0 in 0.32 s; fade in fast, out over the last 0.6 s.
            float s = t < 0.18f ? Mathf.Lerp(1.55f, 0.94f, EaseOut(t / 0.18f))
                    : t < 0.32f ? Mathf.Lerp(0.94f, 1f, EaseOut((t - 0.18f) / 0.14f)) : 1f;
            _banner.localScale = Vector3.one * s;
            float a = Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((_bannerLen - t) / 0.6f);
            _bannerGroup.alpha = a;
            // Blades cut outward from the word.
            float b = EaseOut(Mathf.Clamp01((t - 0.1f) / 0.35f));
            float len = 420f * b;
            _bladeL.sizeDelta = new Vector2(len, 3f);
            _bladeR.sizeDelta = new Vector2(len, 3f);
            // Letters breathe apart slightly while the banner holds.
            _bTitle.characterSpacing = Mathf.Lerp(-6f, 6f, EaseOut(Mathf.Clamp01(t / _bannerLen)));
        }
        float p = Time.unscaledTime - _pulseStart;
        float v = p < 0f || p > 2.2f ? 0f : Mathf.Sin(Mathf.Clamp01(p / 2.2f) * Mathf.PI) * (0.55f + 0.45f * Mathf.Abs(Mathf.Sin(p * 9f)));
        var ec = new Color(0.55f, 0f, 0f, 0.45f * v);
        if (_edges != null) foreach (var e in _edges) if (e != null) e.color = ec;
    }

    static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

    // Outline plus a soft underlay glow in the accent colour (material instance per text).
    static void Glow(TextMeshProUGUI t, Color c)
    {
        try
        {
            t.outlineWidth = 0.22f;
            t.outlineColor = new Color32(20, 8, 4, 255);
            var m = t.fontMaterial;
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", new Color(c.r, c.g, c.b, 0.85f));
            m.SetFloat("_UnderlaySoftness", 0.6f);
            m.SetFloat("_UnderlayDilate", 0.4f);
            m.SetFloat("_UnderlayOffsetX", 0f);
            m.SetFloat("_UnderlayOffsetY", 0f);
        }
        catch (Exception e) { Plugin.L.Warning("HUD glow: " + e.Message); }
    }

    static void Build()
    {
        try
        {
            var font = FirstPersonController.Instance?.interactionText?.font;
            _root = new GameObject("Horde_HUD");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            var canvas = _root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = _root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Edge pulse for bosses: four bars hugging the screen edges, tinted together.
            var vig = HordeWindow.Rect("Vignette", _root.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            var frame = new[] { ("T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, -60), new Vector2(0, 120)),
                                ("B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 60), new Vector2(0, 120)),
                                ("L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(70, 0), new Vector2(140, 0)),
                                ("R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(-70, 0), new Vector2(140, 0)) };
            var edges = new Image[4];
            for (int i = 0; i < 4; i++)
            {
                var (n, a0, a1, pos, size) = frame[i];
                var e = HordeWindow.Rect("Edge" + n, vig, a0, a1, pos, size);
                edges[i] = e.gameObject.AddComponent<Image>();
                edges[i].raycastTarget = false;
                edges[i].color = new Color(0, 0, 0, 0);
            }
            _edges = edges;

            // Top strip: the wave in big letters, one line of state under it.
            var top = HordeWindow.Rect("Top", _root.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0, -170), new Vector2(1200, 110)); // below the health/mana/stamina bars
            _topGroup = top.gameObject.AddComponent<CanvasGroup>();
            _topGroup.blocksRaycasts = false; _topGroup.interactable = false;
            _waveNum = Txt(top, "Wave", font, "", 44, Bone, TextAlignmentOptions.Center, new Vector2(0, 18), new Vector2(1200, 70));
            _waveNum.characterSpacing = 8f;
            Glow(_waveNum, Ember);
            _enemies = Txt(top, "State", font, "", 21, Gold, TextAlignmentOptions.Center, new Vector2(0, -26), new Vector2(1200, 34));

            // Centre banner.
            _banner = HordeWindow.Rect("Banner", _root.transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 190), new Vector2(1400, 220));
            _bannerGroup = _banner.gameObject.AddComponent<CanvasGroup>();
            _bannerGroup.alpha = 0f; _bannerGroup.blocksRaycasts = false; _bannerGroup.interactable = false;
            _bTitle = Txt(_banner, "Title", font, "", 92, Bone, TextAlignmentOptions.Center, new Vector2(0, 18), new Vector2(1400, 130));
            _bTitle.enableAutoSizing = false;
            _bSub = Txt(_banner, "Sub", font, "", 28, Ember, TextAlignmentOptions.Center, new Vector2(0, -62), new Vector2(1200, 40));
            _bSub.characterSpacing = 8f;
            _bladeL = HordeWindow.Rect("BladeL", _banner, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-40, -36), new Vector2(0, 3));
            _bladeL.pivot = new Vector2(1f, 0.5f);
            _bladeLImg = _bladeL.gameObject.AddComponent<Image>(); _bladeLImg.raycastTarget = false;
            _bladeR = HordeWindow.Rect("BladeR", _banner, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(40, -36), new Vector2(0, 3));
            _bladeR.pivot = new Vector2(0f, 0.5f);
            _bladeRImg = _bladeR.gameObject.AddComponent<Image>(); _bladeRImg.raycastTarget = false;
        }
        catch (Exception e)
        {
            Plugin.L.Error("Horde HUD build failed: " + e);
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }

    static TextMeshProUGUI Txt(Transform parent, string name, TMP_FontAsset font, string text, float size, Color color, TextAlignmentOptions align, Vector2 pos, Vector2 box)
    {
        var t = HordeWindow.Text(parent, name, font, text, size, color, align);
        t.enableAutoSizing = false;
        t.fontSize = size;
        // Ellipsis truncates a line that is taller than its box to nothing: Alata's line is about
        // 1.4x the font size, so every strip text except the small label vanished. Let text overflow.
        t.overflowMode = TextOverflowModes.Overflow;
        t.enableWordWrapping = false;
        box = new Vector2(box.x, Mathf.Max(box.y, size * 1.6f));
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        t.rectTransform.anchoredPosition = pos;
        t.rectTransform.sizeDelta = box;
        return t;
    }
}
