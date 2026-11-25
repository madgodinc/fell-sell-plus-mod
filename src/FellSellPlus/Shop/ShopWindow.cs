using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FellSellPlus.Shop;

// TAB toggles a window that stays open, replacing the game's hold-to-view sales list.
// TransactionHistoryUI.HandleTransactionHistoryPressed/Released (0x18058D660 / 0x18058D790) are
// event handlers subscribed to PlayerInputHandler; patching the methods themselves catches them.
[HarmonyPatch(typeof(TransactionHistoryUI), "HandleTransactionHistoryPressed")]
internal static class TabPressedPatch
{
    // The game's handler only fires while its sales list is subscribed (in-game it did not fire near
    // the counter), so ShopModule also reads TAB itself; frame stamps keep the two from toggling twice.
    public static int LastFrame = -1;
    static float _lastToggle = -10f;

    static bool Prefix()
    {
        if (!ShopSettings.ShopWindow.Value) return true;
        if (ShopModule.TabFallbackFrame == Time.frameCount) return false;
        LastFrame = Time.frameCount;
        // One TAB press reaches this handler twice (~50 ms apart, observed): debounce.
        if (Time.unscaledTime - _lastToggle < 0.25f) return false;
        _lastToggle = Time.unscaledTime;
        ShopWindow.Toggle();
        return false;
    }
}

[HarmonyPatch(typeof(TransactionHistoryUI), "HandleTransactionHistoryReleased")]
internal static class TabReleasedPatch
{
    static bool Prefix() => !ShopSettings.ShopWindow.Value;
}

internal static class ShopWindow
{
    enum Tab { Shelves, Stock, Sales, Owned, Stats, Money, Vendors }

    static GameObject _root;
    static bool _builtRu;
    static RectTransform _area, _header, _listContent;
    static ScrollRect _listScroll, _textScroll;
    static TextMeshProUGUI _body, _legend, _toast;
    static readonly List<(Tab tab, Button btn, TextMeshProUGUI label)> _tabs = new();
    static readonly (TextMeshProUGUI value, TextMeshProUGUI caption)[] _facts = new (TextMeshProUGUI, TextMeshProUGUI)[4];
    static Tab _tab = Tab.Shelves;
    static bool _open;
    static int _openedFrame;
    static float _nextRefresh, _toastUntil;

    // Row tabs: rows are pooled, buttons read the row's current data when clicked.
    const float RowH = 58f;
    sealed class Row
    {
        public RectTransform Rt;
        public Image Icon;
        public TextMeshProUGUI[] Cells;
        public Button[] Buttons;
        public TextMeshProUGUI[] ButtonLabels;
    }
    static readonly List<Row> _rows = new();
    static readonly List<ShelfSlot> _shelfData = new();
    static readonly List<ShelfActions.Source> _stockData = new();
    static readonly List<ShelfSlot> _freePlaces = new();
    static Tab _rowsBuiltFor = (Tab)(-1);

    public static bool IsOpen => _open;

    public static void Toggle() { if (_open) Close(); else Open(); }

    static void Open()
    {
        if (ShopManager.Instance == null) return;
        // Labels are written when the window is built: rebuild it after a language switch.
        if (_root != null && _builtRu != Ui.Ru) { UnityEngine.Object.Destroy(_root); _root = null; }
        if (_root == null) { Build(); _builtRu = Ui.Ru; }
        if (_root == null) return;
        _open = true;
        _openedFrame = Time.frameCount;
        _root.SetActive(true);
        GameWindows.Open(_root); // the game's window manager owns cursor, input and pause
        Refresh(true);
    }

    public static void Close()
    {
        if (!_open) return;
        _open = false;
        GameWindows.Close(_root);
        if (_root != null) _root.SetActive(false);
    }

    public static void Tick()
    {
        if (!_open) return;
        if (_root == null || ShopManager.Instance == null) { _open = false; return; }
        if (!_root.activeSelf) { _open = false; return; } // closed by the game's window manager
        ModalGuard.Touch();
        var kb = Keyboard.current;
        if (kb != null && Time.frameCount != _openedFrame)
        {
            if (kb.tabKey.wasPressedThisFrame) { Close(); return; } // Esc is the game's: its window stack closes us
            if (kb.digit1Key.wasPressedThisFrame) Select(Tab.Shelves);
            if (kb.digit2Key.wasPressedThisFrame) Select(Tab.Stock);
            if (kb.digit3Key.wasPressedThisFrame) Select(Tab.Sales);
            if (kb.digit4Key.wasPressedThisFrame) Select(Tab.Owned);
            if (kb.digit5Key.wasPressedThisFrame) Select(Tab.Stats);
            if (kb.digit6Key.wasPressedThisFrame) Select(Tab.Money);
            if (kb.digit7Key.wasPressedThisFrame) Select(Tab.Vendors);
        }
        if (_toast != null && _toast.gameObject.activeSelf && Time.unscaledTime > _toastUntil) _toast.gameObject.SetActive(false);
        if (Time.unscaledTime >= _nextRefresh) Refresh(false);
    }

