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
        try
        {
            await _http.PostAsJsonAsync(BuildUrl("sendMessage"), new { chat_id = _chatId, text, parse_mode = "HTML" });
        }
        catch (Exception ex) when (ex is not TelegramApiException)
        {
            // The bot token lives in the request URL (that's how the Telegram Bot API works,
            // not a choice made here) — GitHub Actions masks the literal token value in its
            // own logs, but re-throw with a scrubbed message so it can't leak into any other
            // log sink (local console, future logging, etc.) via an unhandled exception.
            throw new TelegramApiException("sendMessage", ex);
        }
    }

    public async Task<TelegramGetUpdatesResponse?> GetUpdatesAsync(long offset)
    {
        try
        {
            return await _http.GetFromJsonAsync<TelegramGetUpdatesResponse>(BuildUrl($"getUpdates?offset={offset}&timeout=0"));
        }
        catch (Exception ex) when (ex is not TelegramApiException)
        {
            throw new TelegramApiException("getUpdates", ex);
        }
    }

    private string BuildUrl(string method) => $"https://api.telegram.org/bot{_botToken}/{method}";
}

/// <summary>Wraps a Telegram API failure without the bot token embedded in its message.</summary>
public class TelegramApiException(string method, Exception inner)
    : Exception($"Telegram API call to '{method}' failed: {inner.GetType().Name}: {inner.Message}", inner);
