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
        // literal string "not resolved") — neither is a parseable date. And an explicit range
        // ("3pm to 5pm", "Monday through Wednesday") comes back as a "datetimerange"/"timerange"/
        // "daterange" with separate "start"/"end" fields instead of a single "value" — that real
        // end has to be used rather than defaulting to start+1h/next-day. Skip anything that
        // doesn't resolve to a usable candidate rather than crash or silently drop the range.
        var candidates = values
            .Select(ResolveCandidate)
            .Where(c => c is not null)
            .Select(c => c!.Value)
            .ToList();

        if (candidates.Count == 0) return null;

        var candidate = candidates
            .Where(c => c.Start >= nowIsrael)
            .DefaultIfEmpty(candidates[0])
            .First();

        var title = ExtractTitle(message, match);

        return new ParsedEvent(title, candidate.Start, candidate.End, candidate.IsAllDay);
    }

    private static (DateTime Start, DateTime End, bool IsAllDay)? ResolveCandidate(Dictionary<string, string> v)
    {
        switch (v.GetValueOrDefault("type"))
        {
            case "date":
                if (!TryParse(v.GetValueOrDefault("value"), out var d)) return null;
                return (d.Date, d.Date.AddDays(1), IsAllDay: true);

            case "datetime":
            case "time":
                if (!TryParse(v.GetValueOrDefault("value"), out var dt)) return null;
                return (dt, dt.AddHours(1), IsAllDay: false);

            case "daterange":
                if (!TryParse(v.GetValueOrDefault("start"), out var ds) || !TryParse(v.GetValueOrDefault("end"), out var de))
                    return null;
                // The recognizer's range end is inclusive of the last day; Google Calendar's
                // all-day end is exclusive, so push it one day past the last included day.
                return (ds.Date, de.Date.AddDays(1), IsAllDay: true);

            case "datetimerange":
            case "timerange":
                if (!TryParse(v.GetValueOrDefault("start"), out var ts) || !TryParse(v.GetValueOrDefault("end"), out var te))
                    return null;
                return (ts, te, IsAllDay: false);

            default:
                return null;
        }
    }

    private static bool TryParse(string? text, out DateTime value) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);

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
