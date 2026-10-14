using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal static class CardRemovalForecast
{
    private static readonly FieldInfo BaseCostField = typeof(MerchantEntry)
        .GetField("_cost", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static int ForPlayer(Player player)
    {
        // Detached price entry only: CalcCost reads ascension/removal count; no inventory or RNG generation.
        var entry = new MerchantCardRemovalEntry(player);
        decimal cost = (int)BaseCostField.GetValue(entry)!;
        foreach (var model in player.RunState.IterateHookListeners(null))
        {
            if (model is MembershipCard membership)
            {
                // The native hook skips remote owners. Read its identical discount without impersonating them.
                if (membership.Owner == player)
                    cost *= membership.DynamicVars["Discount"].BaseValue / 100m;
            }
            else
            {
                cost = model.ModifyMerchantPrice(player, entry, cost);
            }
        }
        return (int)cost; // Same final truncation as MerchantEntry.Cost.
    }
}
