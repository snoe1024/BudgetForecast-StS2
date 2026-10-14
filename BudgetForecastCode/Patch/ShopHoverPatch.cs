using BudgetForecast.BudgetForecastCode.Forecast;
using BudgetForecast.BudgetForecastCode.Presentation;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace BudgetForecast.BudgetForecastCode.Patch;

[HarmonyPatch(typeof(NNormalMapPoint), "OnFocus")]
internal static class ShopHoverPatch
{
    [HarmonyPostfix]
    private static void Postfix(NNormalMapPoint __instance)
    {
        if (__instance.Point.PointType != MapPointType.Shop) return;
        try
        {
            var run = Traverse.Create(__instance).Field("_runState").GetValue<IRunState>();
            var screen = Traverse.Create(__instance).Field("_screen").GetValue<NMapScreen>();
            if (run == null || screen == null || !screen.IsOpen || screen.IsTraveling ||
                !Traverse.Create(__instance).Method("IsInputAllowed").GetValue<bool>()) return;
            var current = run.CurrentMapPoint;
            var player = LocalContext.GetMe(run);
            if (current == null || player == null) return;
            var path = CombatMaximizingRoute.Find(current, __instance.Point);
            if (path == null) return;
            var estimate = CombatGoldCalculator.Sum(path, run.AscensionLevel);
            ForecastLog.Write(current, path, player.Gold, run.AscensionLevel, estimate);
        }
        catch (Exception exception)
        {
            // Display-only failures must not interrupt the game's hover handling.
            MainFile.Logger.Warn($"Shop forecast failed: {exception}");
        }
    }
}
