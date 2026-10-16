using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Cards;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal sealed record GoldForecastRules(IReadOnlyList<decimal>? GainMultipliers = null,
    bool CannotGainGold = false, IReadOnlyList<int>? CombatBonuses = null,
    IReadOnlyList<decimal>? FloorBonuses = null, decimal? CardGold = null, bool DoubleCards = false,
    IReadOnlyDictionary<MapCoord, IReadOnlyList<QuestGoldBonus>>? QuestBonuses = null, int? TreasureRoomsEntered = null,
    decimal SealLossPerTurn = 0, decimal NormalTurns = 4, decimal EliteTurns = 6);

internal sealed record QuestGoldBonus(int Claim, decimal Amount);

internal readonly record struct PlayerGoldPrediction(int CurrentGold, GoldEstimate Gains)
{
    public decimal Minimum => CurrentGold + Gains.Minimum;
    public decimal Average => CurrentGold + Gains.Average;
}

internal static class PlayerGoldForecaster
{
    public static decimal NormalCombatTurns { get; set; } = 4;
    public static decimal EliteCombatTurns { get; set; } = 6;

    public static PlayerGoldPrediction Predict(Player player, IReadOnlyList<MapPoint> futurePath,
        GoldEstimate pending = default)
        => Predict(player.Gold, player.RunState.AscensionLevel, futurePath, RulesFor(player), pending);

    public static GoldForecastRules RulesFor(Player player)
    {
        var cards = player.Deck.Cards.OfType<SpoilsMap>().ToArray();
        var bonuses = player.RunState.Map.GetAllMapPoints()
            .Where(point => point.PointType == MapPointType.Treasure && point.Quests.Any(quest => quest is SpoilsMap))
            .ToDictionary(point => point.coord, _ => (IReadOnlyList<QuestGoldBonus>)cards
                .Select((card, index) => new QuestGoldBonus(index, card.DynamicVars.Gold.BaseValue)).ToArray());
        return RulesForRelics(player.Relics) with { QuestBonuses = bonuses };
    }

    public static GoldForecastRules RulesForRelics(IEnumerable<RelicModel> relics)
    {
        var multipliers = new List<decimal>();
        var combatBonuses = new List<int>();
        var floorBonuses = new List<decimal>();
        var blocked = false;
        decimal? cardGold = null;
        var doubleCards = false;
        int? treasureRooms = null;
        decimal sealLoss = 0;
        foreach (var relic in relics.Where(relic => !relic.IsMelted))
        {
            switch (relic)
            {
                case BowlerHat hat: multipliers.Add(hat.DynamicVars["GoldIncrease"].BaseValue); break;
                case AmethystAubergine aubergine: combatBonuses.Add(aubergine.DynamicVars.Gold.IntValue); break;
                case MawBank bank when !bank.HasItemBeenBought: floorBonuses.Add(bank.DynamicVars.Gold.BaseValue); break;
                case Ectoplasm: blocked = true; break;
                case LuckyFysh lucky: cardGold = lucky.DynamicVars.Gold.BaseValue; break;
                case BingBong: doubleCards = true; break;
                case SilverCrucible crucible: treasureRooms = crucible.TreasureRoomsEntered; break;
                case SealOfGold seal: sealLoss += seal.DynamicVars.Gold.BaseValue; break;
                // Future card choices are not assumed for route income.
            }
        }
        return new GoldForecastRules(multipliers, blocked, combatBonuses, floorBonuses, cardGold, doubleCards,
            TreasureRoomsEntered: treasureRooms, SealLossPerTurn: sealLoss,
            NormalTurns: NormalCombatTurns, EliteTurns: EliteCombatTurns);
    }

    public static decimal ApplyGain(decimal amount, GoldForecastRules rules)
    {
        if (rules.CannotGainGold) return 0;
        foreach (var multiplier in rules.GainMultipliers ?? Array.Empty<decimal>()) amount *= multiplier;
        return decimal.Truncate(amount);
    }

    public static GoldEstimate CombatReward(int min, int max, GoldForecastRules rules, bool includeBonuses = true)
    {
        decimal total = 0;
        for (var drop = min; drop <= max; drop++) total += ApplyGain(drop, rules);
        var bonus = includeBonuses
            ? (rules.CombatBonuses ?? Array.Empty<int>()).Sum(amount => ApplyGain(amount, rules)) : 0;
        return new GoldEstimate(ApplyGain(min, rules) + bonus, total / (max - min + 1) + bonus);
    }

    // Pure calculation from public inputs. Does not invoke hooks, rewards, RNGs or game commands.
    public static PlayerGoldPrediction Predict(int currentGold, int ascension, IEnumerable<MapPoint> futurePath,
        GoldForecastRules rules, GoldEstimate pending = default)
    {
        decimal minimum = pending.Minimum, average = pending.Average;
        var claimed = new HashSet<int>();
        var treasureRooms = rules.TreasureRoomsEntered;
        foreach (var point in futurePath)
        {
            // Includes entering the destination shop; spending at intermediate shops is not assumed.
            foreach (var floorGold in rules.FloorBonuses ?? Array.Empty<decimal>())
            {
                var gold = ApplyGain(floorGold, rules);
                minimum += gold;
                average += gold;
            }
            if (point.PointType == MapPointType.Treasure)
            {
                if (treasureRooms.HasValue) treasureRooms++;
                if ((!treasureRooms.HasValue || treasureRooms > 1) && rules.QuestBonuses != null &&
                    rules.QuestBonuses.TryGetValue(point.coord, out var bonuses))
                    foreach (var bonus in bonuses.Where(bonus => claimed.Add(bonus.Claim)))
                    {
                        var gold = ApplyGain(bonus.Amount, rules);
                        minimum += gold;
                        average += gold;
                    }
            }
            if (point.PointType is not (MapPointType.Monster or MapPointType.Elite)) continue;
            var turns = point.PointType == MapPointType.Elite ? rules.EliteTurns : rules.NormalTurns;
            minimum -= SealCost(currentGold + minimum, turns, rules);
            average -= SealCost(currentGold + average, turns, rules);
            var (min, max) = CombatGoldCalculator.RangeForPoint(point.PointType, ascension);
            var combat = CombatReward(min, max, rules);
            minimum += combat.Minimum;
            average += combat.Average;
        }
        return new PlayerGoldPrediction(currentGold, new GoldEstimate(minimum, average));
    }

