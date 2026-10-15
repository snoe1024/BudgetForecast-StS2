using BudgetForecast.BudgetForecastCode.Forecast;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class EventGoldReader
{
    public static IReadOnlyList<ForecastDetail> Read(EventModel model, Player player, IReadOnlyList<MapPoint> path)
    {
        if (model.IsFinished || model.Description == null || model.CurrentOptions.Any(option => option.WasChosen))
            return Array.Empty<ForecastDetail>();
        var rules = PlayerGoldForecaster.RulesFor(player);
        var vars = model.DynamicVars.ToDictionary(pair => pair.Key, pair => pair.Value.BaseValue);
        var range = CombatGoldCalculator.RangeForPoint(MapPointType.Monster, player.RunState.AscensionLevel);
        vars["CombatMin"] = range.Min;
        vars["CombatMax"] = range.Max;
        vars["FoulPotionGold"] = ModelDb.Potion<FoulPotion>().DynamicVars.Gold.BaseValue;
        var snapshot = new EventSnapshot(model.CurrentOptions.Select(option => new EventChoiceSnapshot(option.TextKey,
            option.Title.GetFormattedText(), option.IsLocked || option.IsProceed, option.WasChosen)).ToArray(),
            vars, player.Gold, player.Deck.Cards.Count, player.Deck.Cards.Count(card => card.IsTransformable),
            player.Deck.Cards.Count(card => card.IsTransformable && card.Rarity == CardRarity.Basic),
            player.PotionSlots.Count(potion => potion == null), rules,
            player.Relics.OfType<LuckyFysh>().FirstOrDefault(relic => !relic.IsMelted)?.DynamicVars.Gold.BaseValue,
            player.Relics.Any(relic => !relic.IsMelted && relic is BingBong));
        var details = EventForecast.Evaluate(snapshot).ToList();
        if (model is RelicTrader) details.AddRange(Trades(model, player, path));
        if (model is FakeMerchant && player.Potions.Any(potion => potion is FoulPotion))
        {
            // The event's published fixed reward, without touching its merchant inventory/encounter generation.
            var reward = PlayerGoldForecaster.CombatReward(300, 300, rules);
            details.Add(new ForecastDetail(model.Title.GetFormattedText(), reward, Note: "foulFight"));
        }
        return details;
    }

    private static IEnumerable<ForecastDetail> Trades(EventModel model, Player player, IReadOnlyList<MapPoint> path)
    {
        // Never use OwnedRelics/NewRelics getters: they shuffle/pull relics when not initialized.
        var owned = Traverse.Create(model).Field("_ownedRelics").GetValue<IReadOnlyList<RelicModel>>();
        var offered = Traverse.Create(model).Field("_newRelics").GetValue<IReadOnlyList<RelicModel>>();
        if (owned == null || offered == null) yield break;
        var beforeRules = PlayerGoldForecaster.RulesFor(player);
        var before = PlayerGoldForecaster.Predict(0, player.RunState.AscensionLevel, path, beforeRules).Gains;
        foreach (var option in model.CurrentOptions.Where(option => !option.IsLocked && !option.IsProceed))
        {
            var index = option.TextKey.Split('.').Last() switch { "TOP" => 0, "MIDDLE" => 1, "BOTTOM" => 2, _ => -1 };
            if (index < 0 || index >= owned.Count || index >= offered.Count) continue;
            var oldRelic = owned[index];
            var newRelic = offered[index];
            if (GoldRelics.Classify(oldRelic.GetType()) == GoldRelicKind.Other &&
                GoldRelics.Classify(newRelic.GetType()) == GoldRelicKind.Other &&
                oldRelic is not BingBong && newRelic is not BingBong) continue;
            var afterRules = PlayerGoldForecaster.RulesForRelics(player.Relics.Where(relic => relic != oldRelic).Append(newRelic));
            var after = PlayerGoldForecaster.Predict(0, player.RunState.AscensionLevel, path, afterRules).Gains;
            var instant = CurrentRoomGold.InstantDelta(newRelic, player.Gold, afterRules) ?? 0;
            yield return new ForecastDetail(option.Title.GetFormattedText(),
                new GoldEstimate(after.Minimum - before.Minimum + instant, after.Average - before.Average + instant),
                Icons: Icon(oldRelic.IconPath) + " → " + Icon(newRelic.IconPath), Note: "trade");
        }
    }

    public static CardGoldHint? CardHint(Player player, int count, bool optional = true)
    {
        var lucky = player.Relics.OfType<LuckyFysh>().FirstOrDefault(relic => !relic.IsMelted);
        if (lucky == null || count <= 0) return null;
        return new CardGoldHint(PlayerGoldForecaster.ApplyGain(lucky.DynamicVars.Gold.BaseValue,
            PlayerGoldForecaster.RulesFor(player)), count, optional,
            player.Relics.Any(relic => !relic.IsMelted && relic is BingBong));
    }

    public static ForecastDetail? CarriedPotions(Player player)
    {
        var potions = player.Potions.OfType<FoulPotion>().ToArray();
        if (potions.Length == 0) return null;
        var amount = potions.Sum(potion => PlayerGoldForecaster.ApplyGain(potion.DynamicVars.Gold.BaseValue,
            PlayerGoldForecaster.RulesFor(player)));
        return new ForecastDetail(potions[0].Title.GetFormattedText(), new GoldEstimate(amount, amount),
            Icons: Icon(potions[0].ImagePath), Note: "carriedPotions");
    }

    private static string Icon(string path) => $"[img width=24 height=24]{path}[/img]";
}
