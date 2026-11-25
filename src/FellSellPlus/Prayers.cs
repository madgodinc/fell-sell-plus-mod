using System;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace FellSellPlus;

// The statue asks for 3 prayers (hardcoded). Raise that count wherever the game rolls 3.
[HarmonyPatch(typeof(PrayersStatue), nameof(PrayersStatue.RollBoons))]
internal static class StatueCardsPatch
{
    static void Prefix(ref int count)
    {
        int before = count;
        if (count == 3) count = Settings.CardsPerStatue.Value;
        if (Dev.DevCommands.Enabled.Value) Plugin.L.LogInfo($"RollBoons count {before} -> {count}");
    }
    static void Postfix(Il2CppSystem.Collections.Generic.List<PrayerData> __result)
    {
        if (Dev.DevCommands.Enabled.Value) Plugin.L.LogInfo($"RollBoons returned {__result?.Count}");
    }
}

[HarmonyPatch(typeof(PrayersManager), nameof(PrayersManager.RollRandomPrayers))]
internal static class RerollCardsPatch
{
    static void Prefix(ref int count) { if (count == 3) count = Settings.CardsPerStatue.Value; }
}

// Three prayer constants are inlined in compiled code with no method to hook, so their immediate
// bytes are rewritten in memory. Each pattern is searched only inside its own method; if it is not
// found exactly once (game update), that feature is skipped and logged instead of patching blind.
//  - PrayersUI.SelectBoon: `cmp eax, 5; jl` = the Dungeon+ prayer cap.
//  - PrayersUI.ShowUIRoutine and RerollRoutine: `mov edx, 3` before boons.Take(3) = cards shown.
internal static class PrayerLimit
{
    [DllImport("kernel32.dll")]
    static extern bool VirtualProtect(IntPtr addr, UIntPtr size, uint prot, out uint old);

    public static void Apply()
    {
        int limit = Settings.MaxActivePrayers.Value;
        if (limit != 5)
            Patch(typeof(PrayersUI), "NativeMethodInfoPtr_SelectBoon", new byte[] { 0x83, 0xF8, 0x05, 0x0F, 0x8C }, 2, limit, "MaxActivePrayers");

        int cards = Settings.CardsPerStatue.Value;
        if (cards != 3)
        {
            // Take(n) with n >= the list size is a no-op, so the UI shows whatever the statue rolled.
            Patch(Nested("ShowUIRoutine"), "NativeMethodInfoPtr_MoveNext", new byte[] { 0xBA, 0x03, 0, 0, 0, 0x48, 0x8B, 0x49, 0x28, 0xE8 }, 1, 8, "Cards shown");
            Patch(Nested("RerollRoutine"), "NativeMethodInfoPtr_MoveNext", new byte[] { 0xBA, 0x03, 0, 0, 0, 0x48, 0x8B, 0xCF, 0xE8 }, 1, 8, "Cards after reroll");
        }
    }

    static Type Nested(string part) =>
        Array.Find(typeof(PrayersUI).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic), t => t.Name.Contains(part));

    static unsafe void Patch(Type owner, string fieldPrefix, byte[] pattern, int offset, int value, string what)
    {
        var field = owner == null ? null : Array.Find(owner.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public),
            f => f.Name.StartsWith(fieldPrefix));
        if (field == null) { Plugin.L.LogWarning($"{what}: method not found, skipped."); return; }

        byte* code = *(byte**)(IntPtr)field.GetValue(null); // Il2CppMethodInfo starts with methodPointer
        const int scan = 0x1000;
        int hit = -1, hits = 0;
        for (int i = 0; i < scan - pattern.Length; i++)
        {
            int j = 0;
            while (j < pattern.Length && code[i + j] == pattern[j]) j++;
            if (j == pattern.Length) { hit = i; hits++; }
        }
        if (hits != 1) { Plugin.L.LogWarning($"{what}: expected 1 match, found {hits}; skipped (game updated?)."); return; }

        var target = (IntPtr)(code + hit + offset);
        VirtualProtect(target, (UIntPtr)1, 0x40, out var old);
        *(byte*)target = (byte)value;
        VirtualProtect(target, (UIntPtr)1, old, out _);
        Plugin.L.LogInfo($"{what} set to {value}.");
    }
}
