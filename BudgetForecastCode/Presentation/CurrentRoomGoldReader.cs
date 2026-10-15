using BudgetForecast.BudgetForecastCode.Forecast;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class CurrentRoomGoldReader
{
    public static CurrentRoomGold Read(Player player)
    {
        var run = player.RunState;
        var rules = PlayerGoldForecaster.RulesFor(player);
        // The map hides overlays. IsVisibleInTree would incorrectly discard the rewards beneath it.
        if (NOverlayStack.Instance?.GetChildren().OfType<NRewardsScreen>()
            .LastOrDefault(candidate => !candidate.IsQueuedForDeletion() && candidate.IsNodeReady()) is { } screen)
        {
            if (!LocalContext.IsMe(player) || screen.IsComplete) return CurrentRoomGold.Empty;
            var set = Traverse.Create(screen).Field("_rewardsSet").GetValue<RewardsSet>();
            if (set?.Player != player) return CurrentRoomGold.Empty;
            return ReadRewards(screen, player, rules);
        }

        if (run.CurrentRoom is CombatRoom combat && CombatManager.Instance.IsInProgress &&
            combat.CombatState.Players.Contains(player) && !player.Creature.IsDead && combat.Encounter.ShouldGiveRewards)
        {
            var finalBoss = combat.RoomType == RoomType.Boss && run.CurrentActIndex >= run.Acts.Count - 1;
            if (finalBoss) return CurrentRoomGold.Empty; // RewardsSet.WithRewardsFromRoom returns an empty set.
            // Only this already entered encounter's public range; never create/populate rewards.
            var estimate = PlayerGoldForecaster.CombatReward(combat.Encounter.MinGoldReward,
                combat.Encounter.MaxGoldReward, rules);
            return new CurrentRoomGold(estimate, Array.Empty<GoldChoice>());
        }

        if (run.CurrentRoom is not EventRoom room) return CurrentRoomGold.Empty;
        // Events is already populated/synchronized by the game, including remote players' ancient choices.
        var ancient = RunManager.Instance.EventSynchronizer.Events.OfType<AncientEventModel>()
            .FirstOrDefault(model => model.Owner == player && model.Id == room.ModelId);
        if (ancient == null || ancient.IsFinished || ancient.Description == null) return CurrentRoomGold.Empty;
        // WasChosen is set before the async obtain callback, so hide ALL alternatives immediately.
        if (ancient.CurrentOptions.Any(option => option.WasChosen)) return CurrentRoomGold.Empty;
        var options = ancient.CurrentOptions.Where(option => !option.IsLocked && !option.IsProceed)
            .Select(option => option.Relic).OfType<RelicModel>()
            .Select(relic => Option(relic, player, rules)).OfType<GoldOption>().ToArray();
        return new CurrentRoomGold(new GoldEstimate(0, 0), options.Length == 0
            ? Array.Empty<GoldChoice>() : new[] { new GoldChoice(options, ChooseOne: true) });
    }

    private static CurrentRoomGold ReadRewards(NRewardsScreen screen, Player player, GoldForecastRules rules)
    {
        var buttons = Traverse.Create(screen).Field("_rewardButtons").GetValue<List<Control>>();
        var gold = new List<int>();
        var choices = new List<GoldChoice>();
        foreach (var control in buttons ?? Enumerable.Empty<Control>())
        {
            if (!GodotObject.IsInstanceValid(control) || control.IsQueuedForDeletion()) continue;
            if (control is NRewardButton button && Available(button, player))
            {
                if (button.Reward is GoldReward reward) gold.Add(reward.Amount);
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
        return new CurrentRoomGold(CurrentRoomGold.DisplayedGold(gold, rules), choices);
    }

    private static bool Available(NRewardButton button, Player player)
        => button.IsEnabled && button.Reward is { IsPopulated: true, SuccessfullySelected: false } reward &&
           reward.Player == player;

    private static GoldOption? Option(RelicModel relic, Player player, GoldForecastRules rules)
        => CurrentRoomGold.InstantDelta(relic, player.Gold, rules) is { } delta
            ? new GoldOption(relic.IconPath, delta) : null;
}
