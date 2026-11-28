using System;
using HarmonyLib;

namespace FellSellPlus.Info;

// The forest's sword in a stone (SwordInStoneInteractable): every E press is one pull, each pull
// costs stamina and, once stamina is gone, health; progress starts to drain after a short pause.
// The game shows none of these numbers, so the "look at" text gets a second line with them:
// pulls done / needed, the cost of one pull and how long a pause may last before progress drains.
[HarmonyPatch(typeof(SwordInStoneInteractable), nameof(SwordInStoneInteractable.GetObjectText))]
internal static class SwordInStoneHintPatch
{
    static void Postfix(SwordInStoneInteractable __instance, ref string __result)
    {
        if (!InfoSettings.SwordInStoneHint.Value || __instance == null) return;
        try
        {
            if (__instance.HasBeenClaimed() || __instance._pullsRequired <= 0) return;
            bool ru = InfoUi.Ru;
            int done = (int)Math.Floor(__instance._currentPulls);
            int need = __instance._pullsRequired;
            string cost = __instance._costPerPull.ToString("0.#");
            string pause = __instance.decayDelay.ToString("0.#");
            string line = ru
                ? $"Рывки: {done}/{need} · рывок стоит {cost} · пауза до отката {pause} с"
                : $"Pulls: {done}/{need} · a pull costs {cost} · pause before it drains {pause} s";
            __result += $"\n<size=70%><color=#c8c8c8>{line}</color></size>";
        }
        catch { }
    }
}
