using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace BudgetForecast.BudgetForecastCode.Forecast;

internal enum GoldRelicKind { Other, AcquisitionModifier, Instant }

internal static class GoldRelics
{
    public static GoldRelicKind Classify(Type type)
    {
        if (type == typeof(AmethystAubergine) || type == typeof(LuckyFysh) || type == typeof(BowlerHat) ||
            type == typeof(SealOfGold) || type == typeof(Ectoplasm) || type == typeof(MawBank))
            return GoldRelicKind.AcquisitionModifier;
        if (type == typeof(OldCoin) || type == typeof(GoldenPearl) || type == typeof(CursedPearl) ||
            type == typeof(SilkenTress) || type == typeof(SignetRing)) return GoldRelicKind.Instant;
        return GoldRelicKind.Other;
    }

    // Used-up Maw Banks remain visible as requested, but do not contribute to the calculation.
    public static IEnumerable<RelicModel> AcquisitionModifiers(Player player) =>
        player.Relics.Where(relic => !relic.IsMelted && Classify(relic.GetType()) == GoldRelicKind.AcquisitionModifier);
}
