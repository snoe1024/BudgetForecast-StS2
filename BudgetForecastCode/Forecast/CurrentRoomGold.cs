using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal sealed record GoldOption(string IconPath, decimal Delta);
internal sealed record GoldChoice(IReadOnlyList<GoldOption> Options, bool ChooseOne);
internal sealed record CurrentRoomGold(GoldEstimate Combat, IReadOnlyList<GoldChoice> Choices)
{
    public static readonly CurrentRoomGold Empty = new(new GoldEstimate(0, 0), Array.Empty<GoldChoice>());

    // Each reward is a distinct acquisition. Aubergine has already added its own displayed reward.
    public static GoldEstimate DisplayedGold(IEnumerable<int> amounts, GoldForecastRules rules)
    {
        var sum = amounts.Sum(amount => PlayerGoldForecaster.ApplyGain(amount, rules));
        return new GoldEstimate(sum, sum);
    }

    public static decimal? InstantDelta(RelicModel relic, int currentGold, GoldForecastRules rules)
        => relic switch
        {
            SilkenTress => -currentGold,
            OldCoin or GoldenPearl or CursedPearl or SignetRing
                => PlayerGoldForecaster.ApplyGain(relic.DynamicVars.Gold.BaseValue, rules),
            _ => null
        };
}
