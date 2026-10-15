using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal static class BalanceContextReader
{
    public static BalanceContext Read(IRunState run)
    {
        var hp = run.Players.Count == 0 ? 1m : run.Players.Min(player => player.Creature.MaxHp <= 0
            ? 0m : (decimal)player.Creature.CurrentHp / player.Creature.MaxHp);
        var memory = new RouteMemory(SinceRest: -1);
        if (run.CurrentActIndex >= 0 && run.CurrentActIndex < run.MapPointHistory.Count)
        foreach (var entry in run.MapPointHistory[run.CurrentActIndex])
        {
            if (entry.MapPointType == MapPointType.RestSite)
                memory = new RouteMemory(0, 0, Math.Min(3, memory.Rests + 1), 0);
            else
            {
                var combats = entry.Rooms.Count(room => room.RoomType is RoomType.Monster or RoomType.Elite);
                var elites = entry.Rooms.Count(room => room.RoomType == RoomType.Elite);
                memory = memory with { Combats = memory.Combats + combats, Elites = memory.Elites + elites,
                    SinceRest = memory.SinceRest < 0 ? -1 : memory.SinceRest + 1 };
            }
        }
        if (run is RunState state)
        {
            var straight = 0;
            foreach (var coord in state.VisitedMapCoords)
            {
                var point = run.Map.GetPoint(coord);
                if (point == null) continue;
                straight = point.Children.Count(child => child.coord.row > point.coord.row) > 1 ? 0 : straight + 1;
            }
            memory = memory with { Straight = straight };
        }
        var coats = run.Players.SelectMany(player => player.Relics).OfType<FurCoat>()
            .Where(coat => !coat.IsMelted && !coat.HasBeenRemovedFromState && coat.Owner.IsActiveForHooks).ToHashSet();
        var marked = run.Map.GetAllMapPoints().Where(point => point.Quests.OfType<FurCoat>().Any(coats.Contains))
            .Select(point => point.coord).ToHashSet();
        return new BalanceContext(hp, memory, marked);
    }
}
