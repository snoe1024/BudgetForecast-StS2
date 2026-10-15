namespace BudgetForecast.BudgetForecastCode.Presentation;

internal readonly record struct PlayerForecastDisplay(string Name, string CharacterIcon,
    decimal Minimum, decimal Average, int DeletionGold, string? CurrentRoomGoldLine = null,
    string DiscountRelicIcons = "", string GoldRelicIcons = "");

internal static class PlayerForecastParagraphs
{
    public static string Build(IEnumerable<PlayerForecastDisplay> players, bool multiplayer, bool showAverage,
        Func<decimal, decimal?, string> goldLine, Func<int, string> deletionLine)
    {
        return string.Concat(players.Select(player =>
        {
            var lines = new List<string>();
            if (multiplayer) lines.Add($"{player.CharacterIcon} {player.Name}");
            lines.Add(goldLine(player.Minimum, showAverage ? player.Average : null) +
                (string.IsNullOrEmpty(player.GoldRelicIcons) ? "" : " " + player.GoldRelicIcons));
            // Optional current-room choices disappear without leaving an empty line.
            if (!string.IsNullOrEmpty(player.CurrentRoomGoldLine)) lines.Add(player.CurrentRoomGoldLine);
            lines.Add(deletionLine(player.DeletionGold) +
                (string.IsNullOrEmpty(player.DiscountRelicIcons) ? "" : " " + player.DiscountRelicIcons));
            return "[p]" + string.Join("\n", lines) + "[/p]";
        }));
    }
}