    static void Select(Tab t)
    {
        _tab = t;
        Refresh(true);
        if (_listScroll != null) _listScroll.verticalNormalizedPosition = 1f;
        if (_textScroll != null) _textScroll.verticalNormalizedPosition = 1f;
    }

    static bool RowTab => _tab is Tab.Shelves or Tab.Stock;

    static void Refresh(bool full)
    {
        _nextRefresh = Time.unscaledTime + 1.5f;
        try
        {
            foreach (var (tab, btn, label) in _tabs)
            {
                // The tab sprite is light parchment: dark ink on it, the active tab in deep red on a brighter plate.
                bool on = tab == _tab;
                label.color = on ? new Color(0.55f, 0.10f, 0.06f, 1f) : new Color(0.20f, 0.15f, 0.10f, 1f);
                label.fontStyle = on ? FontStyles.Bold : FontStyles.Normal;
                btn.image.color = on ? new Color(1f, 0.95f, 0.78f, 1f) : new Color(0.72f, 0.66f, 0.56f, 1f);
            }
            Facts();
            _listScroll.gameObject.SetActive(RowTab);
            _header.gameObject.SetActive(RowTab);
            _textScroll.gameObject.SetActive(!RowTab);
            if (_tab == Tab.Shelves) ShelvesRows(full);
            else if (_tab == Tab.Stock) StockRows(full);
            else
            {
                _legend.text = "";
                _body.text = _tab switch
                {
                    Tab.Sales => SalesTab(),
                    Tab.Owned => StockTab(),
                    Tab.Stats => StatsTab(),
                    Tab.Money => MoneyTab(),
                    _ => VendorsTab(),
                };
            }
        }
        catch (Exception e) { Plugin.L.LogWarning($"Shop window: {e.Message}"); }
    }

    // ---------- the four big facts on top ----------

    static void Facts()
    {
        var sm = ShopManager.Instance;
        var slots = ShelfActions.Slots().Where(ShelfActions.IsSellable).ToList();
        int empty = slots.Count(s => s.IsEmpty);
        long earned = 0; int sales = 0;
        var h = sm.TransactionHistory;
        if (h != null) for (int i = 0; i < h.Count; i++) if (h[i] != null && h[i].quantitySold > 0) { earned += h[i].totalPrice; sales++; }
        var onShelf = new HashSet<IntPtr>(slots.Where(s => !s.IsEmpty && s.currentItem != null).Select(s => s.currentItem.Pointer));
        int hdMissing = 0;
        var hd = sm.CurrentHighDemandItems;
        if (hd != null) for (int i = 0; i < hd.Count; i++) if (hd[i] != null && !onShelf.Contains(hd[i].Pointer)) hdMissing++;

        SetFact(0, $"{sm.PlayerGold:N0}", Ui.T("Золото", "Gold"), Ui.GoldInk);
        SetFact(1, $"{earned:N0}", Ui.T($"Выручка за последние {sales} продаж", $"Earned in the last {sales} sales"), Ui.GoldInk);
        SetFact(2, empty.ToString(), Ui.T("Пустые полки", "Empty Shelves"), empty > 0 ? Ui.Warn : Ui.Good);
        SetFact(3, hdMissing.ToString(), Ui.T("Высокий спрос, но нет на полках", "High demand, not on shelves"), hdMissing > 0 ? Ui.Warn : Ui.Good);
    }

    static void SetFact(int i, string value, string caption, Color c)
    {
        _facts[i].value.text = value; _facts[i].value.color = c;
        _facts[i].caption.text = caption;
    }

    // ---------- tab 1: shelves, with price buttons ----------

    static readonly string[] ShelfHeads = { "Товар", "Твоя цена", "Клиент заплатит", "Лучшая цена продажи", "Спрос", "Изменить цену" };
    static readonly string[] ShelfHeadsEn = { "Item", "Your price", "Customer pays", "Best sale price", "Demand", "Change price" };
    static readonly float[] ShelfCols = { 64, 420, 575, 760, 960, 1090 };   // x of each column
    static readonly float[] ShelfW = { 350, 150, 180, 190, 120, 360 };

