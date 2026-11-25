using System;
using UnityEngine.InputSystem;

namespace FellSellPlus.Shop;

internal static class ShopSettings
{
    public static ConfigEntry<bool> DisplayShelves;
    public static ConfigEntry<string> DisplayKey;
    public static ConfigEntry<bool> CheckoutChoice;
    public static ConfigEntry<string> CheckoutKey;
    public static ConfigEntry<float> HoldSeconds;
    public static ConfigEntry<bool> LineHud;
    public static ConfigEntry<float> LineHudX;
    public static ConfigEntry<float> LineHudY;
    public static ConfigEntry<bool> ShopWindow;
    public static ConfigEntry<bool> EmptyShelvesOnPickup;
    public static ConfigEntry<bool> IgnoreCarriedFurniture;
    public static ConfigEntry<bool> OutdoorDecor;
    public static ConfigEntry<float> OutdoorDecorRange;
    public static ConfigEntry<bool> BuyShopWithoutMoving;
    public static ConfigEntry<bool> ServeInArrivalOrder;

    public static void Bind(ConfigFile cfg)
    {
        DisplayShelves = cfg.Bind("Shop", "DisplayShelves", true,
            "Hold DisplayKey while looking at a shelf slot to make it a display: customers don't buy from it.");
        DisplayKey = cfg.Bind("Shop", "DisplayKey", "U", "Key held to toggle a display shelf (Unity Input System key name).");
        CheckoutChoice = cfg.Bind("Shop", "CheckoutChoice", true,
            "Hold CheckoutKey while looking at a counter to make it the checkout; remembered per save.");
        CheckoutKey = cfg.Bind("Shop", "CheckoutKey", "P", "Key held to pick the checkout counter.");
        HoldSeconds = cfg.Bind("Shop", "HoldSeconds", 0.6f,
            new ConfigDescription("How long the display/checkout keys must be held.", new AcceptableValueRange<float>(0.1f, 3f)));
        LineHud = cfg.Bind("Shop", "LineHud", true,
            "In town, show the checkout line (customers in pay order with what they'll pay) and coins waiting.");
        LineHudX = cfg.Bind("Shop", "LineHudX", 24f, "Line panel distance from the right screen edge (1920x1080 units).");
        LineHudY = cfg.Bind("Shop", "LineHudY", 140f, "Line panel distance from the top screen edge (1920x1080 units).");
        ShopWindow = cfg.Bind("Shop", "ShopWindow", true,
            "TAB opens a window that stays open (Shop / Sales / Restock) instead of the hold-to-view sales list.");
        EmptyShelvesOnPickup = cfg.Bind("Shop", "EmptyShelvesOnPickup", true,
            "Picking up a table, shelf or rack with items puts those items in your inventory with the prices you set; what doesn't fit stays on the furniture.");
        IgnoreCarriedFurniture = cfg.Bind("Shop", "IgnoreCarriedFurniture", true,
            "Customers don't take items from furniture you are picking up or placing.");
        OutdoorDecor = cfg.Bind("Shop", "OutdoorDecor", true,
            "Floor decorations (plants, rugs, chairs, statues...) can be placed outside on the ground near the shop.");
        OutdoorDecorRange = cfg.Bind("Shop", "OutdoorDecorRange", 4.5f,
            new ConfigDescription("How far outside the shop floor decorations may go, in metres.", new AcceptableValueRange<float>(0.5f, 20f)));
        BuyShopWithoutMoving = cfg.Bind("Shop", "BuyShopWithoutMoving", true,
            "At the construction workbench, Buy only buys the new shop; choose it again and press Change to move in.");
        ServeInArrivalOrder = cfg.Bind("Shop", "ServeInArrivalOrder", true,
            "Customers pay in the order they reach the checkout line, instead of the order they picked their item.");
    }

    public static Key ParseKey(string name, Key fallback) =>
        Enum.TryParse<Key>(name?.Trim(), true, out var k) ? k : fallback;
}
