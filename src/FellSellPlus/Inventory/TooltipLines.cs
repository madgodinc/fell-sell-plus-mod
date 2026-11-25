using System.Text;
using HarmonyLib;

namespace FellSellPlus.Inventory;

// Extra lines under an item's tooltip: where your copies are, carried weight, Needed recipes,
// the active loot rule and (optional spoiler) the hidden ideal price.
[HarmonyPatch(typeof(InventoryTooltipUI), nameof(InventoryTooltipUI.ShowTooltip))]
internal static class TooltipLinesPatch
{
    static void Prefix(ref string content, ItemData item)
    {
        if (item == null) return;
        PurgeTick.NoteHover(item);
        if (!InventorySettings.TooltipInfo.Value) return;
        try { content += Lines(item); }
        catch (System.Exception e) { Plugin.L.LogWarning($"Tooltip lines skipped: {e.Message}"); }
    }

    static string Lines(ItemData item)
    {
        bool ru = Progression.GameQuery.IsRussian();
        string id = item.name;
        int carried = ItemCounts.Carried(id), stored = ItemCounts.Stored(id), placed = ItemCounts.Placed(id);
        var sb = new StringBuilder("\n<size=80%><color=#c8c8c8>");
        sb.Append(ru ? "Всего: " : "You have: ").Append(carried + stored + placed);
        sb.Append(ru ? $" (с собой {carried}, в сундуках {stored}" : $" (carried {carried}, chests {stored}");
        if (placed > 0) sb.Append(ru ? $", расставлено {placed}" : $", placed {placed}");
        sb.Append(')');
        if (carried > 0 && item.weight > 0f)
            sb.Append(ru ? $"\nНесёте: {carried * item.weight:0.#} кг" : $"\nCarried weight: {carried * item.weight:0.#} kg");
        int missing = NeedList.Missing(id);
        if (missing > 0) sb.Append(ru ? $"\n<color=#ffcc66>Нужно ещё {missing} для рецептов</color>" : $"\n<color=#ffcc66>Need {missing} more for recipes</color>");
        var r = LootRules.Get(id);
        if (r != null && r.Rule != RuleKind.Default)
            sb.Append(ru ? "\nПравило подбора: " : "\nLoot rule: ").Append(r.Rule)
              .Append(r.Rule is RuleKind.Total or RuleKind.ThisRun ? $" {r.Limit}" : "");
        if (InventorySettings.PurgeInfo.Value)
        {
            int best = Value.Best(item);
            var perKg = Value.PerKg(item);
            if (best > 0) sb.Append(ru ? $"\nЦена: {best} G" : $"\nValue: {best} G").Append(perKg.HasValue ? $" ({perKg.Value:0} G/{(ru ? "кг" : "kg")})" : "");
            else sb.Append(ru ? "\nЦена: неизвестна (ещё не продавали)" : "\nValue: unknown (never sold)");
            if (item.weight > 0.0001f && carried > 0)
            {
                var (rank, total) = PurgeOrder.Rank(item, ItemCounts.CarriedSlots());
                if (rank > 0) sb.Append(ru ? $"\nНа выброс: {rank}/{total}" : $"\nPurge: {rank}/{total}");
            }
        }
        if (KeepList.Has(id)) sb.Append(ru ? "\n<color=#88ccff>Оставить (K)</color>" : "\n<color=#88ccff>Keep (K)</color>");
        if (InventorySettings.ShowIdealPrice.Value && item.idealPrice > 0)
            sb.Append(ru ? $"\nИдеальная цена: {item.idealPrice}" : $"\nIdeal price: {item.idealPrice}");
        sb.Append("</color></size>");
        return sb.ToString();
    }
}
