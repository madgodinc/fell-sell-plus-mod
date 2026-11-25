using System;
using UnityEngine.Localization.Settings;

namespace FellSellPlus.Progression;

// Thin, null-safe reads of game state the progression needs.
internal static class GameQuery
{
    public static float Prestige() => ShopManager.Instance != null ? ShopManager.Instance.shopPrestige : 0f;
    public static int Gold() => ShopManager.Instance != null ? ShopManager.Instance.PlayerGold : 0;
    public static bool SpendGold(int amount) => ShopManager.Instance != null && ShopManager.Instance.SpendGold(amount);
    public static int CurrentSlot() => SaveManager.Instance != null ? SaveManager.Instance.CurrentSlot : -1;

    // Each boss (and Grandma) leaves a letter flag once killed.
    public static int BossesKilled()
    {
        var m = GrandmasLetterManager.Instance;
        if (m == null) return 0;
        int n = 0;
        if (m.hasSeenBoss1KilledLetter) n++;
        if (m.hasSeenBoss2KilledLetter) n++;
        if (m.hasSeenBoss3KilledLetter) n++;
        if (m.hasSeenGrandmaKilledLetter || m.isGrandmaDead) n++;
        return n;
    }

    public static void RefreshPlayerStats()
    {
        try { PlayerStats.Instance?.RecalculateAllStats(); }
        catch (Exception e) { Plugin.L.LogWarning($"Stat refresh failed: {e.Message}"); }
    }

    public static bool IsRussian()
    {
        try
        {
            var code = LocalizationSettings.SelectedLocale?.Identifier.Code;
            return code != null && code.StartsWith("ru", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
