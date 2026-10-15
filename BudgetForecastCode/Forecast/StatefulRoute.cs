using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal static class StatefulRoute
{
    // Prefix history is part of the memo key. Keeping only one suffix per map node is incorrect for streak scoring.
    public static IReadOnlyList<MapPoint>? Find(MapPoint current, MapPoint shop, RouteStrategy strategy, BalanceContext context)
    {
        if (shop.PointType != MapPointType.Shop || shop.coord.row <= current.coord.row) return null;
        var memo = new Dictionary<(MapPoint, RouteMemory), (decimal Primary, decimal Secondary)?>();
        var next = new Dictionary<(MapPoint, RouteMemory), (MapPoint Point, RouteMemory Memory)>();
        (decimal Primary, decimal Secondary)? Visit(MapPoint point, RouteMemory memory)
        {
            if (ReferenceEquals(point, shop)) return (0, 0);
            var key = (point, memory);
            if (memo.TryGetValue(key, out var cached)) return cached;
            (decimal Primary, decimal Secondary)? best = null;
            foreach (var child in point.Children.OrderBy(child => child.coord.col))
            {
                if (child.coord.row <= point.coord.row || child.coord.row > shop.coord.row) continue;
                var step = BalanceHeuristic.Step(child, memory, context, child.coord.row - current.coord.row,
                    ReferenceEquals(child, shop));
                var remaining = Visit(child, step.Next);
                if (remaining == null) continue;
                var score = strategy == RouteStrategy.Balance
                    ? (Primary: step.Score + remaining.Value.Primary, Secondary: 0m)
                    : (Primary: RouteStrategies.Score(strategy, child.PointType) + remaining.Value.Primary,
                        Secondary: step.Score + remaining.Value.Secondary);
                if (best.HasValue && score.CompareTo(best.Value) <= 0) continue;
                best = score;
                next[key] = (child, step.Next);
            }
            memo[key] = best;
            return best;
        }
        if (Visit(current, context.Initial) == null) return null;
        var path = new List<MapPoint>();
        var state = (Point: current, Memory: context.Initial);
        while (!ReferenceEquals(state.Point, shop))
        {
            state = next[(state.Point, state.Memory)];
            path.Add(state.Point);
        }
        return path;
    }
}
