using System.Text.Json.Serialization;

public record TelegramChat([property: JsonPropertyName("id")] long Id);

public record TelegramMessage(
    [property: JsonPropertyName("chat")] TelegramChat Chat,
    [property: JsonPropertyName("text")] string? Text);

public record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message);

public record TelegramGetUpdatesResponse(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] List<TelegramUpdate> Result);
