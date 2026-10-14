using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal static class CombatMaximizingRoute
{
    public const string StrategyId = "max-combats";
    // Return only future nodes, excluding the current room and including the destination.
    // Memoized longest weighted path through the upward map DAG. Ties use column order.
    public static IReadOnlyList<MapPoint>? Find(MapPoint current, MapPoint shop)
    {
        if (shop.PointType != MapPointType.Shop || shop.coord.row <= current.coord.row)
            return null;

        var scores = new Dictionary<MapPoint, int>();
        var next = new Dictionary<MapPoint, MapPoint>();
        int Score(MapPoint point)
        {
            if (ReferenceEquals(point, shop)) return 0;
            if (scores.TryGetValue(point, out var cached)) return cached;
            var best = -1;
            foreach (var child in point.Children.OrderBy(p => p.coord.col))
            {
                if (child.coord.row <= point.coord.row || child.coord.row > shop.coord.row)
                    continue;
                var score = Score(child);
                if (score < 0) continue;
                score += child.PointType is MapPointType.Monster or MapPointType.Elite ? 1 : 0;
                if (score <= best) continue;
                best = score;
                next[point] = child;
            }
            scores[point] = best;
            return best;
        }

        if (Score(current) < 0) return null;
        var path = new List<MapPoint>();
        for (var point = current; !ReferenceEquals(point, shop);)
        {
            point = next[point];
            path.Add(point);
        }
        return path;
    }
}
