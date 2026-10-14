using System.Globalization;
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
    public static void Show(NNormalMapPoint owner, int currentGold, GoldEstimate estimate)
    {
        if (NHoverTipSet.shouldBlockHoverTips) return;
        var description = new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.description");
        description.Add("minimum", (currentGold + estimate.Minimum).ToString("0.#", CultureInfo.InvariantCulture));
        description.Add("average", (currentGold + estimate.Average).ToString("0.#", CultureInfo.InvariantCulture));
        var hoverTip = new HoverTip(new LocString("static_hover_tips", "BUDGETFORECAST_SHOP.title"), description);

        // The native registry accepts only one set per owner. Preserve tips from other mods.
        var active = Traverse.Create(typeof(NHoverTipSet)).Field("_activeHoverTips")
            .GetValue<Dictionary<Control, NHoverTipSet>>();
        NHoverTipSet? tip;
        if (active != null && active.TryGetValue(owner, out var existing))
        {
            // Init appends native text cards; it does not clear the existing set.
            Traverse.Create(existing).Method("Init", new[] { typeof(Control), typeof(IEnumerable<IHoverTip>) })
                .GetValue(owner, new IHoverTip[] { hoverTip });
            tip = existing;
        }
        else
        {
            tip = NHoverTipSet.CreateAndShow(owner, hoverTip);
        }
        if (tip == null) return; // Respect the game's targeting/debug tooltip blocking.

        // As in NMapPoint's history tips, position after the native container has laid out.
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(owner) || !GodotObject.IsInstanceValid(tip) ||
                owner.IsQueuedForDeletion() || tip.IsQueuedForDeletion()) return;
            tip.SetAlignment(owner, HoverTip.GetHoverTipAlignment(owner));
            tip.SetFollowOwner();
        }).CallDeferred();
        // Native OnUnfocus / OnPress / TreeExiting remove the owner-associated tip.
    }
}
