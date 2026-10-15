using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Cards;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal sealed record GoldForecastRules(IReadOnlyList<decimal>? GainMultipliers = null,
    bool CannotGainGold = false, IReadOnlyList<int>? CombatBonuses = null,
    IReadOnlyList<decimal>? FloorBonuses = null, decimal? CardGold = null, bool DoubleCards = false,
    IReadOnlyDictionary<MapCoord, IReadOnlyList<QuestGoldBonus>>? QuestBonuses = null, int? TreasureRoomsEntered = null);

internal sealed record QuestGoldBonus(int Claim, decimal Amount);

internal readonly record struct PlayerGoldPrediction(int CurrentGold, GoldEstimate Gains)
{
    public decimal Minimum => CurrentGold + Gains.Minimum;
    public decimal Average => CurrentGold + Gains.Average;
}

internal static class PlayerGoldForecaster
{
    public static PlayerGoldPrediction Predict(Player player, IReadOnlyList<MapPoint> futurePath)
        => Predict(player.Gold, player.RunState.AscensionLevel, futurePath, RulesFor(player));

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
                // No future card choices or turn counts are assumed for route income.
            }
        }
        return new GoldForecastRules(multipliers, blocked, combatBonuses, floorBonuses, cardGold, doubleCards,
            TreasureRoomsEntered: treasureRooms);
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
        GoldForecastRules rules)
    {
        decimal minimum = 0, average = 0;
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
            var (min, max) = CombatGoldCalculator.RangeForPoint(point.PointType, ascension);
            var combat = CombatReward(min, max, rules);
            minimum += combat.Minimum;
            average += combat.Average;
        }
        if (rules.CannotGainGold) minimum = average = 0; // Existing gold is never removed by Ectoplasm.
        return new PlayerGoldPrediction(currentGold, new GoldEstimate(minimum, average));
    }
}
