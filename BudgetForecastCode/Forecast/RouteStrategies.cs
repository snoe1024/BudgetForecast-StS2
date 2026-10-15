using Godot;
using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal enum RouteStrategy { Balance, MinimizeCombats, MaximizeRest, MaximizeEvents, MaximizeElites }

internal static class RouteStrategies
{
    public static RouteStrategy Next(RouteStrategy strategy, int direction) =>
        (RouteStrategy)(((int)strategy + direction + 5) % 5);

    public static int Score(RouteStrategy strategy, MapPointType type) => strategy switch
    {
        RouteStrategy.Balance => BalanceHeuristic.BaseScore(type),
        RouteStrategy.MinimizeCombats => type is MapPointType.Monster or MapPointType.Elite ? -1 : 0,
        RouteStrategy.MaximizeRest => type == MapPointType.RestSite ? 1 : 0,
        RouteStrategy.MaximizeEvents => type == MapPointType.Unknown ? 1 : 0,
        RouteStrategy.MaximizeElites => type == MapPointType.Elite ? 1 : 0,
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    public static Color Color(RouteStrategy strategy) => strategy switch
    {
        RouteStrategy.Balance => new Color(0.2f, 0.8f, 0.35f, 0.7f),
        RouteStrategy.MinimizeCombats => new Color(1f, 0.4f, 0.7f, 0.7f),
        RouteStrategy.MaximizeRest => new Color(1f, 0.55f, 0.15f, 0.7f),
        RouteStrategy.MaximizeEvents => new Color(1f, 0.9f, 0.1f, 0.7f),
        RouteStrategy.MaximizeElites => new Color(0.95f, 0.2f, 0.2f, 0.7f),
        _ => throw new ArgumentOutOfRangeException(nameof(strategy))
    };

    public static IReadOnlyList<MapPoint>? Find(MapPoint current, MapPoint shop, RouteStrategy strategy,
        BalanceContext? context = null) => StatefulRoute.Find(current, shop, strategy, context ?? BalanceContext.Default);
}
