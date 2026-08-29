using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

public record EventCategory(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("colorId")] string ColorId,
    [property: JsonPropertyName("keywords")] List<string> Keywords);

/// <summary>
/// Picks a Google Calendar colorId for an event based on keyword matches against
/// categories.json — edit that file to add/change categories, no code changes needed.
/// See https://developers.google.com/calendar/api/v3/reference/colors/get for the
/// colorId -> color name mapping (1 Lavender, 2 Sage, 3 Grape, 4 Flamingo, 5 Banana,
/// 6 Tangerine, 7 Peacock, 8 Graphite, 9 Blueberry, 10 Basil, 11 Tomato).
/// </summary>
public static class EventCategorizer
{
    private const string ConfigFile = "categories.json";

    public static string? DetectColorId(string message)
    {
        if (!File.Exists(ConfigFile)) return null;

        var categories = JsonSerializer.Deserialize<List<EventCategory>>(File.ReadAllText(ConfigFile));
        if (categories is null) return null;

        foreach (var category in categories)
        {
            foreach (var keyword in category.Keywords)
            {
                if (Regex.IsMatch(message, $@"\b{Regex.Escape(keyword)}\b", RegexOptions.IgnoreCase))
                    return category.ColorId;
            }
        }

        return null;
    }
}
