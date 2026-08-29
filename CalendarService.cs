using System.Globalization;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using Microsoft.Extensions.Configuration;

public class CalendarService
{
    private readonly Google.Apis.Calendar.v3.CalendarService _service;
    private readonly string _calendarId;
    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Jerusalem");

    public CalendarService(IConfiguration config)
    {
        // Locally: a service-account key file (path from user-secrets/appsettings).
        // On Cloud Run: no key file — the service runs *as* the service account
        // identity, so Application Default Credentials picks it up automatically.
        // This means the JSON key never needs to exist in the deployed environment.
        var keyPath = config["Google:ServiceAccountKeyPath"];
        var credential = (string.IsNullOrWhiteSpace(keyPath)
                ? GoogleCredential.GetApplicationDefault()
                : GoogleCredential.FromFile(keyPath))
            .CreateScoped(Google.Apis.Calendar.v3.CalendarService.Scope.Calendar);

        _service = new Google.Apis.Calendar.v3.CalendarService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "TelegramCalendarBot"
        });

        _calendarId = config["Google:CalendarId"]!;
    }

    public async Task<Event> CreateEventAsync(string title, DateTime start, DateTime end, string? colorId = null, bool isAllDay = false)
    {
        var newEvent = new Event { Summary = title, ColorId = colorId };

        if (isAllDay)
        {
            // Google's all-day convention: Date-only start/end, with end being the day
            // *after* the last included day (exclusive), which is what EventParser already
            // produces for an all-day match.
            newEvent.Start = new EventDateTime { Date = start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            newEvent.End = new EventDateTime { Date = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        }
        else
        {
            newEvent.Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(start, _tz.GetUtcOffset(start)) };
            newEvent.End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(end, _tz.GetUtcOffset(end)) };
        }

        var request = _service.Events.Insert(newEvent, _calendarId);
        return await request.ExecuteAsync();
    }

    public async Task<IList<Event>> GetTodayEventsAsync()
    {
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, _tz);
        var startOfDay = TimeZoneInfo.ConvertTimeToUtc(nowLocal.Date, _tz);
        var endOfDay = TimeZoneInfo.ConvertTimeToUtc(nowLocal.Date.AddDays(1), _tz);

        var request = _service.Events.List(_calendarId);
        request.TimeMinDateTimeOffset = startOfDay;
        request.TimeMaxDateTimeOffset = endOfDay;
        request.SingleEvents = true;
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;

        var result = await request.ExecuteAsync();
        return result.Items;
    }
}