using System.Globalization;

/// <summary>
/// Replaces the old Telegram webhook: since there's no always-on server, a scheduled
/// GitHub Actions run calls this to ask Telegram (via getUpdates long-polling) whether
/// there are any new messages since last time, and processes them the same way the
/// webhook used to.
/// </summary>
public static class TelegramPoller
{
    private const string OffsetFile = "state/telegram-offset.txt";

    public static async Task<int> RunAsync(CalendarService calendar, TelegramService telegram)
    {
        Directory.CreateDirectory("state");

        var firstRun = !File.Exists(OffsetFile);
        long offset = firstRun
            ? 0
            : long.Parse(await File.ReadAllTextAsync(OffsetFile), CultureInfo.InvariantCulture);

        var response = await telegram.GetUpdatesAsync(offset);
        if (response?.Result is null || response.Result.Count == 0)
        {
            Console.WriteLine("No new Telegram messages.");
            return 0;
        }

        var maxUpdateId = offset - 1;
        foreach (var update in response.Result)
        {
            maxUpdateId = Math.Max(maxUpdateId, update.UpdateId);

            if (firstRun)
            {
                // Bootstrap only: don't replay whatever backlog exists the very first time
                // this runs — just note where we are so only genuinely new messages are acted on.
                continue;
            }

            var text = update.Message?.Text;
            var msgChatId = update.Message?.Chat.Id.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(text) || msgChatId != telegram.ChatId)
                continue;

            var parsed = EventParser.Parse(text);
            if (parsed is null)
            {
                await telegram.SendMessageAsync("Couldn't find a date/time in that — try something like 'Math test on Thursday at 10 AM'.");
                continue;
            }

            await calendar.CreateEventAsync(parsed.Title, parsed.Start, parsed.End);
            var formattedDate = parsed.Start.ToString("dddd, MMM d 'at' HH:mm", CultureInfo.InvariantCulture);
            // parsed.Title comes from the user's own free-text message — HTML-encode before
            // it goes into a parse_mode=HTML reply, or a stray '<' breaks the send.
            var safeTitle = System.Net.WebUtility.HtmlEncode(parsed.Title);
            await telegram.SendMessageAsync($"✅ Created: <b>{safeTitle}</b>\n{formattedDate}");
        }

        await File.WriteAllTextAsync(OffsetFile, (maxUpdateId + 1).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine(firstRun
            ? $"First run: bootstrapped offset to {maxUpdateId + 1} without processing backlog."
            : $"Processed updates up to {maxUpdateId}.");
        return 0;
    }
}
