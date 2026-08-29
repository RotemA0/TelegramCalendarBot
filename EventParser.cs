using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Recognizers.Text;
using Microsoft.Recognizers.Text.DateTime;

public static class EventParser
{
    public record ParsedEvent(string Title, DateTime Start, DateTime End);

    // Connector words/punctuation that dangle at either end of the message once the
    // matched date/time phrase is removed (e.g. "Math test [on Thursday at 10 AM]" -> "Math test on").
    private static readonly Regex DanglingFillerRegex = new(
        @"^(?:\s*[,\-:]\s*|\s+(?:on|at|in|for|by|this|that|next|coming)\b)+|(?:\s*[,\-:]\s*|\s+(?:on|at|in|for|by|this|that|next|coming)\b)+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedEvent? Parse(string message)
    {
        var results = DateTimeRecognizer.RecognizeDateTime(message, Culture.English);
        if (results.Count == 0) return null;

        var match = results[0];
        var values = (List<Dictionary<string, string>>)match.Resolution["values"];

        var candidate = values
            .Select(v => DateTime.Parse(v.GetValueOrDefault("value") ?? v["start"], CultureInfo.InvariantCulture))
            .Where(dt => dt >= DateTime.Now)
            .DefaultIfEmpty(DateTime.Parse(values[0].GetValueOrDefault("value") ?? values[0]["start"], CultureInfo.InvariantCulture))
            .First();

        var start = candidate;
        var end = start.AddHours(1);

        var title = ExtractTitle(message, match);

        return new ParsedEvent(title, start, end);
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