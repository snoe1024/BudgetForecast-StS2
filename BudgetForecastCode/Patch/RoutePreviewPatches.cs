using BudgetForecast.BudgetForecastCode.Presentation;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace BudgetForecast.BudgetForecastCode.Patch;

[HarmonyPatch(typeof(NNormalMapPoint), "OnUnfocus")]
internal static class RoutePreviewUnfocusPatch
{
    [HarmonyPostfix]
    private static void Postfix(NNormalMapPoint __instance) => RoutePreview.HideFor(__instance);
}

[HarmonyPatch(typeof(NNormalMapPoint), "OnPress")]
internal static class RoutePreviewPressPatch
{
    [HarmonyPostfix]
    private static void Postfix(NNormalMapPoint __instance) => RoutePreview.HideFor(__instance);
}

[HarmonyPatch(typeof(NMapScreen), "Close")]
internal static class RoutePreviewClosePatch
{
    [HarmonyPrefix]
    private static void Prefix(NMapScreen __instance) => RoutePreview.Hide(__instance);
}

[HarmonyPatch(typeof(NMapScreen), "SetMap")]
internal static class RoutePreviewSetMapPatch
{
    [HarmonyPrefix]
    private static void Prefix(NMapScreen __instance) => RoutePreview.Invalidate(__instance);
}

[HarmonyPatch(typeof(NMapScreen), "TravelToMapCoord")]
internal static class RoutePreviewTravelPatch
{
    [HarmonyPrefix]
    private static void Prefix(NMapScreen __instance) => RoutePreview.Hide(__instance);
}

[HarmonyPatch(typeof(NMapScreen), "_Process")]
internal static class RoutePreviewProcessPatch
{
    [HarmonyPostfix]
    private static void Postfix(NMapScreen __instance) => RoutePreview.Refresh(__instance);
}

[HarmonyPatch(typeof(RunState), "AddVisitedMapCoord")]
internal static class RouteCacheLocationPatch
{
    [HarmonyPostfix]
    private static void Postfix(bool __result)
    {
        if (__result) RoutePreview.InvalidateAll();
    }
}
