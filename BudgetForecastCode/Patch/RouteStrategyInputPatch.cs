using BudgetForecast.BudgetForecastCode.Presentation;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace BudgetForecast.BudgetForecastCode.Patch;

// _Input precedes GUI scroll handling; _GuiInput is a fallback for direct GUI delivery.
[HarmonyPatch(typeof(NMapScreen), "_Input")]
internal static class RouteStrategyInputPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(NMapScreen __instance, InputEvent inputEvent) =>
        !RoutePreview.HandleInput(__instance, inputEvent);
}

[HarmonyPatch(typeof(NMapScreen), "_GuiInput")]
internal static class RouteStrategyGuiInputPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(NMapScreen __instance, InputEvent inputEvent) =>
        !RoutePreview.HandleInput(__instance, inputEvent);
}
