using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.DateTime;

public static class EventParser
{
    public record ParsedEvent(string Title, DateTime Start, DateTime End, bool IsAllDay);

    // Connector words/punctuation that dangle at either end of the message once the
    // matched date/time phrase is removed (e.g. "Math test [on Thursday at 10 AM]" -> "Math test on").
    private static readonly Regex DanglingFillerRegex = new(
        @"^(?:\s*[,\-:]\s*|\s+(?:on|at|in|for|by|this|that|next|coming)\b)+|(?:\s*[,\-:]\s*|\s+(?:on|at|in|for|by|this|that|next|coming)\b)+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly TimeZoneInfo IsraelTz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    public static ParsedEvent? Parse(string message)
    {
        // The recognizer resolves relative phrases ("today", "tomorrow", "Thursday") against
        // a reference "now" — if left to its default, that's the machine's local clock, which
        // on GitHub Actions runners is UTC, not Israel time. Between Israel midnight and UTC
        // midnight every night, that silently resolved "today" to the wrong calendar day.
        // Passing an explicit Israel-local reference time fixes it regardless of what timezone
        // the process actually runs in.
        var nowIsrael = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IsraelTz);

        var results = DateTimeRecognizer.RecognizeDateTime(message, Culture.English, DateTimeOptions.None, nowIsrael);
        if (results.Count == 0) return null;

        var match = results[0];
        var values = (List<Dictionary<string, string>>)match.Resolution["values"];

        // Not every match resolves to an actual point in time: "for 2 hours" comes back as a
        // "duration" (value is a number of seconds), "every Monday" as a "set" (value is the
        // literal string "not resolved") — neither is a parseable date, and DateTime.Parse
        // would throw. Skip anything that doesn't actually parse rather than crash on it.
        var candidates = values
            .Select(v => (
                Text: v.GetValueOrDefault("value") is { Length: > 0 } val ? val : v.GetValueOrDefault("start"),
                IsAllDay: v.GetValueOrDefault("type") == "date"))
            .Where(v => v.Text is not null
                        && DateTime.TryParse(v.Text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .Select(v => (DateTime: DateTime.Parse(v.Text!, CultureInfo.InvariantCulture), v.IsAllDay))
            .ToList();

        if (candidates.Count == 0) return null;

        var candidate = candidates
            .Where(c => c.DateTime >= nowIsrael)
            .DefaultIfEmpty(candidates[0])
            .First();

        var start = candidate.DateTime;
        var end = candidate.IsAllDay ? start.Date.AddDays(1) : start.AddHours(1);

        var title = ExtractTitle(message, match);

        return new ParsedEvent(title, start, end, candidate.IsAllDay);
    }

    private static string ExtractTitle(string message, ModelResult match)
    {
        // NOTE: match.Text is lowercased by the recognizer and will not reliably
        // reappear as a substring of the original (differently-cased) message, so
        // string-based Replace(match.Text, "") silently does nothing. Remove by
        // the match's character indices instead — that's case-independent.
        var matchLength = match.End - match.Start + 1;
        var title = message.Remove(match.Start, matchLength);

        // Repeatedly strip connector words/punctuation left dangling at either end.
        string previous;
        do
        {
            previous = title;
            title = DanglingFillerRegex.Replace(title, "").Trim();
        } while (title != previous);

        return string.IsNullOrWhiteSpace(title) ? "New event" : title;
    }
}
