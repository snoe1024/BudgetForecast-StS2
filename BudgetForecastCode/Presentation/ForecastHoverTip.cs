using System.Globalization;
using System.Runtime.CompilerServices;
using BudgetForecast.BudgetForecastCode.Forecast;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class ForecastHoverTip
{
    private sealed class OwnedTip
    {
        public NHoverTipSet? Set;
        public Control? Card;
    }
    private static readonly ConditionalWeakTable<NNormalMapPoint, OwnedTip> OwnedTips = new();

    public static void Show(NNormalMapPoint owner, int currentGold, GoldEstimate estimate, RouteStrategy strategy,
        bool showAverage)
    {
        if (NHoverTipSet.shouldBlockHoverTips) return;
        var description = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.description");
        description.Add("minimum", (currentGold + estimate.Minimum).ToString("0.#", CultureInfo.InvariantCulture));
        var averageLine = "";
        if (showAverage)
        {
            var average = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.average");
            average.Add("average", (currentGold + estimate.Average).ToString("0.#", CultureInfo.InvariantCulture));
            averageLine = average.GetFormattedText();
        }
        description.Add("averageLine", averageLine);
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
                owned.Card!.GetNode<RichTextLabel>("%Description").Text = hoverTip.Description;
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
}
