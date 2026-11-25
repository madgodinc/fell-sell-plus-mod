using HarmonyLib;

namespace FellSellPlus.Skills;

// Wrecker (Fighter, epic): breaking a destructible heals a flat 2/3/4 Health. The game raises a
// global "destructible shattered" event that PlayerSkillEffectsController handles for the vanilla
// Desecration skill (percent heal); patching that handler's body catches the event call.
[HarmonyPatch(typeof(PlayerSkillEffectsController), "HandleDestructibleShatteredGlobal")]
internal static class WreckerPatch
{
    static readonly float[] Heal = { 0f, 2f, 3f, 4f };

    static readonly float[] StaminaBack = { 0f, 0.5f, 0.75f, 1f };

    static void Postfix()
    {
        var ps = PlayerStats.Instance;
        if (ps == null) return;
        int r = SkillRegistry.Rank(SkillRegistry.FighterWrecker);
        if (r > 0) ps.Heal(Heal[System.Math.Min(r, 3)]);

        // Light Hand (Fighter, common): refund the stamina of the swing that broke the object,
        // using the right-hand weapon's stamina cost.
        int lh = SkillRegistry.Rank(SkillRegistry.FighterLightHand);
        if (lh <= 0) return;
        var weapon = InventoryManager.Instance?.rightHandSlot?.item;
        float cost = weapon != null && weapon.staminaCost > 0f ? weapon.staminaCost : 0f;
        if (cost <= 0f) return;
        ps.CurrentStamina = System.Math.Min(ps.MaxStamina, ps.CurrentStamina + cost * StaminaBack[System.Math.Min(lh, 3)]);
    }
}
