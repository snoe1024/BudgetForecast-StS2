using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal sealed class RouteCache
{
    private readonly Dictionary<(MapCoord Shop, string Strategy), IReadOnlyList<MapPoint>> _routes = new();
    private object? _map;
    private MapPoint? _source;
    private MapCoord _sourceCoord;

    private string? _contextKey;

    public int Count => _routes.Count;

    public bool IsFor(object map, MapPoint current) =>
        ReferenceEquals(_map, map) && ReferenceEquals(_source, current) && _sourceCoord == current.coord;

    public IReadOnlyList<MapPoint>? GetOrCompute(object map, MapPoint current, MapPoint shop,
        string strategy, Func<MapPoint, MapPoint, IReadOnlyList<MapPoint>?> calculate, out bool cacheHit, string? contextKey = null)
    {
        if (!IsFor(map, current) || _contextKey != contextKey)
        {
            Clear();
            _map = map;
            _source = current;
            _sourceCoord = current.coord;
            _contextKey = contextKey;
        }
        cacheHit = false;
        if (shop.PointType != MapPointType.Shop || shop.coord.row <= current.coord.row) return null;
        var key = (shop.coord, strategy);
        if (_routes.TryGetValue(key, out var route))
        {
            cacheHit = true;
            return route;
        }
        route = calculate(current, shop);
        if (route == null) return null; // Only reachable shops consume a cache slot.
        var snapshot = Array.AsReadOnly(route.ToArray());
        _routes.Add(key, snapshot);
        return snapshot;
    }

    public void Clear()
    {
        _routes.Clear();
        _map = null;
        _source = null;
        _contextKey = null;
    }
}