    static void ShelvesRows(bool full)
    {
        var sm = ShopManager.Instance;
        _shelfData.Clear();
        _shelfData.AddRange(ShelfActions.Slots().Where(s => !s.IsEmpty && s.currentItem != null)
            .OrderBy(s => ShelfActions.IsSellable(s) ? 0 : 1).ThenBy(s => Name(s.currentItem)));
        EnsureRows(Tab.Shelves, _shelfData.Count, full);
        Header(Ui.Ru ? ShelfHeads : ShelfHeadsEn, ShelfCols, ShelfW);
        for (int i = 0; i < _shelfData.Count; i++)
        {
            var s = _shelfData[i]; var r = _rows[i]; var item = s.currentItem;
            bool display = !ShelfActions.IsSellable(s);
            int best = Inventory.Value.Best(item);
            SetIcon(r, item);
            r.Cells[0].text = $"{Name(item)}{(s.quantity > 1 ? $"  ×{s.quantity}" : "")}" + (display ? Ui.Col(Ui.T("  (витрина)", "  (display)"), Ui.Muted) : "");
            r.Cells[1].text = $"{s.basePrice:N0}";
            r.Cells[2].text = Ui.Col($"{s.currentPrice:N0}", Ui.GoldInk);
            r.Cells[3].text = best > 0 ? $"{best:N0}" : Ui.Col("-", Ui.Muted);
            r.Cells[4].text = Demand(item);
            r.ButtonLabels[0].text = "−10%";
            r.ButtonLabels[1].text = "+10%";
            r.ButtonLabels[2].text = Ui.T("Лучшая цена", "Best price");
            r.Buttons[2].interactable = best > 0 && best != s.basePrice;
            for (int b = 0; b < 3; b++) r.Buttons[b].gameObject.SetActive(!display);
        }
        _legend.text = _shelfData.Count == 0
            ? Ui.T("На полках пока ничего нет. Поставь товар во вкладке «2 Выставить».", "Nothing on the shelves yet. Put goods out in tab «2 Stock shelves».")
            : Ui.T("Клиент заплатит = твоя цена с учётом престижа, спроса и бонусов мебели. Лучшая цена продажи = самая высокая цена, которую клиенты уже принимали.\n"
                   + "Высокий спрос: цена +20%, берут вдвое чаще. Низкий спрос: цена −20%, берут реже (бывает, если продать много одного товара подряд).",
                   "Customer pays = your price with prestige, demand and furniture bonuses. Best sale price = the highest price customers have accepted.\n"
                   + "High demand: price +20%, bought twice as often. Low demand: price −20%, bought less (after selling a lot of one item).");
    }

    static void OnPrice(int row, int action)
    {
        if (_tab != Tab.Shelves || row >= _shelfData.Count) return;
        var s = _shelfData[row];
        if (s == null || s.IsEmpty) { Refresh(true); return; }
        int p = s.basePrice;
        int step = Math.Max(1, (int)Math.Round(p * 0.1f));
        int np = action switch { 0 => p - step, 1 => p + step, _ => Inventory.Value.Best(s.currentItem) };
        if (np <= 0) return;
        ShelfActions.SetPrice(s, np);
        Toast(Ui.T($"{Name(s.currentItem)}: цена {p:N0} → {s.basePrice:N0}", $"{Name(s.currentItem)}: price {p:N0} → {s.basePrice:N0}"));
        Refresh(false);
    }

    // ---------- tab 2: stock the empty places ----------

    static readonly string[] StockHeads = { "Товар", "В инвентаре", "В сундуках", "Лучшая цена продажи", "Спрос", "" };
    static readonly string[] StockHeadsEn = { "Item", "In inventory", "In chests", "Best sale price", "Demand", "" };
    static readonly float[] StockCols = { 64, 420, 575, 760, 960, 1090 };
    static readonly float[] StockW = { 350, 150, 180, 190, 120, 360 };

