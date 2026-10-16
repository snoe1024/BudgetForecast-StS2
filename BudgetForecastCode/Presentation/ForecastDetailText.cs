using System.Globalization;
using BudgetForecast.BudgetForecastCode.Forecast;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class ForecastDetailText
{
    public static string Format(IEnumerable<ForecastDetail> details, bool expanded)
        => string.Join("\n", ForecastDetailGroups.Build(details).Select(group =>
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(group.Title)) parts.Add(group.Title + ":");
            if (!expanded)
            {
                // Keep the identity of each choice, but move effect breakdowns and notes behind Shift.
                if (string.IsNullOrEmpty(group.Title))
                    parts.AddRange(group.Parts.Select(detail => detail.Icons).Where(icons => !string.IsNullOrEmpty(icons)).Distinct());
                var maximum = CompactForecastDetails.Maximum(group);
                if (maximum.HasValue) parts.Add(Signed(maximum.Value) + "G");
                else return ""; // Unknown rewards are described in the expanded view only.
                return string.Join(" ", parts);
            }
            parts.AddRange(group.Parts.Select(detail => detail.Icons).Where(icons => !string.IsNullOrEmpty(icons)).Distinct());
            var deltas = group.Parts.Where(detail => detail.Delta.HasValue).Select(detail => detail.Delta!.Value).ToArray();
            if (deltas.Length > 0)
            {
                var delta = new GoldEstimate(deltas.Sum(value => value.Minimum), deltas.Sum(value => value.Average));
                parts.Add(Signed(delta.Minimum) + "G");
                if (delta.Average != delta.Minimum)
                {
                    var mean = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.average");
                    mean.Add("average", Signed(delta.Average));
                    parts.Add(mean.GetFormattedText());
                }
            }
            foreach (var cards in group.Parts.Select(detail => detail.Cards).OfType<CardGoldHint>())
            {
                var line = new LocString("static_hover_tips", cards.Optional
                    ? "BUDGETFORECAST_SHOP.optionalCards" : "BUDGETFORECAST_SHOP.cards");
                line.Add("icon", Icon(ModelDb.Relic<LuckyFysh>().IconPath));
                line.Add("count", cards.Count);
                line.Add("gold", Signed(cards.PerCard));
                line.Add("copies", cards.Doubled ? " ×2 " + Icon(ModelDb.Relic<BingBong>().IconPath) : "");
                parts.Add(line.GetFormattedText());
            }
            foreach (var note in group.Parts.Select(detail => detail.Note).OfType<string>().Distinct())
                parts.Add(new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.note." + note).GetFormattedText());
            return string.Join(" ", parts);
        }).Where(line => !string.IsNullOrEmpty(line)));

    private static string Signed(decimal amount) => amount.ToString("+0.#;-0.#;0", CultureInfo.InvariantCulture);
    private static string Icon(string path) => $"[img width=24 height=24]{path}[/img]";
}
