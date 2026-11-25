using HarmonyLib;

namespace FellSellPlus.Progression;

// All targets were checked against the compiled game: RecalculateAllStats (0x18048E2A0) is a real
// call from Start/RestoreData/scene refresh; Update (0x180492E60) runs the inlined health regen and
// calls HandleStamina/HandleMana; CurrentSpeed (0x1804310E0) is called twice by HandleMovement;
// OutgoingDamageMultiplier (0x1805C2900) by CalculateDamage. Pool and regen fields are raised only
// for the duration of the original method and restored after it, so nothing ever compounds.

[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.RecalculateAllStats))]
internal static class MaxPoolsPatch
{
    // RecalculateAllStats can re-enter itself (observed: +2 levels gave +4 HP), so only the
    // outermost call shifts the base pools.
    static int _depth;
    static void Prefix(PlayerStats __instance) { if (_depth++ == 0) Shift(__instance, 1f); }
    static void Postfix(PlayerStats __instance) { if (--_depth == 0) Shift(__instance, -1f); }

    static void Shift(PlayerStats p, float sign)
    {
        if (!ProgressionSettings.Enabled.Value) return;
        p._baseMaxHealth += sign * ProgressionState.Bonus(Stat.MaxHealth);
        p._baseMaxStamina += sign * ProgressionState.Bonus(Stat.MaxStamina);
        p._baseMaxMana += sign * ProgressionState.Bonus(Stat.MaxMana);
    }
}

[HarmonyPatch(typeof(PlayerStats), "Update")]
internal static class RegenPatch
{
    static int _depth;
    static void Prefix(PlayerStats __instance) { if (_depth++ == 0) Shift(__instance, 1f); }
    static void Postfix(PlayerStats __instance) { if (--_depth == 0) Shift(__instance, -1f); }

    static void Shift(PlayerStats p, float sign)
    {
        if (!ProgressionSettings.Enabled.Value) return;
        p._healthRegenRate += sign * ProgressionState.Bonus(Stat.HealthRegen);
        p._staminaRegenRate += sign * ProgressionState.Bonus(Stat.StaminaRegen);
        p._manaRegenRate += sign * ProgressionState.Bonus(Stat.ManaRegen);
    }
}

[HarmonyPatch(typeof(FirstPersonController), "CurrentSpeed", MethodType.Getter)]
internal static class MoveSpeedPatch
{
    static void Postfix(ref float __result)
    {
        if (ProgressionSettings.Enabled.Value) __result *= ProgressionState.SpeedMultiplier;
    }
}

// Separate from the challenge postfix on the same getter; Harmony chains both.
[HarmonyPatch(typeof(DifficultyManager), nameof(DifficultyManager.OutgoingDamageMultiplier), MethodType.Getter)]
internal static class ProgressionDamagePatch
{
    static void Postfix(ref float __result)
    {
        if (ProgressionSettings.Enabled.Value) __result *= ProgressionState.DamageMultiplier;
    }
}

// Persistence follows the game's own save cycle (SaveManager.SaveGlobalPlayerState calls
// PlayerStats.SaveData; RestoreData runs through ISaveable).
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.SaveData))]
internal static class SavePatch { static void Postfix(string fileName) => ProgressionState.Save(fileName); }

[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.RestoreData))]
internal static class LoadPatch
{
    static void Postfix(string fileName)
    {
        ProgressionState.Load(fileName);
        GameQuery.RefreshPlayerStats();
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.PrepareForNewGame))]
internal static class NewGamePatch { static void Postfix() => ProgressionState.OnNewGame(); }

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteSlotData), typeof(int))]
internal static class DeleteSlotPatch { static void Postfix(int slot) => ProgressionState.DeleteSlot(slot); }
