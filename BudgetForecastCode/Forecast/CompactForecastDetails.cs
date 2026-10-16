namespace BudgetForecast.BudgetForecastCode.Forecast;

internal static class CompactForecastDetails
{
    // Forced cards are already included in Delta. Optional cards are additional income only in the upper summary.
    public static decimal? Maximum(ForecastDetailGroup group)
    {
        var values = group.Parts.Where(part => part.Delta.HasValue || part.MaximumDelta.HasValue).ToArray();
        var optional = group.Parts.Select(part => part.Cards).OfType<CardGoldHint>()
            .Where(cards => cards.Optional).ToArray();
        if (values.Length == 0 && optional.Length == 0) return null;
        return values.Sum(part => part.MaximumDelta ?? part.Delta!.Value.Minimum) +
            optional.Sum(cards => cards.PerCard * cards.Count * (cards.Doubled ? 2 : 1));
    }
}
