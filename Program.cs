using Microsoft.Extensions.Configuration;

var configuration = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets(System.Reflection.Assembly.GetExecutingAssembly(), optional: true)
    .Build();

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run -- <poll|daily-briefing> [--force]");
    return 1;
}

using var http = new HttpClient();
var calendar = new CalendarService(configuration);
var telegram = new TelegramService(configuration, http);
var force = args.Contains("--force");

return args[0].ToLowerInvariant() switch
{
    "poll" => await TelegramPoller.RunAsync(calendar, telegram),
    "daily-briefing" => await DailyBriefing.RunAsync(calendar, telegram, targetHour: 8, force),
    var unknown => Fail($"Unknown mode '{unknown}'. Expected 'poll' or 'daily-briefing'.")
};

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}
