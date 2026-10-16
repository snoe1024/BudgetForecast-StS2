using Godot;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal static class RouteStroke
{
    // Native Line2D connects node centers directly, including portions outside the current viewport.
    public static bool Draw(IReadOnlyList<Vector2> corners, Action<Vector2> begin,
        Action<Vector2> append, Action stop)
    {
        if (corners.Any(point => !point.IsFinite())) throw new ArgumentException("Non-finite route coordinate.");
        if (corners.Count < 2) return false;
        try
        {
            begin(corners[0]);
            for (var i = 1; i < corners.Count; i++) append(corners[i]);
        }
        finally { stop(); }
        return true;
    }
}
