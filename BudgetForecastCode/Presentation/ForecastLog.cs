using System.Globalization;
using BudgetForecast.BudgetForecastCode.Forecast;
using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class ForecastLog
{
    public static void Write(MapPoint current, IReadOnlyList<MapPoint> path,
        int currentGold, int ascension, GoldEstimate estimate)
    {
        static string Point(MapPoint point) => $"({point.coord.col},{point.coord.row}):{point.PointType}";
        static string Gold(decimal value) => value.ToString("0.#", CultureInfo.InvariantCulture);
        var monsters = path.Count(p => p.PointType == MapPointType.Monster);
        var elites = path.Count(p => p.PointType == MapPointType.Elite);
        MainFile.Logger.Info($"Forecast route (zero-based col,row): {Point(current)} -> " +
            string.Join(" -> ", path.Select(Point)) +
            $"; strategy=max-combats; monsters={monsters}; elites={elites}; A{ascension}; current={currentGold}G; full defeat/collection assumed");
        MainFile.Logger.Info($"ショップ到着時（最低値）: {Gold(currentGold + estimate.Minimum)}G");
        MainFile.Logger.Info($"ショップ到着時（平均値）: {Gold(currentGold + estimate.Average)}G");
    }
}
