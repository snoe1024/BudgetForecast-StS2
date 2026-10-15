using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal sealed record GoldOption(string IconPath, decimal Delta);
internal sealed record GoldChoice(IReadOnlyList<GoldOption> Options, bool ChooseOne);
internal sealed record CurrentRoomGold(GoldEstimate Combat, IReadOnlyList<GoldChoice> Choices,
    IReadOnlyList<ForecastDetail>? Details = null)
{
    public static readonly CurrentRoomGold Empty = new(new GoldEstimate(0, 0), Array.Empty<GoldChoice>());

    // Each reward is a distinct acquisition. Aubergine has already added its own displayed reward.
    public static GoldEstimate DisplayedGold(IEnumerable<int> amounts, GoldForecastRules rules)
    {
        var sum = amounts.Sum(amount => PlayerGoldForecaster.ApplyGain(amount, rules));
        return new GoldEstimate(sum, sum);
    }

    public static decimal? InstantDelta(RelicModel relic, int currentGold, GoldForecastRules rules)
    {
        var type = relic.GetType();
        if (GoldRelics.Classify(type) != GoldRelicKind.Instant) return null;
        return InstantDelta(type, relic is SilkenTress ? 0 : relic.DynamicVars.Gold.BaseValue, currentGold, rules);
    }

    public static decimal? InstantDelta(Type type, decimal rawGold, int currentGold, GoldForecastRules rules)
    {
        if (type == typeof(SilkenTress)) return -currentGold;
        if (GoldRelics.Classify(type) != GoldRelicKind.Instant) return null;
        var gold = PlayerGoldForecaster.ApplyGain(rawGold, rules);
        if (type == typeof(CursedPearl) && rules.CardGold.HasValue)
            gold += PlayerGoldForecaster.ApplyGain(rules.CardGold.Value, rules) * (rules.DoubleCards ? 2 : 1);
        return gold;
    }
}
