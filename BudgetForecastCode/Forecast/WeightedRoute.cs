using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal static class WeightedRoute
{
    // Nullable scores distinguish unreachable paths from valid paths with negative weights.
    public static IReadOnlyList<MapPoint>? Find(MapPoint current, MapPoint shop, Func<MapPoint, int> weight)
    {
        if (shop.PointType != MapPointType.Shop || shop.coord.row <= current.coord.row) return null;
        var scores = new Dictionary<MapPoint, int?>();
        var next = new Dictionary<MapPoint, MapPoint>();
        int? Score(MapPoint point)
        {
            if (ReferenceEquals(point, shop)) return 0;
            if (scores.TryGetValue(point, out var cached)) return cached;
            int? best = null;
            foreach (var child in point.Children.OrderBy(p => p.coord.col))
            {
                if (child.coord.row <= point.coord.row || child.coord.row > shop.coord.row) continue;
                var remaining = Score(child);
                if (remaining == null) continue;
                var score = remaining.Value + weight(child);
                if (best != null && score <= best.Value) continue;
                best = score;
                next[point] = child;
            }
            scores[point] = best;
            return best;
        }
        if (Score(current) == null) return null;
        var path = new List<MapPoint>();
        for (var point = current; !ReferenceEquals(point, shop);)
        {
            point = next[point];
            path.Add(point);
        }
        return path;
    }
}
