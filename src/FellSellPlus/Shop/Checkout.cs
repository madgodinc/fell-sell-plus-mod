using System.Text;
using Il2CppTMPro;
using UnityEngine;

namespace FellSellPlus.Shop;

// Checkout choice and the checkout-line panel.
// The game makes whichever counter registered last the checkout (CheckoutQueue.Instance), which
// changes between loads. We re-point Instance at the counter the player picked, but only while
// nobody is queued and no coins are waiting, so no customer is stranded.
internal static class Checkout
{
    static float _nextApply;

    public static void Pick(CheckoutQueue q)
    {
        if (q == null) return;
        if (!IsIdle(CheckoutQueue.Instance))
        {
            ShopModule.Toast(Ui.T("Сначала обслужите очередь", "Serve the queue first"));
            return;
        }
        ShopStore.SetCheckout(q.transform.position);
        CheckoutQueue.Instance = q;
        ShopModule.Toast(Ui.T("Этот прилавок теперь касса", "This counter is now the checkout"));
    }

    static bool IsIdle(CheckoutQueue q) =>
        q == null || (!q.HasCustomers() && q.SpawnedGoldAmount <= 0 && !q.IsCollecting);

    // Once a second: after a load the game may have chosen another counter; restore the pick.
    public static void Maintain()
    {
        if (!ShopSettings.CheckoutChoice.Value || Time.unscaledTime < _nextApply) return;
        _nextApply = Time.unscaledTime + 1f;
        var want = ShopStore.Checkout;
        var cur = CheckoutQueue.Instance;
        if (string.IsNullOrEmpty(want) || cur == null) return;
        if (ShopStore.Key(cur.transform.position) == want || !IsIdle(cur)) return;
        foreach (var q in Object.FindObjectsOfType<CheckoutQueue>())
        {
            if (q != null && ShopStore.Key(q.transform.position) == want)
            {
                CheckoutQueue.Instance = q;
                return;
            }
        }
    }
}

internal static class LineHud
{
    static GameObject _root;
    static TextMeshProUGUI _text;
    static float _next;

    public static void Tick()
    {
        if (!ShopSettings.LineHud.Value) { Hide(); return; }
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 0.25f;

        var q = CheckoutQueue.Instance;
        bool show = q != null && FirstPersonController.Instance != null && !ShopWindow.IsOpen
                    && (q.HasCustomers() || q.SpawnedGoldAmount > 0);
        if (!show) { Hide(); return; }
        if (_root == null) Build();
        if (_root == null) return;
        _root.SetActive(true);
        _text.text = Render(q);
    }

    static string Render(CheckoutQueue q)
    {
        var sb = new StringBuilder();
        var list = q.GetQueueList();
        int standing = 0;
        if (list != null)
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].IsPhysicallyInQueue) standing++;
        int max = q.maxQueueSize;
        var head = $"{Ui.T("Очередь", "Queue")} {standing}/{max}";
        Color hc = standing >= max ? Ui.Bad : standing == max - 1 ? Ui.Warn : Ui.Ink;
        sb.Append(Ui.Col(head + (standing >= max ? Ui.T(", полная", ", full") : ""), hc)).Append('\n');
        if (list != null)
            for (int i = 0; i < list.Count; i++)
            {
                var npc = list[i];
                if (npc == null) continue;
                var line = $"{i + 1}. {npc._pendingPayment:N0} G";
                sb.Append(npc.IsPhysicallyInQueue ? line : Ui.Col(line + Ui.T(" (идёт)", " (walking)"), Ui.Muted)).Append('\n');
            }
        if (q.SpawnedGoldAmount > 0)
            sb.Append(Ui.Col($"{Ui.T("Монеты на прилавке", "Coins on the counter")}: {q.SpawnedGoldAmount:N0} G", Ui.GoldInk));
        return sb.ToString();
    }

    static void Build()
    {
        try
        {
            _root = Ui.Canvas("FSP_LineHud", 50);
            var bg = Ui.Rect("Bg", _root.transform);
            Ui.At(bg, new Vector2(1, 1), new Vector2(-ShopSettings.LineHudX.Value, -ShopSettings.LineHudY.Value), new Vector2(300, 260));
            var img = bg.gameObject.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0f, 0f, 0f, 0.35f);
            img.raycastTarget = false;
            _text = Ui.Text(bg, "Text", "", 20, Ui.Ink);
            Ui.Stretch(_text.rectTransform, 10, 8, 10, 8);
        }
        catch (System.Exception e)
        {
            Plugin.L.LogError($"Line HUD build failed: {e.Message}");
            if (_root != null) Object.Destroy(_root);
            _root = null;
        }
    }

    static void Hide() { if (_root != null) _root.SetActive(false); }
}
