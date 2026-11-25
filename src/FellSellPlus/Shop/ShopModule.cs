using System;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FellSellPlus.Shop;

// Per-frame driver (called from Plugin.OnUpdate): hold-key actions on what the player looks at,
// checkout upkeep, line panel, shop window, short toasts.
internal static class ShopModule
{
    static float _holdStart = -1f;
    static Key _heldKey;
    static bool _fired;

    static GameObject _toastRoot;
    static TextMeshProUGUI _toast;
    static float _toastUntil;

    public static int TabFallbackFrame = -1;

    public static void Tick()
    {
        try
        {
            // Game singletons are only safe in gameplay scenes; the shop exists only in town.
            if (Inventory.ItemCounts.Scene() != Inventory.ItemCounts.Town) { if (ShopWindow.IsOpen) ShopWindow.Close(); HideToast(); return; }
            TabFallback();
            ShopWindow.Tick();
            if (ShopManager.Instance == null || FirstPersonController.Instance == null) { HideToast(); return; }
            HoldKeys();
            Checkout.Maintain();
            QueueOrder.Tick();
            LineHud.Tick();
            if (_toastRoot != null && Time.unscaledTime > _toastUntil) HideToast();
        }
        catch (Exception e) { Plugin.L.LogWarning($"Shop tick: {e.Message}"); }
    }

    // TAB anywhere in town, unless another window or the game's menus have the input.
    static void TabFallback()
    {
        if (!ShopSettings.ShopWindow.Value || ShopWindow.IsOpen) return;
        var kb = Keyboard.current;
        if (kb == null || !kb.tabKey.wasPressedThisFrame) return;
        if (TabPressedPatch.LastFrame == Time.frameCount) return;
        if (ModalGuard.Active || FirstPersonController.Instance == null || FirstPersonController.IsPlayerBlocked) return;
        TabFallbackFrame = Time.frameCount;
        ShopWindow.Toggle();
    }

    static void HoldKeys()
    {
        var kb = Keyboard.current;
        if (kb == null || ShopWindow.IsOpen) return;
        Key display = ShopSettings.ParseKey(ShopSettings.DisplayKey.Value, Key.U);
        Key checkout = ShopSettings.ParseKey(ShopSettings.CheckoutKey.Value, Key.P);

        Key down = Key.None;
        if (ShopSettings.DisplayShelves.Value && kb[display].isPressed) down = display;
        else if (ShopSettings.CheckoutChoice.Value && kb[checkout].isPressed) down = checkout;

        if (down == Key.None) { _holdStart = -1f; _fired = false; return; }
        if (down != _heldKey || _holdStart < 0) { _heldKey = down; _holdStart = Time.unscaledTime; _fired = false; return; }
        if (_fired || Time.unscaledTime - _holdStart < ShopSettings.HoldSeconds.Value) return;
        _fired = true;

        var hit = LookedAt();
        if (hit == null) return;
        if (down == display)
        {
            var slot = hit.GetComponentInParent<ShelfSlot>();
            if (slot != null) DisplayShelves.Toggle(slot);
        }
        else
        {
            var q = hit.GetComponentInParent<CheckoutQueue>();
            if (q != null) Checkout.Pick(q);
        }
    }

    static GameObject LookedAt()
    {
        var cam = Camera.main;
        if (cam == null) return null;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        return Physics.Raycast(ray, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Collide) && hit.collider != null
            ? hit.collider.gameObject : null;
    }

    public static void Toast(string msg)
    {
        try
        {
            if (_toastRoot == null)
            {
                _toastRoot = Ui.Canvas("FSP_Toast", 60);
                _toast = Ui.Text(_toastRoot.transform, "Toast", "", 28, Ui.GoldInk, TextAlignmentOptions.Center);
                Ui.At(_toast.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -140), new Vector2(900, 50));
            }
            _toast.text = msg;
            _toastRoot.SetActive(true);
            _toastUntil = Time.unscaledTime + 2.5f;
        }
        catch (Exception e) { Plugin.L.LogWarning($"Toast: {e.Message}"); }
    }

    static void HideToast() { if (_toastRoot != null) _toastRoot.SetActive(false); }
}
