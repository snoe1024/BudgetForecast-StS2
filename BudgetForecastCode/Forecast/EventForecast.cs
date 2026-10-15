using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetForecast.BudgetForecastCode.Forecast;

// Semantic output, independent of tooltip layout and game UI objects.
internal sealed record CardGoldHint(decimal PerCard, int Count, bool Optional, bool Doubled);
internal sealed record ForecastDetail(string Title, GoldEstimate? Delta = null,
    CardGoldHint? Cards = null, string Icons = "", string? Note = null, string? ChoiceKey = null);
internal sealed record ForecastDetailGroup(string Title, IReadOnlyList<ForecastDetail> Parts);

internal static class ForecastDetailGroups
{
    public static IReadOnlyList<ForecastDetailGroup> Build(IEnumerable<ForecastDetail> details)
        => details.Select((detail, index) => (Detail: detail, Key: detail.ChoiceKey == null
                ? $"row:{index}" : "choice:" + detail.ChoiceKey))
            .GroupBy(item => item.Key).Select(group => new ForecastDetailGroup(group.First().Detail.Title,
                group.Select(item => item.Detail).ToArray())).ToArray();
}
internal sealed record EventChoiceSnapshot(string Key, string Title, bool Locked = false, bool Chosen = false);
internal sealed record EventSnapshot(IReadOnlyList<EventChoiceSnapshot> Choices,
    IReadOnlyDictionary<string, decimal> Vars, int Gold, int DeckCount, int Transformable, int BasicTransformable,
    int FreePotionSlots, GoldForecastRules Rules, decimal? LuckyGold = null, bool BingBong = false, bool Finished = false);

internal sealed record EventRule(string Key, string? GainVar = null, decimal Gain = 0,
    string? CostVar = null, bool LoseAll = false, int Cards = 0, string? CardsFrom = null,
    bool OptionalCards = false, int ExtraOptionalCards = 0, bool Combat = false,
    bool Potions = false, string? Note = null);

internal static class EventForecast
{
    public static readonly IReadOnlyList<EventRule> Rules = LoadRules();

    private static IReadOnlyList<EventRule> LoadRules()
    {
        using var stream = typeof(EventForecast).Assembly.GetManifestResourceStream("BudgetForecast.EventRules.json")
            ?? throw new InvalidOperationException("Missing event forecast rules.");
        var rules = JsonSerializer.Deserialize<EventRule[]>(stream, new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidOperationException("Invalid event forecast rules.");
        if (rules.Any(rule => string.IsNullOrEmpty(rule.Key) || rule.Key.Count(c => c == '*') > 1) ||
            rules.Select(rule => rule.Key).Distinct().Count() != rules.Length)
            throw new InvalidOperationException("Ambiguous event forecast rules.");
        return rules;
    }

    public static bool Matches(string pattern, string key)
    {
        var pieces = pattern.Split('*');
        if (pieces.Length == 1) return pattern == key;
        return pieces.Length == 2 && key.StartsWith(pieces[0], StringComparison.Ordinal) &&
            key.EndsWith(pieces[1], StringComparison.Ordinal) && key.Length >= pieces[0].Length + pieces[1].Length;
    }

    public static IReadOnlyList<ForecastDetail> Evaluate(EventSnapshot state)
    {
        if (state.Finished || state.Choices.Any(choice => choice.Chosen)) return Array.Empty<ForecastDetail>();
        var result = new List<ForecastDetail>();
        foreach (var (choice, index) in state.Choices.Select((choice, index) => (choice, index)).Where(item => !item.choice.Locked))
        foreach (var rule in Rules.Where(rule => Matches(rule.Key, choice.Key)))
        {
            // Missing data fails closed, never substitute a canonical/unrolled amount.
            if (rule.GainVar != null && !state.Vars.ContainsKey(rule.GainVar) ||
                rule.CostVar != null && !state.Vars.ContainsKey(rule.CostVar)) continue;
            if (rule.Combat && (!state.Vars.ContainsKey("CombatMin") || !state.Vars.ContainsKey("CombatMax"))) continue;
            var gain = PlayerGoldForecaster.ApplyGain(rule.GainVar == null ? rule.Gain : state.Vars[rule.GainVar], state.Rules);
            var cost = rule.LoseAll ? state.Gold : rule.CostVar == null ? 0 :
                Math.Min(state.Gold, decimal.Truncate(state.Vars[rule.CostVar]));
            decimal min = gain - cost, mean = min;
            if (rule.Combat)
            {
                var low = (int)state.Vars["CombatMin"];
                var high = (int)state.Vars["CombatMax"];
                var combat = PlayerGoldForecaster.CombatReward(low, high, state.Rules);
                var spending = PlayerGoldForecaster.SealCost(state.Gold - cost, state.Rules.NormalTurns, state.Rules);
                min += combat.Minimum - spending;
                mean += combat.Average - spending;
            }
            int count;
            switch (rule.CardsFrom)
            {
                case null: count = rule.Cards; break;
                case "DeckPlusOne": count = state.DeckCount + 1; break;
                case "Transformable": count = Math.Min(rule.Cards, state.Transformable); break;
                case "BasicTransformable": count = Math.Min(rule.Cards, state.BasicTransformable); break;
                default:
                    if (!state.Vars.TryGetValue(rule.CardsFrom, out var countVar)) continue;
                    count = (int)countVar;
                    break;
            }
            CardGoldHint? cards = null;
            if (state.LuckyGold.HasValue && count > 0)
            {
                var perCard = PlayerGoldForecaster.ApplyGain(state.LuckyGold.Value, state.Rules);
                var copies = state.BingBong ? 2 : 1;
                cards = new CardGoldHint(perCard, count, rule.OptionalCards, state.BingBong);
                if (!rule.OptionalCards) { min += perCard * copies * count; mean += perCard * copies * count; }
            }
            if (rule.Potions)
            {
                if (!state.Vars.ContainsKey("FoulPotions") || !state.Vars.ContainsKey("FoulPotionGold")) continue;
                count = Math.Min((int)state.Vars["FoulPotions"], state.FreePotionSlots);
                min = mean = PlayerGoldForecaster.ApplyGain(state.Vars["FoulPotionGold"], state.Rules) * count;
            }
            var hasMoney = rule.GainVar != null || rule.Gain != 0 || rule.CostVar != null || rule.LoseAll ||
                rule.Combat || rule.Potions || cards is { Optional: false };
            if (!hasMoney && cards == null && rule.Note == null) continue;
            result.Add(new ForecastDetail(choice.Title, hasMoney ? new GoldEstimate(min, mean) : null, cards,
                Icons: rule.Potions ? $"×{count}" : "", Note: rule.Potions ? "potions" : rule.Note,
                ChoiceKey: choice.Key + "#" + index));
            if (state.LuckyGold.HasValue && rule.ExtraOptionalCards > 0)
                result.Add(new ForecastDetail(choice.Title, Cards: new CardGoldHint(
                    PlayerGoldForecaster.ApplyGain(state.LuckyGold.Value, state.Rules), rule.ExtraOptionalCards,
                    Optional: true, state.BingBong), ChoiceKey: choice.Key + "#" + index));
        }
        return result;
    }
}
