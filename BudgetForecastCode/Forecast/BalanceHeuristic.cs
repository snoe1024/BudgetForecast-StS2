using System.Globalization;
using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal readonly record struct RouteMemory(int Combats = 0, int Elites = 0, int Rests = 0,
    int SinceRest = -1, int Straight = 0);

internal sealed record BalanceContext(decimal LowestHpRatio, RouteMemory Initial,
    IReadOnlySet<MapCoord> EasyCombats)
{
    public static readonly BalanceContext Default = new(1m, new RouteMemory(SinceRest: -1), new HashSet<MapCoord>());
    public string CacheKey => LowestHpRatio.ToString(CultureInfo.InvariantCulture) + ":" + Initial + ":" +
        string.Join(";", EasyCombats.OrderBy(coord => coord.row).ThenBy(coord => coord.col).Select(coord => $"{coord.col},{coord.row}"));
}

internal static class BalanceHeuristic
{
    // Policy weights, not game quantities. HP is observed now; future damage/healing is never simulated.
    public static int BaseScore(MapPointType type) => type switch
    {
        MapPointType.Monster => 12, MapPointType.Unknown => 11, MapPointType.Elite => 20,
        MapPointType.RestSite => 24, MapPointType.Treasure => 10, MapPointType.Shop => 9, _ => 0
    };

    public static (decimal Score, RouteMemory Next) Step(MapPoint point, RouteMemory memory,
        BalanceContext context, int distance, bool destination = false)
    {
        var type = point.PointType;
        var fight = type is MapPointType.Monster or MapPointType.Elite;
        var easy = fight && context.EasyCombats.Contains(point.coord);
        var hp = Math.Clamp(context.LowestHpRatio, 0, 1);
        var urgent = hp <= .25m ? 1 + (.25m - hp) / .25m : 0;
        decimal score = destination ? 0 : BaseScore(type);
        distance = Math.Max(1, distance);
        var sinceRest = memory.SinceRest < 0 ? -1 : memory.SinceRest + 1;
        var combats = memory.Combats + (fight ? 1 : 0);
        if (fight && !easy)
        {
            if (type == MapPointType.Monster) score -= 3 * memory.Combats;
            if (combats >= 5) score -= 12 * (combats - 4) * (combats - 4);
            score -= urgent * (type == MapPointType.Elite ? 70 : 40) / distance;
            if (type == MapPointType.Elite)
            {
                var restCredit = Math.Min(memory.Rests, 3);
                score -= memory.Elites * Math.Max(4, 40 - 30 * hp - 4 * restCredit);
                if (memory.Combats >= 3)
                    score -= (memory.Combats - 2) * Math.Max(2, 16 - 12 * hp - 2 * restCredit);
                if (hp >= 2m / 3m) score += 12 * hp / distance;
            }
        }
        if (type == MapPointType.Elite && sinceRest > 0) score += 24m / sinceRest;
        if (easy) score += type == MapPointType.Elite ? 210 : 150;
        if (type == MapPointType.RestSite) score += urgent * 48 / distance;

        var branches = point.Children.Count(child => child.coord.row > point.coord.row);
        var straight = branches > 1 ? 0 : memory.Straight + 1;
        if (!destination)
        {
            score += 1.5m * Math.Min(Math.Max(0, branches - 1), 2);
            if (straight >= 5) score -= straight - 4;
        }
        var next = type == MapPointType.RestSite
            ? new RouteMemory(0, 0, Math.Min(memory.Rests + 1, 3), 0, straight)
            : new RouteMemory(combats, memory.Elites + (type == MapPointType.Elite ? 1 : 0),
                memory.Rests, sinceRest, straight);
        return (score, next);
    }

    public static decimal Evaluate(MapPoint current, IReadOnlyList<MapPoint> path, BalanceContext context)
    {
        var memory = context.Initial;
        decimal score = 0;
        foreach (var point in path)
        {
            var step = Step(point, memory, context, point.coord.row - current.coord.row,
                ReferenceEquals(point, path.Last()) && point.PointType == MapPointType.Shop);
            score += step.Score;
            memory = step.Next;
        }
        return score;
    }
}
