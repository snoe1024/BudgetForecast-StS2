using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal readonly record struct GoldEstimate(decimal Minimum, decimal Average);

internal static class CombatGoldCalculator
{
    // sts2 v0.111.0: EncounterModel.MinGoldReward / MaxGoldReward.
    // Full defeat and reward collection assumed; no encounter generation or reward RNG calls.
    public static GoldEstimate ForPoint(MapPointType type, int ascension)
    {
        var (min, max) = type switch
        {
            MapPointType.Monster => (10, 20),
            MapPointType.Elite => (35, 45),
            _ => (0, 0)
        };
        if (ascension >= (int)AscensionLevel.Poverty)
        {
            min = (int)(min * AscensionHelper.PovertyAscensionGoldMultiplier);
            max = (int)(max * AscensionHelper.PovertyAscensionGoldMultiplier);
        }
        return new GoldEstimate(min, (min + max) / 2m);
    }

    public static GoldEstimate Sum(IEnumerable<MapPoint> futurePath, int ascension)
    {
        decimal minimum = 0, average = 0;
        foreach (var point in futurePath)
        {
            var gold = ForPoint(point.PointType, ascension);
            minimum += gold.Minimum;
            average += gold.Average;
        }
        return new GoldEstimate(minimum, average);
    }
}
