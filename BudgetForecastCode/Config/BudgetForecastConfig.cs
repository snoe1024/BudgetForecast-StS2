using BaseLib.Config;
using BudgetForecast.BudgetForecastCode.Forecast;

namespace BudgetForecast.BudgetForecastCode.Config;

[ConfigHoverTipsByDefault]
public sealed class BudgetForecastConfig : SimpleModConfig
{
    [ConfigSlider(0, 30, 0.1, Format = "{0:0.0}")]
    public static double NormalCombatTurns
    {
        get => (double)PlayerGoldForecaster.NormalCombatTurns;
        set => PlayerGoldForecaster.NormalCombatTurns = Sanitize(value, 4);
    }

    [ConfigSlider(0, 30, 0.1, Format = "{0:0.0}")]
    public static double EliteCombatTurns
    {
        get => (double)PlayerGoldForecaster.EliteCombatTurns;
        set => PlayerGoldForecaster.EliteCombatTurns = Sanitize(value, 6);
    }

    private static decimal Sanitize(double value, decimal fallback) => double.IsFinite(value)
        ? decimal.Round((decimal)Math.Clamp(value, 0, 30), 1) : fallback;
}
