using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;

public class TelegramService
{
    private readonly HttpClient _http;
    private readonly string _botToken;
    private readonly string _chatId;

    public TelegramService(IConfiguration config, HttpClient http)
    {
        _http = http;
        _botToken = config["Telegram:BotToken"]!;
        _chatId = config["Telegram:ChatId"]!;
    }

    public string BotToken => _botToken;
    public string ChatId => _chatId;

    public async Task SendMessageAsync(string text)
    {
        var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
        await _http.PostAsJsonAsync(url, new { chat_id = _chatId, text, parse_mode = "HTML" });
    }

    public async Task<TelegramGetUpdatesResponse?> GetUpdatesAsync(long offset)
    {
        var url = $"https://api.telegram.org/bot{_botToken}/getUpdates?offset={offset}&timeout=0";
        return await _http.GetFromJsonAsync<TelegramGetUpdatesResponse>(url);
    }
}
