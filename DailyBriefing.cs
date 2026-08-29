using System.Globalization;

/// <summary>
/// Replaces the old Cloud Scheduler-triggered endpoint. The GitHub Actions workflow runs
/// this every hour (UTC cron can't express "8 AM Israel time" directly, since Israel's
/// UTC offset shifts with daylight saving) — this checks the real local Asia/Jerusalem
/// clock each time and only actually sends once, on the hour it's configured for, using
/// a state file so an hour that gets checked twice doesn't send twice.
/// </summary>
public static class DailyBriefing
{
    private const string StateFile = "state/last-briefing-date.txt";
    private static readonly TimeZoneInfo IsraelTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    public static async Task<int> RunAsync(CalendarService calendar, TelegramService telegram, int targetHour, bool force = false)
    {
        var nowIsrael = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelTz);
        if (!force && nowIsrael.Hour != targetHour)
        {
            Console.WriteLine($"Not the target hour yet (now {nowIsrael.Hour:00}:00 Israel time, target {targetHour:00}:00) — skipping.");
            return 0;
        }

        Directory.CreateDirectory("state");
        var today = nowIsrael.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (!force && File.Exists(StateFile) && await File.ReadAllTextAsync(StateFile) == today)
        {
            Console.WriteLine("Already sent today's briefing — skipping.");
            return 0;
        }

        var events = await calendar.GetTodayEventsAsync();

        string message;
        if (events.Count == 0)
        {
            message = "<b>Today's schedule</b>\n\nNothing on the calendar today.";
        }
        else
        {
            var lines = events.Select(e =>
            {
                var time = e.Start.DateTimeDateTimeOffset?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "All day";
                // Event summaries are arbitrary user-entered text — HTML-encode before it goes
                // into a parse_mode=HTML Telegram message, or a stray '<' breaks the send.
                return $"{time} — {System.Net.WebUtility.HtmlEncode(e.Summary)}";
            });
            message = "<b>Today's schedule</b>\n\n" + string.Join("\n", lines);
        }

        await telegram.SendMessageAsync(message);
        await File.WriteAllTextAsync(StateFile, today);
        Console.WriteLine("Daily briefing sent.");
        return 0;
    }
}