    static void StockRows(bool full)
    {
        var sm = ShopManager.Instance;
        _freePlaces.Clear();
        _freePlaces.AddRange(ShelfActions.Slots().Where(s => s.IsEmpty && ShelfActions.IsSellable(s)));
        _stockData.Clear();
        if (_freePlaces.Count > 0)
            _stockData.AddRange(ShelfActions.Sources(_freePlaces)
                .OrderBy(x => sm.GetDemandLevel(x.Item) == ShopManager.DemandLevel.High ? 0 : sm.GetDemandLevel(x.Item) == ShopManager.DemandLevel.Low ? 2 : 1)
                .ThenByDescending(x => Inventory.Value.Best(x.Item)).ThenBy(x => Name(x.Item)));
        EnsureRows(Tab.Stock, _stockData.Count, full);
        Header(Ui.Ru ? StockHeads : StockHeadsEn, StockCols, StockW);
        for (int i = 0; i < _stockData.Count; i++)
        {
            var x = _stockData[i]; var r = _rows[i];
            int best = Inventory.Value.Best(x.Item);
            SetIcon(r, x.Item);
            r.Cells[0].text = Name(x.Item);
            r.Cells[1].text = x.InBag > 0 ? x.InBag.ToString() : Ui.Col("-", Ui.Muted);
            r.Cells[2].text = x.InChests > 0 ? x.InChests.ToString() : Ui.Col("-", Ui.Muted);
            r.Cells[3].text = best > 0 ? $"{best:N0}" : Ui.Col(Ui.T("не продавал", "never sold"), Ui.Muted);
            r.Cells[4].text = Demand(x.Item);
            r.ButtonLabels[0].text = Ui.T("Поставить на полку", "Put on a shelf");
            r.Buttons[0].interactable = true;
            r.Buttons[1].gameObject.SetActive(false);
            r.Buttons[2].gameObject.SetActive(false);
            r.Buttons[0].gameObject.SetActive(true);
        }
        _legend.text = _freePlaces.Count == 0
            ? Ui.T("Свободных мест на полках нет. Освободи место или поставь в магазин ещё мебель с полками.", "No free shelf places. Free one up or place more shelf furniture in your shop.")
            : _stockData.Count == 0
                ? Ui.T("Нечего выставить: в инвентаре и сундуках нет вещей, которые подходят на свободные места.", "Nothing to put out: no item in your inventory or chests fits the free places.")
                : Ui.T($"Свободных мест: {_freePlaces.Count}. «Поставить на полку» кладёт вещь на первое подходящее место по цене, которую ты ставил в прошлый раз (или по лучшей цене продажи). Сначала берётся из инвентаря, потом из сундуков.",
                       $"Free places: {_freePlaces.Count}. «Put on a shelf» uses the first fitting place at the price you set last time (or the best sale price). Takes from your inventory first, then chests.");
    }

    static void OnStock(int row)
    {
        if (_tab != Tab.Stock || row >= _stockData.Count) return;
        var x = _stockData[row];
        try
        {
            int n = ShelfActions.Stock(x, _freePlaces, out var where);
            Toast(n > 0
                ? Ui.T($"{Name(x.Item)} ×{n} на полке{(where == "chest" ? " (из сундука)" : "")}", $"{Name(x.Item)} ×{n} on a shelf{(where == "chest" ? " (from a chest)" : "")}")
                : Ui.T($"{Name(x.Item)}: не получилось поставить", $"{Name(x.Item)}: could not be placed"));
        }
        catch (Exception e) { Plugin.L.LogWarning($"Stock failed: {e.Message}"); }
        Refresh(true);
    }

    // ---------- rows ----------

    static void EnsureRows(Tab tab, int count, bool full)
    {
        if (full || _rowsBuiltFor != tab) _rowsBuiltFor = tab;
        while (_rows.Count < count) _rows.Add(MakeRow(_rows.Count));
        for (int i = 0; i < _rows.Count; i++)
        {
            bool on = i < count;
            if (_rows[i].Rt.gameObject.activeSelf != on) _rows[i].Rt.gameObject.SetActive(on);
            if (on) foreach (var b in _rows[i].Buttons) { b.gameObject.SetActive(true); b.interactable = true; }
        }
        _listContent.sizeDelta = new Vector2(_listContent.sizeDelta.x, Math.Max(1, count) * RowH);
        var cols = tab == Tab.Shelves ? ShelfCols : StockCols;
        var widths = tab == Tab.Shelves ? ShelfW : StockW;
        for (int i = 0; i < count; i++) Layout(_rows[i], cols, widths, tab);
    }

