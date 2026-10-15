using System.Globalization;
using BudgetForecast.BudgetForecastCode.Forecast;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class ForecastDetailText
{
    public static string Format(IEnumerable<ForecastDetail> details, bool showAverage)
        => string.Join("\n", details.Select(detail =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(detail.Title)) parts.Add(detail.Title + ":");
            if (!string.IsNullOrEmpty(detail.Icons)) parts.Add(detail.Icons);
            if (detail.Delta is { } delta)
            {
                parts.Add(Signed(delta.Minimum) + "G");
                if (showAverage && delta.Average != delta.Minimum)
                {
                    var mean = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.average");
                    mean.Add("average", Signed(delta.Average));
                    parts.Add(mean.GetFormattedText());
                }
            }
            if (detail.Cards is { } cards)
            {
                var line = new LocString("static_hover_tips", cards.Optional
                    ? "BUDGETFORECAST_SHOP.optionalCards" : "BUDGETFORECAST_SHOP.cards");
                line.Add("icon", Icon(ModelDb.Relic<LuckyFysh>().IconPath));
                line.Add("count", cards.Count);
                line.Add("gold", Signed(cards.PerCard));
                line.Add("copies", cards.Doubled ? " ×2 " + Icon(ModelDb.Relic<BingBong>().IconPath) : "");
                parts.Add(line.GetFormattedText());
            }
            if (detail.Note != null)
                parts.Add(new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.note." + detail.Note).GetFormattedText());
            return string.Join(" ", parts);
        }));

    private static string Signed(decimal amount) => amount.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
    private static string Icon(string path) => $"[img width=24 height=24]{path}[/img]";
}
