using BudgetForecast.BudgetForecastCode.Forecast;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereItems;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class EventOverlayGoldReader
{
    public static IReadOnlyList<ForecastDetail>? Read(Player player)
    {
        if (!LocalContext.IsMe(player) || player.RunState.CurrentRoom is not EventRoom || NOverlayStack.Instance == null)
            return null; // Combat discoveries can use similar grids but add to hand, not to deck.
        var overlays = NOverlayStack.Instance.GetChildren().OfType<Control>()
            .Where(control => !control.IsQueuedForDeletion() && control.IsNodeReady()).ToArray();
        if (overlays.OfType<NSimpleCardSelectScreen>().LastOrDefault() is { } grid)
        {
            // Only the reward overload sets _cardResults. Deck removal/upgrade grids never qualify.
            var results = Traverse.Create(grid).Field("_cardResults").GetValue<List<CardCreationResult>>();
            var prefs = Traverse.Create(grid).Field("_prefs").GetValue<CardSelectorPrefs>();
            if (results == null) return null;
            var hint = EventGoldReader.CardHint(player, Math.Min(results.Count, prefs.MaxSelect));
            return hint == null ? Array.Empty<ForecastDetail>() :
                new[] { new ForecastDetail("", Cards: hint, Note: "cardsPending") };
        }
        if (overlays.OfType<NDeckCardSelectScreen>().LastOrDefault() is { } deckGrid)
        {
            var prefs = Traverse.Create(deckGrid).Field("_prefs").GetValue<CardSelectorPrefs>();
            if (prefs.Prompt.LocEntryKey == CardSelectorPrefs.TransformSelectionPrompt.LocEntryKey)
            {
                var deckCards = Traverse.Create(deckGrid).Field("_cards").GetValue<IReadOnlyList<MegaCrit.Sts2.Core.Models.CardModel>>();
                var hint = EventGoldReader.CardHint(player, Math.Min(deckCards?.Count ?? 0, prefs.MaxSelect));
                return hint == null ? Array.Empty<ForecastDetail>() :
                    new[] { new ForecastDetail("", Cards: hint, Note: "cardsPending") };
            }
        }
        if (overlays.OfType<NCrystalSphereScreen>().LastOrDefault() is not { } sphere) return null;
        var entity = Traverse.Create(sphere).Field("_entity").GetValue<CrystalSphereMinigame>();
        if (entity == null || entity.IsFinished) return Array.Empty<ForecastDetail>();
        // Deliberately never read Items, cells, positions, or generated but unrevealed content.
        var revealed = Traverse.Create(entity).Field("_revealed").GetValue<List<CrystalSphereItem>>();
        var goldItems = revealed?.OfType<CrystalSphereGold>().ToArray() ?? Array.Empty<CrystalSphereGold>();
        decimal gold = 0;
        foreach (var item in goldItems)
            gold += PlayerGoldForecaster.ApplyGain(Traverse.Create(item).Property("Amount").GetValue<int>(),
                PlayerGoldForecaster.RulesFor(player));
        var cards = EventGoldReader.CardHint(player, revealed?.OfType<CrystalSphereCardReward>().Count() ?? 0);
        if (goldItems.Length == 0 && cards == null) return Array.Empty<ForecastDetail>();
        return new[] { new ForecastDetail("", goldItems.Length == 0 ? null : new GoldEstimate(gold, gold), cards, Note: "revealed") };
    }
}
