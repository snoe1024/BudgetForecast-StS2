using BudgetForecast.BudgetForecastCode.Forecast;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Entities.Cards;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class CurrentRoomGoldReader
{
    public static CurrentRoomGold Read(Player player, IReadOnlyList<MapPoint> path)
    {
        var run = player.RunState;
        var rules = PlayerGoldForecaster.RulesFor(player);
        // Room completion is authoritative even during setup/between turns. Old overlays may survive debug room jumps.
        // A pending current battle must take priority over any reward screen left from another room.
        if (run.CurrentRoom is CombatRoom combat && !combat.IsPreFinished &&
            combat.CombatState.Players.Contains(player) && !player.Creature.IsDead)
        {
            var finalBoss = combat.RoomType == RoomType.Boss && run.CurrentActIndex >= run.Acts.Count - 1;
            if (finalBoss) return CurrentRoomGold.Empty;
            var income = combat.Encounter.ShouldGiveRewards
                ? PlayerGoldForecaster.CombatReward(combat.Encounter.MinGoldReward, combat.Encounter.MaxGoldReward, rules)
                : new GoldEstimate(0, 0);
            var totalTurns = combat.RoomType switch { RoomType.Monster => rules.NormalTurns,
                RoomType.Elite => rules.EliteTurns, _ => 0 };
            // Already-started turns have already charged Seal; do not subtract their payments again.
            var remaining = Math.Max(0, totalTurns - (player.PlayerCombatState?.TurnNumber ?? 0));
            var spending = PlayerGoldForecaster.SealCost(player.Gold, remaining, rules);
            return new CurrentRoomGold(new GoldEstimate(income.Minimum - spending, income.Average - spending),
                Array.Empty<GoldChoice>());
        }
        // The map hides overlays. IsVisibleInTree would incorrectly discard the rewards beneath it.
        var screens = NOverlayStack.Instance?.GetChildren().OfType<NRewardsScreen>()
            .Where(candidate => !candidate.IsQueuedForDeletion() && candidate.IsNodeReady()).Reverse()
            ?? Enumerable.Empty<NRewardsScreen>();
        foreach (var screen in screens)
        {
            var set = Traverse.Create(screen).Field("_rewardsSet").GetValue<RewardsSet>();
            if (set == null || set.Room != null && !ReferenceEquals(set.Room, run.CurrentRoom)) continue;
            if (!LocalContext.IsMe(player) || screen.IsComplete) return CurrentRoomGold.Empty;
            if (set?.Player != player) return CurrentRoomGold.Empty;
            return ReadRewards(screen, player, rules);
        }
        if (EventOverlayGoldReader.Read(player) is { } details)
            return new CurrentRoomGold(new GoldEstimate(0, 0), Array.Empty<GoldChoice>(), details);

        if (run.CurrentRoom is not EventRoom room) return CurrentRoomGold.Empty;
        // Events is already populated/synchronized by the game, including remote players' ancient choices.
        var model = RunManager.Instance.EventSynchronizer.Events
            .FirstOrDefault(model => model.Owner == player && model.Id == room.ModelId);
        if (model is not AncientEventModel ancient)
            return model == null || !model.IsDeterministic && !model.IsShared && !LocalContext.IsMe(player)
                ? CurrentRoomGold.Empty : new CurrentRoomGold(new GoldEstimate(0, 0),
                Array.Empty<GoldChoice>(), EventGoldReader.Read(model, player, path));
        if (ancient == null || ancient.IsFinished || ancient.Description == null) return CurrentRoomGold.Empty;
        // WasChosen is set before the async obtain callback, so hide ALL alternatives immediately.
        if (ancient.CurrentOptions.Any(option => option.WasChosen)) return CurrentRoomGold.Empty;
        var offered = ancient.CurrentOptions.Where(option => !option.IsLocked && !option.IsProceed).ToArray();
        var options = offered
            .Select(option => option.Relic).OfType<RelicModel>()
            .Select(relic => Option(relic, player, rules)).OfType<GoldOption>().ToArray();
        var projections = offered.Where(option => option.Relic is { } relic &&
                GoldRelics.Classify(relic.GetType()) == GoldRelicKind.AcquisitionModifier)
            .Select(option =>
            {
                var relic = option.Relic!;
                var afterRules = PlayerGoldForecaster.RulesForRelics(player.Relics.Append(relic))
                    with { QuestBonuses = rules.QuestBonuses };
                return new ForecastDetail(option.Title.GetFormattedText(),
                    PlayerGoldForecaster.ArrivalChange(player.Gold, run.AscensionLevel, path, rules, afterRules),
                    Icons: $"[img width=24 height=24]{relic.IconPath}[/img]", Note: "relicRoute",
                    MaximumDelta: PlayerGoldForecaster.ArrivalChangeMaximum(player.Gold, run.AscensionLevel,
                        path, rules, afterRules));
            }).ToArray();
        return new CurrentRoomGold(new GoldEstimate(0, 0), options.Length == 0
            ? Array.Empty<GoldChoice>() : new[] { new GoldChoice(options, ChooseOne: true) }, projections);
    }

    private static CurrentRoomGold ReadRewards(NRewardsScreen screen, Player player, GoldForecastRules rules)
    {
        var buttons = Traverse.Create(screen).Field("_rewardButtons").GetValue<List<Control>>();
        var gold = new List<int>();
        var choices = new List<GoldChoice>();
        var cardRewards = 0;
        var foulPotions = new List<MegaCrit.Sts2.Core.Models.Potions.FoulPotion>();
        foreach (var control in buttons ?? Enumerable.Empty<Control>())
        {
            if (!GodotObject.IsInstanceValid(control) || control.IsQueuedForDeletion()) continue;
            if (control is NRewardButton { Reward: CardReward { IsPopulated: true, SuccessfullySelected: false } card }
                && card.Player == player && !card.Cards.Any(candidate => candidate.Pile?.Type == PileType.Deck))
                cardRewards++;
            if (control is NRewardButton button && Available(button, player))
            {
                if (button.Reward is GoldReward reward) gold.Add(reward.Amount);
                else if (button.Reward is PotionReward { Potion: MegaCrit.Sts2.Core.Models.Potions.FoulPotion potion,
                    ClaimedPotion: null }) foulPotions.Add(potion);
                else if (button.Reward is RelicReward relicReward && relicReward.ClaimedRelic == null &&
                         relicReward.Relic is { } relic && Option(relic, player, rules) is { } option)
                    choices.Add(new GoldChoice(new[] { option }, ChooseOne: false));
            }
            else if (control is NLinkedRewardSet linked)
            {
                // Chained rewards are mutually exclusive, even when several show gold.
                var options = linked.GetNode<Control>("%RewardContainer").GetChildren().OfType<NRewardButton>()
                    .Where(b => Available(b, player)).Select(b => b.Reward switch
                    {
                        GoldReward r => new GoldOption(MegaCrit.Sts2.Core.Helpers.ImageHelper.GetImagePath(
                            "ui/reward_screen/reward_icon_money.png"), PlayerGoldForecaster.ApplyGain(r.Amount, rules)),
                        RelicReward { ClaimedRelic: null, Relic: { } r } => Option(r, player, rules),
                        _ => null
                    }).OfType<GoldOption>().ToArray();
                if (options.Length > 0) choices.Add(new GoldChoice(options, ChooseOne: true));
            }
        }
        var hint = EventGoldReader.CardHint(player, cardRewards);
        var details = new List<ForecastDetail>();
        if (hint != null) details.Add(new ForecastDetail("", Cards: hint));
        var potionCount = Math.Min(foulPotions.Count, player.PotionSlots.Count(potion => potion == null));
        if (potionCount > 0)
        {
            var amount = PlayerGoldForecaster.ApplyGain(foulPotions[0].DynamicVars.Gold.BaseValue, rules) * potionCount;
            details.Add(new ForecastDetail(foulPotions[0].Title.GetFormattedText(), new GoldEstimate(amount, amount),
                Icons: $"×{potionCount}", Note: "potions"));
        }
        return new CurrentRoomGold(CurrentRoomGold.DisplayedGold(gold, rules), choices,
            details);
    }

    private static bool Available(NRewardButton button, Player player)
        => button.IsEnabled && button.Reward is { IsPopulated: true, SuccessfullySelected: false } reward &&
           reward.Player == player;

    private static GoldOption? Option(RelicModel relic, Player player, GoldForecastRules rules)
        => CurrentRoomGold.InstantDelta(relic, player.Gold, rules) is { } delta
            ? new GoldOption(relic.IconPath, delta) : null;
}
