using System.Globalization;
using System.Runtime.CompilerServices;
using BudgetForecast.BudgetForecastCode.Forecast;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Map;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class ForecastHoverTip
{
    private sealed class OwnedTip
    {
        public NHoverTipSet? Set;
        public Control? Card;
    }
    private static readonly ConditionalWeakTable<NNormalMapPoint, OwnedTip> OwnedTips = new();

    public static void Show(NNormalMapPoint owner, IRunState run, IReadOnlyList<MapPoint> path, RouteStrategy strategy)
    {
        if (NHoverTipSet.shouldBlockHoverTips) return;
        var width = Math.Min(620f, Math.Max(300f, owner.GetViewportRect().Size.X - 60f));
        var description = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.description");
        var multiplayer = RunManager.Instance.NetService.Type.IsMultiplayer() || run.Players.Count > 1;
        var rows = run.Players.Select(player =>
        {
            var currentRoom = CurrentRoomGoldReader.Read(player, path);
            var prediction = PlayerGoldForecaster.Predict(player, path, currentRoom.Combat);
            var carried = EventGoldReader.CarriedPotions(player);
            var details = (currentRoom.Details ?? Array.Empty<ForecastDetail>())
                .Concat(carried == null ? Array.Empty<ForecastDetail>() : new[] { carried });
            return new PlayerForecastDisplay(
                multiplayer ? PlatformUtil.GetPlayerName(RunManager.Instance.NetService.Platform, player.NetId)
                    .Replace("\r", " ").Replace("\n", " ") : "",
                multiplayer ? InlineIcon(player.Character.IconTexture.ResourcePath) : "",
                prediction.Minimum, prediction.Average,
                CardRemovalForecast.ForPlayer(player), CurrentRoomGoldLine: string.Join("\n",
                    new[] { ChoiceLines(currentRoom.Choices), ForecastDetailText.Format(details, true), SealAssumption(player) }
                    .Where(line => !string.IsNullOrEmpty(line))),
                DiscountRelicIcons: string.Join(" ", player.Relics
                    .Where(relic => !relic.IsMelted && relic is MembershipCard or TheCourier)
                    .OrderBy(relic => relic is MembershipCard ? 0 : 1)
                    .Select(relic => InlineIcon(relic.IconPath))),
                GoldRelicIcons: string.Join(" ", GoldRelics.AcquisitionModifiers(player).Select(relic => InlineIcon(relic.IconPath))));
        });
        string GoldLine(decimal minimum, decimal? averageGold)
        {
            var line = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.gold");
            line.Add("minimum", minimum.ToString("0.#", CultureInfo.InvariantCulture));
            var averageText = "";
            if (averageGold.HasValue)
            {
                var average = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.average");
                average.Add("average", averageGold.Value.ToString("0.#", CultureInfo.InvariantCulture));
                averageText = average.GetFormattedText();
            }
            line.Add("averageLine", averageText);
            return line.GetFormattedText();
        }
        string DeletionLine(int gold)
        {
            var line = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.deletion");
            line.Add("icon", InlineIcon(ImageHelper.GetImagePath("ui/reward_screen/reward_icon_card_removal.png")));
            line.Add("gold", gold.ToString(CultureInfo.InvariantCulture));
            return line.GetFormattedText();
        }
        string ChoiceLines(IReadOnlyList<GoldChoice> choices) => string.Join("\n", choices.Select(choice =>
        {
            var line = new LocString("static_hover_tips", choice.ChooseOne
                ? "BUDGETFORECAST_SHOP.choice" : "BUDGETFORECAST_SHOP.obtain");
            line.Add("options", string.Join(" / ", choice.Options.Select(option =>
                $"{InlineIcon(option.IconPath)}: {option.Delta.ToString("+0;-0;0", CultureInfo.InvariantCulture)}G")));
            return line.GetFormattedText();
        }));
        description.Add("players", PlayerForecastParagraphs.Build(rows, multiplayer, GoldLine, DeletionLine));
        description.Add("strategy", new LocString("static_hover_tips", "BUDGETFORECAST_STRATEGY." + strategy));
        var hoverTip = new HoverTip(new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.title"), description);

        // The native registry accepts only one set per owner. Preserve tips from other mods.
        var active = Traverse.Create(typeof(NHoverTipSet)).Field("_activeHoverTips")
            .GetValue<Dictionary<Control, NHoverTipSet>>();
        NHoverTipSet? tip;
        var owned = OwnedTips.GetValue(owner, _ => new OwnedTip());
        if (active != null && active.TryGetValue(owner, out var existing))
        {
            if (ReferenceEquals(owned.Set, existing) && GodotObject.IsInstanceValid(owned.Card))
            {
                // Change only our card; do not duplicate or remove other mods' tips on each wheel tick.
                var label = owned.Card!.GetNode<RichTextLabel>("%Description");
                if (label.Text == hoverTip.Description && owned.Card.CustomMinimumSize.X == width) return;
                label.Text = hoverTip.Description;
                owned.Card.ResetSize();
            }
            else
            {
                Traverse.Create(existing).Method("Init", new[] { typeof(Control), typeof(IEnumerable<IHoverTip>) })
                    .GetValue(owner, new IHoverTip[] { hoverTip });
                owned.Set = existing;
                var container = existing.GetNode<Control>("textHoverTipContainer");
                owned.Card = container.GetChild<Control>(container.GetChildCount() - 1);
            }
            tip = existing;
        }
        else
        {
            tip = NHoverTipSet.CreateAndShow(owner, hoverTip);
            if (tip != null)
            {
                owned.Set = tip;
                owned.Card = tip.GetNode<Control>("textHoverTipContainer").GetChild<Control>(0);
            }
        }
        if (tip == null) return; // Respect the game's targeting/debug tooltip blocking.
        if (GodotObject.IsInstanceValid(owned.Card))
        {
            // Only our card is widened. Native wrapping/positioning still handles language and viewport bounds.
            owned.Card!.CustomMinimumSize = new Vector2(width, 0);
            owned.Card.GetNode<RichTextLabel>("%Description").CustomMinimumSize = new Vector2(width - 60f, 0);
            owned.Card.ResetSize();
        }

        // As in NMapPoint's history tips, position after the native container has laid out.
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(owner) || !GodotObject.IsInstanceValid(tip) ||
                owner.IsQueuedForDeletion() || tip.IsQueuedForDeletion()) return;
            // Shift can change the card height. Refresh the native flow area before aligning it.
            if (GodotObject.IsInstanceValid(owned.Card)) owned.Card!.ResetSize();
            var container = tip.GetNode<Control>("textHoverTipContainer");
            var height = container.GetChildren().OfType<Control>().Sum(card => card.Size.Y + 5f);
            container.Size = new Vector2(container.Size.X, Math.Min(height, owner.GetViewportRect().Size.Y - 50f));
            tip.SetAlignment(owner, HoverTip.GetHoverTipAlignment(owner));
            tip.SetFollowOwner();
        }).CallDeferred();
        // Native OnUnfocus / OnPress / TreeExiting remove the owner-associated tip.
    }

    private static string InlineIcon(string path) => $"[img width=24 height=24]{path}[/img]";

    private static string SealAssumption(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        var seal = player.Relics.OfType<SealOfGold>().FirstOrDefault(relic => !relic.IsMelted);
        if (seal == null) return "";
        var line = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.sealTurns");
        line.Add("icon", InlineIcon(seal.IconPath));
        line.Add("normal", PlayerGoldForecaster.NormalCombatTurns.ToString("0.#", CultureInfo.InvariantCulture));
        line.Add("elite", PlayerGoldForecaster.EliteCombatTurns.ToString("0.#", CultureInfo.InvariantCulture));
        return line.GetFormattedText();
    }
}
