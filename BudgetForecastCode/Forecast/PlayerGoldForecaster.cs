using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal sealed record GoldForecastRules(IReadOnlyList<decimal>? GainMultipliers = null,
    bool CannotGainGold = false, IReadOnlyList<int>? CombatBonuses = null,
    IReadOnlyList<decimal>? FloorBonuses = null);

internal readonly record struct PlayerGoldPrediction(int CurrentGold, GoldEstimate Gains)
{
    public decimal Minimum => CurrentGold + Gains.Minimum;
    public decimal Average => CurrentGold + Gains.Average;
}

internal static class PlayerGoldForecaster
{
    public static PlayerGoldPrediction Predict(Player player, IReadOnlyList<MapPoint> futurePath)
    {
        var multipliers = new List<decimal>();
        var combatBonuses = new List<int>();
        var floorBonuses = new List<decimal>();
        var blocked = false;
        foreach (var relic in GoldRelics.AcquisitionModifiers(player))
        {
            switch (relic)
            {
                case BowlerHat hat: multipliers.Add(hat.DynamicVars["GoldIncrease"].BaseValue); break;
                case AmethystAubergine aubergine: combatBonuses.Add(aubergine.DynamicVars.Gold.IntValue); break;
                case MawBank bank when !bank.HasItemBeenBought: floorBonuses.Add(bank.DynamicVars.Gold.BaseValue); break;
                case Ectoplasm: blocked = true; break;
                // Lucky Fysh and Seal of Gold are visible reminders only. No future choices/turn counts assumed.
            }
        }
        return Predict(player.Gold, player.RunState.AscensionLevel, futurePath,
            new GoldForecastRules(multipliers, blocked, combatBonuses, floorBonuses));
    }

    // Pure calculation from public inputs. Does not invoke hooks, rewards, RNGs or game commands.
    public static PlayerGoldPrediction Predict(int currentGold, int ascension, IEnumerable<MapPoint> futurePath,
        GoldForecastRules rules)
    {
        decimal Gain(decimal amount)
        {
            foreach (var multiplier in rules.GainMultipliers ?? Array.Empty<decimal>()) amount *= multiplier;
            return decimal.Truncate(amount); // Once per acquisition, after every multiplier.
        }
        decimal minimum = 0, average = 0;
        foreach (var point in futurePath)
        {
            // Includes entering the destination shop; spending at intermediate shops is not assumed.
            foreach (var floorGold in rules.FloorBonuses ?? Array.Empty<decimal>())
            {
                var gold = Gain(floorGold);
                minimum += gold;
                average += gold;
            }
            if (point.PointType is not (MapPointType.Monster or MapPointType.Elite)) continue;
            var (min, max) = CombatGoldCalculator.RangeForPoint(point.PointType, ascension);
            minimum += Gain(min);
            decimal outcomeSum = 0;
            for (var drop = min; drop <= max; drop++) outcomeSum += Gain(drop);
            average += outcomeSum / (max - min + 1); // Mean of rounded outcomes, not rounded mean.
            foreach (var bonus in rules.CombatBonuses ?? Array.Empty<int>())
            {
                var gold = Gain(bonus); // Aubergine is its own reward, not part of the base roll.
                minimum += gold;
                average += gold;
            }
        }
        if (rules.CannotGainGold) minimum = average = 0; // Existing gold is never removed by Ectoplasm.
        return new PlayerGoldPrediction(currentGold, new GoldEstimate(minimum, average));
    }
}
