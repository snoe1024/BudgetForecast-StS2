using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal static class CombatMaximizingRoute
{
    public const string StrategyId = "max-combats";
    public static IReadOnlyList<MapPoint>? Find(MapPoint current, MapPoint shop) =>
        WeightedRoute.Find(current, shop, point => point.PointType is MapPointType.Monster or MapPointType.Elite ? 1 : 0);
}