    // Spending is unaffected by gain multipliers/Ectoplasm. Native Seal cannot pay without a full payment.
    public static decimal SealCost(decimal availableGold, decimal turns, GoldForecastRules rules) =>
        rules.SealLossPerTurn <= 0 ? 0 : rules.SealLossPerTurn *
            Math.Min(Math.Max(0, turns), decimal.Floor(Math.Max(0, availableGold) / rules.SealLossPerTurn));

    public static GoldEstimate ArrivalChange(int currentGold, int ascension, IReadOnlyList<MapPoint> path,
        GoldForecastRules beforeRules, GoldForecastRules afterRules, decimal instant = 0)
    {
        var before = Predict(currentGold, ascension, path, beforeRules);
        var after = Predict(currentGold, ascension, path, afterRules, new GoldEstimate(instant, instant));
        return new GoldEstimate(after.Minimum - before.Minimum, after.Average - before.Average);
    }

    // Public gold distributions only; evaluate both relic sets against the SAME drop, without any RNG calls.
    public static decimal ArrivalChangeMaximum(int currentGold, int ascension, IReadOnlyList<MapPoint> path,
        GoldForecastRules beforeRules, GoldForecastRules afterRules, decimal instant = 0)
    {
        decimal Budget(GoldForecastRules r) => r.SealLossPerTurn * path.Sum(p => p.PointType switch
            { MapPointType.Monster => r.NormalTurns, MapPointType.Elite => r.EliteTurns, _ => 0 });
        decimal Reward(int drop, GoldForecastRules r) => ApplyGain(drop, r) +
            (r.CombatBonuses ?? Array.Empty<int>()).Sum(value => ApplyGain(value, r));
        // With all payments funded, each fight can be maximized independently, including multiplier rounding.
        if (currentGold >= Budget(beforeRules) && currentGold + instant >= Budget(afterRules))
        {
            var result = ArrivalChange(currentGold, ascension, path, beforeRules, afterRules, instant).Minimum;
            foreach (var point in path.Where(p => p.PointType is MapPointType.Monster or MapPointType.Elite))
            {
                var (min, max) = CombatGoldCalculator.RangeForPoint(point.PointType, ascension);
                var best = Enumerable.Range(min, max-min+1).Max(drop => Reward(drop, afterRules)-Reward(drop, beforeRules));
                result += best - (Reward(min, afterRules)-Reward(min, beforeRules));
            }
            return result;
        }
        // Low wallets can change which Seal payments occur. Retain reachable wallet pairs, merging equal states.
        decimal[] FixedIncome(GoldForecastRules r)
        {
            var claimed = new HashSet<int>();
            var treasures = r.TreasureRoomsEntered;
            return path.Select(point =>
            {
                var gold = (r.FloorBonuses ?? Array.Empty<decimal>()).Sum(value => ApplyGain(value,r));
                if (point.PointType == MapPointType.Treasure)
                {
                    if (treasures.HasValue) treasures++;
                    if ((!treasures.HasValue || treasures > 1) && r.QuestBonuses != null &&
                        r.QuestBonuses.TryGetValue(point.coord,out var bonuses))
                        foreach (var bonus in bonuses.Where(bonus => claimed.Add(bonus.Claim))) gold += ApplyGain(bonus.Amount,r);
                }
                return gold;
            }).ToArray();
        }
        var beforeIncome=FixedIncome(beforeRules);
        var afterIncome=FixedIncome(afterRules);
        var states=new HashSet<(decimal Before,decimal After)> { (currentGold,currentGold+instant) };
        for(var index=0;index<path.Count;index++)
        {
            var type=path[index].PointType;
            var next=new HashSet<(decimal Before,decimal After)>();
            foreach(var state in states)
            {
                var before=state.Before+beforeIncome[index];
                var after=state.After+afterIncome[index];
                if(type is MapPointType.Monster or MapPointType.Elite)
                {
                    before-=SealCost(before,type==MapPointType.Elite?beforeRules.EliteTurns:beforeRules.NormalTurns,beforeRules);
                    after-=SealCost(after,type==MapPointType.Elite?afterRules.EliteTurns:afterRules.NormalTurns,afterRules);
                    var (min,max)=CombatGoldCalculator.RangeForPoint(type,ascension);
                    for(var drop=min;drop<=max;drop++) next.Add((before+Reward(drop,beforeRules),after+Reward(drop,afterRules)));
                }
                else next.Add((before,after));
            }
            states=next;
        }
        return states.Max(state=>state.After-state.Before);
    }
}