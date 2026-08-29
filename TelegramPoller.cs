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
            // Advance past this update's id up front, before any processing. If something
            // below throws, the offset still moves past it on save — one bad message must
            // never get the whole pipeline stuck replaying (and re-crashing on) it forever.
            maxUpdateId = Math.Max(maxUpdateId, update.UpdateId);

            if (firstRun)
            {
                // Bootstrap only: don't replay whatever backlog exists the very first time
                // this runs — just note where we are so only genuinely new messages are acted on.
                continue;
            }

            try
            {
                await ProcessUpdateAsync(update, calendar, telegram);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to process update {update.UpdateId}: {ex.GetType().Name}: {ex.Message}");
                await TryNotifyFailureAsync(telegram);
            }
        }

        await File.WriteAllTextAsync(OffsetFile, (maxUpdateId + 1).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine(firstRun
            ? $"First run: bootstrapped offset to {maxUpdateId + 1} without processing backlog."
            : $"Processed updates up to {maxUpdateId}.");
        return 0;
    }

    private static async Task ProcessUpdateAsync(TelegramUpdate update, CalendarService calendar, TelegramService telegram)
    {
        var text = update.Message?.Text;
        var msgChatId = update.Message?.Chat.Id.ToString(CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text) || msgChatId != telegram.ChatId)
            return;

        var parsed = EventParser.Parse(text);
        if (parsed is null)
        {
            await telegram.SendMessageAsync("Couldn't find a date/time in that — try something like 'Math test on Thursday at 10 AM'.");
            return;
        }

        var colorId = EventCategorizer.DetectColorId(text);
        await calendar.CreateEventAsync(parsed.Title, parsed.Start, parsed.End, colorId, parsed.IsAllDay);

        var formattedRange = parsed.IsAllDay
            ? $"{parsed.Start.ToString("dddd, MMM d", CultureInfo.InvariantCulture)} (all day)"
            : parsed.End.Date == parsed.Start.Date
                ? $"{parsed.Start.ToString("dddd, MMM d", CultureInfo.InvariantCulture)} at {parsed.Start.ToString("HH:mm", CultureInfo.InvariantCulture)}–{parsed.End.ToString("HH:mm", CultureInfo.InvariantCulture)}"
                : $"{parsed.Start.ToString("dddd, MMM d 'at' HH:mm", CultureInfo.InvariantCulture)} – {parsed.End.ToString("dddd, MMM d 'at' HH:mm", CultureInfo.InvariantCulture)}";
        // parsed.Title comes from the user's own free-text message — HTML-encode before
        // it goes into a parse_mode=HTML reply, or a stray '<' breaks the send.
        var safeTitle = System.Net.WebUtility.HtmlEncode(parsed.Title);
        await telegram.SendMessageAsync($"✅ Created: <b>{safeTitle}</b>\n{formattedRange}");
    }

    private static async Task TryNotifyFailureAsync(TelegramService telegram)
    {
        try
        {
            await telegram.SendMessageAsync("⚠️ Something went wrong processing that message — try rephrasing it.");
        }
        catch
        {
            // Best-effort only; if even this fails, the console log above is the record of it.
        }
    }
}
