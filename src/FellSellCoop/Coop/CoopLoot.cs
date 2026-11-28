using System;
using System.Collections.Generic;
using HarmonyLib;
using Il2CppEmeraldAI;
using UnityEngine;

namespace FellSellPlus.Coop;

// Loot in co-op: every player has his own (his own chests, his own drops from the enemies he sees
// die), and two players would otherwise carry out twice what one does. While a partner is connected
// each player's loot is cut by LootCut (30%): that share of enemies drops nothing, and that share of
// what a chest rolled is taken out again.
internal static class CoopLoot
{
    static readonly System.Random _rnd = new();
    static readonly Dictionary<IntPtr, bool> _empty = new();
    static int _kept, _cut, _chests, _slotsCut;

    static float Cut => Mathf.Clamp01(CoopSettings.LootCut.Value);
    static bool Active => CoopSettings.On && CoopModule.Running && CoopModule.HasPartner && Cut > 0f;
    internal static string Stats => $"drops kept {_kept} cut {_cut}, chests rolled {_chests} with {_slotsCut} stacks cut";

    // One roll per enemy, whichever of its drop steps asks first: it drops as usual or drops nothing.
    static bool Empty(EmeraldDrop drop)
    {
        if (!Active) return false;
        if (!_empty.TryGetValue(drop.Pointer, out bool empty))
        {
            if (_empty.Count > 4000) _empty.Clear();
            empty = _rnd.NextDouble() < Cut;
            _empty[drop.Pointer] = empty;
            if (empty) _cut++; else _kept++;
            if (_kept + _cut == 1) Plugin.L.LogInfo($"Co-op loot: with a partner connected {Cut:P0} of enemy drops and chest contents are left out.");
        }
        return empty;
    }

    [HarmonyPatch(typeof(EmeraldDrop), nameof(EmeraldDrop.DroppingItems))]
    internal static class DropPatch { static bool Prefix(EmeraldDrop __instance) => !Empty(__instance); }

    [HarmonyPatch(typeof(EmeraldDrop), nameof(EmeraldDrop.TryDropEliteLoot))]
    internal static class EliteDropPatch { static bool Prefix(EmeraldDrop __instance) => !Empty(__instance); }

    [HarmonyPatch(typeof(EmeraldDrop), nameof(EmeraldDrop.TryDropRecipe))]
    internal static class RecipeDropPatch { static bool Prefix(EmeraldDrop __instance) => !Empty(__instance); }

    // A stack loses the share (rounded by chance, so small stacks lose it on average too); a stack
    // that comes out empty is removed.
    static void Thin(Il2CppSystem.Collections.Generic.List<ChestSlot> slots)
    {
        if (!Active || slots == null) return;
        _chests++;
        float keep = 1f - Cut;
        for (int i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            // A recipe slot carries no item, only its rarity.
            if (slot == null || (slot.item == null && !slot.isRecipe) || slot.amount <= 0) continue;
            float want = slot.amount * keep;
            int left = (int)want + (_rnd.NextDouble() < want - (int)want ? 1 : 0);
            if (left == slot.amount) continue;
            _slotsCut++;
            if (left <= 0) slots.RemoveAt(i); else slot.amount = left;
        }
    }

    [HarmonyPatch(typeof(DungeonChest), nameof(DungeonChest.GenerateLoot))]
    internal static class ChestPatch
    {
        static void Postfix(DungeonChest __instance) { try { Thin(__instance.inventory); } catch (Exception e) { CoopModule.Warn("chest loot cut", e); } }
    }

    [HarmonyPatch(typeof(BossChest), nameof(BossChest.GenerateLoot))]
    internal static class BossChestPatch
    {
        static void Postfix(BossChest __instance) { try { Thin(__instance.inventory); } catch (Exception e) { CoopModule.Warn("boss chest loot cut", e); } }
    }
}
