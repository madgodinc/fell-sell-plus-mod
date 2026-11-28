using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace FellSellBows;

// Mad: bows can be crafted. Each bow gets a recipe modelled on a vanilla weapon recipe of the same
// rarity (its ingredients, gold cost and crafting tab), known from the start.
internal static class BowRecipes
{
    static readonly List<RecipeData> Made = new();

    // Puts our recipes into the manager's lists (idempotent; runs after Awake and after a save loads).
    internal static void Ensure(CraftingManager cm)
    {
        if (cm == null || cm.allRecipes == null) return;
        Bows.Ensure();
        if (Made.Count == 0) Build(cm);
        foreach (var r in Made)
        {
            if (!cm.allRecipes.Contains(r)) cm.allRecipes.Add(r);
            try { if (cm._unlockedRecipes != null && !cm._unlockedRecipes.Contains(r)) cm._unlockedRecipes.Add(r); } catch { }
        }
    }

    static void Build(CraftingManager cm)
    {
        var all = cm.allRecipes;
        foreach (var d in Bows.Defs)
        {
            var item = Bows.Made.FirstOrDefault(m => m.name == d.Id);
            if (item == null) continue;
            // The model: a staff of the rarity first (the bow is built from one), then any weapon.
            RecipeData model = null;
            for (int pass = 0; pass < 2 && model == null; pass++)
                for (int i = 0; i < all.Count; i++)
                {
                    var r = all[i];
                    var res = r?.resultItem;
                    if (res == null || !res.IsWeapon || res.rarity != d.Rarity || res.upgradeLevel != 0) continue;
                    if (res.name != null && res.name.StartsWith("FSB_")) continue;
                    if (pass == 0 && (res.subTypes == null || !res.subTypes.Contains(ItemSubType.Staff))) continue;
                    model = r; break;
                }
            if (model == null) { Plugin.L.Warning($"{d.Id}: no vanilla {d.Rarity} weapon recipe to copy; not craftable."); continue; }

            var rec = ScriptableObject.CreateInstance<RecipeData>();
            rec.name = d.Id + "_Recipe";
            rec.hideFlags = HideFlags.DontUnloadUnusedAsset;
            rec.resultItem = item;
            rec.resultQuantity = 1;
            rec.ingredients = new Il2CppSystem.Collections.Generic.List<RecipeIngredient>();
            for (int k = 0; model.ingredients != null && k < model.ingredients.Count; k++)
            {
                var src = model.ingredients[k];
                var ing = new RecipeIngredient();
                ing.item = src.item;
                ing.quantity = src.quantity;
                rec.ingredients.Add(ing);
            }
            rec.goldCost = model.goldCost;
            rec.category = model.category;
            rec.subCategory = model.subCategory;
            rec.unlockedByDefault = true;
            rec.excludeFromRandomDrop = true;
            rec.localizedName = item.localizedName;
            rec.localizedDescription = item.localizedDescription;
            Made.Add(rec);
            Plugin.L.Msg($"Recipe for {d.Id}: copied from '{model.name}' ({model.resultItem.name}), gold {rec.goldCost}, {rec.ingredients.Count} ingredient(s).");
        }
    }
}

[HarmonyPatch(typeof(CraftingManager), nameof(CraftingManager.Awake))]
internal static class RecipesAwakePatch
{
    static void Postfix(CraftingManager __instance)
    {
        try { BowRecipes.Ensure(__instance); } catch (Exception e) { Plugin.L.Error("Bow recipes: " + e.Message); }
    }
}

[HarmonyPatch(typeof(CraftingManager), nameof(CraftingManager.RestoreData))]
internal static class RecipesRestorePatch
{
    static void Postfix(CraftingManager __instance)
    {
        try { BowRecipes.Ensure(__instance); } catch (Exception e) { Plugin.L.Error("Bow recipes: " + e.Message); }
    }
}