    static Row MakeRow(int index)
    {
        var rt = Ui.Rect("Row" + index, _listContent);
        rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1); rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(0, -(index + 1) * RowH); rt.offsetMax = new Vector2(0, -index * RowH);
        var bg = rt.gameObject.AddComponent<Image>();
        bg.color = index % 2 == 0 ? new Color(1f, 0.9f, 0.7f, 0.05f) : new Color(0, 0, 0, 0f);
        var row = new Row { Rt = rt };
        var icon = Ui.Rect("Icon", rt);
        row.Icon = icon.gameObject.AddComponent<Image>();
        row.Icon.preserveAspect = true; row.Icon.raycastTarget = false;
        row.Cells = new TextMeshProUGUI[5];
        for (int c = 0; c < 5; c++) row.Cells[c] = Ui.Text(rt, "C" + c, "", c == 0 ? 22 : 22, Ui.Ink, c == 0 ? TextAlignmentOptions.Left : TextAlignmentOptions.Center);
        row.Buttons = new Button[3]; row.ButtonLabels = new TextMeshProUGUI[3];
        int r = index;
        for (int b = 0; b < 3; b++)
        {
            int which = b;
            row.Buttons[b] = Ui.Button(rt, "", 20, () => { if (_tab == Tab.Shelves) OnPrice(r, which); else OnStock(r); }, out row.ButtonLabels[b]);
        }
        return row;
    }

    static void Layout(Row row, float[] cols, float[] widths, Tab tab)
    {
        Ui.At(row.Icon.rectTransform, new Vector2(0, 0.5f), new Vector2(12, 0), new Vector2(42, 42));
        for (int c = 0; c < 5; c++)
        {
            var t = row.Cells[c].rectTransform;
            t.anchorMin = t.anchorMax = new Vector2(0, 0.5f); t.pivot = new Vector2(0, 0.5f);
            t.anchoredPosition = new Vector2(cols[c], 0); t.sizeDelta = new Vector2(widths[c], RowH);
            row.Cells[c].enableWordWrapping = c == 0;
            row.Cells[c].overflowMode = TextOverflowModes.Ellipsis;
        }
        float x = cols[5];
        if (tab == Tab.Shelves)
        {
            Place(row.Buttons[0], x, 92); Place(row.Buttons[1], x + 98, 92); Place(row.Buttons[2], x + 196, 160);
        }
        else Place(row.Buttons[0], x, 260);
    }

    static void Place(Button b, float x, float w)
    {
        var rt = b.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0); rt.sizeDelta = new Vector2(w, 44);
    }

    static readonly List<TextMeshProUGUI> _heads = new();
    static void Header(string[] names, float[] cols, float[] widths)
    {
        while (_heads.Count < names.Length) _heads.Add(Ui.Text(_header, "H" + _heads.Count, "", 18, Ui.Muted, TextAlignmentOptions.Center));
        for (int c = 0; c < names.Length; c++)
        {
            var t = _heads[c];
            t.text = names[c];
            t.alignment = c == 0 ? TextAlignmentOptions.Left : TextAlignmentOptions.Center;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 0.5f); rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(cols[c], 0); rt.sizeDelta = new Vector2(widths[c], 44);
        }
    }

    static void SetIcon(Row r, ItemData item)
    {
        var sp = item != null ? item.icon : null;
        r.Icon.sprite = sp;
        r.Icon.enabled = sp != null;
    }

    static void Toast(string text)
    {
        if (_toast == null) return;
        _toast.text = text;
        _toast.gameObject.SetActive(true);
        _toastUntil = Time.unscaledTime + 3f;
    }

    static string Demand(ItemData item)
    {
        var d = ShopManager.Instance.GetDemandLevel(item);
        return d switch
        {
            ShopManager.DemandLevel.High => Ui.Col(Ui.T("Высокий", "High"), Ui.Good),
            ShopManager.DemandLevel.Low => Ui.Col(Ui.T("Низкий", "Low"), Ui.Bad),
            _ => Ui.Col(Ui.T("Обычный", "Normal"), Ui.Muted),
        };
    }

    static string Name(ItemData i) => i == null ? "?" : i.GetDisplayName();

    // ---------- text tabs ----------

    static string SalesTab()
    {
        var sm = ShopManager.Instance;
        var h = sm.TransactionHistory;
        var sb = new StringBuilder();
        if (h == null || h.Count == 0) return Ui.T("Продаж пока нет. Поставь товар на полки (вкладка «2 Выставить»), и клиенты начнут покупать.", "No sales yet. Put goods on the shelves (tab «2 Stock shelves») and customers will start buying.");
        int total = 0;
        for (int i = h.Count - 1; i >= 0; i--)
        {
            var r = h[i];
            if (r == null) continue;
            var face = r.emotion switch
            {
                ShopManager.NPCEmotion.Happy => Ui.Col("☺", Ui.Good),
                ShopManager.NPCEmotion.Angry => Ui.Col("☹", Ui.Bad),
                _ => Ui.Col("•", Ui.Warn),
            };
            bool refused = r.emotion == ShopManager.NPCEmotion.Angry && r.quantitySold <= 0;
            sb.Append(face).Append(' ').Append(Name(r.item))
              .Append(refused ? Ui.Col(Ui.T("  · клиент отказался: цена слишком высокая", "  · the customer refused: price too high"), Ui.Bad)
                              : $"  ×{r.quantitySold}  {r.pricePerUnit:N0} G  = {Ui.Col($"{r.totalPrice:N0} G", Ui.GoldInk)}")
              .Append('\n');
            if (!refused) total += r.totalPrice;
        }
        sb.Insert(0, Ui.Col($"{Ui.T("Последние продажи (игра хранит 50)", "Recent sales (the game keeps 50)")} ({h.Count}): {total:N0} G\n\n", Ui.GoldInk));
        return sb.ToString();
    }

    static string StockTab()
    {
        var rows = new Dictionary<string, (ItemData item, int carried, int chests, int shelves)>();
        void Add(ItemData it, int c, int ch, int sh)
        {
            if (it == null) return;
            rows.TryGetValue(it.name, out var r);
            rows[it.name] = (it, r.carried + c, r.chests + ch, r.shelves + sh);
        }
        foreach (var sl in Inventory.ItemCounts.CarriedSlots())
            if (sl != null && sl.item != null && sl.quantity > 0) Add(sl.item, sl.quantity, 0, 0);
        foreach (var sl in ShelfActions.Slots())
            if (!sl.IsEmpty && sl.currentItem != null) Add(sl.currentItem, 0, 0, sl.quantity);
        foreach (var name in Inventory.ItemCounts.StoredNames().ToList())
        {
            var it = Inventory.ItemCounts.ResolveItem(name);
            if (it != null) Add(it, 0, Inventory.ItemCounts.Stored(name), 0);
        }
        var sb = new StringBuilder();
        long total = 0; int unknown = 0;
        var list = rows.Values.Select(r => (r.item, r.carried, r.chests, r.shelves, n: r.carried + r.chests + r.shelves,
                                           price: Inventory.Value.Best(r.item)))
                              .OrderByDescending(r => (long)r.price * r.n).ThenBy(r => Name(r.item)).ToList();
        foreach (var r in list)
        {
            if (r.price > 0) total += (long)r.price * r.n; else unknown++;
            sb.Append($"{Name(r.item)} ×{r.n}   ")
              .Append(Ui.Col(Ui.T($"с собой {r.carried}, сундуки {r.chests}, полки {r.shelves}", $"carried {r.carried}, chests {r.chests}, shelves {r.shelves}"), Ui.Muted))
              .Append("   ")
              .Append(r.price > 0 ? Ui.Col($"{(long)r.price * r.n:N0} G", Ui.GoldInk) : Ui.Col(Ui.T("цена ?", "value ?"), Ui.Muted))
              .Append('\n');
        }
        sb.Insert(0, Ui.Col($"{Ui.T("Стоимость всего, что у тебя есть", "Value of everything you own")}: {total:N0} G", Ui.GoldInk)
                     + (unknown > 0 ? Ui.Col(Ui.T($"  (без цены: {unknown})", $"  ({unknown} without a known price)"), Ui.Muted) : "")
                     + Ui.Col(Ui.T("\nСундуки считаются по последнему посещению деревни.\n\n", "\nChests are counted as of your last time in town.\n\n"), Ui.Muted));
        return sb.ToString();
    }

    // Totals over the sales the game keeps (its history holds the last 50 transactions).
    static string StatsTab()
    {
        var h = ShopManager.Instance.TransactionHistory;
        if (h == null || h.Count == 0) return Ui.T("Продаж пока нет. Поставь товар на полки (вкладка «2 Выставить»), и клиенты начнут покупать.", "No sales yet. Put goods on the shelves (tab «2 Stock shelves») and customers will start buying.");
        int sales = 0, refused = 0, happy = 0; long gold = 0;
        var byItem = new Dictionary<string, (int qty, long gold)>();
        for (int i = 0; i < h.Count; i++)
        {
            var r = h[i];
            if (r == null) continue;
            if (r.quantitySold <= 0) { refused++; continue; }
            sales++; gold += r.totalPrice;
            if (r.emotion == ShopManager.NPCEmotion.Happy) happy++;
            var key = Name(r.item);
            byItem.TryGetValue(key, out var v);
            byItem[key] = (v.qty + r.quantitySold, v.gold + r.totalPrice);
        }
        var sb = new StringBuilder();
        sb.Append(Ui.Col(Ui.T($"Последние {h.Count} сделок", $"Last {h.Count} transactions"), Ui.GoldInk)).Append('\n')
          .Append($"{Ui.T("Продаж", "Sales")}: {sales}   {Ui.T("Золото", "Gold")}: {gold:N0} G   ")
          .Append($"{Ui.T("Средний чек", "Average sale")}: {(sales > 0 ? gold / sales : 0):N0} G\n")
          .Append($"{Ui.T("Довольных", "Happy")}: {happy}   {Ui.T("Отказов от цены", "Refused prices")}: {refused}\n\n")
          .Append(Ui.Col(Ui.T("ЛУЧШЕ ВСЕГО ПРОДАЁТСЯ", "BEST SELLERS"), Ui.GoldInk)).Append('\n');
        foreach (var kv in byItem.OrderByDescending(k => k.Value.gold).Take(15))
            sb.Append($"{kv.Key} ×{kv.Value.qty}   {Ui.Col($"{kv.Value.gold:N0} G", Ui.GoldInk)}\n");
        return sb.ToString();
    }

    // Crafts that pay: result value (best accepted price) minus ingredient values and gold cost.
    // Only recipes you know and whose every part has a known value are listed.
    static string MoneyTab()
    {
        var cm = CraftingManager.Instance;
        if (cm == null) return Ui.T("Верстак недоступен.", "No crafting data.");
        var rows = new List<(string name, int profit, int value, int cost, bool now)>();
        int unknown = 0;
        foreach (var r in Resources.FindObjectsOfTypeAll<RecipeData>())
        {
            if (r == null || r.resultItem == null || !cm.IsUnlocked(r)) continue;
            int value = Inventory.Value.Best(r.resultItem) * Math.Max(1, r.resultQuantity);
            if (value <= 0) { unknown++; continue; }
            int cost = r.goldCost; bool known = true;
            if (r.ingredients != null)
                for (int i = 0; i < r.ingredients.Count; i++)
                {
                    var ing = r.ingredients[i];
                    if (ing.item == null) continue;
                    int v = Inventory.Value.Best(ing.item);
                    if (v <= 0) { known = false; break; }
                    cost += v * ing.quantity;
                }
            if (!known) { unknown++; continue; }
            rows.Add((Name(r.resultItem), value - cost, value, cost, cm.CanCraft(r)));
        }
        var sb = new StringBuilder();
        sb.Append(Ui.Col(Ui.T("ВЫГОДНЫЙ КРАФТ: прибыль за одну вещь", "PROFITABLE CRAFTS: profit per item"), Ui.GoldInk))
          .Append(Ui.Col(Ui.T("\nПрибыль = лучшая цена продажи результата − цена компонентов − золото за крафт", "\nProfit = best sale price of the result − ingredient values − crafting gold"), Ui.Muted)).Append('\n');
        foreach (var x in rows.Where(x => x.profit > 0).OrderByDescending(x => x.profit).Take(30))
            sb.Append($"{x.name}   ").Append(Ui.Col($"+{x.profit:N0} G", Ui.Good))
              .Append(Ui.Col($"   ({x.value:N0} − {x.cost:N0})", Ui.Muted))
              .Append(x.now ? "   " + Ui.Col(Ui.T("можно сейчас", "craftable now"), Ui.GoldInk) : "").Append('\n');
        if (!rows.Any(x => x.profit > 0)) sb.Append(Ui.Col(Ui.T("пока нет", "none yet"), Ui.Muted)).Append('\n');
        if (unknown > 0) sb.Append('\n').Append(Ui.Col(Ui.T($"Без известной цены у результата или компонентов: {unknown} рецептов.", $"{unknown} recipes have an unknown value in the result or an ingredient."), Ui.Muted));
        return sb.ToString();
    }

    // What the town vendors sell, their price, and what you have sold the same item for.
    static string VendorsTab()
    {
        var sb = new StringBuilder();
        sb.Append(Ui.Col(Ui.T("ТОРГОВЦЫ: что выгодно купить и перепродать", "MERCHANTS: what is worth buying to resell"), Ui.GoldInk)).Append('\n');
        var seen = new HashSet<string>();
        foreach (var v in UnityEngine.Object.FindObjectsOfType<ShopSaleItem>()
                     .Where(v => v != null && v.itemData != null).OrderBy(v => Name(v.itemData)))
        {
            if (!seen.Add(v.itemData.name + v.GetPrice())) continue;
            int price = v.GetPrice(), qty = Math.Max(1, v.quantityPerPurchase);
            int sells = Inventory.Value.Best(v.itemData) * qty;
            sb.Append($"{Name(v.itemData)}{(qty > 1 ? $" ×{qty}" : "")}   {Ui.T("у торговца", "merchant price")} {price:N0} G   ");
            if (sells > 0)
            {
                int margin = sells - price;
                sb.Append($"{Ui.T("ты продавал за", "you sold for")} {sells:N0} G   ")
                  .Append(Ui.Col($"{(margin >= 0 ? "+" : "")}{margin:N0} G", margin > 0 ? Ui.Good : Ui.Bad));
            }
            else sb.Append(Ui.Col(Ui.T("цена продажи неизвестна", "sale value unknown"), Ui.Muted));
            sb.Append('\n');
        }
        if (seen.Count == 0) sb.Append(Ui.Col(Ui.T("Торговцев рядом нет.", "No merchants in this area."), Ui.Muted));
        return sb.ToString();
    }

    // ---------- construction ----------

    static void Build()
    {
        try
        {
            _root = Ui.Canvas("FSP_ShopWindow", 480);
            var dim = Ui.Rect("Dim", _root.transform);
            Ui.Stretch(dim);
            dim.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.6f);

            var panel = Ui.Panel(_root.transform, "Panel").rectTransform;
            Ui.At(panel, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1520, 920));

            var title = Ui.Text(panel, "Title", Ui.T("Магазин", "Shop"), 38, Ui.Ink, TextAlignmentOptions.Center);
            Ui.At(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -20), new Vector2(1520, 50));

            _tabs.Clear();
            var names = new[]
            {
                (Tab.Shelves, Ui.T("1  Полки и цены", "1  Prices")), (Tab.Stock, Ui.T("2  Выставить", "2  Stock")),
                (Tab.Sales, Ui.T("3  Продажи", "3  Sales")), (Tab.Owned, Ui.T("4  Всё моё", "4  Owned")),
                (Tab.Stats, Ui.T("5  Итоги", "5  Totals")), (Tab.Money, Ui.T("6  Прибыль", "6  Profit")), (Tab.Vendors, Ui.T("7  Торговцы", "7  Merchants")),
            };
            for (int i = 0; i < names.Length; i++)
            {
                var (tab, label) = names[i];
                var b = Ui.Button(panel, label, 20, () => Select(tab), out var text);
                Ui.At(b.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(-627 + i * 209, -78), new Vector2(200, 46));
                _tabs.Add((tab, b, text));
            }

            // Four big facts.
            for (int i = 0; i < 4; i++)
            {
                var box = Ui.Rect("Fact" + i, panel);
                Ui.At(box, new Vector2(0.5f, 1f), new Vector2(-555 + i * 370, -142), new Vector2(350, 92));
                var v = Ui.Text(box, "Value", "", 40, Ui.GoldInk, TextAlignmentOptions.Center);
                Ui.At(v.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(350, 52));
                var c = Ui.Text(box, "Caption", "", 18, Ui.Muted, TextAlignmentOptions.Center);
                Ui.At(c.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(350, 40));
                _facts[i] = (v, c);
            }

            _area = Ui.Rect("Area", panel);
            Ui.Stretch(_area, 40, 120, 40, 250);

            // Row tabs: column header + scrolling rows.
            _header = Ui.Rect("Header", _area);
            _header.anchorMin = new Vector2(0, 1); _header.anchorMax = new Vector2(1, 1); _header.pivot = new Vector2(0.5f, 1);
            _header.offsetMin = new Vector2(0, -44); _header.offsetMax = Vector2.zero;

            var listView = Ui.Rect("List", _area);
            Ui.Stretch(listView, 0, 0, 0, 48);
            listView.gameObject.AddComponent<RectMask2D>();
            listView.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, 0.01f); // catches the mouse wheel
            _listScroll = listView.gameObject.AddComponent<ScrollRect>();
            _listScroll.horizontal = false; _listScroll.scrollSensitivity = 40f;
            _listScroll.movementType = ScrollRect.MovementType.Clamped;
            _listContent = Ui.Rect("Content", listView);
            _listContent.anchorMin = new Vector2(0, 1); _listContent.anchorMax = new Vector2(1, 1); _listContent.pivot = new Vector2(0.5f, 1);
            _listContent.offsetMin = new Vector2(0, -RowH); _listContent.offsetMax = Vector2.zero;
            _listScroll.content = _listContent; _listScroll.viewport = listView;

            // Text tabs.
            var textArea = Ui.Rect("TextArea", _area);
            Ui.Stretch(textArea);
            _body = Ui.ScrollText(textArea, out _textScroll);
            Ui.Stretch((RectTransform)_textScroll.transform);

            _legend = Ui.Text(panel, "Legend", "", 17, Ui.Muted, TextAlignmentOptions.TopLeft);
            Ui.At(_legend.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 52), new Vector2(1440, 64));
            _legend.enableWordWrapping = true;

            _toast = Ui.Text(panel, "Toast", "", 22, Ui.Good, TextAlignmentOptions.Center);
            Ui.At(_toast.rectTransform, new Vector2(0.5f, 1f), new Vector2(0, -238), new Vector2(1440, 30));
            _toast.gameObject.SetActive(false);

            var hint = Ui.Text(panel, "Hint", Ui.T("[TAB] или [Esc]: закрыть    [1-7] или мышь: вкладки    колесо мыши: прокрутка",
                "[TAB] or [Esc]: close    [1-7] or mouse: tabs    mouse wheel: scroll"), 18, Ui.Muted, TextAlignmentOptions.Center);
            Ui.At(hint.rectTransform, new Vector2(0.5f, 0f), new Vector2(0, 16), new Vector2(1440, 28));
            _rows.Clear(); _heads.Clear();
            _root.SetActive(false);
        }
        catch (Exception e)
        {
            Plugin.L.LogError($"Shop window build failed: {e}");
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }
    }
}
